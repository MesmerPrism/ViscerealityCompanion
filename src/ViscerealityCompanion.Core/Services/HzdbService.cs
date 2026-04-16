using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public interface IHzdbService
{
    bool IsAvailable { get; }
    Task<OperationOutcome> CaptureScreenshotAsync(string deviceSerial, string outputPath, string? method = null, CancellationToken cancellationToken = default);
    Task<OperationOutcome> CapturePerfTraceAsync(string deviceSerial, int durationMs = 5000, CancellationToken cancellationToken = default);
    Task<OperationOutcome> SetProximityAsync(string deviceSerial, bool enabled, int? durationMs = null, CancellationToken cancellationToken = default);
    Task<QuestProximityStatus> GetProximityStatusAsync(string deviceSerial, CancellationToken cancellationToken = default);
    Task<OperationOutcome> WakeDeviceAsync(string deviceSerial, CancellationToken cancellationToken = default);
    Task<OperationOutcome> GetDeviceInfoAsync(string deviceSerial, CancellationToken cancellationToken = default);
    Task<OperationOutcome> ListFilesAsync(string deviceSerial, string remotePath, CancellationToken cancellationToken = default);
    Task<OperationOutcome> PushFileAsync(string deviceSerial, string localPath, string remotePath, CancellationToken cancellationToken = default);
    Task<OperationOutcome> PullFileAsync(string deviceSerial, string remotePath, string localPath, CancellationToken cancellationToken = default);
}

public sealed class WindowsHzdbService : IHzdbService
{
    private const string HzdbPerfCaptureFormatPanicMarker = "Mismatch between definition and access of `format`";
    private static readonly Regex VrPowerManagerVirtualStateRegex = new(@"Virtual proximity state:\s*(.+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VrPowerManagerAutosleepDisabledRegex = new(@"isAutosleepDisabled:\s*(true|false)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VrPowerManagerAutoSleepTimeRegex = new(@"AutoSleepTime:\s*(\d+)\s*ms", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VrPowerManagerHeadsetStateRegex = new(@"State:\s*(.+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VrPowerManagerBroadcastRegex = new(
        @"^\s*\d+(?:\.\d+)?s \(([\d\.]+)s ago\) - received com\.oculus\.vrpowermanager\.(prox_close|automation_disable) broadcast: duration=(\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private readonly Lazy<string?> _commandPath;
    private readonly Lazy<bool> _available;

    public WindowsHzdbService()
    {
        _commandPath = new Lazy<string?>(ResolveHzdbCommandPath);
        _available = new Lazy<bool>(ProbeAvailability);
    }

    public bool IsAvailable => _available.Value;

    public async Task<OperationOutcome> CaptureScreenshotAsync(string deviceSerial, string outputPath, string? method = null, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var normalizedMethod = NormalizeScreenshotMethod(method);
        if (ShouldPreferAdbScreenshot(normalizedMethod))
        {
            var adbOutcome = await CaptureScreenshotViaAdbAsync(deviceSerial, outputPath, cancellationToken).ConfigureAwait(false);
            if (adbOutcome.Kind == OperationOutcomeKind.Success)
            {
                return adbOutcome;
            }

            var hzdbOutcome = await CaptureScreenshotViaHzdbAsync(
                    deviceSerial,
                    outputPath,
                    string.IsNullOrWhiteSpace(normalizedMethod) ? "screencap" : normalizedMethod,
                    cancellationToken)
                .ConfigureAwait(false);
            return hzdbOutcome.Kind == OperationOutcomeKind.Success
                ? hzdbOutcome with
                {
                    Detail = string.IsNullOrWhiteSpace(adbOutcome.Detail)
                        ? hzdbOutcome.Detail
                        : $"{adbOutcome.Detail} Fell back to hzdb screenshot capture and succeeded."
                }
                : Outcome(
                    OperationOutcomeKind.Failure,
                    "Screenshot capture failed.",
                    $"{adbOutcome.Detail}\n{hzdbOutcome.Detail}".Trim());
        }

        return await CaptureScreenshotViaHzdbAsync(deviceSerial, outputPath, normalizedMethod, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationOutcome> CapturePerfTraceAsync(string deviceSerial, int durationMs = 5000, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync($"perf capture --device \"{deviceSerial}\" --duration {durationMs}", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 && LooksLikeHzdbPerfCaptureFormatPanic(result.Combined))
        {
            return await CapturePerfTraceViaAdbFallbackAsync(deviceSerial, durationMs, result.Combined, cancellationToken).ConfigureAwait(false);
        }

        return result.ExitCode == 0
            ? Outcome(OperationOutcomeKind.Success, $"Perf trace captured ({durationMs}ms).", result.StdOut)
            : Outcome(OperationOutcomeKind.Failure, "Perf capture failed.", result.Combined);
    }

    public async Task<OperationOutcome> SetProximityAsync(string deviceSerial, bool enabled, int? durationMs = null, CancellationToken cancellationToken = default)
    {
        var flag = enabled ? "--enable" : "--disable";
        var durationArg = !enabled && durationMs.HasValue && durationMs.Value > 0
            ? $" --duration-ms {durationMs.Value}"
            : string.Empty;
        var result = await RunAsync($"device proximity --device \"{deviceSerial}\" {flag}{durationArg}", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Outcome(
                OperationOutcomeKind.Success,
                enabled
                    ? "Proximity sensor enabled."
                    : durationMs.HasValue && durationMs.Value > 0
                        ? $"Proximity sensor disabled for {durationMs.Value / 1000d / 3600d:0.#}h."
                        : "Proximity sensor disabled.",
                result.StdOut)
            : Outcome(OperationOutcomeKind.Failure, "Proximity toggle failed.", result.Combined);
    }

    public async Task<QuestProximityStatus> GetProximityStatusAsync(string deviceSerial, CancellationToken cancellationToken = default)
    {
        var adbPath = AdbExecutableLocator.TryLocate();
        var observedAtUtc = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(adbPath))
        {
            return new QuestProximityStatus(
                Available: false,
                HoldActive: false,
                VirtualState: string.Empty,
                IsAutosleepDisabled: false,
                HeadsetState: string.Empty,
                AutoSleepTimeMs: null,
                RetrievedAtUtc: observedAtUtc,
                HoldUntilUtc: null,
                StatusDetail: "adb.exe could not be located for Quest vrpowermanager readback.");
        }

        var result = await RunAdbAsync(
            adbPath,
            ["-s", deviceSerial, "shell", "dumpsys", "vrpowermanager"],
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return new QuestProximityStatus(
                Available: false,
                HoldActive: false,
                VirtualState: string.Empty,
                IsAutosleepDisabled: false,
                HeadsetState: string.Empty,
                AutoSleepTimeMs: null,
                RetrievedAtUtc: observedAtUtc,
                HoldUntilUtc: null,
                StatusDetail: string.IsNullOrWhiteSpace(result.Combined)
                    ? "Quest vrpowermanager readback failed."
                    : result.Combined);
        }

        return TryParseQuestProximityStatus(result.StdOut, observedAtUtc, out var status)
            ? status
            : new QuestProximityStatus(
                Available: false,
                HoldActive: false,
                VirtualState: string.Empty,
                IsAutosleepDisabled: false,
                HeadsetState: string.Empty,
                AutoSleepTimeMs: null,
                RetrievedAtUtc: observedAtUtc,
                HoldUntilUtc: null,
                StatusDetail: "Quest vrpowermanager output did not contain a recognizable virtual proximity state.");
    }

    public async Task<OperationOutcome> WakeDeviceAsync(string deviceSerial, CancellationToken cancellationToken = default)
    {
        var wakeResult = await RunAsync($"device wake --device \"{deviceSerial}\"", cancellationToken).ConfigureAwait(false);
        if (wakeResult.ExitCode != 0)
        {
            return Outcome(OperationOutcomeKind.Failure, "Device wake failed.", wakeResult.Combined);
        }

        var proximityResult = await RunAsync(
                $"device proximity --device \"{deviceSerial}\" --disable --duration-ms 60000",
                cancellationToken)
            .ConfigureAwait(false);

        await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken).ConfigureAwait(false);
        var wakeValidation = await ValidateWakeStateAsync(deviceSerial, cancellationToken).ConfigureAwait(false);

        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(wakeResult.StdOut))
        {
            detailParts.Add(wakeResult.StdOut.Trim());
        }

        if (proximityResult.ExitCode == 0)
        {
            if (!string.IsNullOrWhiteSpace(proximityResult.StdOut))
            {
                detailParts.Add(proximityResult.StdOut.Trim());
            }
        }
        else
        {
            detailParts.Add($"Keep-awake proximity override failed after wake: {proximityResult.Combined}".Trim());
        }

        if (!string.IsNullOrWhiteSpace(wakeValidation.Detail))
        {
            detailParts.Add(wakeValidation.Detail);
        }

        var detail = string.Join(Environment.NewLine, detailParts.Where(static part => !string.IsNullOrWhiteSpace(part)));
        if (!wakeValidation.Ready)
        {
            return Outcome(OperationOutcomeKind.Warning, wakeValidation.Summary, detail);
        }

        return Outcome(
            proximityResult.ExitCode == 0 ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning,
            proximityResult.ExitCode == 0
                ? "Device wake sequence sent and Quest reports a usable scene."
                : "Device wake sequence reached a usable scene, but the keep-awake proximity override failed.",
            detail);
    }

    internal sealed record QuestWakeValidation(bool Ready, string Summary, string Detail);

    internal static QuestWakeValidation EvaluateWakeState(string powerOutput, string foregroundOutput)
    {
        var powerStatus = WindowsAdbQuestControlService.ParseQuestPowerStatus(powerOutput);
        var snapshot = AdbShellSupport.ParseForegroundSnapshot(foregroundOutput);
        var foregroundComponent = snapshot?.PrimaryComponent ?? string.Empty;

        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(powerStatus.Detail))
        {
            detailParts.Add($"Power readback: {powerStatus.Detail}.");
        }

        if (!string.IsNullOrWhiteSpace(foregroundComponent))
        {
            detailParts.Add($"Foreground: {foregroundComponent}.");
        }

        if (powerStatus.IsAwake == false)
        {
            detailParts.Add("Quest still reports asleep after the wake request.");
            return new QuestWakeValidation(
                false,
                "Device wake request sent, but Quest still reports asleep.",
                string.Join(" ", detailParts));
        }

        if (IsSensorLockComponent(foregroundComponent))
        {
            detailParts.Add("Quest woke into SensorLockActivity instead of a usable scene.");
            return new QuestWakeValidation(
                false,
                "Device wake request sent, but Quest is still at the lock screen.",
                string.Join(" ", detailParts));
        }

        if (IsGuardianOrClearComponent(foregroundComponent))
        {
            detailParts.Add("Quest is still blocked by Guardian, tracking loss, or ClearActivity.");
            return new QuestWakeValidation(
                false,
                "Device wake request sent, but Quest is still blocked by a Meta visual blocker.",
                string.Join(" ", detailParts));
        }

        if (powerStatus.IsAwake == true && string.IsNullOrWhiteSpace(foregroundComponent))
        {
            detailParts.Add("Quest reports awake, but foreground verification was unavailable.");
            return new QuestWakeValidation(
                false,
                "Device wake request sent, but foreground verification is unavailable.",
                string.Join(" ", detailParts));
        }

        detailParts.Add("Quest reports awake and foreground is not blocked.");
        return new QuestWakeValidation(
            true,
            "Device wake sequence sent and Quest reports a usable scene.",
            string.Join(" ", detailParts));
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
        var result = await RunAsync($"files ls --device \"{deviceSerial}\" \"{remotePath}\"", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? new OperationOutcome(OperationOutcomeKind.Success, $"Listed {remotePath}.", result.StdOut,
                Items: result.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToArray())
            : Outcome(OperationOutcomeKind.Failure, $"List failed for {remotePath}.", result.Combined);
    }

    public async Task<OperationOutcome> PushFileAsync(string deviceSerial, string localPath, string remotePath, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync($"files push --device \"{deviceSerial}\" \"{localPath}\" \"{remotePath}\"", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Outcome(OperationOutcomeKind.Success, $"Pushed {Path.GetFileName(localPath)} to device.", result.StdOut)
            : Outcome(OperationOutcomeKind.Failure, "File push failed.", result.Combined);
    }

    public async Task<OperationOutcome> PullFileAsync(string deviceSerial, string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var result = await RunAsync($"files pull --device \"{deviceSerial}\" \"{remotePath}\" \"{localPath}\"", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Outcome(OperationOutcomeKind.Success, $"Pulled {Path.GetFileName(remotePath)} from device.", result.StdOut)
            : Outcome(OperationOutcomeKind.Failure, "File pull failed.", result.Combined);
    }

    private async Task<QuestWakeValidation> ValidateWakeStateAsync(string deviceSerial, CancellationToken cancellationToken)
    {
        var adbPath = AdbExecutableLocator.TryLocate();
        if (string.IsNullOrWhiteSpace(adbPath))
        {
            return new QuestWakeValidation(
                false,
                "Device wake request sent, but adb.exe is unavailable for readback.",
                "adb.exe could not be located, so the companion could not verify the post-wake headset state.");
        }

        var powerResult = await RunAdbAsync(
                adbPath,
                ["-s", deviceSerial, "shell", "dumpsys", "power"],
                cancellationToken)
            .ConfigureAwait(false);
        var foregroundResult = await RunAdbAsync(
                adbPath,
                ["-s", deviceSerial, "shell", "dumpsys", "activity", "activities"],
                cancellationToken)
            .ConfigureAwait(false);

        if (powerResult.ExitCode != 0)
        {
            return new QuestWakeValidation(
                false,
                "Device wake request sent, but power-state verification failed.",
                string.IsNullOrWhiteSpace(powerResult.Combined)
                    ? "Quest power-state readback failed after the wake request."
                    : powerResult.Combined);
        }

        var combinedForeground = foregroundResult.ExitCode == 0
            ? foregroundResult.StdOut
            : string.Empty;
        return EvaluateWakeState(powerResult.StdOut, combinedForeground);
    }

    internal static string? ResolveHzdbCommandPath()
        => ResolveCommandPath(EnumerateHzdbExecutableCandidates(), File.Exists)
           ?? ResolveCommandPath(EnumerateNpxCommandCandidates(), File.Exists);

    internal static string? ResolveCommandPath(IEnumerable<string?> candidatePaths, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(candidatePaths);
        ArgumentNullException.ThrowIfNull(fileExists);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawCandidate in candidatePaths)
        {
            var candidate = NormalizeCandidate(rawCandidate);
            if (candidate is null || !seen.Add(candidate))
                continue;

            if (fileExists(candidate))
                return candidate;
        }

        return null;
    }

    internal static string? ResolveNpxCommandPath()
        => ResolveCommandPath(EnumerateNpxCommandCandidates(), File.Exists);

    internal static bool ShouldPreferAdbScreenshot(string? method)
        => string.IsNullOrWhiteSpace(method) ||
           string.Equals(method.Trim().Trim('"'), "screencap", StringComparison.OrdinalIgnoreCase);

    internal static string? ResolveNpxCommandPath(IEnumerable<string?> candidatePaths, Func<string, bool> fileExists)
        => ResolveCommandPath(candidatePaths, fileExists);

    private bool ProbeAvailability()
    {
        try
        {
            using var process = Process.Start(CreateProcessStartInfo("--version"));
            if (process is null)
                return false;
            if (!process.WaitForExit(30_000))
            {
                TryKill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryParseQuestProximityStatus(string rawOutput, DateTimeOffset observedAtUtc, out QuestProximityStatus status)
    {
        status = new QuestProximityStatus(
            Available: false,
            HoldActive: false,
            VirtualState: string.Empty,
            IsAutosleepDisabled: false,
            HeadsetState: string.Empty,
            AutoSleepTimeMs: null,
            RetrievedAtUtc: observedAtUtc,
            HoldUntilUtc: null,
            StatusDetail: "Quest vrpowermanager output did not contain a recognizable virtual proximity state.");

        if (string.IsNullOrWhiteSpace(rawOutput))
            return false;

        var virtualState = MatchValue(VrPowerManagerVirtualStateRegex, rawOutput);
        if (string.IsNullOrWhiteSpace(virtualState))
            return false;

        var headsetState = MatchValue(VrPowerManagerHeadsetStateRegex, rawOutput) ?? string.Empty;
        var autosleepDisabled = bool.TryParse(MatchValue(VrPowerManagerAutosleepDisabledRegex, rawOutput), out var autosleepFlag) && autosleepFlag;
        var autoSleepTimeMs = int.TryParse(MatchValue(VrPowerManagerAutoSleepTimeRegex, rawOutput), NumberStyles.Integer, CultureInfo.InvariantCulture, out var autoSleepMs)
            ? autoSleepMs
            : (int?)null;
        var latestBroadcast = TryParseLatestBroadcast(rawOutput);
        var virtualCloseActive = string.Equals(virtualState, "CLOSE", StringComparison.OrdinalIgnoreCase);
        var holdUntilUtc = latestBroadcast is { Action: "prox_close", DurationMs: > 0 } broadcast && TryComputeHoldUntilUtc(broadcast, observedAtUtc, out var parsedHoldUntilUtc)
            ? (DateTimeOffset?)parsedHoldUntilUtc
            : null;

        status = new QuestProximityStatus(
            Available: true,
            HoldActive: virtualCloseActive,
            VirtualState: virtualState,
            IsAutosleepDisabled: autosleepDisabled,
            HeadsetState: headsetState,
            AutoSleepTimeMs: autoSleepTimeMs,
            RetrievedAtUtc: observedAtUtc,
            HoldUntilUtc: holdUntilUtc,
            StatusDetail: "Read from adb shell dumpsys vrpowermanager.",
            LastBroadcastAction: latestBroadcast?.Action ?? string.Empty,
            LastBroadcastDurationMs: latestBroadcast?.DurationMs,
            LastBroadcastAgeSeconds: latestBroadcast?.AgeSeconds);
        return true;
    }

    private static bool IsSensorLockComponent(string? component)
        => !string.IsNullOrWhiteSpace(component) &&
           component.Contains("SensorLockActivity", StringComparison.OrdinalIgnoreCase);

    private static bool IsGuardianOrClearComponent(string? component)
        => !string.IsNullOrWhiteSpace(component) &&
           (component.Contains("GuardianDialogActivity", StringComparison.OrdinalIgnoreCase) ||
            component.Contains("com.oculus.guardian", StringComparison.OrdinalIgnoreCase) ||
            component.Contains("ClearActivity", StringComparison.OrdinalIgnoreCase) ||
            component.Contains("com.oculus.os.clearactivity", StringComparison.OrdinalIgnoreCase));

    private async Task<ProcessResult> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = Process.Start(CreateProcessStartInfo(arguments))
            ?? throw new InvalidOperationException("Failed to start hzdb process.");

        try
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new ProcessResult(process.ExitCode, stdout.Trim(), stderr.Trim());
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private async Task<OperationOutcome> CaptureScreenshotViaHzdbAsync(
        string deviceSerial,
        string outputPath,
        string? method,
        CancellationToken cancellationToken)
    {
        var methodArg = string.IsNullOrWhiteSpace(method)
            ? string.Empty
            : $" --method \"{method}\"";
        var result = await RunAsync($"capture screenshot --device \"{deviceSerial}\"{methodArg} -o \"{outputPath}\"", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0 && File.Exists(outputPath))
        {
            return Outcome(OperationOutcomeKind.Success, "Screenshot captured.", result.StdOut);
        }

        TryDelete(outputPath);
        return Outcome(OperationOutcomeKind.Failure, "Screenshot capture failed.", result.Combined);
    }

    private ProcessStartInfo CreateProcessStartInfo(string arguments)
    {
        var commandPath = _commandPath.Value;
        if (string.IsNullOrWhiteSpace(commandPath))
            throw new FileNotFoundException("Could not locate a managed hzdb.exe or npx.cmd for hzdb.", "hzdb.exe");

        if (string.Equals(Path.GetFileName(commandPath), "npx.cmd", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo(commandPath)
            {
                Arguments = $"-y @meta-quest/hzdb {arguments}",
                WorkingDirectory = Path.GetDirectoryName(commandPath) ?? AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        return new ProcessStartInfo(commandPath)
        {
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(commandPath) ?? AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static bool LooksLikeHzdbPerfCaptureFormatPanic(string detail)
        => detail.Contains(HzdbPerfCaptureFormatPanicMarker, StringComparison.OrdinalIgnoreCase);

    private static async Task<ProcessResult> RunAdbAsync(string adbPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = adbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start adb process.");

        try
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new ProcessResult(process.ExitCode, stdout.Trim(), stderr.Trim());
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task<OperationOutcome> CaptureScreenshotViaAdbAsync(
        string deviceSerial,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var adbPath = AdbExecutableLocator.TryLocate();
        if (string.IsNullOrWhiteSpace(adbPath))
        {
            return Outcome(
                OperationOutcomeKind.Failure,
                "Screenshot capture failed.",
                "adb.exe could not be located for raw Quest screencap capture.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = adbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in new[] { "-s", deviceSerial, "exec-out", "screencap", "-p" })
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await process.StandardOutput.BaseStream.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var stderr = (await stderrTask.ConfigureAwait(false)).Trim();
            var fileInfo = new FileInfo(outputPath);
            if (process.ExitCode == 0 && fileInfo.Exists && fileInfo.Length > 0)
            {
                return new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Screenshot captured.",
                    $"Captured a raw Quest frame through adb exec-out screencap -p to {outputPath}.",
                    Items: [outputPath]);
            }

            TryDelete(outputPath);
            return Outcome(
                OperationOutcomeKind.Failure,
                "Screenshot capture failed.",
                string.IsNullOrWhiteSpace(stderr)
                    ? "adb exec-out screencap -p did not return a usable PNG."
                    : stderr);
        }
        catch (OperationCanceledException)
        {
            TryDelete(outputPath);
            TryKill(process);
            throw;
        }
        catch (Exception exception)
        {
            TryDelete(outputPath);
            return Outcome(
                OperationOutcomeKind.Failure,
                "Screenshot capture failed.",
                $"adb exec-out screencap -p failed: {exception.Message}");
        }
    }

    private static async Task<OperationOutcome> CapturePerfTraceViaAdbFallbackAsync(
        string deviceSerial,
        int durationMs,
        string hzdbFailureDetail,
        CancellationToken cancellationToken)
    {
        var adbPath = AdbExecutableLocator.TryLocate();
        if (string.IsNullOrWhiteSpace(adbPath))
        {
            return Outcome(
                OperationOutcomeKind.Failure,
                "Perf capture failed.",
                $"{hzdbFailureDetail}\nFallback unavailable because adb.exe could not be located.");
        }

        var outputPath = CreatePerfTraceOutputPath(deviceSerial);
        var startInfo = new ProcessStartInfo
        {
            FileName = adbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in BuildAdbPerfettoArguments(deviceSerial, durationMs))
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await process.StandardOutput.BaseStream.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var stderr = (await stderrTask.ConfigureAwait(false)).Trim();
            var fileInfo = new FileInfo(outputPath);
            if (process.ExitCode == 0 && fileInfo.Exists && fileInfo.Length > 0)
            {
                var detail =
                    $"{hzdbFailureDetail}\n" +
                    $"hzdb perf capture crashed, so the companion fell back to adb exec-out / perfetto. " +
                    $"Trace saved to {outputPath}.";

                return new OperationOutcome(
                    OperationOutcomeKind.Success,
                    $"Perf trace captured ({durationMs}ms).",
                    detail,
                    Items: [outputPath]);
            }

            TryDelete(outputPath);
            return Outcome(
                OperationOutcomeKind.Failure,
                "Perf capture failed.",
                $"{hzdbFailureDetail}\nFallback via adb exec-out failed.\n{stderr}");
        }
        catch (OperationCanceledException)
        {
            TryDelete(outputPath);
            TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(outputPath);
            return Outcome(
                OperationOutcomeKind.Failure,
                "Perf capture failed.",
                $"{hzdbFailureDetail}\nFallback via adb exec-out failed.\n{ex.Message}");
        }
    }

    private static IEnumerable<string?> EnumerateHzdbExecutableCandidates()
    {
        yield return Environment.GetEnvironmentVariable("VISCEREALITY_HZDB_EXE");
        yield return OfficialQuestToolingLayout.HzdbExecutablePath;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "hzdb.exe");
        }

        foreach (var pathValue in EnumeratePathValues())
        {
            foreach (var entry in SplitPathEntries(pathValue))
                yield return Path.Combine(entry, "hzdb.exe");
        }
    }

    private static IEnumerable<string?> EnumerateNpxCommandCandidates()
    {
        yield return Environment.GetEnvironmentVariable("VISCEREALITY_NPX_CMD");

        foreach (var pathValue in EnumeratePathValues())
        {
            foreach (var entry in SplitPathEntries(pathValue))
                yield return Path.Combine(entry, "npx.cmd");
        }

        foreach (var wellKnownDirectory in EnumerateWellKnownNodeDirectories())
            yield return Path.Combine(wellKnownDirectory, "npx.cmd");
    }

    private static IEnumerable<string?> EnumeratePathValues()
    {
        yield return Environment.GetEnvironmentVariable("PATH");
        yield return Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        yield return Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
    }

    private static IEnumerable<string> SplitPathEntries(string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
            yield break;

        foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(entry))
                yield return entry;
        }
    }

    private static IEnumerable<string> EnumerateWellKnownNodeDirectories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var userNodeRoot = Path.Combine(localAppData, "Programs", "nodejs");
            if (Directory.Exists(userNodeRoot))
            {
                yield return userNodeRoot;

                foreach (var childDirectory in Directory.EnumerateDirectories(userNodeRoot))
                    yield return childDirectory;
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            yield return Path.Combine(programFiles, "nodejs");

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            yield return Path.Combine(programFilesX86, "nodejs");
    }

    private static string? NormalizeCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        try
        {
            return Path.GetFullPath(candidate.Trim().Trim('"'));
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeScreenshotMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            return null;
        }

        return method.Trim().Trim('"');
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static IReadOnlyList<string> BuildAdbPerfettoArguments(string deviceSerial, int durationMs)
    {
        var durationSeconds = Math.Max(1, (int)Math.Ceiling(durationMs / 1000d));
        return
        [
            "-s",
            deviceSerial,
            "exec-out",
            "perfetto",
            "-o",
            "-",
            "-t",
            $"{durationSeconds}s",
            "sched/sched_switch",
            "wm",
            "am",
            "gfx",
            "view",
            "binder_driver",
            "hal",
            "dalvik",
            "camera",
            "input",
            "res",
            "memory"
        ];
    }

    private static string CreatePerfTraceOutputPath(string deviceSerial)
    {
        var outputDirectory = CompanionOperatorDataLayout.PerfTraceRootPath;
        Directory.CreateDirectory(outputDirectory);

        var sanitizedDeviceSerial = new string(deviceSerial
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray());

        return Path.Combine(
            outputDirectory,
            $"perfetto_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{sanitizedDeviceSerial}.perfetto-trace");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static OperationOutcome Outcome(OperationOutcomeKind kind, string summary, string detail)
        => new(kind, summary, string.IsNullOrWhiteSpace(detail) ? string.Empty : detail);

    private static bool TryComputeHoldUntilUtc(ProximityBroadcastInfo broadcast, DateTimeOffset observedAtUtc, out DateTimeOffset holdUntilUtc)
    {
        holdUntilUtc = default;
        if (!string.Equals(broadcast.Action, "prox_close", StringComparison.OrdinalIgnoreCase) || broadcast.DurationMs <= 0)
            return false;

        var eventTimeUtc = observedAtUtc - TimeSpan.FromSeconds(Math.Max(0, broadcast.AgeSeconds));
        var candidate = eventTimeUtc.AddMilliseconds(broadcast.DurationMs);
        if (candidate <= observedAtUtc)
            return false;

        holdUntilUtc = candidate;
        return true;
    }

    private static ProximityBroadcastInfo? TryParseLatestBroadcast(string rawOutput)
    {
        ProximityBroadcastInfo? latest = null;

        foreach (Match match in VrPowerManagerBroadcastRegex.Matches(rawOutput))
        {
            if (!match.Success ||
                !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ageSeconds) ||
                !int.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationMs))
            {
                continue;
            }

            var candidate = new ProximityBroadcastInfo(
                match.Groups[2].Value.Trim(),
                ageSeconds,
                durationMs);

            if (latest is null || candidate.AgeSeconds < latest.AgeSeconds)
            {
                latest = candidate;
            }
        }

        return latest;
    }

    private static string? MatchValue(Regex regex, string input)
    {
        var match = regex.Match(input);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
    {
        public string Combined => string.IsNullOrWhiteSpace(StdErr) ? StdOut : $"{StdOut}\n{StdErr}";
    }

    private sealed record ProximityBroadcastInfo(
        string Action,
        double AgeSeconds,
        int DurationMs);
}

public sealed class PreviewHzdbService : IHzdbService
{
    public bool IsAvailable => false;

    public Task<OperationOutcome> CaptureScreenshotAsync(string deviceSerial, string outputPath, string? method = null, CancellationToken cancellationToken = default)
        => Preview("Screenshot capture requires hzdb. Run guided setup or install the official Quest tooling cache first.");

    public Task<OperationOutcome> CapturePerfTraceAsync(string deviceSerial, int durationMs = 5000, CancellationToken cancellationToken = default)
        => Preview("Perf capture requires hzdb. Run guided setup or install the official Quest tooling cache first.");

    public Task<OperationOutcome> SetProximityAsync(string deviceSerial, bool enabled, int? durationMs = null, CancellationToken cancellationToken = default)
        => Preview("Proximity control requires hzdb. Run guided setup or install the official Quest tooling cache first.");

    public Task<QuestProximityStatus> GetProximityStatusAsync(string deviceSerial, CancellationToken cancellationToken = default)
        => Task.FromResult(new QuestProximityStatus(
            Available: false,
            HoldActive: false,
            VirtualState: string.Empty,
            IsAutosleepDisabled: false,
            HeadsetState: string.Empty,
            AutoSleepTimeMs: null,
            RetrievedAtUtc: DateTimeOffset.UtcNow,
            HoldUntilUtc: null,
            StatusDetail: "Quest proximity readback requires adb plus hzdb companion integration."));

    public Task<OperationOutcome> WakeDeviceAsync(string deviceSerial, CancellationToken cancellationToken = default)
        => Preview("Device wake via hzdb requires hzdb. Run guided setup or install the official Quest tooling cache first.");

    public Task<OperationOutcome> GetDeviceInfoAsync(string deviceSerial, CancellationToken cancellationToken = default)
        => Preview("Device info requires hzdb. Run guided setup or install the official Quest tooling cache first.");

    public Task<OperationOutcome> ListFilesAsync(string deviceSerial, string remotePath, CancellationToken cancellationToken = default)
        => Preview("File listing requires hzdb. Run guided setup or install the official Quest tooling cache first.");

    public Task<OperationOutcome> PushFileAsync(string deviceSerial, string localPath, string remotePath, CancellationToken cancellationToken = default)
        => Preview("File push requires hzdb. Run guided setup or install the official Quest tooling cache first.");

    public Task<OperationOutcome> PullFileAsync(string deviceSerial, string remotePath, string localPath, CancellationToken cancellationToken = default)
        => Preview("File pull requires hzdb. Run guided setup or install the official Quest tooling cache first.");

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
