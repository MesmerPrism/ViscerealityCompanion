using System.Runtime.InteropServices;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public interface ITestLslSignalService : IDisposable
{
    event EventHandler? StateChanged;

    LslRuntimeState RuntimeState { get; }
    bool IsRunning { get; }
    float LastValue { get; }
    DateTimeOffset? LastSentAtUtc { get; }
    string LastFaultDetail { get; }

    OperationOutcome Start(string streamName, string streamType, string sourceId);
    OperationOutcome Stop();
}

public sealed class WindowsTestLslSignalService : ITestLslSignalService
{
    private const int StreamChannelCount = 1;
    private const int OutletChunkSize = 1;
    private const int OutletMaxBuffered = 1;
    private readonly Lock _sync = new();
    private nint _streamInfo;
    private nint _outlet;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private float _lastValue;
    private DateTimeOffset? _lastSentAtUtc;
    private string _lastFaultDetail = string.Empty;

    public WindowsTestLslSignalService()
    {
        RuntimeState = TestOutletNativeMethods.GetRuntimeState();
    }

    public event EventHandler? StateChanged;

    public LslRuntimeState RuntimeState { get; }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _outlet != IntPtr.Zero;
            }
        }
    }

    public float LastValue
    {
        get
        {
            lock (_sync)
            {
                return _lastValue;
            }
        }
    }

    public DateTimeOffset? LastSentAtUtc
    {
        get
        {
            lock (_sync)
            {
                return _lastSentAtUtc;
            }
        }
    }

    public string LastFaultDetail
    {
        get
        {
            lock (_sync)
            {
                return _lastFaultDetail;
            }
        }
    }

    public OperationOutcome Start(string streamName, string streamType, string sourceId)
    {
        if (!RuntimeState.Available)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Windows TEST sender unavailable.",
                RuntimeState.Detail);
        }

        lock (_sync)
        {
            if (_outlet != IntPtr.Zero)
            {
                return new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Windows TEST sender already active.",
                    $"Streaming synthetic smoothed-HRV packets on {streamName} / {streamType} with irregular beat-timed spacing.");
            }

            _lastFaultDetail = string.Empty;
            _streamInfo = TestOutletNativeMethods.CreateStreamInfo(
                streamName,
                streamType,
                StreamChannelCount,
                0d,
                TestOutletNativeMethods.FloatChannelFormat,
                sourceId);
            if (_streamInfo == IntPtr.Zero)
            {
                return new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Could not create the Windows TEST sender stream info.",
                    TestOutletNativeMethods.GetLastError());
            }

            TestOutletNativeMethods.AppendSingleChannelMetadata(
                _streamInfo,
                HrvBiofeedbackStreamContract.ChannelLabel,
                HrvBiofeedbackStreamContract.ChannelUnit);

            _outlet = TestOutletNativeMethods.CreateOutlet(_streamInfo, OutletChunkSize, OutletMaxBuffered);
            if (_outlet == IntPtr.Zero)
            {
                TestOutletNativeMethods.DestroyStreamInfo(_streamInfo);
                _streamInfo = IntPtr.Zero;
                return new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Could not create the Windows TEST sender outlet.",
                    TestOutletNativeMethods.GetLastError());
            }

            _lastValue = 0f;
            _lastSentAtUtc = null;
            _pumpCts = new CancellationTokenSource();
            _pumpTask = Task.Run(() => PumpAsync(_pumpCts.Token));
        }

        RaiseStateChanged();
        return new OperationOutcome(
            OperationOutcomeKind.Success,
            "Windows TEST sender active.",
            $"Streaming synthetic smoothed-HRV packets on {streamName} / {streamType}. Samples follow an irregular beat-timed profile with a {HrvBiofeedbackStreamContract.FeedbackDispatchDelayMs} ms post-beat dispatch offset.");
    }

    public OperationOutcome Stop()
    {
        CancellationTokenSource? pumpCts;

        lock (_sync)
        {
            if (_outlet == IntPtr.Zero)
            {
                return new OperationOutcome(
                    OperationOutcomeKind.Preview,
                    "Windows TEST sender already off.",
                    "No synthetic smoothed-HRV LSL stream is active.");
            }

            pumpCts = _pumpCts;
            _pumpCts = null;
            _pumpTask = null;
        }

        pumpCts?.Cancel();

        lock (_sync)
        {
            DisposeNativeHandles();
        }

        RaiseStateChanged();
        return new OperationOutcome(
            OperationOutcomeKind.Success,
            "Windows TEST sender stopped.",
            "Synthetic smoothed-HRV publishing has stopped.");
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        var profile = new MockHrvBiofeedbackProfile();

        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(HrvBiofeedbackStreamContract.FeedbackDispatchDelayMs),
                cancellationToken).ConfigureAwait(false);

            while (true)
            {
                var packet = profile.NextPacket();
                PushBeatSample(packet.Value01);
                await Task.Delay(packet.IntervalUntilNextSend, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _lastFaultDetail = ex.Message;
                DisposeNativeHandles();
            }

            RaiseStateChanged();
        }
    }

    private void PushBeatSample(float value)
    {
        lock (_sync)
        {
            if (_outlet == IntPtr.Zero)
            {
                return;
            }

            TestOutletNativeMethods.PushFloatSample(_outlet, [value], TestOutletNativeMethods.LocalClock());
            _lastValue = value;
            _lastSentAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private void DisposeNativeHandles()
    {
        if (_outlet != IntPtr.Zero)
        {
            TestOutletNativeMethods.DestroyOutlet(_outlet);
            _outlet = IntPtr.Zero;
        }

        if (_streamInfo != IntPtr.Zero)
        {
            TestOutletNativeMethods.DestroyStreamInfo(_streamInfo);
            _streamInfo = IntPtr.Zero;
        }
    }

    private void RaiseStateChanged()
        => StateChanged?.Invoke(this, EventArgs.Empty);

    private readonly record struct MockHrvPacket(float Value01, TimeSpan IntervalUntilNextSend, bool IsHoldover);

    private sealed class MockHrvBiofeedbackProfile
    {
        private const float MinIbiMs = 400f;
        private const float MaxIbiMs = 1200f;
        private const float MeanIbiMs = 910f;
        private const float EmaAlpha = 0.3f;
        private const int WarmupBeatCount = 5;

        private readonly Random _random = new(20260323);
        private int _beatIndex;
        private float _phase = 0.4f;
        private float _lastIbiMs = MeanIbiMs;
        private float? _smoothedValue;
        private float _lastPublishedValue = 0.5f;

        public MockHrvPacket NextPacket()
        {
            var ibiMs = BuildIbiMs();
            var rawFeedback = BuildRawFeedback();

            var isWarmup = _beatIndex < WarmupBeatCount;
            var isHoldover = isWarmup || ShouldReusePriorFeedback();
            float value01;

            if (_smoothedValue is null)
            {
                _smoothedValue = rawFeedback;
            }
            else
            {
                _smoothedValue = (EmaAlpha * rawFeedback) + ((1f - EmaAlpha) * _smoothedValue.Value);
            }

            if (isWarmup)
            {
                var settle01 = (_beatIndex + 1f) / WarmupBeatCount;
                var blend = settle01 * 0.35f;
                value01 = 0.5f + ((_smoothedValue.Value - 0.5f) * blend);
                _lastPublishedValue = value01;
            }
            else if (isHoldover)
            {
                value01 = _lastPublishedValue;
            }
            else
            {
                value01 = _smoothedValue.Value;
                _lastPublishedValue = value01;
            }

            _beatIndex++;
            return new MockHrvPacket(
                Math.Clamp(value01, 0.05f, 0.95f),
                TimeSpan.FromMilliseconds(ibiMs),
                isHoldover);
        }

        private float BuildIbiMs()
        {
            _phase += 0.57f + NextCentered(0.05f);

            var respiration = MathF.Sin(_phase);
            var harmonic = MathF.Sin((_phase * 0.5f) + 0.9f);
            var jitterMs = NextCentered(28f);

            var ibiMs = MeanIbiMs + (125f * respiration) + (42f * harmonic) + jitterMs;
            ibiMs = Math.Clamp(ibiMs, MinIbiMs, MaxIbiMs);
            _lastIbiMs = ibiMs;
            return ibiMs;
        }

        private float BuildRawFeedback()
        {
            var zScoreLike =
                (1.12f * MathF.Sin(_phase - 0.35f)) +
                (0.34f * MathF.Sin((_phase * 0.5f) + 1.1f)) +
                NextCentered(0.09f);

            var mapped01 = (zScoreLike + 2f) / 4f;
            return Math.Clamp(mapped01, 0.05f, 0.95f);
        }

        private bool ShouldReusePriorFeedback()
            => _beatIndex > WarmupBeatCount && (_random.NextDouble() < 0.12d || (_lastIbiMs > 1040f && _random.NextDouble() < 0.2d));

        private float NextCentered(float halfRange)
            => ((float)_random.NextDouble() * 2f * halfRange) - halfRange;
    }

    private static class TestOutletNativeMethods
    {
        internal const int FloatChannelFormat = 1;
        private static readonly Lazy<LslRuntimeState> RuntimeState = new(LoadRuntimeState);

        static TestOutletNativeMethods()
        {
            LslNativeLibraryResolver.EnsureInstalled(typeof(TestOutletNativeMethods).Assembly);
        }

        internal static LslRuntimeState GetRuntimeState() => RuntimeState.Value;

        private static LslRuntimeState LoadRuntimeState()
        {
            if (!OperatingSystem.IsWindows())
            {
                return new LslRuntimeState(false, "Windows TEST sender is only available on Windows.");
            }

            if (!LslNativeLibraryResolver.TryLoad(out _, out var detail))
            {
                return new LslRuntimeState(false, detail);
            }

            try
            {
                var info = GetLibraryInfo();
                var runtimeDetail = string.IsNullOrWhiteSpace(info)
                    ? $"Loaded lsl.dll from {detail}."
                    : $"{info} Loaded from {detail}.";
                return new LslRuntimeState(true, runtimeDetail);
            }
            catch (Exception ex)
            {
                return new LslRuntimeState(false, $"lsl.dll loaded from {detail}, but the TEST sender could not initialize: {ex.Message}");
            }
        }

        [DllImport("lsl", EntryPoint = "lsl_last_error", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_last_error();

        [DllImport("lsl", EntryPoint = "lsl_library_info", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_library_info();

        [DllImport("lsl", EntryPoint = "lsl_create_streaminfo", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern nint lsl_create_streaminfo(
            string name,
            string type,
            int channelCount,
            double nominalRate,
            int channelFormat,
            string sourceId);

        [DllImport("lsl", EntryPoint = "lsl_destroy_streaminfo", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_streaminfo(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_create_outlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint lsl_create_outlet(nint streamInfo, int chunkSize, int maxBuffered);

        [DllImport("lsl", EntryPoint = "lsl_destroy_outlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_outlet(nint outlet);

        [DllImport("lsl", EntryPoint = "lsl_get_desc", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint lsl_get_desc(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_append_child", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern nint lsl_append_child(nint element, string name);

        [DllImport("lsl", EntryPoint = "lsl_append_child_value", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern nint lsl_append_child_value(nint element, string name, string value);

        [DllImport("lsl", EntryPoint = "lsl_local_clock", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_local_clock();

        [DllImport("lsl", EntryPoint = "lsl_push_sample_ftp", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lsl_push_sample_ftp(nint outlet, float[] data, double timestamp, int pushThrough);

        internal static string GetLastError()
        {
            var ptr = lsl_last_error();
            return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }

        internal static string GetLibraryInfo()
        {
            var ptr = lsl_library_info();
            return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }

        internal static nint CreateStreamInfo(string name, string type, int channelCount, double nominalRate, int channelFormat, string sourceId)
            => lsl_create_streaminfo(name, type, channelCount, nominalRate, channelFormat, sourceId);

        internal static void DestroyStreamInfo(nint streamInfo) => lsl_destroy_streaminfo(streamInfo);

        internal static nint CreateOutlet(nint streamInfo, int chunkSize, int maxBuffered)
            => lsl_create_outlet(streamInfo, chunkSize, maxBuffered);

        internal static void DestroyOutlet(nint outlet) => lsl_destroy_outlet(outlet);

        internal static void AppendSingleChannelMetadata(nint streamInfo, string label, string unit)
        {
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

        internal static double LocalClock() => lsl_local_clock();

        internal static void PushFloatSample(nint outlet, float[] values, double timestamp)
            => lsl_push_sample_ftp(outlet, values, timestamp, 1);
    }
}

public sealed class PreviewTestLslSignalService : ITestLslSignalService
{
    public event EventHandler? StateChanged
    {
        add { }
        remove { }
    }

    public LslRuntimeState RuntimeState { get; } = new(false, "Preview TEST sender. No liblsl runtime loaded.");
    public bool IsRunning => false;
    public float LastValue => 0f;
    public DateTimeOffset? LastSentAtUtc => null;
    public string LastFaultDetail => string.Empty;

    public OperationOutcome Start(string streamName, string streamType, string sourceId)
        => new(
            OperationOutcomeKind.Preview,
            "Windows TEST sender unavailable.",
            "No real smoothed-HRV LSL samples will be published in preview mode.");

    public OperationOutcome Stop()
        => new(
            OperationOutcomeKind.Preview,
            "Windows TEST sender unavailable.",
            "No real smoothed-HRV LSL samples are active in preview mode.");

    public void Dispose()
    {
    }
}

public static class TestLslSignalServiceFactory
{
    public static ITestLslSignalService CreateDefault()
        => OperatingSystem.IsWindows()
            ? new WindowsTestLslSignalService()
            : new PreviewTestLslSignalService();
}
