using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class LslTwinModeBridge : ITwinModeBridge, IDisposable
{
    private const int TwinMessageChannelCount = 4;
    private const string DefaultCommandStreamName = "quest_twin_commands";
    private const string DefaultCommandStreamType = "quest.twin.command";
    private const string DefaultStateStreamName = "quest_twin_state";
    private const string DefaultStateStreamType = "quest.twin.state";
    private const string DefaultConfigStreamName = "quest_hotload_config";
    private const string DefaultConfigStreamType = "quest.config";

    private readonly ILslOutletService _commandOutlet;
    private readonly ILslOutletService _configOutlet;
    private readonly ILslMonitorService _stateMonitor;
    private readonly Dictionary<string, string> _requestedSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _reportedSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingStructuredSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TwinStateEvent> _stateEvents = [];
    private readonly Lock _settingsSync = new();

    private CancellationTokenSource? _monitorCts;
    private bool _commandOutletOpened;
    private bool _configOutletOpened;
    private DateTimeOffset? _lastStateReceivedAt;
    private string? _pendingStructuredRevision;
    private string? _pendingStructuredHash;
    private string? _lastCommittedSnapshotHash;
    private string? _lastCommittedSnapshotRevision;
    private int _lastCommittedSnapshotEntryCount;
    private int _publishedCommandCount;
    private int _lastPublishedCommandSequence;

    public event EventHandler? StateChanged;

    public LslTwinModeBridge(
        ILslOutletService commandOutlet,
        ILslOutletService configOutlet,
        ILslMonitorService stateMonitor)
    {
        _commandOutlet = commandOutlet;
        _configOutlet = configOutlet;
        _stateMonitor = stateMonitor;
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

        lock (_settingsSync)
        {
            _publishedCommandCount = 0;
            _lastPublishedCommandSequence = 0;
        }

        StartStateMonitor();

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
        var result = _commandOutlet.PublishCommand(command);
        if (result.Kind != OperationOutcomeKind.Failure)
        {
            lock (_settingsSync)
            {
                _publishedCommandCount++;
                _lastPublishedCommandSequence++;
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

        var result = _configOutlet.PublishConfigSnapshot(profile.Entries);

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
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _commandOutlet.Dispose();
        _configOutlet.Dispose();
    }

    private void EnsureOpen()
    {
        if (!_commandOutletOpened)
        {
            Open();
        }
    }

    private void StartStateMonitor()
    {
        if (_monitorCts is not null)
        {
            return;
        }

        _monitorCts = new CancellationTokenSource();
        var subscription = new LslMonitorSubscription(
            DefaultStateStreamName,
            DefaultStateStreamType,
            0);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var reading in _stateMonitor.MonitorAsync(subscription, _monitorCts.Token))
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
        });
    }

    private void ParseStateReading(LslMonitorReading reading)
    {
        var structuredResult = TryParseStructuredFrame(reading);
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

            foreach (var payload in payloads)
            {
                ParsePayloadLocked(payload, reading.Timestamp);
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private StructuredFrameResult TryParseStructuredFrame(LslMonitorReading reading)
    {
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

            var revision = values[1]?.Trim() ?? string.Empty;
            var key = values[2]?.Trim() ?? string.Empty;
            var value = values[3] ?? string.Empty;

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

                        _reportedSettings[key] = value;
                        AddStateEventLocked(new TwinStateEvent(reading.Timestamp, "state", string.Join(" | ", values), $"rev={revision} {key} = {value}"));
                        return StructuredFrameResult.ParsedStateChanged;
                    }

                    return StructuredFrameResult.ParsedNoChange;

                case "end":
                    if (string.IsNullOrWhiteSpace(_pendingStructuredRevision) ||
                        !string.Equals(revision, _pendingStructuredRevision, StringComparison.Ordinal))
                    {
                        _pendingStructuredSnapshot.Clear();
                        _pendingStructuredRevision = null;
                        _pendingStructuredHash = null;
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
                    _lastCommittedSnapshotEntryCount = _pendingStructuredSnapshot.Count;
                    AddStateEventLocked(new TwinStateEvent(reading.Timestamp, "frame", string.Join(" | ", values), $"Snapshot end rev={revision} entries={key} hash={value}"));
                    _pendingStructuredSnapshot.Clear();
                    _pendingStructuredRevision = null;
                    _pendingStructuredHash = null;
                    return StructuredFrameResult.ParsedStateChanged;

                default:
                    return StructuredFrameResult.NotStructured;
            }
        }
    }

    private enum StructuredFrameResult
    {
        NotStructured,
        ParsedNoChange,
        ParsedStateChanged
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
    public static ITwinModeBridge CreateDefault()
    {
        var commandOutlet = LslOutletServiceFactory.CreateDefault();
        var configOutlet = LslOutletServiceFactory.CreateDefault();
        var stateMonitor = LslMonitorServiceFactory.CreateDefault();

        return new LslTwinModeBridge(commandOutlet, configOutlet, stateMonitor);
    }
}
