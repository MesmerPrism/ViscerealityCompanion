using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed record WindowsEnvironmentCheckResult(
    string Id,
    string Label,
    OperationOutcomeKind Level,
    string Summary,
    string Detail);

public sealed record WindowsEnvironmentAnalysisRequest(
    string ExpectedLslStreamName,
    string ExpectedLslStreamType,
    bool ProbeExpectedLslStream = true);

public sealed record WindowsEnvironmentAnalysisResult(
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    IReadOnlyList<WindowsEnvironmentCheckResult> Checks,
    DateTimeOffset CompletedAtUtc);

public sealed class WindowsEnvironmentAnalysisService
{
    private static readonly TimeSpan StreamProbeTimeout = TimeSpan.FromSeconds(4);
    private readonly ILslMonitorService _monitorService;
    private readonly IStudyClockAlignmentService _clockAlignmentService;
    private readonly ITestLslSignalService _testSignalService;
    private readonly ITwinModeBridge _twinBridge;
    private readonly Func<OfficialQuestToolingStatus> _toolingStatusProvider;
    private readonly Func<string?> _adbLocator;
    private readonly Func<string?> _hzdbLocator;
    private readonly Func<DateTimeOffset> _utcNow;

    public WindowsEnvironmentAnalysisService(
        ILslMonitorService monitorService,
        IStudyClockAlignmentService clockAlignmentService,
        ITestLslSignalService testSignalService,
        ITwinModeBridge twinBridge,
        Func<OfficialQuestToolingStatus>? toolingStatusProvider = null,
        Func<string?>? adbLocator = null,
        Func<string?>? hzdbLocator = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _clockAlignmentService = clockAlignmentService ?? throw new ArgumentNullException(nameof(clockAlignmentService));
        _testSignalService = testSignalService ?? throw new ArgumentNullException(nameof(testSignalService));
        _twinBridge = twinBridge ?? throw new ArgumentNullException(nameof(twinBridge));
        _toolingStatusProvider = toolingStatusProvider ?? GetLocalToolingStatus;
        _adbLocator = adbLocator ?? AdbExecutableLocator.TryLocate;
        _hzdbLocator = hzdbLocator ?? WindowsHzdbService.ResolveHzdbCommandPath;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<WindowsEnvironmentAnalysisResult> AnalyzeAsync(
        WindowsEnvironmentAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var checks = new List<WindowsEnvironmentCheckResult>
        {
            BuildWindowsPlatformCheck(),
            BuildManagedToolCacheCheck(_toolingStatusProvider()),
            BuildAdbCheck(),
            BuildHzdbCheck(),
            BuildRuntimeCheck(
                id: "lsl-monitor-runtime",
                label: "Windows liblsl monitor runtime",
                runtimeState: _monitorService.RuntimeState,
                required: true,
                fixHint: "Make sure an official liblsl runtime is available to the app, either from the packaged copy or %VISCEREALITY_LSL_DLL%."),
            BuildRuntimeCheck(
                id: "clock-alignment-runtime",
                label: "Windows clock-alignment runtime",
                runtimeState: _clockAlignmentService.RuntimeState,
                required: true,
                fixHint: "Clock alignment needs the same working liblsl runtime as the Windows monitor path."),
            BuildRuntimeCheck(
                id: "test-sender-runtime",
                label: "Windows TEST sender runtime",
                runtimeState: _testSignalService.RuntimeState,
                required: false,
                fixHint: "The Sussex bench sender is optional, but if you use it the same liblsl runtime must be available locally."),
            BuildTwinBridgeCheck()
        };

        if (request.ProbeExpectedLslStream)
        {
            checks.Add(await ProbeExpectedStreamAsync(request, cancellationToken).ConfigureAwait(false));
        }

        var failureCount = checks.Count(check => check.Level == OperationOutcomeKind.Failure);
        var warningCount = checks.Count(check => check.Level == OperationOutcomeKind.Warning);
        var successCount = checks.Count(check => check.Level == OperationOutcomeKind.Success);

        var level = failureCount > 0
            ? OperationOutcomeKind.Failure
            : warningCount > 0
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success;

        var summary = level switch
        {
            OperationOutcomeKind.Failure => "Windows environment analysis found blocking issues.",
            OperationOutcomeKind.Warning => "Windows environment analysis found advisories.",
            _ => "Windows environment analysis passed."
        };

        var detail = $"Checks ok/warn/fail: {successCount}/{warningCount}/{failureCount}.";
        return new WindowsEnvironmentAnalysisResult(level, summary, detail, checks, _utcNow());
    }

    private static WindowsEnvironmentCheckResult BuildWindowsPlatformCheck()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsEnvironmentCheckResult(
                "windows-platform",
                "Windows platform",
                OperationOutcomeKind.Success,
                "Windows runtime detected.",
                $"Running on {Environment.OSVersion.VersionString}. The public Sussex desktop flow expects Windows.");
        }

        return new WindowsEnvironmentCheckResult(
            "windows-platform",
            "Windows platform",
            OperationOutcomeKind.Failure,
            "Windows runtime not detected.",
            "The Sussex desktop operator shell only supports the live public workflow on Windows.");
    }

    private static OfficialQuestToolingStatus GetLocalToolingStatus()
    {
        using var toolingService = new OfficialQuestToolingService();
        return toolingService.GetLocalStatus();
    }

    private static WindowsEnvironmentCheckResult BuildManagedToolCacheCheck(OfficialQuestToolingStatus localStatus)
    {
        if (localStatus.IsReady)
        {
            return new WindowsEnvironmentCheckResult(
                "official-tool-cache",
                "Managed official tool cache",
                OperationOutcomeKind.Success,
                "Managed Quest tooling cache is ready.",
                $"hzdb {localStatus.Hzdb.InstalledVersion ?? "n/a"} at {localStatus.Hzdb.InstallPath}. Android platform-tools {localStatus.PlatformTools.InstalledVersion ?? "n/a"} at {localStatus.PlatformTools.InstallPath}.");
        }

        return new WindowsEnvironmentCheckResult(
            "official-tool-cache",
            "Managed official tool cache",
            OperationOutcomeKind.Warning,
            "Managed Quest tooling cache is incomplete.",
            $"Run guided setup or `viscereality tooling install-official` so the app can rely on the LocalAppData-managed official Meta hzdb and Google platform-tools copies under {OfficialQuestToolingLayout.RootPath}.");
    }

    private WindowsEnvironmentCheckResult BuildAdbCheck()
    {
        var adbPath = _adbLocator();
        if (!string.IsNullOrWhiteSpace(adbPath))
        {
            return new WindowsEnvironmentCheckResult(
                "adb",
                "ADB transport",
                OperationOutcomeKind.Success,
                "adb.exe is available.",
                $"Resolved adb at {adbPath}.");
        }

        return new WindowsEnvironmentCheckResult(
            "adb",
            "ADB transport",
            OperationOutcomeKind.Failure,
            "adb.exe is missing.",
            $"Quest control cannot run until adb.exe is available. Preferred fix: run guided setup or `viscereality tooling install-official`. Expected managed path: {OfficialQuestToolingLayout.AdbExecutablePath}.");
    }

    private WindowsEnvironmentCheckResult BuildHzdbCheck()
    {
        var hzdbPath = _hzdbLocator();
        if (!string.IsNullOrWhiteSpace(hzdbPath))
        {
            var level = string.Equals(Path.GetExtension(hzdbPath), ".cmd", StringComparison.OrdinalIgnoreCase)
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success;
            var summary = level == OperationOutcomeKind.Success
                ? "hzdb is available."
                : "hzdb is available through npm/npx fallback.";
            var detail = level == OperationOutcomeKind.Success
                ? $"Resolved hzdb command at {hzdbPath}."
                : $"Resolved hzdb through {hzdbPath}. This works, but the managed Meta binary cache is the more stable public path.";
            return new WindowsEnvironmentCheckResult("hzdb", "hzdb command", level, summary, detail);
        }

        return new WindowsEnvironmentCheckResult(
            "hzdb",
            "hzdb command",
            OperationOutcomeKind.Failure,
            "hzdb is missing.",
            $"Quest screenshot, file pullback, and proximity control depend on hzdb. Preferred fix: run guided setup or `viscereality tooling install-official`. Expected managed path: {OfficialQuestToolingLayout.HzdbExecutablePath}.");
    }

    private static WindowsEnvironmentCheckResult BuildRuntimeCheck(
        string id,
        string label,
        LslRuntimeState runtimeState,
        bool required,
        string fixHint)
    {
        if (runtimeState.Available)
        {
            return new WindowsEnvironmentCheckResult(id, label, OperationOutcomeKind.Success, $"{label} is ready.", runtimeState.Detail);
        }

        return new WindowsEnvironmentCheckResult(
            id,
            label,
            required ? OperationOutcomeKind.Failure : OperationOutcomeKind.Warning,
            $"{label} is unavailable.",
            $"{runtimeState.Detail} Fix: {fixHint}");
    }

    private WindowsEnvironmentCheckResult BuildTwinBridgeCheck()
    {
        var status = _twinBridge.Status;
        return new WindowsEnvironmentCheckResult(
            "twin-bridge",
            "Twin bridge transport",
            status.IsAvailable ? OperationOutcomeKind.Success : OperationOutcomeKind.Failure,
            status.Summary,
            status.Detail);
    }

    private async Task<WindowsEnvironmentCheckResult> ProbeExpectedStreamAsync(
        WindowsEnvironmentAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var streamName = string.IsNullOrWhiteSpace(request.ExpectedLslStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : request.ExpectedLslStreamName.Trim();
        var streamType = string.IsNullOrWhiteSpace(request.ExpectedLslStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : request.ExpectedLslStreamType.Trim();

        if (!_monitorService.RuntimeState.Available)
        {
            return new WindowsEnvironmentCheckResult(
                "expected-stream",
                "Expected Sussex LSL stream",
                OperationOutcomeKind.Warning,
                "Expected Sussex LSL stream could not be probed.",
                $"The Windows LSL monitor runtime is unavailable, so the app could not probe {streamName} / {streamType} on this PC.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(StreamProbeTimeout);

        try
        {
            await foreach (var reading in _monitorService
                               .MonitorAsync(new LslMonitorSubscription(streamName, streamType, HrvBiofeedbackStreamContract.DefaultChannelIndex), timeoutCts.Token)
                               .WithCancellation(timeoutCts.Token)
                               .ConfigureAwait(false))
            {
                if (string.Equals(reading.Status, "LSL stream connected.", StringComparison.Ordinal))
                {
                    return new WindowsEnvironmentCheckResult(
                        "expected-stream",
                        "Expected Sussex LSL stream",
                        OperationOutcomeKind.Success,
                        $"{streamName} / {streamType} is visible on Windows.",
                        reading.Detail);
                }

                if (string.Equals(reading.Status, "Streaming LSL sample.", StringComparison.Ordinal))
                {
                    var detail = string.IsNullOrWhiteSpace(reading.Detail)
                        ? $"Received a sample from {streamName} / {streamType}."
                        : reading.Detail;
                    return new WindowsEnvironmentCheckResult(
                        "expected-stream",
                        "Expected Sussex LSL stream",
                        OperationOutcomeKind.Success,
                        $"{streamName} / {streamType} is visible and streaming on Windows.",
                        detail);
                }

                if (string.Equals(reading.Status, "LSL stream not found.", StringComparison.Ordinal))
                {
                    return new WindowsEnvironmentCheckResult(
                        "expected-stream",
                        "Expected Sussex LSL stream",
                        OperationOutcomeKind.Warning,
                        $"{streamName} / {streamType} is not currently visible on Windows.",
                        $"{reading.Detail} Fix: start the upstream sender on this PC, or verify that the external sender is publishing the expected name/type contract.");
                }

                if (string.Equals(reading.Status, "LSL unavailable.", StringComparison.Ordinal) ||
                    string.Equals(reading.Status, "LSL monitor error.", StringComparison.Ordinal) ||
                    string.Equals(reading.Status, "LSL channel unavailable.", StringComparison.Ordinal))
                {
                    return new WindowsEnvironmentCheckResult(
                        "expected-stream",
                        "Expected Sussex LSL stream",
                        OperationOutcomeKind.Failure,
                        $"{streamName} / {streamType} could not be probed.",
                        reading.Detail);
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new WindowsEnvironmentCheckResult(
                "expected-stream",
                "Expected Sussex LSL stream",
                OperationOutcomeKind.Warning,
                $"{streamName} / {streamType} probe timed out.",
                $"The app waited {StreamProbeTimeout.TotalSeconds:0} seconds and did not resolve a definitive state for the expected stream. If an external sender should be running, verify it is publishing the expected contract and that liblsl can still resolve it on this PC.");
        }

        return new WindowsEnvironmentCheckResult(
            "expected-stream",
            "Expected Sussex LSL stream",
            OperationOutcomeKind.Warning,
            $"{streamName} / {streamType} probe ended without a clear result.",
            "The Windows LSL probe did not return a connected, streaming, or not-found state before it stopped.");
    }
}
