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
    public void ParseForegroundSnapshot_prefers_authoritative_focused_component_over_historical_visible_entries()
    {
        const string output = """
        topResumedActivity=ActivityRecord{c61340f u0 com.Viscereality.LslTwin/com.unity3d.player.UnityPlayerGameActivity t5749}
        topResumedActivity=ActivityRecord{f3d8c93 u0 com.oculus.os.vrlockscreen/.SensorLockActivity t5709}
        ResumedActivity: ActivityRecord{c61340f u0 com.Viscereality.LslTwin/com.unity3d.player.UnityPlayerGameActivity t5749}
        mCurrentFocus=Window{461264c u0 com.Viscereality.LslTwin/com.unity3d.player.UnityPlayerGameActivity}
        mFocusedApp=ActivityRecord{c61340f u0 com.Viscereality.LslTwin/com.unity3d.player.UnityPlayerGameActivity t5749}
        """;

        var snapshot = AdbShellSupport.ParseForegroundSnapshot(output);

        Assert.NotNull(snapshot);
        Assert.Equal("com.Viscereality.LslTwin", snapshot!.PackageId);
        Assert.Equal("com.Viscereality.LslTwin/com.unity3d.player.UnityPlayerGameActivity", snapshot.PrimaryComponent);
        Assert.Equal(2, snapshot.VisibleComponents.Count);
        Assert.Contains("com.oculus.os.vrlockscreen/.SensorLockActivity", snapshot.VisibleComponents);
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

    [Fact]
    public void ParseRecentTaskId_ExtractsTaskIdForPackage()
    {
        const string output = """
        * Recent #3: Task{f7032e0 #6247 type=standard A=10237:com.Viscereality.LslTwin}
          userId=0 effectiveUid=u0a237 mCallingUid=2000
        * Recent #4: Task{7a32bb6 #6246 type=standard A=10043:com.oculus.systemux:2065988206}
        """;

        var taskId = AdbShellSupport.ParseRecentTaskId(output, "com.Viscereality.LslTwin");

        Assert.Equal(6247, taskId);
    }

    [Fact]
    public void ParseRecentTaskId_IgnoresCallingUidInsideMatchingTaskBlock()
    {
        const string output = """
        ACTIVITY MANAGER RECENT TASKS (dumpsys activity recents)
          Recent tasks:
          * Recent #0: Task{1814e4e #6668 type=standard A=10237:com.Viscereality.LslTwin}
            userId=0 effectiveUid=u0a237 mCallingUid=2000 mUserSetupComplete=true mCallingPackage=com.android.shell mCallingFeatureId=null
            affinity=10237:com.Viscereality.LslTwin
            intent={act=android.intent.action.MAIN cat=[android.intent.category.LAUNCHER] flg=0x10a10100 cmp=com.Viscereality.LslTwin/com.unity3d.player.UnityPlayerGameActivity}
            mActivityComponent=com.Viscereality.LslTwin/com.unity3d.player.UnityPlayerGameActivity
            rootWasReset=true mNeverRelinquishIdentity=true mReuseTask=false mLockTaskAuth=LOCK_TASK_AUTH_PINNABLE
            Activities=[ActivityRecord{1bb2549 u0 com.Viscereality.LslTwin/com.unity3d.player.UnityPlayerGameActivity t6668}]
            askedCompatMode=false inRecents=true isAvailable=true
            taskId=6668 rootTaskId=3
            hasChildPipActivity=false
          * Recent #1: Task{8891174 #6518 type=standard A=10066:com.oculus.store}
            userId=0 effectiveUid=u0a66 mCallingUid=u0a29
            taskId=6518 rootTaskId=6517
        """;

        var taskId = AdbShellSupport.ParseRecentTaskId(output, "com.Viscereality.LslTwin");

        Assert.Equal(6668, taskId);
    }
}
