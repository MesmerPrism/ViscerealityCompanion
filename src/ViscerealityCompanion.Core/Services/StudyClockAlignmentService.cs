using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public interface IStudyClockAlignmentService : IDisposable
{
    LslRuntimeState RuntimeState { get; }

    Task<OperationOutcome> StartWarmSessionAsync(CancellationToken cancellationToken = default);

    Task<OperationOutcome> StopWarmSessionAsync(CancellationToken cancellationToken = default);

    Task<StudyClockAlignmentRunResult> RunAsync(
        StudyClockAlignmentRunRequest request,
        IProgress<StudyClockAlignmentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsStudyClockAlignmentService : IStudyClockAlignmentService
{
    private const int ProbeChannelCount = 1;
    private const int OutletChunkSize = 1;
    private const int OutletMaxBufferedSeconds = 2;
    private static readonly TimeSpan MonitorShutdownTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MonitorReadyTimeout = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan WarmPulseInterval = TimeSpan.FromMilliseconds(SussexClockAlignmentStreamContract.DefaultProbeIntervalMilliseconds);
    private readonly ILslMonitorService _monitorService;
    private readonly EchoMonitorSession _echoMonitor;
    private readonly Lock _sync = new();
    private nint _streamInfo;
    private nint _outlet;
    private CancellationTokenSource? _warmPulseCts;
    private Task? _warmPulseTask;
    private int _nextWarmPulseSequence;

    public WindowsStudyClockAlignmentService()
        : this(null)
    {
    }

    internal WindowsStudyClockAlignmentService(ILslMonitorService? monitorService)
    {
        _monitorService = monitorService ?? LslMonitorServiceFactory.CreateDefault();
        _echoMonitor = new EchoMonitorSession(_monitorService);
        RuntimeState = NativeMethods.GetRuntimeState();
    }

    public LslRuntimeState RuntimeState { get; }

    public async Task<OperationOutcome> StartWarmSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!RuntimeState.Available)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Clock alignment warmup unavailable.",
                RuntimeState.Detail);
        }

        if (!TryEnsureProbeOutlet(out var outletOutcome))
        {
            return outletOutcome with
            {
                Summary = "Clock alignment warmup could not open the probe stream."
            };
        }

        EchoMonitorState monitorState;
        try
        {
            monitorState = await _echoMonitor.EnsureRunningAsync(MonitorReadyTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Clock alignment warmup could not start the echo monitor.",
                exception.Message);
        }

        EnsureWarmPulseRunning();
        TryPushWarmPulseSample();

        return monitorState switch
        {
            { Connected: true } => new OperationOutcome(
                OperationOutcomeKind.Success,
                "Clock alignment warmup active.",
                $"Reusing {SussexClockAlignmentStreamContract.ProbeStreamName} / {SussexClockAlignmentStreamContract.ProbeStreamType} and keeping the Quest echo path warm with unique keepalive probes every {WarmPulseInterval.TotalMilliseconds:0} ms during the participant session."),
            { ReadyTimedOut: true } => new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Clock alignment warmup is still waiting for the echo monitor.",
                $"The probe outlet is active and unique keepalive probes are running every {WarmPulseInterval.TotalMilliseconds:0} ms, but the echo inlet did not report ready within {MonitorReadyTimeout.TotalSeconds:0.#} seconds."),
            _ => new OperationOutcome(
                OperationOutcomeKind.Success,
                "Clock alignment warmup active.",
                $"The probe outlet is active and unique keepalive probes are running every {WarmPulseInterval.TotalMilliseconds:0} ms for {SussexClockAlignmentStreamContract.EchoStreamName} / {SussexClockAlignmentStreamContract.EchoStreamType}.")
        };
    }

    public async Task<OperationOutcome> StopWarmSessionAsync(CancellationToken cancellationToken = default)
    {
        var warmPulseStopped = await StopWarmPulseAsync(cancellationToken).ConfigureAwait(false);
        var monitorStopped = await _echoMonitor.StopAsync(MonitorShutdownTimeout, cancellationToken).ConfigureAwait(false);
        CloseProbeOutlet();
        Interlocked.Exchange(ref _nextWarmPulseSequence, 0);

        return warmPulseStopped && monitorStopped
            ? new OperationOutcome(
                OperationOutcomeKind.Success,
                "Clock alignment warmup stopped.",
                "Stopped the session-scoped warm clock-alignment transport and closed the probe outlet.")
            : new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Clock alignment warmup stop timed out.",
                $"The session-scoped warm transport did not stop cleanly within {MonitorShutdownTimeout.TotalSeconds:0.#} seconds. The clock-alignment path was still asked to close.");
    }

    public async Task<StudyClockAlignmentRunResult> RunAsync(
        StudyClockAlignmentRunRequest request,
        IProgress<StudyClockAlignmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeState.Available)
        {
            return new StudyClockAlignmentRunResult(
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Clock alignment unavailable.",
                    RuntimeState.Detail),
                BuildSummary([], probesSent: 0),
                []);
        }

        if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.DatasetHash))
        {
            return new StudyClockAlignmentRunResult(
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Clock alignment skipped.",
                    "Clock alignment requires the active Sussex session id and dataset hash before the probe can run."),
                BuildSummary([], probesSent: 0),
                []);
        }

        var probesSent = 0;
        var echoesReceived = 0;
        var pending = new ConcurrentDictionary<int, PendingProbe>();
        var receivedSamples = new ConcurrentQueue<StudyClockAlignmentSample>();
        var mismatchedEchoes = new ConcurrentQueue<EchoSessionMismatch>();
        var monitorReadyTimedOut = false;
        var monitorConnected = false;
        using var echoSubscription = _echoMonitor.Subscribe(reading =>
        {
            var sessionMatches = string.Equals(reading.Payload.SessionId, request.SessionId, StringComparison.Ordinal);
            var datasetMatches = string.Equals(reading.Payload.DatasetHash, request.DatasetHash, StringComparison.OrdinalIgnoreCase);
            if (!sessionMatches || !datasetMatches)
            {
                mismatchedEchoes.Enqueue(new EchoSessionMismatch(
                    reading.Payload.Sequence,
                    reading.Payload.SessionId,
                    reading.Payload.DatasetHash));
                return;
            }

            if (!pending.TryRemove(reading.Payload.Sequence, out var probe))
            {
                return;
            }

            var echoReceivedLocalClockSeconds = reading.ObservedLocalClockSeconds ?? NativeMethods.LocalClock();
            var questEchoLocalClockSeconds = reading.Payload.QuestEchoLocalClockSeconds > 0d
                ? reading.Payload.QuestEchoLocalClockSeconds
                : reading.SampleTimestampSeconds ?? 0d;
            if (questEchoLocalClockSeconds <= 0d)
            {
                questEchoLocalClockSeconds = reading.Payload.QuestReceiveLocalClockSeconds;
            }

            var questMinusWindowsClockSeconds =
                ((reading.Payload.QuestReceiveLocalClockSeconds - probe.SentLocalClockSeconds) +
                 (questEchoLocalClockSeconds - echoReceivedLocalClockSeconds)) / 2d;
            var roundTripSeconds =
                (echoReceivedLocalClockSeconds - probe.SentLocalClockSeconds) -
                (questEchoLocalClockSeconds - reading.Payload.QuestReceiveLocalClockSeconds);

            var sample = new StudyClockAlignmentSample(
                request.WindowKind,
                reading.Payload.Sequence,
                probe.SentAtUtc,
                probe.SentLocalClockSeconds,
                reading.Timestamp,
                echoReceivedLocalClockSeconds,
                reading.SampleTimestampSeconds,
                reading.Payload.QuestReceivedAtUtc,
                reading.Payload.QuestReceiveLocalClockSeconds,
                questEchoLocalClockSeconds,
                questMinusWindowsClockSeconds,
                roundTripSeconds);
            receivedSamples.Enqueue(sample);
            var currentEchoCount = Interlocked.Increment(ref echoesReceived);

            progress?.Report(BuildProgress(
                request,
                Volatile.Read(ref probesSent),
                currentEchoCount,
                "Clock alignment is receiving Quest echoes.",
                $"Echo {reading.Payload.Sequence} received. RTT {roundTripSeconds * 1000d:0.0} ms, quest-minus-Windows offset {questMinusWindowsClockSeconds * 1000d:0.0} ms."));
        });

        if (!TryEnsureProbeOutlet(out var openOutcome))
        {
            return new StudyClockAlignmentRunResult(openOutcome, BuildSummary([], probesSent: 0), []);
        }

        progress?.Report(BuildProgress(
            request,
            probesSent,
            echoesReceived,
            "Clock alignment is starting.",
            $"Opening {SussexClockAlignmentStreamContract.ProbeStreamName} / {SussexClockAlignmentStreamContract.ProbeStreamType} and waiting for Quest echoes."));

        try
        {
            var monitorState = await _echoMonitor.EnsureRunningAsync(MonitorReadyTimeout, cancellationToken).ConfigureAwait(false);
            monitorConnected = monitorState.Connected;
            monitorReadyTimedOut = monitorState.ReadyTimedOut;
        }
        catch (Exception exception)
        {
            return new StudyClockAlignmentRunResult(
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Clock alignment could not start the echo monitor.",
                    exception.Message),
                BuildSummary([], probesSent: 0),
                []);
        }

        progress?.Report(BuildProgress(
            request,
            probesSent,
            echoesReceived,
            monitorConnected
                ? "Clock alignment echo monitor connected."
                : "Clock alignment is still waiting for the echo monitor.",
            monitorConnected
                ? $"Echo inlet for {SussexClockAlignmentStreamContract.EchoStreamName} is ready. Sending probes now."
                : $"Echo inlet did not report ready within {MonitorReadyTimeout.TotalSeconds:0.#} seconds. Sending probes anyway so the Sussex flow can continue."));

        var probeScheduleOffsets = BuildProbeScheduleOffsets(request.Duration, request.ProbeInterval);
        var probeScheduleStartUtc = DateTimeOffset.UtcNow;
        var sequence = request.FirstProbeSequence - 1;

        foreach (var probeOffset in probeScheduleOffsets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scheduledProbeAtUtc = probeScheduleStartUtc + probeOffset;
            var delay = scheduledProbeAtUtc - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            sequence++;
            var sentAtUtc = DateTimeOffset.UtcNow;
            var sentLocalClockSeconds = NativeMethods.LocalClock();
            pending[sequence] = new PendingProbe(sequence, sentAtUtc, sentLocalClockSeconds);
            if (!TryPushProbeSample(sequence, sentLocalClockSeconds))
            {
                pending.TryRemove(sequence, out _);
                var failedSummary = BuildSummary(receivedSamples.OrderBy(sample => sample.ProbeSequence).ToArray(), probesSent);
                return new StudyClockAlignmentRunResult(
                    new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Clock alignment could not push a probe sample.",
                        NativeMethods.GetLastError()),
                    failedSummary,
                    receivedSamples.OrderBy(sample => sample.ProbeSequence).ToArray());
            }

            Interlocked.Increment(ref probesSent);

            progress?.Report(BuildProgress(
                request,
                Volatile.Read(ref probesSent),
                Volatile.Read(ref echoesReceived),
                "Clock alignment is probing both clocks.",
                $"Sent probe {sequence}. Collecting RTT/offset samples for {request.Duration.TotalSeconds:0} seconds."));
        }

        progress?.Report(BuildProgress(
            request,
            Volatile.Read(ref probesSent),
            Volatile.Read(ref echoesReceived),
            "Clock alignment is waiting for the last Quest echoes.",
            $"Probe window finished. Waiting up to {request.EchoGracePeriod.TotalSeconds:0.0} seconds for delayed echoes."));

        if (request.EchoGracePeriod > TimeSpan.Zero)
        {
            await Task.Delay(request.EchoGracePeriod, cancellationToken).ConfigureAwait(false);
        }

        var orderedSamples = receivedSamples
            .OrderBy(sample => sample.ProbeSequence)
            .ToArray();
        var summary = BuildSummary(orderedSamples, probesSent);
        var outcome = BuildOutcome(summary);
        var mismatchDiagnostic = BuildMismatchDiagnosticDetail(
            mismatchedEchoes,
            request.SessionId,
            request.DatasetHash);
        if (!string.IsNullOrWhiteSpace(mismatchDiagnostic))
        {
            outcome = outcome with
            {
                Detail = string.IsNullOrWhiteSpace(outcome.Detail)
                    ? mismatchDiagnostic
                    : $"{outcome.Detail} {mismatchDiagnostic}"
            };
        }

        if (monitorReadyTimedOut)
        {
            outcome = outcome with
            {
                Detail = string.Join(
                    " ",
                    new[]
                    {
                        outcome.Detail,
                        $"The echo inlet took longer than {MonitorReadyTimeout.TotalSeconds:0.#} seconds to report ready, so this run may include startup transport overhead."
                    }.Where(value => !string.IsNullOrWhiteSpace(value)))
            };
        }

        progress?.Report(BuildProgress(
            request,
            Volatile.Read(ref probesSent),
            orderedSamples.Length,
            outcome.Summary,
            outcome.Detail));

        return new StudyClockAlignmentRunResult(outcome, summary, orderedSamples);
    }

    public void Dispose()
    {
        try
        {
            StopWarmSessionAsync().GetAwaiter().GetResult();
        }
        catch
        {
            _echoMonitor.Dispose();
            CloseProbeOutlet();
        }
    }

    private static OperationOutcome BuildOutcome(StudyClockAlignmentSummary summary)
    {
        if (summary.EchoesReceived <= 0)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Clock alignment did not receive any Quest echoes.",
                "The Sussex session started, but no round-trip probe completed during the alignment window. Cross-machine clock decomposition is unavailable for this run.");
        }

        if (summary.EchoesReceived < Math.Max(5, summary.ProbesSent / 4))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Clock alignment completed with partial echo coverage.",
                BuildSummaryDetail(summary));
        }

        return new OperationOutcome(
            OperationOutcomeKind.Success,
            "Clock alignment completed.",
            BuildSummaryDetail(summary));
    }

    private static string BuildSummaryDetail(StudyClockAlignmentSummary summary)
    {
        var detail = $"Quest echoed {summary.EchoesReceived} of {summary.ProbesSent} probes.";
        if (summary.RecommendedQuestMinusWindowsClockSeconds is double offset)
        {
            detail += $" Recommended quest-minus-Windows offset {offset * 1000d:0.0} ms.";
        }

        if (summary.MeanRoundTripSeconds is double meanRtt)
        {
            detail += $" Mean round-trip {meanRtt * 1000d:0.0} ms.";
        }

        if (summary.MinRoundTripSeconds is double minRtt &&
            summary.MaxRoundTripSeconds is double maxRtt)
        {
            detail += $" RTT span {minRtt * 1000d:0.0} to {maxRtt * 1000d:0.0} ms.";
        }

        return detail;
    }

    private static string BuildMismatchDiagnosticDetail(
        ConcurrentQueue<EchoSessionMismatch> mismatchedEchoes,
        string expectedSessionId,
        string expectedDatasetHash)
    {
        if (mismatchedEchoes.IsEmpty)
        {
            return string.Empty;
        }

        var mismatches = mismatchedEchoes.ToArray();
        var latest = mismatches[^1];
        var detail = new StringBuilder();
        detail.Append("Ignored ");
        detail.Append(mismatches.Length.ToString(CultureInfo.InvariantCulture));
        detail.Append(" echoed probe(s) because the Quest reported session_id=`");
        detail.Append(string.IsNullOrWhiteSpace(latest.ReportedSessionId) ? "n/a" : latest.ReportedSessionId);
        detail.Append("`, dataset_hash=`");
        detail.Append(string.IsNullOrWhiteSpace(latest.ReportedDatasetHash) ? "n/a" : latest.ReportedDatasetHash);
        detail.Append("` instead of the active Windows session_id=`");
        detail.Append(expectedSessionId);
        detail.Append("`, dataset_hash=`");
        detail.Append(expectedDatasetHash);
        detail.Append("`. This usually means the Quest recorder is still advertising stale participant-session metadata.");
        return detail.ToString();
    }

    private static StudyClockAlignmentProgress BuildProgress(
        StudyClockAlignmentRunRequest request,
        int probesSent,
        int echoesReceived,
        string summary,
        string detail)
    {
        var expectedProbeCount = GetExpectedProbeCount(request.Duration, request.ProbeInterval);
        var percentComplete = Math.Clamp((double)probesSent / expectedProbeCount * 100d, 0d, 100d);
        return new StudyClockAlignmentProgress(percentComplete, probesSent, echoesReceived, summary, detail);
    }

    internal static int GetExpectedProbeCount(TimeSpan duration, TimeSpan probeInterval)
    {
        if (probeInterval <= TimeSpan.Zero)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Ceiling(duration.TotalMilliseconds / probeInterval.TotalMilliseconds));
    }

    internal static IReadOnlyList<TimeSpan> BuildProbeScheduleOffsets(TimeSpan duration, TimeSpan probeInterval)
    {
        var count = GetExpectedProbeCount(duration, probeInterval);
        if (count <= 0)
        {
            return [];
        }

        var offsets = new TimeSpan[count];
        if (probeInterval <= TimeSpan.Zero)
        {
            offsets[0] = TimeSpan.Zero;
            return offsets;
        }

        for (var index = 0; index < offsets.Length; index++)
        {
            offsets[index] = TimeSpan.FromTicks(probeInterval.Ticks * index);
        }

        return offsets;
    }

    private static StudyClockAlignmentSummary BuildSummary(IReadOnlyList<StudyClockAlignmentSample> samples, int probesSent)
    {
        if (samples.Count == 0)
        {
            return new StudyClockAlignmentSummary(probesSent, 0, null, null, null, null, null, null);
        }

        var orderedByRtt = samples.OrderBy(sample => sample.RoundTripSeconds).ToArray();
        var bestWindowCount = Math.Max(1, (int)Math.Ceiling(orderedByRtt.Length * 0.25d));
        var bestOffsets = orderedByRtt
            .Take(bestWindowCount)
            .Select(sample => sample.QuestMinusWindowsClockSeconds)
            .OrderBy(value => value)
            .ToArray();
        var allOffsets = samples
            .Select(sample => sample.QuestMinusWindowsClockSeconds)
            .OrderBy(value => value)
            .ToArray();
        var allRtts = samples
            .Select(sample => sample.RoundTripSeconds)
            .OrderBy(value => value)
            .ToArray();

        return new StudyClockAlignmentSummary(
            probesSent,
            samples.Count,
            Median(bestOffsets),
            Median(allOffsets),
            allOffsets.Average(),
            allRtts.Average(),
            allRtts.First(),
            allRtts.Last());
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var mid = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[mid - 1] + values[mid]) / 2d
            : values[mid];
    }

    private bool TryEnsureProbeOutlet(out OperationOutcome outcome)
    {
        lock (_sync)
        {
            if (_outlet != nint.Zero && _streamInfo != nint.Zero)
            {
                outcome = new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Clock alignment probe stream active.",
                    $"Reusing {SussexClockAlignmentStreamContract.ProbeStreamName} / {SussexClockAlignmentStreamContract.ProbeStreamType}.");
                return true;
            }

            _streamInfo = NativeMethods.CreateStreamInfo(
                SussexClockAlignmentStreamContract.ProbeStreamName,
                SussexClockAlignmentStreamContract.ProbeStreamType,
                ProbeChannelCount,
                0d,
                NativeMethods.FloatChannelFormat,
                "viscereality.companion.sussex.clockprobe");
            if (_streamInfo == nint.Zero)
            {
                outcome = new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Clock alignment could not open the probe stream.",
                    NativeMethods.GetLastError());
                return false;
            }

            NativeMethods.AppendSingleChannelMetadata(
                _streamInfo,
                "probe_sequence",
                "sequence");

            _outlet = NativeMethods.CreateOutlet(_streamInfo, OutletChunkSize, OutletMaxBufferedSeconds);
            if (_outlet == nint.Zero)
            {
                NativeMethods.DestroyStreamInfo(_streamInfo);
                _streamInfo = nint.Zero;
                outcome = new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Clock alignment could not create the probe outlet.",
                    NativeMethods.GetLastError());
                return false;
            }

            outcome = new OperationOutcome(
                OperationOutcomeKind.Success,
                "Clock alignment probe stream active.",
                $"Publishing {SussexClockAlignmentStreamContract.ProbeStreamName} / {SussexClockAlignmentStreamContract.ProbeStreamType}.");
            return true;
        }
    }

    private void CloseProbeOutlet()
    {
        lock (_sync)
        {
            if (_outlet != nint.Zero)
            {
                NativeMethods.DestroyOutlet(_outlet);
                _outlet = nint.Zero;
            }

            if (_streamInfo != nint.Zero)
            {
                NativeMethods.DestroyStreamInfo(_streamInfo);
                _streamInfo = nint.Zero;
            }
        }
    }

    private void EnsureWarmPulseRunning()
    {
        lock (_sync)
        {
            if (_warmPulseTask is not null && !_warmPulseTask.IsCompleted)
            {
                return;
            }

            _warmPulseCts?.Cancel();
            _warmPulseCts?.Dispose();
            _warmPulseCts = new CancellationTokenSource();
            var token = _warmPulseCts.Token;
            _warmPulseTask = Task.Run(() => RunWarmPulseLoopAsync(token), CancellationToken.None);
            _ = _warmPulseTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task<bool> StopWarmPulseAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_sync)
        {
            cts = _warmPulseCts;
            task = _warmPulseTask;
            _warmPulseCts = null;
            _warmPulseTask = null;
        }

        if (cts is null && task is null)
        {
            return true;
        }

        cts?.Cancel();
        try
        {
            if (task is not null)
            {
                var completedTask = await Task.WhenAny(task, Task.Delay(MonitorShutdownTimeout, cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(completedTask, task))
                {
                    return false;
                }

                await task.ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
        finally
        {
            cts?.Dispose();
        }
    }

    private async Task RunWarmPulseLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TryPushWarmPulseSample();
                await Task.Delay(WarmPulseInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void TryPushWarmPulseSample()
        => TryPushProbeSample(Interlocked.Decrement(ref _nextWarmPulseSequence), NativeMethods.LocalClock());

    private bool TryPushProbeSample(float sequence, double timestampSeconds)
    {
        lock (_sync)
        {
            if (_outlet == nint.Zero)
            {
                return false;
            }

            NativeMethods.PushFloatSample(_outlet, [sequence], timestampSeconds);
            return true;
        }
    }

    private static bool TryParseEchoPayload(string payload, out EchoPayload result)
    {
        var tokens = payload.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var separatorIndex = token.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
            {
                continue;
            }

            values[token[..separatorIndex]] = token[(separatorIndex + 1)..];
        }

        if (!TryGetInt(values, "seq", out var sequence) ||
            !TryGetDouble(values, "quest_receive_lsl", out var questReceiveLsl))
        {
            result = default;
            return false;
        }

        TryGetDouble(values, "quest_echo_lsl", out var questEchoLsl);
        result = new EchoPayload(
            sequence,
            questReceiveLsl,
            questEchoLsl,
            values.GetValueOrDefault("quest_receive_utc") ?? string.Empty,
            values.GetValueOrDefault("session_id") ?? string.Empty,
            values.GetValueOrDefault("dataset_hash") ?? string.Empty);
        return true;
    }

    private static bool TryGetDouble(IReadOnlyDictionary<string, string> values, string key, out double value)
    {
        if (values.TryGetValue(key, out var raw) &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0d;
        return false;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> values, string key, out int value)
    {
        if (values.TryGetValue(key, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private readonly record struct PendingProbe(
        int Sequence,
        DateTimeOffset SentAtUtc,
        double SentLocalClockSeconds);

    private readonly record struct EchoSessionMismatch(
        int Sequence,
        string ReportedSessionId,
        string ReportedDatasetHash);

    private readonly record struct EchoPayload(
        int Sequence,
        double QuestReceiveLocalClockSeconds,
        double QuestEchoLocalClockSeconds,
        string QuestReceivedAtUtc,
        string SessionId,
        string DatasetHash);

    private sealed class EchoMonitorSession : IDisposable
    {
        private readonly ILslMonitorService _monitorService;
        private readonly Lock _sync = new();
        private CancellationTokenSource? _monitorCts;
        private Task? _monitorTask;
        private TaskCompletionSource<bool>? _monitorReady;
        private Action<EchoMonitorReading>? _echoReceived;
        private bool _monitorConnected;

        public EchoMonitorSession(ILslMonitorService monitorService)
        {
            _monitorService = monitorService;
        }

        public async Task<EchoMonitorState> EnsureRunningAsync(TimeSpan readyTimeout, CancellationToken cancellationToken)
        {
            Task<bool> readyTask;
            lock (_sync)
            {
                EnsureMonitorStarted_NoLock();
                if (_monitorConnected)
                {
                    return new EchoMonitorState(Connected: true, ReadyTimedOut: false);
                }

                readyTask = _monitorReady!.Task;
            }

            if (readyTask.IsCompleted)
            {
                return new EchoMonitorState(Connected: await readyTask.ConfigureAwait(false), ReadyTimedOut: false);
            }

            var completedTask = await Task.WhenAny(
                    readyTask,
                    Task.Delay(readyTimeout, cancellationToken))
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return ReferenceEquals(completedTask, readyTask)
                ? new EchoMonitorState(Connected: await readyTask.ConfigureAwait(false), ReadyTimedOut: false)
                : new EchoMonitorState(Connected: false, ReadyTimedOut: true);
        }

        public IDisposable Subscribe(Action<EchoMonitorReading> handler)
        {
            lock (_sync)
            {
                _echoReceived += handler;
            }

            return new EchoMonitorSubscription(this, handler);
        }

        public async Task<bool> StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            CancellationTokenSource? cts;
            Task? task;
            lock (_sync)
            {
                _echoReceived = null;
                cts = _monitorCts;
                task = _monitorTask;
                _monitorCts = null;
                _monitorTask = null;
                _monitorReady = null;
                _monitorConnected = false;
            }

            if (cts is null && task is null)
            {
                return true;
            }

            cts?.Cancel();
            try
            {
                if (task is not null)
                {
                    var completedTask = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(completedTask, task))
                    {
                        return false;
                    }

                    await task.ConfigureAwait(false);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
            finally
            {
                cts?.Dispose();
            }
        }

        public void Dispose()
        {
            try
            {
                _ = StopAsync(MonitorShutdownTimeout).GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        private void EnsureMonitorStarted_NoLock()
        {
            if (_monitorTask is not null && !_monitorTask.IsCompleted)
            {
                return;
            }

            _monitorCts?.Cancel();
            _monitorCts?.Dispose();
            _monitorCts = new CancellationTokenSource();
            _monitorReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _monitorConnected = false;

            var token = _monitorCts.Token;
            _monitorTask = Task.Run(() => MonitorAsync(token), CancellationToken.None);
            _ = _monitorTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task MonitorAsync(CancellationToken cancellationToken)
        {
            var subscription = new LslMonitorSubscription(
                SussexClockAlignmentStreamContract.EchoStreamName,
                SussexClockAlignmentStreamContract.EchoStreamType,
                0);

            try
            {
                await foreach (var reading in _monitorService.MonitorAsync(subscription, cancellationToken).ConfigureAwait(false))
                {
                    if (!_monitorConnected &&
                        (reading.Status.Contains("connected", StringComparison.OrdinalIgnoreCase) ||
                         reading.Status.Contains("Streaming", StringComparison.OrdinalIgnoreCase)))
                    {
                        lock (_sync)
                        {
                            _monitorConnected = true;
                            _monitorReady?.TrySetResult(true);
                        }
                    }

                    if (!reading.Status.Contains("Streaming", StringComparison.OrdinalIgnoreCase) ||
                        reading.ChannelFormat != LslChannelFormat.String ||
                        string.IsNullOrWhiteSpace(reading.TextValue) ||
                        !TryParseEchoPayload(reading.TextValue, out var payload))
                    {
                        continue;
                    }

                    Action<EchoMonitorReading>? subscribers;
                    lock (_sync)
                    {
                        subscribers = _echoReceived;
                    }

                    subscribers?.Invoke(new EchoMonitorReading(
                        payload,
                        reading.Timestamp,
                        reading.SampleTimestampSeconds,
                        reading.ObservedLocalClockSeconds));
                }
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    _monitorReady?.TrySetResult(_monitorConnected);
                }
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    _monitorReady?.TrySetException(exception);
                }

                throw;
            }
            finally
            {
                lock (_sync)
                {
                    _monitorReady?.TrySetResult(_monitorConnected);
                }
            }
        }

        private void Unsubscribe(Action<EchoMonitorReading> handler)
        {
            lock (_sync)
            {
                _echoReceived -= handler;
            }
        }

        private sealed class EchoMonitorSubscription : IDisposable
        {
            private readonly EchoMonitorSession _owner;
            private readonly Action<EchoMonitorReading> _handler;
            private int _disposed;

            public EchoMonitorSubscription(EchoMonitorSession owner, Action<EchoMonitorReading> handler)
            {
                _owner = owner;
                _handler = handler;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                _owner.Unsubscribe(_handler);
            }
        }
    }

    private readonly record struct EchoMonitorState(bool Connected, bool ReadyTimedOut);
    private readonly record struct EchoMonitorReading(
        EchoPayload Payload,
        DateTimeOffset Timestamp,
        double? SampleTimestampSeconds,
        double? ObservedLocalClockSeconds);

    private static class NativeMethods
    {
        internal const int FloatChannelFormat = 1;

        private static readonly Lazy<LslRuntimeState> RuntimeState = new(LoadRuntimeState);

        static NativeMethods()
        {
            LslNativeLibraryResolver.EnsureInstalled(typeof(NativeMethods).Assembly);
        }

        internal static LslRuntimeState GetRuntimeState() => RuntimeState.Value;

        internal static nint CreateStreamInfo(
            string streamName,
            string streamType,
            int channelCount,
            double sampleRate,
            int channelFormat,
            string sourceId)
            => lsl_create_streaminfo(streamName, streamType, channelCount, sampleRate, channelFormat, sourceId);

        internal static nint CreateOutlet(nint streamInfo, int chunkSize, int maxBufferedSeconds)
            => lsl_create_outlet(streamInfo, chunkSize, maxBufferedSeconds);

        internal static void DestroyOutlet(nint outlet) => lsl_destroy_outlet(outlet);

        internal static void DestroyStreamInfo(nint streamInfo) => lsl_destroy_streaminfo(streamInfo);

        internal static double LocalClock() => lsl_local_clock();

        internal static void PushFloatSample(nint outlet, float[] values, double timestamp)
            => lsl_push_sample_ftp(outlet, values, timestamp, 1);

        internal static string GetLastError()
        {
            var pointer = lsl_last_error();
            return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
        }

        internal static void AppendSingleChannelMetadata(nint streamInfo, string label, string unit)
        {
            if (streamInfo == IntPtr.Zero)
            {
                return;
            }

            var desc = lsl_get_desc(streamInfo);
            if (desc == IntPtr.Zero)
            {
                return;
            }

            var channels = lsl_append_child(desc, "channels");
            if (channels == IntPtr.Zero)
            {
                return;
            }

            var channel = lsl_append_child(channels, "channel");
            if (channel == IntPtr.Zero)
            {
                return;
            }

            lsl_append_child_value(channel, "label", label);
            lsl_append_child_value(channel, "unit", unit);
        }

        private static LslRuntimeState LoadRuntimeState()
        {
            if (!OperatingSystem.IsWindows())
            {
                return new LslRuntimeState(false, "Windows clock alignment is only available on Windows.");
            }

            if (!LslNativeLibraryResolver.TryLoad(out _, out var detail))
            {
                return new LslRuntimeState(false, detail);
            }

            return new LslRuntimeState(true, $"Loaded lsl.dll from {detail}.");
        }

        [DllImport("lsl", EntryPoint = "lsl_create_streaminfo", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern nint lsl_create_streaminfo(
            string name,
            string type,
            int channel_count,
            double nominal_srate,
            int channel_format,
            string source_id);

        [DllImport("lsl", EntryPoint = "lsl_create_outlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint lsl_create_outlet(nint stream_info, int chunk_size, int max_buffered);

        [DllImport("lsl", EntryPoint = "lsl_destroy_outlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_outlet(nint outlet);

        [DllImport("lsl", EntryPoint = "lsl_destroy_streaminfo", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_streaminfo(nint stream_info);

        [DllImport("lsl", EntryPoint = "lsl_push_sample_ftp", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lsl_push_sample_ftp(nint outlet, float[] data, double timestamp, int pushthrough);

        [DllImport("lsl", EntryPoint = "lsl_local_clock", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_local_clock();

        [DllImport("lsl", EntryPoint = "lsl_last_error", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_last_error();

        [DllImport("lsl", EntryPoint = "lsl_get_desc", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint lsl_get_desc(nint handle);

        [DllImport("lsl", EntryPoint = "lsl_append_child", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern nint lsl_append_child(nint element, string name);

        [DllImport("lsl", EntryPoint = "lsl_append_child_value", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern nint lsl_append_child_value(nint element, string name, string value);
    }
}

public sealed class PreviewStudyClockAlignmentService : IStudyClockAlignmentService
{
    public LslRuntimeState RuntimeState { get; } = new(false, "Clock alignment is unavailable in preview mode.");

    public Task<OperationOutcome> StartWarmSessionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(
            new OperationOutcome(
                OperationOutcomeKind.Preview,
                "Clock alignment warmup preview only.",
                "Preview mode does not keep a native Sussex clock-alignment transport warm."));

    public Task<OperationOutcome> StopWarmSessionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(
            new OperationOutcome(
                OperationOutcomeKind.Preview,
                "Clock alignment warmup preview only.",
                "Preview mode does not keep a native Sussex clock-alignment transport warm."));

    public Task<StudyClockAlignmentRunResult> RunAsync(
        StudyClockAlignmentRunRequest request,
        IProgress<StudyClockAlignmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(
            new StudyClockAlignmentRunResult(
                new OperationOutcome(
                    OperationOutcomeKind.Preview,
                    "Clock alignment preview only.",
                    "Replace PreviewStudyClockAlignmentService with the Windows liblsl-backed implementation to capture quest-minus-Windows clock offsets."),
                new StudyClockAlignmentSummary(0, 0, null, null, null, null, null, null),
                []));

    public void Dispose()
    {
    }
}

public static class StudyClockAlignmentServiceFactory
{
    public static IStudyClockAlignmentService CreateDefault()
        => OperatingSystem.IsWindows()
            ? new WindowsStudyClockAlignmentService()
            : new PreviewStudyClockAlignmentService();
}
