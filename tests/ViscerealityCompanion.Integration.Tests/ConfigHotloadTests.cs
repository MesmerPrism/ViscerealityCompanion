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
    private const string StudyPackage = "com.Viscereality.LslTwin";
    private const string StudyLaunchComponent = "com.Viscereality.LslTwin/com.unity3d.player.UnityPlayerGameActivity";
    private const string RemoteHotloadDir = "/sdcard/Android/data/com.Viscereality.LslTwin/files/runtime_hotload";
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

        // Force-stop and relaunch the public LslTwin runtime so it picks up the new profile.
        await _device.Shell($"am force-stop {StudyPackage}");
        await Task.Delay(1000);
        var launchOutput = await _device.Shell($"am start -n {StudyLaunchComponent}");
        var surfaced = await WaitForPackageToSurfaceAsync(StudyPackage, TimeSpan.FromSeconds(15));

        Assert.True(
            surfaced || LaunchWasAccepted(launchOutput),
            $"Expected {StudyPackage} to surface or the ActivityManager launch request to be accepted. Output: {launchOutput}");
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
            [StudyPackage],
            [
                new RuntimeConfigEntry("internal_tick_lock_60hz", "true"),
                new RuntimeConfigEntry("performance_hint_cpu_level", "2"),
                new RuntimeConfigEntry("performance_hint_gpu_level", "2"),
                new RuntimeConfigEntry("study_watch_hotload_file_changes", "true")
            ]);

        var target = new QuestAppTarget(
            "sussex-lsltwin", "LslTwin", StudyPackage, "", "", "", "", []);

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

    private async Task<bool> WaitForPackageToSurfaceAsync(string packageId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var pidOutput = (await _device.Shell($"pidof {packageId}")).Trim();
            if (!string.IsNullOrWhiteSpace(pidOutput))
            {
                return true;
            }

            var windowOutput = await _device.Shell("dumpsys window windows");
            if (windowOutput.Contains(packageId, StringComparison.Ordinal))
            {
                return true;
            }

            await Task.Delay(1000);
        }

        return false;
    }

    private static bool LaunchWasAccepted(string launchOutput)
        => !string.IsNullOrWhiteSpace(launchOutput)
            && !launchOutput.Contains("Error:", StringComparison.OrdinalIgnoreCase)
            && launchOutput.Contains("Starting:", StringComparison.OrdinalIgnoreCase);
}
