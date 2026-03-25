using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class AdbShellSupportTests
{
    [Fact]
    public void ParseForegroundPackage_ExtractsResumedPackage()
    {
        const string output = """
        topResumedActivity=ActivityRecord{12345 u0 org.aliusresearch.viscereality.preview/com.unity3d.player.UnityPlayerActivity t12}
        """;

        var packageId = AdbShellSupport.ParseForegroundPackage(output);

        Assert.Equal("org.aliusresearch.viscereality.preview", packageId);
    }

    [Fact]
    public void ParseInstalledPackages_ExtractsDistinctPackageIds()
    {
        const string output = """
        package:org.aliusresearch.viscereality.preview
        package:com.oculus.browser
        package:org.aliusresearch.viscereality.preview
        """;

        var packages = AdbShellSupport.ParseInstalledPackages(output);

        Assert.Equal(2, packages.Count);
        Assert.Contains("org.aliusresearch.viscereality.preview", packages);
        Assert.Contains("com.oculus.browser", packages);
    }

    [Fact]
    public void ParseBatteryLevel_ReadsNumericBatteryLevel()
    {
        const string output = """
        AC powered: false
        level: 83
        status: 3
        """;

        var batteryLevel = AdbShellSupport.ParseBatteryLevel(output);

        Assert.Equal(83, batteryLevel);
    }

    [Fact]
    public void ParseForegroundPackage_ExtractsFromCurrentFocus()
    {
        const string output = """
        mCurrentFocus=Window{abc1234 u0 com.oculus.browser/com.oculus.browser.BrowserActivity}
        """;

        var packageId = AdbShellSupport.ParseForegroundPackage(output);

        Assert.Equal("com.oculus.browser", packageId);
    }

    [Fact]
    public void ParseForegroundPackage_ExtractsFromFocusedApp()
    {
        const string output = """
        mFocusedApp=AppWindowToken{def5678 token=Token{aaa u0 org.aliusresearch.viscereality.twin/com.unity3d.player.UnityPlayerActivity}}
        """;

        var packageId = AdbShellSupport.ParseForegroundPackage(output);

        Assert.Equal("org.aliusresearch.viscereality.twin", packageId);
    }

    [Fact]
    public void ParseForegroundPackage_ExtractsFromWindowListing()
    {
        const string output = """
        Window #1 Window{fa4f5ee u0 com.Viscereality.KarateBio/com.unity3d.player.UnityPlayerGameActivity}:
        """;

        var packageId = AdbShellSupport.ParseForegroundPackage(output);

        Assert.Equal("com.Viscereality.KarateBio", packageId);
    }

    [Fact]
    public void ParseVisibleActivityComponents_ExtractsOrderedVisibleActivities()
    {
        const string output = """
        topResumedActivity=ActivityRecord{9dc95ae u0 com.Viscereality.KarateBio/com.unity3d.player.UnityPlayerGameActivity t5527}
        topResumedActivity=ActivityRecord{93af951 u0 com.oculus.vrshell/com.oculus.panelapp.controlbar.ControlBarActivity t5524}
        topResumedActivity=ActivityRecord{6684d82 u0 com.oculus.systemux/com.oculus.panelapp.anytimeui.AnytimeUIActivity t5522}
        """;

        var components = AdbShellSupport.ParseVisibleActivityComponents(output);

        Assert.Equal(3, components.Count);
        Assert.Equal("com.Viscereality.KarateBio/com.unity3d.player.UnityPlayerGameActivity", components[0]);
        Assert.Equal("com.oculus.vrshell/com.oculus.panelapp.controlbar.ControlBarActivity", components[1]);
        Assert.Equal("com.oculus.systemux/com.oculus.panelapp.anytimeui.AnytimeUIActivity", components[2]);
    }

    [Fact]
    public void ParseForegroundPackage_ExtractsFromTopFullscreen()
    {
        const string output = """
        mTopFullscreenOpaqueOrDimmingWindowState=WindowState{abcd u0 org.aliusresearch.viscereality.preview/com.unity3d.player.UnityPlayerActivity}
        """;

        var packageId = AdbShellSupport.ParseForegroundPackage(output);

        Assert.Equal("org.aliusresearch.viscereality.preview", packageId);
    }

    [Fact]
    public void ParseForegroundPackage_ReturnsNull_WhenNoMatch()
    {
        const string output = "some random text that does not contain foreground info";

        var packageId = AdbShellSupport.ParseForegroundPackage(output);

        Assert.Null(packageId);
    }
}
