using System.Globalization;
using System.Text.Json;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class LslTwinModeBridge : ITwinModeBridge, IDisposable
{
    private const int TwinMessageChannelCount = 4;
    private static readonly TimeSpan KeepaliveInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan MonitorShutdownTimeout = TimeSpan.FromSeconds(2);
    private const string DefaultCommandStreamName = "quest_twin_commands";
    private const string DefaultCommandStreamType = "quest.twin.command";
    private const string DefaultStateStreamName = "quest_twin_state";
    private const string DefaultStateStreamType = "quest.twin.state";
    private const string DefaultConfigStreamName = "quest_hotload_config";
    private const string DefaultConfigStreamType = "quest.config";

    private readonly ILslOutletService _commandOutlet;
    private readonly ILslOutletService _configOutlet;
    private readonly ILslMonitorService _stateMonitor;
    private readonly ITwinCommandSequenceStore _commandSequenceStore;
    private readonly Dictionary<string, string> _requestedSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _reportedSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingStructuredSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TwinStateEvent> _stateEvents = [];
    private readonly List<TwinTimingMarkerEvent> _timingMarkerEvents = [];
    private readonly Lock _settingsSync = new();
    private readonly Lock _commandOutletSync = new();
    private readonly Lock _configOutletSync = new();
    private readonly SemaphoreSlim _monitorRestartGate = new(1, 1);

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private CancellationTokenSource? _keepaliveCts;
    private Task? _keepaliveTask;
    private bool _commandOutletOpened;
    private bool _configOutletOpened;
    private DateTimeOffset? _lastStateReceivedAt;
    private DateTimeOffset? _lastCommittedSnapshotReceivedAt;
    private string? _pendingStructuredRevision;
    private string? _pendingStructuredHash;
    private string? _lastCommittedSnapshotHash;
    private string? _lastCommittedSnapshotRevision;
    private string? _lastResolvedStateSourceId;
    private string? _expectedStateSourceId;
    private string? _expectedStateSourceIdPrefix = TwinLslSourceId.QuestSourcePrefix;
    private bool _structuredSnapshotObserved;
    private int _lastCommittedSnapshotEntryCount;
    private int _publishedCommandCount;
    private int _lastPublishedCommandSequence;

    public event EventHandler? StateChanged;
    public event EventHandler<TwinTimingMarkerEvent>? TimingMarkerReceived;

    public LslTwinModeBridge(
        ILslOutletService commandOutlet,
        ILslOutletService configOutlet,
        ILslMonitorService stateMonitor)
        : this(commandOutlet, configOutlet, stateMonitor, null)
    {
    }

    internal LslTwinModeBridge(
        ILslOutletService commandOutlet,
        ILslOutletService configOutlet,
        ILslMonitorService stateMonitor,
        ITwinCommandSequenceStore? commandSequenceStore)
    {
        _commandOutlet = commandOutlet;
        _configOutlet = configOutlet;
        _stateMonitor = stateMonitor;
        _commandSequenceStore = commandSequenceStore ?? PersistentTwinCommandSequenceStore.Instance;
    }

    public TwinBridgeStatus Status
    {
        get
        {
            lock (_settingsSync)
            {
                var summary = _commandOutletOpened
                    ? "LSL twin bridge active."
                    : "LSL twin bridge available (not yet opened).";

                var detailParts = new List<string>(4);
                if (_commandOutletOpened)
                {
                    detailParts.Add(
                        $"Command outlet {DefaultCommandStreamName} / {DefaultCommandStreamType}: sent {_publishedCommandCount} command(s), last seq " +
                        $"{(_lastPublishedCommandSequence > 0 ? _lastPublishedCommandSequence.ToString(System.Globalization.CultureInfo.InvariantCulture) : "n/a")}.");
                }
                else
                {
                    detailParts.Add("Call Open() or send a command to activate the twin bridge.");
                }

                detailParts.Add($"Monitoring {DefaultStateStreamName} / {DefaultStateStreamType}.");

                if (!string.IsNullOrWhiteSpace(_expectedStateSourceId))
                {
                    detailParts.Add($"Expected quest source_id: {_expectedStateSourceId}.");
                }
                else if (!string.IsNullOrWhiteSpace(_expectedStateSourceIdPrefix))
                {
                    detailParts.Add($"Expected quest source_id prefix: {_expectedStateSourceIdPrefix}.");
                }

                if (!string.IsNullOrWhiteSpace(_lastResolvedStateSourceId))
                {
                    detailParts.Add($"Resolved quest source_id: {_lastResolvedStateSourceId}.");
                }

                if (!string.IsNullOrWhiteSpace(_lastCommittedSnapshotRevision))
                {
                    detailParts.Add(
                        $"Latest quest snapshot rev {_lastCommittedSnapshotRevision} " +
                        $"({_lastCommittedSnapshotEntryCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} entries).");
                }
                else if (_lastStateReceivedAt.HasValue)
                {
                    detailParts.Add(
                        $"Quest state received at {_lastStateReceivedAt.Value.ToLocalTime():HH:mm:ss}, but no completed structured snapshot has been committed yet.");
                }
                else
                {
                    detailParts.Add("No quest_twin_state frame received yet.");
                }

                if (TryBuildLastRemoteCommandDetailLocked(out var lastRemoteCommandDetail))
                {
                    detailParts.Add(lastRemoteCommandDetail);
                }

                return new TwinBridgeStatus(
                    IsAvailable: true,
                    UsesPrivateImplementation: false,
                    Summary: summary,
                    Detail: string.Join(" ", detailParts));
            }
        }
    }

    public IReadOnlyDictionary<string, string> RequestedSettings
    {
        get
        {
            lock (_settingsSync)
            {
                return new Dictionary<string, string>(_requestedSettings, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public IReadOnlyDictionary<string, string> ReportedSettings
    {
        get
        {
            lock (_settingsSync)
            {
                return new Dictionary<string, string>(_reportedSettings, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public IReadOnlyList<TwinStateEvent> StateEvents
    {
        get
        {
            lock (_settingsSync)
            {
                return _stateEvents.ToArray();
            }
        }
    }

    public DateTimeOffset? LastStateReceivedAt
    {
        get
        {
            lock (_settingsSync)
            {
                return _lastStateReceivedAt;
            }
        }
    }

    public bool IsCommandOutletOpen => _commandOutletOpened;

    public int PublishedCommandCount
    {
        get
        {
            lock (_settingsSync)
            {
                return _publishedCommandCount;
            }
        }
    }

    public int LastPublishedCommandSequence
    {
        get
        {
            lock (_settingsSync)
            {
                return _lastPublishedCommandSequence;
            }
        }
    }

    public string? LastCommittedSnapshotRevision
    {
        get
        {
            lock (_settingsSync)
            {
                return _lastCommittedSnapshotRevision;
            }
        }
    }

    public int LastCommittedSnapshotEntryCount
    {
        get
        {
            lock (_settingsSync)
            {
                return _lastCommittedSnapshotEntryCount;
            }
        }
    }

    public DateTimeOffset? LastCommittedSnapshotReceivedAt
    {
        get
        {
            lock (_settingsSync)
            {
                return _lastCommittedSnapshotReceivedAt;
            }
        }
    }

    public IReadOnlyList<TwinTimingMarkerEvent> TimingMarkerEvents
    {
        get
        {
            lock (_settingsSync)
            {
                return _timingMarkerEvents.ToArray();
            }
        }
    }

    public string? ExpectedStateSourceId
    {
        get
        {
            lock (_settingsSync)
            {
                return _expectedStateSourceId;
            }
        }
    }

    public string? LastResolvedStateSourceId
    {
        get
        {
            lock (_settingsSync)
            {
                return _lastResolvedStateSourceId;
            }
        }
    }

    public IReadOnlyList<TwinSettingsDelta> ComputeSettingsDelta()
    {
        lock (_settingsSync)
        {
            var allKeys = _requestedSettings.Keys
                .Union(_reportedSettings.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var deltas = new List<TwinSettingsDelta>(allKeys.Length);

            foreach (var key in allKeys)
            {
                _requestedSettings.TryGetValue(key, out var requested);
                _reportedSettings.TryGetValue(key, out var reported);

                var matches = requested is not null && reported is not null &&
                    string.Equals(requested, reported, StringComparison.Ordinal);

                deltas.Add(new TwinSettingsDelta(key, requested, reported, matches));
            }

            return deltas;
        }
    }

    public void ConfigureExpectedQuestStateSource(string? packageId)
    {
        ConfigureStateSourceFilter(
            string.IsNullOrWhiteSpace(packageId)
                ? null
                : TwinLslSourceId.BuildQuestStateSourceId(packageId, DefaultStateStreamName, DefaultStateStreamType),
            TwinLslSourceId.QuestSourcePrefix);
    }

    public void ConfigureStateSourceFilter(string? exactSourceId, string? sourceIdPrefix = null)
    {
        var normalizedExactSourceId = string.IsNullOrWhiteSpace(exactSourceId) ? null : exactSourceId.Trim();
        var normalizedSourceIdPrefix = string.IsNullOrWhiteSpace(sourceIdPrefix) ? null : sourceIdPrefix.Trim();
        var changed = false;
        lock (_settingsSync)
        {
            changed =
                !string.Equals(_expectedStateSourceId, normalizedExactSourceId, StringComparison.Ordinal) ||
                !string.Equals(_expectedStateSourceIdPrefix, normalizedSourceIdPrefix, StringComparison.Ordinal);
            _expectedStateSourceId = normalizedExactSourceId;
            _expectedStateSourceIdPrefix = normalizedSourceIdPrefix;
        }

        if (changed)
        {
            RestartStateMonitor();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public OperationOutcome Open()
    {
        if (!_commandOutletOpened)
        {
            var result = _commandOutlet.Open(DefaultCommandStreamName, DefaultCommandStreamType, TwinMessageChannelCount);
            if (result.Kind == OperationOutcomeKind.Failure)
            {
                return result;
            }

            _commandOutletOpened = true;
        }

        if (!_configOutletOpened)
        {
            var result = _configOutlet.Open(DefaultConfigStreamName, DefaultConfigStreamType, TwinMessageChannelCount);
            if (result.Kind == OperationOutcomeKind.Failure)
            {
                return result;
            }

            _configOutletOpened = true;
        }

        StartStateMonitor();
        StartKeepaliveLoop();

        return new OperationOutcome(
            OperationOutcomeKind.Success,
            "Twin bridge opened.",
            $"Command outlet: {DefaultCommandStreamName}. Config outlet: {DefaultConfigStreamName}. State monitor: {DefaultStateStreamName}.");
    }

    public Task<OperationOutcome> SendCommandAsync(
        TwinModeCommand command,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var sequence = _commandSequenceStore.Next();
        OperationOutcome result;
        lock (_commandOutletSync)
        {
            result = _commandOutlet.PublishCommand(command, sequence);
        }

        if (result.Kind != OperationOutcomeKind.Failure)
        {
            lock (_settingsSync)
            {
                _publishedCommandCount++;
                _lastPublishedCommandSequence = sequence;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return Task.FromResult(result);
    }

    public Task<OperationOutcome> ApplyConfigAsync(
        HotloadProfile profile,
        QuestAppTarget target,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OperationOutcome(
            OperationOutcomeKind.Success,
            $"Config tracked for twin mode: {profile.Label}.",
            $"The hotload profile is staged for {target.PackageId}. Use PublishRuntimeConfigAsync to push via LSL.",
            PackageId: target.PackageId));
    }

    public Task<OperationOutcome> PublishRuntimeConfigAsync(
        RuntimeConfigProfile profile,
        QuestAppTarget target,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        lock (_settingsSync)
        {
            _requestedSettings.Clear();
            foreach (var entry in profile.Entries)
            {
                _requestedSettings[entry.Key] = entry.Value;
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);

        OperationOutcome result;
        lock (_configOutletSync)
        {
            result = _configOutlet.PublishConfigSnapshot(profile.Entries);
        }

        return Task.FromResult(result.Kind == OperationOutcomeKind.Success
            ? new OperationOutcome(
                OperationOutcomeKind.Success,
                $"Published runtime config: {profile.Label} ({profile.Entries.Count} entries).",
                $"Snapshot pushed on {DefaultConfigStreamName} for {target.PackageId}.",
                PackageId: target.PackageId,
                Items: [$"profile={profile.Id}", $"entries={profile.Entries.Count}"])
            : result);
    }

    public void Dispose()
    {
        try
        {
            StopKeepaliveLoopAsync().GetAwaiter().GetResult();
            StopStateMonitorAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        lock (_commandOutletSync)
        {
            _commandOutlet.Dispose();
        }

        lock (_configOutletSync)
        {
            _configOutlet.Dispose();
        }

        _monitorRestartGate.Dispose();
    }

    private void EnsureOpen()
    {
        if (!_commandOutletOpened)
        {
            Open();
        }
    }

    private void RestartStateMonitor()
        => _ = RestartStateMonitorAsync();

    private void StartStateMonitor()
    {
        lock (_settingsSync)
        {
            if (_monitorTask is not null && !_monitorTask.IsCompleted)
            {
                return;
            }
        }

        _ = RestartStateMonitorAsync();
    }

    private async Task RestartStateMonitorAsync()
    {
        await _monitorRestartGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopStateMonitorAsyncCore().ConfigureAwait(false);
            if (!_commandOutletOpened && !_configOutletOpened)
            {
                return;
            }

            string? exactSourceId;
            string? sourceIdPrefix;
            lock (_settingsSync)
            {
                exactSourceId = _expectedStateSourceId;
                sourceIdPrefix = _expectedStateSourceIdPrefix;
            }

            var subscription = new LslMonitorSubscription(
                DefaultStateStreamName,
                DefaultStateStreamType,
                0,
                exactSourceId,
                sourceIdPrefix);
            var monitorCts = new CancellationTokenSource();
            var cancellationToken = monitorCts.Token;
            Task? monitorTask = null;
            monitorTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var reading in _stateMonitor.MonitorAsync(subscription, cancellationToken))
                    {
                        if (reading.Status.Contains("Streaming", StringComparison.OrdinalIgnoreCase))
                        {
                            ParseStateReading(reading);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                finally
                {
                    lock (_settingsSync)
                    {
                        if (ReferenceEquals(_monitorTask, monitorTask))
                        {
                            _monitorTask = null;
                        }

                        if (ReferenceEquals(_monitorCts, monitorCts))
                        {
                            _monitorCts = null;
                        }
                    }
                }
            }, CancellationToken.None);

            lock (_settingsSync)
            {
                _monitorCts = monitorCts;
                _monitorTask = monitorTask;
            }
        }
        finally
        {
            _monitorRestartGate.Release();
        }
    }

    private async Task<bool> StopStateMonitorAsync()
    {
        await _monitorRestartGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await StopStateMonitorAsyncCore().ConfigureAwait(false);
        }
        finally
        {
            _monitorRestartGate.Release();
        }
    }

    private async Task<bool> StopStateMonitorAsyncCore()
    {
        CancellationTokenSource? previousCts;
        Task? previousTask;
        lock (_settingsSync)
        {
            previousCts = _monitorCts;
            previousTask = _monitorTask;
            _monitorCts = null;
            _monitorTask = null;
        }

        if (previousCts is null && previousTask is null)
        {
            return true;
        }

        previousCts?.Cancel();
        try
        {
            if (previousTask is not null)
            {
                var completed = await Task.WhenAny(previousTask, Task.Delay(MonitorShutdownTimeout)).ConfigureAwait(false);
                if (!ReferenceEquals(completed, previousTask))
                {
                    return false;
                }

                await previousTask.ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            previousCts?.Dispose();
        }
    }

    private void StartKeepaliveLoop()
    {
        if (_keepaliveTask is not null)
        {
            return;
        }

        _keepaliveCts = new CancellationTokenSource();
        var cancellationToken = _keepaliveCts.Token;
        _keepaliveTask = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(KeepaliveInterval);
                PublishKeepalives();
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    PublishKeepalives();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);
    }

    private async Task<bool> StopKeepaliveLoopAsync()
    {
        var keepaliveCts = Interlocked.Exchange(ref _keepaliveCts, null);
        var keepaliveTask = Interlocked.Exchange(ref _keepaliveTask, null);
        if (keepaliveCts is null && keepaliveTask is null)
        {
            return true;
        }

        keepaliveCts?.Cancel();
        try
        {
            if (keepaliveTask is not null)
            {
                var completed = await Task.WhenAny(keepaliveTask, Task.Delay(MonitorShutdownTimeout)).ConfigureAwait(false);
                if (!ReferenceEquals(completed, keepaliveTask))
                {
                    return false;
                }

                await keepaliveTask.ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            keepaliveCts?.Dispose();
        }
    }

    private void PublishKeepalives()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        if (_commandOutletOpened)
        {
            try
            {
                lock (_commandOutletSync)
                {
                    _commandOutlet.PushSample(["keepalive", timestamp, string.Empty, string.Empty]);
                }
            }
            catch
            {
            }
        }

        if (_configOutletOpened)
        {
            try
            {
                lock (_configOutletSync)
                {
                    _configOutlet.PushSample(["keepalive", timestamp, string.Empty, string.Empty]);
                }
            }
            catch
            {
            }
        }
    }

    private void ParseStateReading(LslMonitorReading reading)
    {
        var structuredResult = TryParseStructuredFrame(reading, out var timingMarker);
        if (structuredResult == StructuredFrameResult.ParsedTimingMarker)
        {
            if (timingMarker is not null)
            {
                TimingMarkerReceived?.Invoke(this, timingMarker);
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (structuredResult == StructuredFrameResult.ParsedStateChanged)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (structuredResult == StructuredFrameResult.ParsedNoChange)
        {
            return;
        }

        var payloads = reading.SampleValues is { Count: > 0 }
            ? reading.SampleValues
            : string.IsNullOrWhiteSpace(reading.TextValue)
                ? string.IsNullOrWhiteSpace(reading.Detail)
                    ? []
                    : [reading.Detail]
                : [reading.TextValue];

        if (payloads.Count == 0)
        {
            return;
        }

        lock (_settingsSync)
        {
            _lastStateReceivedAt = reading.Timestamp;
            if (!string.IsNullOrWhiteSpace(reading.ResolvedSourceId))
            {
                _lastResolvedStateSourceId = reading.ResolvedSourceId.Trim();
            }

            foreach (var payload in payloads)
            {
                ParsePayloadLocked(payload, reading.Timestamp);
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private StructuredFrameResult TryParseStructuredFrame(
        LslMonitorReading reading,
        out TwinTimingMarkerEvent? timingMarker)
    {
        timingMarker = null;

        if (reading.SampleValues is not { Count: >= 4 } values)
        {
            return StructuredFrameResult.NotStructured;
        }

        var operation = values[0]?.Trim();
        if (string.IsNullOrWhiteSpace(operation))
        {
            return StructuredFrameResult.NotStructured;
        }

        lock (_settingsSync)
        {
            _lastStateReceivedAt = reading.Timestamp;
            if (!string.IsNullOrWhiteSpace(reading.ResolvedSourceId))
            {
                _lastResolvedStateSourceId = reading.ResolvedSourceId.Trim();
            }

            var revision = values[1]?.Trim() ?? string.Empty;
            var key = values[2]?.Trim() ?? string.Empty;
            var value = values[3] ?? string.Empty;
            _structuredSnapshotObserved = true;

            switch (operation.ToLowerInvariant())
            {
                case "begin":
                    _pendingStructuredSnapshot.Clear();
                    _pendingStructuredRevision = revision;
                    _pendingStructuredHash = value;
                    return StructuredFrameResult.ParsedNoChange;

                case "set":
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        if (!string.IsNullOrWhiteSpace(_pendingStructuredRevision) && string.Equals(revision, _pendingStructuredRevision, StringComparison.Ordinal))
                        {
                            _pendingStructuredSnapshot[key] = value;
                            return StructuredFrameResult.ParsedNoChange;
                        }

                        AddStateEventLocked(new TwinStateEvent(
                            reading.Timestamp,
                            "ignored",
                            string.Join(" | ", values),
                            $"Ignored out-of-revision structured state update rev={revision} key={key}."));
                        return StructuredFrameResult.ParsedNoChange;
                    }

                    return StructuredFrameResult.ParsedNoChange;

                case "end":
                    if (string.IsNullOrWhiteSpace(_pendingStructuredRevision) ||
                        !string.Equals(revision, _pendingStructuredRevision, StringComparison.Ordinal))
                    {
                        _pendingStructuredSnapshot.Clear();
                        _pendingStructuredRevision = null;
                        _pendingStructuredHash = null;
                        AddStateEventLocked(new TwinStateEvent(
                            reading.Timestamp,
                            "ignored",
                            string.Join(" | ", values),
                            $"Ignored structured end for unexpected rev={revision}."));
                        return StructuredFrameResult.ParsedNoChange;
                    }

                    var isDuplicateSnapshot = !string.IsNullOrWhiteSpace(_pendingStructuredHash) &&
                        string.Equals(_pendingStructuredHash, _lastCommittedSnapshotHash, StringComparison.OrdinalIgnoreCase);

                    if (isDuplicateSnapshot)
                    {
                        _pendingStructuredSnapshot.Clear();
                        _pendingStructuredRevision = null;
                        _pendingStructuredHash = null;
                        return StructuredFrameResult.ParsedNoChange;
                    }

                    _reportedSettings.Clear();
                    foreach (var entry in _pendingStructuredSnapshot)
                    {
                        _reportedSettings[entry.Key] = entry.Value;
                    }

                    _lastCommittedSnapshotHash = _pendingStructuredHash;
                    _lastCommittedSnapshotRevision = revision;
                    _lastCommittedSnapshotReceivedAt = reading.Timestamp;
                    _lastCommittedSnapshotEntryCount = _pendingStructuredSnapshot.Count;
                    AddStateEventLocked(new TwinStateEvent(reading.Timestamp, "frame", string.Join(" | ", values), $"Snapshot end rev={revision} entries={key} hash={value}"));
                    _pendingStructuredSnapshot.Clear();
                    _pendingStructuredRevision = null;
                    _pendingStructuredHash = null;
                    return StructuredFrameResult.ParsedStateChanged;

                case "event":
                    if (IsTimingMarkerEventFrame(revision, key) &&
                        TryParseTimingMarkerEvent(value, reading.Timestamp, out var parsedTimingMarker))
                    {
                        timingMarker = parsedTimingMarker;
                        AddTimingMarkerEventLocked(parsedTimingMarker);
                        AddStateEventLocked(new TwinStateEvent(
                            reading.Timestamp,
                            "timing_marker",
                            value,
                            $"{parsedTimingMarker.MarkerName} seq={FormatNullableInt(parsedTimingMarker.SampleSequence)} source_lsl={FormatNullableDouble(parsedTimingMarker.SourceLslTimestampSeconds)}"));
                        return StructuredFrameResult.ParsedTimingMarker;
                    }

                    AddStateEventLocked(new TwinStateEvent(
                        reading.Timestamp,
                        "event",
                        string.Join(" | ", values),
                        string.IsNullOrWhiteSpace(key) ? revision : key));
                    return StructuredFrameResult.ParsedNoChange;

                default:
                    return StructuredFrameResult.NotStructured;
            }
        }
    }

    private enum StructuredFrameResult
    {
        NotStructured,
        ParsedNoChange,
        ParsedStateChanged,
        ParsedTimingMarker
    }

    private void ParsePayloadLocked(string payload, DateTimeOffset timestamp)
    {
        var normalized = payload.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (string.Equals(normalized, "begin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "end", StringComparison.OrdinalIgnoreCase))
        {
            AddStateEventLocked(new TwinStateEvent(timestamp, "frame", normalized, $"State frame marker: {normalized}."));
            return;
        }

        var category = "state";
        var candidate = normalized;
        foreach (var prefix in new[] { "set ", "state ", "event ", "telemetry ", "status ", "app ", "cmd " })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                category = prefix.Trim();
                candidate = normalized[prefix.Length..].Trim();
                break;
            }
        }

        if (TryParseKeyValue(candidate, out var key, out var value))
        {
            if (_structuredSnapshotObserved || !string.IsNullOrWhiteSpace(_pendingStructuredRevision))
            {
                AddStateEventLocked(new TwinStateEvent(timestamp, "ignored", normalized, $"Ignored loose state payload after structured snapshot activation: {key} = {value}"));
                return;
            }

            _reportedSettings[key] = value;
            AddStateEventLocked(new TwinStateEvent(timestamp, category, normalized, $"{key} = {value}"));
            return;
        }

        AddStateEventLocked(new TwinStateEvent(timestamp, category, normalized, candidate));
    }

    private void AddStateEventLocked(TwinStateEvent stateEvent)
    {
        _stateEvents.Add(stateEvent);
        while (_stateEvents.Count > 120)
        {
            _stateEvents.RemoveAt(0);
        }
    }

    private void AddTimingMarkerEventLocked(TwinTimingMarkerEvent timingMarker)
    {
        _timingMarkerEvents.Add(timingMarker);
        while (_timingMarkerEvents.Count > 500)
        {
            _timingMarkerEvents.RemoveAt(0);
        }
    }

    private static bool IsTimingMarkerEventFrame(string revision, string key)
        => string.Equals(revision, "timing_marker", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(key, "timing_marker", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseTimingMarkerEvent(
        string payload,
        DateTimeOffset receivedAtUtc,
        out TwinTimingMarkerEvent timingMarker)
    {
        timingMarker = new TwinTimingMarkerEvent(
            receivedAtUtc,
            receivedAtUtc,
            string.Empty,
            string.Empty,
            null,
            null,
            null,
            null,
            null);

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var markerName = GetJsonString(root, "marker_name", "markerName", "name");
            if (string.IsNullOrWhiteSpace(markerName))
            {
                return false;
            }

            var recordedAtUtc = GetJsonDateTimeOffset(root, "recorded_at_utc", "recordedAtUtc") ?? receivedAtUtc;
            timingMarker = new TwinTimingMarkerEvent(
                receivedAtUtc,
                recordedAtUtc,
                markerName.Trim(),
                GetJsonString(root, "marker_detail", "markerDetail", "detail"),
                GetJsonInt(root, "sample_sequence", "sampleSequence"),
                GetJsonDouble(root, "source_lsl_timestamp_seconds", "sourceLslTimestampSeconds"),
                GetJsonDouble(root, "quest_local_clock_seconds", "questLocalClockSeconds"),
                GetJsonDouble(root, "value01"),
                GetJsonDouble(root, "aux_value", "auxValue"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string GetJsonString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString() ?? string.Empty,
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => string.Empty
                };
            }
        }

        return string.Empty;
    }

    private static DateTimeOffset? GetJsonDateTimeOffset(JsonElement root, params string[] propertyNames)
    {
        var value = GetJsonString(root, propertyNames);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private static int? GetJsonInt(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsedInt))
            {
                return parsedInt;
            }

            if (value.ValueKind == JsonValueKind.String &&
                int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
            {
                return parsedInt;
            }
        }

        return null;
    }

    private static double? GetJsonDouble(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsedDouble))
            {
                return double.IsFinite(parsedDouble) ? parsedDouble : null;
            }

            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsedDouble))
            {
                return double.IsFinite(parsedDouble) ? parsedDouble : null;
            }
        }

        return null;
    }

    private static string FormatNullableInt(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    private static string FormatNullableDouble(double? value)
        => value.HasValue
            ? value.Value.ToString("0.########", CultureInfo.InvariantCulture)
            : "n/a";

    private static bool TryParseKeyValue(string payload, out string key, out string value)
    {
        var separatorIndex = payload.IndexOf('=');
        if (separatorIndex <= 0 || separatorIndex >= payload.Length - 1)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = payload[..separatorIndex].Trim();
        value = payload[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(key);
    }

    private bool TryBuildLastRemoteCommandDetailLocked(out string detail)
    {
        detail = string.Empty;

        _reportedSettings.TryGetValue("study.command.last_action_sequence", out var sequenceRaw);
        _reportedSettings.TryGetValue("study.command.last_action_label", out var label);
        _reportedSettings.TryGetValue("study.command.last_action_source", out var source);
        _reportedSettings.TryGetValue("study.command.last_action_at_utc", out var timestampRaw);

        var hasAnySignal =
            !string.IsNullOrWhiteSpace(sequenceRaw) ||
            !string.IsNullOrWhiteSpace(label) ||
            !string.IsNullOrWhiteSpace(source) ||
            !string.IsNullOrWhiteSpace(timestampRaw);
        if (!hasAnySignal)
        {
            return false;
        }

        var renderedLabel = string.IsNullOrWhiteSpace(label) ? "command" : label.Trim();
        var renderedSequence = string.IsNullOrWhiteSpace(sequenceRaw) ? "n/a" : sequenceRaw.Trim();
        var renderedSource = string.IsNullOrWhiteSpace(source) ? "unknown source" : source.Trim();
        var renderedTime = timestampRaw;
        if (DateTimeOffset.TryParse(
                timestampRaw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsedTimestamp))
        {
            renderedTime = parsedTimestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }

        detail =
            $"Headset last executed {renderedLabel} seq {renderedSequence} via {renderedSource}" +
            $"{(string.IsNullOrWhiteSpace(renderedTime) ? "." : $" at {renderedTime}.")}";
        return true;
    }
}

public static class TwinModeBridgeFactory
{
    private static readonly Lazy<ITwinModeBridge> SharedBridge = new(CreateBridge);

    public static ITwinModeBridge CreateDefault()
        => CreateBridge();

    public static ITwinModeBridge CreateShared()
        => SharedBridge.Value;

    private static ITwinModeBridge CreateBridge()
    {
        var commandOutlet = LslOutletServiceFactory.CreateDefault();
        var configOutlet = LslOutletServiceFactory.CreateDefault();
        var stateMonitor = LslMonitorServiceFactory.CreateDefault();

        return new LslTwinModeBridge(commandOutlet, configOutlet, stateMonitor);
    }
}
