using System.Diagnostics;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

internal sealed record ProcessProbeResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    Exception? StartException = null)
{
    public string CombinedOutput
        => string.Join(
            Environment.NewLine,
            new[] { StandardOutput, StandardError }
                .Where(static value => !string.IsNullOrWhiteSpace(value)))
            .Trim();
}

internal sealed record WorkspaceCliProbeResult(
    string WorkspaceRootPath,
    bool WorkspaceExists,
    string CliRootPath,
    string? EntryPath,
    bool SmokeTestPassed,
    string Summary,
    string Detail,
    string? VersionText);

internal sealed record RepoTestProjectProbe(
    string Label,
    string ProjectPath,
    string AssemblyPath,
    string Configuration);

internal sealed class WindowsCompanionExecutionDiagnosticsService
{
    private const string RepoDirectoryName = "ViscerealityCompanion";

    private readonly Func<string?> _repoRootProvider;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<ProcessProbeResult>> _processRunner;

    public WindowsCompanionExecutionDiagnosticsService(
        Func<string?>? repoRootProvider = null,
        Func<ProcessStartInfo, CancellationToken, Task<ProcessProbeResult>>? processRunner = null)
    {
        _repoRootProvider = repoRootProvider ?? TryLocateRepoRoot;
        _processRunner = processRunner ?? RunProcessAsync;
    }

    public async Task<WindowsEnvironmentCheckResult> AnalyzeExportedCliAsync(
        WindowsInstallFootprintSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!OperatingSystem.IsWindows())
        {
            return new WindowsEnvironmentCheckResult(
                "packaged-cli-export",
                "Exported packaged CLI workspace",
                OperationOutcomeKind.Preview,
                "Packaged CLI workspace diagnostics are Windows-only.",
                "The local-agent workspace mirror and its CLI export only apply on Windows.");
        }

        var packagedInstall = SelectPreferredPackagedInstall(snapshot);
        var packagedWorkspaceRoot = string.IsNullOrWhiteSpace(packagedInstall?.OperatorDataRootPath)
            ? string.Empty
            : Path.Combine(packagedInstall.OperatorDataRootPath, "agent-workspace");

        var packagedProbe = string.IsNullOrWhiteSpace(packagedWorkspaceRoot)
            ? null
            : await ProbeWorkspaceCliAsync(packagedWorkspaceRoot, cancellationToken).ConfigureAwait(false);
        var unpackagedProbe = snapshot.UnpackagedAgentWorkspaceExists
            ? await ProbeWorkspaceCliAsync(snapshot.UnpackagedAgentWorkspaceRootPath, cancellationToken).ConfigureAwait(false)
            : null;

        var detailLines = new List<string>();
        if (packagedInstall is null)
        {
            detailLines.Add("Current packaged workspace: none.");
        }
        else
        {
            detailLines.Add($"Current packaged family: {packagedInstall.FamilyName}.");
            detailLines.Add($"Current packaged workspace: {packagedWorkspaceRoot}.");
        }

        detailLines.Add(packagedProbe is null
            ? "Current packaged CLI smoke test: not run."
            : $"Current packaged CLI: {FormatWorkspaceProbe(packagedProbe)}");

        detailLines.Add(snapshot.UnpackagedAgentWorkspaceExists
            ? $"Unpackaged workspace: {snapshot.UnpackagedAgentWorkspaceRootPath}."
            : $"Unpackaged workspace: none under {snapshot.UnpackagedAgentWorkspaceRootPath}.");

        if (unpackagedProbe is not null)
        {
            detailLines.Add($"Unpackaged CLI: {FormatWorkspaceProbe(unpackagedProbe)}");
        }

        if (packagedInstall is null)
        {
            detailLines.Add("Fix: install or launch the packaged app before expecting a packaged local-agent workspace export.");
            return new WindowsEnvironmentCheckResult(
                "packaged-cli-export",
                "Exported packaged CLI workspace",
                OperationOutcomeKind.Preview,
                "No packaged local-agent workspace was detected.",
                string.Join(Environment.NewLine, detailLines));
        }

        if (packagedProbe is null || !packagedProbe.WorkspaceExists || string.IsNullOrWhiteSpace(packagedProbe.EntryPath))
        {
            detailLines.Add("Fix: open the packaged app once so it can recreate the local-agent workspace mirror under the packaged operator-data root.");
            return new WindowsEnvironmentCheckResult(
                "packaged-cli-export",
                "Exported packaged CLI workspace",
                OperationOutcomeKind.Warning,
                "The packaged local-agent workspace is missing its CLI export.",
                string.Join(Environment.NewLine, detailLines));
        }

        if (!packagedProbe.SmokeTestPassed)
        {
            detailLines.Add("Fix: refresh the packaged workspace mirror from the installed app and verify that the branded bundled CLI still starts with `--version`.");
            return new WindowsEnvironmentCheckResult(
                "packaged-cli-export",
                "Exported packaged CLI workspace",
                OperationOutcomeKind.Warning,
                "The packaged CLI export failed its smoke test.",
                string.Join(Environment.NewLine, detailLines));
        }

        if (unpackagedProbe is not null)
        {
            var versionsDiffer = !string.IsNullOrWhiteSpace(packagedProbe.VersionText) &&
                                 !string.IsNullOrWhiteSpace(unpackagedProbe.VersionText) &&
                                 !string.Equals(packagedProbe.VersionText, unpackagedProbe.VersionText, StringComparison.OrdinalIgnoreCase);
            var summary = versionsDiffer
                ? "The packaged CLI export is runnable, but an older unpackaged workspace is still present."
                : "The packaged CLI export is runnable, but an extra unpackaged workspace is still present.";
            detailLines.Add("Fix: use `Clean Install Footprint` to retire the unpackaged `agent-workspace` mirror if you no longer need that older local export.");
            return new WindowsEnvironmentCheckResult(
                "packaged-cli-export",
                "Exported packaged CLI workspace",
                OperationOutcomeKind.Warning,
                summary,
                string.Join(Environment.NewLine, detailLines));
        }

        return new WindowsEnvironmentCheckResult(
            "packaged-cli-export",
            "Exported packaged CLI workspace",
            OperationOutcomeKind.Success,
            "The packaged CLI export is runnable and no extra unpackaged workspace was found.",
            string.Join(Environment.NewLine, detailLines));
    }

    public async Task<WindowsEnvironmentCheckResult> AnalyzeRepoTestAssemblyLoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WindowsEnvironmentCheckResult(
                "repo-test-assembly-load",
                "Repo test assembly load path",
                OperationOutcomeKind.Preview,
                "Repo test assembly diagnostics are Windows-only.",
                "This maintainer-only probe checks whether `dotnet test --no-build --list-tests` can enumerate built repo tests on Windows.");
        }

        var repoRoot = _repoRootProvider();
        if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
        {
            return new WindowsEnvironmentCheckResult(
                "repo-test-assembly-load",
                "Repo test assembly load path",
                OperationOutcomeKind.Preview,
                "No local repo checkout was detected for the test-assembly probe.",
                "This check only runs when a local ViscerealityCompanion source checkout with built tests is available.");
        }

        var probes = DiscoverRepoTestProjects(repoRoot);
        if (probes.Count == 0)
        {
            return new WindowsEnvironmentCheckResult(
                "repo-test-assembly-load",
                "Repo test assembly load path",
                OperationOutcomeKind.Preview,
                "No built repo test projects were detected.",
                $"Repo root: {repoRoot}.{Environment.NewLine}Build the repo tests first if you want the environment report to probe the maintainer-only `dotnet test --no-build --list-tests` path.");
        }

        var detailLines = new List<string> { $"Repo root: {repoRoot}." };
        var failures = new List<(RepoTestProjectProbe Probe, ProcessProbeResult Result)>();
        foreach (var probe in probes)
        {
            var result = await _processRunner(CreateRepoTestStartInfo(repoRoot, probe), cancellationToken).ConfigureAwait(false);
            if (result.StartException is null && result.ExitCode == 0)
            {
                detailLines.Add($"{probe.Label}: OK via `dotnet test --no-build --list-tests` ({probe.Configuration}) against {probe.AssemblyPath}.");
            }
            else
            {
                failures.Add((probe, result));
                detailLines.Add($"{probe.Label}: FAIL exit {(result.StartException is null ? result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "start")} while probing {probe.AssemblyPath}. {SummarizeFailure(result)}");
            }
        }

        if (failures.Count == 0)
        {
            return new WindowsEnvironmentCheckResult(
                "repo-test-assembly-load",
                "Repo test assembly load path",
                OperationOutcomeKind.Success,
                "The built repo test assemblies enumerate successfully.",
                string.Join(Environment.NewLine, detailLines));
        }

        var isLikelyPolicyBlock = failures.Any(static failure => IsLikelyPolicyOrLoadBlock(failure.Result.CombinedOutput));
        detailLines.Add(isLikelyPolicyBlock
            ? "Fix: this is a maintainer-only repo path. The packaged consumer diagnostics can still be healthy. Use `powershell -ExecutionPolicy Bypass -File .\\tools\\app\\Invoke-Signed-DotNetTest.ps1` when Windows policy blocks direct repo test loads."
            : "Fix: rebuild the repo tests and inspect the failing `dotnet test --no-build --list-tests` output before treating this as a consumer install problem.");
        return new WindowsEnvironmentCheckResult(
            "repo-test-assembly-load",
            "Repo test assembly load path",
            OperationOutcomeKind.Warning,
            isLikelyPolicyBlock
                ? "Windows policy is blocking the maintainer-only repo test assembly load path."
                : "The maintainer-only repo test assembly probe failed before test execution.",
            string.Join(Environment.NewLine, detailLines));
    }

    private static WindowsPackagedInstallFootprint? SelectPreferredPackagedInstall(WindowsInstallFootprintSnapshot snapshot)
        => snapshot.PackagedInstalls
            .Where(static install => !string.IsNullOrWhiteSpace(install.OperatorDataRootPath))
            .OrderBy(static install => GetPackagedInstallPriority(install.PackageName))
            .ThenBy(static install => install.FamilyName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static int GetPackagedInstallPriority(string packageName)
        => PackagedAppIdentity.IsReleasePackageName(packageName)
            ? 0
            : PackagedAppIdentity.IsDevPackageName(packageName)
                ? 1
                : PackagedAppIdentity.IsLegacyPreviewPackageName(packageName)
                    ? 2
                    : 3;

    private static string FormatWorkspaceProbe(WorkspaceCliProbeResult probe)
    {
        var versionText = string.IsNullOrWhiteSpace(probe.VersionText) ? "version n/a" : probe.VersionText;
        return $"{probe.Summary} Entry {(string.IsNullOrWhiteSpace(probe.EntryPath) ? "n/a" : probe.EntryPath)}. {versionText}. {probe.Detail}";
    }

    private async Task<WorkspaceCliProbeResult> ProbeWorkspaceCliAsync(
        string workspaceRootPath,
        CancellationToken cancellationToken)
    {
        var cliRootPath = Path.Combine(workspaceRootPath, "cli", "current");
        if (!Directory.Exists(workspaceRootPath))
        {
            return new WorkspaceCliProbeResult(
                workspaceRootPath,
                WorkspaceExists: false,
                cliRootPath,
                EntryPath: null,
                SmokeTestPassed: false,
                "Workspace not present.",
                "The agent-workspace root does not exist.",
                VersionText: null);
        }

        var entryPath = ResolveCliEntryPath(cliRootPath);
        if (string.IsNullOrWhiteSpace(entryPath))
        {
            return new WorkspaceCliProbeResult(
                workspaceRootPath,
                WorkspaceExists: true,
                cliRootPath,
                EntryPath: null,
                SmokeTestPassed: false,
                "CLI entry not present.",
                "No branded exe, legacy exe, or bundled CLI dll was found under cli/current.",
                VersionText: null);
        }

        var result = await _processRunner(CreateCliVersionStartInfo(workspaceRootPath, entryPath), cancellationToken).ConfigureAwait(false);
        if (result.StartException is not null)
        {
            return new WorkspaceCliProbeResult(
                workspaceRootPath,
                WorkspaceExists: true,
                cliRootPath,
                entryPath,
                SmokeTestPassed: false,
                "CLI smoke test failed.",
                $"The process could not start: {result.StartException.Message}",
                VersionText: null);
        }

        var versionText = ExtractFirstMeaningfulLine(result.CombinedOutput);
        if (result.ExitCode == 0)
        {
            return new WorkspaceCliProbeResult(
                workspaceRootPath,
                WorkspaceExists: true,
                cliRootPath,
                entryPath,
                SmokeTestPassed: true,
                "CLI smoke test passed.",
                "The exported CLI responded to `--version`.",
                versionText);
        }

        return new WorkspaceCliProbeResult(
            workspaceRootPath,
            WorkspaceExists: true,
            cliRootPath,
            entryPath,
            SmokeTestPassed: false,
            "CLI smoke test failed.",
            $"Exit {result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}. {SummarizeFailure(result)}",
            versionText);
    }

    private static string? ResolveCliEntryPath(string cliRootPath)
    {
        if (string.IsNullOrWhiteSpace(cliRootPath) || !Directory.Exists(cliRootPath))
        {
            return null;
        }

        var brandedCliPath = Path.Combine(cliRootPath, LocalAgentWorkspaceLayout.BundledCliExecutableFileName);
        if (File.Exists(brandedCliPath))
        {
            return brandedCliPath;
        }

        var legacyCliPath = Path.Combine(cliRootPath, LocalAgentWorkspaceLayout.LegacyBundledCliExecutableFileName);
        if (File.Exists(legacyCliPath))
        {
            return legacyCliPath;
        }

        var dllPath = Path.Combine(cliRootPath, LocalAgentWorkspaceLayout.BundledCliDllFileName);
        return File.Exists(dllPath) ? dllPath : null;
    }

    private static ProcessStartInfo CreateCliVersionStartInfo(string workspaceRootPath, string entryPath)
    {
        var usesDotNetHost = string.Equals(Path.GetExtension(entryPath), ".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo(usesDotNetHost ? "dotnet" : entryPath)
        {
            WorkingDirectory = workspaceRootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (usesDotNetHost)
        {
            startInfo.ArgumentList.Add(entryPath);
        }

        startInfo.ArgumentList.Add("--version");
        return startInfo;
    }

    private static IReadOnlyList<RepoTestProjectProbe> DiscoverRepoTestProjects(string repoRootPath)
    {
        var testsRoot = Path.Combine(repoRootPath, "tests");
        if (!Directory.Exists(testsRoot))
        {
            return [];
        }

        var definitions = new[]
        {
            (Label: "Core tests", ProjectRelativePath: Path.Combine("tests", "ViscerealityCompanion.Core.Tests", "ViscerealityCompanion.Core.Tests.csproj"), AssemblyFileName: "ViscerealityCompanion.Core.Tests.dll"),
            (Label: "Integration tests", ProjectRelativePath: Path.Combine("tests", "ViscerealityCompanion.Integration.Tests", "ViscerealityCompanion.Integration.Tests.csproj"), AssemblyFileName: "ViscerealityCompanion.Integration.Tests.dll")
        };

        var probes = new List<RepoTestProjectProbe>();
        foreach (var definition in definitions)
        {
            var projectPath = Path.Combine(repoRootPath, definition.ProjectRelativePath);
            if (!File.Exists(projectPath))
            {
                continue;
            }

            var projectDirectory = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                continue;
            }

            if (!TryFindBuiltAssembly(projectDirectory, definition.AssemblyFileName, out var assemblyPath, out var configuration))
            {
                continue;
            }

            probes.Add(new RepoTestProjectProbe(definition.Label, projectPath, assemblyPath, configuration));
        }

        return probes;
    }

    private static bool TryFindBuiltAssembly(
        string projectDirectory,
        string assemblyFileName,
        out string assemblyPath,
        out string configuration)
    {
        foreach (var candidateConfiguration in new[] { "Debug", "Release" })
        {
            var configurationRoot = Path.Combine(projectDirectory, "bin", candidateConfiguration);
            if (!Directory.Exists(configurationRoot))
            {
                continue;
            }

            try
            {
                var candidatePath = Directory.EnumerateFiles(configurationRoot, assemblyFileName, SearchOption.AllDirectories)
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(candidatePath))
                {
                    assemblyPath = candidatePath;
                    configuration = candidateConfiguration;
                    return true;
                }
            }
            catch
            {
                // Best effort only.
            }
        }

        assemblyPath = string.Empty;
        configuration = string.Empty;
        return false;
    }

    private static ProcessStartInfo CreateRepoTestStartInfo(string repoRootPath, RepoTestProjectProbe probe)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(probe.ProjectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(probe.Configuration);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--list-tests");
        return startInfo;
    }

    private static bool IsLikelyPolicyOrLoadBlock(string? text)
        => !string.IsNullOrWhiteSpace(text) &&
           (text.Contains("0x800711C7", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("potentially unwanted software", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("code integrity", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("application control", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("could not load file or assembly", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("fileloadexception", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("blocked", StringComparison.OrdinalIgnoreCase));

    private static string SummarizeFailure(ProcessProbeResult result)
    {
        if (result.StartException is not null)
        {
            return result.StartException.Message;
        }

        var firstLine = ExtractFirstMeaningfulLine(result.CombinedOutput);
        return string.IsNullOrWhiteSpace(firstLine)
            ? "No diagnostic output was returned."
            : firstLine;
    }

    private static string ExtractFirstMeaningfulLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line))
            ?? string.Empty;
    }

    private static string? TryLocateRepoRoot()
    {
        foreach (var seedPath in EnumerateRepoRootSeedPaths())
        {
            var resolved = TryFindRepoRootFromSeed(seedPath);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateRepoRootSeedPaths()
    {
        yield return Environment.CurrentDirectory;
        yield return AppContext.BaseDirectory;

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var processDirectory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(processDirectory))
            {
                yield return processDirectory;
            }
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return Path.Combine(userProfile, "source", "repos", RepoDirectoryName);
        }
    }

    private static string? TryFindRepoRootFromSeed(string? seedPath)
    {
        if (string.IsNullOrWhiteSpace(seedPath))
        {
            return null;
        }

        DirectoryInfo? directory;
        try
        {
            directory = new DirectoryInfo(Path.GetFullPath(seedPath));
        }
        catch
        {
            return null;
        }

        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "ViscerealityCompanion.sln");
            var testsPath = Path.Combine(directory.FullName, "tests");
            if (File.Exists(solutionPath) && Directory.Exists(testsPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static async Task<ProcessProbeResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessProbeResult(-1, string.Empty, "Process could not be started.");
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                throw;
            }

            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            return new ProcessProbeResult(process.ExitCode, standardOutput, standardError);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ProcessProbeResult(-1, string.Empty, string.Empty, exception);
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort only. The caller reports the timeout/cancellation separately.
        }
    }
}
