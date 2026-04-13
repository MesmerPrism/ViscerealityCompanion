using System.Runtime.InteropServices;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public interface ILslStreamDiscoveryService
{
    LslRuntimeState RuntimeState { get; }

    IReadOnlyList<LslVisibleStreamInfo> Discover(LslStreamDiscoveryRequest request);
}

public sealed class WindowsLslStreamDiscoveryService : ILslStreamDiscoveryService
{
    private readonly ILslStreamDiscoveryBridge _bridge;

    public WindowsLslStreamDiscoveryService()
        : this(null)
    {
    }

    internal WindowsLslStreamDiscoveryService(ILslStreamDiscoveryBridge? bridge = null)
    {
        _bridge = bridge ?? new LslNativeStreamDiscoveryBridge();
        RuntimeState = _bridge.GetRuntimeState();
    }

    public LslRuntimeState RuntimeState { get; }

    public IReadOnlyList<LslVisibleStreamInfo> Discover(LslStreamDiscoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!RuntimeState.Available)
        {
            return [];
        }

        return _bridge.Discover(request);
    }
}

public sealed class PreviewLslStreamDiscoveryService : ILslStreamDiscoveryService
{
    public LslRuntimeState RuntimeState { get; } = new(false, "Preview LSL discovery. No liblsl runtime loaded.");

    public IReadOnlyList<LslVisibleStreamInfo> Discover(LslStreamDiscoveryRequest request)
        => [];
}

public static class LslStreamDiscoveryServiceFactory
{
    public static ILslStreamDiscoveryService CreateDefault()
        => OperatingSystem.IsWindows()
            ? new WindowsLslStreamDiscoveryService()
            : new PreviewLslStreamDiscoveryService();
}

internal interface ILslStreamDiscoveryBridge
{
    LslRuntimeState GetRuntimeState();

    IReadOnlyList<LslVisibleStreamInfo> Discover(LslStreamDiscoveryRequest request);
}

internal sealed class LslNativeStreamDiscoveryBridge : ILslStreamDiscoveryBridge
{
    private const int DefaultResolveBufferSize = 16;
    private const double ResolveTimeoutSeconds = 1d;
    private readonly LslRuntimeState _runtimeState = NativeMethods.GetRuntimeState();

    public LslRuntimeState GetRuntimeState() => _runtimeState;

    public IReadOnlyList<LslVisibleStreamInfo> Discover(LslStreamDiscoveryRequest request)
    {
        if (!_runtimeState.Available)
        {
            throw new InvalidOperationException(_runtimeState.Detail);
        }

        var streamName = request.StreamName?.Trim();
        var streamType = request.StreamType?.Trim();
        if (string.IsNullOrWhiteSpace(streamName) && string.IsNullOrWhiteSpace(streamType))
        {
            throw new ArgumentException("LSL discovery requires a stream name or type.");
        }

        var limit = request.Limit <= 0 ? DefaultResolveBufferSize : Math.Min(request.Limit, 64);
        var resolvedInfos = new IntPtr[limit];
        var resolveCount = ResolveCandidates(streamName, streamType, resolvedInfos);
        if (resolveCount < 0)
        {
            DestroyResolvedInfos(resolvedInfos);
            throw new InvalidOperationException(BuildErrorMessage("Could not resolve visible LSL streams", resolveCount));
        }

        if (resolveCount == 0)
        {
            return [];
        }

        var matches = new List<LslVisibleStreamInfo>(resolveCount);
        for (var index = 0; index < resolveCount; index++)
        {
            var streamInfo = resolvedInfos[index];
            if (streamInfo == IntPtr.Zero ||
                !MatchesRequestedStream(
                    streamInfo,
                    streamName,
                    streamType,
                    request.ExactSourceId,
                    request.SourceIdPrefix))
            {
                continue;
            }

            matches.Add(new LslVisibleStreamInfo(
                NativeMethods.GetName(streamInfo),
                NativeMethods.GetTypeName(streamInfo),
                NativeMethods.GetSourceId(streamInfo),
                NativeMethods.GetChannelCount(streamInfo),
                (float)NativeMethods.GetNominalSampleRate(streamInfo),
                NativeMethods.GetCreatedAt(streamInfo)));
        }

        DestroyResolvedInfos(resolvedInfos);

        return request.PreferNewestFirst
            ? matches
                .OrderByDescending(static stream => stream.CreatedAtSeconds)
                .ToArray()
            : matches
                .OrderBy(static stream => stream.Name, StringComparer.Ordinal)
                .ThenBy(static stream => stream.Type, StringComparer.Ordinal)
                .ThenBy(static stream => stream.SourceId, StringComparer.Ordinal)
                .ToArray();
    }

    private static int ResolveCandidates(string? streamName, string? streamType, IntPtr[] resolvedInfos)
        => !string.IsNullOrWhiteSpace(streamName)
            ? NativeMethods.ResolveByProperty(resolvedInfos, (uint)resolvedInfos.Length, "name", streamName, 0, ResolveTimeoutSeconds)
            : NativeMethods.ResolveByProperty(resolvedInfos, (uint)resolvedInfos.Length, "type", streamType!, 0, ResolveTimeoutSeconds);

    private static bool MatchesRequestedStream(
        nint streamInfo,
        string? requestedName,
        string? requestedType,
        string? exactSourceId,
        string? sourceIdPrefix)
    {
        var candidateName = NativeMethods.GetName(streamInfo);
        var candidateType = NativeMethods.GetTypeName(streamInfo);
        var candidateSourceId = NativeMethods.GetSourceId(streamInfo);
        var nameMatches = string.IsNullOrWhiteSpace(requestedName) || string.Equals(candidateName, requestedName, StringComparison.Ordinal);
        var typeMatches = string.IsNullOrWhiteSpace(requestedType) || string.Equals(candidateType, requestedType, StringComparison.Ordinal);
        var exactSourceMatches = string.IsNullOrWhiteSpace(exactSourceId) ||
            string.Equals(candidateSourceId, exactSourceId, StringComparison.Ordinal);
        var prefixSourceMatches = string.IsNullOrWhiteSpace(sourceIdPrefix) ||
            (!string.IsNullOrWhiteSpace(candidateSourceId) &&
             candidateSourceId.StartsWith(sourceIdPrefix, StringComparison.Ordinal));
        return nameMatches && typeMatches && exactSourceMatches && prefixSourceMatches;
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

    private static class NativeMethods
    {
        private static readonly Lazy<LslRuntimeState> RuntimeState = new(LoadRuntimeState);

        static NativeMethods()
        {
            LslNativeLibraryResolver.EnsureInstalled(typeof(NativeMethods).Assembly);
        }

        internal static LslRuntimeState GetRuntimeState() => RuntimeState.Value;

        private static LslRuntimeState LoadRuntimeState()
        {
            if (!OperatingSystem.IsWindows())
            {
                return new LslRuntimeState(false, "Windows LSL discovery is only available on Windows.");
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
                return new LslRuntimeState(false, $"lsl.dll loaded from {detail}, but stream discovery initialization failed: {ex.Message}");
            }
        }

        [DllImport("lsl", EntryPoint = "lsl_last_error", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_last_error();

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

        [DllImport("lsl", EntryPoint = "lsl_get_source_id", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_get_source_id(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_get_created_at", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_get_created_at(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_get_channel_count", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lsl_get_channel_count(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_get_nominal_srate", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_get_nominal_srate(nint streamInfo);

        internal static string GetLastError() => PtrToString(lsl_last_error());

        internal static string GetLibraryInfo() => PtrToString(lsl_library_info());

        internal static int ResolveByProperty(IntPtr[] buffer, uint bufferElements, string property, string value, int minimum, double timeoutSeconds)
            => lsl_resolve_byprop(buffer, bufferElements, property, value, minimum, timeoutSeconds);

        internal static void DestroyStreamInfo(nint streamInfo) => lsl_destroy_streaminfo(streamInfo);

        internal static string GetName(nint streamInfo) => PtrToString(lsl_get_name(streamInfo));

        internal static string GetTypeName(nint streamInfo) => PtrToString(lsl_get_type(streamInfo));

        internal static string GetSourceId(nint streamInfo) => PtrToString(lsl_get_source_id(streamInfo));

        internal static double GetCreatedAt(nint streamInfo) => lsl_get_created_at(streamInfo);

        internal static int GetChannelCount(nint streamInfo) => lsl_get_channel_count(streamInfo);

        internal static double GetNominalSampleRate(nint streamInfo) => lsl_get_nominal_srate(streamInfo);

        private static string PtrToString(IntPtr pointer)
            => pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
    }
}
