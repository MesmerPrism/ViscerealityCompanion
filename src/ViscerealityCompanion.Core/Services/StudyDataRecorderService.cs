using System.Globalization;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

internal sealed record StudyRecorderSignalSpec(
    string Group,
    string Name,
    string Unit,
    IReadOnlyList<string> Keys);

public sealed class StudyDataRecorderService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly StudyRecorderSignalSpec[] RecorderSignalSpecs =
    [
        new("headset_pose", "headset.position.x", "meters", ["study.pose.headset.local_pos_x"]),
        new("headset_pose", "headset.position.y", "meters", ["study.pose.headset.local_pos_y"]),
        new("headset_pose", "headset.position.z", "meters", ["study.pose.headset.local_pos_z"]),
        new("headset_pose", "headset.rotation.qx", "quaternion", ["study.pose.headset.local_rot_x"]),
        new("headset_pose", "headset.rotation.qy", "quaternion", ["study.pose.headset.local_rot_y"]),
        new("headset_pose", "headset.rotation.qz", "quaternion", ["study.pose.headset.local_rot_z"]),
        new("headset_pose", "headset.rotation.qw", "quaternion", ["study.pose.headset.local_rot_w"]),
        new("controller_pose", "controller.position.x", "meters", ["study.pose.controller.local_pos_x"]),
        new("controller_pose", "controller.position.y", "meters", ["study.pose.controller.local_pos_y"]),
        new("controller_pose", "controller.position.z", "meters", ["study.pose.controller.local_pos_z"]),
        new("controller_pose", "controller.rotation.qx", "quaternion", ["study.pose.controller.local_rot_x"]),
        new("controller_pose", "controller.rotation.qy", "quaternion", ["study.pose.controller.local_rot_y"]),
        new("controller_pose", "controller.rotation.qz", "quaternion", ["study.pose.controller.local_rot_z"]),
        new("controller_pose", "controller.rotation.qw", "quaternion", ["study.pose.controller.local_rot_w"]),
        new("heartbeat", "heartbeat.value01", "unit01", ["study.heartbeat.value01"]),
        new("heartbeat", "heartbeat.real_beat_value01", "unit01", ["study.heartbeat.real_beat_value01"]),
        new("heartbeat", "heartbeat.packet_value01", "unit01", ["study.lsl.latest_default_value", "study.lsl.latest_ch0_value"]),
        new("coherence", "coherence.value01", "unit01", ["study.coherence.value01"]),
        new("breathing", "breathing.value01", "unit01", ["study.breathing.value01", "signal01.breathing_controller", "tracker.breathing.controller.volume01"]),
        new("breathing", "sphere_radius.progress01", "unit01", ["study.radius.sphere.progress01"]),
        new("breathing", "sphere_radius.raw", "units", ["study.radius.sphere.raw"]),
        new("lsl", "lsl.sample_count", "count", ["study.lsl.received_sample_count"]),
        new("lsl", "lsl.latest_timestamp_seconds", "seconds", ["study.lsl.latest_timestamp"]),
        new("session", "session.state", string.Empty, ["study.session.state_label"]),
        new("session", "session.run_index", "count", ["study.session.run_index"]),
        new("session", "session.dataset_id", string.Empty, ["study.session.dataset_id"]),
        new("session", "session.dataset_hash", string.Empty, ["study.session.dataset_hash"]),
        new("session", "session.settings_hash", string.Empty, ["study.session.settings_hash"]),
        new("session", "session.environment_hash", string.Empty, ["study.session.environment_hash"])
    ];

    internal static IReadOnlyList<StudyRecorderSignalSpec> SignalSpecs => RecorderSignalSpecs;

    public StudyDataRecorderService(string? rootPath = null)
    {
        RootPath = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ViscerealityCompanion",
                "study-data")
            : Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }

    public StudyParticipantStatus GetParticipantStatus(string studyId, string participantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studyId);

        var normalizedParticipantId = NormalizeParticipantId(participantId);
        var participantFolderName = BuildParticipantFolderName(normalizedParticipantId);
        var participantFolderPath = Path.Combine(
            RootPath,
            SanitizePathSegment(studyId),
            participantFolderName);

        if (!Directory.Exists(participantFolderPath))
        {
            return new StudyParticipantStatus(
                normalizedParticipantId,
                participantFolderName,
                participantFolderPath,
                false,
                []);
        }

        var existingSessions = Directory.EnumerateDirectories(participantFolderPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new StudyParticipantStatus(
            normalizedParticipantId,
            participantFolderName,
            participantFolderPath,
            existingSessions.Length > 0,
            existingSessions);
    }

    public StudyDataRecordingSession StartSession(StudyDataRecordingStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var participantStatus = GetParticipantStatus(request.StudyId, request.ParticipantId);
        Directory.CreateDirectory(participantStatus.ParticipantFolderPath);

        var sessionFolderPath = ResolveUniqueSessionFolderPath(
            participantStatus.ParticipantFolderPath,
            SanitizePathSegment(request.SessionId));
        Directory.CreateDirectory(sessionFolderPath);

        return new StudyDataRecordingSession(sessionFolderPath, request, JsonOptions, Utf8NoBom);
    }

    public static string NormalizeParticipantId(string participantId)
    {
        var trimmed = participantId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Participant id must not be blank.", nameof(participantId));
        }

        return trimmed;
    }

    public static string BuildParticipantFolderName(string participantId)
    {
        var safeId = SanitizePathSegment(NormalizeParticipantId(participantId));
        if (safeId.StartsWith("participant-", StringComparison.OrdinalIgnoreCase))
        {
            return safeId;
        }

        return $"participant-{safeId}";
    }

    public static string SanitizePathSegment(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "unknown";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(trimmed.Length);
        foreach (var character in trimmed)
        {
            if (invalidCharacters.Contains(character))
            {
                continue;
            }

            builder.Append(char.IsWhiteSpace(character) ? '-' : character);
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string ResolveUniqueSessionFolderPath(string participantFolderPath, string baseSessionId)
    {
        var candidate = Path.Combine(participantFolderPath, baseSessionId);
        if (!Directory.Exists(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            candidate = Path.Combine(participantFolderPath, $"{baseSessionId}_{suffix:00}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not allocate a unique session folder under {participantFolderPath}.");
    }

}

public sealed record StudyParticipantStatus(
    string ParticipantId,
    string ParticipantFolderName,
    string ParticipantFolderPath,
    bool HasExistingSessions,
    IReadOnlyList<string> ExistingSessionIds);

public sealed record StudyDataRecordingStartRequest(
    string StudyId,
    string StudyLabel,
    string ParticipantId,
    string SessionId,
    string DatasetId,
    string DatasetHash,
    string SettingsHash,
    string EnvironmentHash,
    DateTimeOffset SessionStartedAtUtc,
    string PackageId,
    string ApkSha256,
    string AppVersionName,
    string LaunchComponent,
    string HeadsetSoftwareVersion,
    string HeadsetBuildId,
    string HeadsetDisplayId,
    string DeviceProfileId,
    string DeviceProfileLabel,
    IReadOnlyDictionary<string, string> DeviceProfileProperties,
    string ExpectedLslStreamName,
    string ExpectedLslStreamType,
    double RecenterDistanceThresholdUnits,
    string RuntimeConfigJsonHash,
    string RuntimeHotloadProfileId,
    string RuntimeHotloadProfileVersion,
    string RuntimeHotloadProfileChannel,
    string WindowsMachineName,
    string QuestSelector,
    string SessionConditionsJson);

public sealed class StudyDataRecordingSession : IDisposable
{
    private static readonly string[] BreathingVolumeKeys =
    [
        "study.breathing.value01",
        "signal01.breathing_controller",
        "tracker.breathing.controller.volume01"
    ];

    private static readonly string[] SphereRadiusProgressKeys =
    [
        "study.radius.sphere.progress01"
    ];

    private static readonly string[] SphereRadiusRawKeys =
    [
        "study.radius.sphere.raw"
    ];

    private static readonly string[] ControllerCalibratedKeys =
    [
        "tracker.breathing.controller.calibrated",
        "study.session.calibration_completed"
    ];

    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Encoding _fileEncoding;
    private readonly AsyncFileQueue _ioQueue;
    private readonly StreamWriter _eventsWriter;
    private readonly StreamWriter _signalsWriter;
    private readonly StreamWriter _breathingWriter;
    private readonly StreamWriter _clockAlignmentWriter;
    private readonly StreamWriter _upstreamLslMonitorWriter;
    private readonly string _sessionConditionsJson;
    private readonly Dictionary<string, string> _lastSignalValues = new(StringComparer.OrdinalIgnoreCase);
    private const int UpstreamLslMonitorSettingsPersistInterval = 25;

    private bool _disposed;
    private string _lastBreathingSignature = string.Empty;
    private StudyDataRecordingSettingsDocument _settings;

    internal StudyDataRecordingSession(
        string sessionFolderPath,
        StudyDataRecordingStartRequest request,
        JsonSerializerOptions jsonOptions,
        Encoding encoding)
    {
        SessionFolderPath = sessionFolderPath;
        SessionId = request.SessionId;
        ParticipantId = request.ParticipantId;
        _jsonOptions = jsonOptions;

        EventsCsvPath = Path.Combine(SessionFolderPath, "session_events.csv");
        SignalsCsvPath = Path.Combine(SessionFolderPath, "signals_long.csv");
        BreathingCsvPath = Path.Combine(SessionFolderPath, "breathing_trace.csv");
        ClockAlignmentCsvPath = Path.Combine(SessionFolderPath, "clock_alignment_roundtrip.csv");
        UpstreamLslMonitorCsvPath = Path.Combine(SessionFolderPath, "upstream_lsl_monitor.csv");
        SettingsJsonPath = Path.Combine(SessionFolderPath, "session_settings.json");
        SessionSnapshotJsonPath = Path.Combine(SessionFolderPath, "session_snapshot.json");
        _sessionConditionsJson = request.SessionConditionsJson ?? string.Empty;
        _fileEncoding = encoding;
        _ioQueue = new AsyncFileQueue();

        _eventsWriter = CreateWriter(EventsCsvPath, encoding);
        _signalsWriter = CreateWriter(SignalsCsvPath, encoding);
        _breathingWriter = CreateWriter(BreathingCsvPath, encoding);
        _clockAlignmentWriter = CreateWriter(ClockAlignmentCsvPath, encoding);
        _upstreamLslMonitorWriter = CreateWriter(UpstreamLslMonitorCsvPath, encoding);

        QueueWriteLine(_eventsWriter, "participant_id,session_id,dataset_id,recorded_at_utc,event_name,event_detail,command_action_id,result");
        QueueWriteLine(_signalsWriter, "participant_id,session_id,dataset_id,recorded_at_utc,source_timestamp_utc,lsl_timestamp_seconds,source,signal_group,signal_name,value_numeric,value_text,unit,sequence,quest_selector");
        QueueWriteLine(_breathingWriter, "participant_id,session_id,dataset_id,recorded_at_utc,source_timestamp_utc,breath_volume01,sphere_radius_progress01,sphere_radius_raw,controller_calibrated");
        QueueWriteLine(_clockAlignmentWriter, "participant_id,session_id,dataset_id,window_kind,probe_sequence,probe_sent_at_utc,probe_sent_lsl_seconds,echo_received_at_utc,echo_received_lsl_seconds,echo_sample_lsl_seconds,quest_received_at_utc,quest_received_lsl_seconds,quest_echo_lsl_seconds,quest_minus_windows_clock_seconds,roundtrip_seconds");
        QueueWriteLine(_upstreamLslMonitorWriter, "participant_id,session_id,dataset_id,recorded_at_utc,observed_local_clock_seconds,stream_sample_timestamp_seconds,stream_name,stream_type,channel_index,channel_format,value_numeric,value_text,sequence,status,detail");

        _settings = new StudyDataRecordingSettingsDocument(
            request.StudyId,
            request.StudyLabel,
            request.ParticipantId,
            request.SessionId,
            request.DatasetId,
            request.DatasetHash,
            request.SettingsHash,
            request.EnvironmentHash,
            request.SessionStartedAtUtc,
            null,
            request.PackageId,
            request.ApkSha256,
            request.AppVersionName,
            request.LaunchComponent,
            request.HeadsetSoftwareVersion,
            request.HeadsetBuildId,
            request.HeadsetDisplayId,
            request.DeviceProfileId,
            request.DeviceProfileLabel,
            new Dictionary<string, string>(request.DeviceProfileProperties, StringComparer.OrdinalIgnoreCase),
            request.ExpectedLslStreamName,
            request.ExpectedLslStreamType,
            request.RecenterDistanceThresholdUnits,
            request.RuntimeConfigJsonHash,
            request.RuntimeHotloadProfileId,
            request.RuntimeHotloadProfileVersion,
            request.RuntimeHotloadProfileChannel,
            request.WindowsMachineName,
            request.QuestSelector,
            ClockAlignmentCsvPath,
            SussexClockAlignmentStreamContract.DefaultDurationSeconds,
            SussexClockAlignmentStreamContract.DefaultProbeIntervalMilliseconds,
            10,
            null,
            null,
            0,
            null,
            null,
            0,
            null,
            null,
            0,
            0,
            UpstreamLslMonitorCsvPath,
            0);

        WriteSettingsDocument();
    }

    public string ParticipantId { get; }

    public string SessionId { get; }

    public string DatasetId => _settings.DatasetId;

    public string DatasetHash => _settings.DatasetHash;

    public string SettingsHash => _settings.SettingsHash;

    public string EnvironmentHash => _settings.EnvironmentHash;

    public DateTimeOffset SessionStartedAtUtc => _settings.SessionStartedAtUtc;

    public string SessionFolderPath { get; }

    public string EventsCsvPath { get; }

    public string SignalsCsvPath { get; }

    public string BreathingCsvPath { get; }

    public string ClockAlignmentCsvPath { get; }

    public string UpstreamLslMonitorCsvPath { get; }

    public string SettingsJsonPath { get; }

    public string SessionSnapshotJsonPath { get; }

    public void RecordEvent(
        string eventName,
        string eventDetail,
        string? commandActionId,
        string result,
        DateTimeOffset? recordedAtUtc = null)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            var timestamp = recordedAtUtc ?? DateTimeOffset.UtcNow;
            QueueWriteLine(
                _eventsWriter,
                string.Join(
                    ",",
                    Csv(ParticipantId),
                    Csv(SessionId),
                    Csv(DatasetId),
                    Csv(timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(eventName),
                    Csv(eventDetail),
                    Csv(commandActionId ?? string.Empty),
                    Csv(result)));
        }
    }

    public void RecordTwinState(
        IReadOnlyDictionary<string, string> twinState,
        DateTimeOffset recordedAtUtc,
        string questSelector)
    {
        if (twinState.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            ThrowIfDisposed();

            var lslTimestamp = TryGetFirstValue(twinState, ["study.lsl.latest_timestamp"], out var lslTimestampValue)
                ? lslTimestampValue
                : string.Empty;
            var sequence = TryGetFirstValue(twinState, ["study.lsl.received_sample_count"], out var sequenceValue)
                ? sequenceValue
                : string.Empty;

            foreach (var spec in StudyDataRecorderService.SignalSpecs)
            {
                if (!TryGetFirstValue(twinState, spec.Keys, out var rawValue))
                {
                    continue;
                }

                if (_lastSignalValues.TryGetValue(spec.Name, out var previousValue) &&
                    string.Equals(previousValue, rawValue, StringComparison.Ordinal))
                {
                    continue;
                }

                _lastSignalValues[spec.Name] = rawValue;
                var (numericValue, textValue) = SplitValue(rawValue);
                QueueWriteLine(
                    _signalsWriter,
                    string.Join(
                        ",",
                        Csv(ParticipantId),
                        Csv(SessionId),
                        Csv(DatasetId),
                        Csv(recordedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
                        Csv(string.Empty),
                        Csv(lslTimestamp),
                        Csv("quest_twin_state"),
                        Csv(spec.Group),
                        Csv(spec.Name),
                        Csv(numericValue),
                        Csv(textValue),
                        Csv(spec.Unit),
                        Csv(sequence),
                        Csv(questSelector)));
            }

            var breathingVolume = TryGetFirstValue(twinState, BreathingVolumeKeys, out var breathingValueRaw)
                ? breathingValueRaw
                : string.Empty;
            var sphereProgress = TryGetFirstValue(twinState, SphereRadiusProgressKeys, out var sphereProgressRaw)
                ? sphereProgressRaw
                : string.Empty;
            var sphereRaw = TryGetFirstValue(twinState, SphereRadiusRawKeys, out var sphereRawValue)
                ? sphereRawValue
                : string.Empty;
            var controllerCalibrated = TryGetFirstValue(twinState, ControllerCalibratedKeys, out var controllerCalibratedRaw)
                ? controllerCalibratedRaw
                : string.Empty;

            var signature = string.Join("|", breathingVolume, sphereProgress, sphereRaw, controllerCalibrated);
            if (string.Equals(signature, _lastBreathingSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastBreathingSignature = signature;
            QueueWriteLine(
                _breathingWriter,
                string.Join(
                    ",",
                    Csv(ParticipantId),
                    Csv(SessionId),
                    Csv(DatasetId),
                    Csv(recordedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(string.Empty),
                    Csv(breathingVolume),
                    Csv(sphereProgress),
                    Csv(sphereRaw),
                    Csv(controllerCalibrated)));
        }
    }

    public void CopyArtifact(string sourcePath, string targetFileName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            string.IsNullOrWhiteSpace(targetFileName) ||
            !File.Exists(sourcePath))
        {
            return;
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            _ioQueue.Enqueue(() => File.Copy(sourcePath, Path.Combine(SessionFolderPath, targetFileName), overwrite: true));
        }
    }

    public void RecordClockAlignmentSample(StudyClockAlignmentSample sample)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            QueueWriteLine(
                _clockAlignmentWriter,
                string.Join(
                    ",",
                    Csv(ParticipantId),
                    Csv(SessionId),
                    Csv(DatasetId),
                    Csv(sample.WindowKind.ToString()),
                    Csv(sample.ProbeSequence.ToString(CultureInfo.InvariantCulture)),
                    Csv(sample.ProbeSentAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(sample.ProbeSentLocalClockSeconds.ToString("0.########", CultureInfo.InvariantCulture)),
                    Csv(sample.EchoReceivedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(sample.EchoReceivedLocalClockSeconds.ToString("0.########", CultureInfo.InvariantCulture)),
                    Csv(sample.EchoSampleTimestampSeconds?.ToString("0.########", CultureInfo.InvariantCulture) ?? string.Empty),
                    Csv(sample.QuestReceivedAtUtc),
                    Csv(sample.QuestReceivedLocalClockSeconds.ToString("0.########", CultureInfo.InvariantCulture)),
                    Csv(sample.QuestEchoLocalClockSeconds.ToString("0.########", CultureInfo.InvariantCulture)),
                    Csv(sample.QuestMinusWindowsClockSeconds.ToString("0.########", CultureInfo.InvariantCulture)),
                    Csv(sample.RoundTripSeconds.ToString("0.########", CultureInfo.InvariantCulture))));
        }
    }

    public void UpdateClockAlignmentSummary(StudyClockAlignmentWindowKind windowKind, StudyClockAlignmentSummary summary)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _settings = windowKind switch
            {
                StudyClockAlignmentWindowKind.StartBurst => _settings with
                {
                    ClockAlignmentRecommendedQuestMinusWindowsClockSeconds = summary.RecommendedQuestMinusWindowsClockSeconds,
                    ClockAlignmentMeanRoundTripSeconds = summary.MeanRoundTripSeconds,
                    ClockAlignmentSampleCount = summary.EchoesReceived,
                    ClockAlignmentStartRecommendedQuestMinusWindowsClockSeconds = summary.RecommendedQuestMinusWindowsClockSeconds,
                    ClockAlignmentStartMeanRoundTripSeconds = summary.MeanRoundTripSeconds,
                    ClockAlignmentStartSampleCount = summary.EchoesReceived
                },
                StudyClockAlignmentWindowKind.EndBurst => _settings with
                {
                    ClockAlignmentEndRecommendedQuestMinusWindowsClockSeconds = summary.RecommendedQuestMinusWindowsClockSeconds,
                    ClockAlignmentEndMeanRoundTripSeconds = summary.MeanRoundTripSeconds,
                    ClockAlignmentEndSampleCount = summary.EchoesReceived
                },
                StudyClockAlignmentWindowKind.BackgroundSparse => _settings with
                {
                    ClockAlignmentBackgroundSampleCount = _settings.ClockAlignmentBackgroundSampleCount + summary.EchoesReceived
                },
                _ => _settings
            };
            WriteSettingsDocument();
        }
    }

    public void RecordUpstreamLslObservation(StudyUpstreamLslObservation observation)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            QueueWriteLine(
                _upstreamLslMonitorWriter,
                string.Join(
                    ",",
                    Csv(ParticipantId),
                    Csv(SessionId),
                    Csv(DatasetId),
                    Csv(observation.RecordedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(observation.ObservedLocalClockSeconds?.ToString("0.########", CultureInfo.InvariantCulture) ?? string.Empty),
                    Csv(observation.SampleTimestampSeconds?.ToString("0.########", CultureInfo.InvariantCulture) ?? string.Empty),
                    Csv(observation.StreamName),
                    Csv(observation.StreamType),
                    Csv(observation.ChannelIndex.ToString(CultureInfo.InvariantCulture)),
                    Csv(observation.ChannelFormat.ToString()),
                    Csv(observation.NumericValue?.ToString("0.######", CultureInfo.InvariantCulture) ?? string.Empty),
                    Csv(observation.TextValue ?? string.Empty),
                    Csv(observation.Sequence.ToString(CultureInfo.InvariantCulture)),
                    Csv(observation.Status),
                    Csv(observation.Detail)));
            _settings = _settings with
            {
                UpstreamLslMonitorSampleCount = _settings.UpstreamLslMonitorSampleCount + 1
            };
            if (_settings.UpstreamLslMonitorSampleCount % UpstreamLslMonitorSettingsPersistInterval == 0)
            {
                WriteSettingsDocument();
            }
        }
    }

    public void Complete(DateTimeOffset endedAtUtc)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _settings = _settings with { SessionEndedAtUtc = endedAtUtc };
            WriteSettingsDocument();
            DisposeWriters();
            _disposed = true;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            DisposeWriters();
            _disposed = true;
        }
    }

    private void WriteSettingsDocument()
    {
        var settingsJson = JsonSerializer.Serialize(_settings, _jsonOptions);
        var snapshotJson = BuildSessionSnapshotDocumentJson();
        _ioQueue.Enqueue(() => File.WriteAllText(SettingsJsonPath, settingsJson, _fileEncoding));
        _ioQueue.Enqueue(() => File.WriteAllText(SessionSnapshotJsonPath, snapshotJson, _fileEncoding));
        _ioQueue.Drain();
    }

    private string BuildSessionSnapshotDocumentJson()
    {
        JsonNode? conditions = null;
        string? parseError = null;
        if (!string.IsNullOrWhiteSpace(_sessionConditionsJson))
        {
            try
            {
                conditions = JsonNode.Parse(_sessionConditionsJson);
            }
            catch (JsonException ex)
            {
                parseError = ex.Message;
            }
        }

        var snapshot = new JsonObject
        {
            ["SchemaVersion"] = "sussex-session-snapshot-v1",
            ["UpdatedAtUtc"] = DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["Settings"] = JsonSerializer.SerializeToNode(_settings, _jsonOptions),
            ["Conditions"] = conditions ?? new JsonObject()
        };

        if (!string.IsNullOrWhiteSpace(parseError))
        {
            snapshot["ConditionsParseError"] = parseError;
        }

        return snapshot.ToJsonString(_jsonOptions);
    }

    private void DisposeWriters()
    {
        try
        {
            _ioQueue.Dispose();
        }
        finally
        {
            _eventsWriter.Dispose();
            _signalsWriter.Dispose();
            _breathingWriter.Dispose();
            _clockAlignmentWriter.Dispose();
            _upstreamLslMonitorWriter.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static StreamWriter CreateWriter(string path, Encoding encoding)
    {
        var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite);
        var writer = new StreamWriter(stream, encoding)
        {
            AutoFlush = true
        };
        return writer;
    }

    private void QueueWriteLine(StreamWriter writer, string line)
        => _ioQueue.Enqueue(() => WriteLine(writer, line));

    private static void WriteLine(StreamWriter writer, string line)
        => writer.WriteLine(line);

    private static string Csv(string value)
    {
        var token = value ?? string.Empty;
        if (!token.Contains(',') && !token.Contains('"') && !token.Contains('\n') && !token.Contains('\r'))
        {
            return token;
        }

        return $"\"{token.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static (string NumericValue, string TextValue) SplitValue(string rawValue)
    {
        if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            return (numeric.ToString("0.######", CultureInfo.InvariantCulture), string.Empty);
        }

        if (bool.TryParse(rawValue, out var flag))
        {
            return (flag ? "1" : "0", rawValue);
        }

        return (string.Empty, rawValue);
    }

    private static bool TryGetFirstValue(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<string> keys,
        out string value)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
            {
                value = found;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private sealed record StudyDataRecordingSettingsDocument(
        string StudyId,
        string StudyLabel,
        string ParticipantId,
        string SessionId,
        string DatasetId,
        string DatasetHash,
        string SettingsHash,
        string EnvironmentHash,
        DateTimeOffset SessionStartedAtUtc,
        DateTimeOffset? SessionEndedAtUtc,
        string PackageId,
        string ApkSha256,
        string AppVersionName,
        string LaunchComponent,
        string HeadsetSoftwareVersion,
        string HeadsetBuildId,
        string HeadsetDisplayId,
        string DeviceProfileId,
        string DeviceProfileLabel,
        IReadOnlyDictionary<string, string> DeviceProfileProperties,
        string LslStreamName,
        string LslStreamType,
        double RecenterDistanceThresholdUnits,
        string RuntimeConfigJsonHash,
        string RuntimeHotloadProfileId,
        string RuntimeHotloadProfileVersion,
        string RuntimeHotloadProfileChannel,
        string WindowsMachineName,
        string QuestSelector,
        string ClockAlignmentFile,
        int ClockAlignmentDurationSeconds,
        int ClockAlignmentProbeIntervalMilliseconds,
        int ClockAlignmentBackgroundProbeIntervalSeconds,
        double? ClockAlignmentRecommendedQuestMinusWindowsClockSeconds,
        double? ClockAlignmentMeanRoundTripSeconds,
        int ClockAlignmentSampleCount,
        double? ClockAlignmentStartRecommendedQuestMinusWindowsClockSeconds,
        double? ClockAlignmentStartMeanRoundTripSeconds,
        int ClockAlignmentStartSampleCount,
        double? ClockAlignmentEndRecommendedQuestMinusWindowsClockSeconds,
        double? ClockAlignmentEndMeanRoundTripSeconds,
        int ClockAlignmentEndSampleCount,
        int ClockAlignmentBackgroundSampleCount,
        string UpstreamLslMonitorFile,
        int UpstreamLslMonitorSampleCount);

    private sealed class AsyncFileQueue : IDisposable
    {
        private readonly BlockingCollection<QueueOperation> _operations = new(new ConcurrentQueue<QueueOperation>(), 4096);
        private readonly Thread _workerThread;
        private ExceptionDispatchInfo? _fault;
        private bool _disposed;

        public AsyncFileQueue()
        {
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "StudyDataRecorderAsyncFileQueue"
            };
            _workerThread.Start();
        }

        public void Enqueue(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            ThrowIfFaulted();
            ThrowIfDisposed();

            if (!_operations.TryAdd(new QueueOperation(action, null), millisecondsTimeout: 250))
            {
                throw new IOException("The Windows study recorder queue is saturated. The recorder will be faulted instead of silently dropping data.");
            }
        }

        public void Drain()
        {
            ThrowIfFaulted();
            ThrowIfDisposed();

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_operations.TryAdd(new QueueOperation(null, completion), millisecondsTimeout: 250))
            {
                throw new IOException("The Windows study recorder queue could not drain because the IO queue is saturated.");
            }

            completion.Task.GetAwaiter().GetResult();
            ThrowIfFaulted();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                Drain();
            }
            catch
            {
            }

            _disposed = true;
            _operations.CompleteAdding();
            _workerThread.Join(millisecondsTimeout: 3000);
            _operations.Dispose();
        }

        private void WorkerLoop()
        {
            try
            {
                foreach (var operation in _operations.GetConsumingEnumerable())
                {
                    if (operation.Action is not null)
                    {
                        operation.Action();
                    }

                        operation.Completion?.SetResult(true);
                }
            }
            catch (Exception exception)
            {
                _fault = ExceptionDispatchInfo.Capture(exception);
                while (_operations.TryTake(out var operation))
                {
                    operation.Completion?.SetException(exception);
                }
            }
        }

        private void ThrowIfFaulted()
        {
            _fault?.Throw();
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private readonly record struct QueueOperation(Action? Action, TaskCompletionSource<bool>? Completion);
    }
}

public sealed record StudyUpstreamLslObservation(
    DateTimeOffset RecordedAtUtc,
    double? ObservedLocalClockSeconds,
    double? SampleTimestampSeconds,
    string StreamName,
    string StreamType,
    int ChannelIndex,
    LslChannelFormat ChannelFormat,
    float? NumericValue,
    string? TextValue,
    int Sequence,
    string Status,
    string Detail);
