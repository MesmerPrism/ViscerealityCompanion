using System.Runtime.InteropServices;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public interface ILslOutletService : IDisposable
{
    LslRuntimeState RuntimeState { get; }
    bool IsOpen { get; }

    OperationOutcome Open(string streamName, string streamType, int channelCount);
    void Close();
    void PushSample(string[] values);
    OperationOutcome PublishConfigSnapshot(IReadOnlyList<RuntimeConfigEntry> entries);
    OperationOutcome PublishCommand(TwinModeCommand command, int sequence);
}

public sealed class WindowsLslOutletService : ILslOutletService
{
    private readonly ILslOutletBridge _bridge;
    private nint _outlet;
    private nint _streamInfo;
    private string _streamName = string.Empty;
    private string _streamType = string.Empty;
    private int _publishedRevision;
    private int _publishedCommandSequence;

    public WindowsLslOutletService()
        : this(null)
    {
    }

    internal WindowsLslOutletService(ILslOutletBridge? bridge = null)
    {
        _bridge = bridge ?? new LslNativeOutletBridge();
        RuntimeState = _bridge.GetRuntimeState();
    }

    public LslRuntimeState RuntimeState { get; }

    public bool IsOpen => _outlet != IntPtr.Zero;

    public OperationOutcome Open(string streamName, string streamType, int channelCount)
    {
        if (!RuntimeState.Available)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "LSL outlet unavailable.",
                RuntimeState.Detail);
        }

        Close();

        _streamName = streamName ?? string.Empty;
        _streamType = streamType ?? string.Empty;

        _streamInfo = _bridge.CreateStreamInfo(
            _streamName,
            _streamType,
            channelCount,
            TwinLslSourceId.BuildCompanionSourceId(_streamName, _streamType, Environment.MachineName));
        if (_streamInfo == IntPtr.Zero)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Could not create LSL stream info.",
                _bridge.GetLastError());
        }

        _outlet = _bridge.CreateOutlet(_streamInfo);
        if (_outlet == IntPtr.Zero)
        {
            _bridge.DestroyStreamInfo(_streamInfo);
            _streamInfo = IntPtr.Zero;
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Could not create LSL outlet.",
                _bridge.GetLastError());
        }

        _publishedRevision = 0;
        _publishedCommandSequence = 0;

        return new OperationOutcome(
            OperationOutcomeKind.Success,
            $"LSL outlet opened: {streamName} / {streamType}.",
            $"Publishing {channelCount} channel(s) as string samples.");
    }

    public void Close()
    {
        if (_outlet != IntPtr.Zero)
        {
            _bridge.DestroyOutlet(_outlet);
            _outlet = IntPtr.Zero;
        }

        if (_streamInfo != IntPtr.Zero)
        {
            _bridge.DestroyStreamInfo(_streamInfo);
            _streamInfo = IntPtr.Zero;
        }

        _streamName = string.Empty;
        _streamType = string.Empty;
    }

    public void PushSample(string[] values)
    {
        if (_outlet == IntPtr.Zero)
        {
            return;
        }

        _bridge.PushSample(_outlet, values, values.Length);
    }

    public OperationOutcome PublishConfigSnapshot(IReadOnlyList<RuntimeConfigEntry> entries)
    {
        if (_outlet == IntPtr.Zero)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "LSL outlet not open.",
                "Open the outlet before publishing config snapshots.");
        }

        _publishedRevision++;
        var revision = _publishedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var snapshotHash = ComputeSnapshotHash(entries);

        PushSample(["begin", revision, string.Empty, snapshotHash]);

        foreach (var entry in entries)
        {
            PushSample(["set", revision, entry.Key, entry.Value]);
        }

        PushSample(["end", revision, entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), snapshotHash]);

        return new OperationOutcome(
            OperationOutcomeKind.Success,
            $"Published config snapshot ({entries.Count} entries).",
            $"Snapshot frame: begin/set/end revision {revision}.");
    }

    public OperationOutcome PublishCommand(TwinModeCommand command, int sequence)
    {
        if (_outlet == IntPtr.Zero)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "LSL outlet not open.",
                "Open the outlet before sending twin commands.");
        }

        _publishedCommandSequence = Math.Max(_publishedCommandSequence, sequence);
        var renderedSequence = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PushSample(["cmd", renderedSequence, command.ActionId, command.DisplayName]);

        return new OperationOutcome(
            OperationOutcomeKind.Success,
            $"Sent twin command: {command.DisplayName}.",
            $"Published command frame seq={renderedSequence} action={command.ActionId} on the twin command stream.");
    }

    private static string ComputeSnapshotHash(IReadOnlyList<RuntimeConfigEntry> entries)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var builder = new System.Text.StringBuilder();
        foreach (var entry in entries.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(entry.Key);
            builder.Append('=');
            builder.Append(entry.Value);
            builder.Append('\n');
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(sha256.ComputeHash(bytes)).ToLowerInvariant();
    }

    public void Dispose()
    {
        Close();
    }
}

public sealed class PreviewLslOutletService : ILslOutletService
{
    public LslRuntimeState RuntimeState { get; } = new(false, "Preview LSL outlet. No liblsl runtime loaded.");
    public bool IsOpen { get; private set; }

    public OperationOutcome Open(string streamName, string streamType, int channelCount)
    {
        IsOpen = true;
        return new OperationOutcome(
            OperationOutcomeKind.Preview,
            $"Preview outlet opened: {streamName} / {streamType}.",
            $"No real LSL samples will be published. Replace with WindowsLslOutletService when liblsl is available.");
    }

    public void Close()
    {
        IsOpen = false;
    }

    public void PushSample(string[] values) { }

    public OperationOutcome PublishConfigSnapshot(IReadOnlyList<RuntimeConfigEntry> entries)
        => new(OperationOutcomeKind.Preview,
            $"Preview config snapshot ({entries.Count} entries).",
            "No real LSL samples published in preview mode.");

    public OperationOutcome PublishCommand(TwinModeCommand command, int sequence)
        => new(OperationOutcomeKind.Preview,
            $"Preview twin command: {command.DisplayName}.",
            $"Command `{command.ActionId}` would be published on the twin stream as seq={sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");

    public void Dispose()
    {
        IsOpen = false;
    }
}

internal interface ILslOutletBridge
{
    LslRuntimeState GetRuntimeState();
    string GetLastError();
    nint CreateStreamInfo(string name, string type, int channelCount, string sourceId);
    void DestroyStreamInfo(nint streamInfo);
    nint CreateOutlet(nint streamInfo);
    void DestroyOutlet(nint outlet);
    void PushSample(nint outlet, string[] values, int count);
}

internal sealed class LslNativeOutletBridge : ILslOutletBridge
{
    private const int LslStringChannelFormat = 3;

    public LslRuntimeState GetRuntimeState() => OutletNativeMethods.GetRuntimeState();
    public string GetLastError() => OutletNativeMethods.GetLastError();

    public nint CreateStreamInfo(string name, string type, int channelCount, string sourceId)
        => OutletNativeMethods.CreateStreamInfo(name, type, channelCount, 0d, LslStringChannelFormat, sourceId);

    public void DestroyStreamInfo(nint streamInfo) => OutletNativeMethods.DestroyStreamInfo(streamInfo);

    public nint CreateOutlet(nint streamInfo) => OutletNativeMethods.CreateOutlet(streamInfo, 0, 360);

    public void DestroyOutlet(nint outlet) => OutletNativeMethods.DestroyOutlet(outlet);

    public void PushSample(nint outlet, string[] values, int count)
        => OutletNativeMethods.PushSample(outlet, values, count);

    private static class OutletNativeMethods
    {
        private static readonly Lazy<LslRuntimeState> RuntimeState = new(LoadRuntimeState);

        static OutletNativeMethods()
        {
            LslNativeLibraryResolver.EnsureInstalled(typeof(OutletNativeMethods).Assembly);
        }

        internal static LslRuntimeState GetRuntimeState() => RuntimeState.Value;

        private static LslRuntimeState LoadRuntimeState()
        {
            if (!OperatingSystem.IsWindows())
            {
                return new LslRuntimeState(false, "Windows liblsl outlet is only available on Windows.");
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
                return new LslRuntimeState(false, $"lsl.dll loaded from {detail}, but outlet initialization failed: {ex.Message}");
            }
        }

        [DllImport("lsl", EntryPoint = "lsl_last_error", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_last_error();

        [DllImport("lsl", EntryPoint = "lsl_library_info", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_library_info();

        [DllImport("lsl", EntryPoint = "lsl_create_streaminfo", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern nint lsl_create_streaminfo(
            string name, string type, int channelCount, double nominalRate, int channelFormat, string sourceId);

        [DllImport("lsl", EntryPoint = "lsl_destroy_streaminfo", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_streaminfo(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_create_outlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint lsl_create_outlet(nint streamInfo, int chunkSize, int maxBuffered);

        [DllImport("lsl", EntryPoint = "lsl_destroy_outlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_outlet(nint outlet);

        [DllImport("lsl", EntryPoint = "lsl_push_sample_str", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void lsl_push_sample_str(nint outlet, [In] string[] data, int dataElements);

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

        internal static void PushSample(nint outlet, string[] values, int count)
            => lsl_push_sample_str(outlet, values, count);
    }
}

public static class LslOutletServiceFactory
{
    public static ILslOutletService CreateDefault()
        => OperatingSystem.IsWindows()
            ? new WindowsLslOutletService()
            : new PreviewLslOutletService();
}

internal interface ITwinCommandSequenceStore
{
    int Next();
}

internal sealed class PersistentTwinCommandSequenceStore : ITwinCommandSequenceStore
{
    private static readonly Lazy<PersistentTwinCommandSequenceStore> Shared = new(() => new PersistentTwinCommandSequenceStore());
    private readonly string _statePath;
    private readonly Mutex _mutex;

    internal PersistentTwinCommandSequenceStore()
        : this(
            Path.Combine(
                CompanionOperatorDataLayout.SessionRootPath,
                "twin-command-sequence.txt"),
            @"Local\ViscerealityCompanion.TwinCommandSequence")
    {
    }

    internal PersistentTwinCommandSequenceStore(string statePath, string mutexName)
    {
        _statePath = statePath;
        _mutex = new Mutex(false, mutexName);
    }

    public static PersistentTwinCommandSequenceStore Instance => Shared.Value;

    public int Next()
    {
        _mutex.WaitOne();
        try
        {
            var current = LoadCurrentSequence();
            var next = current >= int.MaxValue - 1 ? 1 : current + 1;
            SaveSequence(next);
            return next;
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    private int LoadCurrentSequence()
    {
        var floor = GetSequenceFloor();
        try
        {
            if (!File.Exists(_statePath))
                return floor;

            var raw = File.ReadAllText(_statePath).Trim();
            var persisted = int.TryParse(raw, out var value) && value > 0 ? value : 0;
            return Math.Max(persisted, floor);
        }
        catch
        {
            return floor;
        }
    }

    private void SaveSequence(int sequence)
    {
        try
        {
            var directory = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_statePath, sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
        }
    }

    private static int GetSequenceFloor()
    {
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (unixSeconds <= 0)
            return 0;

        return unixSeconds >= int.MaxValue
            ? int.MaxValue - 1
            : (int)unixSeconds;
    }
}
