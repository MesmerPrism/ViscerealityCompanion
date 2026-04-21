using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class WindowsInstallFootprintCleanupServiceTests
{
    [Fact]
    public void Cleanup_RemovesUnexpectedShortcuts_AndLegacyCliWhenBrandedSiblingExists()
    {
        var deletedPaths = new List<string>();
        var service = new WindowsInstallFootprintCleanupService(
            snapshotProvider: () => new WindowsInstallFootprintSnapshot(
                PackagedInstalls:
                [
                    new WindowsPackagedInstallFootprint(
                        PackagedAppIdentity.ReleasePackageName,
                        "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y",
                        @"C:\Users\operator\AppData\Local\Packages\MesmerPrism.ViscerealityCompanion_zncnfcs118r0y",
                        @"C:\Users\operator\AppData\Local\Packages\MesmerPrism.ViscerealityCompanion_zncnfcs118r0y\LocalCache\Local\ViscerealityCompanion")
                ],
                DesktopShortcutPaths:
                [
                    @"C:\Users\operator\Desktop\Viscereality Companion Legacy.lnk",
                    @"C:\Users\operator\Desktop\Viscereality Companion.lnk"
                ],
                StartMenuShortcutPaths:
                [
                    @"C:\Users\operator\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Viscereality Companion.lnk"
                ],
                BrandedCliExecutablePaths:
                [
                    @"C:\Users\operator\AppData\Local\Packages\MesmerPrism.ViscerealityCompanion_zncnfcs118r0y\LocalCache\Local\ViscerealityCompanion\agent-workspace\cli\current\Viscereality CLI.exe",
                    @"C:\Users\operator\AppData\Local\ViscerealityCompanion\agent-workspace\cli\current\Viscereality CLI.exe"
                ],
                LegacyCliExecutablePaths:
                [
                    @"C:\Users\operator\AppData\Local\ViscerealityCompanion\agent-workspace\cli\current\viscereality.exe"
                ],
                UnpackagedOperatorDataRootPath: @"C:\Users\operator\AppData\Local\ViscerealityCompanion",
                UnpackagedAgentWorkspaceRootPath: @"C:\Users\operator\AppData\Local\ViscerealityCompanion\agent-workspace",
                UnpackagedAgentWorkspaceExists: true),
            deleteFile: deletedPaths.Add,
            deleteDirectory: deletedPaths.Add);

        var result = service.Cleanup();

        Assert.Equal(OperationOutcomeKind.Success, result.Level);
        Assert.Equal(3, deletedPaths.Count);
        Assert.Contains(@"C:\Users\operator\Desktop\Viscereality Companion Legacy.lnk", deletedPaths);
        Assert.Contains(@"C:\Users\operator\AppData\Local\ViscerealityCompanion\agent-workspace\cli\current\viscereality.exe", deletedPaths);
        Assert.Contains(@"C:\Users\operator\AppData\Local\ViscerealityCompanion\agent-workspace", deletedPaths);
        Assert.Contains("Removed shortcuts: 1.", result.Detail, StringComparison.Ordinal);
        Assert.Contains("Removed legacy generic CLI exports: 1.", result.Detail, StringComparison.Ordinal);
        Assert.Contains("Removed stale unpackaged workspaces: 1.", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_Warns_WhenOnlyManualFollowUpRemains()
    {
        var deletedPaths = new List<string>();
        var service = new WindowsInstallFootprintCleanupService(
            snapshotProvider: () => new WindowsInstallFootprintSnapshot(
                PackagedInstalls:
                [
                    new WindowsPackagedInstallFootprint(
                        PackagedAppIdentity.ReleasePackageName,
                        "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y",
                        @"C:\Users\operator\AppData\Local\Packages\MesmerPrism.ViscerealityCompanion_zncnfcs118r0y",
                        @"C:\Users\operator\AppData\Local\Packages\MesmerPrism.ViscerealityCompanion_zncnfcs118r0y\LocalCache\Local\ViscerealityCompanion"),
                    new WindowsPackagedInstallFootprint(
                        PackagedAppIdentity.LegacyPreviewPackageName,
                        "MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y",
                        @"C:\Users\operator\AppData\Local\Packages\MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y",
                        @"C:\Users\operator\AppData\Local\Packages\MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y\LocalCache\Local\ViscerealityCompanion")
                ],
                DesktopShortcutPaths:
                [
                    @"C:\Users\operator\Desktop\Viscereality Companion.lnk"
                ],
                StartMenuShortcutPaths:
                [
                    @"C:\Users\operator\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Viscereality Companion.lnk"
                ],
                BrandedCliExecutablePaths: [],
                LegacyCliExecutablePaths:
                [
                    @"C:\Users\operator\AppData\Local\ViscerealityCompanion\agent-workspace\cli\current\viscereality.exe"
                ],
                UnpackagedOperatorDataRootPath: @"C:\Users\operator\AppData\Local\ViscerealityCompanion",
                UnpackagedAgentWorkspaceRootPath: @"C:\Users\operator\AppData\Local\ViscerealityCompanion\agent-workspace",
                UnpackagedAgentWorkspaceExists: true),
            deleteFile: deletedPaths.Add,
            deleteDirectory: deletedPaths.Add);

        var result = service.Cleanup();

        Assert.Equal(OperationOutcomeKind.Warning, result.Level);
        Assert.Empty(deletedPaths);
        Assert.Contains("legacy preview package family is still installed", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("branded `Viscereality CLI.exe` sibling", result.Detail, StringComparison.Ordinal);
        Assert.Contains("no packaged cli workspace export", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cleanup_Warns_WhenUnpackagedWorkspaceIsLeftInPlaceWithoutPackagedInstall()
    {
        var deletedPaths = new List<string>();
        var service = new WindowsInstallFootprintCleanupService(
            snapshotProvider: () => new WindowsInstallFootprintSnapshot(
                PackagedInstalls: [],
                DesktopShortcutPaths: [],
                StartMenuShortcutPaths: [],
                BrandedCliExecutablePaths: [],
                LegacyCliExecutablePaths: [],
                UnpackagedOperatorDataRootPath: @"C:\Users\operator\AppData\Local\ViscerealityCompanion",
                UnpackagedAgentWorkspaceRootPath: @"C:\Users\operator\AppData\Local\ViscerealityCompanion\agent-workspace",
                UnpackagedAgentWorkspaceExists: true),
            deleteFile: deletedPaths.Add,
            deleteDirectory: deletedPaths.Add);

        var result = service.Cleanup();

        Assert.Equal(OperationOutcomeKind.Warning, result.Level);
        Assert.Empty(deletedPaths);
        Assert.Contains("unpackaged `agent-workspace` mirror was left in place", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cleanup_Warns_WhenPackagedInstallExists_ButNoPackagedCliWorkspaceExportExists()
    {
        var deletedPaths = new List<string>();
        var service = new WindowsInstallFootprintCleanupService(
            snapshotProvider: () => new WindowsInstallFootprintSnapshot(
                PackagedInstalls:
                [
                    new WindowsPackagedInstallFootprint(
                        PackagedAppIdentity.ReleasePackageName,
                        "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y",
                        @"C:\Users\operator\AppData\Local\Packages\MesmerPrism.ViscerealityCompanion_zncnfcs118r0y",
                        @"C:\Users\operator\AppData\Local\Packages\MesmerPrism.ViscerealityCompanion_zncnfcs118r0y\LocalCache\Local\ViscerealityCompanion")
                ],
                DesktopShortcutPaths: [],
                StartMenuShortcutPaths: [],
                BrandedCliExecutablePaths:
                [
                    @"C:\Users\operator\AppData\Local\ViscerealityCompanion\agent-workspace\cli\current\Viscereality CLI.exe"
                ],
                LegacyCliExecutablePaths: [],
                UnpackagedOperatorDataRootPath: @"C:\Users\operator\AppData\Local\ViscerealityCompanion",
                UnpackagedAgentWorkspaceRootPath: @"C:\Users\operator\AppData\Local\ViscerealityCompanion\agent-workspace",
                UnpackagedAgentWorkspaceExists: true),
            deleteFile: deletedPaths.Add,
            deleteDirectory: deletedPaths.Add);

        var result = service.Cleanup();

        Assert.Equal(OperationOutcomeKind.Warning, result.Level);
        Assert.Empty(deletedPaths);
        Assert.Contains("no packaged cli workspace export", result.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
