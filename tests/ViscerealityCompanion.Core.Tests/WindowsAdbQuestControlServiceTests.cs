using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class WindowsAdbQuestControlServiceTests
{
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
    public void EvaluateWakeReadiness_treats_sensor_lock_as_awake_when_no_other_meta_blocker_is_visible()
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

        Assert.True(readiness.IsAwake);
        Assert.False(readiness.IsInWakeLimbo);
        Assert.DoesNotContain("Meta visual blocker active", readiness.Detail, StringComparison.OrdinalIgnoreCase);
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
    public void EvaluateWakeReadiness_flags_focus_placeholder_as_slumber()
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

        Assert.False(readiness.IsAwake);
        Assert.True(readiness.IsInWakeLimbo);
        Assert.Contains("FocusPlaceholderActivity", readiness.Detail);
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
}
