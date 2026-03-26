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
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(50);
    private const float PulseBaseline01 = 0.1f;
    private const float PulsePeak01 = 1.0f;
    private const float PulsePeriodSeconds = 1.0f;
    private const float PulsePeakWindowSeconds = 0.08f;
    private const float PulseDecayWindowSeconds = 0.24f;
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
                    $"Streaming synthetic heartbeat pulse values on {streamName} / {streamType}.");
            }

            _lastFaultDetail = string.Empty;
            _streamInfo = TestOutletNativeMethods.CreateStreamInfo(streamName, streamType, 1, 0d, TestOutletNativeMethods.FloatChannelFormat, sourceId);
            if (_streamInfo == IntPtr.Zero)
            {
                return new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Could not create the Windows TEST sender stream info.",
                    TestOutletNativeMethods.GetLastError());
            }

            _outlet = TestOutletNativeMethods.CreateOutlet(_streamInfo, 0, 120);
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
            $"Streaming synthetic heartbeat pulse values on {streamName} / {streamType}. Use this only for bench checks.");
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
                    "No synthetic LSL heartbeat-pulse stream is active.");
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
            "Synthetic heartbeat-pulse publishing has stopped.");
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SampleInterval);
        var startedAt = DateTime.UtcNow;

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var elapsedSeconds = (float)(DateTime.UtcNow - startedAt).TotalSeconds;
                var phaseSeconds = elapsedSeconds % PulsePeriodSeconds;
                float value = phaseSeconds switch
                {
                    <= PulsePeakWindowSeconds => PulsePeak01,
                    <= PulseDecayWindowSeconds => PulseBaseline01 + (PulsePeak01 - PulseBaseline01) *
                        MathF.Exp(-(phaseSeconds - PulsePeakWindowSeconds) / 0.05f),
                    _ => PulseBaseline01
                };

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
            "No real LSL heartbeat-pulse samples will be published in preview mode.");

    public OperationOutcome Stop()
        => new(
            OperationOutcomeKind.Preview,
            "Windows TEST sender unavailable.",
            "No real LSL heartbeat-pulse samples are active in preview mode.");

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
