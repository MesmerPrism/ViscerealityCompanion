using System.Diagnostics;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class WindowsCompanionExecutionDiagnosticsServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "ViscerealityCompanion.WindowsCompanionExecutionDiagnosticsTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AnalyzeExportedCliAsync_ReturnsSuccess_WhenPackagedWorkspaceRuns()
    {
        var packagedOperatorRoot = CreateDirectory(Path.Combine(
            "Packages",
            "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y",
            "LocalCache",
            "Local",
            "ViscerealityCompanion"));
        var packagedCliPath = CreateFile(
            Path.Combine(packagedOperatorRoot, "agent-workspace", "cli", "current", "Viscereality CLI.exe"),
            "stub");

        var snapshot = new WindowsInstallFootprintSnapshot(
            PackagedInstalls:
            [
                new WindowsPackagedInstallFootprint(
                    PackagedAppIdentity.ReleasePackageName,
                    "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y",
                    Path.Combine(_tempRoot, "Packages", "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y"),
                    packagedOperatorRoot)
            ],
            DesktopShortcutPaths: [],
            StartMenuShortcutPaths: [],
            BrandedCliExecutablePaths: [packagedCliPath],
            LegacyCliExecutablePaths: [],
            UnpackagedOperatorDataRootPath: Path.Combine(_tempRoot, "ViscerealityCompanion"),
            UnpackagedAgentWorkspaceRootPath: Path.Combine(_tempRoot, "ViscerealityCompanion", "agent-workspace"),
            UnpackagedAgentWorkspaceExists: false);

        var service = new WindowsCompanionExecutionDiagnosticsService(
            processRunner: (startInfo, cancellationToken) => Task.FromResult(new ProcessProbeResult(
                0,
                "1.0.0+packagedhash",
                string.Empty)));

        var result = await service.AnalyzeExportedCliAsync(snapshot);

        Assert.Equal(OperationOutcomeKind.Success, result.Level);
        Assert.Contains("runnable", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.0.0+packagedhash", result.Detail, StringComparison.Ordinal);
        Assert.Contains(packagedCliPath, result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeExportedCliAsync_Warns_WhenUnpackagedWorkspaceIsAlsoPresent()
    {
        var packagedOperatorRoot = CreateDirectory(Path.Combine(
            "Packages",
            "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y",
            "LocalCache",
            "Local",
            "ViscerealityCompanion"));
        var packagedWorkspaceRoot = Path.Combine(packagedOperatorRoot, "agent-workspace");
        CreateFile(Path.Combine(packagedWorkspaceRoot, "cli", "current", "Viscereality CLI.exe"), "packaged");

        var unpackagedWorkspaceRoot = CreateDirectory(Path.Combine("ViscerealityCompanion", "agent-workspace"));
        CreateFile(Path.Combine(unpackagedWorkspaceRoot, "cli", "current", "Viscereality CLI.exe"), "unpackaged");

        var snapshot = new WindowsInstallFootprintSnapshot(
            PackagedInstalls:
            [
                new WindowsPackagedInstallFootprint(
                    PackagedAppIdentity.ReleasePackageName,
                    "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y",
                    Path.Combine(_tempRoot, "Packages", "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y"),
                    packagedOperatorRoot)
            ],
            DesktopShortcutPaths: [],
            StartMenuShortcutPaths: [],
            BrandedCliExecutablePaths:
            [
                Path.Combine(packagedWorkspaceRoot, "cli", "current", "Viscereality CLI.exe"),
                Path.Combine(unpackagedWorkspaceRoot, "cli", "current", "Viscereality CLI.exe")
            ],
            LegacyCliExecutablePaths: [],
            UnpackagedOperatorDataRootPath: Path.Combine(_tempRoot, "ViscerealityCompanion"),
            UnpackagedAgentWorkspaceRootPath: unpackagedWorkspaceRoot,
            UnpackagedAgentWorkspaceExists: true);

        var service = new WindowsCompanionExecutionDiagnosticsService(
            processRunner: (startInfo, cancellationToken) =>
            {
                var version = string.Equals(
                    Path.TrimEndingDirectorySeparator(startInfo.WorkingDirectory ?? string.Empty),
                    Path.TrimEndingDirectorySeparator(packagedWorkspaceRoot),
                    StringComparison.OrdinalIgnoreCase)
                    ? "1.0.0+newhash"
                    : "1.0.0+oldhash";
                return Task.FromResult(new ProcessProbeResult(0, version, string.Empty));
            });

        var result = await service.AnalyzeExportedCliAsync(snapshot);

        Assert.Equal(OperationOutcomeKind.Warning, result.Level);
        Assert.Contains("older unpackaged workspace", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.0.0+newhash", result.Detail, StringComparison.Ordinal);
        Assert.Contains("1.0.0+oldhash", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeRepoTestAssemblyLoadAsync_ReturnsSuccess_WhenDotNetTestCanEnumerate()
    {
        var repoRoot = CreateDirectory("repo");
        CreateFile(Path.Combine(repoRoot, "ViscerealityCompanion.sln"), "solution");
        var coreProjectRoot = CreateDirectory(Path.Combine("repo", "tests", "ViscerealityCompanion.Core.Tests"));
        CreateFile(Path.Combine(coreProjectRoot, "ViscerealityCompanion.Core.Tests.csproj"), "<Project />");
        var coreAssemblyPath = CreateFile(Path.Combine(coreProjectRoot, "bin", "Debug", "net10.0-windows", "ViscerealityCompanion.Core.Tests.dll"), "dll");

        var service = new WindowsCompanionExecutionDiagnosticsService(
            repoRootProvider: () => repoRoot,
            processRunner: (startInfo, cancellationToken) => Task.FromResult(new ProcessProbeResult(
                0,
                "The following Tests are available:",
                string.Empty)));

        var result = await service.AnalyzeRepoTestAssemblyLoadAsync();

        Assert.Equal(OperationOutcomeKind.Success, result.Level);
        Assert.Contains("enumerate successfully", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(coreAssemblyPath, result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeRepoTestAssemblyLoadAsync_Warns_WhenPolicyBlocksRepoProbe()
    {
        var repoRoot = CreateDirectory("repo-blocked");
        CreateFile(Path.Combine(repoRoot, "ViscerealityCompanion.sln"), "solution");
        var coreProjectRoot = CreateDirectory(Path.Combine("repo-blocked", "tests", "ViscerealityCompanion.Core.Tests"));
        CreateFile(Path.Combine(coreProjectRoot, "ViscerealityCompanion.Core.Tests.csproj"), "<Project />");
        CreateFile(Path.Combine(coreProjectRoot, "bin", "Debug", "net10.0-windows", "ViscerealityCompanion.Core.Tests.dll"), "dll");

        var service = new WindowsCompanionExecutionDiagnosticsService(
            repoRootProvider: () => repoRoot,
            processRunner: (startInfo, cancellationToken) => Task.FromResult(new ProcessProbeResult(
                1,
                string.Empty,
                "System.IO.FileLoadException: Could not load file or assembly 'ViscerealityCompanion.Core.Tests'. HRESULT: 0x800711C7")));

        var result = await service.AnalyzeRepoTestAssemblyLoadAsync();

        Assert.Equal(OperationOutcomeKind.Warning, result.Level);
        Assert.Contains("policy", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invoke-Signed-DotNetTest.ps1", result.Detail, StringComparison.Ordinal);
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

    private string CreateFile(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
        return path;
    }
}
