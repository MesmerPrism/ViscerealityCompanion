using ViscerealityCompanion.PreviewInstaller;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class PreviewInstallerProgramTests
{
    [Fact]
    public void ValidatePublishedPackageIdentity_accepts_release_family()
    {
        var packageIdentity = new PreviewPackageIdentity(
            "MesmerPrism.ViscerealityCompanion",
            "CN=MesmerPrism",
            "0.1.60.0",
            new Uri("file:///C:/Temp/ViscerealityCompanion.appinstaller"));

        Program.ValidatePublishedPackageIdentity(packageIdentity);
    }

    [Fact]
    public void ValidatePublishedPackageIdentity_rejects_legacy_preview_family()
    {
        var packageIdentity = new PreviewPackageIdentity(
            "MesmerPrism.ViscerealityCompanionPreview",
            "CN=MesmerPrism",
            "0.1.60.0",
            new Uri("file:///C:/Temp/ViscerealityCompanion.appinstaller"));

        var exception = Assert.Throws<InvalidOperationException>(() => Program.ValidatePublishedPackageIdentity(packageIdentity));
        Assert.Contains("MesmerPrism.ViscerealityCompanionPreview", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MesmerPrism.ViscerealityCompanion", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
