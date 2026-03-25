using System.Diagnostics;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public interface IHzdbService
{
    bool IsAvailable { get; }
    Task<OperationOutcome> CaptureScreenshotAsync(string deviceSerial, string outputPath, CancellationToken cancellationToken = default);
    Task<OperationOutcome> CapturePerfTraceAsync(string deviceSerial, int durationMs = 5000, CancellationToken cancellationToken = default);
    Task<OperationOutcome> SetProximityAsync(string deviceSerial, bool enabled, CancellationToken cancellationToken = default);
    Task<OperationOutcome> WakeDeviceAsync(string deviceSerial, CancellationToken cancellationToken = default);
    Task<OperationOutcome> GetDeviceInfoAsync(string deviceSerial, CancellationToken cancellationToken = default);
    Task<OperationOutcome> ListFilesAsync(string deviceSerial, string remotePath, CancellationToken cancellationToken = default);
    Task<OperationOutcome> PushFileAsync(string deviceSerial, string localPath, string remotePath, CancellationToken cancellationToken = default);
    Task<OperationOutcome> PullFileAsync(string deviceSerial, string remotePath, string localPath, CancellationToken cancellationToken = default);
}

public sealed class WindowsHzdbService : IHzdbService
{
    private readonly Lazy<bool> _available = new(() =>
    {
        try
        {
            var psi = new ProcessStartInfo("npx.cmd")
            {
                Arguments = "-y @meta-quest/hzdb --version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(30_000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    });

    public bool IsAvailable => _available.Value;

    public async Task<OperationOutcome> CaptureScreenshotAsync(string deviceSerial, string outputPath, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var result = await RunAsync($"capture screenshot -d {deviceSerial} -o \"{outputPath}\"", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Outcome(OperationOutcomeKind.Success, "Screenshot captured.", result.StdOut)
            : Outcome(OperationOutcomeKind.Failure, "Screenshot capture failed.", result.Combined);
    }

    public async Task<OperationOutcome> CapturePerfTraceAsync(string deviceSerial, int durationMs = 5000, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync($"perf capture -d {deviceSerial} --duration {durationMs}", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Outcome(OperationOutcomeKind.Success, $"Perf trace captured ({durationMs}ms).", result.StdOut)
            : Outcome(OperationOutcomeKind.Failure, "Perf capture failed.", result.Combined);
    }

    public async Task<OperationOutcome> SetProximityAsync(string deviceSerial, bool enabled, CancellationToken cancellationToken = default)
    {
        var flag = enabled ? "--enable" : "--disable";
        var result = await RunAsync($"device proximity -d {deviceSerial} {flag}", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Outcome(OperationOutcomeKind.Success, $"Proximity sensor {(enabled ? "enabled" : "disabled")}.", result.StdOut)
            : Outcome(OperationOutcomeKind.Failure, "Proximity toggle failed.", result.Combined);
    }

    public async Task<OperationOutcome> WakeDeviceAsync(string deviceSerial, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync($"device wake -d {deviceSerial}", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Outcome(OperationOutcomeKind.Success, "Device wake sent.", result.StdOut)
            : Outcome(OperationOutcomeKind.Failure, "Device wake failed.", result.Combined);
    }

    public async Task<OperationOutcome> GetDeviceInfoAsync(string deviceSerial, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync($"device info --json {deviceSerial}", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Outcome(OperationOutcomeKind.Success, "Device info retrieved.", result.StdOut)
            : Outcome(OperationOutcomeKind.Failure, "Device info failed.", result.Combined);
    }

    public async Task<OperationOutcome> ListFilesAsync(string deviceSerial, string remotePath, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync($"files ls -d {deviceSerial} {remotePath}", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? new OperationOutcome(OperationOutcomeKind.Success, $"Listed {remotePath}.", result.StdOut,
                Items: result.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToArray())
            : Outcome(OperationOutcomeKind.Failure, $"List failed for {remotePath}.", result.Combined);
    }

    public async Task<OperationOutcome> PushFileAsync(string deviceSerial, string localPath, string remotePath, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync($"files push -d {deviceSerial} \"{localPath}\" {remotePath}", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Outcome(OperationOutcomeKind.Success, $"Pushed {Path.GetFileName(localPath)} to device.", result.StdOut)
            : Outcome(OperationOutcomeKind.Failure, "File push failed.", result.Combined);
    }

    public async Task<OperationOutcome> PullFileAsync(string deviceSerial, string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var result = await RunAsync($"files pull -d {deviceSerial} {remotePath} \"{localPath}\"", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Outcome(OperationOutcomeKind.Success, $"Pulled {Path.GetFileName(remotePath)} from device.", result.StdOut)
            : Outcome(OperationOutcomeKind.Failure, "File pull failed.", result.Combined);
    }

    private static async Task<HzdbResult> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("npx.cmd")
        {
            Arguments = $"-y @meta-quest/hzdb {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start hzdb process.");

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new HzdbResult(process.ExitCode, stdout.Trim(), stderr.Trim());
    }

    private static OperationOutcome Outcome(OperationOutcomeKind kind, string summary, string detail)
        => new(kind, summary, string.IsNullOrWhiteSpace(detail) ? string.Empty : detail);

    private sealed record HzdbResult(int ExitCode, string StdOut, string StdErr)
    {
        public string Combined => string.IsNullOrWhiteSpace(StdErr) ? StdOut : $"{StdOut}\n{StdErr}";
    }
}

public sealed class PreviewHzdbService : IHzdbService
{
    public bool IsAvailable => false;

    public Task<OperationOutcome> CaptureScreenshotAsync(string deviceSerial, string outputPath, CancellationToken cancellationToken = default)
        => Preview("Screenshot capture requires hzdb (npx @meta-quest/hzdb).");

    public Task<OperationOutcome> CapturePerfTraceAsync(string deviceSerial, int durationMs = 5000, CancellationToken cancellationToken = default)
        => Preview("Perf capture requires hzdb (npx @meta-quest/hzdb).");

    public Task<OperationOutcome> SetProximityAsync(string deviceSerial, bool enabled, CancellationToken cancellationToken = default)
        => Preview("Proximity control requires hzdb (npx @meta-quest/hzdb).");

    public Task<OperationOutcome> WakeDeviceAsync(string deviceSerial, CancellationToken cancellationToken = default)
        => Preview("Device wake via hzdb requires hzdb (npx @meta-quest/hzdb).");

    public Task<OperationOutcome> GetDeviceInfoAsync(string deviceSerial, CancellationToken cancellationToken = default)
        => Preview("Device info requires hzdb (npx @meta-quest/hzdb).");

    public Task<OperationOutcome> ListFilesAsync(string deviceSerial, string remotePath, CancellationToken cancellationToken = default)
        => Preview("File listing requires hzdb (npx @meta-quest/hzdb).");

    public Task<OperationOutcome> PushFileAsync(string deviceSerial, string localPath, string remotePath, CancellationToken cancellationToken = default)
        => Preview("File push requires hzdb (npx @meta-quest/hzdb).");

    public Task<OperationOutcome> PullFileAsync(string deviceSerial, string remotePath, string localPath, CancellationToken cancellationToken = default)
        => Preview("File pull requires hzdb (npx @meta-quest/hzdb).");

    private static Task<OperationOutcome> Preview(string detail)
        => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Preview, "hzdb not available.", detail));
}

public static class HzdbServiceFactory
{
    public static IHzdbService CreateDefault()
    {
        var service = new WindowsHzdbService();
        return service.IsAvailable ? service : new PreviewHzdbService();
    }
}
