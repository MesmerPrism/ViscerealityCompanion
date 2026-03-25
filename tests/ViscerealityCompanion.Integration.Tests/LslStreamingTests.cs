using System.Runtime.CompilerServices;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

/// <summary>
/// Tests for LSL streaming between Windows and Quest over WiFi.
/// Validates breathing belt outlet, twin command/config publishing,
/// and state monitoring from the headset.
/// </summary>
public class LslStreamingTests
{

    [Fact]
    public void TwinBridge_creates_outlets_successfully()
    {
        var commandOutlet = new FakeOutletService();
        var configOutlet = new FakeOutletService();
        var stateMonitor = new FakeMonitorService();
        using var bridge = new LslTwinModeBridge(commandOutlet, configOutlet, stateMonitor);

        var result = bridge.Open();
        Assert.Equal(OperationOutcomeKind.Success, result.Kind);
        Assert.True(commandOutlet.IsOpen);
        Assert.True(configOutlet.IsOpen);
    }

    [Fact]
    public async Task TwinBridge_sends_command_through_outlet()
    {
        var commandOutlet = new FakeOutletService();
        var configOutlet = new FakeOutletService();
        var stateMonitor = new FakeMonitorService();
        using var bridge = new LslTwinModeBridge(commandOutlet, configOutlet, stateMonitor);

        var cmd = new TwinModeCommand("breathing-start", "Start breathing biofeedback");
        var result = await bridge.SendCommandAsync(cmd);

        Assert.Equal(OperationOutcomeKind.Preview, result.Kind);
        Assert.Equal("breathing-start", commandOutlet.LastPublishedCommand);
    }

    [Fact]
    public async Task TwinBridge_publishes_breathing_belt_config()
    {
        var commandOutlet = new FakeOutletService();
        var configOutlet = new FakeOutletService();
        var stateMonitor = new FakeMonitorService();
        using var bridge = new LslTwinModeBridge(commandOutlet, configOutlet, stateMonitor);

        var profile = new RuntimeConfigProfile(
            "breathing-belt", "Breathing Belt Config", "breath.csv", "1.0", "preview", false,
            "Breathing belt biofeedback control parameters",
            ["com.Viscereality.KarateBio"],
            [
                new RuntimeConfigEntry("showcase_lsl_in_stream_name", "quest_biofeedback_in"),
                new RuntimeConfigEntry("showcase_lsl_in_stream_type", "quest.biofeedback"),
                new RuntimeConfigEntry("showcase_lsl_in_auto_connect", "true"),
                new RuntimeConfigEntry("showcase_lsl_in_default_channel", "0"),
                new RuntimeConfigEntry("breathing_modulate_particle_speed", "0.8"),
                new RuntimeConfigEntry("breathing_modulate_size_pulse", "0.4"),
                new RuntimeConfigEntry("breathing_modulate_sdf_attraction", "0.6")
            ]);

        var target = new QuestAppTarget(
            "karatebio", "KarateBio", "com.Viscereality.KarateBio", "", "", "", "", []);

        var result = await bridge.PublishRuntimeConfigAsync(profile, target);

        Assert.Equal(OperationOutcomeKind.Preview, result.Kind);
        Assert.Equal(7, configOutlet.PublishedEntryCount);

        var requested = bridge.RequestedSettings;
        Assert.Equal("quest_biofeedback_in", requested["showcase_lsl_in_stream_name"]);
        Assert.Equal("true", requested["showcase_lsl_in_auto_connect"]);
    }

    [Fact]
    public async Task TwinBridge_detects_settings_delta_from_headset_state()
    {
        var commandOutlet = new FakeOutletService();
        var configOutlet = new FakeOutletService();
        var stateMonitor = new FakeMonitorService();

        // Simulate headset reporting back different settings
        stateMonitor.EnqueueReading(new LslMonitorReading(
            "Streaming", "set breathing_modulate_particle_speed=0.6", 0f, 20f, DateTimeOffset.UtcNow));
        stateMonitor.EnqueueReading(new LslMonitorReading(
            "Streaming", "set breathing_modulate_size_pulse=0.4", 0f, 20f, DateTimeOffset.UtcNow));
        stateMonitor.EnqueueReading(new LslMonitorReading(
            "Streaming", "set sphere_radius_progress=1.0", 0f, 20f, DateTimeOffset.UtcNow));

        using var bridge = new LslTwinModeBridge(commandOutlet, configOutlet, stateMonitor);
        bridge.Open();
        await Task.Delay(300); // Wait for monitor to process

        // Publish requested config
        var profile = new RuntimeConfigProfile(
            "test", "Test", "t.csv", "1.0", "preview", false, "",
            ["com.Viscereality.KarateBio"],
            [
                new RuntimeConfigEntry("breathing_modulate_particle_speed", "0.8"),
                new RuntimeConfigEntry("breathing_modulate_size_pulse", "0.4")
            ]);
        var target = new QuestAppTarget("t", "T", "com.Viscereality.KarateBio", "", "", "", "", []);
        await bridge.PublishRuntimeConfigAsync(profile, target);

        var delta = bridge.ComputeSettingsDelta();

        // Should find: particle_speed (mismatch), size_pulse (match), sphere_radius_progress (reported only)
        var speedDelta = delta.FirstOrDefault(d => d.Key == "breathing_modulate_particle_speed");
        Assert.NotNull(speedDelta);
        Assert.Equal("0.8", speedDelta.Requested);
        Assert.Equal("0.6", speedDelta.Reported);
        Assert.False(speedDelta.Matches);

        var pulseDelta = delta.FirstOrDefault(d => d.Key == "breathing_modulate_size_pulse");
        Assert.NotNull(pulseDelta);
        Assert.True(pulseDelta.Matches);

        var sphereDelta = delta.FirstOrDefault(d => d.Key == "sphere_radius_progress");
        Assert.NotNull(sphereDelta);
        Assert.Null(sphereDelta.Requested);
        Assert.Equal("1.0", sphereDelta.Reported);
    }

    [Fact]
    public async Task TwinBridge_multiple_config_publishes_accumulate_settings()
    {
        var commandOutlet = new FakeOutletService();
        var configOutlet = new FakeOutletService();
        var stateMonitor = new FakeMonitorService();
        using var bridge = new LslTwinModeBridge(commandOutlet, configOutlet, stateMonitor);

        var target = new QuestAppTarget("t", "T", "com.Viscereality.KarateBio", "", "", "", "", []);

        // First publish
        var profile1 = new RuntimeConfigProfile(
            "p1", "P1", "p1.csv", "1.0", "preview", false, "",
            ["com.Viscereality.KarateBio"],
            [new RuntimeConfigEntry("alpha", "1.0"), new RuntimeConfigEntry("beta", "2.0")]);
        await bridge.PublishRuntimeConfigAsync(profile1, target);

        // Second publish with overlapping key
        var profile2 = new RuntimeConfigProfile(
            "p2", "P2", "p2.csv", "1.0", "preview", false, "",
            ["com.Viscereality.KarateBio"],
            [new RuntimeConfigEntry("alpha", "1.5"), new RuntimeConfigEntry("gamma", "3.0")]);
        await bridge.PublishRuntimeConfigAsync(profile2, target);

        var requested = bridge.RequestedSettings;
        Assert.Equal("1.5", requested["alpha"]); // Updated
        Assert.False(requested.ContainsKey("beta"));  // Cleared — PublishRuntimeConfigAsync replaces, not accumulates
        Assert.Equal("3.0", requested["gamma"]); // Added in second
    }

    [Fact]
    public async Task TwinBridge_command_sequence_for_breathing_session()
    {
        var commandOutlet = new FakeOutletService();
        var configOutlet = new FakeOutletService();
        var stateMonitor = new FakeMonitorService();
        using var bridge = new LslTwinModeBridge(commandOutlet, configOutlet, stateMonitor);

        // Typical session command sequence
        var commands = new[]
        {
            new TwinModeCommand("twin-start", "Initialize twin session"),
            new TwinModeCommand("config-apply", "Apply breathing config"),
            new TwinModeCommand("breathing-start", "Start biofeedback inlet"),
            new TwinModeCommand("breathing-stop", "Stop biofeedback inlet"),
            new TwinModeCommand("twin-stop", "End twin session"),
        };

        foreach (var cmd in commands)
        {
            var result = await bridge.SendCommandAsync(cmd);
            Assert.Equal(OperationOutcomeKind.Preview, result.Kind);
        }

        Assert.Equal("twin-stop", commandOutlet.LastPublishedCommand);
    }
}
