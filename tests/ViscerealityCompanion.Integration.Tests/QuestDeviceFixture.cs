using System.Diagnostics;
using ViscerealityCompanion.Core.Services;

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

        // Default test runs must stay read-only on live hardware.
        if (DeviceSkip.MutatingTestsEnabled && !string.IsNullOrEmpty(DeviceIp))
        {
            await RunAdb($"-s {_usbSerial} tcpip 5555");
            await WaitForDeviceStateAsync(_usbSerial, "device", TimeSpan.FromSeconds(15));
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
        if (!DeviceSkip.MutatingTestsEnabled)
            throw new InvalidOperationException($"Mutating Quest tests are disabled. Set {DeviceSkip.MutatingTestsOptInVariable}=1 to enable Wi-Fi bootstrap.");

        if (string.IsNullOrEmpty(DeviceIp))
            throw new InvalidOperationException("No device WiFi IP available.");

        await RunAdb($"-s {UsbSerial} tcpip 5555");
        await WaitForDeviceStateAsync(UsbSerial, "device", TimeSpan.FromSeconds(15));
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

    public async Task WaitForUsbReadyAsync(TimeSpan? timeout = null)
    {
        await WaitForDeviceStateAsync(UsbSerial, "device", timeout ?? TimeSpan.FromSeconds(15));
    }

    private async Task WaitForDeviceStateAsync(string selector, string expectedState, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = (await RunAdb($"-s {selector} get-state")).Trim();
            if (state.Equals(expectedState, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"ADB selector '{selector}' did not reach state '{expectedState}' within {timeout.TotalSeconds:0}s.");
    }

    private static string? LocateAdb()
        => AdbExecutableLocator.TryLocate();
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
    private const string MutatingTestsEnvVar = "VISCEREALITY_ENABLE_MUTATING_QUEST_TESTS";
    private static readonly Lazy<bool> _deviceAvailable = new(() =>
    {
        try
        {
            // Quick check: just run `adb devices` without WiFi setup
            var adb = AdbExecutableLocator.TryLocate();

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
    private static readonly Lazy<bool> _mutatingTestsEnabled = new(() =>
    {
        var raw = Environment.GetEnvironmentVariable(MutatingTestsEnvVar);
        return raw is not null
            && (raw.Equals("1", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("yes", StringComparison.OrdinalIgnoreCase));
    });

    public static string? WhenNoDevice => _deviceAvailable.Value ? null : "No Quest device connected";
    public static string MutatingTestsOptInVariable => MutatingTestsEnvVar;
    public static bool MutatingTestsEnabled => _mutatingTestsEnabled.Value;
    public static string? WhenMutatingTestsDisabled
        => MutatingTestsEnabled ? null : $"Set {MutatingTestsEnvVar}=1 to enable mutating Quest integration tests.";

    /// <summary>Call at the start of any test that needs a device. Returns true if the test should be skipped.</summary>
    public static bool ShouldSkip => !_deviceAvailable.Value;
    public static bool ShouldSkipMutating => ShouldSkip || !MutatingTestsEnabled;
}
