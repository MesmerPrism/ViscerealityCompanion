using System.IO;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class WindowsAdbQuestControlServiceTests
{
    [Fact]
    public void BuildInstalledPackageHashTempPath_uses_unique_file_names_per_call()
    {
        var first = WindowsAdbQuestControlService.BuildInstalledPackageHashTempPath("com.Viscereality.SussexExperiment");
        var second = WindowsAdbQuestControlService.BuildInstalledPackageHashTempPath("com.Viscereality.SussexExperiment");

        Assert.NotEqual(first, second);
        Assert.Equal(Path.GetDirectoryName(first), Path.GetDirectoryName(second));
        Assert.StartsWith("com.Viscereality.SussexExperiment-", Path.GetFileNameWithoutExtension(first), StringComparison.Ordinal);
        Assert.EndsWith(".apk", first, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".apk", second, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildInstalledPackageHashUnavailableDetail_appends_failure_reason_when_present()
    {
        var detail = WindowsAdbQuestControlService.BuildInstalledPackageHashUnavailableDetail(
            "The process cannot access the file because it is being used by another process.");

        Assert.StartsWith("Installed package hash unavailable.", detail, StringComparison.Ordinal);
        Assert.Contains("used by another process", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRuntimeHotloadRemotePaths_keeps_adb_push_path_unquoted()
    {
        var paths = WindowsAdbQuestControlService.BuildRuntimeHotloadRemotePaths("com.Viscereality.SussexExperiment");

        Assert.Equal(
            "/sdcard/Android/data/com.Viscereality.SussexExperiment/files/runtime_hotload",
            paths.DeviceDirectory);
        Assert.Equal(
            "/sdcard/Android/data/com.Viscereality.SussexExperiment/files/runtime_hotload/runtime_overrides.csv",
            paths.DeviceFile);
        Assert.Equal(
            "'/sdcard/Android/data/com.Viscereality.SussexExperiment/files/runtime_hotload'",
            paths.QuotedDirectory);
        Assert.Equal(
            "'/sdcard/Android/data/com.Viscereality.SussexExperiment/files/runtime_hotload/runtime_overrides.csv'",
            paths.QuotedFile);
    }

    [Fact]
    public void ParseQuestWifiStatus_extracts_ssid_and_ip()
    {
        var output = """
            Wifi is enabled
            Wifi is connected to "MagentaWLAN-R5V4"
            WifiInfo: SSID: "MagentaWLAN-R5V4", BSSID: aa:bb:cc:dd:ee:ff, MAC: 11:22:33:44:55:66, IP: /192.168.2.56, Security type: 4, Supplicant state: COMPLETED, Wi-Fi standard: 11ac, Link speed: 866Mbps
            """;

        var status = WindowsAdbQuestControlService.ParseQuestWifiStatus(output);

        Assert.Equal("MagentaWLAN-R5V4", status.Ssid);
        Assert.Equal("192.168.2.56", status.IpAddress);
    }

    [Fact]
    public void ParseHostWifiStatus_returns_connected_ssid()
    {
        var output = """

            Name                   : Wi-Fi
            Description            : Intel(R) Wi-Fi 6E AX211 160MHz
            GUID                   : 12345678-1234-1234-1234-1234567890ab
            Physical address       : 00:11:22:33:44:55
            State                  : connected
            SSID                   : MagentaWLAN-R5V4
            BSSID                  : aa:bb:cc:dd:ee:ff
            Network type           : Infrastructure
            Radio type             : 802.11ax
            """;

        var status = WindowsAdbQuestControlService.ParseHostWifiStatus(output);

        Assert.Equal("Wi-Fi", status.InterfaceName);
        Assert.Equal("MagentaWLAN-R5V4", status.Ssid);
    }

    [Fact]
    public void ParseHostWifiStatus_ignores_netsh_header_lines()
    {
        var output = """
            There is 1 interface on the system:

                Name                   : Wi-Fi
                Description            : Realtek 8852CE WiFi 6E PCI-E NIC
                GUID                   : 12345678-1234-1234-1234-1234567890ab
                Physical address       : 00:11:22:33:44:55
                State                  : connected
                SSID                   : MagentaWLAN-R5V4
                BSSID                  : aa:bb:cc:dd:ee:ff
                Network type           : Infrastructure
                Radio type             : 802.11ax
            """;

        var status = WindowsAdbQuestControlService.ParseHostWifiStatus(output);

        Assert.Equal("Wi-Fi", status.InterfaceName);
        Assert.Equal("MagentaWLAN-R5V4", status.Ssid);
    }

    [Fact]
    public void ParseHostWifiStatus_returns_empty_when_no_connected_interface_is_present()
    {
        var output = """

            Name                   : Wi-Fi
            State                  : disconnected
            SSID                   :
            BSSID                  :
            """;

        var status = WindowsAdbQuestControlService.ParseHostWifiStatus(output);

        Assert.Equal(string.Empty, status.InterfaceName);
        Assert.Equal(string.Empty, status.Ssid);
    }

    [Fact]
    public void ParseQuestWifiIpAddressFromIpRoute_extracts_src_ip_from_wlan_route()
    {
        var output = """
            139.184.120.0/21 dev wlan0 proto kernel scope link src 139.184.125.224
            """;

        var ipAddress = WindowsAdbQuestControlService.ParseQuestWifiIpAddressFromIpRoute(output);

        Assert.Equal("139.184.125.224", ipAddress);
    }

    [Fact]
    public void ParseQuestWifiIpAddressFromIpRoute_ignores_non_wlan_routes()
    {
        var output = """
            default via 192.168.1.1 dev rmnet_data0
            192.168.1.0/24 dev rmnet_data0 proto kernel scope link src 192.168.1.12
            """;

        var ipAddress = WindowsAdbQuestControlService.ParseQuestWifiIpAddressFromIpRoute(output);

        Assert.Equal(string.Empty, ipAddress);
    }

    [Fact]
    public void ParseQuestPowerStatus_reports_awake_state_from_power_dump()
    {
        var output = """
            mWakefulness=Awake
            mInteractive=true
            Display Power: state=ON
            """;

        var status = WindowsAdbQuestControlService.ParseQuestPowerStatus(output);

        Assert.True(status.IsAwake);
        Assert.True(status.IsInteractive);
        Assert.Equal("Awake", status.Wakefulness);
        Assert.Equal("ON", status.DisplayPowerState);
    }

    [Fact]
    public void ParseQuestPowerStatus_reports_sleep_state_from_power_dump()
    {
        var output = """
            mWakefulness=Asleep
            mInteractive=false
            Display Power: state=OFF
            """;

        var status = WindowsAdbQuestControlService.ParseQuestPowerStatus(output);

        Assert.False(status.IsAwake);
        Assert.False(status.IsInteractive);
        Assert.Equal("Asleep", status.Wakefulness);
        Assert.Equal("OFF", status.DisplayPowerState);
    }

    [Fact]
    public void ParseControllerStatuses_extracts_left_and_right_battery_levels()
    {
        var output = """
            ControllerTrackingGlue Status
              Right
                Device: d2a88f3474addf66 handedness: Right
               15163.204s (24.365s ago) - [id: d2a88f3474addf66, model: CONTROLLER_RUBY, conn: CONNECTED_ACTIVE, battery: 87, fw: 201.36.6, ]
              Left
                Device: 9f1d3c6cc0cbaf1a handedness: Left
               15157.588s (29.981s ago) - [id: 9f1d3c6cc0cbaf1a, model: CONTROLLER_RUBY, conn: CONNECTED_INACTIVE, battery: 64, fw: 201.36.6, ]
            """;

        var statuses = WindowsAdbQuestControlService.ParseControllerStatuses(output);

        var left = Assert.Single(statuses, status => status.HandLabel == "Left");
        var right = Assert.Single(statuses, status => status.HandLabel == "Right");

        Assert.Equal(64, left.BatteryLevel);
        Assert.Equal("CONNECTED_INACTIVE", left.ConnectionState);
        Assert.Equal("9f1d3c6cc0cbaf1a", left.DeviceId);
        Assert.Equal(87, right.BatteryLevel);
        Assert.Equal("CONNECTED_ACTIVE", right.ConnectionState);
        Assert.Equal("d2a88f3474addf66", right.DeviceId);
    }

    [Fact]
    public void EvaluateWakeReadiness_flags_primary_sensor_lock_as_blocked()
    {
        var powerStatus = new WindowsAdbQuestControlService.QuestPowerStatus(
            "Awake",
            true,
            "ON",
            true,
            "wakefulness Awake; interactive true; display ON");
        var snapshot = new AdbShellSupport.ForegroundAppSnapshot(
            "com.oculus.os.vrlockscreen",
            ".SensorLockActivity",
            "com.oculus.os.vrlockscreen/.SensorLockActivity",
            ["com.oculus.os.vrlockscreen/.SensorLockActivity"]);

        var readiness = WindowsAdbQuestControlService.EvaluateWakeReadiness(powerStatus, snapshot);

        Assert.False(readiness.IsAwake);
        Assert.True(readiness.IsInWakeLimbo);
        Assert.Contains("SensorLockActivity", readiness.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lock-screen blocker", readiness.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateWakeReadiness_keeps_home_shell_as_awake()
    {
        var powerStatus = new WindowsAdbQuestControlService.QuestPowerStatus(
            "Awake",
            true,
            "ON",
            true,
            "wakefulness Awake; interactive true; display ON");
        var snapshot = new AdbShellSupport.ForegroundAppSnapshot(
            "com.oculus.vrshell",
            ".HomeActivity",
            "com.oculus.vrshell/.HomeActivity",
            ["com.oculus.vrshell/.HomeActivity"]);

        var readiness = WindowsAdbQuestControlService.EvaluateWakeReadiness(powerStatus, snapshot);

        Assert.Equal(true, readiness.IsAwake);
        Assert.False(readiness.IsInWakeLimbo);
        Assert.DoesNotContain("sensor lock", readiness.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateWakeReadiness_flags_clear_activity_as_slumber_not_command_ready()
    {
        var powerStatus = new WindowsAdbQuestControlService.QuestPowerStatus(
            "Awake",
            true,
            "ON",
            true,
            "wakefulness Awake; interactive true; display ON");
        var snapshot = new AdbShellSupport.ForegroundAppSnapshot(
            "com.oculus.os.clearactivity",
            ".ClearActivity",
            "com.oculus.os.clearactivity/.ClearActivity",
            ["com.oculus.os.clearactivity/.ClearActivity"]);

        var readiness = WindowsAdbQuestControlService.EvaluateWakeReadiness(powerStatus, snapshot);

        Assert.Equal(false, readiness.IsAwake);
        Assert.True(readiness.IsInWakeLimbo);
        Assert.Contains("ClearActivity", readiness.Detail);
    }

    [Fact]
    public void EvaluateWakeReadiness_ignores_stale_sensor_lock_when_unity_is_focused()
    {
        var powerStatus = new WindowsAdbQuestControlService.QuestPowerStatus(
            "Awake",
            true,
            "ON",
            true,
            "wakefulness Awake; interactive true; display ON");
        var snapshot = new AdbShellSupport.ForegroundAppSnapshot(
            "com.Viscereality.SussexExperiment",
            "com.unity3d.player.UnityPlayerGameActivity",
            "com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity",
            [
                "com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity",
                "com.oculus.os.vrlockscreen/.SensorLockActivity",
                "com.oculus.vrshell/.HomeActivity"
            ]);

        var readiness = WindowsAdbQuestControlService.EvaluateWakeReadiness(powerStatus, snapshot);

        Assert.True(readiness.IsAwake);
        Assert.False(readiness.IsInWakeLimbo);
        Assert.DoesNotContain("SensorLockActivity", readiness.Detail);
    }

    [Fact]
    public void EvaluateWakeReadiness_ignores_stale_focus_placeholder_when_unity_is_focused()
    {
        var powerStatus = new WindowsAdbQuestControlService.QuestPowerStatus(
            "Awake",
            true,
            "ON",
            true,
            "wakefulness Awake; interactive true; display ON");
        var snapshot = new AdbShellSupport.ForegroundAppSnapshot(
            "com.Viscereality.SussexExperiment",
            "com.unity3d.player.UnityPlayerGameActivity",
            "com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity",
            [
                "com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity",
                "com.oculus.vrshell/.FocusPlaceholderActivity",
                "com.oculus.vrshell/.HomeActivity"
            ]);

        var readiness = WindowsAdbQuestControlService.EvaluateWakeReadiness(powerStatus, snapshot);

        Assert.True(readiness.IsAwake);
        Assert.False(readiness.IsInWakeLimbo);
        Assert.DoesNotContain("FocusPlaceholderActivity", readiness.Detail);
    }

    [Fact]
    public void EvaluateWakeReadiness_flags_guardian_dialog_when_it_remains_visible()
    {
        var powerStatus = new WindowsAdbQuestControlService.QuestPowerStatus(
            "Awake",
            true,
            "ON",
            true,
            "wakefulness Awake; interactive true; display ON");
        var snapshot = new AdbShellSupport.ForegroundAppSnapshot(
            "com.oculus.vrshell",
            ".HomeActivity",
            "com.oculus.vrshell/.HomeActivity",
            [
                "com.oculus.vrshell/.HomeActivity",
                "com.oculus.guardian/com.oculus.vrguardianservice.guardiandialog.GuardianDialogActivity",
                "com.oculus.os.vrlockscreen/.SensorLockActivity"
            ]);

        var readiness = WindowsAdbQuestControlService.EvaluateWakeReadiness(powerStatus, snapshot);

        Assert.False(readiness.IsAwake);
        Assert.True(readiness.IsInWakeLimbo);
        Assert.Contains("GuardianDialogActivity", readiness.Detail);
    }

    [Fact]
    public void EvaluateWakeReadiness_keeps_home_shell_awake_when_only_sensor_lock_is_visible()
    {
        var powerStatus = new WindowsAdbQuestControlService.QuestPowerStatus(
            "Awake",
            true,
            "ON",
            true,
            "wakefulness Awake; interactive true; display ON");
        var snapshot = new AdbShellSupport.ForegroundAppSnapshot(
            "com.oculus.vrshell",
            ".HomeActivity",
            "com.oculus.vrshell/.HomeActivity",
            [
                "com.oculus.vrshell/.HomeActivity",
                "com.oculus.os.vrlockscreen/.SensorLockActivity"
            ]);

        var readiness = WindowsAdbQuestControlService.EvaluateWakeReadiness(powerStatus, snapshot);

        Assert.True(readiness.IsAwake);
        Assert.False(readiness.IsInWakeLimbo);
    }

    [Fact]
    public void EvaluateWakeReadiness_keeps_home_shell_awake_when_focus_placeholder_is_only_visible_companion_layer()
    {
        var powerStatus = new WindowsAdbQuestControlService.QuestPowerStatus(
            "Awake",
            true,
            "ON",
            true,
            "wakefulness Awake; interactive true; display ON");
        var snapshot = new AdbShellSupport.ForegroundAppSnapshot(
            "com.oculus.vrshell",
            ".HomeActivity",
            "com.oculus.vrshell/.HomeActivity",
            [
                "com.oculus.vrshell/.HomeActivity",
                "com.oculus.vrshell/.FocusPlaceholderActivity",
                "com.oculus.systemux/com.oculus.panelapp.virtualobjects.VirtualObjectsActivity"
            ]);

        var readiness = WindowsAdbQuestControlService.EvaluateWakeReadiness(powerStatus, snapshot);

        Assert.True(readiness.IsAwake);
        Assert.False(readiness.IsInWakeLimbo);
        Assert.DoesNotContain("FocusPlaceholderActivity", readiness.Detail);
    }

    [Fact]
    public void EvaluateWakeReadiness_keeps_primary_focus_placeholder_awake_when_no_guardian_or_clearactivity_is_visible()
    {
        var powerStatus = new WindowsAdbQuestControlService.QuestPowerStatus(
            "Awake",
            true,
            "ON",
            true,
            "wakefulness Awake; interactive true; display ON");
        var snapshot = new AdbShellSupport.ForegroundAppSnapshot(
            "com.oculus.vrshell",
            ".FocusPlaceholderActivity",
            "com.oculus.vrshell/.FocusPlaceholderActivity",
            [
                "com.oculus.vrshell/.FocusPlaceholderActivity",
                "com.oculus.vrshell/.HomeActivity"
            ]);

        var readiness = WindowsAdbQuestControlService.EvaluateWakeReadiness(powerStatus, snapshot);

        Assert.True(readiness.IsAwake);
        Assert.False(readiness.IsInWakeLimbo);
        Assert.DoesNotContain("FocusPlaceholderActivity", readiness.Detail);
    }

    [Fact]
    public void MergeWakeWarning_upgrades_success_to_warning_and_keeps_command_summary()
    {
        var wakeOutcome = new OperationOutcome(
            OperationOutcomeKind.Warning,
            "Wake before launching Sussex Controller Study APK left the headset blocked.",
            "Initial: wakefulness Asleep; foreground com.oculus.vrshell/.FocusPlaceholderActivity");
        var launchOutcome = new OperationOutcome(
            OperationOutcomeKind.Success,
            "Launch command sent for Sussex Controller Study APK.",
            "Events injected: 1");

        var merged = WindowsAdbQuestControlService.MergeWakeWarning(launchOutcome, wakeOutcome);

        Assert.Equal(OperationOutcomeKind.Warning, merged.Kind);
        Assert.Equal(launchOutcome.Summary, merged.Summary);
        Assert.Contains(wakeOutcome.Summary, merged.Detail);
        Assert.Contains(launchOutcome.Detail, merged.Detail);
    }

    [Fact]
    public void MergeWakeWarning_leaves_failure_kind_unchanged()
    {
        var wakeOutcome = new OperationOutcome(
            OperationOutcomeKind.Warning,
            "Wake before stopping Sussex Controller Study APK left the headset blocked.",
            "Initial: wakefulness Awake; foreground com.oculus.guardian/com.oculus.vrguardianservice.guardiandialog.GuardianDialogActivity");
        var stopOutcome = new OperationOutcome(
            OperationOutcomeKind.Failure,
            "Stop failed for Sussex Controller Study APK.",
            "device offline");

        var merged = WindowsAdbQuestControlService.MergeWakeWarning(stopOutcome, wakeOutcome);

        Assert.Equal(OperationOutcomeKind.Failure, merged.Kind);
        Assert.Equal(stopOutcome.Summary, merged.Summary);
        Assert.Contains(wakeOutcome.Summary, merged.Detail);
        Assert.Contains(stopOutcome.Detail, merged.Detail);
    }

    [Fact]
    public void BuildSleepBlockedLaunchOutcome_requires_waking_before_launch()
    {
        var target = new QuestAppTarget(
            Id: "sussex",
            Label: "Sussex Experiment",
            PackageId: "com.Viscereality.SussexExperiment",
            ApkFile: string.Empty,
            LaunchComponent: "com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity",
            BrowserPackageId: string.Empty,
            Description: string.Empty,
            Tags: []);
        var readiness = new WindowsAdbQuestControlService.QuestWakeReadiness(
            IsAwake: false,
            IsInWakeLimbo: false,
            WakeLimboComponent: string.Empty,
            Detail: "wakefulness Asleep; interactive false; display OFF");

        var outcome = WindowsAdbQuestControlService.BuildSleepBlockedLaunchOutcome(target, readiness);

        Assert.Equal(OperationOutcomeKind.Failure, outcome.Kind);
        Assert.Equal("Launch blocked for Sussex Experiment.", outcome.Summary);
        Assert.Equal(target.PackageId, outcome.PackageId);
        Assert.Contains("Wake the headset to enable launching", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("headset restart", outcome.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildVisualBlockedLaunchOutcome_requires_clearing_blocker_before_launch()
    {
        var target = new QuestAppTarget(
            Id: "sussex",
            Label: "Sussex Experiment",
            PackageId: "com.Viscereality.SussexExperiment",
            ApkFile: string.Empty,
            LaunchComponent: "com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity",
            BrowserPackageId: string.Empty,
            Description: string.Empty,
            Tags: []);
        var readiness = new WindowsAdbQuestControlService.QuestWakeReadiness(
            IsAwake: false,
            IsInWakeLimbo: true,
            WakeLimboComponent: "com.oculus.guardian/com.oculus.vrguardianservice.guardiandialog.GuardianDialogActivity",
            Detail: "wakefulness Awake; interactive true; display ON; foreground com.oculus.guardian/com.oculus.vrguardianservice.guardiandialog.GuardianDialogActivity; Guardian or tracking blocker active");

        var outcome = WindowsAdbQuestControlService.BuildVisualBlockedLaunchOutcome(target, readiness);

        Assert.Equal(OperationOutcomeKind.Failure, outcome.Kind);
        Assert.Equal("Launch blocked for Sussex Experiment.", outcome.Summary);
        Assert.Equal(target.PackageId, outcome.PackageId);
        Assert.Contains("Clear the current Guardian, tracking-loss, or ClearActivity blocker before launching", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildVisualBlockedLaunchOutcome_requires_clearing_lock_screen_when_sensor_lock_is_foreground()
    {
        var target = new QuestAppTarget(
            Id: "sussex",
            Label: "Sussex Experiment",
            PackageId: "com.Viscereality.SussexExperiment",
            ApkFile: "sussex.apk",
            LaunchComponent: "com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity",
            BrowserPackageId: string.Empty,
            Description: string.Empty,
            Tags: []);

        var readiness = new WindowsAdbQuestControlService.QuestWakeReadiness(
            IsAwake: false,
            IsInWakeLimbo: true,
            WakeLimboComponent: "com.oculus.os.vrlockscreen/.SensorLockActivity",
            Detail: "wakefulness Awake; interactive true; display ON; foreground com.oculus.os.vrlockscreen/.SensorLockActivity; Quest lock-screen blocker active");

        var outcome = WindowsAdbQuestControlService.BuildVisualBlockedLaunchOutcome(target, readiness);

        Assert.Equal(OperationOutcomeKind.Failure, outcome.Kind);
        Assert.Equal("Launch blocked for Sussex Experiment.", outcome.Summary);
        Assert.Equal(target.PackageId, outcome.PackageId);
        Assert.Contains("Clear the headset lock screen on the headset before launching", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("SensorLockActivity", outcome.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseKioskForegroundEvidence_detects_clear_activity_as_real_pinned_foreground()
    {
        const string output = """
            topResumedActivity=ActivityRecord{c61340f u0 com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity t8083}
            ResumedActivity: ActivityRecord{e5cc2d7 u0 com.oculus.os.clearactivity/.ClearActivity t8085}
            mCurrentFocus=Window{39d1619 u0 com.oculus.os.clearactivity/com.oculus.os.clearactivity.ClearActivity}
            mFocusedApp=ActivityRecord{e5cc2d7 u0 com.oculus.os.clearactivity/.ClearActivity t8085}
            mTopFullscreenOpaqueWindowState=Window{39d1619 u0 com.oculus.os.clearactivity/com.oculus.os.clearactivity.ClearActivity}
            mLockTaskModeState=PINNED
            """;

        var evidence = WindowsAdbQuestControlService.ParseKioskForegroundEvidence(output);

        Assert.NotNull(evidence.Snapshot);
        Assert.Equal("com.oculus.os.clearactivity/.ClearActivity", evidence.Snapshot!.PrimaryComponent);
        Assert.Equal("com.oculus.os.clearactivity/.ClearActivity", evidence.ResumedComponent);
        Assert.Equal("com.oculus.os.clearactivity/com.oculus.os.clearactivity.ClearActivity", evidence.CurrentFocusComponent);
        Assert.Equal("com.oculus.os.clearactivity/com.oculus.os.clearactivity.ClearActivity", evidence.TopOpaqueComponent);
        Assert.Equal("PINNED", evidence.LockTaskModeState);
        Assert.False(WindowsAdbQuestControlService.IsPinnedForegroundForPackage(evidence, "com.Viscereality.SussexExperiment"));
    }

    [Fact]
    public void IsPinnedForegroundForPackage_accepts_stable_target_pin()
    {
        const string output = """
            topResumedActivity=ActivityRecord{e631854 u0 com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity t8086}
            ResumedActivity: ActivityRecord{e631854 u0 com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity t8086}
            mCurrentFocus=Window{c420b05 u0 com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity}
            mFocusedApp=ActivityRecord{e631854 u0 com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity t8086}
            mFocusedWindow=Window{c420b05 u0 com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity}
            mTopFullscreenOpaqueWindowState=Window{c420b05 u0 com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity}
            mLockTaskModeState=PINNED
            """;

        var evidence = WindowsAdbQuestControlService.ParseKioskForegroundEvidence(output);

        Assert.True(WindowsAdbQuestControlService.IsSettledForegroundForPackage(evidence, "com.Viscereality.SussexExperiment"));
        Assert.True(WindowsAdbQuestControlService.IsPinnedForegroundForPackage(evidence, "com.Viscereality.SussexExperiment"));
    }

    [Fact]
    public void IsHomeForegroundAfterExit_accepts_home_shell_foreground()
    {
        const string output = """
            ResumedActivity: ActivityRecord{8d9f77c u0 com.oculus.vrshell/.HomeActivity t7929}
            mCurrentFocus=Window{5724ad1 u0 com.oculus.vrshell/com.oculus.vrshell.HomeActivity}
            mFocusedWindow=Window{5724ad1 u0 com.oculus.vrshell/com.oculus.vrshell.HomeActivity}
            mTopFullscreenOpaqueWindowState=Window{5724ad1 u0 com.oculus.vrshell/com.oculus.vrshell.HomeActivity}
            mLockTaskModeState=NONE
            """;

        var evidence = WindowsAdbQuestControlService.ParseKioskForegroundEvidence(output);

        Assert.True(WindowsAdbQuestControlService.IsHomeForegroundAfterExit(evidence, "com.Viscereality.SussexExperiment"));
    }

    [Fact]
    public void IsHomeForegroundAfterExit_accepts_systemux_virtual_objects_foreground()
    {
        const string output = """
            ResumedActivity: ActivityRecord{12345 u0 com.oculus.systemux/com.oculus.panelapp.virtualobjects.VirtualObjectsActivity t7931}
            mCurrentFocus=Window{67890 u0 com.oculus.systemux/com.oculus.panelapp.virtualobjects.VirtualObjectsActivity}
            mFocusedWindow=Window{67890 u0 com.oculus.systemux/com.oculus.panelapp.virtualobjects.VirtualObjectsActivity}
            mTopFullscreenOpaqueWindowState=Window{67890 u0 com.oculus.systemux/com.oculus.panelapp.virtualobjects.VirtualObjectsActivity}
            mLockTaskModeState=NONE
            """;

        var evidence = WindowsAdbQuestControlService.ParseKioskForegroundEvidence(output);

        Assert.True(WindowsAdbQuestControlService.IsHomeForegroundAfterExit(evidence, "com.Viscereality.SussexExperiment"));
    }
}
