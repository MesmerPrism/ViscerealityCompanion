using ViscerealityCompanion.App;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class PackagedTaskbarShortcutRepairServiceTests
{
    [Fact]
    public void IsPreviewPackageFamilyName_matches_only_rotated_preview_family()
    {
        Assert.True(PackagedTaskbarShortcutRepairService.IsPreviewPackageFamilyName(
            "MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y"));
        Assert.False(PackagedTaskbarShortcutRepairService.IsPreviewPackageFamilyName(
            "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y"));
        Assert.False(PackagedTaskbarShortcutRepairService.IsPreviewPackageFamilyName(null));
    }

    [Fact]
    public void BuildAppsFolderArguments_targets_preview_package_family()
    {
        var arguments = PackagedTaskbarShortcutRepairService.BuildAppsFolderArguments(
            "MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y");

        Assert.Equal(
            @"shell:AppsFolder\MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y!App",
            arguments);
    }

    [Fact]
    public void ShouldRepairShortcut_matches_legacy_packaged_taskbar_pin()
    {
        var shouldRepair = PackagedTaskbarShortcutRepairService.ShouldRepairShortcut(
            "Viscereality Companion.lnk",
            targetPath: @"C:\Windows\explorer.exe",
            arguments: @"shell:AppsFolder\MesmerPrism.ViscerealityCompanion_zncnfcs118r0y!App");

        Assert.True(shouldRepair);
    }

    [Fact]
    public void ShouldRepairShortcut_ignores_repo_local_launcher_pin()
    {
        var shouldRepair = PackagedTaskbarShortcutRepairService.ShouldRepairShortcut(
            "Viscereality Companion.lnk",
            targetPath: @"C:\Windows\System32\wscript.exe",
            arguments: @"//B //nologo ""C:\Users\tillh\source\repos\ViscerealityCompanion\tools\app\Start-Desktop-App.vbs""");

        Assert.False(shouldRepair);
    }

    [Fact]
    public void ShouldRepairShortcut_ignores_preview_package_pin()
    {
        var shouldRepair = PackagedTaskbarShortcutRepairService.ShouldRepairShortcut(
            "Viscereality Companion Preview.lnk",
            targetPath: @"C:\Windows\explorer.exe",
            arguments: @"shell:AppsFolder\MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y!App");

        Assert.False(shouldRepair);
    }
}
