using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public interface IStudyClockAlignmentService : IDisposable
{
    LslRuntimeState RuntimeState { get; }

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
    private readonly ILslMonitorService _monitorService;
    private readonly Lock _sync = new();
    private nint _streamInfo;
    private nint _outlet;

    public WindowsStudyClockAlignmentService()
        : this(null)
    {
    }

    internal WindowsStudyClockAlignmentService(ILslMonitorService? monitorService)
    {
        _monitorService = monitorService ?? LslMonitorServiceFactory.CreateDefault();
        RuntimeState = NativeMethods.GetRuntimeState();
    }

    public LslRuntimeState RuntimeState { get; }

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

        if (!TryOpenProbeOutlet(out var openOutcome))
        {
            return new StudyClockAlignmentRunResult(openOutcome, BuildSummary([], probesSent: 0), []);
        }

        var probesSent = 0;
        var echoesReceived = 0;
        var pending = new ConcurrentDictionary<int, PendingProbe>();
        var receivedSamples = new ConcurrentQueue<StudyClockAlignmentSample>();
        var monitorShutdownTimedOut = false;
        var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitorTask = Task.Run(
            async () =>
            {
                var subscription = new LslMonitorSubscription(
                    SussexClockAlignmentStreamContract.EchoStreamName,
                    SussexClockAlignmentStreamContract.EchoStreamType,
                    0);

                try
                {
                    await foreach (var reading in _monitorService.MonitorAsync(subscription, monitorCts.Token).ConfigureAwait(false))
                    {
                        if (!reading.Status.Contains("Streaming", StringComparison.OrdinalIgnoreCase) ||
                            reading.ChannelFormat != LslChannelFormat.String ||
                            string.IsNullOrWhiteSpace(reading.TextValue))
                        {
                            continue;
                        }

                        if (!TryParseEchoPayload(reading.TextValue, out var payload) ||
                            !string.Equals(payload.SessionId, request.SessionId, StringComparison.Ordinal) ||
                            !string.Equals(payload.DatasetHash, request.DatasetHash, StringComparison.OrdinalIgnoreCase) ||
                            !pending.TryRemove(payload.Sequence, out var probe))
                        {
                            continue;
                        }

                        var echoReceivedLocalClockSeconds = reading.ObservedLocalClockSeconds ?? NativeMethods.LocalClock();
                        var questEchoLocalClockSeconds = payload.QuestEchoLocalClockSeconds > 0d
                            ? payload.QuestEchoLocalClockSeconds
                            : reading.SampleTimestampSeconds ?? 0d;
                        if (questEchoLocalClockSeconds <= 0d)
                        {
                            questEchoLocalClockSeconds = payload.QuestReceiveLocalClockSeconds;
                        }

                        var questMinusWindowsClockSeconds =
                            ((payload.QuestReceiveLocalClockSeconds - probe.SentLocalClockSeconds) +
                             (questEchoLocalClockSeconds - echoReceivedLocalClockSeconds)) / 2d;
                        var roundTripSeconds =
                            (echoReceivedLocalClockSeconds - probe.SentLocalClockSeconds) -
                            (questEchoLocalClockSeconds - payload.QuestReceiveLocalClockSeconds);

                        var sample = new StudyClockAlignmentSample(
                            request.WindowKind,
                            payload.Sequence,
                            probe.SentAtUtc,
                            probe.SentLocalClockSeconds,
                            reading.Timestamp,
                            echoReceivedLocalClockSeconds,
                            reading.SampleTimestampSeconds,
                            payload.QuestReceivedAtUtc,
                            payload.QuestReceiveLocalClockSeconds,
                            questEchoLocalClockSeconds,
                            questMinusWindowsClockSeconds,
                            roundTripSeconds);
                        receivedSamples.Enqueue(sample);
                        echoesReceived = receivedSamples.Count;

                        progress?.Report(BuildProgress(
                            request,
                            probesSent,
                            echoesReceived,
                            "Clock alignment is receiving Quest echoes.",
                            $"Echo {payload.Sequence} received. RTT {roundTripSeconds * 1000d:0.0} ms, quest-minus-Windows offset {questMinusWindowsClockSeconds * 1000d:0.0} ms."));
                    }
                }
                catch (OperationCanceledException)
                {
                }
            },
            monitorCts.Token);

        try
        {
            progress?.Report(BuildProgress(
                request,
                probesSent,
                echoesReceived,
                "Clock alignment is starting.",
                $"Opening {SussexClockAlignmentStreamContract.ProbeStreamName} / {SussexClockAlignmentStreamContract.ProbeStreamType} and waiting for Quest echoes."));

            var deadline = DateTimeOffset.UtcNow + request.Duration;
            var sequence = 0;

            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                sequence++;
                var sentAtUtc = DateTimeOffset.UtcNow;
                var sentLocalClockSeconds = NativeMethods.LocalClock();
                pending[sequence] = new PendingProbe(sequence, sentAtUtc, sentLocalClockSeconds);
                NativeMethods.PushFloatSample(_outlet, [sequence], sentLocalClockSeconds);
                probesSent = sequence;

                progress?.Report(BuildProgress(
                    request,
                    probesSent,
                    echoesReceived,
                    "Clock alignment is probing both clocks.",
                    $"Sent probe {sequence}. Collecting RTT/offset samples for {request.Duration.TotalSeconds:0} seconds."));

                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                var delay = remaining < request.ProbeInterval ? remaining : request.ProbeInterval;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(BuildProgress(
                request,
                probesSent,
                echoesReceived,
                "Clock alignment is waiting for the last Quest echoes.",
                $"Probe window finished. Waiting up to {request.EchoGracePeriod.TotalSeconds:0.0} seconds for delayed echoes."));

            if (request.EchoGracePeriod > TimeSpan.Zero)
            {
                await Task.Delay(request.EchoGracePeriod, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            monitorCts.Cancel();
            try
            {
                var completedTask = await Task.WhenAny(
                        monitorTask,
                        Task.Delay(MonitorShutdownTimeout, CancellationToken.None))
                    .ConfigureAwait(false);

                if (ReferenceEquals(completedTask, monitorTask))
                {
                    await monitorTask.ConfigureAwait(false);
                    monitorCts.Dispose();
                }
                else
                {
                    monitorShutdownTimedOut = true;
                    _ = monitorTask.ContinueWith(
                        static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                        monitorCts,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
            catch (OperationCanceledException)
            {
                monitorCts.Dispose();
            }
            catch
            {
                monitorCts.Dispose();
                throw;
            }

            CloseProbeOutlet();
        }

        var orderedSamples = receivedSamples
            .OrderBy(sample => sample.ProbeSequence)
            .ToArray();
        var summary = BuildSummary(orderedSamples, probesSent);
        var outcome = BuildOutcome(summary);
        if (monitorShutdownTimedOut)
        {
            outcome = new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Clock alignment completed, but the echo monitor took too long to unwind.",
                string.Join(
                    " ",
                    new[]
                    {
                        outcome.Detail,
                        $"The companion stopped waiting after {MonitorShutdownTimeout.TotalSeconds:0} seconds so the Sussex workflow would not hang."
                    }.Where(value => !string.IsNullOrWhiteSpace(value))));
        }

        progress?.Report(BuildProgress(
            request,
            probesSent,
            orderedSamples.Length,
            outcome.Summary,
            outcome.Detail));

        return new StudyClockAlignmentRunResult(outcome, summary, orderedSamples);
    }

    public void Dispose()
    {
        CloseProbeOutlet();
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

    private static StudyClockAlignmentProgress BuildProgress(
        StudyClockAlignmentRunRequest request,
        int probesSent,
        int echoesReceived,
        string summary,
        string detail)
    {
        var expectedProbeCount = request.ProbeInterval <= TimeSpan.Zero
            ? 1d
            : Math.Max(1d, Math.Ceiling(request.Duration.TotalMilliseconds / request.ProbeInterval.TotalMilliseconds));
        var percentComplete = Math.Clamp(probesSent / expectedProbeCount * 100d, 0d, 100d);
        return new StudyClockAlignmentProgress(percentComplete, probesSent, echoesReceived, summary, detail);
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

    private bool TryOpenProbeOutlet(out OperationOutcome outcome)
    {
        lock (_sync)
        {
            CloseProbeOutlet();

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

    private readonly record struct EchoPayload(
        int Sequence,
        double QuestReceiveLocalClockSeconds,
        double QuestEchoLocalClockSeconds,
        string QuestReceivedAtUtc,
        string SessionId,
        string DatasetHash);

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
