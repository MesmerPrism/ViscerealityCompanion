using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

/// <summary>
/// End-to-end session tests that exercise the full operator workflow:
/// probe → WiFi → connect → launch → configure → monitor → settings comparison.
/// </summary>
[Collection("QuestDevice")]
public class EndToEndSessionTests
{
    private readonly QuestDeviceFixture _device;
    private const string KarateBioPackage = "com.Viscereality.KarateBio";

    public EndToEndSessionTests(QuestDeviceFixture device) => _device = device;

    [Fact]
    public async Task Full_session_probe_connect_launch_status()
    {
        if (DeviceSkip.ShouldSkip) return;

        var service = QuestControlServiceFactory.CreateDefault();

        // Prefer direct WiFi connect if fixture established it
        if (_device.IsWifiConnected)
        {
            var connect = await service.ConnectAsync(_device.WifiEndpoint!);
            Assert.True(connect.Kind is OperationOutcomeKind.Success or OperationOutcomeKind.Warning);
        }
        else
        {
            var probe = await service.ProbeUsbAsync();
            Assert.True(probe.Kind is OperationOutcomeKind.Success or OperationOutcomeKind.Warning);

            if (probe.Kind == OperationOutcomeKind.Success && !string.IsNullOrEmpty(_device.DeviceIp))
            {
                await service.EnableWifiFromUsbAsync();
                await Task.Delay(1000);
                await service.ConnectAsync($"{_device.DeviceIp}:5555");
            }
        }

        // Launch KarateBio
        var target = new QuestAppTarget(
            "karatebio", "KarateBio", KarateBioPackage, "", "",
            "", "", []);

        var launch = await service.LaunchAppAsync(target);
        Assert.True(launch.Kind is OperationOutcomeKind.Success or OperationOutcomeKind.Warning);

        await Task.Delay(3000);

        // Step 5: Query status
        var status = await service.QueryHeadsetStatusAsync(target, false);
        Assert.True(status.IsConnected);
        Assert.True(status.BatteryLevel is >= 1 and <= 100);
    }

    [Fact]
    public async Task Twin_bridge_config_publish_and_delta_detection()
    {
        // Test the full config publish + settings comparison pipeline
        var stateMonitor = new FakeMonitorService();

        // Simulate headset reporting its current state
        stateMonitor.EnqueueSetting("internal_tick_lock_60hz", "true");
        stateMonitor.EnqueueSetting("performance_hint_cpu_level", "2");
        stateMonitor.EnqueueSetting("performance_hint_gpu_level", "2");
        stateMonitor.EnqueueSetting("study_watch_hotload_file_changes", "true");
        stateMonitor.EnqueueSetting("breathing_modulate_particle_speed", "0.0");

        var commandOutlet = new FakeOutletService();
        var configOutlet = new FakeOutletService();
        using var bridge = new LslTwinModeBridge(commandOutlet, configOutlet, stateMonitor);
        bridge.Open();
        await Task.Delay(300);

        // Operator changes settings (wants to enable breathing modulation)
        var profile = new RuntimeConfigProfile(
            "breathing-enabled", "Breathing Enabled", "breath.csv", "1.0", "preview", false,
            "Enable breathing biofeedback",
            [KarateBioPackage],
            [
                new RuntimeConfigEntry("internal_tick_lock_60hz", "true"),  // Same
                new RuntimeConfigEntry("performance_hint_cpu_level", "3"),  // Changed
                new RuntimeConfigEntry("breathing_modulate_particle_speed", "0.8"),  // Changed
                new RuntimeConfigEntry("breathing_modulate_size_pulse", "0.4")  // New
            ]);

        var target = new QuestAppTarget("k", "K", KarateBioPackage, "", "", "", "", []);
        await bridge.PublishRuntimeConfigAsync(profile, target);

        var delta = bridge.ComputeSettingsDelta();

        // Verify settings comparison
        var tickDelta = delta.First(d => d.Key == "internal_tick_lock_60hz");
        Assert.True(tickDelta.Matches); // Both true

        var cpuDelta = delta.First(d => d.Key == "performance_hint_cpu_level");
        Assert.False(cpuDelta.Matches); // Requested 3, reported 2

        var breathDelta = delta.First(d => d.Key == "breathing_modulate_particle_speed");
        Assert.False(breathDelta.Matches); // Requested 0.8, reported 0.0

        var pulseDelta = delta.First(d => d.Key == "breathing_modulate_size_pulse");
        Assert.False(pulseDelta.Matches); // Requested 0.4, not reported

        // Headset-only settings should also appear
        var watchDelta = delta.First(d => d.Key == "study_watch_hotload_file_changes");
        Assert.Null(watchDelta.Requested); // Not in our profile
        Assert.Equal("true", watchDelta.Reported);
    }

    [Fact]
    public async Task Catalog_loader_finds_apps_and_profiles()
    {
        var catalogRoot = ResolveCatalogRoot();
        if (catalogRoot is null) return;

        var catalog = await new QuestSessionKitCatalogLoader().LoadAsync(catalogRoot);

        Assert.True(catalog.Apps.Count > 0, "No apps in catalog");
        Assert.True(catalog.HotloadProfiles.Count >= 0);
        Assert.True(catalog.DeviceProfiles.Count >= 0);
    }

    [Fact]
    public async Task Performance_level_sweep()
    {
        if (DeviceSkip.ShouldSkip) return;

        var service = QuestControlServiceFactory.CreateDefault();

        if (_device.IsWifiConnected)
        {
            await service.ConnectAsync(_device.WifiEndpoint!);
        }
        else
        {
            var probe = await service.ProbeUsbAsync();
            if (probe.Kind == OperationOutcomeKind.Success && !string.IsNullOrEmpty(_device.DeviceIp))
            {
                await service.EnableWifiFromUsbAsync();
                await Task.Delay(500);
                await service.ConnectAsync($"{_device.DeviceIp}:5555");
            }
            else if (!string.IsNullOrEmpty(_device.DeviceIp))
            {
                await service.ConnectAsync($"{_device.DeviceIp}:5555");
            }
        }

        // Sweep through performance levels
        for (int cpu = 0; cpu <= 5; cpu += 2)
        {
            for (int gpu = 0; gpu <= 5; gpu += 2)
            {
                var result = await service.ApplyPerformanceLevelsAsync(cpu, gpu);
                Assert.True(result.Kind is OperationOutcomeKind.Success or OperationOutcomeKind.Warning);
            }
        }

        // Reset to moderate levels
        await service.ApplyPerformanceLevelsAsync(3, 2);
    }

    private static string? ResolveCatalogRoot()
    {
        var envRoot = Environment.GetEnvironmentVariable("VISCEREALITY_QUEST_SESSION_KIT_ROOT");
        if (!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot))
            return envRoot;

        var samples = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source", "repos", "ViscerealityCompanion", "samples", "quest-session-kit");
        if (Directory.Exists(samples))
            return samples;

        var questSessionKit = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source", "repos", "AstralKarateDojo", "QuestSessionKit");
        return Directory.Exists(questSessionKit) ? questSessionKit : null;
    }
}
