using System.Diagnostics;
using ViscerealityCompanion.PreviewInstaller;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class PreviewPackageInstallerTests
{
    [Fact]
    public void ParseAppInstallerManifest_reads_main_package_identity()
    {
        var appInstallerPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.appinstaller");

        try
        {
            File.WriteAllText(
                appInstallerPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Version="0.1.46.0">
                  <MainPackage Name="MesmerPrism.ViscerealityCompanion"
                               Version="0.1.46.0"
                               Publisher="CN=MesmerPrism"
                               ProcessorArchitecture="x64"
                               Uri="https://example.invalid/ViscerealityCompanion.msix" />
                </AppInstaller>
                """);

            var identity = PreviewPackageInstaller.ParseAppInstallerManifest(appInstallerPath);

            Assert.Equal("MesmerPrism.ViscerealityCompanion", identity.Name);
            Assert.Equal("CN=MesmerPrism", identity.Publisher);
            Assert.Equal("0.1.46.0", identity.Version);
            Assert.Equal(new Uri(appInstallerPath, UriKind.Absolute), identity.AppInstallerUri);
        }
        finally
        {
            File.Delete(appInstallerPath);
        }
    }

    [Fact]
    public void FindExistingPackage_matches_name_and_publisher_and_prefers_newest_match()
    {
        var packageIdentity = new PreviewPackageIdentity(
            "MesmerPrism.ViscerealityCompanionPreview",
            "CN=MesmerPrism",
            "0.1.46.0",
            new Uri("file:///C:/Temp/ViscerealityCompanion.appinstaller"));

        var package = PreviewPackageInstaller.FindExistingPackage(
            packageIdentity,
            new[]
            {
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanionPreview_0.1.36.0_x64__zcnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanionPreview",
                    "CN=MesmerPrism",
                    "0.1.36.0",
                    "MesmerPrism.ViscerealityCompanionPreview_zcnfcs118r0y"),
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanionPreview_0.1.37.0_x64__zcnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanionPreview",
                    "CN=MesmerPrism",
                    "0.1.37.0",
                    "MesmerPrism.ViscerealityCompanionPreview_zcnfcs118r0y"),
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanionPreview_0.1.99.0_x64__otherpublisher",
                    "MesmerPrism.ViscerealityCompanionPreview",
                    "CN=OtherPublisher",
                    "0.1.99.0",
                    "MesmerPrism.ViscerealityCompanionPreview_otherpublisher")
            });

        Assert.NotNull(package);
        Assert.Equal("MesmerPrism.ViscerealityCompanionPreview_0.1.37.0_x64__zcnfcs118r0y", package!.FullName);
        Assert.Equal("MesmerPrism.ViscerealityCompanionPreview", package.Name);
        Assert.Equal("0.1.37.0", package.Version);
        Assert.Equal("MesmerPrism.ViscerealityCompanionPreview_zcnfcs118r0y", package.FamilyName);
    }

    [Fact]
    public void FindLegacyPackageToRetire_matches_preview_family_for_public_release_package()
    {
        var packageIdentity = new PreviewPackageIdentity(
            "MesmerPrism.ViscerealityCompanion",
            "CN=MesmerPrism",
            "0.1.60.0",
            new Uri("file:///C:/Temp/ViscerealityCompanion.appinstaller"));

        var package = PreviewPackageInstaller.FindLegacyPackageToRetire(
            packageIdentity,
            new[]
            {
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanionPreview_0.1.59.0_x64__zncnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanionPreview",
                    "CN=MesmerPrism",
                    "0.1.59.0",
                    "MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y"),
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanionPreview_0.1.57.0_x64__zncnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanionPreview",
                    "CN=MesmerPrism",
                    "0.1.57.0",
                    "MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y")
            });

        Assert.NotNull(package);
        Assert.Equal("MesmerPrism.ViscerealityCompanionPreview_0.1.59.0_x64__zncnfcs118r0y", package!.FullName);
        Assert.Equal("MesmerPrism.ViscerealityCompanionPreview", package.Name);
        Assert.Equal("0.1.59.0", package.Version);
        Assert.Equal("MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y", package.FamilyName);
    }

    [Fact]
    public void FindLegacyPackageToRetire_matches_main_family_for_rotated_preview_package()
    {
        var packageIdentity = new PreviewPackageIdentity(
            "MesmerPrism.ViscerealityCompanionPreview",
            "CN=MesmerPrism",
            "0.1.58.0",
            new Uri("file:///C:/Temp/ViscerealityCompanion.appinstaller"));

        var package = PreviewPackageInstaller.FindLegacyPackageToRetire(
            packageIdentity,
            new[]
            {
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanion_0.1.56.0_x64__zncnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanion",
                    "CN=MesmerPrism",
                    "0.1.56.0",
                    "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y"),
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanion_0.1.55.0_x64__zncnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanion",
                    "CN=MesmerPrism",
                    "0.1.55.0",
                    "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y")
            });

        Assert.NotNull(package);
        Assert.Equal("MesmerPrism.ViscerealityCompanion_0.1.56.0_x64__zncnfcs118r0y", package!.FullName);
        Assert.Equal("MesmerPrism.ViscerealityCompanion", package.Name);
        Assert.Equal("0.1.56.0", package.Version);
        Assert.Equal("MesmerPrism.ViscerealityCompanion_zncnfcs118r0y", package.FamilyName);
    }

    [Fact]
    public void FindLegacyPackageToRetire_returns_null_for_dev_identity()
    {
        var packageIdentity = new PreviewPackageIdentity(
            "MesmerPrism.ViscerealityCompanionDev",
            "CN=MesmerPrism",
            "0.1.56.0",
            new Uri("file:///C:/Temp/ViscerealityCompanion.appinstaller"));

        var package = PreviewPackageInstaller.FindLegacyPackageToRetire(
            packageIdentity,
            new[]
            {
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanion_0.1.56.0_x64__zncnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanion",
                    "CN=MesmerPrism",
                    "0.1.56.0",
                    "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y")
            });

        Assert.Null(package);
    }

    [Fact]
    public void FindLegacyPackagesToRetire_returns_all_matching_legacy_packages()
    {
        var packageIdentity = new PreviewPackageIdentity(
            "MesmerPrism.ViscerealityCompanion",
            "CN=MesmerPrism",
            "0.1.60.0",
            new Uri("file:///C:/Temp/ViscerealityCompanion.appinstaller"));

        var packages = PreviewPackageInstaller.FindLegacyPackagesToRetire(
            packageIdentity,
            new[]
            {
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanionPreview_0.1.59.0_x64__zncnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanionPreview",
                    "CN=MesmerPrism",
                    "0.1.59.0",
                    "MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y"),
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanionPreview_0.1.57.0_x64__zncnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanionPreview",
                    "CN=MesmerPrism",
                    "0.1.57.0",
                    "MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y"),
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanion_0.1.56.0_x64__zncnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanion",
                    "CN=MesmerPrism",
                    "0.1.56.0",
                    "MesmerPrism.ViscerealityCompanion_zncnfcs118r0y")
            });

        Assert.Equal(2, packages.Count);
        Assert.Equal("0.1.59.0", packages[0].Version);
        Assert.Equal("0.1.57.0", packages[1].Version);
    }

    [Fact]
    public void BuildLegacyCleanupAction_mentions_legacy_preview_package()
    {
        var action = PreviewPackageInstaller.BuildLegacyCleanupAction(
            new[]
            {
                new ExistingPreviewPackage(
                    "MesmerPrism.ViscerealityCompanionPreview_0.1.58.0_x64__zncnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanionPreview",
                    "0.1.58.0",
                    "MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y")
            });

        Assert.Equal("Remove legacy install and retry", action.ButtonLabel);
        Assert.Contains("Viscereality Companion Preview 0.1.58.0", action.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryLaunchInstalledPackage_uses_apps_folder_target()
    {
        ProcessStartInfo? capturedStartInfo = null;

        var launched = PreviewPackageInstaller.TryLaunchInstalledPackage(
            new ExistingPreviewPackage(
                "MesmerPrism.ViscerealityCompanionPreview_0.1.46.0_x64__zcnfcs118r0y",
                "MesmerPrism.ViscerealityCompanionPreview",
                "0.1.46.0",
                "MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y"),
            out var detail,
            startInfo => capturedStartInfo = startInfo);

        Assert.True(launched);
        Assert.Contains("open the installed app automatically", detail, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(capturedStartInfo);
        Assert.Equal("explorer.exe", capturedStartInfo!.FileName);
        Assert.Equal(
            @"shell:AppsFolder\MesmerPrism.ViscerealityCompanionPreview_zncnfcs118r0y!App",
            capturedStartInfo.Arguments);
        Assert.True(capturedStartInfo.UseShellExecute);
    }
}
