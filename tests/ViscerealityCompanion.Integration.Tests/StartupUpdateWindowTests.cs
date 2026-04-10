namespace ViscerealityCompanion.Integration.Tests;

public sealed class StartupUpdateWindowTests
{
    [Fact]
    public async Task Startup_update_window_declares_local_visibility_converter_and_one_way_version_bindings()
    {
        var xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "ViscerealityCompanion.App",
            "StartupUpdateWindow.xaml");

        var xaml = await File.ReadAllTextAsync(Path.GetFullPath(xamlPath));

        Assert.Contains("<BooleanToVisibilityConverter x:Key=\"BoolToVisibility\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("AppCurrentVersion, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.Contains("AppAvailableVersion, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.Contains("HzdbCurrentVersion, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.Contains("HzdbAvailableVersion, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.Contains("PlatformToolsCurrentVersion, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.Contains("PlatformToolsAvailableVersion, Mode=OneWay", xaml, StringComparison.Ordinal);
    }
}
