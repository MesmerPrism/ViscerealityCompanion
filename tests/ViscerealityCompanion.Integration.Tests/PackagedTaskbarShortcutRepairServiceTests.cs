using ViscerealityCompanion.App;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class PackagedTaskbarShortcutRepairServiceTests
{
    [Fact]
    public void IsCurrentReleasePackageFamilyName_matches_only_public_release_family()
    {
        Assert.True(PackagedTaskbarShortcutRepairService.IsCurrentReleasePackageFamilyName(
            "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y"));
        Assert.False(PackagedTaskbarShortcutRepairService.IsCurrentReleasePackageFamilyName(
            "MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y"));
        Assert.False(PackagedTaskbarShortcutRepairService.IsCurrentReleasePackageFamilyName(null));
    }

    [Fact]
    public void BuildAppsFolderArguments_targets_public_release_package_family()
    {
        var arguments = PackagedTaskbarShortcutRepairService.BuildAppsFolderArguments(
            "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y");

        Assert.Equal(
            @"shell:AppsFolder\MesmerPrism.ViscerealityCompanion_zncnfcs118r0y!App",
            arguments);
    }

    [Fact]
    public void ShouldRepairShortcut_matches_preview_family_taskbar_pin()
    {
        var shouldRepair = PackagedTaskbarShortcutRepairService.ShouldRepairShortcut(
            "Viscereality Companion.lnk",
            targetPath: @"C:\Windows\explorer.exe",
            arguments: @"shell:AppsFolder\MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y!App");

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
    public void ShouldRepairShortcut_ignores_current_public_release_pin()
    {
        var shouldRepair = PackagedTaskbarShortcutRepairService.ShouldRepairShortcut(
            "Viscereality Companion.lnk",
            targetPath: @"C:\Windows\explorer.exe",
            arguments: @"shell:AppsFolder\MesmerPrism.ViscerealityCompanion_zncnfcs118r0y!App");

        Assert.False(shouldRepair);
    }
}
