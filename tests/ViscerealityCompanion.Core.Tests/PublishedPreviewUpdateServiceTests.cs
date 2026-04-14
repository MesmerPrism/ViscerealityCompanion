using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class PublishedPreviewUpdateServiceTests
{
    [Fact]
    public void TryParseAppInstallerVersion_reads_root_version()
    {
        var version = PublishedPreviewUpdateService.TryParseAppInstallerVersion(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Version="0.1.46.0">
              <MainPackage Name="MesmerPrism.ViscerealityCompanion" Version="0.1.46.0" />
            </AppInstaller>
            """);

        Assert.Equal("0.1.46.0", version);
    }

    [Theory]
    [InlineData("0.1.45.0", "0.1.46.0", true)]
    [InlineData("0.1.46.0", "0.1.46.0", false)]
    [InlineData("0.1.47.0", "0.1.46.0", false)]
    [InlineData("preview", "0.1.46.0", false)]
    [InlineData("0.1.29.0", null, false)]
    public void IsUpdateAvailable_matches_expected_versions(string? currentVersion, string? availableVersion, bool expected)
    {
        Assert.Equal(expected, PublishedPreviewUpdateService.IsUpdateAvailable(currentVersion, availableVersion));
    }

    [Fact]
    public void BuildStatus_marks_unpackaged_builds_as_not_applicable()
    {
        var status = PublishedPreviewUpdateService.BuildStatus("unpackaged", isPackaged: false, availableVersion: "0.1.46.0");

        Assert.False(status.IsApplicable);
        Assert.False(status.UpdateAvailable);
    }
}
