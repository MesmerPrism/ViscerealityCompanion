using ViscerealityCompanion.App;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class LocalAgentWorkspaceServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "ViscerealityCompanion.LocalAgentWorkspaceTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnsureWorkspace_MirrorsCuratedFilesAndGeneratesPrompt()
    {
        var docsRoot = CreateDirectory("docs");
        var cliRoot = CreateDirectory(Path.Combine("cli", "current"));
        var questRoot = CreateDirectory(Path.Combine("samples", "quest-session-kit"));
        var studyRoot = CreateDirectory(Path.Combine("samples", "study-shells"));
        var oscillatorRoot = CreateDirectory(Path.Combine("samples", "oscillator-config"));
        var workspaceRoot = Path.Combine(_tempRoot, "agent-workspace");

        WriteFile(Path.Combine(docsRoot, "cli.md"), "# CLI");
        WriteFile(Path.Combine(docsRoot, "study-shells.md"), "# Study Shells");
        WriteFile(Path.Combine(docsRoot, "monitoring-and-control.md"), "# Monitoring");
        WriteFile(Path.Combine(cliRoot, "viscereality.exe"), "stub-cli");
        WriteFile(Path.Combine(cliRoot, "cli.runtimeconfig.json"), "{}");
        WriteFile(Path.Combine(cliRoot, "lsl.dll"), "stub-lsl");

        WriteFile(Path.Combine(questRoot, "README.md"), "# Session Kit");
        WriteFile(Path.Combine(questRoot, "APKs", "library.json"), "{}");
        WriteFile(Path.Combine(questRoot, "APKs", "compatibility.json"), "{}");
        WriteFile(Path.Combine(questRoot, "APKs", "SussexExperiment.apk"), "apk-bytes");
        WriteFile(Path.Combine(questRoot, "DeviceProfiles", "profiles.json"), "{}");
        WriteFile(Path.Combine(questRoot, "HotloadProfiles", "profiles.json"), "{}");
        WriteFile(Path.Combine(questRoot, "HotloadProfiles", "baseline.csv"), "key,value");
        WriteFile(Path.Combine(questRoot, "LlmTuningProfiles", "sussex-controller-breathing-tuning-v1.template.json"), "{}");

        WriteFile(Path.Combine(studyRoot, "manifest.json"), "{}");
        WriteFile(Path.Combine(studyRoot, "sussex-university.json"), "{}");
        WriteFile(Path.Combine(studyRoot, "sussex-university", "visual-profiles", "baseline.json"), "{}");

        WriteFile(Path.Combine(oscillatorRoot, "README.md"), "# Oscillator");
        WriteFile(Path.Combine(oscillatorRoot, "profiles.json"), "{}");
        WriteFile(Path.Combine(oscillatorRoot, "twin-coherence.json"), "{}");
        WriteFile(Path.Combine(oscillatorRoot, "llm-tuning", "sussex-visual-tuning-v1.template.json"), "{}");

        var service = new LocalAgentWorkspaceService(
            workspaceRoot: workspaceRoot,
            docsRoot: docsRoot,
            bundledCliRoot: cliRoot,
            questSessionKitRoot: questRoot,
            studyShellRoot: studyRoot,
            oscillatorConfigRoot: oscillatorRoot);

        var snapshot = service.EnsureWorkspace();

        Assert.Equal(workspaceRoot, snapshot.RootPath);
        Assert.True(File.Exists(snapshot.ReadmePath));
        Assert.True(File.Exists(snapshot.LocalPathsPath));
        Assert.True(File.Exists(snapshot.PromptPath));
        Assert.True(File.Exists(snapshot.PowerShellEnvScriptPath));
        Assert.True(File.Exists(snapshot.CmdEnvScriptPath));
        Assert.True(File.Exists(snapshot.PowerShellCliWrapperPath));
        Assert.True(File.Exists(snapshot.CmdCliWrapperPath));
        Assert.True(snapshot.HasBundledCli);
        Assert.True(File.Exists(Path.Combine(workspaceRoot, "docs", "cli.md")));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, "cli", "current", "viscereality.exe")));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, "cli", "current", "cli.runtimeconfig.json")));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, "cli", "current", "lsl.dll")));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, "samples", "quest-session-kit", "HotloadProfiles", "baseline.csv")));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, "samples", "study-shells", "sussex-university", "visual-profiles", "baseline.json")));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, "samples", "oscillator-config", "llm-tuning", "sussex-visual-tuning-v1.template.json")));
        Assert.False(File.Exists(Path.Combine(workspaceRoot, "samples", "quest-session-kit", "APKs", "SussexExperiment.apk")));

        var promptText = File.ReadAllText(snapshot.PromptPath);
        Assert.Contains("comprehensive explanation of what can be controlled from the CLI today", promptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(workspaceRoot, promptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".\\viscereality.ps1 --help", promptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".\\viscereality.ps1 windows-env analyze", promptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".\\viscereality.ps1 study probe-connection sussex-university", promptText, StringComparison.OrdinalIgnoreCase);

        var envScript = File.ReadAllText(snapshot.PowerShellEnvScriptPath);
        Assert.Contains("VISCEREALITY_QUEST_SESSION_KIT_ROOT", envScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(workspaceRoot, "samples", "quest-session-kit"), envScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(workspaceRoot, "cli", "current"), envScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VISCEREALITY_LSL_DLL", envScript, StringComparison.OrdinalIgnoreCase);

        var wrapperScript = File.ReadAllText(snapshot.PowerShellCliWrapperPath);
        Assert.Contains("viscereality.exe", wrapperScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VISCEREALITY_STUDY_SHELL_ROOT", wrapperScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VISCEREALITY_LSL_DLL", wrapperScript, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_tempRoot, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
    }
}
