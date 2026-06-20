using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public interface IPeripersonalCommandTransport
{
    Task<PeripersonalCommandReceiptEnvelope> SendAsync(
        PeripersonalCommandEnvelope command,
        CancellationToken cancellationToken = default);
}

public enum PeripersonalOperatorWorkflowState
{
    Idle,
    Prepared,
    Recording,
    Stopped
}

public sealed record PeripersonalSessionSetupRequest(
    string ParticipantRef,
    string SessionId,
    string Handedness,
    string StudyId = "",
    string LanguageCode = "",
    string InitialConditionId = "");

public sealed record PeripersonalPreparedSession(
    string StudyId,
    string ParticipantRef,
    string SessionId,
    string Handedness,
    string BreathTrackingControllerSide,
    string SessionFolderName,
    string WindowsSessionDirectory,
    string LedgerPath,
    DateTimeOffset PreparedAtUtc);

public sealed record PeripersonalWorkflowOperationResult(
    OperationOutcome Outcome,
    PeripersonalPreparedSession? Session,
    IReadOnlyList<PeripersonalCommandLedgerSnapshot> CommandSnapshots)
{
    public bool Succeeded => Outcome.Kind == OperationOutcomeKind.Success;
}

public sealed class PeripersonalOperatorWorkflowService
{
    public const string SessionFolderConvention = "participantRef_sessionId_yyyyMMdd-HHmmss";
    public const string RecordingLifecycle = "one_global_recording_after_block_1";
    public const string RuntimeStateStreamPrefix = "peripersonal_runtime_state";
    public const string ParticleTriggerStreamPrefix = "peripersonal_particle_triggers";

    private static readonly Regex SafeFolderTokenPattern = new("[^A-Za-z0-9._-]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly StudyShellDefinition _study;
    private readonly IPeripersonalCommandTransport _transport;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly string _windowsSessionRoot;
    private readonly List<PeripersonalCommandLedgerSnapshot> _snapshots = [];
    private PeripersonalPreparedSession? _session;
    private PeripersonalCommandLedgerService? _ledger;
    private bool _questionnaireBlockOneSubmitted;

    public PeripersonalOperatorWorkflowService(
        StudyShellDefinition study,
        IPeripersonalCommandTransport transport,
        string? windowsSessionRoot = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _study = study ?? throw new ArgumentNullException(nameof(study));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _windowsSessionRoot = string.IsNullOrWhiteSpace(windowsSessionRoot)
            ? Path.Combine(CompanionOperatorDataLayout.StudyDataRootPath, study.Id)
            : Path.GetFullPath(windowsSessionRoot);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public PeripersonalOperatorWorkflowState State { get; private set; } = PeripersonalOperatorWorkflowState.Idle;
    public PeripersonalPreparedSession? CurrentSession => _session;
    public IReadOnlyList<PeripersonalCommandLedgerSnapshot> CommandSnapshots => _snapshots;

    public async Task<PeripersonalWorkflowOperationResult> PrepareSessionAsync(
        PeripersonalSessionSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (State != PeripersonalOperatorWorkflowState.Idle)
        {
            return Result(OperationOutcomeKind.Warning, "Session is already prepared.", "Start a new workflow service instance for a new participant session.");
        }

        var participantRef = RequireValue(request.ParticipantRef, nameof(request.ParticipantRef));
        var sessionId = RequireValue(request.SessionId, nameof(request.SessionId));
        var handedness = RequireValue(request.Handedness, nameof(request.Handedness));
        var breathSide = BreathTrackingControllerFromHandedness(handedness);
        if (string.IsNullOrWhiteSpace(breathSide))
        {
            return Result(
                OperationOutcomeKind.Failure,
                "Handedness is unsupported.",
                "Peripersonal setup requires right-handed or left-handed so the opposite controller can be fixed for breath tracking.");
        }

        var preparedAtUtc = _utcNow();
        var sessionFolderName = BuildSessionFolderName(participantRef, sessionId, preparedAtUtc);
        var sessionDirectory = Path.Combine(_windowsSessionRoot, SafeFolderToken(_study.Id), sessionFolderName);
        Directory.CreateDirectory(sessionDirectory);

        _ledger = new PeripersonalCommandLedgerService(Path.Combine(sessionDirectory, "command_ledger.jsonl"));
        _session = new PeripersonalPreparedSession(
            string.IsNullOrWhiteSpace(request.StudyId) ? _study.Id : request.StudyId.Trim(),
            participantRef,
            sessionId,
            handedness.Trim(),
            breathSide,
            sessionFolderName,
            sessionDirectory,
            _ledger.LedgerPath,
            preparedAtUtc);
        WriteWindowsSessionMetadata(_session, request);

        var unityResult = await SendAndVerifyAsync(
            "unity",
            "unity_quest_apk",
            _study.App.PackageId,
            "prepare_session",
            requiresObservedState: true,
            BuildSessionPayload(_session, request),
            observedState => SessionReadyObserved(observedState, sessionFolderName),
            cancellationToken).ConfigureAwait(false);
        if (!unityResult.Succeeded)
        {
            return unityResult;
        }

        var panelResult = await SendAndVerifyAsync(
            "panel",
            ResolveDeviceProfileValue("viscereality.panel.targetRuntimeKind", "quest_questionnaire_panel_apk"),
            ResolveDeviceProfileValue("viscereality.panel.package", "io.github.mesmerprism.questquestionnaire.panel"),
            "prepare_session",
            requiresObservedState: true,
            BuildSessionPayload(_session, request),
            observedState => SessionReadyObserved(observedState, sessionFolderName),
            cancellationToken).ConfigureAwait(false);
        if (!panelResult.Succeeded)
        {
            return panelResult;
        }

        State = PeripersonalOperatorWorkflowState.Prepared;
        return Result(
            OperationOutcomeKind.Success,
            "Peripersonal session prepared.",
            $"Windows, Unity, and panel session folders are prepared as {sessionFolderName}.",
            _session);
    }

    public PeripersonalWorkflowOperationResult RecordQuestionnaireSubmitted(string blockId)
    {
        if (string.Equals(blockId?.Trim(), "block-1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(blockId?.Trim(), "questionnaire-block-1", StringComparison.OrdinalIgnoreCase))
        {
            _questionnaireBlockOneSubmitted = true;
            return Result(OperationOutcomeKind.Success, "Questionnaire Block 1 marked complete.", "Global recording can now start.");
        }

        return Result(OperationOutcomeKind.Success, "Questionnaire block marked complete.", blockId?.Trim() ?? string.Empty);
    }

    public async Task<PeripersonalWorkflowOperationResult> StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (State != PeripersonalOperatorWorkflowState.Prepared || _session is null)
        {
            return Result(OperationOutcomeKind.Warning, "Session is not ready to record.", "Run prepare_session before Start Recording.");
        }

        if (!_questionnaireBlockOneSubmitted)
        {
            return Result(
                OperationOutcomeKind.Warning,
                "Start Recording is blocked until Questionnaire Block 1 is submitted.",
                "George's workflow starts one global recording after Block 1, not at the beginning of setup.");
        }

        var result = await SendAndVerifyAsync(
            "unity",
            "unity_quest_apk",
            _study.App.PackageId,
            "start_recording",
            requiresObservedState: true,
            BuildSessionPayload(_session, null),
            observedState => BoolObserved(observedState, "recording_active", expected: true),
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            State = PeripersonalOperatorWorkflowState.Recording;
        }

        return result;
    }

    public Task<PeripersonalWorkflowOperationResult> MarkXrBlockEndAsync(
        string blockId,
        string conditionId,
        CancellationToken cancellationToken = default)
    {
        if (State != PeripersonalOperatorWorkflowState.Recording)
        {
            return Task.FromResult(Result(OperationOutcomeKind.Warning, "XR block marker is blocked.", "Block-end markers are only valid while the global recording is active."));
        }

        return SendAndVerifyAsync(
            "unity",
            "unity_quest_apk",
            _study.App.PackageId,
            "mark_block_end",
            requiresObservedState: false,
            BuildSessionPayload(_session!, null, payload =>
            {
                payload["block_id"] = blockId?.Trim() ?? string.Empty;
                payload["condition_id"] = conditionId?.Trim() ?? string.Empty;
                payload["marker_name"] = "XR-Block-End";
            }),
            _ => true,
            cancellationToken);
    }

    public Task<PeripersonalWorkflowOperationResult> SetParticlesVisibleAsync(
        bool visible,
        CancellationToken cancellationToken = default)
    {
        if (State != PeripersonalOperatorWorkflowState.Recording)
        {
            return Task.FromResult(Result(OperationOutcomeKind.Warning, "Particle visibility command is blocked.", "Particles Visible toggles are runtime controls during the active global recording."));
        }

        return SendAndVerifyAsync(
            "unity",
            "unity_quest_apk",
            _study.App.PackageId,
            visible ? "set_particles_visible" : "set_particles_hidden",
            requiresObservedState: true,
            BuildSessionPayload(_session!, null, payload =>
            {
                payload["particles_visible"] = visible;
                payload["particle_trigger_label"] = visible ? "Particles-ON" : "Particles-OFF";
            }),
            observedState => BoolObserved(observedState, "particles_visible", visible),
            cancellationToken);
    }

    public Task<PeripersonalWorkflowOperationResult> OpenQuestionnaireBlockAsync(
        string requestId,
        string openStage,
        IReadOnlyList<string> screenSequence,
        int? conditionNumber = null,
        CancellationToken cancellationToken = default)
    {
        if (_session is null || State == PeripersonalOperatorWorkflowState.Idle)
        {
            return Task.FromResult(Result(OperationOutcomeKind.Warning, "Questionnaire launch is blocked.", "Prepare the session before opening the Quest questionnaire panel."));
        }

        if (!_questionnaireBlockOneSubmitted &&
            !string.Equals(openStage, "block-1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(openStage, "questionnaire-block-1", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Result(OperationOutcomeKind.Warning, "Questionnaire launch is blocked.", "Only Questionnaire Block 1 can run before the global recording starts."));
        }

        return SendAndVerifyAsync(
            "unity",
            "unity_quest_apk",
            _study.App.PackageId,
            "open_questionnaire_block",
            requiresObservedState: false,
            BuildSessionPayload(_session, null, payload =>
            {
                payload["request_id"] = RequireValue(requestId, nameof(requestId));
                payload["open_stage"] = RequireValue(openStage, nameof(openStage));
                payload["schema_id"] = "quest-questionnaire-panel";
                payload["screen_sequence"] = new JsonArray(screenSequence.Select(item => JsonValue.Create(item)).ToArray());
                if (conditionNumber.HasValue)
                {
                    payload["condition_number"] = conditionNumber.Value;
                }
            }),
            _ => true,
            cancellationToken);
    }

    public async Task<PeripersonalWorkflowOperationResult> StopRecordingAndCloseAppsAsync(CancellationToken cancellationToken = default)
    {
        if (State != PeripersonalOperatorWorkflowState.Recording || _session is null)
        {
            return Result(OperationOutcomeKind.Warning, "Stop Recording is blocked.", "The final central Stop Recording command is only valid while recording is active.");
        }

        var result = await SendAndVerifyAsync(
            "unity",
            "unity_quest_apk",
            _study.App.PackageId,
            "stop_recording_and_close_apps",
            requiresObservedState: true,
            BuildSessionPayload(_session, null, payload => payload["close_apps"] = true),
            observedState =>
                BoolObserved(observedState, "recording_active", expected: false) &&
                BoolObserved(observedState, "quest_apps_close_requested", expected: true),
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            State = PeripersonalOperatorWorkflowState.Stopped;
        }

        return result;
    }

    public static string BuildSessionFolderName(string participantRef, string sessionId, DateTimeOffset timestampUtc)
        => string.Join(
            "_",
            SafeFolderToken(RequireValue(participantRef, nameof(participantRef))),
            SafeFolderToken(RequireValue(sessionId, nameof(sessionId))),
            timestampUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

    public static string BreathTrackingControllerFromHandedness(string handedness)
        => handedness.Trim().ToLowerInvariant() switch
        {
            "right" or "r" or "right-handed" or "right_handed" or "right handed" => "left",
            "left" or "l" or "left-handed" or "left_handed" or "left handed" => "right",
            _ => string.Empty
        };

    private async Task<PeripersonalWorkflowOperationResult> SendAndVerifyAsync(
        string targetApp,
        string targetRuntimeKind,
        string targetPackage,
        string action,
        bool requiresObservedState,
        JsonObject payload,
        Func<JsonObject?, bool> observedPredicate,
        CancellationToken cancellationToken)
    {
        if (_session is null || _ledger is null)
        {
            return Result(OperationOutcomeKind.Warning, "Peripersonal session is not prepared.", "Prepare the session before sending runtime commands.");
        }

        var command = PeripersonalCommandEnvelope.Create(
            _ledger.AllocateSequence(),
            _session.SessionId,
            _session.ParticipantRef,
            targetApp,
            targetRuntimeKind,
            targetPackage,
            action,
            targetApp == "panel" ? "adb_ordered_broadcast" : "lsl",
            requiresObservedState,
            payload);

        Track(_ledger.RecordSent(command));

        PeripersonalCommandReceiptEnvelope receipt;
        try
        {
            receipt = await _transport.SendAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Track(_ledger.MarkTimedOut(command.CommandId, "transport_exception", ex.Message));
            return Result(OperationOutcomeKind.Failure, $"{action} command failed.", ex.Message);
        }

        var receiptSnapshot = _ledger.RecordReceipt(receipt);
        Track(receiptSnapshot);
        if (!receipt.Accepted || !receipt.Executed || !receipt.Completed)
        {
            return Result(
                OperationOutcomeKind.Failure,
                $"{action} command was rejected.",
                string.IsNullOrWhiteSpace(receipt.Message) ? receipt.IssueCode : receipt.Message);
        }

        if (requiresObservedState)
        {
            var observed = observedPredicate(receipt.ObservedState);
            Track(_ledger.RecordObservedState(
                command.CommandId,
                receipt.ObservedState,
                observed,
                observed ? "Observed state satisfied." : "Observed state did not satisfy the workflow gate."));
            if (!observed)
            {
                return Result(
                    OperationOutcomeKind.Warning,
                    $"{action} command receipt did not satisfy observed-state gate.",
                    "Transport success is not enough; the observed state must match the requested workflow transition.");
            }
        }

        return Result(OperationOutcomeKind.Success, $"{action} command completed.", receipt.Message);
    }

    private JsonObject BuildSessionPayload(
        PeripersonalPreparedSession session,
        PeripersonalSessionSetupRequest? request,
        Action<JsonObject>? customize = null)
    {
        var participantScopedRuntimeStateStream = $"{RuntimeStateStreamPrefix}_{session.ParticipantRef}";
        var participantScopedParticleStream = $"{ParticleTriggerStreamPrefix}_{session.ParticipantRef}";
        var payload = new JsonObject
        {
            ["study_id"] = session.StudyId,
            ["session_id"] = session.SessionId,
            ["participant_ref"] = session.ParticipantRef,
            ["handedness"] = session.Handedness,
            ["breath_tracking_controller_side"] = session.BreathTrackingControllerSide,
            ["session_folder_name"] = session.SessionFolderName,
            ["session_folder_convention"] = SessionFolderConvention,
            ["recording_lifecycle"] = RecordingLifecycle,
            ["language_code"] = request?.LanguageCode ?? string.Empty,
            ["condition_id"] = request?.InitialConditionId ?? string.Empty,
            ["runtime_state_lsl_stream_name"] = participantScopedRuntimeStateStream,
            ["particle_trigger_lsl_stream_name"] = participantScopedParticleStream,
            ["unity_lsl_stream_name_must_include_participant_ref"] = true,
            ["questionnaires_run_during_global_recording"] = true
        };
        customize?.Invoke(payload);
        return payload;
    }

    private void WriteWindowsSessionMetadata(
        PeripersonalPreparedSession session,
        PeripersonalSessionSetupRequest request)
    {
        var payload = new
        {
            protocol_version = PeripersonalCommandProtocol.CommandProtocolVersion,
            target_app = "windows_operator",
            study_id = session.StudyId,
            participant_ref = session.ParticipantRef,
            session_id = session.SessionId,
            handedness = session.Handedness,
            breath_tracking_controller_side = session.BreathTrackingControllerSide,
            breath_tracking_controller_rule = "right-handed=>left-controller; left-handed=>right-controller",
            session_folder_name = session.SessionFolderName,
            session_folder_convention = SessionFolderConvention,
            recording_lifecycle = RecordingLifecycle,
            prepared_at_utc = session.PreparedAtUtc,
            runtime_state_lsl_stream_name = $"{RuntimeStateStreamPrefix}_{session.ParticipantRef}",
            particle_trigger_lsl_stream_name = $"{ParticleTriggerStreamPrefix}_{session.ParticipantRef}",
            unity_package = _study.App.PackageId,
            panel_package = ResolveDeviceProfileValue("viscereality.panel.package", "io.github.mesmerprism.questquestionnaire.panel"),
            language_code = request.LanguageCode,
            initial_condition_id = request.InitialConditionId
        };

        File.WriteAllText(
            Path.Combine(session.WindowsSessionDirectory, "windows_session_metadata.json"),
            JsonSerializer.Serialize(payload, MetadataJsonOptions),
            Utf8NoBom);
    }

    private string ResolveDeviceProfileValue(string key, string fallback)
        => _study.DeviceProfile.Properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;

    private static bool SessionReadyObserved(JsonObject? observedState, string sessionFolderName)
        => BoolObserved(observedState, "session_ready", expected: true) &&
           StringObserved(observedState, "session_folder_name", sessionFolderName, allowMissing: true);

    private static bool BoolObserved(JsonObject? observedState, string key, bool expected)
    {
        var node = observedState?[key];
        if (node is null)
        {
            return false;
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.True => expected,
            JsonValueKind.False => !expected,
            JsonValueKind.String => bool.TryParse(node.GetValue<string>(), out var parsed) && parsed == expected,
            _ => false
        };
    }

    private static bool StringObserved(JsonObject? observedState, string key, string expected, bool allowMissing = false)
    {
        var node = observedState?[key];
        if (node is null)
        {
            return allowMissing;
        }

        return string.Equals(node.GetValue<string>(), expected, StringComparison.Ordinal);
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Trim();
    }

    private static string SafeFolderToken(string value)
    {
        var sanitized = SafeFolderTokenPattern.Replace(value.Trim(), "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private void Track(PeripersonalCommandLedgerSnapshot snapshot)
    {
        _snapshots.RemoveAll(existing => string.Equals(existing.CommandId, snapshot.CommandId, StringComparison.OrdinalIgnoreCase));
        _snapshots.Add(snapshot);
    }

    private PeripersonalWorkflowOperationResult Result(
        OperationOutcomeKind kind,
        string summary,
        string detail,
        PeripersonalPreparedSession? session = null)
        => new(
            new OperationOutcome(kind, summary, detail),
            session ?? _session,
            _snapshots.ToArray());
}
