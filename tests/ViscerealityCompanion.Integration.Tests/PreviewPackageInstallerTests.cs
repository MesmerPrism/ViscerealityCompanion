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
                <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Version="0.1.43.0">
                  <MainPackage Name="MesmerPrism.ViscerealityCompanion"
                               Version="0.1.43.0"
                               Publisher="CN=MesmerPrism"
                               ProcessorArchitecture="x64"
                               Uri="https://example.invalid/ViscerealityCompanion.msix" />
                </AppInstaller>
                """);

            var identity = PreviewPackageInstaller.ParseAppInstallerManifest(appInstallerPath);

            Assert.Equal("MesmerPrism.ViscerealityCompanion", identity.Name);
            Assert.Equal("CN=MesmerPrism", identity.Publisher);
            Assert.Equal("0.1.43.0", identity.Version);
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
            "MesmerPrism.ViscerealityCompanion",
            "CN=MesmerPrism",
            "0.1.43.0",
            new Uri("file:///C:/Temp/ViscerealityCompanion.appinstaller"));

        var package = PreviewPackageInstaller.FindExistingPackage(
            packageIdentity,
            new[]
            {
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanion_0.1.36.0_x64__zcnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanion",
                    "CN=MesmerPrism",
                    "0.1.36.0"),
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanion_0.1.37.0_x64__zcnfcs118r0y",
                    "MesmerPrism.ViscerealityCompanion",
                    "CN=MesmerPrism",
                    "0.1.37.0"),
                new PreviewPackageCandidate(
                    "MesmerPrism.ViscerealityCompanion_0.1.99.0_x64__otherpublisher",
                    "MesmerPrism.ViscerealityCompanion",
                    "CN=OtherPublisher",
                    "0.1.99.0")
            });

        Assert.NotNull(package);
        Assert.Equal("MesmerPrism.ViscerealityCompanion_0.1.37.0_x64__zcnfcs118r0y", package!.FullName);
        Assert.Equal("0.1.37.0", package.Version);
    }
}
