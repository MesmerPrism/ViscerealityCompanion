using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

/// <summary>
/// Tests for the config hotloading pipeline: push CSV profiles to Quest,
/// verify profile was received, test twin bridge config publish.
/// </summary>
[Collection("QuestDevice")]
public class ConfigHotloadTests
{
    private readonly QuestDeviceFixture _device;
    private const string KarateBioPackage = "com.Viscereality.KarateBio";
    private const string RemoteHotloadDir = "/sdcard/Android/data/com.Viscereality.KarateBio/files/runtime_hotload";
    private const string RemoteOverridesPath = RemoteHotloadDir + "/runtime_overrides.csv";

    public ConfigHotloadTests(QuestDeviceFixture device) => _device = device;

    [Fact]
    public async Task Push_showcase_profile_csv()
    {
        if (DeviceSkip.ShouldSkip) return;

        var profilePath = FindHotloadProfile("showcase_runtime_overrides.csv");
        if (profilePath is null) return;

        await _device.Shell($"mkdir -p {RemoteHotloadDir}");
        await _device.RunAdb($"-s {_device.ActiveSelector} push \"{profilePath}\" \"{RemoteOverridesPath}\"");

        var readBack = (await _device.Shell($"cat {RemoteOverridesPath}")).Trim();
        Assert.Contains("showcase", readBack);
    }

    [Fact]
    public async Task Push_baseline_profile_and_relaunch()
    {
        if (DeviceSkip.ShouldSkip) return;

        var profilePath = FindHotloadProfile("karatebio_baseline.csv");
        if (profilePath is null) return;

        await _device.Shell($"mkdir -p {RemoteHotloadDir}");
        await _device.RunAdb($"-s {_device.ActiveSelector} push \"{profilePath}\" \"{RemoteOverridesPath}\"");

        // Force-stop and relaunch KarateBio so it picks up the new profile
        await _device.Shell($"am force-stop {KarateBioPackage}");
        await Task.Delay(1000);
        await _device.Shell($"monkey -p {KarateBioPackage} -c android.intent.category.LAUNCHER 1");
        await Task.Delay(5000);

        // Verify app is running
        var fgOutput = await _device.Shell("dumpsys activity activities");
        Assert.Contains(KarateBioPackage, fgOutput);
    }

    [Fact]
    public async Task Clear_runtime_overrides_restores_defaults()
    {
        if (DeviceSkip.ShouldSkip) return;

        await _device.Shell($"rm -f {RemoteOverridesPath}");
        var ls = await _device.Shell($"ls {RemoteOverridesPath} 2>/dev/null");
        Assert.DoesNotContain("runtime_overrides.csv", ls);
    }

    [Fact]
    public async Task Push_custom_csv_profile_with_specific_settings()
    {
        if (DeviceSkip.ShouldSkip) return;

        var customCsv = """
        key,value
        hotload_profile_id,integration-test
        internal_tick_lock_60hz,true
        performance_hint_cpu_level,3
        performance_hint_gpu_level,4
        study_watch_hotload_file_changes,true
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, customCsv);
            await _device.Shell($"mkdir -p {RemoteHotloadDir}");
            await _device.RunAdb($"-s {_device.ActiveSelector} push \"{tempFile}\" \"{RemoteOverridesPath}\"");

            var readBack = (await _device.Shell($"cat {RemoteOverridesPath}")).Trim();
            Assert.Contains("integration-test", readBack);
            Assert.Contains("performance_hint_cpu_level", readBack);
        }
        finally
        {
            File.Delete(tempFile);
            await _device.Shell($"rm -f {RemoteOverridesPath}");
        }
    }

    [Fact]
    public async Task TwinBridge_publishes_config_snapshot_via_lsl()
    {
        if (DeviceSkip.ShouldSkip) return;

        var commandOutlet = new FakeOutletService();
        var configOutlet = new FakeOutletService();
        var stateMonitor = new FakeMonitorService();
        using var bridge = new LslTwinModeBridge(commandOutlet, configOutlet, stateMonitor);

        var profile = new RuntimeConfigProfile(
            "karatebio_baseline", "KarateBio Baseline", "karatebio_baseline.csv", "1.0", "release", false,
            "Standard baseline config",
            [KarateBioPackage],
            [
                new RuntimeConfigEntry("internal_tick_lock_60hz", "true"),
                new RuntimeConfigEntry("performance_hint_cpu_level", "2"),
                new RuntimeConfigEntry("performance_hint_gpu_level", "2"),
                new RuntimeConfigEntry("study_watch_hotload_file_changes", "true")
            ]);

        var target = new QuestAppTarget(
            "karatebio", "KarateBio", KarateBioPackage, "", "", "", "", []);

        var result = await bridge.PublishRuntimeConfigAsync(profile, target);

        Assert.Equal(OperationOutcomeKind.Preview, result.Kind);
        Assert.Equal(4, configOutlet.PublishedEntryCount);

        var requested = bridge.RequestedSettings;
        Assert.Equal("true", requested["internal_tick_lock_60hz"]);
        Assert.Equal("2", requested["performance_hint_cpu_level"]);
    }

    private static string? FindHotloadProfile(string name)
    {
        var profileDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source", "repos", "AstralKarateDojo", "QuestSessionKit", "HotloadProfiles");

        var path = Path.Combine(profileDir, name);
        return File.Exists(path) ? path : null;
    }
}
