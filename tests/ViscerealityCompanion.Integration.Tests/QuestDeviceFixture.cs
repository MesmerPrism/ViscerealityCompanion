using System.Diagnostics;

namespace ViscerealityCompanion.Integration.Tests;

/// <summary>
/// Shared infrastructure for integration tests that require a connected Quest device.
/// Tests using this fixture are skipped when no device is connected.
/// </summary>
public sealed class QuestDeviceFixture : IAsyncLifetime
{
    private string? _adbPath;
    private string? _usbSerial;
    private string? _wifiEndpoint;

    public string AdbPath => _adbPath ?? throw new InvalidOperationException("ADB not found.");
    public string UsbSerial => _usbSerial ?? throw new InvalidOperationException("No USB device.");
    public string? WifiEndpoint => _wifiEndpoint;
    public string ActiveSelector => _wifiEndpoint ?? _usbSerial ?? throw new InvalidOperationException("No device.");
    public bool IsWifiConnected => _wifiEndpoint is not null;
    public string DeviceIp { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _adbPath = LocateAdb();
        if (_adbPath is null)
            return;

        var devicesOutput = await RunAdb("devices -l");
        var deviceLine = devicesOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.Contains("device") && !l.StartsWith("List"));

        if (deviceLine is null)
            return;

        _usbSerial = deviceLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];

        // Try to get WiFi IP
        var ipOutput = await RunAdb($"-s {_usbSerial} shell ip addr show wlan0");
        var inetLine = ipOutput.Split('\n').FirstOrDefault(l => l.Contains("inet "));
        if (inetLine is not null)
        {
            var parts = inetLine.Trim().Split(' ');
            if (parts.Length >= 2)
            {
                DeviceIp = parts[1].Split('/')[0];
            }
        }

        // Establish WiFi ADB early so all tests use a stable connection
        if (!string.IsNullOrEmpty(DeviceIp))
        {
            await RunAdb($"-s {_usbSerial} tcpip 5555");
            await Task.Delay(1500);
            var connectResult = await RunAdb($"connect {DeviceIp}:5555");
            if (connectResult.Contains("connected"))
                _wifiEndpoint = $"{DeviceIp}:5555";
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public bool IsDeviceAvailable => _usbSerial is not null;

    public async Task EnableWifiAndConnect()
    {
        if (string.IsNullOrEmpty(DeviceIp))
            throw new InvalidOperationException("No device WiFi IP available.");

        await RunAdb($"-s {_usbSerial} tcpip 5555");
        await Task.Delay(1000); // Allow ADB to restart in TCP mode

        var connectResult = await RunAdb($"connect {DeviceIp}:5555");
        if (connectResult.Contains("connected"))
            _wifiEndpoint = $"{DeviceIp}:5555";
    }

    public async Task<string> RunAdb(string arguments)
    {
        var psi = new ProcessStartInfo(_adbPath!, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdout;
    }

    public async Task<string> Shell(string command)
    {
        return await RunAdb($"-s {ActiveSelector} shell {command}");
    }

    private static string? LocateAdb()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
            @"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe",
            @"C:\Program Files\Unity\Hub\Editor\6000.2.7f2\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe",
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        // Check PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? [];
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, "adb.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}

/// <summary>
/// Trait to mark tests that require a connected Quest device.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequiresQuestAttribute : Attribute;

/// <summary>
/// Collection definition to serialize all Quest-device-dependent test classes.
/// </summary>
[CollectionDefinition("QuestDevice")]
public class QuestDeviceCollection : ICollectionFixture<QuestDeviceFixture>;

/// <summary>
/// Skip-condition for tests needing a connected Quest device.
/// </summary>
public static class DeviceSkip
{
    private static readonly Lazy<bool> _deviceAvailable = new(() =>
    {
        try
        {
            // Quick check: just run `adb devices` without WiFi setup
            string[] candidates =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
                @"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe",
                @"C:\Program Files\Unity\Hub\Editor\6000.2.7f2\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe",
            ];

            var adb = candidates.FirstOrDefault(File.Exists);
            if (adb is null)
            {
                var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? [];
                adb = pathDirs.Select(d => Path.Combine(d, "adb.exe")).FirstOrDefault(File.Exists);
            }

            if (adb is null) return false;

            var psi = new System.Diagnostics.ProcessStartInfo(adb, "devices")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output.Split('\n').Any(l => l.Contains("device") && !l.StartsWith("List"));
        }
        catch
        {
            return false;
        }
    });

    public static string? WhenNoDevice => _deviceAvailable.Value ? null : "No Quest device connected";

    /// <summary>Call at the start of any test that needs a device. Returns true if the test should be skipped.</summary>
    public static bool ShouldSkip => !_deviceAvailable.Value;
}
