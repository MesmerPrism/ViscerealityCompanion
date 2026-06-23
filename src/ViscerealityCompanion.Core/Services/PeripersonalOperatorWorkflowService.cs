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
    DateTimeOffset PreparedAtUtc,
    string LanguageCode = "",
    string InitialConditionId = "");

public sealed record PeripersonalWorkflowOperationResult(
    OperationOutcome Outcome,
    PeripersonalPreparedSession? Session,
    IReadOnlyList<PeripersonalCommandLedgerSnapshot> CommandSnapshots)
{
    public bool Succeeded => Outcome.Kind == OperationOutcomeKind.Success;
}

public sealed record PeripersonalClockProbeOperationResult(
    OperationOutcome Outcome,
    PeripersonalPreparedSession? Session,
    StudyClockAlignmentRunResult ClockAlignment,
    string WindowsRoundTripCsvPath)
{
    public bool Succeeded => Outcome.Kind == OperationOutcomeKind.Success;
}

public sealed class PeripersonalOperatorWorkflowService
{
    public const string SessionFolderConvention = "participantRef_sessionId_yyyyMMdd-HHmmss";
    public const string RecordingLifecycle = "one_global_recording_after_block_1";
    public const string RuntimeStateStreamPrefix = "peripersonal_runtime_state";
    public const string ParticleTriggerStreamPrefix = "peripersonal_particle_triggers";
    public const string PanelQuestionnaireId = "maia2-spatial-frame-questionnaire-v1";
    public const string PanelQuestionnaireBlock1LanguageStage = "maia_spatial:language_selection";
    public const string PanelQuestionnaireBlock1DemographicsStage = "maia_spatial:demographics";
    public const string PanelQuestionnaireBlock1Maia2Stage = "maia_spatial:maia2";
    public const string PanelQuestionnaireBlock2SpatialFrameStage = "maia_spatial:spatial_frame_reference_1";
    public const string PanelQuestionnaireBlock3SpatialFrameStage = "maia_spatial:spatial_frame_reference_2";

    private static readonly Regex SafeFolderTokenPattern = new("[^A-Za-z0-9._-]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly string[] PanelQuestionnaireBlock1Sequence =
    [
        PanelQuestionnaireBlock1LanguageStage,
        PanelQuestionnaireBlock1DemographicsStage,
        PanelQuestionnaireBlock1Maia2Stage
    ];
    private static readonly string[] PanelQuestionnaireBlock2Sequence = [PanelQuestionnaireBlock2SpatialFrameStage];
    private static readonly string[] PanelQuestionnaireBlock3Sequence = [PanelQuestionnaireBlock3SpatialFrameStage];
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly StudyShellDefinition _study;
    private readonly IPeripersonalCommandTransport _transport;
    private readonly IPeripersonalQuestAppCloser _questAppCloser;
    private readonly IPeripersonalQuestBackupPuller _questBackupPuller;
    private readonly IPeripersonalQuestHttpForwarder _questHttpForwarder;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly string _windowsSessionRoot;
    private readonly List<PeripersonalCommandLedgerSnapshot> _snapshots = [];
    private PeripersonalPreparedSession? _session;
    private PeripersonalCommandLedgerService? _ledger;
    private bool _questionnaireBlockOneSubmitted;
    private OperationOutcome? _unityHttpBridgeForwardOutcome;

    public PeripersonalOperatorWorkflowService(
        StudyShellDefinition study,
        IPeripersonalCommandTransport transport,
        string? windowsSessionRoot = null,
        Func<DateTimeOffset>? utcNow = null,
        IPeripersonalQuestAppCloser? questAppCloser = null,
        IPeripersonalQuestBackupPuller? questBackupPuller = null,
        IPeripersonalQuestHttpForwarder? questHttpForwarder = null)
    {
        _study = study ?? throw new ArgumentNullException(nameof(study));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _questAppCloser = questAppCloser ?? NoOpPeripersonalQuestAppCloser.Instance;
        _questBackupPuller = questBackupPuller ?? NoOpPeripersonalQuestBackupPuller.Instance;
        _questHttpForwarder = questHttpForwarder ?? NoOpPeripersonalQuestHttpForwarder.Instance;
        _windowsSessionRoot = string.IsNullOrWhiteSpace(windowsSessionRoot)
            ? Path.Combine(CompanionOperatorDataLayout.StudyDataRootPath, study.Id)
            : Path.GetFullPath(windowsSessionRoot);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public PeripersonalOperatorWorkflowState State { get; private set; } = PeripersonalOperatorWorkflowState.Idle;
    public PeripersonalPreparedSession? CurrentSession => _session;
    public IReadOnlyList<PeripersonalCommandLedgerSnapshot> CommandSnapshots => _snapshots;

    public PeripersonalOperatorWorkflowService RestorePreparedSession(
        PeripersonalPreparedSession session,
        PeripersonalOperatorWorkflowState state,
        bool questionnaireBlockOneSubmitted)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (state == PeripersonalOperatorWorkflowState.Idle)
        {
            throw new ArgumentException("A restored Peripersonal workflow cannot be idle when a prepared session is supplied.", nameof(state));
        }

        _session = session;
        _ledger = new PeripersonalCommandLedgerService(session.LedgerPath);
        State = state;
        _questionnaireBlockOneSubmitted = questionnaireBlockOneSubmitted;
        _snapshots.Clear();
        return this;
    }

    public async Task<PeripersonalWorkflowOperationResult> PrepareSessionAsync(
        PeripersonalSessionSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (State != PeripersonalOperatorWorkflowState.Idle)
        {
            return Result(OperationOutcomeKind.Warning, "Session is already prepared.", "Start a new workflow service instance for a new participant session.");
        }

        var httpForward = await EnsureUnityHttpBridgeForwardAsync(cancellationToken).ConfigureAwait(false);
        if (httpForward.Kind == OperationOutcomeKind.Failure)
        {
            return Result(httpForward.Kind, httpForward.Summary, httpForward.Detail, items: httpForward.SafeItems);
        }

        var participantRef = RequireValue(request.ParticipantRef, nameof(request.ParticipantRef));
        var sessionId = RequireValue(request.SessionId, nameof(request.SessionId));
        var handedness = RequireValue(request.Handedness, nameof(request.Handedness));
        var languageCode = NormalizeLanguageCode(request.LanguageCode);
        var initialConditionId = request.InitialConditionId?.Trim() ?? string.Empty;
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
            preparedAtUtc,
            languageCode,
            initialConditionId);
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
            JoinDetails(
                $"Windows, Unity, and panel session folders are prepared as {sessionFolderName}.",
                httpForward.Detail),
            _session);
    }

    public PeripersonalWorkflowOperationResult RecordQuestionnaireSubmitted(string blockId)
    {
        if (IsOperatorQuestionnaireBlockOne(blockId))
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

    public async Task<PeripersonalClockProbeOperationResult> RunClockProbeAsync(
        IStudyClockAlignmentService clockAlignmentService,
        IProgress<StudyClockAlignmentProgress>? progress = null,
        TimeSpan? duration = null,
        TimeSpan? probeInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clockAlignmentService);
        if (State != PeripersonalOperatorWorkflowState.Recording || _session is null)
        {
            var blocked = new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Clock probe is blocked.",
                "Run Clock Probe is only valid while the single global Peripersonal recording is active.");
            return new PeripersonalClockProbeOperationResult(
                blocked,
                _session,
                new StudyClockAlignmentRunResult(
                    blocked,
                    new StudyClockAlignmentSummary(0, 0, null, null, null, null, null, null),
                    []),
                string.Empty);
        }

        if (!clockAlignmentService.RuntimeState.Available)
        {
            var unavailable = new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Clock alignment unavailable on this machine.",
                clockAlignmentService.RuntimeState.Detail);
            return new PeripersonalClockProbeOperationResult(
                unavailable,
                _session,
                new StudyClockAlignmentRunResult(
                    unavailable,
                    new StudyClockAlignmentSummary(0, 0, null, null, null, null, null, null),
                    []),
                string.Empty);
        }

        var effectiveDuration = duration ?? TimeSpan.FromSeconds(StudyClockAlignmentStreamContract.DefaultDurationSeconds);
        var effectiveProbeInterval = probeInterval ?? TimeSpan.FromMilliseconds(StudyClockAlignmentStreamContract.DefaultProbeIntervalMilliseconds);
        var request = new StudyClockAlignmentRunRequest(
            _session.SessionId,
            BuildDatasetId(_session.StudyId, _session.ParticipantRef, _session.SessionId),
            StudyClockAlignmentWindowKind.BackgroundSparse,
            effectiveDuration,
            effectiveProbeInterval,
            TimeSpan.FromMilliseconds(StudyClockAlignmentStreamContract.DefaultEchoGraceMilliseconds));
        var result = await clockAlignmentService.RunAsync(request, progress, cancellationToken).ConfigureAwait(false);
        var windowsCsvPath = Path.Combine(_session.WindowsSessionDirectory, "clock_alignment_roundtrip.csv");
        WriteClockAlignmentRoundTripCsv(_session, result, windowsCsvPath);
        return new PeripersonalClockProbeOperationResult(
            result.Outcome,
            _session,
            result,
            windowsCsvPath);
    }

    public Task<PeripersonalWorkflowOperationResult> OpenQuestionnaireBlockAsync(
        string requestId,
        string openStage,
        IReadOnlyList<string> screenSequence,
        int? conditionNumber = null,
        string? participantCommandScript = null,
        int? participantCommandIntervalMs = null,
        CancellationToken cancellationToken = default)
    {
        if (_session is null || State == PeripersonalOperatorWorkflowState.Idle)
        {
            return Task.FromResult(Result(OperationOutcomeKind.Warning, "Questionnaire launch is blocked.", "Prepare the session before opening the Quest questionnaire panel."));
        }

        if (!_questionnaireBlockOneSubmitted &&
            !IsOperatorQuestionnaireBlockOne(openStage))
        {
            return Task.FromResult(Result(OperationOutcomeKind.Warning, "Questionnaire launch is blocked.", "Only Questionnaire Block 1 can run before the global recording starts."));
        }

        if (!IsOperatorQuestionnaireBlockOne(openStage) &&
            State != PeripersonalOperatorWorkflowState.Recording)
        {
            return Task.FromResult(Result(OperationOutcomeKind.Warning, "Questionnaire launch is blocked.", "Questionnaire Blocks 2 and 3 run while the single global recording remains active."));
        }

        var panelOpenStage = NormalizePanelQuestionnaireStage(openStage);
        var panelScreenSequence = NormalizePanelQuestionnaireSequence(screenSequence, panelOpenStage);

        return SendAndVerifyAsync(
            "unity",
            "unity_quest_apk",
            _study.App.PackageId,
            "open_questionnaire_block",
            requiresObservedState: false,
            BuildSessionPayload(_session, null, payload =>
            {
                payload["request_id"] = RequireValue(requestId, nameof(requestId));
                payload["open_stage"] = panelOpenStage;
                payload["schema_id"] = PanelQuestionnaireId;
                payload["questionnaire_id"] = PanelQuestionnaireId;
                payload["screen_sequence"] = new JsonArray(panelScreenSequence.Select(item => JsonValue.Create(item)).ToArray());
                payload["operator_questionnaire_block_id"] = RequireValue(openStage, nameof(openStage));
                if (conditionNumber.HasValue)
                {
                    payload["condition_number"] = conditionNumber.Value;
                }

                if (!string.IsNullOrWhiteSpace(participantCommandScript))
                {
                    payload["debug_command_script"] = participantCommandScript.Trim();
                }

                if (participantCommandIntervalMs.HasValue)
                {
                    payload["debug_command_interval_ms"] = Math.Clamp(participantCommandIntervalMs.Value, 0, 10000);
                }
            }),
            _ => true,
            cancellationToken);
    }

    private static string NormalizePanelQuestionnaireStage(string openStage)
    {
        var trimmed = RequireValue(openStage, nameof(openStage));
        if (IsOperatorQuestionnaireBlockOne(trimmed))
        {
            return PanelQuestionnaireBlock1LanguageStage;
        }

        if (IsOperatorQuestionnaireBlockTwo(trimmed))
        {
            return PanelQuestionnaireBlock2SpatialFrameStage;
        }

        if (IsOperatorQuestionnaireBlockThree(trimmed))
        {
            return PanelQuestionnaireBlock3SpatialFrameStage;
        }

        return trimmed;
    }

    private static string[] NormalizePanelQuestionnaireSequence(
        IReadOnlyList<string> screenSequence,
        string panelOpenStage)
    {
        if (screenSequence.Count == 0)
        {
            return PanelQuestionnaireSequenceForStage(panelOpenStage);
        }

        var normalized = screenSequence
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .ToArray();
        if (normalized.Length == 0)
        {
            return PanelQuestionnaireSequenceForStage(panelOpenStage);
        }

        var blockAlias = normalized.FirstOrDefault(IsOperatorQuestionnaireBlock);
        if (!string.IsNullOrWhiteSpace(blockAlias))
        {
            return PanelQuestionnaireSequenceForBlockAlias(blockAlias);
        }

        return normalized;
    }

    private static bool IsOperatorQuestionnaireBlock(string value)
        => IsOperatorQuestionnaireBlockOne(value) ||
           IsOperatorQuestionnaireBlockTwo(value) ||
           IsOperatorQuestionnaireBlockThree(value);

    private static bool IsOperatorQuestionnaireBlockOne(string? value)
        => MatchesAny(value, "block-1", "questionnaire-block-1", "block_1_setup_maia2", "maia_spatial.block1");

    private static bool IsOperatorQuestionnaireBlockTwo(string? value)
        => MatchesAny(value, "block-2", "questionnaire-block-2", "block_2_spatial_frame_reference", "maia_spatial.block2");

    private static bool IsOperatorQuestionnaireBlockThree(string? value)
        => MatchesAny(value, "block-3", "questionnaire-block-3", "block_3_spatial_frame_reference", "maia_spatial.block3");

    private static bool MatchesAny(string? value, params string[] expected)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed) &&
               expected.Any(item => string.Equals(trimmed, item, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] PanelQuestionnaireSequenceForBlockAlias(string blockAlias)
    {
        if (IsOperatorQuestionnaireBlockTwo(blockAlias))
        {
            return PanelQuestionnaireBlock2Sequence;
        }

        if (IsOperatorQuestionnaireBlockThree(blockAlias))
        {
            return PanelQuestionnaireBlock3Sequence;
        }

        return PanelQuestionnaireBlock1Sequence;
    }

    private static string[] PanelQuestionnaireSequenceForStage(string stage)
        => string.Equals(stage, PanelQuestionnaireBlock2SpatialFrameStage, StringComparison.OrdinalIgnoreCase)
            ? PanelQuestionnaireBlock2Sequence
            : string.Equals(stage, PanelQuestionnaireBlock3SpatialFrameStage, StringComparison.OrdinalIgnoreCase)
                ? PanelQuestionnaireBlock3Sequence
                : PanelQuestionnaireBlock1Sequence;

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
            var backupResult = await _questBackupPuller.PullSessionBackupAsync(
                    new PeripersonalQuestBackupPullRequest(
                        _study.App.PackageId,
                        _session.SessionFolderName,
                        _session.WindowsSessionDirectory,
                        ExpectedFiles: PeripersonalAdbQuestBackupPuller.ExpectedSessionFiles),
                    cancellationToken)
                .ConfigureAwait(false);
            var closeResult = await _questAppCloser.CloseQuestAppsAsync(
                    _study.App.PackageId,
                    ResolveDeviceProfileValue("viscereality.panel.package", "io.github.mesmerprism.questquestionnaire.panel"),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!closeResult.Succeeded)
            {
                return Result(
                    OperationOutcomeKind.Warning,
                    "Stop Recording completed, but Quest app close failed.",
                    JoinDetails(backupResult.Outcome.Detail, closeResult.Detail),
                    items: backupResult.Outcome.SafeItems);
            }

            if (backupResult.Outcome.Kind != OperationOutcomeKind.Success)
            {
                return Result(
                    OperationOutcomeKind.Warning,
                    "Stop Recording completed and Quest apps closed, but Quest backup pullback needs attention.",
                    JoinDetails(backupResult.Outcome.Detail, closeResult.Detail),
                    items: backupResult.Outcome.SafeItems);
            }

            var backupWasConfigured = backupResult.PulledFiles.Count > 0;
            return Result(
                OperationOutcomeKind.Success,
                backupWasConfigured
                    ? "Stop Recording completed, Quest backup pulled, and Quest apps closed."
                    : "Stop Recording completed and Quest apps closed.",
                JoinDetails(backupResult.Outcome.Detail, closeResult.Detail),
                items: backupResult.Outcome.SafeItems);
        }

        return result;
    }

    public static string BuildSessionFolderName(string participantRef, string sessionId, DateTimeOffset timestampUtc)
        => string.Join(
            "_",
            SafeFolderToken(RequireValue(participantRef, nameof(participantRef))),
            SafeFolderToken(RequireValue(sessionId, nameof(sessionId))),
            timestampUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

    public static string BuildDatasetId(string studyId, string participantRef, string sessionId)
        => string.Join(
            "-",
            SafeFolderToken(RequireValue(studyId, nameof(studyId))),
            SafeFolderToken(RequireValue(participantRef, nameof(participantRef))),
            SafeFolderToken(RequireValue(sessionId, nameof(sessionId))));

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

    private async Task<OperationOutcome> EnsureUnityHttpBridgeForwardAsync(CancellationToken cancellationToken)
    {
        if (_unityHttpBridgeForwardOutcome is not null)
        {
            return _unityHttpBridgeForwardOutcome;
        }

        _unityHttpBridgeForwardOutcome = await _questHttpForwarder
            .EnsureUnityHttpBridgeForwardAsync(cancellationToken)
            .ConfigureAwait(false);
        return _unityHttpBridgeForwardOutcome;
    }

    private JsonObject BuildSessionPayload(
        PeripersonalPreparedSession session,
        PeripersonalSessionSetupRequest? request,
        Action<JsonObject>? customize = null)
    {
        var participantScopedRuntimeStateStream = $"{RuntimeStateStreamPrefix}_{session.ParticipantRef}";
        var participantScopedParticleStream = $"{ParticleTriggerStreamPrefix}_{session.ParticipantRef}";
        var datasetId = BuildDatasetId(session.StudyId, session.ParticipantRef, session.SessionId);
        var payload = new JsonObject
        {
            ["study_id"] = session.StudyId,
            ["session_id"] = session.SessionId,
            ["dataset_id"] = datasetId,
            ["dataset_hash"] = datasetId,
            ["participant_ref"] = session.ParticipantRef,
            ["handedness"] = session.Handedness,
            ["breath_tracking_controller_side"] = session.BreathTrackingControllerSide,
            ["session_folder_name"] = session.SessionFolderName,
            ["session_folder_convention"] = SessionFolderConvention,
            ["recording_lifecycle"] = RecordingLifecycle,
            ["language_code"] = NormalizeLanguageCode(request?.LanguageCode ?? session.LanguageCode),
            ["condition_id"] = request?.InitialConditionId ?? session.InitialConditionId,
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
        var datasetId = BuildDatasetId(session.StudyId, session.ParticipantRef, session.SessionId);
        var payload = new
        {
            protocol_version = PeripersonalCommandProtocol.CommandProtocolVersion,
            target_app = "windows_operator",
            study_id = session.StudyId,
            participant_ref = session.ParticipantRef,
            session_id = session.SessionId,
            dataset_id = datasetId,
            dataset_hash = datasetId,
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
            language_code = session.LanguageCode,
            initial_condition_id = session.InitialConditionId,
            questionnaire_id = PanelQuestionnaireId
        };

        File.WriteAllText(
            Path.Combine(session.WindowsSessionDirectory, "windows_session_metadata.json"),
            JsonSerializer.Serialize(payload, MetadataJsonOptions),
            Utf8NoBom);
    }

    private static void WriteClockAlignmentRoundTripCsv(
        PeripersonalPreparedSession session,
        StudyClockAlignmentRunResult result,
        string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var datasetId = BuildDatasetId(session.StudyId, session.ParticipantRef, session.SessionId);
        var builder = new StringBuilder();
        builder.AppendLine("participant_id,session_id,dataset_id,window_kind,probe_sequence,probe_sent_at_utc,probe_sent_lsl_seconds,echo_received_at_utc,echo_received_lsl_seconds,echo_sample_lsl_seconds,quest_received_at_utc,quest_received_lsl_seconds,quest_echo_lsl_seconds,quest_minus_windows_clock_seconds,roundtrip_seconds");
        foreach (var sample in result.Samples)
        {
            builder.Append(Csv(session.ParticipantRef)).Append(',');
            builder.Append(Csv(session.SessionId)).Append(',');
            builder.Append(Csv(datasetId)).Append(',');
            builder.Append(Csv(sample.WindowKind.ToString())).Append(',');
            builder.Append(sample.ProbeSequence.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Csv(sample.ProbeSentAtUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(sample.ProbeSentLocalClockSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Csv(sample.EchoReceivedAtUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(sample.EchoReceivedLocalClockSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(sample.EchoSampleTimestampSeconds.HasValue ? sample.EchoSampleTimestampSeconds.Value.ToString("R", CultureInfo.InvariantCulture) : string.Empty).Append(',');
            builder.Append(Csv(sample.QuestReceivedAtUtc)).Append(',');
            builder.Append(sample.QuestReceivedLocalClockSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(sample.QuestEchoLocalClockSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(sample.QuestMinusWindowsClockSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(sample.RoundTripSeconds.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), Utf8NoBom);
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

    private static string NormalizeLanguageCode(string? languageCode)
        => string.IsNullOrWhiteSpace(languageCode)
            ? "en"
            : languageCode.Trim().ToLowerInvariant();

    private static string SafeFolderToken(string value)
    {
        var sanitized = SafeFolderTokenPattern.Replace(value.Trim(), "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return text.IndexOfAny(['"', ',', '\r', '\n']) < 0
            ? text
            : $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string JoinDetails(params string[] details)
        => string.Join(" ", details.Where(static detail => !string.IsNullOrWhiteSpace(detail)));

    private void Track(PeripersonalCommandLedgerSnapshot snapshot)
    {
        _snapshots.RemoveAll(existing => string.Equals(existing.CommandId, snapshot.CommandId, StringComparison.OrdinalIgnoreCase));
        _snapshots.Add(snapshot);
    }

    private PeripersonalWorkflowOperationResult Result(
        OperationOutcomeKind kind,
        string summary,
        string detail,
        PeripersonalPreparedSession? session = null,
        IReadOnlyList<string>? items = null)
        => new(
            new OperationOutcome(kind, summary, detail, Items: items),
            session ?? _session,
            _snapshots.ToArray());
}

