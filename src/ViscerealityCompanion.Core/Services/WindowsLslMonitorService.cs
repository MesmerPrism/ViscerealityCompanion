using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Globalization;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class WindowsLslMonitorService : ILslMonitorService
{
    private readonly ILslMonitorBridge _bridge;
    private readonly Func<long> _nowMilliseconds;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _idleEmitInterval;
    private readonly TimeSpan _valueEmitInterval;
    private readonly TimeSpan _staleReconnectInterval;

    public LslRuntimeState RuntimeState { get; }

    public WindowsLslMonitorService()
        : this(null, null, null, null, null, null)
    {
    }

    internal WindowsLslMonitorService(
        ILslMonitorBridge? bridge = null,
        Func<long>? nowMilliseconds = null,
        TimeSpan? retryDelay = null,
        TimeSpan? idleEmitInterval = null,
        TimeSpan? valueEmitInterval = null,
        TimeSpan? staleReconnectInterval = null)
    {
        _bridge = bridge ?? new LslNativeMonitorBridge();
        _nowMilliseconds = nowMilliseconds ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(1500);
        _idleEmitInterval = idleEmitInterval ?? TimeSpan.FromMilliseconds(1500);
        _valueEmitInterval = valueEmitInterval ?? TimeSpan.FromMilliseconds(50);
        _staleReconnectInterval = staleReconnectInterval ?? TimeSpan.FromMilliseconds(5000);
        RuntimeState = _bridge.GetRuntimeState();
    }

    public async IAsyncEnumerable<LslMonitorReading> MonitorAsync(
        LslMonitorSubscription subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var runtimeState = RuntimeState;
        if (!runtimeState.Available)
        {
            yield return BuildReading(subscription, "LSL unavailable.", runtimeState.Detail);
            yield break;
        }

        yield return BuildReading(
            subscription,
            "Resolving LSL stream...",
            $"Searching for `{subscription.StreamName}` / `{subscription.StreamType}` over the Windows liblsl runtime.");

        while (!cancellationToken.IsCancellationRequested)
        {
            LslMonitorSession? session;
            LslMonitorReading? openErrorReading = null;
            try
            {
                session = _bridge.OpenStream(subscription);
            }
            catch (ArgumentException ex)
            {
                openErrorReading = BuildReading(subscription, "LSL channel unavailable.", ex.Message);
                session = null;
            }
            catch (Exception ex)
            {
                openErrorReading = BuildReading(subscription, "LSL monitor error.", ex.Message);
                session = null;
            }

            if (openErrorReading is not null)
            {
                yield return openErrorReading;
                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (session is null)
            {
                yield return BuildReading(
                    subscription,
                    "LSL stream not found.",
                    $"No visible stream matched `{subscription.StreamName}` / `{subscription.StreamType}`. The monitor will keep retrying.");
                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            yield return BuildReading(
                subscription,
                "LSL stream connected.",
                $"Connected to `{session.ResolvedName}` / `{session.ResolvedType}` with {session.ChannelCount} channel(s).",
                sampleRateHz: session.SampleRateHz);

            var sessionOpenedAtMs = _nowMilliseconds();
            var lastIdleEmissionMs = sessionOpenedAtMs;
            var lastSampleReceivedAtMs = sessionOpenedAtMs;
            var openNewSession = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                var shouldReconnect = false;
                var waitingStateEmitted = false;
                var canceled = false;
                LslMonitorReading? sampleReading = null;
                LslMonitorReading? sessionErrorReading = null;

                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var sample = _bridge.PullSample(session);
                        var nowMs = _nowMilliseconds();

                        if (sample is null)
                        {
                            if (!waitingStateEmitted && nowMs - lastIdleEmissionMs >= _idleEmitInterval.TotalMilliseconds)
                            {
                                lastIdleEmissionMs = nowMs;
                                waitingStateEmitted = true;
                                break;
                            }

                            if (nowMs - lastSampleReceivedAtMs >= _staleReconnectInterval.TotalMilliseconds)
                            {
                                shouldReconnect = true;
                                break;
                            }

                            continue;
                        }

                        lastSampleReceivedAtMs = nowMs;
                        waitingStateEmitted = false;

                        if (sample.ChannelFormat != LslChannelFormat.String &&
                            nowMs - lastIdleEmissionMs < _valueEmitInterval.TotalMilliseconds)
                        {
                            continue;
                        }

                        lastIdleEmissionMs = nowMs;
                        sampleReading = BuildReading(subscription, sample, session.SampleRateHz);
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }
                catch (Exception ex)
                {
                    sessionErrorReading = BuildReading(subscription, "LSL stream lost.", ex.Message, sampleRateHz: session.SampleRateHz);
                }

                if (canceled)
                {
                    _bridge.CloseStream(session);
                    yield break;
                }

                if (sessionErrorReading is not null)
                {
                    _bridge.CloseStream(session);
                    yield return sessionErrorReading;
                    await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
                    openNewSession = true;
                    break;
                }

                if (sampleReading is not null)
                {
                    yield return sampleReading;
                    continue;
                }

                if (shouldReconnect)
                {
                    var staleSeconds = _staleReconnectInterval.TotalSeconds % 1d == 0d
                        ? ((int)_staleReconnectInterval.TotalSeconds).ToString()
                        : _staleReconnectInterval.TotalSeconds.ToString("0.0");

                    _bridge.CloseStream(session);
                    yield return BuildReading(
                        subscription,
                        "LSL stream idle. Re-resolving...",
                        $"No sample arrived for {staleSeconds}s, so the app is closing the current inlet and resolving the stream again.",
                        sampleRateHz: session.SampleRateHz);
                    yield return BuildReading(
                        subscription,
                        "Resolving LSL stream...",
                        $"The previous inlet was idle, so the app is searching the network again for `{subscription.StreamName}` / `{subscription.StreamType}`.");
                    await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
                    openNewSession = true;
                    break;
                }

                if (waitingStateEmitted)
                {
                    yield return BuildReading(
                        subscription,
                        "Waiting for LSL samples...",
                        "The stream is connected, but no new sample arrived during the last polling window. The monitor will re-resolve if this stays idle.",
                        sampleRateHz: session.SampleRateHz);
                    continue;
                }

                break;
            }

            if (openNewSession)
            {
                continue;
            }

            _bridge.CloseStream(session);
        }
    }

    private LslMonitorReading BuildReading(
        LslMonitorSubscription subscription,
        LslMonitorSample sample,
        float sampleRateHz)
        => new(
            "Streaming LSL sample.",
            sample.ChannelFormat == LslChannelFormat.String
                ? $"String sample from `{subscription.StreamName}` / `{subscription.StreamType}`."
                : $"Numeric sample from `{subscription.StreamName}` / `{subscription.StreamType}`.",
            sample.NumericValue,
            sampleRateHz,
            DateTimeOffset.UtcNow,
            sample.TextValue,
            sample.SampleValues,
            sample.ChannelFormat,
            sample.TimestampSeconds,
            _bridge.GetLocalClockSeconds());

    private static LslMonitorReading BuildReading(
        LslMonitorSubscription subscription,
        string status,
        string detail,
        float? value = null,
        float sampleRateHz = 0f)
        => new(
            status,
            detail,
            value,
            sampleRateHz,
            DateTimeOffset.UtcNow,
            value?.ToString(CultureInfo.InvariantCulture),
            value is null ? Array.Empty<string>() : [value.Value.ToString(CultureInfo.InvariantCulture)],
            value is null ? LslChannelFormat.Unknown : LslChannelFormat.Float32);
}

internal interface ILslMonitorBridge
{
    LslRuntimeState GetRuntimeState();
    double? GetLocalClockSeconds();
    LslMonitorSession? OpenStream(LslMonitorSubscription subscription);
    LslMonitorSample? PullSample(LslMonitorSession session);
    void CloseStream(LslMonitorSession session);
}

internal sealed class LslNativeMonitorBridge : ILslMonitorBridge
{
    private const int ResolveBufferSize = 16;
    private const int MaxBufferedSeconds = 5;
    private const int MaxChunkLength = 1;
    private const int RecoverLostStreams = 1;
    private const double OpenTimeoutSeconds = 2d;
    private const double PullTimeoutSeconds = 0.5d;
    private readonly LslRuntimeState _runtimeState = NativeMethods.GetRuntimeState();

    public LslRuntimeState GetRuntimeState() => _runtimeState;
    public double? GetLocalClockSeconds() => _runtimeState.Available ? NativeMethods.GetLocalClockSeconds() : null;

    public LslMonitorSession? OpenStream(LslMonitorSubscription subscription)
    {
        if (!_runtimeState.Available)
        {
            throw new InvalidOperationException(_runtimeState.Detail);
        }

        var streamName = subscription.StreamName.Trim();
        var streamType = subscription.StreamType.Trim();

        if (string.IsNullOrWhiteSpace(streamName) && string.IsNullOrWhiteSpace(streamType))
        {
            throw new ArgumentException("At least a stream name or type is required to resolve an LSL stream.");
        }

        if (subscription.ChannelIndex < 0)
        {
            throw new ArgumentException("LSL channel index must be zero or greater.");
        }

        var resolvedInfos = new IntPtr[ResolveBufferSize];
        var resolveCount = ResolveCandidates(streamName, streamType, resolvedInfos);
        if (resolveCount < 0)
        {
            DestroyResolvedInfos(resolvedInfos);
            throw new InvalidOperationException(BuildErrorMessage("Could not resolve the requested LSL stream", resolveCount));
        }

        if (resolveCount == 0)
        {
            DestroyResolvedInfos(resolvedInfos);
            return null;
        }

        var selectedInfo = nint.Zero;
        for (var index = 0; index < resolveCount; index++)
        {
            var candidate = resolvedInfos[index];
            if (candidate != IntPtr.Zero && MatchesRequestedStream(candidate, streamName, streamType))
            {
                selectedInfo = candidate;
                break;
            }
        }

        if (selectedInfo == IntPtr.Zero)
        {
            DestroyResolvedInfos(resolvedInfos);
            return null;
        }

        var channelCount = NativeMethods.GetChannelCount(selectedInfo);
        if (channelCount <= 0)
        {
            DestroyResolvedInfos(resolvedInfos);
            throw new InvalidOperationException("Resolved LSL stream did not report any channels.");
        }

        if (subscription.ChannelIndex >= channelCount)
        {
            DestroyResolvedInfos(resolvedInfos);
            throw new ArgumentException("Requested LSL channel index is outside the resolved stream channel range.");
        }

        var inlet = NativeMethods.CreateInlet(selectedInfo, MaxBufferedSeconds, MaxChunkLength, RecoverLostStreams);
        if (inlet == IntPtr.Zero)
        {
            DestroyResolvedInfos(resolvedInfos);
            throw new InvalidOperationException($"Could not create an LSL inlet. {NativeMethods.GetLastError()}".Trim());
        }

        var errorCode = 0;
        NativeMethods.OpenStream(inlet, OpenTimeoutSeconds, ref errorCode);
        if (errorCode != 0)
        {
            NativeMethods.DestroyInlet(inlet);
            DestroyResolvedInfos(resolvedInfos);
            throw new InvalidOperationException(BuildErrorMessage("Could not open the resolved LSL stream", errorCode));
        }

        errorCode = NativeMethods.SetPostProcessing(inlet, (uint)LslProcessingOptions.All);
        if (errorCode != 0)
        {
            NativeMethods.DestroyInlet(inlet);
            DestroyResolvedInfos(resolvedInfos);
            throw new InvalidOperationException(BuildErrorMessage("Could not configure LSL inlet post-processing", errorCode));
        }

        var resolvedName = NativeMethods.GetName(selectedInfo);
        var resolvedType = NativeMethods.GetTypeName(selectedInfo);
        var sampleRateHz = (float)NativeMethods.GetNominalSampleRate(selectedInfo);
        var channelFormat = MapChannelFormat(NativeMethods.GetChannelFormat(selectedInfo));
        DestroyResolvedInfos(resolvedInfos);

        return new LslMonitorSession(inlet, resolvedName, resolvedType, channelCount, subscription.ChannelIndex, sampleRateHz, channelFormat);
    }

    public LslMonitorSample? PullSample(LslMonitorSession session)
    {
        if (!_runtimeState.Available)
        {
            throw new InvalidOperationException(_runtimeState.Detail);
        }

        if (session.Handle == IntPtr.Zero)
        {
            throw new ArgumentException("LSL monitor handle is not valid.");
        }

        return session.ChannelFormat switch
        {
            LslChannelFormat.String => PullStringSample(session),
            _ => PullFloatSample(session)
        };
    }

    public void CloseStream(LslMonitorSession session)
    {
        if (session.Handle == IntPtr.Zero || !_runtimeState.Available)
        {
            return;
        }

        NativeMethods.DestroyInlet(session.Handle);
    }

    private static int ResolveCandidates(string streamName, string streamType, IntPtr[] resolvedInfos)
        => !string.IsNullOrWhiteSpace(streamName)
            ? NativeMethods.ResolveByProperty(resolvedInfos, (uint)resolvedInfos.Length, "name", streamName, 1, 1d)
            : NativeMethods.ResolveByProperty(resolvedInfos, (uint)resolvedInfos.Length, "type", streamType, 1, 1d);

    private static bool MatchesRequestedStream(nint streamInfo, string requestedName, string requestedType)
    {
        var candidateName = NativeMethods.GetName(streamInfo);
        var candidateType = NativeMethods.GetTypeName(streamInfo);
        var nameMatches = string.IsNullOrWhiteSpace(requestedName) || string.Equals(candidateName, requestedName, StringComparison.Ordinal);
        var typeMatches = string.IsNullOrWhiteSpace(requestedType) || string.Equals(candidateType, requestedType, StringComparison.Ordinal);
        return nameMatches && typeMatches;
    }

    private static void DestroyResolvedInfos(IEnumerable<IntPtr> streamInfos)
    {
        foreach (var streamInfo in streamInfos)
        {
            if (streamInfo != IntPtr.Zero)
            {
                NativeMethods.DestroyStreamInfo(streamInfo);
            }
        }
    }

    private static string BuildErrorMessage(string prefix, int errorCode)
    {
        var lastError = NativeMethods.GetLastError();
        var message = $"{prefix} ({GetErrorCodeName(errorCode)})";
        return string.IsNullOrWhiteSpace(lastError) ? message : $"{message}: {lastError}";
    }

    private static string GetErrorCodeName(int errorCode)
        => errorCode switch
        {
            0 => "no error",
            -1 => "timeout",
            -2 => "stream lost",
            -3 => "invalid argument",
            -4 => "internal error",
            _ => "unknown error"
        };

    private static LslChannelFormat MapChannelFormat(int channelFormat)
        => channelFormat switch
        {
            1 => LslChannelFormat.Float32,
            3 => LslChannelFormat.String,
            _ => LslChannelFormat.Unknown
        };

    private static LslMonitorSample? PullFloatSample(LslMonitorSession session)
    {
        var sampleBuffer = new float[session.ChannelCount];
        var errorCode = 0;
        var timestamp = NativeMethods.PullSample(session.Handle, sampleBuffer, sampleBuffer.Length, PullTimeoutSeconds, ref errorCode);
        if (errorCode != 0)
        {
            throw new InvalidOperationException(BuildErrorMessage("Could not pull a sample from the LSL stream", errorCode));
        }

        if (timestamp == 0d)
        {
            return null;
        }

        var values = sampleBuffer
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        return new LslMonitorSample(
            timestamp,
            sampleBuffer[session.SelectedChannelIndex],
            values[session.SelectedChannelIndex],
            values,
            LslChannelFormat.Float32);
    }

    private static LslMonitorSample? PullStringSample(LslMonitorSession session)
    {
        var sampleBuffer = new IntPtr[session.ChannelCount];
        var lengths = new uint[session.ChannelCount];
        var errorCode = 0;
        var timestamp = NativeMethods.PullStringSample(session.Handle, sampleBuffer, lengths, sampleBuffer.Length, PullTimeoutSeconds, ref errorCode);
        if (errorCode != 0)
        {
            throw new InvalidOperationException(BuildErrorMessage("Could not pull a string sample from the LSL stream", errorCode));
        }

        if (timestamp == 0d)
        {
            return null;
        }

        var values = new string[session.ChannelCount];
        for (var index = 0; index < sampleBuffer.Length; index++)
        {
            if (sampleBuffer[index] == IntPtr.Zero)
            {
                values[index] = string.Empty;
                continue;
            }

            try
            {
                values[index] = ReadLslString(sampleBuffer[index], lengths[index]);
            }
            finally
            {
                NativeMethods.DestroyString(sampleBuffer[index]);
            }
        }

        return new LslMonitorSample(
            timestamp,
            null,
            values[session.SelectedChannelIndex],
            values,
            LslChannelFormat.String);
    }

    private static string ReadLslString(IntPtr pointer, uint length)
    {
        if (pointer == IntPtr.Zero || length == 0)
        {
            return string.Empty;
        }

        var raw = Marshal.PtrToStringAnsi(pointer, checked((int)length)) ?? string.Empty;
        var nulIndex = raw.IndexOf('\0');
        return nulIndex >= 0 ? raw[..nulIndex] : raw;
    }

    [Flags]
    private enum LslProcessingOptions : uint
    {
        ClockSync = 1,
        Dejitter = 2,
        Monotonize = 4,
        ThreadSafe = 8,
        All = ClockSync | Dejitter | Monotonize | ThreadSafe
    }

    private static class NativeMethods
    {
        private static readonly Lazy<LslRuntimeState> RuntimeState = new(LoadRuntimeState);

        static NativeMethods()
        {
            LslNativeLibraryResolver.EnsureInstalled(typeof(NativeMethods).Assembly);
        }

        internal static LslRuntimeState GetRuntimeState() => RuntimeState.Value;
        internal static double GetLocalClockSeconds() => lsl_local_clock();

        private static LslRuntimeState LoadRuntimeState()
        {
            if (!OperatingSystem.IsWindows())
            {
                return new LslRuntimeState(false, "Windows liblsl monitoring is only available on Windows.");
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
                return new LslRuntimeState(false, $"lsl.dll loaded from {detail}, but initialization failed: {ex.Message}");
            }
        }

        [DllImport("lsl", EntryPoint = "lsl_last_error", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_last_error();

        [DllImport("lsl", EntryPoint = "lsl_local_clock", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_local_clock();

        [DllImport("lsl", EntryPoint = "lsl_library_info", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_library_info();

        [DllImport("lsl", EntryPoint = "lsl_resolve_byprop", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int lsl_resolve_byprop(
            [Out] IntPtr[] buffer,
            uint bufferElements,
            string property,
            string value,
            int minimum,
            double timeoutSeconds);

        [DllImport("lsl", EntryPoint = "lsl_destroy_streaminfo", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_streaminfo(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_get_name", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_get_name(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_get_type", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_get_type(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_get_channel_count", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lsl_get_channel_count(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_get_nominal_srate", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_get_nominal_srate(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_get_channel_format", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lsl_get_channel_format(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_create_inlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint lsl_create_inlet(nint streamInfo, int maxBufferSeconds, int maxChunkLength, int recoverLostStreams);

        [DllImport("lsl", EntryPoint = "lsl_destroy_inlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_inlet(nint inlet);

        [DllImport("lsl", EntryPoint = "lsl_open_stream", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_open_stream(nint inlet, double timeoutSeconds, ref int errorCode);

        [DllImport("lsl", EntryPoint = "lsl_set_postprocessing", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lsl_set_postprocessing(nint inlet, uint flags);

        [DllImport("lsl", EntryPoint = "lsl_pull_sample_f", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_pull_sample_f(
            nint inlet,
            [Out] float[] buffer,
            int bufferElements,
            double timeoutSeconds,
            ref int errorCode);

        [DllImport("lsl", EntryPoint = "lsl_pull_sample_buf", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_pull_sample_buf(
            nint inlet,
            [Out] IntPtr[] buffer,
            [Out] uint[] bufferLengths,
            int bufferElements,
            double timeoutSeconds,
            ref int errorCode);

        [DllImport("lsl", EntryPoint = "lsl_destroy_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_string(IntPtr value);

        internal static string GetLastError() => PtrToString(lsl_last_error());

        internal static string GetLibraryInfo() => PtrToString(lsl_library_info());

        internal static int ResolveByProperty(IntPtr[] buffer, uint bufferElements, string property, string value, int minimum, double timeoutSeconds)
            => lsl_resolve_byprop(buffer, bufferElements, property, value, minimum, timeoutSeconds);

        internal static void DestroyStreamInfo(nint streamInfo) => lsl_destroy_streaminfo(streamInfo);

        internal static string GetName(nint streamInfo) => PtrToString(lsl_get_name(streamInfo));

        internal static string GetTypeName(nint streamInfo) => PtrToString(lsl_get_type(streamInfo));

        internal static int GetChannelCount(nint streamInfo) => lsl_get_channel_count(streamInfo);

        internal static double GetNominalSampleRate(nint streamInfo) => lsl_get_nominal_srate(streamInfo);

        internal static int GetChannelFormat(nint streamInfo) => lsl_get_channel_format(streamInfo);

        internal static nint CreateInlet(nint streamInfo, int maxBufferSeconds, int maxChunkLength, int recoverLostStreams)
            => lsl_create_inlet(streamInfo, maxBufferSeconds, maxChunkLength, recoverLostStreams);

        internal static void DestroyInlet(nint inlet) => lsl_destroy_inlet(inlet);

        internal static void OpenStream(nint inlet, double timeoutSeconds, ref int errorCode) => lsl_open_stream(inlet, timeoutSeconds, ref errorCode);

        internal static int SetPostProcessing(nint inlet, uint flags) => lsl_set_postprocessing(inlet, flags);

        internal static double PullSample(nint inlet, float[] buffer, int bufferElements, double timeoutSeconds, ref int errorCode)
            => lsl_pull_sample_f(inlet, buffer, bufferElements, timeoutSeconds, ref errorCode);

        internal static double PullStringSample(nint inlet, IntPtr[] buffer, uint[] bufferLengths, int bufferElements, double timeoutSeconds, ref int errorCode)
            => lsl_pull_sample_buf(inlet, buffer, bufferLengths, bufferElements, timeoutSeconds, ref errorCode);

        internal static void DestroyString(IntPtr value) => lsl_destroy_string(value);

        private static string PtrToString(IntPtr pointer)
            => pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
    }
}
