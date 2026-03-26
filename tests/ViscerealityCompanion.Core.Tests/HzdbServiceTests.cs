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
        Assert.Null(status.HoldUntilUtc);
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
}
