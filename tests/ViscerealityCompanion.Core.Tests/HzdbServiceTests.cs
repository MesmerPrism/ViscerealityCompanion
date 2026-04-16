using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class HzdbServiceTests
{
    [Fact]
    public void TryParseQuestProximityStatus_detects_active_hold_and_expiry()
    {
        var observedAtUtc = new DateTimeOffset(2026, 03, 26, 12, 47, 32, TimeSpan.Zero);
        var rawOutput = """
            Virtual proximity state: CLOSE
            isAutosleepDisabled: false
            AutoSleepTime: 15000 ms
            State: HEADSET_MOUNTED
            Device idle state: Idle
            Event log:
              5465.13s (5.29s ago) - received com.oculus.vrpowermanager.prox_close broadcast: duration=28800000
            """;

        var parsed = WindowsHzdbService.TryParseQuestProximityStatus(rawOutput, observedAtUtc, out var status);

        Assert.True(parsed);
        Assert.True(status.Available);
        Assert.True(status.HoldActive);
        Assert.Equal("CLOSE", status.VirtualState);
        Assert.Equal("HEADSET_MOUNTED", status.HeadsetState);
        Assert.Equal(15000, status.AutoSleepTimeMs);
        Assert.False(status.IsAutosleepDisabled);
        Assert.True(status.HoldUntilUtc.HasValue);
        Assert.Equal("prox_close", status.LastBroadcastAction);
        Assert.Equal(28800000, status.LastBroadcastDurationMs);
        Assert.NotNull(status.LastBroadcastAgeSeconds);
        Assert.InRange(status.LastBroadcastAgeSeconds!.Value, 5.28d, 5.30d);
        Assert.Equal(
            observedAtUtc.AddHours(8).AddSeconds(-5.29),
            status.HoldUntilUtc!.Value,
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TryParseQuestProximityStatus_detects_normal_sensor_mode()
    {
        var observedAtUtc = new DateTimeOffset(2026, 03, 26, 12, 47, 59, TimeSpan.Zero);
        var rawOutput = """
            Virtual proximity state: DISABLED
            isAutosleepDisabled: false
            AutoSleepTime: 15000 ms
            State: WAITING_FOR_SLEEP_MSG
            Device idle state: Idle
            Event log:
              5479.07s (0.26s ago) - received com.oculus.vrpowermanager.automation_disable broadcast: duration=0
            """;

        var parsed = WindowsHzdbService.TryParseQuestProximityStatus(rawOutput, observedAtUtc, out var status);

        Assert.True(parsed);
        Assert.True(status.Available);
        Assert.False(status.HoldActive);
        Assert.Equal("DISABLED", status.VirtualState);
        Assert.Equal("WAITING_FOR_SLEEP_MSG", status.HeadsetState);
        Assert.Equal("automation_disable", status.LastBroadcastAction);
        Assert.Equal(0, status.LastBroadcastDurationMs);
        Assert.NotNull(status.LastBroadcastAgeSeconds);
        Assert.InRange(status.LastBroadcastAgeSeconds!.Value, 0.25d, 0.27d);
        Assert.Null(status.HoldUntilUtc);
    }

    [Fact]
    public void TryParseQuestProximityStatus_treats_close_without_duration_as_active_direct_override()
    {
        var observedAtUtc = new DateTimeOffset(2026, 03, 27, 08, 40, 05, TimeSpan.Zero);
        var rawOutput = """
            Virtual proximity state: CLOSE
            isAutosleepDisabled: false
            AutoSleepTime: 15000 ms
            State: HEADSET_MOUNTED
            Device idle state: Idle
            Event log:
              8139.75s (5.00s ago) - received com.oculus.vrpowermanager.prox_close broadcast: duration=0
            """;

        var parsed = WindowsHzdbService.TryParseQuestProximityStatus(rawOutput, observedAtUtc, out var status);

        Assert.True(parsed);
        Assert.True(status.Available);
        Assert.True(status.HoldActive);
        Assert.Equal("CLOSE", status.VirtualState);
        Assert.Equal("HEADSET_MOUNTED", status.HeadsetState);
        Assert.Equal("prox_close", status.LastBroadcastAction);
        Assert.Equal(0, status.LastBroadcastDurationMs);
        Assert.Null(status.HoldUntilUtc);
    }

    [Fact]
    public void TryParseQuestProximityStatus_uses_newest_relevant_broadcast()
    {
        var observedAtUtc = new DateTimeOffset(2026, 03, 27, 08, 40, 05, TimeSpan.Zero);
        var rawOutput = """
            Virtual proximity state: DISABLED
            isAutosleepDisabled: false
            AutoSleepTime: 15000 ms
            State: STANDBY
            Device idle state: Idle
            Event log:
              8120.75s (24.00s ago) - received com.oculus.vrpowermanager.prox_close broadcast: duration=600000
              8139.75s (5.00s ago) - received com.oculus.vrpowermanager.automation_disable broadcast: duration=0
            """;

        var parsed = WindowsHzdbService.TryParseQuestProximityStatus(rawOutput, observedAtUtc, out var status);

        Assert.True(parsed);
        Assert.False(status.HoldActive);
        Assert.Equal("automation_disable", status.LastBroadcastAction);
        Assert.Equal(0, status.LastBroadcastDurationMs);
        Assert.NotNull(status.LastBroadcastAgeSeconds);
        Assert.InRange(status.LastBroadcastAgeSeconds!.Value, 4.99d, 5.01d);
        Assert.Null(status.HoldUntilUtc);
    }

    [Fact]
    public void ResolveCommandPath_prefers_first_existing_normalized_candidate()
    {
        var candidates = new[]
        {
            "",
            null,
            "  \"C:\\\\missing\\\\hzdb.exe\"  ",
            ".\\tools\\..\\managed\\hzdb.exe",
            "C:\\\\other\\\\hzdb.exe"
        };

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(".\\managed\\hzdb.exe")
        };

        var resolved = WindowsHzdbService.ResolveCommandPath(candidates, existing.Contains);

        Assert.Equal(Path.GetFullPath(".\\managed\\hzdb.exe"), resolved);
    }

    [Fact]
    public void ResolveNpxCommandPath_prefers_first_existing_normalized_candidate()
    {
        var candidates = new[]
        {
            "",
            null,
            "  \"C:\\\\missing\\\\npx.cmd\"  ",
            ".\\tools\\..\\fake\\npx.cmd",
            "C:\\\\other\\\\npx.cmd"
        };

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(".\\fake\\npx.cmd")
        };

        var resolved = WindowsHzdbService.ResolveNpxCommandPath(candidates, existing.Contains);

        Assert.Equal(Path.GetFullPath(".\\fake\\npx.cmd"), resolved);
    }

    [Fact]
    public void ResolveNpxCommandPath_returns_null_when_no_candidates_exist()
    {
        var resolved = WindowsHzdbService.ResolveNpxCommandPath(
            ["C:\\\\missing\\\\npx.cmd", null, ""],
            _ => false);

        Assert.Null(resolved);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("screencap", true)]
    [InlineData(" Screencap ", true)]
    [InlineData("metacam", false)]
    public void ShouldPreferAdbScreenshot_matches_expected_methods(string? method, bool expected)
    {
        Assert.Equal(expected, WindowsHzdbService.ShouldPreferAdbScreenshot(method));
    }

    [Fact]
    public void EvaluateWakeState_flags_sensor_lock_as_not_visually_ready()
    {
        var powerOutput = """
            mWakefulness=Awake
            mInteractive=true
            Display Power: state=ON
            """;
        var foregroundOutput = """
            topResumedActivity=ActivityRecord{123 u0 com.oculus.os.vrlockscreen/.SensorLockActivity t1}
            """;

        var result = WindowsHzdbService.EvaluateWakeState(powerOutput, foregroundOutput);

        Assert.False(result.Ready);
        Assert.Contains("lock screen", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SensorLockActivity", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateWakeState_accepts_home_as_usable()
    {
        var powerOutput = """
            mWakefulness=Awake
            mInteractive=true
            Display Power: state=ON
            """;
        var foregroundOutput = """
            topResumedActivity=ActivityRecord{123 u0 com.oculus.vrshell/.HomeActivity t1}
            ACTIVITY com.oculus.vrshell/.HomeActivity 123 pid=111 userId=0
            """;

        var result = WindowsHzdbService.EvaluateWakeState(powerOutput, foregroundOutput);

        Assert.True(result.Ready);
        Assert.Contains("usable scene", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HomeActivity", result.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
