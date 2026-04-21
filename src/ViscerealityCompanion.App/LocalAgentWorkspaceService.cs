using System.IO;
using System.Text;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App;

internal sealed record LocalAgentWorkspaceSnapshot(
    string RootPath,
    string ReadmePath,
    string LocalPathsPath,
    string PromptPath,
    string PowerShellEnvScriptPath,
    string CmdEnvScriptPath,
    string PowerShellCliWrapperPath,
    string CmdCliWrapperPath,
    string DocsRootPath,
    string BundledCliRootPath,
    string BundledCliEntryPointPath,
    string QuestSessionKitRootPath,
    string StudyShellRootPath,
    string OscillatorConfigRootPath,
    bool HasBundledCli,
    string PromptText);

internal sealed class LocalAgentWorkspaceService
{
    private static readonly string[] BundledDocFiles =
    [
        "cli.md",
        "study-shells.md",
        "monitoring-and-control.md"
    ];

    private readonly object _gate = new();
    private readonly string _workspaceRoot;
    private readonly string? _docsRoot;
    private readonly string? _bundledCliRoot;
    private readonly string? _questSessionKitRoot;
    private readonly string? _studyShellRoot;
    private readonly string? _oscillatorConfigRoot;

    public LocalAgentWorkspaceService(
        string? workspaceRoot = null,
        string? docsRoot = null,
        string? bundledCliRoot = null,
        string? questSessionKitRoot = null,
        string? studyShellRoot = null,
        string? oscillatorConfigRoot = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot ?? LocalAgentWorkspaceLayout.RootPath);
        _docsRoot = docsRoot ?? AppAssetLocator.TryResolveDocsRoot();
        _bundledCliRoot = bundledCliRoot ?? AppAssetLocator.TryResolveBundledCliRoot();
        _questSessionKitRoot = questSessionKitRoot ?? AppAssetLocator.TryResolveQuestSessionKitRoot();
        _studyShellRoot = studyShellRoot ?? AppAssetLocator.TryResolveStudyShellRoot();
        _oscillatorConfigRoot = oscillatorConfigRoot ?? AppAssetLocator.TryResolveOscillatorConfigRoot();
    }

    public string RootPath => _workspaceRoot;

    public LocalAgentWorkspaceSnapshot EnsureWorkspace()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_workspaceRoot);

            MirrorBundledDocs();
            MirrorBundledCli();
            MirrorQuestSessionKit();
            MirrorStudyShells();
            MirrorOscillatorConfig();

            var snapshot = BuildSnapshot();
            WriteTextFile(snapshot.ReadmePath, BuildReadme(snapshot));
            WriteTextFile(snapshot.LocalPathsPath, BuildLocalPaths(snapshot));
            WriteTextFile(snapshot.PowerShellEnvScriptPath, BuildPowerShellEnvScript(snapshot));
            WriteTextFile(snapshot.CmdEnvScriptPath, BuildCmdEnvScript(snapshot));
            WriteTextFile(snapshot.PowerShellCliWrapperPath, BuildPowerShellCliWrapper(snapshot));
            WriteTextFile(snapshot.CmdCliWrapperPath, BuildCmdCliWrapper(snapshot));

            var promptText = BuildPrompt(snapshot);
            WriteTextFile(snapshot.PromptPath, promptText);
            return snapshot with { PromptText = promptText };
        }
    }

    private LocalAgentWorkspaceSnapshot BuildSnapshot()
    {
        var docsRoot = Path.Combine(_workspaceRoot, "docs");
        var bundledCliRoot = Path.Combine(_workspaceRoot, "cli", "current");
        var bundledCliEntryPoint = ResolveBundledCliEntryPointPath(bundledCliRoot);
        return new LocalAgentWorkspaceSnapshot(
            RootPath: _workspaceRoot,
            ReadmePath: Path.Combine(_workspaceRoot, "README.md"),
            LocalPathsPath: Path.Combine(_workspaceRoot, "LOCAL_PATHS.md"),
            PromptPath: Path.Combine(_workspaceRoot, "AGENT_PROMPT.txt"),
            PowerShellEnvScriptPath: Path.Combine(_workspaceRoot, "agent-env.ps1"),
            CmdEnvScriptPath: Path.Combine(_workspaceRoot, "agent-env.cmd"),
            PowerShellCliWrapperPath: Path.Combine(_workspaceRoot, "viscereality.ps1"),
            CmdCliWrapperPath: Path.Combine(_workspaceRoot, "viscereality.cmd"),
            DocsRootPath: docsRoot,
            BundledCliRootPath: bundledCliRoot,
            BundledCliEntryPointPath: bundledCliEntryPoint ?? string.Empty,
            QuestSessionKitRootPath: Path.Combine(_workspaceRoot, "samples", "quest-session-kit"),
            StudyShellRootPath: Path.Combine(_workspaceRoot, "samples", "study-shells"),
            OscillatorConfigRootPath: Path.Combine(_workspaceRoot, "samples", "oscillator-config"),
            HasBundledCli: !string.IsNullOrWhiteSpace(bundledCliEntryPoint),
            PromptText: string.Empty);
    }

    private void MirrorBundledDocs()
    {
        if (string.IsNullOrWhiteSpace(_docsRoot) || !Directory.Exists(_docsRoot))
        {
            return;
        }

        foreach (var fileName in BundledDocFiles)
        {
            MirrorFileIfExists(
                Path.Combine(_docsRoot, fileName),
                Path.Combine(_workspaceRoot, "docs", fileName));
        }
    }

    private void MirrorBundledCli()
    {
        if (string.IsNullOrWhiteSpace(_bundledCliRoot) || !Directory.Exists(_bundledCliRoot))
        {
            return;
        }

        MirrorDirectorySubset(
            _bundledCliRoot,
            Path.Combine(_workspaceRoot, "cli", "current"),
            _ => true);

        var brandedCliPath = Path.Combine(_workspaceRoot, "cli", "current", LocalAgentWorkspaceLayout.BundledCliExecutableFileName);
        var legacyCliPath = Path.Combine(_workspaceRoot, "cli", "current", LocalAgentWorkspaceLayout.LegacyBundledCliExecutableFileName);
        if (!File.Exists(brandedCliPath) && File.Exists(legacyCliPath))
        {
            File.Move(legacyCliPath, brandedCliPath);
        }
    }

    private void MirrorQuestSessionKit()
    {
        if (string.IsNullOrWhiteSpace(_questSessionKitRoot) || !Directory.Exists(_questSessionKitRoot))
        {
            return;
        }

        MirrorDirectorySubset(
            _questSessionKitRoot,
            Path.Combine(_workspaceRoot, "samples", "quest-session-kit"),
            relativePath =>
            {
                var normalized = NormalizeRelativePath(relativePath);
                return string.Equals(normalized, "README.md", StringComparison.OrdinalIgnoreCase)
                       || (normalized.StartsWith("APKs/", StringComparison.OrdinalIgnoreCase)
                           && normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                       || normalized.StartsWith("DeviceProfiles/", StringComparison.OrdinalIgnoreCase)
                       || normalized.StartsWith("HotloadProfiles/", StringComparison.OrdinalIgnoreCase)
                       || normalized.StartsWith("LlmTuningProfiles/", StringComparison.OrdinalIgnoreCase);
            });
    }

    private void MirrorStudyShells()
    {
        if (string.IsNullOrWhiteSpace(_studyShellRoot) || !Directory.Exists(_studyShellRoot))
        {
            return;
        }

        MirrorDirectorySubset(
            _studyShellRoot,
            Path.Combine(_workspaceRoot, "samples", "study-shells"),
            _ => true);
    }

    private void MirrorOscillatorConfig()
    {
        if (string.IsNullOrWhiteSpace(_oscillatorConfigRoot) || !Directory.Exists(_oscillatorConfigRoot))
        {
            return;
        }

        MirrorDirectorySubset(
            _oscillatorConfigRoot,
            Path.Combine(_workspaceRoot, "samples", "oscillator-config"),
            _ => true);
    }

    private static void MirrorDirectorySubset(string sourceRoot, string destinationRoot, Func<string, bool> includeRelativePath)
    {
        foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            if (!includeRelativePath(relativePath))
            {
                continue;
            }

            MirrorFileIfExists(sourcePath, Path.Combine(destinationRoot, relativePath));
        }
    }

    private static void MirrorFileIfExists(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        var sourceInfo = new FileInfo(sourcePath);
        var destinationInfo = new FileInfo(destinationPath);
        if (destinationInfo.Exists &&
            destinationInfo.Length == sourceInfo.Length &&
            destinationInfo.LastWriteTimeUtc == sourceInfo.LastWriteTimeUtc)
        {
            return;
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
        File.SetLastWriteTimeUtc(destinationPath, sourceInfo.LastWriteTimeUtc);
    }

    private static void WriteTextFile(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(path))
        {
            var existingContents = File.ReadAllText(path, Encoding.UTF8);
            if (string.Equals(existingContents, contents, StringComparison.Ordinal))
            {
                return;
            }
        }

        File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private string BuildReadme(LocalAgentWorkspaceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Local Agent Workspace");
        builder.AppendLine();
        builder.AppendLine("Open your local agent in this folder if you installed Viscereality Companion from the guided installer and do not have a repo checkout.");
        builder.AppendLine("This workspace mirrors the bundled CLI, the CLI reference, and the Sussex example catalogs into the same host-visible operator-data root the installed app uses, so the agent does not need access to the protected WindowsApps install.");
        builder.AppendLine();
        builder.AppendLine("## Start Here");
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, Path.Combine(snapshot.DocsRootPath, "cli.md"));
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, Path.Combine(snapshot.DocsRootPath, "study-shells.md"));
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, Path.Combine(snapshot.DocsRootPath, "monitoring-and-control.md"));
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, snapshot.PowerShellCliWrapperPath);
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, snapshot.CmdCliWrapperPath);
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, Path.Combine(snapshot.RootPath, "LOCAL_PATHS.md"));
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, Path.Combine(snapshot.StudyShellRootPath, "sussex-university.json"));
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, Path.Combine(snapshot.QuestSessionKitRootPath, "README.md"));
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, Path.Combine(snapshot.QuestSessionKitRootPath, "DeviceProfiles", "profiles.json"));
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, Path.Combine(snapshot.QuestSessionKitRootPath, "HotloadProfiles", "profiles.json"));
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, Path.Combine(snapshot.OscillatorConfigRootPath, "README.md"));
        AppendRelativeFileIfPresent(builder, snapshot.RootPath, Path.Combine(snapshot.RootPath, "AGENT_PROMPT.txt"));
        builder.AppendLine();
        builder.AppendLine("## CLI Notes");
        if (snapshot.HasBundledCli)
        {
            builder.AppendLine("- This workspace includes the bundled CLI payload under `cli/current`.");
            builder.AppendLine("- Use `.\\viscereality.ps1` from PowerShell or `viscereality.cmd` from `cmd.exe`; both wrappers preload the mirrored sample-root overrides and the bundled liblsl path before invoking the bundled CLI.");
        }
        else
        {
            builder.AppendLine("- The CLI wrappers are present, but the bundled CLI payload could not be mirrored into this workspace on this machine.");
            builder.AppendLine("- If that happens, rely on the mirrored docs and examples here or fetch the standalone CLI zip from the release page.");
        }

        builder.AppendLine("- `agent-env.ps1` and `agent-env.cmd` are available if you want a whole shell session to inherit the same sample-root overrides.");
        builder.AppendLine("- `tooling status` only reports the managed Quest tool cache (`hzdb` and Android platform-tools). Use `windows-env analyze` for liblsl and live-stream diagnostics.");
        builder.AppendLine("- The wrappers also set `VISCEREALITY_OPERATOR_DATA_ROOT` so the bundled CLI keeps using the same host-visible operator-data root as the desktop app; see `LOCAL_PATHS.md`.");
        builder.AppendLine("- This workspace intentionally mirrors docs, manifests, device profiles, hotload profiles, tuning templates, and the bundled CLI without duplicating the bundled Sussex APK.");
        return builder.ToString();
    }

    private string BuildLocalPaths(LocalAgentWorkspaceSnapshot snapshot)
    {
        var localRoot = CompanionOperatorDataLayout.RootPath;

        var builder = new StringBuilder();
        builder.AppendLine("# Local Paths");
        builder.AppendLine();
        builder.AppendLine("## Accessible Agent Workspace");
        builder.AppendLine($"- Agent workspace root: `{snapshot.RootPath}`");
        builder.AppendLine($"- Mirrored docs root: `{snapshot.DocsRootPath}`");
        builder.AppendLine($"- Bundled CLI root: `{snapshot.BundledCliRootPath}`");
        builder.AppendLine($"- Bundled CLI entrypoint: `{snapshot.BundledCliEntryPointPath}`");
        builder.AppendLine($"- Bundled CLI liblsl path: `{LslRuntimeLayout.GetLocalDllPath(snapshot.BundledCliRootPath)}`");
        builder.AppendLine($"- Bundled CLI runtime liblsl path: `{LslRuntimeLayout.GetRuntimeDllPath(snapshot.BundledCliRootPath)}`");
        builder.AppendLine($"- PowerShell CLI wrapper: `{snapshot.PowerShellCliWrapperPath}`");
        builder.AppendLine($"- cmd.exe CLI wrapper: `{snapshot.CmdCliWrapperPath}`");
        builder.AppendLine($"- Mirrored quest-session-kit root: `{snapshot.QuestSessionKitRootPath}`");
        builder.AppendLine($"- Mirrored study-shell root: `{snapshot.StudyShellRootPath}`");
        builder.AppendLine($"- Mirrored oscillator-config root: `{snapshot.OscillatorConfigRootPath}`");
        builder.AppendLine($"- PowerShell env helper: `{snapshot.PowerShellEnvScriptPath}`");
        builder.AppendLine($"- cmd.exe env helper: `{snapshot.CmdEnvScriptPath}`");
        builder.AppendLine();
        builder.AppendLine("## Local Runtime Data");
        builder.AppendLine($"- Operator data root: `{localRoot}`");
        builder.AppendLine($"- Managed tooling root: `{OfficialQuestToolingLayout.RootPath}`");
        builder.AppendLine($"- Managed adb path: `{OfficialQuestToolingLayout.AdbExecutablePath}`");
        builder.AppendLine($"- Managed hzdb path: `{OfficialQuestToolingLayout.HzdbExecutablePath}`");
        builder.AppendLine($"- Session state root: `{CompanionOperatorDataLayout.SessionRootPath}`");
        builder.AppendLine($"- Study data root: `{CompanionOperatorDataLayout.StudyDataRootPath}`");
        builder.AppendLine($"- Diagnostics root: `{CompanionOperatorDataLayout.DiagnosticsRootPath}`");
        builder.AppendLine($"- Sussex visual profiles: `{CompanionOperatorDataLayout.SussexVisualProfilesRootPath}`");
        builder.AppendLine($"- Sussex controller profiles: `{CompanionOperatorDataLayout.SussexControllerBreathingProfilesRootPath}`");
        builder.AppendLine($"- Screenshots: `{CompanionOperatorDataLayout.ScreenshotsRootPath}`");
        builder.AppendLine($"- Logs: `{CompanionOperatorDataLayout.LogsRootPath}`");
        builder.AppendLine($"- Operator-data override env var: `{CompanionOperatorDataLayout.RootOverrideEnvironmentVariable}`");
        builder.AppendLine();
        builder.AppendLine("## Bundled Asset Sources");
        builder.AppendLine($"- Bundled docs source: `{_docsRoot ?? "not found"}`");
        builder.AppendLine($"- Bundled CLI source: `{_bundledCliRoot ?? "not found"}`");
        builder.AppendLine($"- Bundled quest-session-kit source: `{_questSessionKitRoot ?? "not found"}`");
        builder.AppendLine($"- Bundled study-shell source: `{_studyShellRoot ?? "not found"}`");
        builder.AppendLine($"- Bundled oscillator-config source: `{_oscillatorConfigRoot ?? "not found"}`");
        builder.AppendLine();
        builder.AppendLine("## Standalone Alternatives");
        builder.AppendLine("- A separate CLI zip is still published on the release page if you want the command without the desktop app.");
        builder.AppendLine("- The guided-install local-agent workflow no longer depends on that separate CLI zip when the bundled payload is present.");
        builder.AppendLine();
        builder.AppendLine("## Build");
        builder.AppendLine($"- Current app build: `{AppBuildIdentity.Current.Summary}`");
        return builder.ToString();
    }

    private static string BuildPowerShellEnvScript(LocalAgentWorkspaceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"$workspaceRoot = \"{EscapeForPowerShell(snapshot.RootPath)}\"");
        builder.AppendLine($"$env:VISCEREALITY_QUEST_SESSION_KIT_ROOT = \"{EscapeForPowerShell(snapshot.QuestSessionKitRootPath)}\"");
        builder.AppendLine($"$env:VISCEREALITY_STUDY_SHELL_ROOT = \"{EscapeForPowerShell(snapshot.StudyShellRootPath)}\"");
        builder.AppendLine($"$env:VISCEREALITY_OSCILLATOR_CONFIG_ROOT = \"{EscapeForPowerShell(snapshot.OscillatorConfigRootPath)}\"");
        builder.AppendLine($"$env:{CompanionOperatorDataLayout.RootOverrideEnvironmentVariable} = \"{EscapeForPowerShell(CompanionOperatorDataLayout.RootPath)}\"");
        builder.AppendLine($"$bundledCliRoot = \"{EscapeForPowerShell(snapshot.BundledCliRootPath)}\"");
        builder.AppendLine("$bundledLslDll = Join-Path $bundledCliRoot 'lsl.dll'");
        builder.AppendLine("$bundledLslRuntimeDll = Join-Path $bundledCliRoot 'runtimes\\win-x64\\native\\lsl.dll'");
        builder.AppendLine($"$bundledCliExe = Join-Path $bundledCliRoot \"{EscapeForPowerShell(LocalAgentWorkspaceLayout.BundledCliExecutableFileName)}\"");
        builder.AppendLine($"$legacyBundledCliExe = Join-Path $bundledCliRoot \"{EscapeForPowerShell(LocalAgentWorkspaceLayout.LegacyBundledCliExecutableFileName)}\"");
        builder.AppendLine("if (Test-Path $bundledLslDll) {");
        builder.AppendLine("    $env:VISCEREALITY_LSL_DLL = $bundledLslDll");
        builder.AppendLine("} elseif (Test-Path $bundledLslRuntimeDll) {");
        builder.AppendLine("    $env:VISCEREALITY_LSL_DLL = $bundledLslRuntimeDll");
        builder.AppendLine("}");
        builder.AppendLine("if ((Test-Path $bundledCliExe) -or (Test-Path $legacyBundledCliExe)) {");
        builder.AppendLine("    $env:PATH = \"$workspaceRoot;$bundledCliRoot;$env:PATH\"");
        builder.AppendLine("}");
        builder.AppendLine("Write-Host \"Set Viscereality CLI sample-root overrides and bundled liblsl path for this PowerShell session.\"");
        return builder.ToString();
    }

    private static string BuildCmdEnvScript(LocalAgentWorkspaceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine($"set \"WORKSPACE_ROOT={snapshot.RootPath}\"");
        builder.AppendLine($"set \"VISCEREALITY_QUEST_SESSION_KIT_ROOT={snapshot.QuestSessionKitRootPath}\"");
        builder.AppendLine($"set \"VISCEREALITY_STUDY_SHELL_ROOT={snapshot.StudyShellRootPath}\"");
        builder.AppendLine($"set \"VISCEREALITY_OSCILLATOR_CONFIG_ROOT={snapshot.OscillatorConfigRootPath}\"");
        builder.AppendLine($"set \"{CompanionOperatorDataLayout.RootOverrideEnvironmentVariable}={CompanionOperatorDataLayout.RootPath}\"");
        builder.AppendLine($"if exist \"{LslRuntimeLayout.GetLocalDllPath(snapshot.BundledCliRootPath)}\" set \"VISCEREALITY_LSL_DLL={LslRuntimeLayout.GetLocalDllPath(snapshot.BundledCliRootPath)}\"");
        builder.AppendLine($"if not defined VISCEREALITY_LSL_DLL if exist \"{LslRuntimeLayout.GetRuntimeDllPath(snapshot.BundledCliRootPath)}\" set \"VISCEREALITY_LSL_DLL={LslRuntimeLayout.GetRuntimeDllPath(snapshot.BundledCliRootPath)}\"");
        builder.AppendLine($"set \"PATH={snapshot.RootPath};{snapshot.BundledCliRootPath};%PATH%\"");
        builder.AppendLine("echo Set Viscereality CLI sample-root overrides and bundled liblsl path for this cmd.exe session.");
        return builder.ToString();
    }

    private static string BuildPowerShellCliWrapper(LocalAgentWorkspaceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("$env:VISCEREALITY_QUEST_SESSION_KIT_ROOT = \"" + EscapeForPowerShell(snapshot.QuestSessionKitRootPath) + "\"");
        builder.AppendLine("$env:VISCEREALITY_STUDY_SHELL_ROOT = \"" + EscapeForPowerShell(snapshot.StudyShellRootPath) + "\"");
        builder.AppendLine("$env:VISCEREALITY_OSCILLATOR_CONFIG_ROOT = \"" + EscapeForPowerShell(snapshot.OscillatorConfigRootPath) + "\"");
        builder.AppendLine("$env:" + CompanionOperatorDataLayout.RootOverrideEnvironmentVariable + " = \"" + EscapeForPowerShell(CompanionOperatorDataLayout.RootPath) + "\"");
        builder.AppendLine("$bundledCliRoot = Join-Path $PSScriptRoot 'cli\\current'");
        builder.AppendLine("$bundledLslDll = Join-Path $bundledCliRoot 'lsl.dll'");
        builder.AppendLine("$bundledLslRuntimeDll = Join-Path $bundledCliRoot 'runtimes\\win-x64\\native\\lsl.dll'");
        builder.AppendLine("$bundledCliExe = Join-Path $bundledCliRoot \"" + EscapeForPowerShell(LocalAgentWorkspaceLayout.BundledCliExecutableFileName) + "\"");
        builder.AppendLine("$legacyBundledCliExe = Join-Path $bundledCliRoot \"" + EscapeForPowerShell(LocalAgentWorkspaceLayout.LegacyBundledCliExecutableFileName) + "\"");
        builder.AppendLine("$bundledCliDll = Join-Path $bundledCliRoot \"" + EscapeForPowerShell(LocalAgentWorkspaceLayout.BundledCliDllFileName) + "\"");
        builder.AppendLine("if (Test-Path $bundledLslDll) {");
        builder.AppendLine("    $env:VISCEREALITY_LSL_DLL = $bundledLslDll");
        builder.AppendLine("} elseif (Test-Path $bundledLslRuntimeDll) {");
        builder.AppendLine("    $env:VISCEREALITY_LSL_DLL = $bundledLslRuntimeDll");
        builder.AppendLine("}");
        builder.AppendLine("$env:PATH = \"$PSScriptRoot;$bundledCliRoot;$env:PATH\"");
        builder.AppendLine("if (Test-Path $bundledCliExe) {");
        builder.AppendLine("    & $bundledCliExe @Args");
        builder.AppendLine("    exit $LASTEXITCODE");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("if (Test-Path $legacyBundledCliExe) {");
        builder.AppendLine("    & $legacyBundledCliExe @Args");
        builder.AppendLine("    exit $LASTEXITCODE");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("if (Test-Path $bundledCliDll) {");
        builder.AppendLine("    & dotnet $bundledCliDll @Args");
        builder.AppendLine("    exit $LASTEXITCODE");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("Write-Error \"Bundled Viscereality CLI not found under $bundledCliRoot. Open LOCAL_PATHS.md in this workspace for alternatives.\"");
        builder.AppendLine("exit 1");
        return builder.ToString();
    }

    private static string BuildCmdCliWrapper(LocalAgentWorkspaceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("setlocal");
        builder.AppendLine($"set \"VISCEREALITY_QUEST_SESSION_KIT_ROOT={snapshot.QuestSessionKitRootPath}\"");
        builder.AppendLine($"set \"VISCEREALITY_STUDY_SHELL_ROOT={snapshot.StudyShellRootPath}\"");
        builder.AppendLine($"set \"VISCEREALITY_OSCILLATOR_CONFIG_ROOT={snapshot.OscillatorConfigRootPath}\"");
        builder.AppendLine($"set \"{CompanionOperatorDataLayout.RootOverrideEnvironmentVariable}={CompanionOperatorDataLayout.RootPath}\"");
        builder.AppendLine("set \"BUNDLED_CLI_ROOT=%~dp0cli\\current\"");
        builder.AppendLine("if exist \"%BUNDLED_CLI_ROOT%\\lsl.dll\" set \"VISCEREALITY_LSL_DLL=%BUNDLED_CLI_ROOT%\\lsl.dll\"");
        builder.AppendLine("if not defined VISCEREALITY_LSL_DLL if exist \"%BUNDLED_CLI_ROOT%\\runtimes\\win-x64\\native\\lsl.dll\" set \"VISCEREALITY_LSL_DLL=%BUNDLED_CLI_ROOT%\\runtimes\\win-x64\\native\\lsl.dll\"");
        builder.AppendLine("set \"PATH=%~dp0;%BUNDLED_CLI_ROOT%;%PATH%\"");
        builder.AppendLine("if exist \"%BUNDLED_CLI_ROOT%\\" + LocalAgentWorkspaceLayout.BundledCliExecutableFileName + "\" (");
        builder.AppendLine("  \"%BUNDLED_CLI_ROOT%\\" + LocalAgentWorkspaceLayout.BundledCliExecutableFileName + "\" %*");
        builder.AppendLine("  exit /b %ERRORLEVEL%");
        builder.AppendLine(")");
        builder.AppendLine("if exist \"%BUNDLED_CLI_ROOT%\\" + LocalAgentWorkspaceLayout.LegacyBundledCliExecutableFileName + "\" (");
        builder.AppendLine("  \"%BUNDLED_CLI_ROOT%\\" + LocalAgentWorkspaceLayout.LegacyBundledCliExecutableFileName + "\" %*");
        builder.AppendLine("  exit /b %ERRORLEVEL%");
        builder.AppendLine(")");
        builder.AppendLine("if exist \"%BUNDLED_CLI_ROOT%\\" + LocalAgentWorkspaceLayout.BundledCliDllFileName + "\" (");
        builder.AppendLine("  dotnet \"%BUNDLED_CLI_ROOT%\\" + LocalAgentWorkspaceLayout.BundledCliDllFileName + "\" %*");
        builder.AppendLine("  exit /b %ERRORLEVEL%");
        builder.AppendLine(")");
        builder.AppendLine("echo Bundled Viscereality CLI not found under \"%BUNDLED_CLI_ROOT%\". Open LOCAL_PATHS.md in this workspace for alternatives.");
        builder.AppendLine("exit /b 1");
        return builder.ToString();
    }

    private string BuildPrompt(LocalAgentWorkspaceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are looking at a local agent workspace exported from an installed copy of Viscereality Companion.");
        builder.AppendLine("Familiarize yourself with the project and especially the CLI layer, then give me a comprehensive explanation of what can be controlled from the CLI today.");
        builder.AppendLine();
        builder.AppendLine($"Workspace root: {snapshot.RootPath}");
        builder.AppendLine($"Bundled CLI root: {snapshot.BundledCliRootPath}");
        builder.AppendLine();
        builder.AppendLine("Start by reading these files in this workspace:");
        builder.AppendLine("- README.md");
        builder.AppendLine("- LOCAL_PATHS.md");
        builder.AppendLine("- docs/cli.md");
        builder.AppendLine("- docs/study-shells.md");
        builder.AppendLine("- docs/monitoring-and-control.md");
        builder.AppendLine("- samples/study-shells/sussex-university.json");
        builder.AppendLine("- samples/quest-session-kit/README.md");
        builder.AppendLine("- samples/quest-session-kit/DeviceProfiles/profiles.json");
        builder.AppendLine("- samples/quest-session-kit/HotloadProfiles/profiles.json");
        builder.AppendLine("- samples/oscillator-config/README.md");
        builder.AppendLine();
        builder.AppendLine("Then inspect the bundled CLI from this workspace if it is present:");
        builder.AppendLine("- `.\\viscereality.ps1 --help`");
        builder.AppendLine("- `.\\viscereality.ps1 windows-env analyze`");
        builder.AppendLine("- `.\\viscereality.ps1 study --help`");
        builder.AppendLine("- `.\\viscereality.ps1 study probe-connection sussex-university`");
        builder.AppendLine("- `.\\viscereality.ps1 study diagnostics-report sussex-university --wait-seconds 15`");
        builder.AppendLine("- `.\\viscereality.ps1 sussex --help`");
        builder.AppendLine("- `.\\viscereality.ps1 hzdb --help`");
        builder.AppendLine("- `.\\viscereality.ps1 tooling status`");
        builder.AppendLine();
        builder.AppendLine("The wrapper script preloads the mirrored sample-root overrides and the bundled liblsl path before invoking the bundled CLI under `cli/current`.");
        builder.AppendLine("`tooling status` only reports the managed Quest tool cache. Use `windows-env analyze` for liblsl and expected-stream diagnostics, or `study diagnostics-report sussex-university` when you need one shareable LSL/twin report folder.");
        builder.AppendLine("If the wrapper reports that the bundled CLI is unavailable, say that clearly and reason from the mirrored docs and examples instead of assuming repo source is available.");
        builder.AppendLine();
        builder.AppendLine("In your explanation, cover:");
        builder.AppendLine("1. What the CLI can control today, grouped by area.");
        builder.AppendLine("2. Which Sussex tasks are CLI-first versus GUI-first.");
        builder.AppendLine("3. Which local folders matter for tooling, profiles, sessions, examples, and the bundled CLI.");
        builder.AppendLine("4. Which environment variables or `--root` overrides matter.");
        builder.AppendLine("5. A safe first command sequence for this machine.");
        builder.AppendLine("6. The current limits or gaps in the CLI layer.");
        return builder.ToString();
    }

    private static string? ResolveBundledCliEntryPointPath(string bundledCliRoot)
    {
        var bundledCliExe = Path.Combine(bundledCliRoot, LocalAgentWorkspaceLayout.BundledCliExecutableFileName);
        if (File.Exists(bundledCliExe))
        {
            return bundledCliExe;
        }

        var legacyBundledCliExe = Path.Combine(bundledCliRoot, LocalAgentWorkspaceLayout.LegacyBundledCliExecutableFileName);
        if (File.Exists(legacyBundledCliExe))
        {
            return legacyBundledCliExe;
        }

        var bundledCliDll = Path.Combine(bundledCliRoot, LocalAgentWorkspaceLayout.BundledCliDllFileName);
        return File.Exists(bundledCliDll) ? bundledCliDll : null;
    }

    private static void AppendRelativeFileIfPresent(StringBuilder builder, string rootPath, string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var relativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, filePath));
        builder.AppendLine($"- `{relativePath}`");
    }

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace(Path.DirectorySeparatorChar, '/');

    private static string EscapeForPowerShell(string value)
        => value.Replace("`", "``").Replace("\"", "`\"");
}
