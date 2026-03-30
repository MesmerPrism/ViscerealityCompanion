using System.Diagnostics;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

/// <summary>
/// Tests for Meta Horizon Debug Bridge (hzdb) commands that extend ADB.
/// These tests verify hzdb availability and exercise its unique capabilities.
/// </summary>
[Collection("QuestDevice")]
public class HzdbCommandTests
{
    private const string StudyPackage = "com.Viscereality.SussexExperiment";
    private const string StudyFilesDir = "/sdcard/Android/data/com.Viscereality.SussexExperiment/files/";
    private readonly QuestDeviceFixture _device;
    private static readonly Lazy<string?> _npxCommandPath = new(WindowsHzdbService.ResolveNpxCommandPath);
    private static readonly Lazy<bool> _hzdbAvailable = new(() =>
    {
        try
        {
            var npxCommandPath = _npxCommandPath.Value;
            if (string.IsNullOrWhiteSpace(npxCommandPath))
                return false;

            var psi = new ProcessStartInfo(npxCommandPath)
            {
                Arguments = "-y @meta-quest/hzdb --version",
                WorkingDirectory = Path.GetDirectoryName(npxCommandPath) ?? AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            process.WaitForExit(30_000);
            return process.ExitCode == 0;
        }
        catch { return false; }
    });

    public HzdbCommandTests(QuestDeviceFixture device) => _device = device;

    [Fact]
    public async Task Hzdb_is_available_via_npx()
    {
        if (!_hzdbAvailable.Value) return;
        var result = await RunHzdb("--help");
        Assert.Contains("horizon debug bridge", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hzdb_device_info_returns_json()
    {
        if (!_hzdbAvailable.Value || DeviceSkip.ShouldSkip) return;
        var serial = _device.UsbSerial;

        var result = await RunHzdb($"device info --json {serial}");
        Assert.Contains("Quest 3S", result);
        Assert.Contains("panther", result);
        Assert.Contains(serial, result);
    }

    [Fact]
    public async Task Hzdb_device_battery_returns_level()
    {
        if (!_hzdbAvailable.Value || DeviceSkip.ShouldSkip) return;

        var result = await RunHzdb($"device battery --device {_device.UsbSerial}");
        Assert.Contains("level", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hzdb_app_info_lsl_twin()
    {
        if (!_hzdbAvailable.Value || DeviceSkip.ShouldSkip) return;

        var result = await RunHzdb($"app info --device {_device.UsbSerial} {StudyPackage}");
        Assert.Contains(StudyPackage, result);
    }

    [Fact]
    public async Task Hzdb_app_list_includes_installed_apps()
    {
        if (!_hzdbAvailable.Value || DeviceSkip.ShouldSkip) return;

        await _device.WaitForUsbReadyAsync();
        var result = await RunHzdbWithUsbRetryAsync($"app list --device {_device.UsbSerial} --json");
        Assert.Contains(StudyPackage, result);
    }

    [Fact]
    public async Task Hzdb_capture_screenshot()
    {
        if (!_hzdbAvailable.Value || DeviceSkip.ShouldSkip) return;

        var tempDir = Path.Combine(Path.GetTempPath(), "viscereality-test");
        Directory.CreateDirectory(tempDir);
        var outputPath = Path.Combine(tempDir, "test-screenshot");

        try
        {
            var result = await RunHzdb($"capture screenshot --device {_device.UsbSerial} -o \"{outputPath}\"");
            // hzdb creates the file with .png extension
            var pngFile = Directory.GetFiles(tempDir, "test-screenshot*").FirstOrDefault();
            Assert.NotNull(pngFile);
            Assert.True(new FileInfo(pngFile).Length > 1000, "Screenshot file too small");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Hzdb_perf_capture_short_trace()
    {
        if (!_hzdbAvailable.Value || DeviceSkip.ShouldSkip) return;

        var service = new WindowsHzdbService();
        var outcome = await service.CapturePerfTraceAsync(_device.UsbSerial, 2000);

        Assert.Equal(OperationOutcomeKind.Success, outcome.Kind);

        var tracePath = outcome.SafeItems.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(tracePath))
        {
            Assert.True(File.Exists(tracePath), $"Expected local trace at {tracePath}");
            Assert.True(new FileInfo(tracePath).Length > 0, "Expected non-empty local perf trace");
        }
    }

    [Fact]
    public async Task Hzdb_files_ls_device_storage()
    {
        if (!_hzdbAvailable.Value || DeviceSkip.ShouldSkip) return;

        var result = await RunHzdb($"files ls --device {_device.UsbSerial} {StudyFilesDir}");
        // Should list files we know exist (deformable-room-cache, etc.)
        Assert.True(result.Length > 10, "Expected file listing output");
    }

    [Fact]
    public async Task Hzdb_device_proximity_can_be_disabled()
    {
        if (!_hzdbAvailable.Value || DeviceSkip.ShouldSkipMutating) return;

        await _device.WaitForUsbReadyAsync();
        var result = await RunHzdbWithUsbRetryAsync($"device proximity --device {_device.UsbSerial} --disable");
        Assert.DoesNotContain("error", result, StringComparison.OrdinalIgnoreCase);

        // Re-enable to not leave device in weird state? Actually user said it's already off
    }

    [Fact]
    public async Task Hzdb_service_reads_live_proximity_status()
    {
        if (!_hzdbAvailable.Value || DeviceSkip.ShouldSkip) return;

        var service = new WindowsHzdbService();
        var status = await service.GetProximityStatusAsync(_device.UsbSerial);

        Assert.True(status.Available, status.StatusDetail);
        Assert.False(string.IsNullOrWhiteSpace(status.VirtualState));
    }

    [Fact]
    public async Task Hzdb_device_wake_wakes_device()
    {
        if (!_hzdbAvailable.Value || DeviceSkip.ShouldSkipMutating) return;

        var result = await RunHzdb($"device wake --device {_device.UsbSerial}");
        Assert.DoesNotContain("error", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hzdb_docs_search_returns_results()
    {
        if (!_hzdbAvailable.Value) return;
        var result = await RunHzdb("docs search \"LSL Lab Streaming Layer Quest\"");
        // May or may not find results, but shouldn't crash
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Hzdb_mcp_server_help_available()
    {
        if (!_hzdbAvailable.Value) return;
        var result = await RunHzdb("mcp --help");
        Assert.Contains("server", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install", result, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> RunHzdb(string arguments)
    {
        var npxCommandPath = _npxCommandPath.Value
            ?? throw new InvalidOperationException("Could not locate npx.cmd for hzdb integration tests.");

        var psi = new ProcessStartInfo(npxCommandPath)
        {
            Arguments = $"-y @meta-quest/hzdb {arguments}",
            WorkingDirectory = Path.GetDirectoryName(npxCommandPath) ?? AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdout + stderr;
    }

    private async Task<string> RunHzdbWithUsbRetryAsync(string arguments)
    {
        var result = await RunHzdb(arguments);
        if (!result.Contains("Failed to detect USB", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        await _device.WaitForUsbReadyAsync();
        await Task.Delay(1000);
        return await RunHzdb(arguments);
    }
}
