using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ViscerealityCompanion.App;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App.ViewModels;

public sealed partial class StudyShellViewModel : ObservableObject, IDisposable
{
    private const int WorkflowTabIndex = 0;
    private const int PreSessionTabIndex = 1;
    private const int WindowsEnvironmentTabIndex = 6;
    // Keep verified-baseline tracking in code, but do not surface it in the operator shell for now.
    private static bool SurfaceVerifiedBaselineInShell => false;
    private const string TestSenderHeartbeatMode = "3";
    private const string TestSenderCoherenceMode = "2";
    private const string QuestSensorLockActivity = "SensorLockActivity";
    private static readonly bool SuppressClockAlignmentWindows =
        string.Equals(Environment.GetEnvironmentVariable("VC_SUPPRESS_ALIGNMENT_WINDOWS"), "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("VC_SUPPRESS_ALIGNMENT_WINDOWS"), "true", StringComparison.OrdinalIgnoreCase);
    private static readonly string[] QuestScreenshotShellCaptureMethods = ["screencap", "metacam"];
    private static readonly string[] QuestScreenshotRuntimeCaptureMethods = ["screencap", "metacam"];
    private const int WorkflowGuideValidationCaptureDurationSeconds = 20;
    private const int WorkflowClockAlignmentDurationSeconds = SussexClockAlignmentStreamContract.DefaultDurationSeconds;
    private const int WorkflowClockAlignmentBackgroundProbeIntervalSeconds = SussexClockAlignmentStreamContract.DefaultBackgroundProbeIntervalSeconds;
    private const int WorkflowClockAlignmentInitialBackgroundProbeDelaySeconds = 1;
    private const string LaunchSleepBlockButtonLabel = "Wake Headset To Enable Launching";
    private const string LaunchLockScreenBlockButtonLabel = "Clear Lock Screen Before Launching";
    private const string LaunchVisualBlockButtonLabel = "Clear Guardian Blocker Before Launching";
    private const string LaunchSleepBlockInstruction = "Wake the headset to enable launching.";
    private const string LaunchLockScreenInstruction = "Clear the headset lock screen before launching.";
    private const string LaunchVisualBlockInstruction = "Clear the current Guardian, tracking-loss, or ClearActivity blocker before launching.";
    private const string KioskMenuButtonAdvisory = "On the current Meta OS build, launch uses best-effort task pinning only and does not reliably disable the controller Meta/menu button.";
    private const string TwinCommandStreamName = "quest_twin_commands";
    private const string TwinCommandStreamType = "quest.twin.command";
    private const string TwinStateStreamName = "quest_twin_state";
    private const string TwinStateStreamType = "quest.twin.state";
    private const string TwinConfigStreamName = "quest_hotload_config";
    private const string TwinConfigStreamType = "quest.config";
    private const string ControllerCalibrationModeHotloadKey = "study_controller_breathing_use_principal_axis_calibration";
    private const string ParticipantSessionReviewPdfFileName = "session_review_report.pdf";
    private static readonly TimeSpan WorkflowClockAlignmentSparseProbeDuration = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan WorkflowClockAlignmentRunSafetyMargin = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WorkflowClockAlignmentStopTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan WorkflowUpstreamMonitorStopTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ControllerCalibrationModeConfirmationTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ControllerCalibrationModeConfirmationPollInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan WorkflowValidationPdfTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan WorkflowHzdbListTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan WorkflowHzdbPullTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StartupRuntimeConfigBaselineTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan StartupRuntimeConfigBaselinePollInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan TwinStateIdleThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BenchRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DeviceSnapshotRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProximityReadbackRefreshInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RecordingSampleInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MachineLslStateRefreshSettleDelay = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan WorkflowGuidePendingCommandWindow = TimeSpan.FromSeconds(15);
    private const int ClockAlignmentConsistencyHistoryLimit = 6;
    private const double ClockAlignmentConsistencyWarningMeanRoundTripSeconds = 0.08d;
    private const double ClockAlignmentConsistencyFailureMeanRoundTripSeconds = 0.16d;
    private const double ClockAlignmentConsistencyWarningSpanSeconds = 0.04d;
    private const double ClockAlignmentConsistencyFailureSpanSeconds = 0.09d;
    private const double ClockAlignmentConsistencyWarningProbeToProbeSeconds = 0.03d;
    private const double ClockAlignmentConsistencyFailureProbeToProbeSeconds = 0.08d;
    private static readonly Regex CommandSequenceRegex = new(@"\bseq=(\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly StudyShellDefinition _study;
    private AppSessionState _appSessionState;
    private StudyShellSessionState _studySessionState;
    private readonly IQuestControlService _questService;
    private readonly IHzdbService _hzdbService = HzdbServiceFactory.CreateDefault();
    private readonly ITwinModeBridge _twinBridge = TwinModeBridgeFactory.CreateShared();
    private readonly ITestLslSignalService _testLslSignalService = TestLslSignalServiceFactory.CreateDefault();
    private readonly IStudyClockAlignmentService _clockAlignmentService = StudyClockAlignmentServiceFactory.CreateDefault();
    private readonly ILslMonitorService _upstreamLslMonitorService = LslMonitorServiceFactory.CreateDefault();
    private readonly ILslStreamDiscoveryService _lslStreamDiscoveryService = LslStreamDiscoveryServiceFactory.CreateDefault();
    private readonly WindowsEnvironmentAnalysisService _windowsEnvironmentAnalysisService;
    private readonly WindowsInstallFootprintCleanupService _windowsInstallFootprintCleanupService;
    private readonly QuestWifiTransportDiagnosticsService _questWifiTransportDiagnosticsService = new();
    private readonly LocalAgentWorkspaceService _localAgentWorkspaceService = new();
    private readonly StudyDataRecorderService _studyDataRecorderService = new();
    private readonly RuntimeConfigWriter _runtimeConfigWriter = new();
    private readonly SemaphoreSlim _startupHotloadSyncSemaphore = new(1, 1);
    private readonly SemaphoreSlim _machineLslStateRefreshGate = new(1, 1);
    private readonly SussexVisualProfilesWorkspaceViewModel _visualProfiles;
    private readonly SussexControllerBreathingProfilesWorkspaceViewModel _controllerBreathingProfiles;
    private readonly SussexConditionWorkspaceViewModel _conditionProfiles;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer? _twinRefreshTimer;
    private readonly DispatcherTimer? _benchRefreshTimer;
    private readonly DispatcherTimer? _deviceSnapshotRefreshTimer;
    private readonly DispatcherTimer? _recordingSampleTimer;
    private CancellationTokenSource? _recordingSampleLoopCts;
    private Task? _recordingSampleLoopTask;
    private Window? _twinEventsWindow;
    private Window? _workflowGuideWindow;
    private Window? _clockAlignmentWindow;
    private Window? _experimentSessionWindow;
    private bool _initialized;
    private bool _twinRefreshPending;
    private bool _proximityRefreshPending;
    private bool _deviceSnapshotRefreshPending;
    private string _deviceSnapshotRefreshPhase = string.Empty;
    private bool _startupHotloadSyncDeferredUntilStudyStops;
    private string _activeFocusSectionId = string.Empty;
    private IReadOnlyDictionary<string, string> _reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private HeadsetAppStatus? _headsetStatus;
    private InstalledAppStatus? _installedAppStatus;
    private DeviceProfileStatus? _deviceProfileStatus;
    private string _lastInstalledAppStatusStagedApkPath = string.Empty;
    private QuestProximityStatus? _liveProximityStatus;
    private string? _liveProximitySelector;
    private DateTimeOffset? _lastProximityRefreshAtUtc;
    private DateTimeOffset? _lastDeviceSnapshotAtUtc;
    private QuestWifiTransportDiagnosticsResult? _questWifiTransportDiagnostics;
    private bool _questWifiTransportDiagnosticsPending;
    private string _questWifiTransportDiagnosticsInputKey = string.Empty;
    private bool _regularAdbSnapshotEnabled;
    private string _endpointDraft;
    private string _connectionSummary = "Quest connection has not been checked yet.";
    private string _connectionTransportSummary = "ADB over Wi-Fi: off.";
    private string _connectionTransportDetail = "Connect the Quest and switch to Wi-Fi ADB before relying on remote-only study control.";
    private string _headsetWifiSummary = "Headset Wi-Fi n/a.";
    private string _hostWifiSummary = "This PC Wi-Fi n/a.";
    private string _wifiNetworkMatchSummary = "Wi-Fi match unknown.";
    private string _questStatusSummary = "Waiting for Quest connection.";
    private string _questStatusDetail = "Connect to the headset to verify the Sussex study runtime and profile.";
    private string _headsetModel = "Unknown";
    private int _batteryPercent;
    private string _headsetBatteryLabel = "Battery n/a";
    private string _headsetSoftwareVersionLabel = "Headset OS n/a";
    private string _headsetPerformanceLabel = "CPU n/a / GPU n/a";
    private string _headsetForegroundLabel = "Foreground n/a";
    private string _headsetAwakeSummary = "Awake status not checked yet.";
    private string _headsetAwakeDetail = "Quest vrpowermanager readback will appear here once the shell can query the active headset selector.";
    private OperationOutcomeKind _headsetAwakeLevel = OperationOutcomeKind.Preview;
    private string _headsetSnapshotModeSummary = "Regular ADB readouts off. Use Refresh Snapshot when you need a fresh device state.";
    private string _deviceSnapshotTimestampLabel = "No headset snapshot received yet.";
    private OperationOutcomeKind _headsetSnapshotModeLevel = OperationOutcomeKind.Success;
    private string _pinnedBuildSummary = "Waiting for the bundled Sussex APK.";
    private string _pinnedBuildDetail = "The window will compare the bundled Sussex APK and the installed Quest build against the Sussex study hash.";
    private string _localApkSummary = "Waiting for the bundled Sussex APK.";
    private string _localApkDetail = "The Sussex shell installs the bundled Sussex APK and checks it against the Sussex study hash.";
    private string _installedApkSummary = "Installed build has not been checked yet.";
    private string _installedApkDetail = "Refresh the study status after connecting to the headset.";
    private string _stagedApkPath;
    private string _localApkHash = string.Empty;
    private OperationOutcomeKind _questStatusLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _connectionCardLevel = OperationOutcomeKind.Warning;
    private OperationOutcomeKind _connectionTransportLevel = OperationOutcomeKind.Warning;
    private OperationOutcomeKind _wifiNetworkMatchLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _pinnedBuildLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _pinnedBuildCardLevel = OperationOutcomeKind.Warning;
    private OperationOutcomeKind _localApkLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _installedApkLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _deviceProfileLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _deviceProfileCardLevel = OperationOutcomeKind.Warning;
    private OperationOutcomeKind _liveRuntimeLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _lslLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _controllerLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _heartbeatLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _coherenceLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _performanceLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _recenterLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _particlesLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _proximityLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _questScreenshotLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _testLslSenderLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _machineLslStateLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _windowsEnvironmentAnalysisLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _windowsEnvironmentCardLevel = OperationOutcomeKind.Warning;
    private OperationOutcomeKind _benchToolsCardLevel = OperationOutcomeKind.Warning;
    private string _deviceProfileSummary = "Pinned device profile has not been checked yet.";
    private string _deviceProfileDetail = "Refresh the study status after connecting to the headset.";
    private string _liveRuntimeSummary = "Waiting for quest_twin_state.";
    private string _liveRuntimeDetail = "Once the study APK starts publishing quest_twin_state, this window will focus on the Sussex-specific signals instead of the full raw keyspace.";
    private string _lslSummary = "Waiting for LSL runtime state.";
    private string _lslDetail = "The pinned stream target and live LSL connectivity will appear here once the study runtime is active.";
    private string _lslExpectedStreamLabel = $"{HrvBiofeedbackStreamContract.StreamName} / {HrvBiofeedbackStreamContract.StreamType}";
    private string _lslRuntimeTargetLabel = "Runtime target n/a";
    private string _lslConnectedStreamLabel = "Connected stream n/a";
    private string _lslConnectionStateLabel = "Connection counts n/a";
    private string _lslEchoStateLabel = "Echo state n/a";
    private string _lslBenchStateLabel = "Companion TEST sender off.";
    private string _lslStatusLineLabel = "Runtime did not publish an extra LSL status line.";
    private double _lslValuePercent;
    private string _lslValueLabel = "n/a";
    private string _controllerSummary = "Waiting for controller breathing state.";
    private string _controllerDetail = "The Sussex study expects controller-based breathing. Calibration and controller value reporting will appear here.";
    private string _heartbeatSummary = "Waiting for heartbeat state.";
    private string _heartbeatDetail = "The study runtime should report the latest heartbeat source and value over quest_twin_state.";
    private string _coherenceSummary = "Waiting for coherence state.";
    private string _coherenceDetail = "The study runtime should report the latest coherence route and value over quest_twin_state.";
    private string _coherenceRouteSummary = "Current route n/a.";
    private string _performanceSummary = "Waiting for performance telemetry.";
    private string _performanceDetail = "Current fps, frame time, and the runtime target will appear here once the study runtime publishes them.";
    private string _recenterSummary = "Waiting for recenter telemetry.";
    private string _recenterDetail = "The recenter action is available, and camera drift will appear here once the study runtime starts publishing it.";
    private string _particlesSummary = "Waiting for runtime particle state.";
    private string _particlesDetail = "Particle visibility and render suppression will appear here once the study runtime starts publishing them.";
    private string _proximitySummary = "Proximity sensor state has not been checked yet.";
    private string _proximityDetail = "Quest vrpowermanager readback will appear here once the shell can reach the active headset selector.";
    private string _proximityEvidenceLabel = "Latest readback n/a.";
    private string _proximityActionLabel = "Disable Proximity";
    private string _benchToolsSummary = "Bench tools need attention before bench checks.";
    private string _questScreenshotSummary = "No Quest screenshot captured yet.";
    private string _questScreenshotDetail = "Capture a Quest screenshot whenever the visible headset scene needs confirmation.";
    private string _questScreenshotPath = string.Empty;
    private BitmapImage? _questScreenshotPreview;
    private string _windowsEnvironmentAnalysisSummary = "No Windows environment analysis has been run yet.";
    private string _windowsEnvironmentAnalysisDetail = "Run Analyze Windows Environment to check local tooling, liblsl runtimes, the twin bridge, and the expected upstream sender visibility on this PC.";
    private string _windowsEnvironmentAnalysisTimestampLabel = "Not run yet.";
    private bool _windowsEnvironmentAnalysisHasRun;
    private string _machineLslStateSummary = "Machine LSL state has not been checked yet.";
    private string _machineLslStateDetail = "Refresh Machine LSL State to compare companion-owned LSL services against the streams currently visible on this Windows machine. Use it to catch duplicate upstream senders, stale companion outlets, or cleanup leaks.";
    private string _machineLslStateTimestampLabel = "Not checked yet.";
    private bool _machineLslStateHasRun;
    private OperationOutcomeKind _diagnosticsReportLevel = OperationOutcomeKind.Preview;
    private string _diagnosticsReportSummary = "No Sussex diagnostics report has been generated yet.";
    private string _diagnosticsReportDetail = "Generate a shareable report when LSL, quest_twin_state, or twin command acceptance needs to be diagnosed without relying on screenshots.";
    private string _diagnosticsReportTimestampLabel = "Not generated yet.";
    private string _diagnosticsReportFolderPath = string.Empty;
    private string _diagnosticsReportPdfPath = string.Empty;
    private SussexExpectedUpstreamState? _lslExpectedUpstreamProbeState;
    private string _questTwinStatePublisherInventoryDetail = "Quest twin-state outlet inventory has not run yet.";
    private QuestTwinStatePublisherInventory _questTwinStatePublisherInventory = new(
        OperationOutcomeKind.Preview,
        "Quest twin-state outlet inventory has not run yet.",
        "The probe has not inspected Windows-visible Quest twin-state publishers yet.",
        AnyPublisherVisible: false,
        ExpectedPublisherVisible: false,
        ExpectedSourceId: string.Empty,
        ExpectedSourceIdPrefix: string.Empty,
        VisiblePublishers: []);
    private string _windowsEnvironmentCardSummary = "Run the dedicated Windows environment checks before blaming the headset.";
    private string _windowsEnvironmentCardDetail = "Use the dedicated Windows environment page to inspect managed tooling, liblsl, machine-visible LSL streams, and the host-visible operator-data paths exported by the guided installer.";
    private string _testLslSenderSummary = "Windows TEST sender off.";
    private string _testLslSenderDetail = "Start the Windows TEST sender only for bench checks. It publishes smoothed HRV biofeedback samples on an irregular heartbeat-timed profile; Sussex treats packet arrival as heartbeat timing and the payload as the routed coherence value.";
    private string _testLslSenderValueLabel = "Not running";
    private string _testLslSenderActionLabel = "Start TEST Sender";
    private string _lslRuntimeLibrarySummary = "liblsl runtime not checked yet.";
    private string _lslRuntimeLibraryDetail = "The packaged sender/runtime path will appear here once the desktop runtime is initialized.";
    private string _lastTwinStateTimestampLabel = "No live app-state timestamp yet.";
    private string _lastActionLabel = "None";
    private string _lastActionDetail = "No study action has run yet.";
    private OperationOutcomeKind _lastActionLevel = OperationOutcomeKind.Preview;
    private string _lastConnectionActionLabel = string.Empty;
    private string _lastConnectionDetail = string.Empty;
    private OperationOutcomeKind _lastConnectionLevel = OperationOutcomeKind.Preview;
    private string _breathingStatusTitle = "Controller Breathing";
    private double _breathingDriverValuePercent;
    private string _breathingDriverValueText = "Current controller volume n/a";
    private double _controllerValuePercent;
    private string _controllerValueLabel = "n/a";
    private double _controllerCalibrationPercent;
    private string _controllerCalibrationLabel = "Calibration n/a";
    private bool _controllerCalibrationQualityVisible;
    private OperationOutcomeKind _controllerCalibrationQualityLevel = OperationOutcomeKind.Preview;
    private string _controllerCalibrationQualityBadge = "Calibration quality n/a";
    private string _controllerCalibrationQualitySummary = "Quality guidance will appear during controller validation.";
    private string _controllerCalibrationQualityExpectation = string.Empty;
    private string _controllerCalibrationQualityMetrics = "Progress n/a · Observed n/a · Accepted n/a · Rejected n/a · Target n/a · Acceptance n/a";
    private string _controllerCalibrationQualityCause = string.Empty;
    private string _controllerCalibrationQualityDetail = "Raw validation counters stay hidden until you expand details.";
    private int? _controllerValidationLastObservedFrames;
    private int? _controllerValidationLastAcceptedFrames;
    private int _controllerValidationStalledUpdateCount;
    private OperationOutcomeKind _workflowCurrentStepLevel = OperationOutcomeKind.Warning;
    private string _workflowCurrentStepSummary = "Finish headset setup before starting the Sussex protocol.";
    private string _workflowCurrentStepDetail = "Use the Sequential Guide for the pre-session protocol, then move into Experiment Session for the live participant run. Keep the legacy tabs for tuning and deeper inspection.";
    private OperationOutcomeKind _workflowSetupLevel = OperationOutcomeKind.Warning;
    private string _workflowSetupSummary = "Headset setup still needs attention.";
    private string _workflowSetupDetail = "Connect over Wi-Fi ADB, confirm the Sussex build, and apply the pinned device profile.";
    private OperationOutcomeKind _workflowKioskLevel = OperationOutcomeKind.Preview;
    private string _workflowKioskSummary = "Boundary setup and runtime launch have not started yet.";
    private string _workflowKioskDetail = "Keep the session on Wi-Fi ADB, wake the headset to enable launching, wake the controller, then launch the runtime.";
    private OperationOutcomeKind _workflowBenchLevel = OperationOutcomeKind.Preview;
    private string _workflowBenchSummary = "Bench verification is waiting for the Sussex runtime.";
    private string _workflowBenchDetail = "Run particles, recenter, LSL, and controller calibration checks before participant handoff.";
    private OperationOutcomeKind _workflowHandoffLevel = OperationOutcomeKind.Warning;
    private string _workflowHandoffSummary = "Participant handoff should finish in Experiment Session.";
    private string _workflowHandoffDetail = "Reset Calibration is part of the Sussex shell contract. Finish the handoff, then move into Experiment Session; the actual between-hands sleep and wake path should still end with the physical power button.";
    private OperationOutcomeKind _workflowParticipantStartLevel = OperationOutcomeKind.Warning;
    private string _workflowParticipantStartSummary = "Experiment Session opens the live participant-run controls after the workflow checks pass.";
    private string _workflowParticipantStartDetail = "Participant number entry, screenshot confirmation, and Start Recording in Experiment Session drive both the Windows recorder and the Quest-side backup recorder, including start/end clock-alignment bursts plus sparse background drift probes.";
    private OperationOutcomeKind _workflowParticipantEndLevel = OperationOutcomeKind.Warning;
    private string _workflowParticipantEndSummary = "Stop Recording waits for an active participant run.";
    private string _workflowParticipantEndDetail = "Stop Recording in Experiment Session closes the Quest-side backup recorder and the Windows recorder before the shell runs cleanup.";
    private string _workflowRuntimePendingSummary = "Current Sussex APK exposes recenter, calibration start, and particle toggles.";
    private string _workflowRuntimePendingDetail =
        "The current Sussex runtime contract includes reset calibration, participant start/end commands, shared session metadata handoff, and mirrored Windows/Quest study recording.";
    private string _participantIdDraft = string.Empty;
    private OperationOutcomeKind _participantEntryLevel = OperationOutcomeKind.Preview;
    private string _participantEntrySummary = "Enter a participant number before starting recorded data collection.";
    private string _participantEntryDetail = "Duplicate participant ids will warn but will not block the run.";
    private string _participantExistingSessionsLabel = "No previous participant sessions found on this machine.";
    private bool _participantHasExistingSessions;
    private OperationOutcomeKind _recordingLevel = OperationOutcomeKind.Preview;
    private string _recordingSummary = "Recorder idle.";
    private string _recordingDetail = "Use the Experiment Session window to enter a participant number and arm the recorder for the next participant run.";
    private string _recorderStateLabel = "Idle";
    private string _recordingSessionLabel = "No active participant session.";
    private string _recordingFolderPath = string.Empty;
    private string _recordingDevicePullFolderPath = string.Empty;
    private string _recordingPdfPath = string.Empty;
    private string _lastCompletedRecordingFolderPath = string.Empty;
    private string _lastCompletedRecordingDevicePullFolderPath = string.Empty;
    private string _lastCompletedRecordingPdfPath = string.Empty;
    private StudyConditionDefinition? _selectedCondition;
    private string _conditionSummary = "No Sussex condition selected.";
    private string _conditionDetail = "Choose a condition before starting a participant run.";
    private OperationOutcomeKind _conditionLevel = OperationOutcomeKind.Preview;
    private string _selectedConditionVisualProfileLabel = "Visual profile: n/a";
    private string _selectedConditionControllerBreathingProfileLabel = "Breathing profile: n/a";
    private bool _participantRunStopping;
    private string _recorderFaultDetail = string.Empty;
    private StudyDataRecordingSession? _activeRecordingSession;
    private CancellationTokenSource? _upstreamLslMonitorCts;
    private Task? _upstreamLslMonitorTask;
    private CancellationTokenSource? _backgroundClockAlignmentCts;
    private Task? _backgroundClockAlignmentTask;
    private int _nextClockAlignmentProbeSequence = 1;
    private bool _workflowGuideParticlesOnVerified;
    private bool _workflowGuideParticlesOffVerified;
    private string _latestDeviceRecordingSessionDir = string.Empty;
    private int _workflowGuideStepIndex;
    private OperationOutcomeKind _workflowGuideStepLevel = OperationOutcomeKind.Warning;
    private string _workflowGuideStepLabel = "Step 1 of 13";
    private string _workflowGuideStepTitle = "Verify USB visibility";
    private string _workflowGuideStepExplanation = "Start with a real USB connection. The headset must be visible over USB ADB before the guide can bootstrap remote control.";
    private string _workflowGuideStepSummary = "USB ADB has not been verified yet.";
    private string _workflowGuideStepDetail = "Connect the headset over USB, accept the in-headset debugging prompt if needed, then use Probe USB.";
    private string _workflowGuideGateSummary = "Complete this step before continuing.";
    private string _workflowGuideGateDetail = "Next stays disabled until the current step turns green.";
    private OperationOutcomeKind _workflowGuideActionLevel = OperationOutcomeKind.Preview;
    private string _workflowGuideActionSummary = "Use the step button below once.";
    private string _workflowGuideActionDetail = "The guide will show immediately when a click was accepted and whether it is still waiting for headset confirmation.";
    private string _workflowGuideNextActionSummary = "Use the step button below once.";
    private string _workflowGuideNextActionDetail = "Only the controls relevant to this step stay visible here.";
    private OperationOutcomeKind _workflowGuideQuestScreenshotLevel = OperationOutcomeKind.Warning;
    private string _workflowGuideQuestScreenshotSummary = "Visual confirmation still needed.";
    private string _workflowGuideQuestScreenshotDetail = "Capture a Quest screenshot in the particle step to verify the visible scene.";
    private string _workflowGuideQuestScreenshotPath = string.Empty;
    private BitmapImage? _workflowGuideQuestScreenshotPreview;
    private string _validationCaptureSummary = "No validation capture has been run yet.";
    private string _validationCaptureDetail = "Enter a temporary subject id and run the 20 second validation capture once the earlier setup steps are complete.";
    private string _validationCaptureLocalFolderPath = string.Empty;
    private string _validationCaptureDeviceSessionPath = string.Empty;
    private string _validationCaptureDevicePullFolderPath = string.Empty;
    private string _validationCapturePdfPath = string.Empty;
    private bool _validationCaptureRunning;
    private bool _validationCaptureCompleted;
    private string _validationCaptureParticipantId = string.Empty;
    private double _validationCaptureProgressPercent;
    private string _validationCaptureProgressLabel = "No validation capture is running yet.";
    private PointCollection _validationCaptureBreathingPlotPoints = [];
    private PointCollection _validationCaptureCoherencePlotPoints = [];
    private string _validationCaptureBreathingPlotSummary = "Breathing plot not loaded yet.";
    private string _validationCaptureCoherencePlotSummary = "Coherence plot not loaded yet.";
    private bool _validationCaptureBreathingPlotAvailable;
    private bool _validationCaptureCoherencePlotAvailable;
    private OperationOutcomeKind _clockAlignmentLevel = OperationOutcomeKind.Preview;
    private bool _clockAlignmentRunning;
    private string _clockAlignmentSummary = "Clock alignment has not run yet.";
    private string _clockAlignmentDetail = "Use Start Recording in Experiment Session to capture the dedicated Sussex clock-alignment bursts and sparse background drift probes.";
    private double _clockAlignmentProgressPercent;
    private string _clockAlignmentProgressLabel = "No clock alignment is running yet.";
    private string _clockAlignmentProbeStatsLabel = "No probes sent yet.";
    private string _clockAlignmentOffsetStatsLabel = "Offset estimate n/a";
    private string _clockAlignmentRoundTripStatsLabel = "Round-trip estimate n/a";
    private OperationOutcomeKind _clockAlignmentConsistencyLevel = OperationOutcomeKind.Preview;
    private string _clockAlignmentConsistencySummary = "Full-loop timing consistency has not been sampled yet.";
    private string _clockAlignmentConsistencyDetail = "Start Recording in Experiment Session to collect background clock-alignment probes and watch for unstable loop timing.";
    private string _clockAlignmentConsistencyMetricsLabel = "No background RTT consistency samples yet.";
    private OperationOutcomeKind _validationClockAlignmentStartLevel = OperationOutcomeKind.Preview;
    private string _validationClockAlignmentStartSummary = "Queued until recording starts.";
    private string _validationClockAlignmentStartDetail = "The validation flow begins with a dedicated 10 second echo burst before data collection.";
    private OperationOutcomeKind _validationClockAlignmentBackgroundLevel = OperationOutcomeKind.Preview;
    private string _validationClockAlignmentBackgroundSummary = "Armed after the start burst.";
    private string _validationClockAlignmentBackgroundDetail = "Sparse drift probes stay idle until the 20 second recording is underway.";
    private OperationOutcomeKind _validationClockAlignmentEndLevel = OperationOutcomeKind.Preview;
    private string _validationClockAlignmentEndSummary = "Queued until recording stops.";
    private string _validationClockAlignmentEndDetail = "A matching end burst runs after the 20 second recording so the session can compare start and end timing.";
    private bool _validationClockAlignmentBackgroundProbeObserved;
    private double _coherencePercent;
    private string _coherenceValueLabel = "n/a";
    private double _performancePercent;
    private string _performanceValueLabel = "n/a";
    private double _recenterDistancePercent;
    private string _recenterDistanceLabel = "n/a";
    private int _selectedPhaseTabIndex;
    private StudyTwinCommandRequest? _lastStudyTwinCommandRequest;
    private StudyTwinCommandRequest? _lastRecenterCommandRequest;
    private StudyTwinCommandRequest? _lastParticlesCommandRequest;
    private AutomaticBreathingRequest? _lastAutomaticBreathingRequest;
    private readonly Queue<ClockAlignmentConsistencyProbeMetrics> _recentBackgroundClockAlignmentMetrics = new();
    private string _lastRecordedRecenterConfirmationSignature = string.Empty;
    private string _lastRecordedRecenterEffectSignature = string.Empty;
    private string _lastRecordedParticlesConfirmationSignature = string.Empty;
    private string _lastRecordedParticlesEffectSignature = string.Empty;
    private int _workflowGuideLastRenderedStepIndex = -1;
    private DateTimeOffset? _workflowGuideParticleStepStartedAtUtc;
    private string? _testSenderRestoreHeartbeatMode;
    private string? _testSenderRestoreCoherenceMode;
    private StudyValueSection? _selectedLiveSection;
    private DateTimeOffset? _lastQuestScreenshotCapturedAtUtc;
    private string _questVisualConfirmationPendingReason = string.Empty;
    private const string AutomaticInstalledBuildHashingDetail =
        "Installed-build hashing stays on the manual refresh path so regular ADB snapshots do not keep pulling the APK over Wi-Fi.";

    public StudyShellViewModel(StudyShellDefinition study)
    {
        _study = study;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        var loadedSessionState = AppSessionState.Load();
        _appSessionState = NormalizeStartupSessionState(loadedSessionState, out var clearedPersistedRegularSnapshots);
        if (clearedPersistedRegularSnapshots)
        {
            _appSessionState.Save();
        }

        _studySessionState = StudyShellSessionState.Load();
        _regularAdbSnapshotEnabled = _appSessionState.RegularAdbSnapshotEnabled;
        _questService = QuestControlServiceFactory.CreateDefault(_appSessionState.ActiveEndpoint);
        _visualProfiles = new SussexVisualProfilesWorkspaceViewModel(study, _questService, _twinBridge);
        _controllerBreathingProfiles = new SussexControllerBreathingProfilesWorkspaceViewModel(study, _questService, _twinBridge);
        _conditionProfiles = new SussexConditionWorkspaceViewModel(study, _visualProfiles, _controllerBreathingProfiles);
        _windowsEnvironmentAnalysisService = new WindowsEnvironmentAnalysisService(
            _upstreamLslMonitorService,
            _lslStreamDiscoveryService,
            _clockAlignmentService,
            _testLslSignalService,
            _twinBridge);
        _windowsInstallFootprintCleanupService = new WindowsInstallFootprintCleanupService();
        _visualProfiles.StartupProfileChanged += OnStartupProfileChanged;
        _visualProfiles.SessionParameterActivity += OnSessionParameterActivity;
        _controllerBreathingProfiles.StartupProfileChanged += OnStartupProfileChanged;
        _controllerBreathingProfiles.SessionParameterActivity += OnSessionParameterActivity;
        _conditionProfiles.ActiveConditionsChanged += OnActiveConditionsChanged;
        _endpointDraft = _appSessionState.ActiveEndpoint ?? string.Empty;
        _stagedApkPath = ResolveInitialApkPath();

        foreach (var section in SectionCatalog)
        {
            LiveSections.Add(section);
        }

        _selectedLiveSection = LiveSections.FirstOrDefault();
        ReplaceActiveConditions(_study.Conditions.Where(condition => condition.IsActive).ToArray());

        _twinRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _twinRefreshTimer.Tick += OnTwinRefreshTimerTick;

        _benchRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = BenchRefreshInterval
        };
        _benchRefreshTimer.Tick += OnBenchRefreshTimerTick;

        _deviceSnapshotRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = DeviceSnapshotRefreshInterval
        };
        _deviceSnapshotRefreshTimer.Tick += OnDeviceSnapshotRefreshTimerTick;

        _recordingSampleTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = RecordingSampleInterval
        };
        _recordingSampleTimer.Tick += OnRecordingSampleTimerTick;

        if (_twinBridge is LslTwinModeBridge lslBridge)
        {
            lslBridge.ConfigureExpectedQuestStateSource(_study.App.PackageId);
            lslBridge.StateChanged += OnTwinBridgeStateChanged;
            lslBridge.TimingMarkerReceived += OnTwinTimingMarkerReceived;
        }

        _testLslSignalService.StateChanged += OnTestLslSignalServiceStateChanged;

        ProbeUsbCommand = new AsyncRelayCommand(ProbeUsbAsync);
        DiscoverWifiCommand = new AsyncRelayCommand(DiscoverWifiAsync);
        EnableWifiCommand = new AsyncRelayCommand(EnableWifiAsync);
        ConnectQuestCommand = new AsyncRelayCommand(ConnectQuestAsync);
        RefreshStatusCommand = new AsyncRelayCommand(RefreshStatusAsync);
        RefreshDeviceSnapshotCommand = new AsyncRelayCommand(RefreshDeviceSnapshotAsync);
        ProbeLslConnectionCommand = new AsyncRelayCommand(ProbeLslConnectionAsync);
        RefreshMachineLslStateCommand = new AsyncRelayCommand(RefreshMachineLslStateAsync);
        AnalyzeWindowsEnvironmentCommand = new AsyncRelayCommand(AnalyzeWindowsEnvironmentAsync);
        CleanInstallFootprintCommand = new AsyncRelayCommand(CleanInstallFootprintAsync);
        GenerateDiagnosticsReportCommand = new AsyncRelayCommand(GenerateDiagnosticsReportAsync);
        OpenDiagnosticsReportFolderCommand = new AsyncRelayCommand(OpenDiagnosticsReportFolderAsync);
        OpenDiagnosticsReportPdfCommand = new AsyncRelayCommand(OpenDiagnosticsReportPdfAsync);
        OpenWindowsEnvironmentPageCommand = new AsyncRelayCommand(OpenWindowsEnvironmentPageAsync);
        BrowseApkCommand = new AsyncRelayCommand(BrowseApkAsync);
        InstallStudyAppCommand = new AsyncRelayCommand(InstallStudyAppAsync);
        LaunchStudyAppCommand = new AsyncRelayCommand(LaunchStudyAppAsync, () => CanLaunchStudyRuntime);
        StopStudyAppCommand = new AsyncRelayCommand(StopStudyAppAsync);
        ToggleStudyRuntimeCommand = new AsyncRelayCommand(ToggleStudyRuntimeAsync, () => CanToggleStudyRuntime);
        ApplyPinnedDeviceProfileCommand = new AsyncRelayCommand(ApplyPinnedDeviceProfileAsync);
        ToggleProximityCommand = new AsyncRelayCommand(ToggleProximityAsync);
        CaptureQuestScreenshotCommand = new AsyncRelayCommand(CaptureQuestScreenshotAsync);
        OpenLastQuestScreenshotCommand = new AsyncRelayCommand(OpenLastQuestScreenshotAsync);
        ToggleTestLslSenderCommand = new AsyncRelayCommand(ToggleTestLslSenderAsync);
        StartBreathingCalibrationCommand = new AsyncRelayCommand(StartBreathingCalibrationAsync);
        StartDynamicAxisCalibrationCommand = new AsyncRelayCommand(StartDynamicAxisCalibrationAsync, () => CanStartDynamicAxisCalibration);
        StartFixedAxisCalibrationCommand = new AsyncRelayCommand(StartFixedAxisCalibrationAsync, () => CanStartFixedAxisCalibration);
        ResetBreathingCalibrationCommand = new AsyncRelayCommand(ResetBreathingCalibrationAsync);
        ToggleAutomaticBreathingModeCommand = new AsyncRelayCommand(ToggleAutomaticBreathingModeAsync);
        ToggleAutomaticBreathingRunCommand = new AsyncRelayCommand(ToggleAutomaticBreathingRunAsync);
        StartExperimentCommand = new AsyncRelayCommand(StartExperimentAsync);
        EndExperimentCommand = new AsyncRelayCommand(EndExperimentAsync);
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync);
        ApplySelectedConditionCommand = new AsyncRelayCommand(ApplySelectedConditionAsync, () => CanApplySelectedCondition);
        RecenterCommand = new AsyncRelayCommand(RecenterAsync);
        ParticlesOnCommand = new AsyncRelayCommand(ParticlesOnAsync);
        ParticlesOffCommand = new AsyncRelayCommand(ParticlesOffAsync);
        ToggleParticlesCommand = new AsyncRelayCommand(ToggleParticlesAsync);
        OpenTwinEventsWindowCommand = new AsyncRelayCommand(OpenTwinEventsWindowAsync);
        OpenWorkflowGuideWindowCommand = new AsyncRelayCommand(OpenWorkflowGuideWindowAsync);
        OpenExperimentSessionWindowCommand = new AsyncRelayCommand(OpenExperimentSessionWindowAsync);
        OpenLocalAgentWorkspaceCommand = new AsyncRelayCommand(OpenLocalAgentWorkspaceAsync);
        CopyLocalAgentPromptCommand = new AsyncRelayCommand(CopyLocalAgentPromptAsync);
        PreviousWorkflowGuideStepCommand = new AsyncRelayCommand(PreviousWorkflowGuideStepAsync);
        NextWorkflowGuideStepCommand = new AsyncRelayCommand(NextWorkflowGuideStepAsync);
        RunWorkflowValidationCaptureCommand = new AsyncRelayCommand(RunWorkflowValidationCaptureAsync);
        OpenRecordingSessionFolderCommand = new AsyncRelayCommand(OpenRecordingSessionFolderAsync);
        OpenRecordingSessionDevicePullFolderCommand = new AsyncRelayCommand(OpenRecordingSessionDevicePullFolderAsync);
        OpenRecordingSessionPdfCommand = new AsyncRelayCommand(OpenRecordingSessionPdfAsync);
        OpenValidationCaptureLocalFolderCommand = new AsyncRelayCommand(OpenValidationCaptureLocalFolderAsync);
        OpenValidationCaptureDevicePullFolderCommand = new AsyncRelayCommand(OpenValidationCaptureDevicePullFolderAsync);
        OpenValidationCapturePdfCommand = new AsyncRelayCommand(OpenValidationCapturePdfAsync);
        CloseWorkflowGuideWindowCommand = new AsyncRelayCommand(CloseWorkflowGuideWindowAsync);
        RegisterWorkflowGuideCommands();
        UpdateLslRuntimeLibraryState();
        UpdateHeadsetSnapshotModeState();
        UpdateDeviceSnapshotTimerState();
        RefreshConditionSelectionState();
        UpdateParticipantSessionState();
        UpdateWorkflowStatus();
    }

    public string StudyLabel => _study.Label;
    public string StudyId => _study.Id;
    public string StudyPartner => _study.Partner;
    public string StudyDescription => _study.Description;
    public string PinnedPackageId => _study.App.PackageId;
    public string PublishedBuildSummary => AppBuildIdentity.Current.Summary;
    public string PublishedBuildDetail => AppBuildIdentity.Current.Detail;
    public string ExperimentSessionWindowTitle => $"Sussex Experiment Session ({AppBuildIdentity.Current.ShortId})";
    public string OperatorDataRootPath => CompanionOperatorDataLayout.RootPath;
    public string ManagedToolingRootPath => OfficialQuestToolingLayout.RootPath;
    public string LocalAgentWorkspacePath => _localAgentWorkspaceService.RootPath;
    public string LocalAgentWorkspaceSummary =>
        "Use this folder as the environment for a local agent instead of the protected WindowsApps install. The companion mirrors a bundled CLI, the CLI docs, and the Sussex example catalogs there under the host-visible operator-data root.";
    public string LocalAgentWorkspaceDetail =>
        "Open the folder, then paste the built-in prompt into your local agent. The workspace includes `viscereality.ps1`, `viscereality.cmd`, and the bundled CLI payload under `cli\\current`, and the wrappers keep the CLI pointed at the same host-visible operator-data root as the installed app.";
    public SussexVisualProfilesWorkspaceViewModel VisualProfiles => _visualProfiles;
    public SussexControllerBreathingProfilesWorkspaceViewModel ControllerBreathingProfiles => _controllerBreathingProfiles;
    public SussexConditionWorkspaceViewModel ConditionProfiles => _conditionProfiles;
    public ObservableCollection<StudyConditionDefinition> Conditions { get; } = new();
    public bool HasConditions => Conditions.Count > 0;

    public StudyConditionDefinition? SelectedCondition
    {
        get => _selectedCondition;
        set
        {
            if (SetProperty(ref _selectedCondition, value))
            {
                RefreshConditionSelectionState();
            }
        }
    }

    public string ConditionSummary
    {
        get => _conditionSummary;
        private set => SetProperty(ref _conditionSummary, value);
    }

    public string ConditionDetail
    {
        get => _conditionDetail;
        private set => SetProperty(ref _conditionDetail, value);
    }

    public OperationOutcomeKind ConditionLevel
    {
        get => _conditionLevel;
        private set => SetProperty(ref _conditionLevel, value);
    }

    public string SelectedConditionVisualProfileLabel
    {
        get => _selectedConditionVisualProfileLabel;
        private set => SetProperty(ref _selectedConditionVisualProfileLabel, value);
    }

    public string SelectedConditionControllerBreathingProfileLabel
    {
        get => _selectedConditionControllerBreathingProfileLabel;
        private set => SetProperty(ref _selectedConditionControllerBreathingProfileLabel, value);
    }

    public bool CanApplySelectedCondition
        => HasConditions &&
           SelectedCondition is not null &&
           _activeRecordingSession is null &&
           !_participantRunStopping;

    public string DeviceSnapshotRefreshPhase => _deviceSnapshotRefreshPhase;

    private void RegisterWorkflowGuideCommands()
    {
        var commands =
            new[]
            {
                ProbeUsbCommand,
                EnableWifiCommand,
                ConnectQuestCommand,
                RefreshDeviceSnapshotCommand,
                AnalyzeWindowsEnvironmentCommand,
                InstallStudyAppCommand,
                ApplyPinnedDeviceProfileCommand,
                LaunchStudyAppCommand,
                ToggleTestLslSenderCommand,
                ToggleParticlesCommand,
                ParticlesOnCommand,
                ParticlesOffCommand,
                StartBreathingCalibrationCommand,
                RunWorkflowValidationCaptureCommand,
                ResetBreathingCalibrationCommand
            };

        foreach (var command in commands)
        {
            command.CanExecuteChanged += OnWorkflowGuideCommandCanExecuteChanged;
        }
    }

    private void OnWorkflowGuideCommandCanExecuteChanged(object? sender, EventArgs e)
    {
        _ = _dispatcher.InvokeAsync(UpdateWorkflowGuideState);
    }
    public string PinnedBuildVersion => string.IsNullOrWhiteSpace(_study.App.VersionName) ? "n/a" : _study.App.VersionName;
    public string PinnedBuildHash => _study.App.Sha256;
    public string PinnedLaunchComponent => string.IsNullOrWhiteSpace(_study.App.LaunchComponent) ? "n/a" : _study.App.LaunchComponent;
    public string PinnedAppNotes => string.IsNullOrWhiteSpace(_study.App.Notes) ? "No extra study-build notes." : _study.App.Notes;
    public string PinnedDeviceProfileId => _study.DeviceProfile.Id;
    public string PinnedDeviceProfileLabel => _study.DeviceProfile.Label;
    public StudyVerificationBaseline? VerificationBaseline => _study.App.VerificationBaseline;
    public bool CanChooseStudyApk => _study.App.AllowManualSelection;
    public string StudyApkStepTitle => CanChooseStudyApk ? "2. Verify study APK" : "2. Verify Sussex APK";
    public string StudyApkInstallButtonLabel => CanChooseStudyApk ? "Install Study APK" : "Install Sussex APK";
    public string StudyApkSourceLabel => CanChooseStudyApk ? "Selected study APK" : "Bundled Sussex APK";

    public string EndpointDraft
    {
        get => _endpointDraft;
        set
        {
            if (SetProperty(ref _endpointDraft, value))
            {
                OnPropertyChanged(nameof(CanToggleProximity));
            }
        }
    }

    public string ConnectionSummary
    {
        get => _connectionSummary;
        private set => SetProperty(ref _connectionSummary, value);
    }

    public string ConnectionTransportSummary
    {
        get => _connectionTransportSummary;
        private set => SetProperty(ref _connectionTransportSummary, value);
    }

    public string ConnectionTransportDetail
    {
        get => _connectionTransportDetail;
        private set => SetProperty(ref _connectionTransportDetail, value);
    }

    public string HeadsetWifiSummary
    {
        get => _headsetWifiSummary;
        private set => SetProperty(ref _headsetWifiSummary, value);
    }

    public string HostWifiSummary
    {
        get => _hostWifiSummary;
        private set => SetProperty(ref _hostWifiSummary, value);
    }

    public string WifiNetworkMatchSummary
    {
        get => _wifiNetworkMatchSummary;
        private set => SetProperty(ref _wifiNetworkMatchSummary, value);
    }

    public string QuestStatusSummary
    {
        get => _questStatusSummary;
        private set => SetProperty(ref _questStatusSummary, value);
    }

    public string QuestStatusDetail
    {
        get => _questStatusDetail;
        private set => SetProperty(ref _questStatusDetail, value);
    }

    public string HeadsetModel
    {
        get => _headsetModel;
        private set => SetProperty(ref _headsetModel, value);
    }

    public int BatteryPercent
    {
        get => _batteryPercent;
        private set => SetProperty(ref _batteryPercent, value);
    }

    public string HeadsetBatteryLabel
    {
        get => _headsetBatteryLabel;
        private set => SetProperty(ref _headsetBatteryLabel, value);
    }

    public string HeadsetSoftwareVersionLabel
    {
        get => _headsetSoftwareVersionLabel;
        private set => SetProperty(ref _headsetSoftwareVersionLabel, value);
    }

    public string HeadsetPerformanceLabel
    {
        get => _headsetPerformanceLabel;
        private set => SetProperty(ref _headsetPerformanceLabel, value);
    }

    public string HeadsetForegroundLabel
    {
        get => _headsetForegroundLabel;
        private set => SetProperty(ref _headsetForegroundLabel, value);
    }

    public string HeadsetAwakeSummary
    {
        get => _headsetAwakeSummary;
        private set => SetProperty(ref _headsetAwakeSummary, value);
    }

    public string HeadsetAwakeDetail
    {
        get => _headsetAwakeDetail;
        private set => SetProperty(ref _headsetAwakeDetail, value);
    }

    public OperationOutcomeKind HeadsetAwakeLevel
    {
        get => _headsetAwakeLevel;
        private set => SetProperty(ref _headsetAwakeLevel, value);
    }

    public string HeadsetSnapshotModeSummary
    {
        get => _headsetSnapshotModeSummary;
        private set => SetProperty(ref _headsetSnapshotModeSummary, value);
    }

    public string DeviceSnapshotTimestampLabel
    {
        get => _deviceSnapshotTimestampLabel;
        private set => SetProperty(ref _deviceSnapshotTimestampLabel, value);
    }

    public OperationOutcomeKind HeadsetSnapshotModeLevel
    {
        get => _headsetSnapshotModeLevel;
        private set => SetProperty(ref _headsetSnapshotModeLevel, value);
    }

    public bool RegularAdbSnapshotEnabled
    {
        get => _regularAdbSnapshotEnabled;
        set
        {
            if (!SetProperty(ref _regularAdbSnapshotEnabled, value))
            {
                return;
            }

            _appSessionState = _appSessionState.WithRegularAdbSnapshotEnabled(value);
            _appSessionState.Save();
            UpdateHeadsetSnapshotModeState();
            UpdateDeviceSnapshotTimerState();

            if (value)
            {
                _ = RefreshDeviceSnapshotBundleAsync(forceProximity: true);
            }
        }
    }

    public string PinnedBuildSummary
    {
        get => _pinnedBuildSummary;
        private set => SetProperty(ref _pinnedBuildSummary, value);
    }

    public string PinnedBuildDetail
    {
        get => _pinnedBuildDetail;
        private set => SetProperty(ref _pinnedBuildDetail, value);
    }

    public string LocalApkSummary
    {
        get => _localApkSummary;
        private set => SetProperty(ref _localApkSummary, value);
    }

    public string LocalApkDetail
    {
        get => _localApkDetail;
        private set => SetProperty(ref _localApkDetail, value);
    }

    public string InstalledApkSummary
    {
        get => _installedApkSummary;
        private set => SetProperty(ref _installedApkSummary, value);
    }

    public string InstalledApkDetail
    {
        get => _installedApkDetail;
        private set => SetProperty(ref _installedApkDetail, value);
    }

    public string StagedApkPath
    {
        get => _stagedApkPath;
        private set => SetProperty(ref _stagedApkPath, value);
    }

    public string LocalApkHash
    {
        get => _localApkHash;
        private set => SetProperty(ref _localApkHash, value);
    }

    public string InstalledApkHash
        => !string.IsNullOrWhiteSpace(_installedAppStatus?.InstalledSha256)
            ? _installedAppStatus!.InstalledSha256
            : GetReportedRuntimeApkHash();

    public string HeadsetSoftwareRelease => _headsetStatus?.SoftwareReleaseOrCodename ?? string.Empty;

    public string HeadsetSoftwareBuildId => _headsetStatus?.SoftwareBuildId ?? string.Empty;

    public string HeadsetSoftwareDisplayId => _headsetStatus?.SoftwareDisplayId ?? string.Empty;

    public OperationOutcomeKind QuestStatusLevel
    {
        get => _questStatusLevel;
        private set => SetProperty(ref _questStatusLevel, value);
    }

    public OperationOutcomeKind ConnectionCardLevel
    {
        get => _connectionCardLevel;
        private set => SetProperty(ref _connectionCardLevel, value);
    }

    public OperationOutcomeKind ConnectionTransportLevel
    {
        get => _connectionTransportLevel;
        private set => SetProperty(ref _connectionTransportLevel, value);
    }

    public OperationOutcomeKind WifiNetworkMatchLevel
    {
        get => _wifiNetworkMatchLevel;
        private set => SetProperty(ref _wifiNetworkMatchLevel, value);
    }

    public OperationOutcomeKind PinnedBuildLevel
    {
        get => _pinnedBuildLevel;
        private set => SetProperty(ref _pinnedBuildLevel, value);
    }

    public OperationOutcomeKind PinnedBuildCardLevel
    {
        get => _pinnedBuildCardLevel;
        private set => SetProperty(ref _pinnedBuildCardLevel, value);
    }

    public OperationOutcomeKind LocalApkLevel
    {
        get => _localApkLevel;
        private set => SetProperty(ref _localApkLevel, value);
    }

    public OperationOutcomeKind InstalledApkLevel
    {
        get => _installedApkLevel;
        private set => SetProperty(ref _installedApkLevel, value);
    }

    public OperationOutcomeKind DeviceProfileLevel
    {
        get => _deviceProfileLevel;
        private set => SetProperty(ref _deviceProfileLevel, value);
    }

    public OperationOutcomeKind DeviceProfileCardLevel
    {
        get => _deviceProfileCardLevel;
        private set => SetProperty(ref _deviceProfileCardLevel, value);
    }

    public OperationOutcomeKind LiveRuntimeLevel
    {
        get => _liveRuntimeLevel;
        private set => SetProperty(ref _liveRuntimeLevel, value);
    }

    public OperationOutcomeKind LslLevel
    {
        get => _lslLevel;
        private set => SetProperty(ref _lslLevel, value);
    }

    public OperationOutcomeKind ControllerLevel
    {
        get => _controllerLevel;
        private set => SetProperty(ref _controllerLevel, value);
    }

    public OperationOutcomeKind HeartbeatLevel
    {
        get => _heartbeatLevel;
        private set => SetProperty(ref _heartbeatLevel, value);
    }

    public OperationOutcomeKind CoherenceLevel
    {
        get => _coherenceLevel;
        private set => SetProperty(ref _coherenceLevel, value);
    }

    public OperationOutcomeKind PerformanceLevel
    {
        get => _performanceLevel;
        private set => SetProperty(ref _performanceLevel, value);
    }

    public OperationOutcomeKind RecenterLevel
    {
        get => _recenterLevel;
        private set => SetProperty(ref _recenterLevel, value);
    }

    public OperationOutcomeKind ParticlesLevel
    {
        get => _particlesLevel;
        private set => SetProperty(ref _particlesLevel, value);
    }

    public OperationOutcomeKind ProximityLevel
    {
        get => _proximityLevel;
        private set => SetProperty(ref _proximityLevel, value);
    }

    public OperationOutcomeKind QuestScreenshotLevel
    {
        get => _questScreenshotLevel;
        private set => SetProperty(ref _questScreenshotLevel, value);
    }

    public OperationOutcomeKind TestLslSenderLevel
    {
        get => _testLslSenderLevel;
        private set => SetProperty(ref _testLslSenderLevel, value);
    }

    public OperationOutcomeKind MachineLslStateLevel
    {
        get => _machineLslStateLevel;
        private set => SetProperty(ref _machineLslStateLevel, value);
    }

    public OperationOutcomeKind WindowsEnvironmentAnalysisLevel
    {
        get => _windowsEnvironmentAnalysisLevel;
        private set => SetProperty(ref _windowsEnvironmentAnalysisLevel, value);
    }

    public OperationOutcomeKind WindowsEnvironmentCardLevel
    {
        get => _windowsEnvironmentCardLevel;
        private set => SetProperty(ref _windowsEnvironmentCardLevel, value);
    }

    public OperationOutcomeKind BenchToolsCardLevel
    {
        get => _benchToolsCardLevel;
        private set => SetProperty(ref _benchToolsCardLevel, value);
    }

    public OperationOutcomeKind WorkflowCurrentStepLevel
    {
        get => _workflowCurrentStepLevel;
        private set => SetProperty(ref _workflowCurrentStepLevel, value);
    }

    public string WorkflowCurrentStepSummary
    {
        get => _workflowCurrentStepSummary;
        private set => SetProperty(ref _workflowCurrentStepSummary, value);
    }

    public string WorkflowCurrentStepDetail
    {
        get => _workflowCurrentStepDetail;
        private set => SetProperty(ref _workflowCurrentStepDetail, value);
    }

    public OperationOutcomeKind WorkflowSetupLevel
    {
        get => _workflowSetupLevel;
        private set => SetProperty(ref _workflowSetupLevel, value);
    }

    public string WorkflowSetupSummary
    {
        get => _workflowSetupSummary;
        private set => SetProperty(ref _workflowSetupSummary, value);
    }

    public string WorkflowSetupDetail
    {
        get => _workflowSetupDetail;
        private set => SetProperty(ref _workflowSetupDetail, value);
    }

    public OperationOutcomeKind WorkflowKioskLevel
    {
        get => _workflowKioskLevel;
        private set => SetProperty(ref _workflowKioskLevel, value);
    }

    public string WorkflowKioskSummary
    {
        get => _workflowKioskSummary;
        private set => SetProperty(ref _workflowKioskSummary, value);
    }

    public string WorkflowKioskDetail
    {
        get => _workflowKioskDetail;
        private set => SetProperty(ref _workflowKioskDetail, value);
    }

    public OperationOutcomeKind WorkflowBenchLevel
    {
        get => _workflowBenchLevel;
        private set => SetProperty(ref _workflowBenchLevel, value);
    }

    public string WorkflowBenchSummary
    {
        get => _workflowBenchSummary;
        private set => SetProperty(ref _workflowBenchSummary, value);
    }

    public string WorkflowBenchDetail
    {
        get => _workflowBenchDetail;
        private set => SetProperty(ref _workflowBenchDetail, value);
    }

    public OperationOutcomeKind WorkflowHandoffLevel
    {
        get => _workflowHandoffLevel;
        private set => SetProperty(ref _workflowHandoffLevel, value);
    }

    public string WorkflowHandoffSummary
    {
        get => _workflowHandoffSummary;
        private set => SetProperty(ref _workflowHandoffSummary, value);
    }

    public string WorkflowHandoffDetail
    {
        get => _workflowHandoffDetail;
        private set => SetProperty(ref _workflowHandoffDetail, value);
    }

    public OperationOutcomeKind WorkflowParticipantStartLevel
    {
        get => _workflowParticipantStartLevel;
        private set => SetProperty(ref _workflowParticipantStartLevel, value);
    }

    public string WorkflowParticipantStartSummary
    {
        get => _workflowParticipantStartSummary;
        private set => SetProperty(ref _workflowParticipantStartSummary, value);
    }

    public string WorkflowParticipantStartDetail
    {
        get => _workflowParticipantStartDetail;
        private set => SetProperty(ref _workflowParticipantStartDetail, value);
    }

    public OperationOutcomeKind WorkflowParticipantEndLevel
    {
        get => _workflowParticipantEndLevel;
        private set => SetProperty(ref _workflowParticipantEndLevel, value);
    }

    public string WorkflowParticipantEndSummary
    {
        get => _workflowParticipantEndSummary;
        private set => SetProperty(ref _workflowParticipantEndSummary, value);
    }

    public string WorkflowParticipantEndDetail
    {
        get => _workflowParticipantEndDetail;
        private set => SetProperty(ref _workflowParticipantEndDetail, value);
    }

    public string ParticipantIdDraft
    {
        get => _participantIdDraft;
        set
        {
            if (SetProperty(ref _participantIdDraft, value))
            {
                UpdateParticipantSessionState();
                RefreshBenchToolsStatus();
                UpdateWorkflowGuideState();
            }
        }
    }

    public OperationOutcomeKind ParticipantEntryLevel
    {
        get => _participantEntryLevel;
        private set => SetProperty(ref _participantEntryLevel, value);
    }

    public string ParticipantEntrySummary
    {
        get => _participantEntrySummary;
        private set => SetProperty(ref _participantEntrySummary, value);
    }

    public string ParticipantEntryDetail
    {
        get => _participantEntryDetail;
        private set => SetProperty(ref _participantEntryDetail, value);
    }

    public string ParticipantExistingSessionsLabel
    {
        get => _participantExistingSessionsLabel;
        private set => SetProperty(ref _participantExistingSessionsLabel, value);
    }

    public bool ParticipantHasExistingSessions
    {
        get => _participantHasExistingSessions;
        private set => SetProperty(ref _participantHasExistingSessions, value);
    }

    public OperationOutcomeKind RecordingLevel
    {
        get => _recordingLevel;
        private set => SetProperty(ref _recordingLevel, value);
    }

    public string RecordingSummary
    {
        get => _recordingSummary;
        private set => SetProperty(ref _recordingSummary, value);
    }

    public string RecordingDetail
    {
        get => _recordingDetail;
        private set => SetProperty(ref _recordingDetail, value);
    }

    public string RecorderStateLabel
    {
        get => _recorderStateLabel;
        private set => SetProperty(ref _recorderStateLabel, value);
    }

    public string RecordingSessionLabel
    {
        get => _recordingSessionLabel;
        private set => SetProperty(ref _recordingSessionLabel, value);
    }

    public string RecordingFolderPath
    {
        get => _recordingFolderPath;
        private set
        {
            if (SetProperty(ref _recordingFolderPath, NormalizeHostVisibleOperatorPath(value)))
            {
                OnPropertyChanged(nameof(CanOpenRecordingSessionFolder));
            }
        }
    }

    public string RecordingDevicePullFolderPath
    {
        get => _recordingDevicePullFolderPath;
        private set
        {
            if (SetProperty(ref _recordingDevicePullFolderPath, NormalizeHostVisibleOperatorPath(value)))
            {
                OnPropertyChanged(nameof(CanOpenRecordingSessionDevicePullFolder));
            }
        }
    }

    public string RecordingPdfPath
    {
        get => _recordingPdfPath;
        private set
        {
            if (SetProperty(ref _recordingPdfPath, NormalizeHostVisibleOperatorPath(value)))
            {
                OnPropertyChanged(nameof(CanOpenRecordingSessionPdf));
            }
        }
    }

    public bool CanOpenRecordingSessionFolder
        => CompanionOperatorDataLayout.TryResolveExistingDirectory(RecordingFolderPath, out _);

    public bool CanOpenRecordingSessionDevicePullFolder
        => HasPulledQuestBackupFolder(RecordingDevicePullFolderPath);

    public bool CanOpenRecordingSessionPdf
        => CompanionOperatorDataLayout.TryResolveExistingFile(RecordingPdfPath, out _);

    public string WorkflowRuntimePendingSummary
    {
        get => _workflowRuntimePendingSummary;
        private set => SetProperty(ref _workflowRuntimePendingSummary, value);
    }

    public string WorkflowRuntimePendingDetail
    {
        get => _workflowRuntimePendingDetail;
        private set => SetProperty(ref _workflowRuntimePendingDetail, value);
    }

    public int WorkflowGuideStepCount => WorkflowGuideCatalog.Length;

    public int WorkflowGuideStepIndex
    {
        get => _workflowGuideStepIndex;
        private set
        {
            if (SetProperty(ref _workflowGuideStepIndex, value))
            {
                UpdateWorkflowGuideState();
            }
        }
    }

    public OperationOutcomeKind WorkflowGuideStepLevel
    {
        get => _workflowGuideStepLevel;
        private set => SetProperty(ref _workflowGuideStepLevel, value);
    }

    public string WorkflowGuideStepLabel
    {
        get => _workflowGuideStepLabel;
        private set => SetProperty(ref _workflowGuideStepLabel, value);
    }

    public string WorkflowGuideStepTitle
    {
        get => _workflowGuideStepTitle;
        private set => SetProperty(ref _workflowGuideStepTitle, value);
    }

    public string WorkflowGuideStepExplanation
    {
        get => _workflowGuideStepExplanation;
        private set => SetProperty(ref _workflowGuideStepExplanation, value);
    }

    public string WorkflowGuideStepSummary
    {
        get => _workflowGuideStepSummary;
        private set => SetProperty(ref _workflowGuideStepSummary, value);
    }

    public string WorkflowGuideStepDetail
    {
        get => _workflowGuideStepDetail;
        private set => SetProperty(ref _workflowGuideStepDetail, value);
    }

    public string WorkflowGuideGateSummary
    {
        get => _workflowGuideGateSummary;
        private set => SetProperty(ref _workflowGuideGateSummary, value);
    }

    public string WorkflowGuideGateDetail
    {
        get => _workflowGuideGateDetail;
        private set => SetProperty(ref _workflowGuideGateDetail, value);
    }

    public OperationOutcomeKind WorkflowGuideActionLevel
    {
        get => _workflowGuideActionLevel;
        private set => SetProperty(ref _workflowGuideActionLevel, value);
    }

    public string WorkflowGuideActionSummary
    {
        get => _workflowGuideActionSummary;
        private set => SetProperty(ref _workflowGuideActionSummary, value);
    }

    public string WorkflowGuideActionDetail
    {
        get => _workflowGuideActionDetail;
        private set => SetProperty(ref _workflowGuideActionDetail, value);
    }

    public string WorkflowGuideNextActionSummary
    {
        get => _workflowGuideNextActionSummary;
        private set => SetProperty(ref _workflowGuideNextActionSummary, value);
    }

    public string WorkflowGuideNextActionDetail
    {
        get => _workflowGuideNextActionDetail;
        private set => SetProperty(ref _workflowGuideNextActionDetail, value);
    }

    public OperationOutcomeKind WorkflowGuideQuestScreenshotLevel
    {
        get => _workflowGuideQuestScreenshotLevel;
        private set => SetProperty(ref _workflowGuideQuestScreenshotLevel, value);
    }

    public string WorkflowGuideQuestScreenshotSummary
    {
        get => _workflowGuideQuestScreenshotSummary;
        private set => SetProperty(ref _workflowGuideQuestScreenshotSummary, value);
    }

    public string WorkflowGuideQuestScreenshotDetail
    {
        get => _workflowGuideQuestScreenshotDetail;
        private set => SetProperty(ref _workflowGuideQuestScreenshotDetail, value);
    }

    public string WorkflowGuideQuestScreenshotPath
    {
        get => _workflowGuideQuestScreenshotPath;
        private set => SetProperty(ref _workflowGuideQuestScreenshotPath, NormalizeHostVisibleOperatorPath(value));
    }

    public BitmapImage? WorkflowGuideQuestScreenshotPreview
    {
        get => _workflowGuideQuestScreenshotPreview;
        private set => SetProperty(ref _workflowGuideQuestScreenshotPreview, value);
    }

    public bool CanOpenWorkflowGuideQuestScreenshot
        => CompanionOperatorDataLayout.TryResolveExistingFile(WorkflowGuideQuestScreenshotPath, out _);

    public bool CanGoToPreviousWorkflowGuideStep => WorkflowGuideStepIndex > 0;

    public bool CanGoToNextWorkflowGuideStep
        => WorkflowGuideStepIndex < WorkflowGuideCatalog.Length - 1
            && IsWorkflowGuideStepReady(WorkflowGuideStepIndex);

    public bool WorkflowGuideIsFinalStep => WorkflowGuideStepIndex == WorkflowGuideCatalog.Length - 1;

    public bool WorkflowGuideShowsParticipantEntry => WorkflowGuideStepIndex == 11;

    public bool WorkflowGuideShowsRecordingState => WorkflowGuideStepIndex == 11;

    public bool WorkflowGuideShowsDeviceProfileRows => WorkflowGuideStepIndex == 5;

    public bool WorkflowGuideShowsCalibrationTelemetry => WorkflowGuideStepIndex == 10;

    public bool WorkflowGuideShowsWindowsEnvironmentAnalysis => WorkflowGuideStepIndex == 8;

    public bool WorkflowGuideShowsQuestScreenshotVerification => WorkflowGuideStepIndex == 9;

    public bool WorkflowGuideShowsValidationCaptureState => WorkflowGuideStepIndex == 11;

    public string ValidationCaptureActionSummary
        => BuildValidationCaptureActionSummary();

    public string ValidationCaptureSummary
    {
        get => _validationCaptureSummary;
        private set => SetProperty(ref _validationCaptureSummary, value);
    }

    public string ValidationCaptureDetail
    {
        get => _validationCaptureDetail;
        private set => SetProperty(ref _validationCaptureDetail, value);
    }

    public string ValidationCaptureLocalFolderPath
    {
        get => _validationCaptureLocalFolderPath;
        private set => SetProperty(ref _validationCaptureLocalFolderPath, NormalizeHostVisibleOperatorPath(value));
    }

    public string ValidationCaptureDeviceSessionPath
    {
        get => _validationCaptureDeviceSessionPath;
        private set => SetProperty(ref _validationCaptureDeviceSessionPath, value);
    }

    public string ValidationCaptureDevicePullFolderPath
    {
        get => _validationCaptureDevicePullFolderPath;
        private set => SetProperty(ref _validationCaptureDevicePullFolderPath, NormalizeHostVisibleOperatorPath(value));
    }

    public string ValidationCapturePdfPath
    {
        get => _validationCapturePdfPath;
        private set => SetProperty(ref _validationCapturePdfPath, NormalizeHostVisibleOperatorPath(value));
    }

    public bool ValidationCaptureRunning
    {
        get => _validationCaptureRunning;
        private set => SetProperty(ref _validationCaptureRunning, value);
    }

    public bool ValidationCaptureCompleted
    {
        get => _validationCaptureCompleted;
        private set => SetProperty(ref _validationCaptureCompleted, value);
    }

    public string ValidationCaptureParticipantId
    {
        get => _validationCaptureParticipantId;
        private set => SetProperty(ref _validationCaptureParticipantId, value);
    }

    public double ValidationCaptureProgressPercent
    {
        get => _validationCaptureProgressPercent;
        private set => SetProperty(ref _validationCaptureProgressPercent, value);
    }

    public string ValidationCaptureProgressLabel
    {
        get => _validationCaptureProgressLabel;
        private set => SetProperty(ref _validationCaptureProgressLabel, value);
    }

    public PointCollection ValidationCaptureBreathingPlotPoints
    {
        get => _validationCaptureBreathingPlotPoints;
        private set => SetProperty(ref _validationCaptureBreathingPlotPoints, value);
    }

    public PointCollection ValidationCaptureCoherencePlotPoints
    {
        get => _validationCaptureCoherencePlotPoints;
        private set => SetProperty(ref _validationCaptureCoherencePlotPoints, value);
    }

    public string ValidationCaptureBreathingPlotSummary
    {
        get => _validationCaptureBreathingPlotSummary;
        private set => SetProperty(ref _validationCaptureBreathingPlotSummary, value);
    }

    public string ValidationCaptureCoherencePlotSummary
    {
        get => _validationCaptureCoherencePlotSummary;
        private set => SetProperty(ref _validationCaptureCoherencePlotSummary, value);
    }

    public bool ValidationCaptureBreathingPlotAvailable
    {
        get => _validationCaptureBreathingPlotAvailable;
        private set => SetProperty(ref _validationCaptureBreathingPlotAvailable, value);
    }

    public bool ValidationCaptureCoherencePlotAvailable
    {
        get => _validationCaptureCoherencePlotAvailable;
        private set => SetProperty(ref _validationCaptureCoherencePlotAvailable, value);
    }

    public bool ClockAlignmentRunning
    {
        get => _clockAlignmentRunning;
        private set => SetProperty(ref _clockAlignmentRunning, value);
    }

    public OperationOutcomeKind ClockAlignmentLevel
    {
        get => _clockAlignmentLevel;
        private set => SetProperty(ref _clockAlignmentLevel, value);
    }

    public string ClockAlignmentSummary
    {
        get => _clockAlignmentSummary;
        private set => SetProperty(ref _clockAlignmentSummary, value);
    }

    public string ClockAlignmentDetail
    {
        get => _clockAlignmentDetail;
        private set => SetProperty(ref _clockAlignmentDetail, value);
    }

    public double ClockAlignmentProgressPercent
    {
        get => _clockAlignmentProgressPercent;
        private set => SetProperty(ref _clockAlignmentProgressPercent, value);
    }

    public string ClockAlignmentProgressLabel
    {
        get => _clockAlignmentProgressLabel;
        private set => SetProperty(ref _clockAlignmentProgressLabel, value);
    }

    public string ClockAlignmentProbeStatsLabel
    {
        get => _clockAlignmentProbeStatsLabel;
        private set => SetProperty(ref _clockAlignmentProbeStatsLabel, value);
    }

    public string ClockAlignmentOffsetStatsLabel
    {
        get => _clockAlignmentOffsetStatsLabel;
        private set => SetProperty(ref _clockAlignmentOffsetStatsLabel, value);
    }

    public string ClockAlignmentRoundTripStatsLabel
    {
        get => _clockAlignmentRoundTripStatsLabel;
        private set => SetProperty(ref _clockAlignmentRoundTripStatsLabel, value);
    }

    public OperationOutcomeKind ClockAlignmentConsistencyLevel
    {
        get => _clockAlignmentConsistencyLevel;
        private set => SetProperty(ref _clockAlignmentConsistencyLevel, value);
    }

    public string ClockAlignmentConsistencySummary
    {
        get => _clockAlignmentConsistencySummary;
        private set => SetProperty(ref _clockAlignmentConsistencySummary, value);
    }

    public string ClockAlignmentConsistencyDetail
    {
        get => _clockAlignmentConsistencyDetail;
        private set => SetProperty(ref _clockAlignmentConsistencyDetail, value);
    }

    public string ClockAlignmentConsistencyMetricsLabel
    {
        get => _clockAlignmentConsistencyMetricsLabel;
        private set => SetProperty(ref _clockAlignmentConsistencyMetricsLabel, value);
    }

    public bool CanOpenValidationCaptureLocalFolder
        => CompanionOperatorDataLayout.TryResolveExistingDirectory(ValidationCaptureLocalFolderPath, out _);

    public bool CanOpenValidationCaptureDevicePullFolder
        => HasPulledQuestBackupFolder(ValidationCaptureDevicePullFolderPath);

    public bool CanOpenValidationCapturePdf
        => CompanionOperatorDataLayout.TryResolveExistingFile(ValidationCapturePdfPath, out _);

    public string DeviceProfileSummary
    {
        get => _deviceProfileSummary;
        private set => SetProperty(ref _deviceProfileSummary, value);
    }

    public string DeviceProfileDetail
    {
        get => _deviceProfileDetail;
        private set => SetProperty(ref _deviceProfileDetail, value);
    }

    public string BenchToolsSummary
    {
        get => _benchToolsSummary;
        private set => SetProperty(ref _benchToolsSummary, value);
    }

    public string WindowsEnvironmentCardSummary
    {
        get => _windowsEnvironmentCardSummary;
        private set => SetProperty(ref _windowsEnvironmentCardSummary, value);
    }

    public string WindowsEnvironmentAnalysisSummary
    {
        get => _windowsEnvironmentAnalysisSummary;
        private set => SetProperty(ref _windowsEnvironmentAnalysisSummary, value);
    }

    public string MachineLslStateSummary
    {
        get => _machineLslStateSummary;
        private set => SetProperty(ref _machineLslStateSummary, value);
    }

    public string MachineLslStateDetail
    {
        get => _machineLslStateDetail;
        private set => SetProperty(ref _machineLslStateDetail, value);
    }

    public string MachineLslStateTimestampLabel
    {
        get => _machineLslStateTimestampLabel;
        private set => SetProperty(ref _machineLslStateTimestampLabel, value);
    }

    public bool MachineLslStateHasRun
    {
        get => _machineLslStateHasRun;
        private set => SetProperty(ref _machineLslStateHasRun, value);
    }

    public OperationOutcomeKind DiagnosticsReportLevel
    {
        get => _diagnosticsReportLevel;
        private set => SetProperty(ref _diagnosticsReportLevel, value);
    }

    public string DiagnosticsReportSummary
    {
        get => _diagnosticsReportSummary;
        private set => SetProperty(ref _diagnosticsReportSummary, value);
    }

    public string DiagnosticsReportDetail
    {
        get => _diagnosticsReportDetail;
        private set => SetProperty(ref _diagnosticsReportDetail, value);
    }

    public string DiagnosticsReportTimestampLabel
    {
        get => _diagnosticsReportTimestampLabel;
        private set => SetProperty(ref _diagnosticsReportTimestampLabel, value);
    }

    public string DiagnosticsReportFolderPath
    {
        get => _diagnosticsReportFolderPath;
        private set
        {
            if (SetProperty(ref _diagnosticsReportFolderPath, NormalizeHostVisibleOperatorPath(value)))
            {
                OnPropertyChanged(nameof(CanOpenDiagnosticsReportFolder));
            }
        }
    }

    public string DiagnosticsReportPdfPath
    {
        get => _diagnosticsReportPdfPath;
        private set
        {
            if (SetProperty(ref _diagnosticsReportPdfPath, NormalizeHostVisibleOperatorPath(value)))
            {
                OnPropertyChanged(nameof(CanOpenDiagnosticsReportPdf));
            }
        }
    }

    public bool CanOpenDiagnosticsReportPdf
        => CompanionOperatorDataLayout.TryResolveExistingFile(DiagnosticsReportPdfPath, out _);

    public bool CanOpenDiagnosticsReportFolder
        => CompanionOperatorDataLayout.TryResolveExistingDirectory(DiagnosticsReportFolderPath, out _);

    public string WindowsEnvironmentCardDetail
    {
        get => _windowsEnvironmentCardDetail;
        private set => SetProperty(ref _windowsEnvironmentCardDetail, value);
    }

    public string WindowsEnvironmentAnalysisDetail
    {
        get => _windowsEnvironmentAnalysisDetail;
        private set => SetProperty(ref _windowsEnvironmentAnalysisDetail, value);
    }

    public string WindowsEnvironmentAnalysisTimestampLabel
    {
        get => _windowsEnvironmentAnalysisTimestampLabel;
        private set => SetProperty(ref _windowsEnvironmentAnalysisTimestampLabel, value);
    }

    public bool WindowsEnvironmentAnalysisHasRun
    {
        get => _windowsEnvironmentAnalysisHasRun;
        private set => SetProperty(ref _windowsEnvironmentAnalysisHasRun, value);
    }

    public string QuestScreenshotSummary
    {
        get => _questScreenshotSummary;
        private set => SetProperty(ref _questScreenshotSummary, value);
    }

    public string QuestScreenshotDetail
    {
        get => _questScreenshotDetail;
        private set => SetProperty(ref _questScreenshotDetail, value);
    }

    public string QuestScreenshotPath
    {
        get => _questScreenshotPath;
        private set => SetProperty(ref _questScreenshotPath, NormalizeHostVisibleOperatorPath(value));
    }

    public BitmapImage? QuestScreenshotPreview
    {
        get => _questScreenshotPreview;
        private set => SetProperty(ref _questScreenshotPreview, value);
    }

    public string LiveRuntimeSummary
    {
        get => _liveRuntimeSummary;
        private set => SetProperty(ref _liveRuntimeSummary, value);
    }

    public string LiveRuntimeDetail
    {
        get => _liveRuntimeDetail;
        private set => SetProperty(ref _liveRuntimeDetail, value);
    }

    public string LslSummary
    {
        get => _lslSummary;
        private set => SetProperty(ref _lslSummary, value);
    }

    public string LslDetail
    {
        get => _lslDetail;
        private set => SetProperty(ref _lslDetail, value);
    }

    public string LslExpectedStreamLabel
    {
        get => _lslExpectedStreamLabel;
        private set => SetProperty(ref _lslExpectedStreamLabel, value);
    }

    public string LslRuntimeTargetLabel
    {
        get => _lslRuntimeTargetLabel;
        private set => SetProperty(ref _lslRuntimeTargetLabel, value);
    }

    public string LslConnectedStreamLabel
    {
        get => _lslConnectedStreamLabel;
        private set => SetProperty(ref _lslConnectedStreamLabel, value);
    }

    public string LslConnectionStateLabel
    {
        get => _lslConnectionStateLabel;
        private set => SetProperty(ref _lslConnectionStateLabel, value);
    }

    public string LslEchoStateLabel
    {
        get => _lslEchoStateLabel;
        private set => SetProperty(ref _lslEchoStateLabel, value);
    }

    public string LslBenchStateLabel
    {
        get => _lslBenchStateLabel;
        private set => SetProperty(ref _lslBenchStateLabel, value);
    }

    public string LslStatusLineLabel
    {
        get => _lslStatusLineLabel;
        private set => SetProperty(ref _lslStatusLineLabel, value);
    }

    public double LslValuePercent
    {
        get => _lslValuePercent;
        private set => SetProperty(ref _lslValuePercent, value);
    }

    public string LslValueLabel
    {
        get => _lslValueLabel;
        private set => SetProperty(ref _lslValueLabel, value);
    }

    public string ControllerSummary
    {
        get => _controllerSummary;
        private set => SetProperty(ref _controllerSummary, value);
    }

    public string ControllerDetail
    {
        get => _controllerDetail;
        private set => SetProperty(ref _controllerDetail, value);
    }

    public string HeartbeatSummary
    {
        get => _heartbeatSummary;
        private set => SetProperty(ref _heartbeatSummary, value);
    }

    public string HeartbeatDetail
    {
        get => _heartbeatDetail;
        private set => SetProperty(ref _heartbeatDetail, value);
    }

    public string CoherenceSummary
    {
        get => _coherenceSummary;
        private set => SetProperty(ref _coherenceSummary, value);
    }

    public string CoherenceDetail
    {
        get => _coherenceDetail;
        private set => SetProperty(ref _coherenceDetail, value);
    }

    public string CoherenceRouteSummary
    {
        get => _coherenceRouteSummary;
        private set => SetProperty(ref _coherenceRouteSummary, value);
    }

    public string PerformanceSummary
    {
        get => _performanceSummary;
        private set => SetProperty(ref _performanceSummary, value);
    }

    public string PerformanceDetail
    {
        get => _performanceDetail;
        private set => SetProperty(ref _performanceDetail, value);
    }

    public string RecenterSummary
    {
        get => _recenterSummary;
        private set => SetProperty(ref _recenterSummary, value);
    }

    public string RecenterDetail
    {
        get => _recenterDetail;
        private set => SetProperty(ref _recenterDetail, value);
    }

    public string ParticlesSummary
    {
        get => _particlesSummary;
        private set => SetProperty(ref _particlesSummary, value);
    }

    public string ParticlesDetail
    {
        get => _particlesDetail;
        private set => SetProperty(ref _particlesDetail, value);
    }

    public string ProximitySummary
    {
        get => _proximitySummary;
        private set => SetProperty(ref _proximitySummary, value);
    }

    public string ProximityDetail
    {
        get => _proximityDetail;
        private set => SetProperty(ref _proximityDetail, value);
    }

    public string ProximityEvidenceLabel
    {
        get => _proximityEvidenceLabel;
        private set => SetProperty(ref _proximityEvidenceLabel, value);
    }

    public string ProximityActionLabel
    {
        get => _proximityActionLabel;
        private set => SetProperty(ref _proximityActionLabel, value);
    }

    public bool? IsProximityToggleState
        => GetCurrentProximityEnabledState();

    public string StudyRuntimeActionLabel
        => IsStudyRuntimeToggleState
            ? "Stop Study Runtime"
            : IsLaunchBlockedBySleepingHeadset
                ? LaunchSleepBlockButtonLabel
                : IsLaunchBlockedByHeadsetLockScreen
                    ? LaunchLockScreenBlockButtonLabel
                : IsLaunchBlockedByHeadsetVisualBlocker
                    ? LaunchVisualBlockButtonLabel
                : "Launch Study Runtime";

    public bool IsStudyRuntimeToggleState
        => IsStudyRuntimeForeground();

    public bool IsLaunchBlockedBySleepingHeadset
        => !IsStudyRuntimeForeground() &&
           !IsHeadsetWakeBlockedByLockScreen() &&
           _headsetStatus?.IsAwake == false;

    public bool IsLaunchBlockedByHeadsetLockScreen
        => !IsStudyRuntimeForeground() && IsHeadsetWakeBlockedByLockScreen();

    public bool IsLaunchBlockedByHeadsetVisualBlocker
        => !IsLaunchBlockedByHeadsetLockScreen && !IsStudyRuntimeForeground() && _headsetStatus?.IsInWakeLimbo == true;

    public bool CanLaunchStudyRuntime
        => !IsLaunchBlockedBySleepingHeadset && !IsLaunchBlockedByHeadsetLockScreen && !IsLaunchBlockedByHeadsetVisualBlocker;

    public bool CanToggleStudyRuntime
        => IsStudyRuntimeToggleState || CanLaunchStudyRuntime;

    public string WorkflowGuideLaunchActionLabel
        => IsLaunchBlockedBySleepingHeadset
            ? LaunchSleepBlockButtonLabel
            : IsLaunchBlockedByHeadsetLockScreen
                ? LaunchLockScreenBlockButtonLabel
            : IsLaunchBlockedByHeadsetVisualBlocker
                ? LaunchVisualBlockButtonLabel
            : "Launch Study Runtime";

    public string TestLslSenderSummary
    {
        get => _testLslSenderSummary;
        private set => SetProperty(ref _testLslSenderSummary, value);
    }

    public string TestLslSenderDetail
    {
        get => _testLslSenderDetail;
        private set => SetProperty(ref _testLslSenderDetail, value);
    }

    public string TestLslSenderValueLabel
    {
        get => _testLslSenderValueLabel;
        private set => SetProperty(ref _testLslSenderValueLabel, value);
    }

    public string TestLslSenderActionLabel
    {
        get => _testLslSenderActionLabel;
        private set => SetProperty(ref _testLslSenderActionLabel, value);
    }

    public bool IsTestLslSenderToggleState
        => _testLslSignalService.IsRunning;

    public string LslRuntimeLibrarySummary
    {
        get => _lslRuntimeLibrarySummary;
        private set => SetProperty(ref _lslRuntimeLibrarySummary, value);
    }

    public string LslRuntimeLibraryDetail
    {
        get => _lslRuntimeLibraryDetail;
        private set => SetProperty(ref _lslRuntimeLibraryDetail, value);
    }

    public string LastTwinStateTimestampLabel
    {
        get => _lastTwinStateTimestampLabel;
        private set => SetProperty(ref _lastTwinStateTimestampLabel, value);
    }

    public string LastActionLabel
    {
        get => _lastActionLabel;
        private set => SetProperty(ref _lastActionLabel, value);
    }

    public string LastActionDetail
    {
        get => _lastActionDetail;
        private set => SetProperty(ref _lastActionDetail, value);
    }

    public OperationOutcomeKind LastActionLevel
    {
        get => _lastActionLevel;
        private set => SetProperty(ref _lastActionLevel, value);
    }

    public string BreathingStatusTitle
    {
        get => _breathingStatusTitle;
        private set => SetProperty(ref _breathingStatusTitle, value);
    }

    public double BreathingDriverValuePercent
    {
        get => _breathingDriverValuePercent;
        private set => SetProperty(ref _breathingDriverValuePercent, value);
    }

    public string BreathingDriverValueText
    {
        get => _breathingDriverValueText;
        private set => SetProperty(ref _breathingDriverValueText, value);
    }

    public double ControllerValuePercent
    {
        get => _controllerValuePercent;
        private set => SetProperty(ref _controllerValuePercent, value);
    }

    public string ControllerValueLabel
    {
        get => _controllerValueLabel;
        private set => SetProperty(ref _controllerValueLabel, value);
    }

    public double ControllerCalibrationPercent
    {
        get => _controllerCalibrationPercent;
        private set => SetProperty(ref _controllerCalibrationPercent, value);
    }

    public string ControllerCalibrationLabel
    {
        get => _controllerCalibrationLabel;
        private set => SetProperty(ref _controllerCalibrationLabel, value);
    }

    public bool ControllerCalibrationQualityVisible
    {
        get => _controllerCalibrationQualityVisible;
        private set => SetProperty(ref _controllerCalibrationQualityVisible, value);
    }

    public OperationOutcomeKind ControllerCalibrationQualityLevel
    {
        get => _controllerCalibrationQualityLevel;
        private set => SetProperty(ref _controllerCalibrationQualityLevel, value);
    }

    public string ControllerCalibrationQualityBadge
    {
        get => _controllerCalibrationQualityBadge;
        private set => SetProperty(ref _controllerCalibrationQualityBadge, value);
    }

    public string ControllerCalibrationQualitySummary
    {
        get => _controllerCalibrationQualitySummary;
        private set => SetProperty(ref _controllerCalibrationQualitySummary, value);
    }

    public string ControllerCalibrationQualityExpectation
    {
        get => _controllerCalibrationQualityExpectation;
        private set => SetProperty(ref _controllerCalibrationQualityExpectation, value);
    }

    public string ControllerCalibrationQualityMetrics
    {
        get => _controllerCalibrationQualityMetrics;
        private set => SetProperty(ref _controllerCalibrationQualityMetrics, value);
    }

    public string ControllerCalibrationQualityCause
    {
        get => _controllerCalibrationQualityCause;
        private set
        {
            if (SetProperty(ref _controllerCalibrationQualityCause, value))
            {
                OnPropertyChanged(nameof(HasControllerCalibrationQualityCause));
            }
        }
    }

    public bool HasControllerCalibrationQualityCause
        => !string.IsNullOrWhiteSpace(ControllerCalibrationQualityCause);

    public string ControllerCalibrationQualityDetail
    {
        get => _controllerCalibrationQualityDetail;
        private set => SetProperty(ref _controllerCalibrationQualityDetail, value);
    }

    public double CoherencePercent
    {
        get => _coherencePercent;
        private set => SetProperty(ref _coherencePercent, value);
    }

    public string CoherenceValueLabel
    {
        get => _coherenceValueLabel;
        private set => SetProperty(ref _coherenceValueLabel, value);
    }

    public double PerformancePercent
    {
        get => _performancePercent;
        private set => SetProperty(ref _performancePercent, value);
    }

    public string PerformanceValueLabel
    {
        get => _performanceValueLabel;
        private set => SetProperty(ref _performanceValueLabel, value);
    }

    public double RecenterDistancePercent
    {
        get => _recenterDistancePercent;
        private set => SetProperty(ref _recenterDistancePercent, value);
    }

    public string RecenterDistanceLabel
    {
        get => _recenterDistanceLabel;
        private set => SetProperty(ref _recenterDistanceLabel, value);
    }

    public int SelectedPhaseTabIndex
    {
        get => _selectedPhaseTabIndex;
        set => SetProperty(ref _selectedPhaseTabIndex, value);
    }

    public bool HasValidPinnedLocalApk
        => !string.IsNullOrWhiteSpace(StagedApkPath)
            && File.Exists(StagedApkPath)
            && HashMatches(LocalApkHash, _study.App.Sha256);

    public bool CanSendRecenterCommand
        => !string.IsNullOrWhiteSpace(_study.Controls.RecenterCommandActionId);

    public bool CanStartBreathingCalibration
        => !string.IsNullOrWhiteSpace(_study.Controls.StartBreathingCalibrationActionId);

    public bool CanStartDynamicAxisCalibration
        => CanStartBreathingCalibration && _controllerBreathingProfiles.IsAvailable;

    public bool CanStartFixedAxisCalibration
        => CanStartBreathingCalibration && _controllerBreathingProfiles.IsAvailable;

    public bool CanResetBreathingCalibration
        => !string.IsNullOrWhiteSpace(_study.Controls.ResetBreathingCalibrationActionId);

    public bool CanToggleAutomaticBreathingMode
        => !string.IsNullOrWhiteSpace(_study.Controls.SetBreathingModeControllerVolumeActionId)
            && !string.IsNullOrWhiteSpace(_study.Controls.SetBreathingModeAutomaticCycleActionId);

    public bool CanToggleAutomaticBreathingRun
        => !string.IsNullOrWhiteSpace(_study.Controls.SetBreathingModeAutomaticCycleActionId)
            && !string.IsNullOrWhiteSpace(_study.Controls.StartAutomaticBreathingActionId)
            && !string.IsNullOrWhiteSpace(_study.Controls.PauseAutomaticBreathingActionId);

    public string AutomaticBreathingSummary
    {
        get
        {
            var telemetry = CaptureAutomaticBreathingTelemetry();
            if (IsAutomaticBreathingPending(telemetry))
            {
                return $"{_lastAutomaticBreathingRequest!.RequestedLabel} sent from companion. Waiting for headset confirmation.";
            }

            return telemetry.AutomaticRoute && telemetry.AutomaticRunning == true
                ? "Automatic breathing driver confirmed."
                : telemetry.AutomaticRoute && telemetry.AutomaticRunning == false
                    ? "Automatic breathing selected but paused."
                    : telemetry.ControllerVolumeRoute
                    ? "Controller volume driver confirmed."
                    : telemetry.HasAnyTelemetry
                        ? "Breathing driver partially confirmed."
                        : "Automatic breathing state not confirmed yet.";
        }
    }

    public string AutomaticBreathingDetail
    {
        get
        {
            var telemetry = CaptureAutomaticBreathingTelemetry();
            var twinStateDetail = BuildAutomaticBreathingTwinStateDetail(telemetry);
            if (IsAutomaticBreathingPending(telemetry))
            {
                return $"{_lastAutomaticBreathingRequest!.RequestedLabel} was sent at {_lastAutomaticBreathingRequest.RequestedAtUtc:HH:mm:ss}. {twinStateDetail} Waiting for the requested breathing-driver state to appear in the live headset frame.".Trim();
            }

            return telemetry.AutomaticRoute && telemetry.AutomaticRunning == true
                ? $"{twinStateDetail} Automatic breathing is currently driving the sphere. Controller calibration stays visible for bench reference only and is not required in this mode.".Trim()
                : telemetry.AutomaticRoute && telemetry.AutomaticRunning == false
                    ? $"{twinStateDetail} Automatic breathing is selected, but the cycle is paused. Controller calibration is still optional until you switch back to controller volume.".Trim()
                    : telemetry.ControllerVolumeRoute
                        ? $"{twinStateDetail} Controller-volume breathing is currently driving the sphere. Controller calibration warnings only matter in this mode.".Trim()
                    : telemetry.HasAnyTelemetry
                        ? $"{twinStateDetail} The shell cannot classify this as the Sussex controller-volume baseline or the standalone automatic cycle yet.".Trim()
                        : "Live twin-state has not confirmed whether Sussex is currently in controller-volume or automatic breathing mode.";
        }
    }

    public string AutomaticBreathingModeActionLabel
        => IsAutomaticBreathingActiveFromTwinState()
            ? "Use Controller Volume Driver"
            : "Use Automatic Driver";

    public string AutomaticBreathingRunActionLabel
        => IsAutomaticBreathingActiveFromTwinState() && IsAutomaticBreathingRunningFromTwinState()
            ? "Pause Automatic"
            : "Start Automatic";

    public bool IsAutomaticBreathingRunToggleState
        => IsAutomaticBreathingActiveFromTwinState() && IsAutomaticBreathingRunningFromTwinState();

    public bool IsRecordingToggleState
        => _activeRecordingSession is not null || _participantRunStopping || IsExperimentActive();

    public string RecordingToggleActionLabel
        => IsRecordingToggleState ? "Stop Recording" : "Start Recording";

    public bool CanToggleRecording
        => IsRecordingToggleState ? CanEndParticipantExperiment : CanStartParticipantExperiment;

    public bool CanStartExperiment
        => !string.IsNullOrWhiteSpace(_study.Controls.StartExperimentActionId);

    public bool CanEndExperiment
        => !string.IsNullOrWhiteSpace(_study.Controls.EndExperimentActionId);

    public bool CanStartParticipantExperiment
        => CanStartExperiment
            && IsStudyRuntimeForeground()
            && !string.IsNullOrWhiteSpace(ParticipantIdDraft)
            && _activeRecordingSession is null
            && !_participantRunStopping
            && !ClockAlignmentRunning;

    public bool CanEndParticipantExperiment
        => CanEndExperiment
            && IsStudyRuntimeForeground()
            && !_participantRunStopping;

    public bool CanRunWorkflowValidationCapture
        => CanStartExperiment
            && CanEndExperiment
            && IsStudyRuntimeForeground()
            && !_participantRunStopping
            && !ClockAlignmentRunning
            && !ValidationCaptureRunning
            && !string.IsNullOrWhiteSpace(ParticipantIdDraft);

    public bool CanToggleParticles
        => !string.IsNullOrWhiteSpace(_study.Controls.ParticleVisibleOnActionId)
            && !string.IsNullOrWhiteSpace(_study.Controls.ParticleVisibleOffActionId);

    public bool? IsParticlesToggleState
        => GetCurrentReportedParticleVisibility();

    public string ParticlesToggleActionLabel
        => IsParticlesToggleState == true ? "Particles Off" : "Particles On";

    public bool CanToggleProximity
        => !string.IsNullOrWhiteSpace(ResolveHeadsetActionSelector());

    public bool CanToggleTestLslSender
        => _testLslSignalService.IsRunning
            || _testLslSignalService.RuntimeState.Available;

    public bool CanCaptureQuestScreenshot
        => _hzdbService.IsAvailable
            && !string.IsNullOrWhiteSpace(ResolveQuestScreenshotSelector());

    public bool CanOpenLastQuestScreenshot
        => CompanionOperatorDataLayout.TryResolveExistingFile(QuestScreenshotPath, out _);

    public ObservableCollection<StudyValueSection> LiveSections { get; } = new();

    public StudyValueSection? SelectedLiveSection
    {
        get => _selectedLiveSection;
        set
        {
            if (SetProperty(ref _selectedLiveSection, value))
            {
                RefreshFocusRows(forceRebuild: true);
            }
        }
    }

    public ObservableCollection<StudyStatusRow> DeviceProfileRows { get; } = new();
    public ObservableCollection<StudyStatusRowViewModel> FocusRows { get; } = new();
    public ObservableCollection<TwinStateEvent> RecentTwinEvents { get; } = new();
    public ObservableCollection<OperatorLogEntry> Logs { get; } = new();
    public ObservableCollection<WorkflowGuideCheckItem> WorkflowGuideChecks { get; } = new();
    public ObservableCollection<WorkflowGuideActionItem> WorkflowGuideActions { get; } = new();
    public ObservableCollection<WorkflowGuideCheckItem> MachineLslStateChecks { get; } = new();
    public ObservableCollection<WorkflowGuideCheckItem> WindowsEnvironmentChecks { get; } = new();
    public ObservableCollection<WorkflowGuideCheckItem> ValidationClockAlignmentChecks { get; } = new();

    public bool TryGetObservedLslValue(out double value, out string sourceKey)
    {
        if (TryGetConfiguredUnitIntervalValue(
                ["study.lsl.latest_default_value", "study.lsl.latest_ch0_value"],
                out value,
                out sourceKey))
        {
            return true;
        }

        if (TryGetConfiguredUnitIntervalValue(_study.Monitoring.LslValueKeys, out value, out sourceKey))
        {
            return true;
        }

        if (TryGetSignalMirrorValue("coherence_lsl", out value, out sourceKey))
        {
            return true;
        }

        if (TryGetSignalMirrorValue("breathing_lsl", out value, out sourceKey))
        {
            return true;
        }

        value = 0d;
        sourceKey = string.Empty;
        return false;
    }

    private bool TryGetRoutedCoherenceValue(bool routeMatchesExpectedDirectLsl, out double value, out string sourceKey)
    {
        if (routeMatchesExpectedDirectLsl &&
            TryGetObservedLslValue(out value, out sourceKey))
        {
            return true;
        }

        if (TryGetConfiguredUnitIntervalValue(_study.Monitoring.CoherenceValueKeys, out value, out sourceKey))
        {
            return true;
        }

        value = 0d;
        sourceKey = string.Empty;
        return false;
    }

    private static string FormatSourceKeySuffix(string sourceKey)
        => string.IsNullOrWhiteSpace(sourceKey)
            ? string.Empty
            : $" from `{sourceKey}`";

    public IReadOnlyDictionary<string, string> ReportedTwinStateSnapshot => _reportedTwinState;

    public AsyncRelayCommand ProbeUsbCommand { get; }
    public AsyncRelayCommand DiscoverWifiCommand { get; }
    public AsyncRelayCommand EnableWifiCommand { get; }
    public AsyncRelayCommand ConnectQuestCommand { get; }
    public AsyncRelayCommand RefreshStatusCommand { get; }
    public AsyncRelayCommand RefreshDeviceSnapshotCommand { get; }
    public AsyncRelayCommand ProbeLslConnectionCommand { get; }
    public AsyncRelayCommand RefreshMachineLslStateCommand { get; }
    public AsyncRelayCommand AnalyzeWindowsEnvironmentCommand { get; }
    public AsyncRelayCommand CleanInstallFootprintCommand { get; }
    public AsyncRelayCommand GenerateDiagnosticsReportCommand { get; }
    public AsyncRelayCommand OpenDiagnosticsReportFolderCommand { get; }
    public AsyncRelayCommand OpenDiagnosticsReportPdfCommand { get; }
    public AsyncRelayCommand OpenWindowsEnvironmentPageCommand { get; }
    public AsyncRelayCommand BrowseApkCommand { get; }
    public AsyncRelayCommand InstallStudyAppCommand { get; }
    public AsyncRelayCommand LaunchStudyAppCommand { get; }
    public AsyncRelayCommand StopStudyAppCommand { get; }
    public AsyncRelayCommand ToggleStudyRuntimeCommand { get; }
    public AsyncRelayCommand ApplyPinnedDeviceProfileCommand { get; }
    public AsyncRelayCommand ToggleProximityCommand { get; }
    public AsyncRelayCommand CaptureQuestScreenshotCommand { get; }
    public AsyncRelayCommand OpenLastQuestScreenshotCommand { get; }
    public AsyncRelayCommand ToggleTestLslSenderCommand { get; }
    public AsyncRelayCommand StartBreathingCalibrationCommand { get; }
    public AsyncRelayCommand StartDynamicAxisCalibrationCommand { get; }
    public AsyncRelayCommand StartFixedAxisCalibrationCommand { get; }
    public AsyncRelayCommand ResetBreathingCalibrationCommand { get; }
    public AsyncRelayCommand ToggleAutomaticBreathingModeCommand { get; }
    public AsyncRelayCommand ToggleAutomaticBreathingRunCommand { get; }
    public AsyncRelayCommand StartExperimentCommand { get; }
    public AsyncRelayCommand EndExperimentCommand { get; }
    public AsyncRelayCommand ToggleRecordingCommand { get; }
    public AsyncRelayCommand ApplySelectedConditionCommand { get; }
    public AsyncRelayCommand RecenterCommand { get; }
    public AsyncRelayCommand ParticlesOnCommand { get; }
    public AsyncRelayCommand ParticlesOffCommand { get; }
    public AsyncRelayCommand ToggleParticlesCommand { get; }
    public AsyncRelayCommand OpenTwinEventsWindowCommand { get; }
    public AsyncRelayCommand OpenWorkflowGuideWindowCommand { get; }
    public AsyncRelayCommand OpenExperimentSessionWindowCommand { get; }
    public AsyncRelayCommand OpenLocalAgentWorkspaceCommand { get; }
    public AsyncRelayCommand CopyLocalAgentPromptCommand { get; }
    public AsyncRelayCommand PreviousWorkflowGuideStepCommand { get; }
    public AsyncRelayCommand NextWorkflowGuideStepCommand { get; }
    public AsyncRelayCommand RunWorkflowValidationCaptureCommand { get; }
    public AsyncRelayCommand OpenRecordingSessionFolderCommand { get; }
    public AsyncRelayCommand OpenRecordingSessionDevicePullFolderCommand { get; }
    public AsyncRelayCommand OpenRecordingSessionPdfCommand { get; }
    public AsyncRelayCommand OpenValidationCaptureLocalFolderCommand { get; }
    public AsyncRelayCommand OpenValidationCaptureDevicePullFolderCommand { get; }
    public AsyncRelayCommand OpenValidationCapturePdfCommand { get; }
    public AsyncRelayCommand CloseWorkflowGuideWindowCommand { get; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _visualProfiles.InitializeAsync().ConfigureAwait(false);
        await _controllerBreathingProfiles.InitializeAsync().ConfigureAwait(false);
        await _conditionProfiles.InitializeAsync().ConfigureAwait(false);
        EnsureTwinBridgeMonitoringStarted();
        await DispatchAsync(RefreshBenchToolsStatus).ConfigureAwait(false);
        var autoConnected = await ConnectQuestCoreAsync(warnWhenMissingEndpoint: false).ConfigureAwait(false);
        if (!autoConnected)
        {
            await RefreshStatusAsync().ConfigureAwait(false);
        }

        _ = WarmLocalAgentWorkspaceAsync();
        QueueMachineLslStateRefresh();
    }

    public void Dispose()
    {
        CancelExperimentTimingTasks();

        if (_twinBridge is LslTwinModeBridge lslBridge)
        {
            lslBridge.StateChanged -= OnTwinBridgeStateChanged;
            lslBridge.TimingMarkerReceived -= OnTwinTimingMarkerReceived;
        }

        _testLslSignalService.StateChanged -= OnTestLslSignalServiceStateChanged;
        _visualProfiles.StartupProfileChanged -= OnStartupProfileChanged;
        _visualProfiles.SessionParameterActivity -= OnSessionParameterActivity;
        _controllerBreathingProfiles.StartupProfileChanged -= OnStartupProfileChanged;
        _controllerBreathingProfiles.SessionParameterActivity -= OnSessionParameterActivity;
        _conditionProfiles.ActiveConditionsChanged -= OnActiveConditionsChanged;

        if (_twinRefreshTimer is not null)
        {
            _twinRefreshTimer.Tick -= OnTwinRefreshTimerTick;
            _twinRefreshTimer.Stop();
        }

        if (_benchRefreshTimer is not null)
        {
            _benchRefreshTimer.Tick -= OnBenchRefreshTimerTick;
            _benchRefreshTimer.Stop();
        }

        if (_deviceSnapshotRefreshTimer is not null)
        {
            _deviceSnapshotRefreshTimer.Tick -= OnDeviceSnapshotRefreshTimerTick;
            _deviceSnapshotRefreshTimer.Stop();
        }

        if (_recordingSampleTimer is not null)
        {
            _recordingSampleTimer.Tick -= OnRecordingSampleTimerTick;
            _recordingSampleTimer.Stop();
        }

        StopRegularRecordingSamples();

        CloseTwinEventsWindow();
        CloseWorkflowGuideWindow();
        CloseClockAlignmentWindow();
        CloseExperimentSessionWindow();
        _activeRecordingSession?.Dispose();
        _visualProfiles.Dispose();
        _controllerBreathingProfiles.Dispose();
        _startupHotloadSyncSemaphore.Dispose();
        _machineLslStateRefreshGate.Dispose();
        _clockAlignmentService.Dispose();
        _testLslSignalService.Dispose();
    }

    private async Task ProbeUsbAsync()
    {
        var outcome = await _questService.ProbeUsbAsync().ConfigureAwait(false);
        RecordConnectionOutcome("Probe USB", outcome);
        await ApplyOutcomeAsync("Probe USB", outcome).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(outcome.Endpoint))
        {
            SaveSession(usbSerial: outcome.Endpoint);
            await RefreshStatusAsync().ConfigureAwait(false);
        }
    }

    private async Task DiscoverWifiAsync()
    {
        var outcome = await _questService.DiscoverWifiAsync().ConfigureAwait(false);
        await HandleConnectionOutcomeAsync("Find Wi-Fi Quest", outcome).ConfigureAwait(false);
    }

    private async Task EnableWifiAsync()
    {
        var outcome = await _questService.EnableWifiFromUsbAsync().ConfigureAwait(false);
        await HandleConnectionOutcomeAsync("Enable Wi-Fi ADB", outcome).ConfigureAwait(false);
    }

    private async Task ConnectQuestAsync()
    {
        await ConnectQuestCoreAsync(warnWhenMissingEndpoint: true).ConfigureAwait(false);
    }

    public async Task RefreshStatusAsync()
    {
        await RefreshLocalApkStatusAsync().ConfigureAwait(false);
        await RefreshDeviceSnapshotBundleAsync(
            forceProximity: true,
            includeHostWifiStatus: true,
            forceInstalledAppStatusRefresh: false).ConfigureAwait(false);
    }

    public async Task RefreshDeviceSnapshotAsync()
    {
        var started = await RefreshDeviceSnapshotBundleAsync(
            forceProximity: true,
            includeHostWifiStatus: true,
            forceInstalledAppStatusRefresh: false).ConfigureAwait(false);
        if (started)
        {
            return;
        }

        await ApplyOutcomeAsync(
            "Refresh Snapshot",
            new OperationOutcome(
                OperationOutcomeKind.Preview,
                "Headset snapshot already refreshing.",
                "A device refresh is already in progress. Wait for the live checks to update instead of clicking again.")).ConfigureAwait(false);
    }

    public async Task ProbeLslConnectionAsync()
    {
        var started = await RefreshDeviceSnapshotBundleAsync(forceProximity: true).ConfigureAwait(false);
        if (!started)
        {
            await ApplyOutcomeAsync(
                "Probe Connection",
                new OperationOutcome(
                    OperationOutcomeKind.Preview,
                    "Connection probe already refreshing.",
                    "A device refresh is already in progress. Wait for the live checks to update instead of clicking Probe Connection again.")).ConfigureAwait(false);
            return;
        }

        var wifiPreparation = await PrepareQuestWifiAdbForDiagnosticsAsync().ConfigureAwait(false);
        var expectedUpstream = await RefreshExpectedUpstreamProbeStateAsync(autoStartCompanionTestSender: true).ConfigureAwait(false);
        if (expectedUpstream.AutoStartedCompanionTestSender)
        {
            await RefreshDeviceSnapshotBundleAsync(
                forceProximity: true,
                includeHostWifiStatus: true,
                forceInstalledAppStatusRefresh: false).ConfigureAwait(false);
        }

        await RefreshQuestTwinStatePublisherInventoryAsync().ConfigureAwait(false);
        await RefreshQuestWifiTransportDiagnosticsAsync(wifiPreparation).ConfigureAwait(false);
        var machineLslState = await BuildMachineLslStateResultAsync().ConfigureAwait(false);
        await DispatchAsync(() =>
        {
            ApplyMachineLslStateResult(machineLslState);
            RefreshBenchToolsStatus();
        }).ConfigureAwait(false);
        await ApplyOutcomeAsync("Probe Connection", BuildLslConnectionProbeOutcome()).ConfigureAwait(false);
    }

    private async Task<QuestWifiAdbDiagnosticPreparationResult?> PrepareQuestWifiAdbForDiagnosticsAsync()
    {
        HeadsetAppStatus? initialHeadset = null;
        string requestedSelector = string.Empty;
        string? stagedApkPath = null;

        await DispatchAsync(() =>
        {
            initialHeadset = _headsetStatus;
            requestedSelector = ResolveHeadsetActionSelector();
            stagedApkPath = StagedApkPath;
        }).ConfigureAwait(false);

        if (initialHeadset?.IsConnected != true)
        {
            return null;
        }

        var effectiveRequestedSelector = string.IsNullOrWhiteSpace(requestedSelector)
            ? initialHeadset.ConnectionLabel
            : requestedSelector;
        var preparation = await new QuestWifiAdbDiagnosticPreparationService(_questService)
            .PrepareAsync(
                CreateStudyTarget(stagedApkPath),
                effectiveRequestedSelector,
                initialHeadset)
            .ConfigureAwait(false);

        var savedSelector = ResolveQuestWifiDiagnosticSelector(preparation, effectiveRequestedSelector);
        if (!string.IsNullOrWhiteSpace(savedSelector))
        {
            await DispatchAsync(() =>
            {
                EndpointDraft = savedSelector;
                SaveSession(endpoint: savedSelector);
            }).ConfigureAwait(false);
        }

        if (preparation.Attempted)
        {
            await RefreshDeviceSnapshotBundleAsync(
                forceProximity: true,
                includeHostWifiStatus: true,
                forceInstalledAppStatusRefresh: false).ConfigureAwait(false);
        }

        return preparation;
    }

    private async Task<QuestWifiTransportDiagnosticsResult?> RefreshQuestWifiTransportDiagnosticsAsync(
        QuestWifiAdbDiagnosticPreparationResult? preparation = null,
        CancellationToken cancellationToken = default)
    {
        HeadsetAppStatus? headset = null;
        string requestedSelector = string.Empty;

        await DispatchAsync(() =>
        {
            headset = _headsetStatus;
            requestedSelector = ResolveHeadsetActionSelector();
        }).ConfigureAwait(false);

        if (headset?.IsConnected != true)
        {
            await DispatchAsync(() =>
            {
                _questWifiTransportDiagnostics = null;
                _questWifiTransportDiagnosticsInputKey = string.Empty;
                _questWifiTransportDiagnosticsPending = false;
            }).ConfigureAwait(false);
            return null;
        }

        var effectiveRequestedSelector = string.IsNullOrWhiteSpace(requestedSelector)
            ? headset.ConnectionLabel
            : requestedSelector;
        var result = await _questWifiTransportDiagnosticsService
            .AnalyzeAsync(headset, effectiveRequestedSelector, cancellationToken)
            .ConfigureAwait(false);
        if (preparation is not null)
        {
            result = preparation.ApplyTo(result);
        }

        var inputKey = BuildQuestWifiTransportDiagnosticsInputKey(headset, effectiveRequestedSelector);
        await DispatchAsync(() =>
        {
            _questWifiTransportDiagnostics = result;
            _questWifiTransportDiagnosticsInputKey = inputKey;
            _questWifiTransportDiagnosticsPending = false;
        }).ConfigureAwait(false);
        return result;
    }

    private async Task RefreshQuestTwinStatePublisherInventoryAsync()
    {
        QuestTwinStatePublisherInventory inventory;
        try
        {
            inventory = await Task
                .Run(() => QuestTwinStatePublisherInventoryService.Inspect(_lslStreamDiscoveryService, _study.App.PackageId))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            inventory = new QuestTwinStatePublisherInventory(
                OperationOutcomeKind.Warning,
                "Quest twin-state outlet inventory failed.",
                exception.Message,
                AnyPublisherVisible: false,
                ExpectedPublisherVisible: false,
                ExpectedSourceId: string.Empty,
                ExpectedSourceIdPrefix: string.Empty,
                VisiblePublishers: []);
        }

        await DispatchAsync(() =>
        {
            _questTwinStatePublisherInventory = inventory;
            _questTwinStatePublisherInventoryDetail = QuestTwinStatePublisherInventoryService.RenderForOperator(inventory);
        }).ConfigureAwait(false);
    }

    public async Task AnalyzeWindowsEnvironmentAsync()
    {
        WindowsEnvironmentAnalysisResult result;
        try
        {
            if (AppBuildIdentity.Current.IsPackaged)
            {
                await Task.Run(() => _localAgentWorkspaceService.EnsureWorkspace()).ConfigureAwait(false);
            }

            var wifiPreparation = await PrepareQuestWifiAdbForDiagnosticsAsync().ConfigureAwait(false);
            result = await _windowsEnvironmentAnalysisService
                .AnalyzeAsync(BuildWindowsEnvironmentAnalysisRequest(wifiPreparation))
                .ConfigureAwait(false);
            await RefreshQuestWifiTransportDiagnosticsAsync(wifiPreparation).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await ApplyOutcomeAsync(
                "Analyze Windows Environment",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Windows environment analysis failed.",
                    exception.Message)).ConfigureAwait(false);
            return;
        }

        await DispatchAsync(() =>
        {
            ApplyWindowsEnvironmentAnalysisResult(result);
            RefreshBenchToolsStatus();
        }).ConfigureAwait(false);
        QueueMachineLslStateRefresh();

        await ApplyOutcomeAsync(
            "Analyze Windows Environment",
            new OperationOutcome(
                result.Level,
                result.Summary,
                BuildWindowsEnvironmentActionDetail(result))).ConfigureAwait(false);
    }

    private async Task CleanInstallFootprintAsync()
    {
        await DispatchAsync(() =>
        {
            WindowsEnvironmentAnalysisLevel = OperationOutcomeKind.Preview;
            WindowsEnvironmentAnalysisSummary = "Cleaning Windows install footprint...";
            WindowsEnvironmentAnalysisDetail = "Removing stale Viscereality shortcuts, older unpackaged agent-workspace mirrors, and legacy generic CLI exports where a branded CLI is already present.";
            WindowsEnvironmentAnalysisTimestampLabel = $"Started {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
        }).ConfigureAwait(false);

        WindowsInstallFootprintCleanupResult cleanupResult;
        WindowsEnvironmentAnalysisResult analysisResult;
        try
        {
            if (AppBuildIdentity.Current.IsPackaged)
            {
                await Task.Run(() => _localAgentWorkspaceService.EnsureWorkspace()).ConfigureAwait(false);
            }

            cleanupResult = _windowsInstallFootprintCleanupService.Cleanup();
            analysisResult = await _windowsEnvironmentAnalysisService
                .AnalyzeAsync(BuildWindowsEnvironmentAnalysisRequest())
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await ApplyOutcomeAsync(
                "Clean Install Footprint",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Windows install-footprint cleanup failed.",
                    exception.Message)).ConfigureAwait(false);
            return;
        }

        await DispatchAsync(() =>
        {
            ApplyWindowsEnvironmentAnalysisResult(analysisResult);
            RefreshBenchToolsStatus();
        }).ConfigureAwait(false);

        var outcomeItems = cleanupResult.RemovedPaths
            .Concat(cleanupResult.SkippedPaths)
            .ToArray();
        var actionDetail = string.Join(
            Environment.NewLine,
            new[]
            {
                cleanupResult.Detail,
                $"Post-clean analysis: {analysisResult.Summary}",
                analysisResult.Detail
            });

        await ApplyOutcomeAsync(
            "Clean Install Footprint",
            new OperationOutcome(
                cleanupResult.Level,
                cleanupResult.Summary,
                actionDetail,
                Items: outcomeItems)).ConfigureAwait(false);
    }

    private async Task GenerateDiagnosticsReportAsync()
    {
        await DispatchAsync(() =>
        {
            DiagnosticsReportLevel = OperationOutcomeKind.Preview;
            DiagnosticsReportSummary = "Generating Sussex diagnostics report...";
            DiagnosticsReportDetail = "Collecting Windows LSL inventory, Quest setup, quest_twin_state return path, and command acknowledgement evidence.";
            DiagnosticsReportTimestampLabel = $"Started {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
        }).ConfigureAwait(false);

        SussexDiagnosticsReportResult result;
        OperationOutcome pdfOutcome;
        try
        {
            if (AppBuildIdentity.Current.IsPackaged)
            {
                await Task.Run(() => _localAgentWorkspaceService.EnsureWorkspace()).ConfigureAwait(false);
            }

            var reportService = new SussexDiagnosticsReportService(
                _questService,
                _windowsEnvironmentAnalysisService,
                _lslStreamDiscoveryService,
                _testLslSignalService,
                _twinBridge);
            result = await reportService
                .GenerateAsync(new SussexDiagnosticsReportRequest(
                    _study,
                    DeviceSelector: ResolveHeadsetActionSelector(),
                    ProbeWaitDuration: TimeSpan.FromSeconds(12)))
                .ConfigureAwait(false);
            pdfOutcome = await GenerateDiagnosticsReportPdfAsync(result.Report, result.PdfPath).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchAsync(() =>
            {
                DiagnosticsReportLevel = OperationOutcomeKind.Failure;
                DiagnosticsReportSummary = "Sussex diagnostics report failed.";
                DiagnosticsReportDetail = exception.Message;
                DiagnosticsReportTimestampLabel = $"Failed {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
            }).ConfigureAwait(false);
            await ApplyOutcomeAsync(
                "Generate Diagnostics Report",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Sussex diagnostics report failed.",
                    exception.Message)).ConfigureAwait(false);
            return;
        }

        var displayLevel = result.Level == OperationOutcomeKind.Success && pdfOutcome.Kind == OperationOutcomeKind.Warning
            ? OperationOutcomeKind.Warning
            : result.Level;
        var pdfDetail = pdfOutcome.Kind == OperationOutcomeKind.Success
            ? $"PDF ready: {NormalizeHostVisibleOperatorPath(result.PdfPath)}"
            : $"PDF issue: {pdfOutcome.Summary} {pdfOutcome.Detail}".Trim();

        await DispatchAsync(() =>
        {
            ApplyWindowsEnvironmentAnalysisResult(result.Report.WindowsEnvironment);
            ApplyMachineLslStateResult(new LslMachineStateResult(
                result.Report.MachineLslState.Level,
                result.Report.MachineLslState.Summary,
                result.Report.MachineLslState.Detail,
                result.Report.MachineLslState.Checks
                    .Select(check => new LslMachineCheckResult(check.Label, check.Level, check.Summary, check.Detail))
                    .ToArray(),
                result.Report.MachineLslState.CompletedAtUtc));
            _questWifiTransportDiagnostics = result.Report.QuestWifiTransport;
            DiagnosticsReportLevel = displayLevel;
            DiagnosticsReportSummary = result.Summary;
            DiagnosticsReportDetail =
                $"{result.Detail} {pdfDetail} Folder: {NormalizeHostVisibleOperatorPath(result.ReportDirectory)}";
            DiagnosticsReportTimestampLabel = $"Generated {result.CompletedAtUtc.ToLocalTime():HH:mm:ss}.";
            DiagnosticsReportFolderPath = result.ReportDirectory;
            DiagnosticsReportPdfPath = result.PdfPath;
            RefreshBenchToolsStatus();
        }).ConfigureAwait(false);

        await ApplyOutcomeAsync(
            "Generate Diagnostics Report",
            new OperationOutcome(
                displayLevel,
                result.Summary,
                DiagnosticsReportDetail,
                Items: [result.PdfPath, result.TexPath, result.JsonPath, result.ReportDirectory])).ConfigureAwait(false);

        if (pdfOutcome.Kind == OperationOutcomeKind.Success && CompanionOperatorDataLayout.TryResolveExistingFile(result.PdfPath, out _))
        {
            await OpenValidationCaptureFileAsync(
                result.PdfPath,
                "Open Diagnostics Report",
                "Diagnostics report PDF is not available yet.",
                "Generate the diagnostics report first so the shareable PDF exists.").ConfigureAwait(false);
        }
        else
        {
            await OpenValidationCaptureFolderAsync(
                result.ReportDirectory,
                "Open Diagnostics Report Folder",
                "Diagnostics report folder is not available yet.",
                "Generate the diagnostics report first so the JSON and LaTeX artifacts exist.").ConfigureAwait(false);
        }
    }

    private WindowsEnvironmentAnalysisRequest BuildWindowsEnvironmentAnalysisRequest(
        QuestWifiAdbDiagnosticPreparationResult? wifiPreparation = null)
    {
        var headset = wifiPreparation?.EffectiveHeadset ?? _headsetStatus;
        var requestedSelector = string.IsNullOrWhiteSpace(ResolveHeadsetActionSelector())
            ? headset?.ConnectionLabel ?? string.Empty
            : ResolveHeadsetActionSelector();

        return new WindowsEnvironmentAnalysisRequest(
            string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamName)
                ? HrvBiofeedbackStreamContract.StreamName
                : _study.Monitoring.ExpectedLslStreamName,
            string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamType)
                ? HrvBiofeedbackStreamContract.StreamType
                : _study.Monitoring.ExpectedLslStreamType,
            QuestWifiTransport: headset?.IsConnected == true
                ? new QuestWifiTransportDiagnosticsContext(headset, requestedSelector, wifiPreparation)
                : null);
    }

    private void ApplyWindowsEnvironmentAnalysisResult(WindowsEnvironmentAnalysisResult result)
    {
        WindowsEnvironmentAnalysisHasRun = true;
        WindowsEnvironmentAnalysisLevel = result.Level;
        WindowsEnvironmentAnalysisSummary = result.Summary;
        WindowsEnvironmentAnalysisDetail = BuildWindowsEnvironmentSummaryDetail(result);
        WindowsEnvironmentAnalysisTimestampLabel = $"Last analyzed {result.CompletedAtUtc.ToLocalTime():HH:mm:ss}.";
        ReplaceWorkflowGuideCheckItems(
            WindowsEnvironmentChecks,
            result.Checks
                .Select(check => new WorkflowGuideCheckItem(check.Label, check.Summary, check.Detail, check.Level))
                .ToArray());
    }

    private static string BuildWindowsEnvironmentSummaryDetail(WindowsEnvironmentAnalysisResult result)
    {
        var attentionItems = result.Checks
            .Where(check => check.Level is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
            .Select(check => $"{check.Label}: {check.Summary}")
            .ToArray();

        if (attentionItems.Length == 0)
        {
            return $"{result.Detail} All Windows-side prerequisites and the current Quest Wi-Fi transport path that the shell can verify are present.";
        }

        return $"{result.Detail} {string.Join(" ", attentionItems)}";
    }

    private static string BuildWindowsEnvironmentActionDetail(WindowsEnvironmentAnalysisResult result)
        => string.Join(
            Environment.NewLine,
            new[] { result.Detail }
                .Concat(result.Checks.Select(check => $"{check.Label}: {check.Summary}")));

    private void QueueQuestWifiTransportDiagnosticsRefresh(bool force = false)
    {
        if (_headsetStatus?.IsConnected != true)
        {
            _questWifiTransportDiagnostics = null;
            _questWifiTransportDiagnosticsInputKey = string.Empty;
            _questWifiTransportDiagnosticsPending = false;
            return;
        }

        var requestedSelector = ResolveHeadsetActionSelector();
        var currentInputKey = BuildQuestWifiTransportDiagnosticsInputKey(_headsetStatus, requestedSelector);

        if (!force &&
            _questWifiTransportDiagnosticsPending)
        {
            return;
        }

        if (!force &&
            string.Equals(_questWifiTransportDiagnosticsInputKey, currentInputKey, StringComparison.OrdinalIgnoreCase) &&
            _questWifiTransportDiagnostics is not null &&
            DateTimeOffset.UtcNow - _questWifiTransportDiagnostics.CheckedAtUtc < TimeSpan.FromSeconds(20))
        {
            return;
        }

        _questWifiTransportDiagnosticsPending = true;
        _questWifiTransportDiagnosticsInputKey = currentInputKey;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _questWifiTransportDiagnosticsService
                    .AnalyzeAsync(_headsetStatus, requestedSelector)
                    .ConfigureAwait(false);
                await DispatchAsync(() => _questWifiTransportDiagnostics = result).ConfigureAwait(false);
            }
            catch
            {
                // Best effort only. Explicit analysis commands surface actionable failures.
            }
            finally
            {
                await DispatchAsync(() => _questWifiTransportDiagnosticsPending = false).ConfigureAwait(false);
            }
        });
    }

    private WorkflowGuideCheckItem BuildWifiTransportWorkflowGuideCheckItem()
    {
        if (_headsetStatus?.IsConnected != true)
        {
            return new WorkflowGuideCheckItem(
                "Wi-Fi router path",
                "Wi-Fi router path not checked yet.",
                "Connect the headset first. Probe Connection or Analyze Windows Environment will then try to switch onto Wi-Fi ADB before checking whether Windows can actually reach the Quest endpoint over the current router path.",
                OperationOutcomeKind.Warning);
        }

        if (_questWifiTransportDiagnostics is null)
        {
            return new WorkflowGuideCheckItem(
                "Wi-Fi router path",
                _headsetStatus.IsWifiAdbTransport
                    ? "Wi-Fi router path is still being measured."
                    : "Quest is not on Wi-Fi ADB yet.",
                _headsetStatus.IsWifiAdbTransport
                    ? "The shell is waiting for a live PC↔Quest Wi-Fi transport probe. Refresh the snapshot again if this stays pending after the current ADB selector stabilizes."
                    : "Probe Connection or Analyze Windows Environment will try to switch the active Quest transport onto Wi-Fi ADB automatically. If that still fails, keep USB attached, accept any in-headset debugging prompt, and then use Connect Quest with the current headset Wi-Fi IP plus port 5555.",
                _headsetStatus.IsWifiAdbTransport ? OperationOutcomeKind.Preview : OperationOutcomeKind.Warning);
        }

        return new WorkflowGuideCheckItem(
            "Wi-Fi router path",
            _questWifiTransportDiagnostics.Summary,
            _questWifiTransportDiagnostics.Detail,
            _questWifiTransportDiagnostics.Level);
    }

    public Task RefreshMachineLslStateAsync()
        => RefreshMachineLslStateCoreAsync(reportOutcome: true);

    public Task OpenWindowsEnvironmentPageAsync()
    {
        SelectedPhaseTabIndex = WindowsEnvironmentTabIndex;
        return Task.CompletedTask;
    }

    private void QueueMachineLslStateRefresh(TimeSpan? delay = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (delay is { } requestedDelay && requestedDelay > TimeSpan.Zero)
                {
                    await Task.Delay(requestedDelay).ConfigureAwait(false);
                }

                await RefreshMachineLslStateCoreAsync(reportOutcome: false).ConfigureAwait(false);
            }
            catch
            {
                // Best effort only. Explicit refreshes surface actionable failures.
            }
        });
    }

    private async Task RefreshMachineLslStateCoreAsync(bool reportOutcome)
    {
        if (!await _machineLslStateRefreshGate.WaitAsync(0).ConfigureAwait(false))
        {
            if (reportOutcome)
            {
                await ApplyOutcomeAsync(
                    "Refresh Machine LSL State",
                    new OperationOutcome(
                        OperationOutcomeKind.Preview,
                        "Machine LSL state is already refreshing.",
                        "Wait for the current Windows-side LSL inventory pass to finish before running it again.")).ConfigureAwait(false);
            }

            return;
        }

        try
        {
            var result = await BuildMachineLslStateResultAsync().ConfigureAwait(false);
            await DispatchAsync(() =>
            {
                ApplyMachineLslStateResult(result);
                RefreshBenchToolsStatus();
            }).ConfigureAwait(false);

            if (reportOutcome)
            {
                await ApplyOutcomeAsync(
                    "Refresh Machine LSL State",
                    new OperationOutcome(
                        result.Level,
                        result.Summary,
                        BuildMachineLslStateActionDetail(result))).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            await DispatchAsync(() =>
            {
                MachineLslStateHasRun = true;
                MachineLslStateLevel = OperationOutcomeKind.Failure;
                MachineLslStateSummary = "Machine LSL state refresh failed.";
                MachineLslStateDetail = exception.Message;
                MachineLslStateTimestampLabel = $"Failed {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
                ReplaceWorkflowGuideCheckItems(MachineLslStateChecks, []);
                RefreshBenchToolsStatus();
            }).ConfigureAwait(false);

            if (reportOutcome)
            {
                await ApplyOutcomeAsync(
                    "Refresh Machine LSL State",
                    new OperationOutcome(
                        OperationOutcomeKind.Failure,
                        "Machine LSL state refresh failed.",
                        exception.Message)).ConfigureAwait(false);
            }
        }
        finally
        {
            _machineLslStateRefreshGate.Release();
        }
    }

    private async Task<LslMachineStateResult> BuildMachineLslStateResultAsync()
    {
        var expectedStreamName = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : _study.Monitoring.ExpectedLslStreamName;
        var expectedStreamType = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : _study.Monitoring.ExpectedLslStreamType;
        var testSenderSourceId = BuildTestSenderSourceId();
        var lslBridge = _twinBridge as LslTwinModeBridge;
        var localTwinOutletsActive = lslBridge?.IsCommandOutletOpen == true;
        var passiveMonitorRunning = _upstreamLslMonitorTask is { IsCompleted: false };
        var backgroundClockMonitorRunning = _backgroundClockAlignmentTask is { IsCompleted: false };
        var clockTransportExpectedActive = _activeRecordingSession is not null || backgroundClockMonitorRunning;

        var runtimeState = _lslStreamDiscoveryService.RuntimeState;
        IReadOnlyList<LslVisibleStreamInfo> expectedStreams = [];
        IReadOnlyList<LslVisibleStreamInfo> twinCommandStreams = [];
        IReadOnlyList<LslVisibleStreamInfo> twinConfigStreams = [];
        IReadOnlyList<LslVisibleStreamInfo> clockProbeStreams = [];

        if (runtimeState.Available)
        {
            var expectedTask = Task.Run(
                () => _lslStreamDiscoveryService.Discover(new LslStreamDiscoveryRequest(expectedStreamName, expectedStreamType)),
                CancellationToken.None);
            var commandTask = Task.Run(
                () => _lslStreamDiscoveryService.Discover(new LslStreamDiscoveryRequest(TwinCommandStreamName, TwinCommandStreamType)),
                CancellationToken.None);
            var configTask = Task.Run(
                () => _lslStreamDiscoveryService.Discover(new LslStreamDiscoveryRequest(TwinConfigStreamName, TwinConfigStreamType)),
                CancellationToken.None);
            var clockTask = Task.Run(
                () => _lslStreamDiscoveryService.Discover(new LslStreamDiscoveryRequest(SussexClockAlignmentStreamContract.ProbeStreamName, SussexClockAlignmentStreamContract.ProbeStreamType)),
                CancellationToken.None);

            await Task.WhenAll(expectedTask, commandTask, configTask, clockTask).ConfigureAwait(false);
            expectedStreams = expectedTask.Result;
            twinCommandStreams = commandTask.Result;
            twinConfigStreams = configTask.Result;
            clockProbeStreams = clockTask.Result;
        }

        _lslExpectedUpstreamProbeState = BuildExpectedUpstreamProbeState(
            expectedStreamName,
            expectedStreamType,
            runtimeState,
            expectedStreams,
            testSenderSourceId);

        var companionExpectedStreams = expectedStreams
            .Where(stream => string.Equals(stream.SourceId, testSenderSourceId, StringComparison.Ordinal))
            .ToArray();

        var checks = new[]
        {
            BuildMachineLslRuntimeCheck(runtimeState),
            BuildExpectedUpstreamInventoryCheck(expectedStreamName, expectedStreamType, expectedStreams, testSenderSourceId),
            BuildTestSenderServiceCheck(expectedStreamName, expectedStreamType, companionExpectedStreams),
            BuildTwinOutletInventoryCheck(localTwinOutletsActive, twinCommandStreams, twinConfigStreams, _twinBridge.Status),
            BuildClockAlignmentTransportCheck(clockTransportExpectedActive, backgroundClockMonitorRunning, clockProbeStreams),
            BuildPassiveUpstreamMonitorCheck(passiveMonitorRunning)
        };

        var failureCount = checks.Count(check => check.Level == OperationOutcomeKind.Failure);
        var warningCount = checks.Count(check => check.Level == OperationOutcomeKind.Warning);
        var level = failureCount > 0
            ? OperationOutcomeKind.Failure
            : warningCount > 0
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success;

        var summary = level switch
        {
            OperationOutcomeKind.Failure => "Machine LSL state found blocking issues.",
            OperationOutcomeKind.Warning => "Machine LSL state needs attention.",
            _ => "Machine LSL state looks clean."
        };

        var detail = $"Checks warn/fail: {warningCount}/{failureCount}.";
        return new LslMachineStateResult(level, summary, detail, checks, DateTimeOffset.UtcNow);
    }

    private void ApplyMachineLslStateResult(LslMachineStateResult result)
    {
        MachineLslStateHasRun = true;
        MachineLslStateLevel = result.Level;
        MachineLslStateSummary = result.Summary;
        MachineLslStateDetail = BuildMachineLslSummaryDetail(result);
        MachineLslStateTimestampLabel = $"Last checked {result.CompletedAtUtc.ToLocalTime():HH:mm:ss}.";
        ReplaceWorkflowGuideCheckItems(
            MachineLslStateChecks,
            result.Checks
                .Select(check => new WorkflowGuideCheckItem(check.Label, check.Summary, check.Detail, check.Level))
                .ToArray());
    }

    private static string BuildMachineLslSummaryDetail(LslMachineStateResult result)
    {
        var attentionItems = result.Checks
            .Where(check => check.Level is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
            .Select(check => $"{check.Label}: {check.Summary}")
            .ToArray();

        if (attentionItems.Length == 0)
        {
            return $"{result.Detail} Companion-owned LSL services and the currently visible Windows-side streams agree.";
        }

        return $"{result.Detail} {string.Join(" ", attentionItems)}";
    }

    private static string BuildMachineLslStateActionDetail(LslMachineStateResult result)
        => string.Join(
            Environment.NewLine,
            new[] { result.Detail }
                .Concat(result.Checks.Select(check => $"{check.Label}: {check.Summary}")));

    private async Task<LslExpectedUpstreamProbeRefreshResult> RefreshExpectedUpstreamProbeStateAsync(
        bool autoStartCompanionTestSender,
        CancellationToken cancellationToken = default)
    {
        var expectedUpstream = InspectExpectedUpstreamOnWindows();
        var autoStartedCompanionTestSender = false;
        if (autoStartCompanionTestSender &&
            expectedUpstream.DiscoveryAvailable &&
            !expectedUpstream.ProbeFailed &&
            !expectedUpstream.VisibleOnWindows)
        {
            autoStartedCompanionTestSender = await TryStartCompanionTestSenderForProbeAsync(cancellationToken).ConfigureAwait(false);
            if (autoStartedCompanionTestSender)
            {
                await Task.Delay(MachineLslStateRefreshSettleDelay, cancellationToken).ConfigureAwait(false);
                expectedUpstream = InspectExpectedUpstreamOnWindows();
            }
        }

        _lslExpectedUpstreamProbeState = expectedUpstream;
        return new LslExpectedUpstreamProbeRefreshResult(expectedUpstream, autoStartedCompanionTestSender);
    }

    private SussexExpectedUpstreamState InspectExpectedUpstreamOnWindows()
    {
        var expectedStreamName = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : _study.Monitoring.ExpectedLslStreamName;
        var expectedStreamType = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : _study.Monitoring.ExpectedLslStreamType;
        return SussexConnectionAssessmentService.InspectExpectedUpstream(
            _lslStreamDiscoveryService,
            expectedStreamName,
            expectedStreamType,
            BuildTestSenderSourceId());
    }

    private SussexExpectedUpstreamState BuildExpectedUpstreamProbeState(
        string expectedStreamName,
        string expectedStreamType,
        LslRuntimeState runtimeState,
        IReadOnlyList<LslVisibleStreamInfo> expectedStreams,
        string companionTestSourceId)
    {
        if (!runtimeState.Available)
        {
            return new SussexExpectedUpstreamState(
                expectedStreamName,
                expectedStreamType,
                DiscoveryAvailable: false,
                ProbeFailed: false,
                VisibleOnWindows: false,
                VisibleViaCompanionTestSender: false,
                VisibleMatchCount: 0,
                VisibleMatches: [],
                Summary: $"{expectedStreamName} / {expectedStreamType} could not be probed on Windows.",
                Detail: runtimeState.Detail);
        }

        var visibleMatches = expectedStreams.ToArray();
        var visibleViaCompanionTestSender = visibleMatches.Any(stream =>
            string.Equals(stream.SourceId, companionTestSourceId, StringComparison.Ordinal));
        var summary = visibleMatches.Length switch
        {
            0 => $"{expectedStreamName} / {expectedStreamType} is not currently visible on Windows.",
            1 when visibleViaCompanionTestSender => $"{expectedStreamName} / {expectedStreamType} is visible on Windows via the companion TEST sender.",
            1 => $"{expectedStreamName} / {expectedStreamType} is visible on Windows.",
            _ when visibleViaCompanionTestSender => $"Multiple {expectedStreamName} / {expectedStreamType} sources are visible on Windows, including the companion TEST sender.",
            _ => $"Multiple {expectedStreamName} / {expectedStreamType} sources are visible on Windows."
        };
        var detail = visibleMatches.Length == 0
            ? "No matching visible streams."
            : FormatVisibleStreamInventory(visibleMatches);
        return new SussexExpectedUpstreamState(
            expectedStreamName,
            expectedStreamType,
            DiscoveryAvailable: true,
            ProbeFailed: false,
            VisibleOnWindows: visibleMatches.Length > 0,
            VisibleViaCompanionTestSender: visibleViaCompanionTestSender,
            VisibleMatchCount: visibleMatches.Length,
            VisibleMatches: visibleMatches,
            Summary: summary,
            Detail: detail);
    }

    private async Task<bool> TryStartCompanionTestSenderForProbeAsync(CancellationToken cancellationToken = default)
    {
        if (!_testLslSignalService.RuntimeState.Available || _testLslSignalService.IsRunning)
        {
            return false;
        }

        var routeOutcome = await ApplyTestSenderRoutingAsync().ConfigureAwait(false);
        if (routeOutcome.Kind == OperationOutcomeKind.Failure)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var streamName = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : _study.Monitoring.ExpectedLslStreamName;
        var streamType = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : _study.Monitoring.ExpectedLslStreamType;
        var startOutcome = _testLslSignalService.Start(streamName, streamType, BuildTestSenderSourceId());
        if (startOutcome.Kind == OperationOutcomeKind.Failure)
        {
            await RestoreTestSenderRoutingAsync().ConfigureAwait(false);
            return false;
        }

        await DispatchAsync(() =>
        {
            RefreshBenchToolsStatus();
            UpdateLslCard();
        }).ConfigureAwait(false);
        return true;
    }

    private LslMachineCheckResult BuildMachineLslRuntimeCheck(LslRuntimeState runtimeState)
        => runtimeState.Available
            ? new LslMachineCheckResult(
                "Windows liblsl discovery runtime",
                OperationOutcomeKind.Success,
                "Windows liblsl discovery runtime is ready.",
                runtimeState.Detail)
            : new LslMachineCheckResult(
                "Windows liblsl discovery runtime",
                OperationOutcomeKind.Failure,
                "Windows liblsl discovery runtime is unavailable.",
                runtimeState.Detail);

    private LslMachineCheckResult BuildExpectedUpstreamInventoryCheck(
        string expectedStreamName,
        string expectedStreamType,
        IReadOnlyList<LslVisibleStreamInfo> expectedStreams,
        string companionTestSourceId)
    {
        if (expectedStreams.Count == 0)
        {
            return new LslMachineCheckResult(
                "Expected upstream sources",
                OperationOutcomeKind.Warning,
                $"No {expectedStreamName} / {expectedStreamType} sources are visible on Windows.",
                $"Start the intended upstream sender or inspect the external publisher. The companion currently cannot see any source advertising {expectedStreamName} / {expectedStreamType} on this PC.");
        }

        var detail = $"Visible matches:{Environment.NewLine}{FormatVisibleStreamInventory(expectedStreams)}";
        if (expectedStreams.Count == 1)
        {
            return new LslMachineCheckResult(
                "Expected upstream sources",
                OperationOutcomeKind.Success,
                $"{expectedStreamName} / {expectedStreamType} is visible on Windows.",
                detail);
        }

        var includesCompanionTestSender = expectedStreams.Any(stream => string.Equals(stream.SourceId, companionTestSourceId, StringComparison.Ordinal));
        var summary = includesCompanionTestSender
            ? $"Multiple {expectedStreamName} / {expectedStreamType} sources are visible, including the companion TEST sender."
            : $"Multiple {expectedStreamName} / {expectedStreamType} sources are visible on Windows.";
        var warningDetail = includesCompanionTestSender
            ? $"{detail}{Environment.NewLine}This can make switching between the companion TEST sender and external sources unreliable because more than one source is advertising the same expected stream contract."
            : $"{detail}{Environment.NewLine}More than one upstream source is advertising the expected stream contract on Windows.";
        return new LslMachineCheckResult(
            "Expected upstream sources",
            OperationOutcomeKind.Warning,
            summary,
            warningDetail);
    }

    private LslMachineCheckResult BuildTestSenderServiceCheck(
        string expectedStreamName,
        string expectedStreamType,
        IReadOnlyList<LslVisibleStreamInfo> companionExpectedStreams)
    {
        if (!_testLslSignalService.RuntimeState.Available)
        {
            return new LslMachineCheckResult(
                "Companion TEST sender",
                OperationOutcomeKind.Preview,
                "Companion TEST sender unavailable.",
                _testLslSignalService.RuntimeState.Detail);
        }

        if (!string.IsNullOrWhiteSpace(_testLslSignalService.LastFaultDetail))
        {
            return new LslMachineCheckResult(
                "Companion TEST sender",
                OperationOutcomeKind.Failure,
                "Companion TEST sender stopped after a local fault.",
                _testLslSignalService.LastFaultDetail);
        }

        if (_testLslSignalService.IsRunning)
        {
            return companionExpectedStreams.Count switch
            {
                0 => new LslMachineCheckResult(
                    "Companion TEST sender",
                    OperationOutcomeKind.Warning,
                    "Companion TEST sender says it is running, but its source is not visible yet.",
                    $"The local sender is active, but no visible source matched `{BuildTestSenderSourceId()}` for {expectedStreamName} / {expectedStreamType}. If this does not clear after a refresh, the local sender may not be advertising cleanly."),
                1 => new LslMachineCheckResult(
                    "Companion TEST sender",
                    OperationOutcomeKind.Success,
                    "Companion TEST sender is running and visible.",
                    FormatVisibleStreamInventory(companionExpectedStreams)),
                _ => new LslMachineCheckResult(
                    "Companion TEST sender",
                    OperationOutcomeKind.Warning,
                    "Companion TEST sender source appears more than once.",
                    FormatVisibleStreamInventory(companionExpectedStreams))
            };
        }

        if (companionExpectedStreams.Count > 0)
        {
            return new LslMachineCheckResult(
                "Companion TEST sender",
                OperationOutcomeKind.Warning,
                "Companion TEST sender is off, but its source is still visible on Windows.",
                $"{FormatVisibleStreamInventory(companionExpectedStreams)}{Environment.NewLine}If this persists after another refresh, the sender may not have shut down cleanly or another companion instance is still advertising the same source id.");
        }

        return new LslMachineCheckResult(
            "Companion TEST sender",
            OperationOutcomeKind.Preview,
            "Companion TEST sender idle.",
            $"The companion is not currently publishing {expectedStreamName} / {expectedStreamType}.");
    }

    private static LslMachineCheckResult BuildTwinOutletInventoryCheck(
        bool localTwinOutletsActive,
        IReadOnlyList<LslVisibleStreamInfo> twinCommandStreams,
        IReadOnlyList<LslVisibleStreamInfo> twinConfigStreams,
        TwinBridgeStatus twinBridgeStatus)
    {
        var detail =
            $"Local twin bridge: {twinBridgeStatus.Summary}{Environment.NewLine}" +
            $"Command stream matches ({TwinCommandStreamName} / {TwinCommandStreamType}): {twinCommandStreams.Count}{Environment.NewLine}" +
            $"{FormatVisibleStreamInventory(twinCommandStreams)}{Environment.NewLine}" +
            $"Config stream matches ({TwinConfigStreamName} / {TwinConfigStreamType}): {twinConfigStreams.Count}{Environment.NewLine}" +
            $"{FormatVisibleStreamInventory(twinConfigStreams)}";

        if (localTwinOutletsActive)
        {
            if (twinCommandStreams.Count == 1 && twinConfigStreams.Count == 1)
            {
                return new LslMachineCheckResult(
                    "Companion twin outlets",
                    OperationOutcomeKind.Success,
                    "Companion twin outlets are active and visible.",
                    detail);
            }

            return new LslMachineCheckResult(
                "Companion twin outlets",
                OperationOutcomeKind.Warning,
                "Companion twin outlets are active, but the visible Windows inventory is unexpected.",
                detail);
        }

        if (twinCommandStreams.Count > 0 || twinConfigStreams.Count > 0)
        {
            return new LslMachineCheckResult(
                "Companion twin outlets",
                OperationOutcomeKind.Warning,
                "Twin outlet streams are still visible while the local bridge is idle.",
                detail);
        }

        return new LslMachineCheckResult(
            "Companion twin outlets",
            OperationOutcomeKind.Preview,
            "Companion twin outlets idle.",
            twinBridgeStatus.Detail);
    }

    private static LslMachineCheckResult BuildClockAlignmentTransportCheck(
        bool clockTransportExpectedActive,
        bool backgroundClockMonitorRunning,
        IReadOnlyList<LslVisibleStreamInfo> clockProbeStreams)
    {
        var stateDetail =
            $"Warm transport expected active: {(clockTransportExpectedActive ? "yes" : "no")}.{Environment.NewLine}" +
            $"Background sparse monitor active: {(backgroundClockMonitorRunning ? "yes" : "no")}.{Environment.NewLine}" +
            $"Probe stream matches ({SussexClockAlignmentStreamContract.ProbeStreamName} / {SussexClockAlignmentStreamContract.ProbeStreamType}): {clockProbeStreams.Count}{Environment.NewLine}" +
            FormatVisibleStreamInventory(clockProbeStreams);

        if (clockTransportExpectedActive)
        {
            return clockProbeStreams.Count == 1
                ? new LslMachineCheckResult(
                    "Clock-alignment transport",
                    OperationOutcomeKind.Success,
                    "Clock-alignment probe transport is active and visible.",
                    stateDetail)
                : new LslMachineCheckResult(
                    "Clock-alignment transport",
                    OperationOutcomeKind.Warning,
                    "Clock-alignment transport is expected active, but the visible probe inventory is unexpected.",
                    stateDetail);
        }

        if (clockProbeStreams.Count > 0)
        {
            return new LslMachineCheckResult(
                "Clock-alignment transport",
                OperationOutcomeKind.Warning,
                "Clock-alignment probe stream is visible while no local timing run is active.",
                stateDetail);
        }

        return new LslMachineCheckResult(
            "Clock-alignment transport",
            OperationOutcomeKind.Preview,
            "Clock-alignment transport idle.",
            stateDetail);
    }

    private static LslMachineCheckResult BuildPassiveUpstreamMonitorCheck(bool passiveMonitorRunning)
        => passiveMonitorRunning
            ? new LslMachineCheckResult(
                "Passive upstream monitor",
                OperationOutcomeKind.Success,
                "Passive upstream monitor is running.",
                $"The Windows-side passive inlet monitor for {HrvBiofeedbackStreamContract.StreamName} / {HrvBiofeedbackStreamContract.StreamType} is currently armed for the active participant session.")
            : new LslMachineCheckResult(
                "Passive upstream monitor",
                OperationOutcomeKind.Preview,
                "Passive upstream monitor idle.",
                $"No participant-session upstream inlet monitor is running for {HrvBiofeedbackStreamContract.StreamName} / {HrvBiofeedbackStreamContract.StreamType}.");

    private static string FormatVisibleStreamInventory(IReadOnlyList<LslVisibleStreamInfo> streams)
        => streams.Count == 0
            ? "No matching visible streams."
            : string.Join(
                Environment.NewLine,
                streams.Select(static stream =>
                    $"{stream.Name} / {stream.Type} | source_id `{(string.IsNullOrWhiteSpace(stream.SourceId) ? "n/a" : stream.SourceId)}` | channels {stream.ChannelCount.ToString(CultureInfo.InvariantCulture)} | nominal {stream.SampleRateHz.ToString("0.###", CultureInfo.InvariantCulture)} Hz"));

    private string BuildTestSenderSourceId()
        => $"viscereality.companion.study-shell.test.{_study.Id}";

    private sealed record LslMachineCheckResult(
        string Label,
        OperationOutcomeKind Level,
        string Summary,
        string Detail);

    private sealed record LslMachineStateResult(
        OperationOutcomeKind Level,
        string Summary,
        string Detail,
        IReadOnlyList<LslMachineCheckResult> Checks,
        DateTimeOffset CompletedAtUtc);

    private sealed record LslExpectedUpstreamProbeRefreshResult(
        SussexExpectedUpstreamState State,
        bool AutoStartedCompanionTestSender);

    private async Task BrowseApkAsync()
    {
        var selectedPath = await DispatchAsync(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = $"Select {_study.Label} APK",
                Filter = "Android Packages (*.apk)|*.apk|All files (*.*)|*.*",
                FileName = Path.GetFileName(StagedApkPath)
            };

            return dialog.ShowDialog() == true ? dialog.FileName : string.Empty;
        }).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        await DispatchAsync(() =>
        {
            StagedApkPath = Path.GetFullPath(selectedPath);
            SaveStudySession(StagedApkPath);
        }).ConfigureAwait(false);

        await RefreshLocalApkStatusAsync().ConfigureAwait(false);
        await DispatchAsync(UpdatePinnedBuildStatus).ConfigureAwait(false);
    }

    public async Task InstallStudyAppAsync()
    {
        var localPath = await DispatchAsync(() => StagedApkPath).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            await DispatchAsync(() => AppendLog(
                OperatorLogLevel.Warning,
                "Study install blocked.",
                CanChooseStudyApk
                    ? "Study APK not available locally. Keep the bundled Sussex APK with the app or choose the approved Sussex APK before installing."
                    : "Bundled Sussex APK not available locally. Restore the shipped Sussex APK before installing.")).ConfigureAwait(false);
            return;
        }

        if (!HashMatches(LocalApkHash, _study.App.Sha256))
        {
            await DispatchAsync(() => AppendLog(
                OperatorLogLevel.Failure,
                "Study install blocked.",
                CanChooseStudyApk
                    ? "The selected study APK does not match the Sussex study hash. Choose the approved Sussex APK before installing."
                    : "The bundled Sussex APK does not match the Sussex study hash. Replace it with the approved Sussex APK before installing.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _questService.InstallAppAsync(CreateStudyTarget(localPath)).ConfigureAwait(false);
        await ApplyOutcomeAsync("Install Sussex APK", outcome).ConfigureAwait(false);
        await RefreshInstalledAppStatusAsync().ConfigureAwait(false);
        await RefreshStatusAsync().ConfigureAwait(false);
    }

    public async Task LaunchStudyAppAsync()
    {
        var target = CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false));
        var startupHotloadSync = await EnsurePinnedStartupHotloadStateAsync(
            target,
            forceWhenStudyNotForeground: true,
            reasonLabel: "before launch").ConfigureAwait(false);

        var launchTwinSnapshotGate = await DispatchAsync(CaptureTwinSnapshotGate).ConfigureAwait(false);
        var launchIssuedAtUtc = DateTimeOffset.UtcNow;
        var outcome = await _questService
            .LaunchAppAsync(
                target,
                kioskMode: _study.App.LaunchInKioskMode)
            .ConfigureAwait(false);

        await ApplyOutcomeAsync(
            "Launch Sussex Runtime",
            outcome).ConfigureAwait(false);
        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            await DispatchAsync(() =>
            {
                var selector = ResolveHeadsetActionSelector();
                if (!string.IsNullOrWhiteSpace(selector))
                {
                    _appSessionState = _appSessionState.WithTrackedProximity(selector, expectedEnabled: false, disableUntilUtc: null);
                    _appSessionState.Save();
                    RefreshBenchToolsStatus();
                }
            }).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        await RefreshStatusAsync().ConfigureAwait(false);
        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            await RefreshProximityStatusAsync(force: true).ConfigureAwait(false);
        }

        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            await DispatchAsync(ClearQuestVisualConfirmationPending).ConfigureAwait(false);
            var runtimeForeground = await WaitForStudyRuntimeForegroundAsync().ConfigureAwait(false);
            if (runtimeForeground)
            {
                var performanceOutcome = await TryStabilizeStudyPerformancePolicyAsync().ConfigureAwait(false);
                if (performanceOutcome is not null)
                {
                    await DispatchAsync(() => AppendLog(MapLevel(performanceOutcome.Kind), performanceOutcome.Summary, performanceOutcome.Detail)).ConfigureAwait(false);
                    await RefreshDeviceProfileStatusAsync().ConfigureAwait(false);
                    await RefreshHeadsetStatusAsync().ConfigureAwait(false);
                }

                var hasPinnedVisualStartupProfile = await DispatchAsync(() => _visualProfiles.HasPinnedStartupProfile).ConfigureAwait(false);
                if (hasPinnedVisualStartupProfile)
                {
                    var runtimeConfigBaselineReady = await WaitForFreshRuntimeConfigBaselineAsync(
                        launchTwinSnapshotGate,
                        launchIssuedAtUtc).ConfigureAwait(false);
                    if (!runtimeConfigBaselineReady)
                    {
                        await DispatchAsync(() =>
                            AppendLog(
                                OperatorLogLevel.Warning,
                                "Startup visual baseline did not report readiness in time.",
                                "Sussex did not publish a fresh showcase_active_runtime_config_json snapshot within 8 seconds of launch, so the pinned startup visual profile is staged on device without that readiness confirmation yet. If shape or size values still mismatch, wait for live twin state and launch again or reapply the runtime draft manually.")).ConfigureAwait(false);
                    }
                }

                if (startupHotloadSync.AppliedToDevice)
                {
                    var startupAppliedAtUtc = DateTimeOffset.UtcNow;
                    await DispatchAsync(() =>
                    {
                        // Startup plans track only the saved launch profiles staged for the next
                        // Sussex boot. Runtime draft applies are confirmed separately and do not
                        // rewrite this launch-profile history.
                        if (startupHotloadSync.VisualPlan is not null)
                        {
                            _visualProfiles.TrackPinnedStartupLaunch(startupHotloadSync.VisualPlan, startupAppliedAtUtc, startupHotloadSync.CsvPath);
                        }

                        if (startupHotloadSync.ControllerPlan is not null)
                        {
                            _controllerBreathingProfiles.TrackPinnedStartupLaunch(startupHotloadSync.ControllerPlan, startupAppliedAtUtc, startupHotloadSync.CsvPath);
                        }
                    }).ConfigureAwait(false);
                }
            }

            await DispatchAsync(() =>
            {
                if (!string.Equals(_headsetStatus?.ForegroundPackageId, _study.App.PackageId, StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog(
                        OperatorLogLevel.Warning,
                        "Launch command sent, but Sussex is not active.",
                        $"Active app is {_headsetStatus?.ForegroundPackageId ?? "unknown"}. The headset may be in Guardian, lockscreen, or the Meta shell instead of the study runtime.");
                }
            }).ConfigureAwait(false);
        }
    }

    public async Task StopStudyAppAsync()
    {
        var target = CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false));
        var outcome = await _questService
            .StopAppAsync(
                target,
                exitKioskMode: _study.App.LaunchInKioskMode)
            .ConfigureAwait(false);

        await ApplyOutcomeAsync(
            "Stop Sussex Runtime",
            outcome).ConfigureAwait(false);

        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            await EnsurePinnedStartupHotloadStateAsync(
                target,
                forceWhenStudyNotForeground: true,
                reasonLabel: "after stop").ConfigureAwait(false);
        }

        await RefreshStatusAsync().ConfigureAwait(false);
    }

    private void OnStartupProfileChanged(object? sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var target = CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false));
                var runtimeForeground = await DispatchAsync(IsStudyRuntimeForeground).ConfigureAwait(false);
                if (runtimeForeground)
                {
                    _startupHotloadSyncDeferredUntilStudyStops = true;
                    await DispatchAsync(() =>
                        AppendLog(
                            OperatorLogLevel.Info,
                            "Saved launch profile update queued.",
                            "The selected launch profile was saved locally, but the device-side launch file will wait until Sussex stops so the current session keeps running unchanged.")).ConfigureAwait(false);
                    return;
                }

                await EnsurePinnedStartupHotloadStateAsync(
                    target,
                    forceWhenStudyNotForeground: true,
                    reasonLabel: "after launch-profile change").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await DispatchAsync(() =>
                    AppendLog(
                        OperatorLogLevel.Warning,
                        "Saved launch profile sync failed.",
                        ex.Message)).ConfigureAwait(false);
            }
        });
    }

    private void OnSessionParameterActivity(object? sender, SussexSessionParameterActivity activity)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await DispatchAsync(() =>
                {
                    var recordingSession = _activeRecordingSession;
                    if (recordingSession is null)
                    {
                        return;
                    }

                    recordingSession.RecordEvent(
                        $"tuning.{activity.Surface}.{activity.Kind}",
                        BuildSessionParameterActivityDetail(activity),
                        null,
                        "recorded",
                        activity.RecordedAtUtc);
                    recordingSession.UpdateSessionContext(
                        BuildSessionParameterStateNode(activity.RecordedAtUtc),
                        BuildSessionConditionsJson(activity.RecordedAtUtc),
                        activity.RecordedAtUtc,
                        incrementParameterChangeCount: true);
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await DispatchAsync(() =>
                    AppendLog(
                        OperatorLogLevel.Warning,
                        "Session tuning metadata capture failed.",
                        exception.Message)).ConfigureAwait(false);
            }
        });
    }

    private async Task<bool> WaitForStudyRuntimeForegroundAsync()
    {
        var timeoutAtUtc = DateTimeOffset.UtcNow + StartupRuntimeConfigBaselineTimeout;
        while (DateTimeOffset.UtcNow < timeoutAtUtc)
        {
            await RefreshHeadsetStatusAsync(includeHostWifiStatus: false).ConfigureAwait(false);
            if (await DispatchAsync(IsStudyRuntimeForeground).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(StartupRuntimeConfigBaselinePollInterval).ConfigureAwait(false);
        }

        return await DispatchAsync(IsStudyRuntimeForeground).ConfigureAwait(false);
    }

    private async Task<PinnedStartupHotloadSyncResult> EnsurePinnedStartupHotloadStateAsync(
        QuestAppTarget target,
        bool forceWhenStudyNotForeground,
        string reasonLabel)
    {
        await _startupHotloadSyncSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            SussexVisualStartupHotloadPlan? visualPlan = null;
            SussexControllerBreathingStartupHotloadPlan? controllerPlan = null;
            await DispatchAsync(() =>
            {
                visualPlan = _visualProfiles.CapturePinnedStartupHotloadPlan();
                controllerPlan = _controllerBreathingProfiles.CapturePinnedStartupHotloadPlan();
            }).ConfigureAwait(false);

            if (visualPlan is null && controllerPlan is null)
            {
                var clearOutcome = await _questService.ClearHotloadOverrideAsync(target).ConfigureAwait(false);
                _startupHotloadSyncDeferredUntilStudyStops = false;
                if (clearOutcome.Kind is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
                {
                    await DispatchAsync(() =>
                        AppendLog(
                            MapLevel(clearOutcome.Kind),
                            clearOutcome.Summary,
                            clearOutcome.Detail)).ConfigureAwait(false);
                }

                return new PinnedStartupHotloadSyncResult(
                    CsvPath: null,
                    VisualPlan: null,
                    ControllerPlan: null,
                    AppliedToDevice: clearOutcome.Kind != OperationOutcomeKind.Failure);
            }

            if (!forceWhenStudyNotForeground)
            {
                var runtimeForeground = await DispatchAsync(IsStudyRuntimeForeground).ConfigureAwait(false);
                if (runtimeForeground)
                {
                    _startupHotloadSyncDeferredUntilStudyStops = true;
                    return new PinnedStartupHotloadSyncResult(
                        CsvPath: null,
                        VisualPlan: visualPlan,
                        ControllerPlan: controllerPlan,
                        AppliedToDevice: false);
                }
            }

            var mergedEntries = MergeRuntimeConfigEntries(
                visualPlan?.Entries,
                controllerPlan?.Entries);
            var runtimeProfile = new RuntimeConfigProfile(
                $"sussex_pinned_startup_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}",
                "Sussex Pinned Startup Profile",
                string.Empty,
                DateTimeOffset.UtcNow.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture),
                "study",
                false,
                "Pinned Sussex launch profile payload staged for the next runtime start.",
                [target.PackageId],
                mergedEntries);
            var csvPath = await _runtimeConfigWriter.WriteAsync(runtimeProfile).ConfigureAwait(false);
            var hotloadProfile = new HotloadProfile(
                runtimeProfile.Id,
                runtimeProfile.Label,
                csvPath,
                runtimeProfile.Version,
                runtimeProfile.Channel,
                runtimeProfile.StudyLock,
                runtimeProfile.Description,
                runtimeProfile.PackageIds);

            var outcome = await _questService.ApplyHotloadProfileAsync(hotloadProfile, target).ConfigureAwait(false);
            if (outcome.Kind is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
            {
                _startupHotloadSyncDeferredUntilStudyStops = true;
                await DispatchAsync(() =>
                    AppendLog(
                        MapLevel(outcome.Kind),
                        outcome.Summary,
                        outcome.Detail)).ConfigureAwait(false);
                return new PinnedStartupHotloadSyncResult(csvPath, visualPlan, controllerPlan, AppliedToDevice: false);
            }

            _startupHotloadSyncDeferredUntilStudyStops = false;
            if (!string.Equals(reasonLabel, "before launch", StringComparison.OrdinalIgnoreCase))
            {
                await DispatchAsync(() =>
                    AppendLog(
                        OperatorLogLevel.Info,
                        "Saved launch profile synced to headset.",
                        $"Updated the persistent Sussex launch profile file on device from the current pinned startup profiles ({reasonLabel}).")).ConfigureAwait(false);
            }

            return new PinnedStartupHotloadSyncResult(csvPath, visualPlan, controllerPlan, AppliedToDevice: true);
        }
        finally
        {
            _startupHotloadSyncSemaphore.Release();
        }
    }

    private static IReadOnlyList<RuntimeConfigEntry> MergeRuntimeConfigEntries(
        IReadOnlyList<RuntimeConfigEntry>? visualEntries,
        IReadOnlyList<RuntimeConfigEntry>? controllerEntries)
    {
        var merged = new List<RuntimeConfigEntry>();
        var indexesByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Append(IEnumerable<RuntimeConfigEntry>? entries)
        {
            if (entries is null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                if (indexesByKey.TryGetValue(entry.Key, out var index))
                {
                    merged[index] = entry;
                    continue;
                }

                indexesByKey[entry.Key] = merged.Count;
                merged.Add(entry);
            }
        }

        Append(visualEntries);
        Append(controllerEntries);
        return merged;
    }

    public async Task ToggleStudyRuntimeAsync()
    {
        if (IsStudyRuntimeForeground())
        {
            await StopStudyAppAsync().ConfigureAwait(false);
            return;
        }

        await LaunchStudyAppAsync().ConfigureAwait(false);
    }

    public async Task ApplyPinnedDeviceProfileAsync()
    {
        var outcome = await TryStabilizeStudyPerformancePolicyAsync().ConfigureAwait(false)
            ?? await _questService.ApplyDeviceProfileAsync(CreatePinnedDeviceProfile()).ConfigureAwait(false);
        await ApplyOutcomeAsync("Apply Study Device Profile", outcome).ConfigureAwait(false);
        await RefreshDeviceProfileStatusAsync().ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
    }

    private async Task ToggleProximityAsync()
    {
        await TryWakeHeadsetBeforeStudyActionAsync("Toggle proximity hold").ConfigureAwait(false);
        var selector = await DispatchAsync(ResolveHeadsetActionSelector).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(selector))
        {
            await DispatchAsync(() => AppendLog(
                OperatorLogLevel.Warning,
                "Proximity hold blocked.",
                "Probe USB or connect the Quest first so the shell has a live Quest selector for the proximity broadcast.")).ConfigureAwait(false);
            return;
        }

        var liveStatus = await RefreshProximityStatusAsync(force: true).ConfigureAwait(false);
        var tracked = await DispatchAsync(() => _appSessionState.GetTrackedProximity(selector)).ConfigureAwait(false);
        var disableHoldIsActive = IsProximityBypassExpected(tracked, liveStatus);
        var enableNormalProximity = disableHoldIsActive;
        var actionLabel = enableNormalProximity ? "Enable Proximity" : "Disable Proximity";
        OperationOutcome outcome;
        if (_hzdbService.IsAvailable)
        {
            outcome = await _hzdbService
                .SetProximityAsync(selector, enableNormalProximity)
                .ConfigureAwait(false);

            if (outcome.Kind == OperationOutcomeKind.Failure)
            {
                var fallbackOutcome = await _questService
                    .RunUtilityAsync(
                        enableNormalProximity ? QuestUtilityAction.EnableProximity : QuestUtilityAction.DisableProximity,
                        allowWakeResumeTarget: false)
                    .ConfigureAwait(false);
                outcome = fallbackOutcome.Kind == OperationOutcomeKind.Success
                    ? fallbackOutcome with
                    {
                        Detail = string.IsNullOrWhiteSpace(outcome.Detail)
                            ? fallbackOutcome.Detail
                            : $"{outcome.Detail} Fell back to the direct Quest broadcast path and it succeeded."
                    }
                    : fallbackOutcome with
                    {
                        Detail = string.IsNullOrWhiteSpace(outcome.Detail)
                            ? fallbackOutcome.Detail
                            : $"{outcome.Detail} Fallback via the direct Quest broadcast path also failed. {fallbackOutcome.Detail}".Trim()
                    };
            }
        }
        else
        {
            outcome = await _questService
                .RunUtilityAsync(
                    enableNormalProximity ? QuestUtilityAction.EnableProximity : QuestUtilityAction.DisableProximity,
                    allowWakeResumeTarget: false)
                .ConfigureAwait(false);
        }

        if (outcome.Kind == OperationOutcomeKind.Success)
        {
            await DispatchAsync(() =>
            {
                _appSessionState = _appSessionState.WithTrackedProximity(selector, expectedEnabled: enableNormalProximity, disableUntilUtc: null);
                _appSessionState.Save();
                RefreshBenchToolsStatus();
            }).ConfigureAwait(false);
        }

        await ApplyOutcomeAsync(actionLabel, outcome).ConfigureAwait(false);
        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            await Task.Delay(250).ConfigureAwait(false);
            await RefreshProximityStatusAsync(force: true).ConfigureAwait(false);
        }

        await DispatchAsync(RefreshBenchToolsStatus).ConfigureAwait(false);
    }

    private async Task CaptureQuestScreenshotAsync()
    {
        await CaptureQuestScreenshotForVerificationAsync(
                wakeBeforeCapture: true,
                captureTimeout: TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);
    }

    public async Task<OperationOutcome> CaptureQuestScreenshotForVerificationAsync(bool wakeBeforeCapture, TimeSpan? captureTimeout = null)
    {
        if (wakeBeforeCapture)
        {
            await TryWakeHeadsetBeforeStudyActionAsync("Capture Quest Screenshot").ConfigureAwait(false);
        }

        var selectors = await DispatchAsync(() => ResolveQuestScreenshotSelectorCandidates().ToArray()).ConfigureAwait(false);
        if (!_hzdbService.IsAvailable)
        {
            var unavailableOutcome = new OperationOutcome(
                OperationOutcomeKind.Preview,
                "hzdb not available.",
                "Run guided setup or install the official Quest tooling cache before using Quest screenshot capture.");
            await ApplyOutcomeAsync(
                "Capture Quest Screenshot",
                unavailableOutcome).ConfigureAwait(false);
            return unavailableOutcome;
        }

        if (selectors.Length == 0)
        {
            var blockedOutcome = new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Quest screenshot blocked.",
                "Probe USB or connect the Quest first so the study shell has a selector for hzdb screenshot capture.");
            await ApplyOutcomeAsync(
                "Capture Quest Screenshot",
                blockedOutcome).ConfigureAwait(false);
            return blockedOutcome;
        }

        var previousScreenshotHash = await DispatchAsync(() => ComputeQuestScreenshotHash(QuestScreenshotPath)).ConfigureAwait(false);
        var captureMethods = await DispatchAsync(() => ResolveQuestScreenshotCaptureMethods().ToArray()).ConfigureAwait(false);
        OperationOutcome outcome = new(
            OperationOutcomeKind.Warning,
            "Quest screenshot blocked.",
            "No responsive screenshot selector was available.");
        string acceptedPath = string.Empty;
        string acceptedSelector = string.Empty;
        string acceptedMethod = string.Empty;
        string stalePath = string.Empty;
        string staleSelector = string.Empty;
        string staleMethod = string.Empty;
        var attemptedPaths = new List<string>();

        foreach (var selector in selectors)
        {
            foreach (var method in captureMethods)
            {
                var outputPath = BuildQuestScreenshotOutputPath();
                attemptedPaths.Add(outputPath);

                try
                {
                    using var captureTimeoutCts = captureTimeout.HasValue
                        ? new CancellationTokenSource(captureTimeout.Value)
                        : null;
                    outcome = await _hzdbService
                        .CaptureScreenshotAsync(
                            selector,
                            outputPath,
                            method,
                            captureTimeoutCts?.Token ?? CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    TryDeleteFile(outputPath);
                    outcome = new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Quest screenshot timed out.",
                        $"{method} capture on {DescribeSelectorTransport(selector)} did not complete within {(captureTimeout ?? TimeSpan.Zero).TotalSeconds:0.#} seconds.");
                }

                if (outcome.Kind != OperationOutcomeKind.Success || !File.Exists(outputPath))
                {
                    continue;
                }

                var captureHash = ComputeQuestScreenshotHash(outputPath);
                if (!string.IsNullOrWhiteSpace(previousScreenshotHash) &&
                    string.Equals(captureHash, previousScreenshotHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(stalePath))
                    {
                        stalePath = outputPath;
                        staleSelector = selector;
                        staleMethod = method;
                    }

                    continue;
                }

                acceptedPath = outputPath;
                acceptedSelector = selector;
                acceptedMethod = method;
                break;
            }

            if (!string.IsNullOrWhiteSpace(acceptedPath))
            {
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(acceptedPath) && !string.IsNullOrWhiteSpace(stalePath))
        {
            acceptedPath = stalePath;
            acceptedSelector = staleSelector;
            acceptedMethod = staleMethod;
            outcome = new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Quest screenshot may be stale.",
                $"The new capture matched the previous screenshot even after retrying {string.Join(" then ", captureMethods)} on {DescribeSelectorTransport(acceptedSelector)}. Review the preview carefully because the headset image may not have advanced.",
                Items: [acceptedPath]);
        }

        if (!string.IsNullOrWhiteSpace(acceptedPath) && File.Exists(acceptedPath))
        {
            var capturedAtUtc = DateTimeOffset.UtcNow;
            var preview = LoadQuestScreenshotPreview(acceptedPath);
            await DispatchAsync(() =>
            {
                QuestScreenshotPath = acceptedPath;
                QuestScreenshotPreview = preview;
                _lastQuestScreenshotCapturedAtUtc = capturedAtUtc;
                ClearQuestVisualConfirmationPending();
                RefreshBenchToolsStatus();
                UpdateLiveRuntimeCard();
            }).ConfigureAwait(false);

            await TryArchiveQuestScreenshotAsync(acceptedPath, capturedAtUtc).ConfigureAwait(false);

            if (outcome.Kind != OperationOutcomeKind.Warning)
            {
                outcome = new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Quest screenshot captured.",
                    $"Saved {acceptedMethod} Quest screenshot to {acceptedPath} using {DescribeSelectorTransport(acceptedSelector)}. Review the screenshot preview to confirm what is actually visible on the headset.",
                    Items: [acceptedPath]);
            }
        }

        foreach (var attemptedPath in attemptedPaths)
        {
            if (string.Equals(attemptedPath, acceptedPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteFile(attemptedPath);
        }

        await ApplyOutcomeAsync("Capture Quest Screenshot", outcome).ConfigureAwait(false);
        return outcome;
    }

    private async Task OpenLastQuestScreenshotAsync()
    {
        var screenshotPath = await DispatchAsync(() => QuestScreenshotPath).ConfigureAwait(false);
        if (!CompanionOperatorDataLayout.TryResolveExistingFile(screenshotPath, out var resolvedScreenshotPath))
        {
            await ApplyOutcomeAsync(
                "Open Quest Screenshot",
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Quest screenshot not available.",
                    "Capture a Quest screenshot first before trying to open it.")).ConfigureAwait(false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = resolvedScreenshotPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ApplyOutcomeAsync(
                "Open Quest Screenshot",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Quest screenshot could not be opened.",
                    ex.Message,
                    Items: [resolvedScreenshotPath])).ConfigureAwait(false);
        }
    }

    private async Task OpenLocalAgentWorkspaceAsync()
    {
        LocalAgentWorkspaceSnapshot snapshot;
        try
        {
            snapshot = await Task.Run(() => _localAgentWorkspaceService.EnsureWorkspace()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ApplyOutcomeAsync(
                "Open Local Agent Workspace",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Local agent workspace could not be prepared.",
                    ex.Message)).ConfigureAwait(false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = snapshot.RootPath,
                UseShellExecute = true
            });

            await ApplyOutcomeAsync(
                "Open Local Agent Workspace",
                new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Local agent workspace ready.",
                    $"Open your local agent in {snapshot.RootPath}. This workspace mirrors the bundled CLI, CLI docs, Sussex study-shell manifests, device and hotload profiles, tuning templates, and a ready-made agent prompt outside WindowsApps.",
                    Items: [snapshot.RootPath])).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ApplyOutcomeAsync(
                "Open Local Agent Workspace",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Local agent workspace could not be opened.",
                    ex.Message,
                    Items: [snapshot.RootPath])).ConfigureAwait(false);
        }
    }

    private async Task CopyLocalAgentPromptAsync()
    {
        LocalAgentWorkspaceSnapshot snapshot;
        try
        {
            snapshot = await Task.Run(() => _localAgentWorkspaceService.EnsureWorkspace()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ApplyOutcomeAsync(
                "Copy Local Agent Prompt",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Local agent prompt could not be prepared.",
                    ex.Message)).ConfigureAwait(false);
            return;
        }

        try
        {
            await DispatchAsync(() => Clipboard.SetText(snapshot.PromptText)).ConfigureAwait(false);
            await ApplyOutcomeAsync(
                "Copy Local Agent Prompt",
                new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Local agent prompt copied.",
                    $"Copied a prompt that tells a local agent to inspect {snapshot.RootPath}, use the bundled CLI wrappers, read the mirrored Sussex docs and examples, and explain what the CLI can control today.",
                    Items: [snapshot.PromptPath])).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ApplyOutcomeAsync(
                "Copy Local Agent Prompt",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Local agent prompt could not be copied.",
                    ex.Message,
                    Items: [snapshot.PromptPath])).ConfigureAwait(false);
        }
    }

    private async Task WarmLocalAgentWorkspaceAsync()
    {
        try
        {
            await Task.Run(() => _localAgentWorkspaceService.EnsureWorkspace()).ConfigureAwait(false);
        }
        catch
        {
            // Best effort only. The explicit workspace buttons still surface any failure details.
        }
    }

    private async Task ToggleTestLslSenderAsync()
    {
        await TryWakeHeadsetBeforeStudyActionAsync("Toggle TEST sender route").ConfigureAwait(false);
        var isRunning = _testLslSignalService.IsRunning;
        var actionLabel = isRunning ? "Stop TEST Sender" : "Start TEST Sender";
        var streamName = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : _study.Monitoring.ExpectedLslStreamName;
        var streamType = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : _study.Monitoring.ExpectedLslStreamType;
        var sourceId = BuildTestSenderSourceId();

        OperationOutcome outcome;
        if (isRunning)
        {
            var stopOutcome = _testLslSignalService.Stop();
            var restoreOutcome = await RestoreTestSenderRoutingAsync().ConfigureAwait(false);
            outcome = CombineTestSenderOutcomes(stopOutcome, restoreOutcome);
        }
        else
        {
            var routeOutcome = await ApplyTestSenderRoutingAsync().ConfigureAwait(false);
            if (routeOutcome.Kind == OperationOutcomeKind.Failure)
            {
                outcome = routeOutcome;
            }
            else
            {
                var startOutcome = _testLslSignalService.Start(streamName, streamType, sourceId);
                if (startOutcome.Kind == OperationOutcomeKind.Failure)
                {
                    var restoreOutcome = await RestoreTestSenderRoutingAsync().ConfigureAwait(false);
                    outcome = CombineTestSenderOutcomes(startOutcome, restoreOutcome);
                }
                else
                {
                    outcome = CombineTestSenderOutcomes(routeOutcome, startOutcome);
                }
            }
        }

        await ApplyOutcomeAsync(actionLabel, outcome).ConfigureAwait(false);
        await DispatchAsync(() =>
        {
            RefreshBenchToolsStatus();
            UpdateLslCard();
        }).ConfigureAwait(false);
        QueueMachineLslStateRefresh(MachineLslStateRefreshSettleDelay);
    }

    private async Task<OperationOutcome> ApplyTestSenderRoutingAsync()
    {
        _testSenderRestoreHeartbeatMode ??= GetFirstValue(
            "showcase_heartbeat_mode",
            "hotload.showcase_heartbeat_mode",
            "routing.heartbeat.mode");
        _testSenderRestoreCoherenceMode ??= GetFirstValue(
            "showcase_coherence_mode",
            "hotload.showcase_coherence_mode",
            "routing.coherence.mode");

        return await PublishTestSenderRoutingAsync(
            TestSenderHeartbeatMode,
            TestSenderCoherenceMode,
            "test-sender-coherence-route",
            "Bench route for TEST sender-driven coherence checks.",
            "TEST sender routing enabled.",
            "Heartbeat mode switched to LSL and coherence mode switched to direct LSL for bench checks.")
            .ConfigureAwait(false);
    }

    private async Task<OperationOutcome> RestoreTestSenderRoutingAsync()
    {
        var heartbeatMode = _testSenderRestoreHeartbeatMode;
        var coherenceMode = _testSenderRestoreCoherenceMode;
        _testSenderRestoreHeartbeatMode = null;
        _testSenderRestoreCoherenceMode = null;

        if (string.IsNullOrWhiteSpace(heartbeatMode) || string.IsNullOrWhiteSpace(coherenceMode))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Preview,
                "TEST sender route restore skipped.",
                "No prior heartbeat/coherence routing snapshot was available to restore.");
        }

        return await PublishTestSenderRoutingAsync(
            heartbeatMode,
            coherenceMode,
            "test-sender-route-restore",
            "Restore heartbeat/coherence routing after TEST sender stop.",
            "TEST sender routing restored.",
            $"Heartbeat mode restored to {heartbeatMode} and coherence mode restored to {coherenceMode}.")
            .ConfigureAwait(false);
    }

    private async Task<OperationOutcome> PublishTestSenderRoutingAsync(
        string heartbeatMode,
        string coherenceMode,
        string profileId,
        string description,
        string summary,
        string detail)
    {
        var profile = new RuntimeConfigProfile(
            profileId,
            "Study Shell TEST Sender Route",
            string.Empty,
            DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture),
            "bench",
            false,
            description,
            [_study.App.PackageId],
            [
                new RuntimeConfigEntry("showcase_heartbeat_mode", heartbeatMode),
                new RuntimeConfigEntry("showcase_coherence_mode", coherenceMode)
            ]);
        var target = new QuestAppTarget(
            _study.Id,
            _study.App.Label,
            _study.App.PackageId,
            _study.App.ApkPath,
            _study.App.LaunchComponent,
            string.Empty,
            _study.Description,
            []);

        var outcome = await _twinBridge.PublishRuntimeConfigAsync(profile, target).ConfigureAwait(false);
        return outcome.Kind == OperationOutcomeKind.Failure
            ? outcome
            : new OperationOutcome(outcome.Kind, summary, detail);
    }

    private bool IsStudyRuntimeForeground()
        => _headsetStatus?.IsTargetForeground == true
            || string.Equals(_headsetStatus?.ForegroundPackageId, _study.App.PackageId, StringComparison.OrdinalIgnoreCase);

    private bool IsHeadsetWakeBlockedByLockScreen()
        => _headsetStatus?.IsConnected == true &&
           !IsStudyRuntimeForeground() &&
           !string.IsNullOrWhiteSpace(_headsetStatus.ForegroundComponent) &&
           _headsetStatus.ForegroundComponent.Contains(QuestSensorLockActivity, StringComparison.OrdinalIgnoreCase);

    private async Task<OperationOutcome?> TryStabilizeStudyPerformancePolicyAsync()
    {
        if (!IsStudyRuntimeForeground())
        {
            return null;
        }

        var target = CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false));
        var profile = new RuntimeConfigProfile(
            "sussex-study-performance-policy",
            "Sussex Study Runtime Performance Policy",
            string.Empty,
            DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture),
            "study",
            false,
            "Disable Sussex direct OVR CPU/GPU writes so the pinned ADB device profile remains authoritative.",
            [_study.App.PackageId],
            [
                new RuntimeConfigEntry("performance_hint_write_direct_levels", "false")
            ]);

        var policyOutcome = await _twinBridge.PublishRuntimeConfigAsync(profile, target).ConfigureAwait(false);
        var profileOutcome = await _questService.ApplyDeviceProfileAsync(CreatePinnedDeviceProfile()).ConfigureAwait(false);
        if (profileOutcome.Kind == OperationOutcomeKind.Failure)
        {
            return profileOutcome;
        }

        if (policyOutcome.Kind == OperationOutcomeKind.Failure)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Applied study device profile, but Sussex may still reset CPU/GPU levels.",
                $"{profileOutcome.Detail} The live runtime performance policy could not be published: {policyOutcome.Detail}");
        }

        return new OperationOutcome(
            profileOutcome.Kind,
            "Applied study device profile and locked Sussex runtime CPU/GPU policy.",
            $"{profileOutcome.Detail} Published performance_hint_write_direct_levels=false so the running Sussex APK stops overwriting debug.oculus.cpuLevel/gpuLevel.");
    }

    private static OperationOutcome CombineTestSenderOutcomes(OperationOutcome first, OperationOutcome second)
    {
        if (first.Kind == OperationOutcomeKind.Failure)
        {
            return first;
        }

        if (second.Kind == OperationOutcomeKind.Failure)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                first.Summary,
                $"{first.Detail} {second.Detail}".Trim());
        }

        var kind = first.Kind == OperationOutcomeKind.Warning || second.Kind == OperationOutcomeKind.Warning
            ? OperationOutcomeKind.Warning
            : first.Kind == OperationOutcomeKind.Preview || second.Kind == OperationOutcomeKind.Preview
                ? OperationOutcomeKind.Preview
                : OperationOutcomeKind.Success;

        var detail = string.Join(
            " ",
            new[] { first.Detail, second.Detail }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));
        return new OperationOutcome(kind, first.Summary, detail);
    }

    public async Task StartBreathingCalibrationAsync()
    {
        const string ActionLabel = "Start Breathing Calibration";
        if (!CanStartBreathingCalibration)
        {
            await ApplyOutcomeAsync(
                ActionLabel,
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    $"{ActionLabel} unavailable.",
                    "The current public runtime does not expose a breathing-calibration command yet.")).ConfigureAwait(false);
            return;
        }

        if (!await EnsureControllerTrackedForCalibrationAsync(ActionLabel).ConfigureAwait(false))
        {
            return;
        }

        await SendStudyTwinCommandAsync(_study.Controls.StartBreathingCalibrationActionId, ActionLabel).ConfigureAwait(false);
    }

    public Task StartDynamicAxisCalibrationAsync()
        => StartBreathingCalibrationWithModeAsync(
            useDynamicMotionAxis: true,
            actionLabel: "Start Dynamic-Axis Calibration");

    public Task StartFixedAxisCalibrationAsync()
        => StartBreathingCalibrationWithModeAsync(
            useDynamicMotionAxis: false,
            actionLabel: "Start Fixed-Axis Calibration");

    public Task ResetBreathingCalibrationAsync()
        => SendStudyTwinCommandAsync(_study.Controls.ResetBreathingCalibrationActionId, "Reset Breathing Calibration");

    private async Task StartBreathingCalibrationWithModeAsync(bool useDynamicMotionAxis, string actionLabel)
    {
        if (!CanStartBreathingCalibration)
        {
            await ApplyOutcomeAsync(
                actionLabel,
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    $"{actionLabel} unavailable.",
                    "The current public runtime does not expose a breathing-calibration command yet.")).ConfigureAwait(false);
            return;
        }

        if (!_controllerBreathingProfiles.IsAvailable)
        {
            await ApplyOutcomeAsync(
                actionLabel,
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Calibration mode controls are unavailable.",
                    "The Sussex controller-breathing profile workspace is not ready yet, so the session window cannot switch between dynamic-axis and fixed-axis calibration on this machine.")).ConfigureAwait(false);
            return;
        }

        if (!await EnsureControllerTrackedForCalibrationAsync(actionLabel).ConfigureAwait(false))
        {
            return;
        }

        if (useDynamicMotionAxis)
        {
            await _controllerBreathingProfiles.UseDynamicAxisCalibrationAsync().ConfigureAwait(false);
        }
        else
        {
            await _controllerBreathingProfiles.UseFixedOrientationCalibrationAsync().ConfigureAwait(false);
        }

        var applyLevel = await DispatchAsync(() => _controllerBreathingProfiles.ApplyLevel).ConfigureAwait(false);
        if (applyLevel == OperationOutcomeKind.Failure)
        {
            var applySummary = await DispatchAsync(() => _controllerBreathingProfiles.ApplySummary).ConfigureAwait(false);
            var applyDetail = await DispatchAsync(() => _controllerBreathingProfiles.ApplyDetail).ConfigureAwait(false);
            await ApplyOutcomeAsync(
                actionLabel,
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    string.IsNullOrWhiteSpace(applySummary) ? $"{actionLabel} failed." : applySummary,
                    applyDetail)).ConfigureAwait(false);
            return;
        }

        var modeConfirmationOutcome = await WaitForControllerCalibrationModeConfirmationAsync(
                useDynamicMotionAxis,
                actionLabel)
            .ConfigureAwait(false);
        if (modeConfirmationOutcome is not null)
        {
            await ApplyOutcomeAsync(actionLabel, modeConfirmationOutcome).ConfigureAwait(false);
            return;
        }

        await SendStudyTwinCommandCoreAsync(_study.Controls.StartBreathingCalibrationActionId, actionLabel).ConfigureAwait(false);
    }

    private async Task<bool> EnsureControllerTrackedForCalibrationAsync(string actionLabel)
    {
        var failure = await DispatchAsync(() => BuildControllerTrackingPreflightFailure(actionLabel)).ConfigureAwait(false);
        if (failure is null)
        {
            return true;
        }

        await ApplyOutcomeAsync(actionLabel, failure).ConfigureAwait(false);
        return false;
    }

    private OperationOutcome? BuildControllerTrackingPreflightFailure(string actionLabel)
    {
        if (_reportedTwinState.Count == 0)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                $"{actionLabel} blocked until controller tracking is visible.",
                "The shell has not received a live quest_twin_state frame yet, so it cannot verify that the active controller is connected and tracked. Probe the connection or wait for a fresh Sussex runtime frame, then try calibration again.");
        }

        var connected = ParseBool(GetFirstValue(
            "tracker.breathing.controller.selected_controller_connected",
            "study.pose.controller.connected"));
        var tracked = ParseBool(GetFirstValue(
            "tracker.breathing.controller.selected_controller_tracked",
            "study.pose.controller.tracked"));
        var hand = GetFirstValue(
            "tracker.breathing.controller.active_hand",
            "study.pose.controller.hand") ?? "controller";
        var status = GetFirstValue(
            "tracker.breathing.controller.tracking_status",
            "study.pose.controller.tracking_status");
        var blockedReason = GetFirstValue(
            "study.session.calibration_blocked_reason",
            "tracker.breathing.controller.failure_reason");

        var frameLabel = string.IsNullOrWhiteSpace(LastTwinStateTimestampLabel)
            ? "the latest live frame"
            : LastTwinStateTimestampLabel;
        var statusDetail = BuildControllerTrackingDetail(hand, connected, tracked, status);
        var reasonDetail = string.IsNullOrWhiteSpace(blockedReason)
            ? string.Empty
            : $" Last runtime block reason: {blockedReason.Trim()}";

        if (connected is null || tracked is null)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                $"{actionLabel} blocked until controller tracking is reported.",
                $"The latest Sussex state frame does not include selected-controller connected/tracked fields, so the shell cannot verify calibration safety from {frameLabel}. {statusDetail}.{reasonDetail}".Trim());
        }

        if (connected == false)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                $"{actionLabel} blocked because the active controller is not connected.",
                $"The latest Sussex state reports {statusDetail} in {frameLabel}. Connect or wake the selected controller, then try calibration again.{reasonDetail}".Trim());
        }

        if (tracked == false)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                $"{actionLabel} blocked because the active controller is not tracked.",
                $"The latest Sussex state reports {statusDetail} in {frameLabel}. Move the selected controller back into tracking view, then try calibration again.{reasonDetail}".Trim());
        }

        return null;
    }

    private async Task<OperationOutcome?> WaitForControllerCalibrationModeConfirmationAsync(
        bool expectedDynamicMotionAxis,
        string actionLabel)
    {
        var deadline = DateTimeOffset.UtcNow + ControllerCalibrationModeConfirmationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var actualMode = await DispatchAsync(TryGetCurrentControllerCalibrationModeSelection).ConfigureAwait(false);
            if (actualMode == expectedDynamicMotionAxis)
            {
                return null;
            }

            await Task.Delay(ControllerCalibrationModeConfirmationPollInterval).ConfigureAwait(false);
        }

        var finalMode = await DispatchAsync(TryGetCurrentControllerCalibrationModeSelection).ConfigureAwait(false);
        var expectedModeLabel = expectedDynamicMotionAxis ? "dynamic motion axis" : "fixed controller orientation";
        var actualModeLabel = finalMode is null
            ? "unknown"
            : finalMode.Value
                ? "dynamic motion axis"
                : "fixed controller orientation";

        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            $"{actionLabel} is waiting for calibration mode confirmation.",
            $"The companion requested {expectedModeLabel}, but quest_twin_state did not confirm that controller-calibration mode within {ControllerCalibrationModeConfirmationTimeout.TotalSeconds:0} seconds. The latest reported mode is {actualModeLabel}. Wait for the live mode switch to land, then try the calibration start again.");
    }

    public async Task ToggleAutomaticBreathingModeAsync()
    {
        var automaticActive = await DispatchAsync(IsAutomaticBreathingActiveFromTwinState).ConfigureAwait(false);
        if (automaticActive)
        {
            if (!string.IsNullOrWhiteSpace(_study.Controls.PauseAutomaticBreathingActionId))
            {
                var pauseOutcome = await SendStudyTwinCommandCoreAsync(
                        _study.Controls.PauseAutomaticBreathingActionId,
                        "Pause Automatic Breathing")
                    .ConfigureAwait(false);
                if (pauseOutcome.Kind == OperationOutcomeKind.Failure)
                {
                    return;
                }
            }

            var restoreModeOutcome = await SendStudyTwinCommandCoreAsync(
                    _study.Controls.SetBreathingModeControllerVolumeActionId,
                    "Set Breathing: Controller Volume")
                .ConfigureAwait(false);
            if (restoreModeOutcome.Kind == OperationOutcomeKind.Failure)
            {
                return;
            }

            await DispatchAsync(() =>
            {
                RememberAutomaticBreathingRequest(false, false, "Use Controller Volume Driver");
                RefreshAutomaticBreathingStateProperties();
            }).ConfigureAwait(false);
            return;
        }

        var modeOutcome = await SendStudyTwinCommandCoreAsync(
                _study.Controls.SetBreathingModeAutomaticCycleActionId,
                "Set Breathing: Automatic Cycle")
            .ConfigureAwait(false);
        if (modeOutcome.Kind == OperationOutcomeKind.Failure)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_study.Controls.StartAutomaticBreathingActionId))
        {
            var startOutcome = await SendStudyTwinCommandCoreAsync(
                    _study.Controls.StartAutomaticBreathingActionId,
                    "Start Automatic Breathing")
                .ConfigureAwait(false);
            if (startOutcome.Kind == OperationOutcomeKind.Failure)
            {
                return;
            }
        }

        await DispatchAsync(() =>
        {
            RememberAutomaticBreathingRequest(true, true, "Use Automatic Driver");
            RefreshAutomaticBreathingStateProperties();
        }).ConfigureAwait(false);
    }

    public async Task ToggleAutomaticBreathingRunAsync()
    {
        var telemetry = await DispatchAsync(CaptureAutomaticBreathingTelemetry).ConfigureAwait(false);
        if (telemetry.AutomaticRoute && telemetry.AutomaticRunning == true)
        {
            var pauseOutcome = await SendStudyTwinCommandCoreAsync(
                    _study.Controls.PauseAutomaticBreathingActionId,
                    "Pause Automatic Breathing")
                .ConfigureAwait(false);
            if (pauseOutcome.Kind == OperationOutcomeKind.Failure)
            {
                return;
            }

            await DispatchAsync(() =>
            {
                RememberAutomaticBreathingRequest(true, false, "Pause Automatic");
                RefreshAutomaticBreathingStateProperties();
            }).ConfigureAwait(false);
            return;
        }

        if (!telemetry.AutomaticRoute)
        {
            var modeOutcome = await SendStudyTwinCommandCoreAsync(
                    _study.Controls.SetBreathingModeAutomaticCycleActionId,
                    "Set Breathing: Automatic Cycle")
                .ConfigureAwait(false);
            if (modeOutcome.Kind == OperationOutcomeKind.Failure)
            {
                return;
            }
        }

        var startOutcome = await SendStudyTwinCommandCoreAsync(
                _study.Controls.StartAutomaticBreathingActionId,
                "Start Automatic Breathing")
            .ConfigureAwait(false);
        if (startOutcome.Kind == OperationOutcomeKind.Failure)
        {
            return;
        }

        await DispatchAsync(() =>
        {
            RememberAutomaticBreathingRequest(true, true, "Start Automatic");
            RefreshAutomaticBreathingStateProperties();
        }).ConfigureAwait(false);
    }

    private Task ToggleRecordingAsync()
        => IsRecordingToggleState
            ? !CanEndParticipantExperiment
                ? Task.CompletedTask
                : EndExperimentAsync()
            : !CanStartParticipantExperiment
                ? Task.CompletedTask
                : StartExperimentAsync();

    public async Task ApplySelectedConditionAsync()
    {
        var condition = await DispatchAsync(() => SelectedCondition).ConfigureAwait(false);
        if (condition is null)
        {
            await DispatchAsync(() => SetConditionStatus(
                OperationOutcomeKind.Warning,
                "No Sussex condition selected.",
                "Choose a condition before applying session profiles.")).ConfigureAwait(false);
            return;
        }

        if (!await DispatchAsync(() => CanApplySelectedCondition).ConfigureAwait(false))
        {
            await DispatchAsync(() => SetConditionStatus(
                OperationOutcomeKind.Warning,
                "Condition switch blocked during the active run.",
                "Stop the current participant recording before changing condition profiles.")).ConfigureAwait(false);
            return;
        }

        await DispatchAsync(() => SetConditionStatus(
            OperationOutcomeKind.Preview,
            $"Applying {condition.Label}.",
            "Loading the linked Sussex visual and controller-breathing profiles.")).ConfigureAwait(false);

        await _visualProfiles.InitializeAsync().ConfigureAwait(false);
        await _controllerBreathingProfiles.InitializeAsync().ConfigureAwait(false);

        var visualSelection = await DispatchAsync(() =>
        {
            var ok = _visualProfiles.TrySelectProfile(condition.VisualProfileId, out var label, out var error);
            return new ProfileConditionSelectionResult(ok, label, error);
        }).ConfigureAwait(false);
        if (!visualSelection.Success)
        {
            await DispatchAsync(() => SetConditionStatus(
                OperationOutcomeKind.Failure,
                $"Condition {condition.Label} could not select its visual profile.",
                visualSelection.Error ?? "The configured Sussex visual profile reference did not resolve.")).ConfigureAwait(false);
            return;
        }

        var controllerSelection = await DispatchAsync(() =>
        {
            var ok = _controllerBreathingProfiles.TrySelectProfile(condition.ControllerBreathingProfileId, out var label, out var error);
            return new ProfileConditionSelectionResult(ok, label, error);
        }).ConfigureAwait(false);
        if (!controllerSelection.Success)
        {
            await DispatchAsync(() => SetConditionStatus(
                OperationOutcomeKind.Failure,
                $"Condition {condition.Label} could not select its breathing profile.",
                controllerSelection.Error ?? "The configured Sussex controller-breathing profile reference did not resolve.")).ConfigureAwait(false);
            return;
        }

        var visualPinned = await _visualProfiles.PinSelectedProfileForStartupAsync().ConfigureAwait(false);
        if (!visualPinned)
        {
            await DispatchAsync(() => SetConditionStatus(
                OperationOutcomeKind.Warning,
                $"Condition {condition.Label} visual profile was selected but not pinned.",
                _visualProfiles.LibraryDetail)).ConfigureAwait(false);
            return;
        }

        var controllerPinned = await _controllerBreathingProfiles.PinSelectedProfileForStartupAsync().ConfigureAwait(false);
        if (!controllerPinned)
        {
            await DispatchAsync(() => SetConditionStatus(
                OperationOutcomeKind.Warning,
                $"Condition {condition.Label} breathing profile was selected but not pinned.",
                _controllerBreathingProfiles.LibraryDetail)).ConfigureAwait(false);
            return;
        }

        var runtimeForeground = await DispatchAsync(IsStudyRuntimeForeground).ConfigureAwait(false);
        if (runtimeForeground)
        {
            await _visualProfiles.ApplySelectedProfileToCurrentSessionAsync().ConfigureAwait(false);
            await _controllerBreathingProfiles.ApplySelectedProfileToCurrentSessionAsync().ConfigureAwait(false);
        }

        var applyLevel = runtimeForeground &&
                         (_visualProfiles.ApplyLevel == OperationOutcomeKind.Failure ||
                          _controllerBreathingProfiles.ApplyLevel == OperationOutcomeKind.Failure)
            ? OperationOutcomeKind.Warning
            : OperationOutcomeKind.Success;
        var runtimeAction = runtimeForeground
            ? "The profiles were also hotloaded into the foreground Sussex runtime."
            : "The profiles were pinned for the next Sussex launch; no live hotload was attempted because Sussex is not foregrounded.";
        var detail =
            $"Visual: {visualSelection.Label}. Breathing: {controllerSelection.Label}. {runtimeAction}";

        await DispatchAsync(() =>
        {
            SelectedConditionVisualProfileLabel = $"Visual profile: {visualSelection.Label}";
            SelectedConditionControllerBreathingProfileLabel = $"Breathing profile: {controllerSelection.Label}";
            SetConditionStatus(
                applyLevel,
                $"Condition {condition.Label} ready.",
                detail);
            AppendLog(
                applyLevel == OperationOutcomeKind.Success ? OperatorLogLevel.Info : OperatorLogLevel.Warning,
                $"Condition {condition.Label} selected.",
                detail);
        }).ConfigureAwait(false);
    }

    public async Task StartExperimentAsync()
    {
        var recordingSession = await TryBeginParticipantRecordingAsync().ConfigureAwait(false);
        if (recordingSession is null)
        {
            return;
        }

        var metadataSnapshotGate = await DispatchAsync(CaptureTwinSnapshotGate).ConfigureAwait(false);
        var metadataIssuedAtUtc = DateTimeOffset.UtcNow;
        var metadataOutcome = await PublishParticipantSessionMetadataAsync(recordingSession).ConfigureAwait(false);
        recordingSession.RecordEvent(
            "recording.device_metadata_publish",
            BuildRecorderEventDetail(metadataOutcome),
            null,
            metadataOutcome.Kind.ToString());

        if (metadataOutcome.Kind == OperationOutcomeKind.Failure)
        {
            await StopUpstreamLslMonitorAsync().ConfigureAwait(false);
            await FinishParticipantRecordingAsync(
                    recordingSession,
                    DateTimeOffset.UtcNow,
                    "recording.aborted",
                    "Local recording closed because Quest session metadata could not be published.",
                    "failed",
                    clearParticipantId: false,
                    pullQuestArtifacts: false,
                    remoteSessionDir: string.Empty,
                    generateSessionReviewPdf: false)
                .ConfigureAwait(false);

            await ApplyOutcomeAsync(
                    "Start Participant Run",
                    new OperationOutcome(
                        OperationOutcomeKind.Failure,
                        "Participant run did not start.",
                        $"The local recorder was created, but the Quest session metadata handoff failed: {metadataOutcome.Detail}"))
                .ConfigureAwait(false);
            return;
        }

        var metadataBaselineReady = await WaitForFreshParticipantSessionRuntimeConfigBaselineAsync(
                metadataSnapshotGate,
                metadataIssuedAtUtc,
                recordingSession)
            .ConfigureAwait(false);
        var metadataConfirmationOutcome = metadataBaselineReady
            ? new OperationOutcome(
                OperationOutcomeKind.Success,
                "Quest-side session metadata confirmed.",
                "Quest published a fresh runtime-config snapshot containing the expected participant session id and dataset hash before recording started.")
            : new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Quest-side session metadata did not confirm in time.",
                $"The companion published session metadata for session `{recordingSession.SessionId}`, but no fresh runtime-config snapshot reported that session id plus dataset hash `{recordingSession.DatasetHash}` within {StartupRuntimeConfigBaselineTimeout.TotalSeconds:0} seconds. Start Recording was aborted to avoid mixing Windows data with stale Quest session metadata.");
        recordingSession.RecordEvent(
            "recording.device_metadata_confirmation",
            BuildRecorderEventDetail(metadataConfirmationOutcome),
            null,
            metadataConfirmationOutcome.Kind.ToString());

        if (!metadataBaselineReady)
        {
            await StopUpstreamLslMonitorAsync().ConfigureAwait(false);
            await FinishParticipantRecordingAsync(
                    recordingSession,
                    DateTimeOffset.UtcNow,
                    "recording.aborted",
                    "Local recording closed because Quest session metadata never confirmed for the active participant session.",
                    "failed",
                    clearParticipantId: false,
                    pullQuestArtifacts: false,
                    remoteSessionDir: string.Empty,
                    generateSessionReviewPdf: false)
                .ConfigureAwait(false);

            await ApplyOutcomeAsync(
                    "Start Participant Run",
                    new OperationOutcome(
                        OperationOutcomeKind.Failure,
                        "Participant run did not start.",
                        metadataConfirmationOutcome.Detail))
                .ConfigureAwait(false);
            return;
        }

        recordingSession.RecordEvent(
            "experiment.start_requested",
            "Participant run requested from the Sussex workflow shell.",
            _study.Controls.StartExperimentActionId,
            "pending");

        var startConfirmationGate = await DispatchAsync(CaptureTwinSnapshotGate).ConfigureAwait(false);
        var outcome = await SendStudyTwinCommandCoreAsync(_study.Controls.StartExperimentActionId, "Start Experiment").ConfigureAwait(false);
        var startCommandIssuedAtUtc = DateTimeOffset.UtcNow;
        recordingSession.RecordEvent(
            "experiment.start_command",
            BuildRecorderEventDetail(outcome),
            _study.Controls.StartExperimentActionId,
            outcome.Kind.ToString());

        if (outcome.Kind == OperationOutcomeKind.Failure)
        {
            await StopUpstreamLslMonitorAsync().ConfigureAwait(false);
            await FinishParticipantRecordingAsync(
                    recordingSession,
                    DateTimeOffset.UtcNow,
                    "recording.aborted",
                    "Local recording closed because Start Experiment failed.",
                    "failed",
                    clearParticipantId: false,
                    pullQuestArtifacts: false,
                    remoteSessionDir: string.Empty,
                    generateSessionReviewPdf: false)
                .ConfigureAwait(false);

            await ApplyOutcomeAsync(
                    "Start Participant Run",
                    new OperationOutcome(
                        OperationOutcomeKind.Failure,
                        "Participant run did not start.",
                        $"The local recorder was created, but Start Experiment failed: {outcome.Detail}"))
                .ConfigureAwait(false);
            return;
        }

        var deviceConfirmationOutcome = await ConfirmDeviceRecordingSessionAsync(
                recordingSession,
                startConfirmationGate,
                startCommandIssuedAtUtc)
            .ConfigureAwait(false);
        recordingSession.RecordEvent(
            "recording.device_confirmation",
            BuildRecorderEventDetail(deviceConfirmationOutcome),
            null,
            deviceConfirmationOutcome.Kind.ToString());

        var warmTransportOutcome = await _clockAlignmentService.StartWarmSessionAsync().ConfigureAwait(false);
        recordingSession.RecordEvent(
            "clock_alignment.transport_warmup",
            BuildRecorderEventDetail(warmTransportOutcome),
            null,
            warmTransportOutcome.Kind.ToString());

        var clockAlignmentOutcome = await RunClockAlignmentAsync(
                recordingSession,
                StudyClockAlignmentWindowKind.StartBurst,
                showWindow: false)
            .ConfigureAwait(false);
        recordingSession.RecordEvent(
            "clock_alignment.start_burst.result",
            BuildRecorderEventDetail(clockAlignmentOutcome),
            null,
            clockAlignmentOutcome.Kind.ToString());

        var upstreamMonitorOutcome = TryStartUpstreamLslMonitor(recordingSession);
        recordingSession.RecordEvent(
            "lsl.upstream_monitor.start",
            BuildRecorderEventDetail(upstreamMonitorOutcome),
            null,
            upstreamMonitorOutcome.Kind.ToString());

        StartBackgroundClockAlignmentMonitoring(recordingSession);

        var overallOutcome = CombineWorkflowOutcomes(
            "Start Participant Run",
            [
                ("Start Experiment", outcome),
                ("Quest Recorder", deviceConfirmationOutcome),
                ("Clock Alignment Warmup", warmTransportOutcome),
                ("Clock Alignment (Start Burst)", clockAlignmentOutcome),
                ("Passive Upstream LSL Monitor", upstreamMonitorOutcome)
            ],
            "Started the participant run, confirmed the Quest-side recorder, collected the dedicated start clock-alignment burst, and armed sparse background timing probes.");
        await ApplyOutcomeAsync("Start Participant Run", overallOutcome).ConfigureAwait(false);

        await DispatchAsync(UpdateParticipantSessionState).ConfigureAwait(false);
    }

    public async Task EndExperimentAsync()
    {
        await DispatchAsync(() =>
        {
            _participantRunStopping = true;
            UpdateParticipantSessionState();
            RefreshBenchToolsStatus();
        }).ConfigureAwait(false);

        var activeSession = await DispatchAsync(() => _activeRecordingSession).ConfigureAwait(false);
        activeSession?.RecordEvent(
            "experiment.end_requested",
            "Participant wrap-up requested from the Sussex workflow shell.",
            _study.Controls.EndExperimentActionId,
            "pending");

        var outcomes = new List<(string Label, OperationOutcome Outcome)>(4);
        var cleanupPermitted = activeSession is null;
        try
        {
            if (activeSession is not null)
            {
                var backgroundStopOutcome = await StopBackgroundClockAlignmentMonitoringAsync().ConfigureAwait(false);
                outcomes.Add(("Background Clock Alignment", backgroundStopOutcome));
                activeSession.RecordEvent(
                    "clock_alignment.background_monitor.result",
                    BuildRecorderEventDetail(backgroundStopOutcome),
                    null,
                    backgroundStopOutcome.Kind.ToString());

                var endClockAlignmentOutcome = await RunClockAlignmentAsync(
                        activeSession,
                        StudyClockAlignmentWindowKind.EndBurst,
                        showWindow: false)
                    .ConfigureAwait(false);
                outcomes.Add(("Clock Alignment (End Burst)", endClockAlignmentOutcome));
                activeSession.RecordEvent(
                    "clock_alignment.end_burst.result",
                    BuildRecorderEventDetail(endClockAlignmentOutcome),
                    null,
                    endClockAlignmentOutcome.Kind.ToString());
            }

            var stopConfirmationGate = await DispatchAsync(CaptureTwinSnapshotGate).ConfigureAwait(false);
            var endOutcome = await SendStudyTwinCommandCoreAsync(_study.Controls.EndExperimentActionId, "End Experiment").ConfigureAwait(false);
            var endCommandIssuedAtUtc = DateTimeOffset.UtcNow;
            outcomes.Add(("End Experiment", endOutcome));
            activeSession?.RecordEvent(
                "experiment.end_command",
                BuildRecorderEventDetail(endOutcome),
                _study.Controls.EndExperimentActionId,
                endOutcome.Kind.ToString());

            if (activeSession is null)
            {
                cleanupPermitted = endOutcome.Kind != OperationOutcomeKind.Failure;
                var warmTransportStopOutcome = await _clockAlignmentService.StopWarmSessionAsync().ConfigureAwait(false);
                outcomes.Add(("Clock Alignment Warmup", warmTransportStopOutcome));
            }
            else
            {
                var deviceStopOutcome = await ConfirmDeviceRecordingStoppedAsync(
                        activeSession,
                        stopConfirmationGate,
                        endCommandIssuedAtUtc)
                    .ConfigureAwait(false);
                outcomes.Add(("Quest Recorder Stop", deviceStopOutcome));
                activeSession.RecordEvent(
                    "recording.device_stop_confirmation",
                    BuildRecorderEventDetail(deviceStopOutcome),
                    null,
                    deviceStopOutcome.Kind.ToString());

                var upstreamMonitorStopOutcome = await StopUpstreamLslMonitorAsync().ConfigureAwait(false);
                outcomes.Add(("Upstream LSL Monitor", upstreamMonitorStopOutcome));
                activeSession.RecordEvent(
                    "lsl.upstream_monitor.stopped",
                    BuildRecorderEventDetail(upstreamMonitorStopOutcome),
                    null,
                    upstreamMonitorStopOutcome.Kind.ToString());

                var warmTransportStopOutcome = await _clockAlignmentService.StopWarmSessionAsync().ConfigureAwait(false);
                outcomes.Add(("Clock Alignment Warmup", warmTransportStopOutcome));
                activeSession.RecordEvent(
                    "clock_alignment.transport_cooldown",
                    BuildRecorderEventDetail(warmTransportStopOutcome),
                    null,
                    warmTransportStopOutcome.Kind.ToString());

                var remoteSessionDir = await DispatchAsync(() =>
                        GetFirstValue("study.recording.device.session_dir") ?? _latestDeviceRecordingSessionDir)
                    .ConfigureAwait(false);
                var closedAtUtc = DateTimeOffset.UtcNow;
                var finishOutcome = await FinishParticipantRecordingAsync(
                        activeSession,
                        closedAtUtc,
                        "recording.stopped",
                        "Participant recording closed from the Sussex workflow shell before cleanup commands.",
                        deviceStopOutcome.Kind == OperationOutcomeKind.Success ? "completed" : "warning",
                        clearParticipantId: true,
                        pullQuestArtifacts: true,
                        remoteSessionDir: remoteSessionDir ?? string.Empty,
                        generateSessionReviewPdf: !ValidationCaptureRunning)
                    .ConfigureAwait(false);
                if (finishOutcome is not null)
                {
                    outcomes.Add(("Quest Backup Pullback", finishOutcome));
                }

                cleanupPermitted = endOutcome.Kind != OperationOutcomeKind.Failure;
            }
        }
        finally
        {
            if (cleanupPermitted)
            {
                if (CanResetBreathingCalibration)
                {
                    var resetOutcome = await SendStudyTwinCommandCoreAsync(_study.Controls.ResetBreathingCalibrationActionId, "Reset Breathing Calibration").ConfigureAwait(false);
                    outcomes.Add(("Reset Calibration", resetOutcome));
                }

                if (CanToggleParticles)
                {
                    var particlesOutcome = await SendStudyTwinCommandCoreAsync(_study.Controls.ParticleVisibleOffActionId, "Particles Off").ConfigureAwait(false);
                    outcomes.Add(("Particles Off", particlesOutcome));
                }
            }
            else
            {
                await DispatchAsync(() =>
                {
                    _participantRunStopping = false;
                    UpdateParticipantSessionState();
                    RefreshBenchToolsStatus();
                }).ConfigureAwait(false);
            }
        }

        var overallOutcome = CombineWorkflowOutcomes(
            "End Participant Run",
            outcomes,
            activeSession is null
                ? "Ended the participant flow without an active local recording session."
                : "Ended the participant flow, captured the matching end clock-alignment burst, waited for Quest recorder stop confirmation, closed the upstream monitor and local recording session, and only then ran shell cleanup.");
        await ApplyOutcomeAsync("End Participant Run", overallOutcome).ConfigureAwait(false);

        if (activeSession is null)
        {
            await DispatchAsync(() =>
            {
                _participantRunStopping = false;
                UpdateParticipantSessionState();
                RefreshBenchToolsStatus();
            }).ConfigureAwait(false);
        }
    }

    private async Task RunWorkflowValidationCaptureAsync()
    {
        if (ValidationCaptureRunning)
        {
            return;
        }

        await DispatchAsync(() =>
        {
            ValidationCaptureRunning = true;
            ValidationCaptureCompleted = false;
            ValidationCaptureSummary = "Validation capture is starting.";
            ValidationCaptureDetail = "The guide will run a start clock burst, record 20 seconds of data with sparse drift probes armed in the background, run a matching end burst, and then pull the Quest-side backup files.";
            SetValidationCaptureFolders(string.Empty, string.Empty, string.Empty, string.Empty);
            ValidationCaptureParticipantId = string.Empty;
            ResetValidationClockAlignmentGuideState();
            SetValidationCaptureProgress(0d, "Phase 1 of 4: preparing the start clock-alignment burst.");
            ClearValidationCapturePlots();
            UpdateWorkflowGuideState();
        }).ConfigureAwait(false);

        try
        {
            await StartExperimentAsync().ConfigureAwait(false);

            var activeSession = await DispatchAsync(() => _activeRecordingSession).ConfigureAwait(false);
            if (activeSession is null)
            {
                await DispatchAsync(() =>
                {
                    ValidationCaptureRunning = false;
                    ValidationCaptureCompleted = false;
                    ValidationCaptureSummary = "Validation capture did not start.";
                    ValidationCaptureDetail = "The participant recorder never became active. Fix the earlier guide steps and try again.";
                    SetValidationCaptureProgress(0d, "Validation capture did not start.");
                    UpdateWorkflowGuideState();
                }).ConfigureAwait(false);
                return;
            }

            var participantId = activeSession.ParticipantId;
            var localSessionFolderPath = activeSession.SessionFolderPath;
            var remoteSessionDir = await DispatchAsync(() => GetFirstValue("study.recording.device.session_dir") ?? _latestDeviceRecordingSessionDir).ConfigureAwait(false);

            await DispatchAsync(() =>
            {
                ValidationCaptureParticipantId = participantId;
                SetValidationCaptureFolders(localSessionFolderPath, remoteSessionDir ?? string.Empty, string.Empty, string.Empty);
                ValidationCaptureSummary = $"Validation capture running for {participantId}.";
                ValidationCaptureDetail = $"Recording for {WorkflowGuideValidationCaptureDurationSeconds} seconds. Windows session folder: {localSessionFolderPath}";
                SetValidationCaptureProgress(0d, $"Phase 2 of 4: collecting data for 0.0 of {WorkflowGuideValidationCaptureDurationSeconds} seconds.");
                UpdateWorkflowGuideState();
            }).ConfigureAwait(false);

            var captureDuration = TimeSpan.FromSeconds(WorkflowGuideValidationCaptureDurationSeconds);
            var progressUpdateInterval = TimeSpan.FromMilliseconds(250);
            var captureStopwatch = Stopwatch.StartNew();

            while (captureStopwatch.Elapsed < captureDuration)
            {
                var remaining = captureDuration - captureStopwatch.Elapsed;
                var delay = remaining < progressUpdateInterval ? remaining : progressUpdateInterval;
                await Task.Delay(delay).ConfigureAwait(false);

                var elapsed = captureStopwatch.Elapsed > captureDuration ? captureDuration : captureStopwatch.Elapsed;
                var percent = captureDuration.TotalMilliseconds <= 0d
                    ? 100d
                    : Math.Clamp(elapsed.TotalMilliseconds / captureDuration.TotalMilliseconds * 100d, 0d, 100d);

                await DispatchAsync(() =>
                {
                    SetValidationCaptureProgress(
                        percent,
                        $"Phase 2 of 4: collecting data for {elapsed.TotalSeconds:0.0} of {captureDuration.TotalSeconds:0} seconds.");
                }).ConfigureAwait(false);
            }

            await DispatchAsync(() =>
            {
                SetValidationCaptureProgress(100d, "Phase 3 of 4: finishing the run and starting the end clock-alignment burst.");
                UpdateWorkflowGuideState();
            }).ConfigureAwait(false);

            await EndExperimentAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(remoteSessionDir))
            {
                remoteSessionDir = await DispatchAsync(() => _latestDeviceRecordingSessionDir).ConfigureAwait(false);
            }

            var completedLocalFolder = await DispatchAsync(() => string.IsNullOrWhiteSpace(_lastCompletedRecordingFolderPath) ? localSessionFolderPath : _lastCompletedRecordingFolderPath).ConfigureAwait(false);
            await DispatchAsync(() =>
            {
                FinalizeValidationClockAlignmentBackgroundState();
                SetValidationCaptureProgress(100d, "Phase 4 of 4: pulling Quest backup files and attempting the validation PDF.");
                UpdateWorkflowGuideState();
            }).ConfigureAwait(false);
            var pullOutcome = await PullQuestRecordingArtifactsAsync(remoteSessionDir, completedLocalFolder).ConfigureAwait(false);
            var pulledFolderPath = pullOutcome.Items?.FirstOrDefault() ?? string.Empty;
            var completed = pullOutcome.Kind == OperationOutcomeKind.Success
                && Directory.Exists(completedLocalFolder)
                && Directory.Exists(pulledFolderPath);

            var summary = completed
                ? $"Validation capture completed for {participantId}."
                : "Validation capture finished, but the Quest pullback is incomplete.";
            var detail = completed
                ? $"Windows data: {completedLocalFolder} Quest pullback: {pulledFolderPath}"
                : $"{pullOutcome.Detail} Windows data: {completedLocalFolder}";
            var breathingPlot = await Task.Run(() => ValidationCapturePlotLoader.LoadBreathing(completedLocalFolder)).ConfigureAwait(false);
            var coherencePlot = await Task.Run(() => ValidationCapturePlotLoader.LoadCoherence(completedLocalFolder)).ConfigureAwait(false);
            var pdfReportOutcome = await GenerateValidationCapturePdfAsync(completedLocalFolder).ConfigureAwait(false);
            var pdfPath = pdfReportOutcome.Items?.FirstOrDefault() ?? string.Empty;
            var pdfGenerated = pdfReportOutcome.Kind == OperationOutcomeKind.Success && !string.IsNullOrWhiteSpace(pdfPath);
            summary = completed
                ? pdfGenerated
                    ? $"Validation capture completed for {participantId}."
                    : "Validation capture completed, but the validation PDF could not be generated automatically."
                : pdfGenerated
                    ? "Validation capture finished, but the Quest pullback is incomplete."
                    : "Validation capture finished, but the Quest pullback is incomplete and the validation PDF could not be generated automatically.";
            if (pdfReportOutcome.Kind == OperationOutcomeKind.Success)
            {
                detail = string.IsNullOrWhiteSpace(detail)
                    ? $"Validation PDF: {pdfPath}"
                    : $"{detail} Validation PDF: {pdfPath}";
            }
            else if (!string.IsNullOrWhiteSpace(pdfReportOutcome.Detail))
            {
                detail = string.IsNullOrWhiteSpace(detail)
                    ? pdfReportOutcome.Detail
                    : $"{detail} {pdfReportOutcome.Detail}";
            }

            var outcome = new OperationOutcome(
                completed ? OperationOutcomeKind.Success : pullOutcome.Kind == OperationOutcomeKind.Failure ? OperationOutcomeKind.Warning : pullOutcome.Kind,
                summary,
                detail,
                Items:
                [
                    completedLocalFolder,
                    pulledFolderPath,
                    pdfPath
                ]);

            await DispatchAsync(() =>
            {
                ValidationCaptureRunning = false;
                ValidationCaptureCompleted = completed;
                SetValidationCaptureFolders(completedLocalFolder, remoteSessionDir ?? string.Empty, pulledFolderPath, pdfPath);
                ValidationCaptureSummary = summary;
                ValidationCaptureDetail = detail;
                SetValidationCaptureProgress(
                    100d,
                    completed
                        ? "Validation capture complete. The Windows session folder and pulled Quest backup are ready to inspect."
                        : "Validation capture finished, but the pulled Quest backup is incomplete.");
                ApplyValidationCapturePlots(breathingPlot, coherencePlot);
                UpdateWorkflowGuideState();
            }).ConfigureAwait(false);

            await ApplyOutcomeAsync("20 Second Validation Capture", outcome).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchAsync(() =>
            {
                ValidationCaptureRunning = false;
                ValidationCaptureCompleted = false;
                ValidationCaptureSummary = "Validation capture failed.";
                ValidationCaptureDetail = exception.Message;
                SetValidationCaptureProgress(0d, "Validation capture failed.");
                ClearValidationCapturePlots();
                UpdateWorkflowGuideState();
            }).ConfigureAwait(false);

            await ApplyOutcomeAsync(
                "20 Second Validation Capture",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Validation capture failed.",
                    exception.Message)).ConfigureAwait(false);
        }
        finally
        {
            await DispatchAsync(() =>
            {
                ValidationCaptureRunning = false;
                OnPropertyChanged(nameof(CanRunWorkflowValidationCapture));
                UpdateWorkflowGuideState();
            }).ConfigureAwait(false);
        }
    }

    private void SetValidationCaptureFolders(string localFolderPath, string deviceSessionPath, string devicePullFolderPath)
    {
        ValidationCaptureLocalFolderPath = localFolderPath;
        ValidationCaptureDeviceSessionPath = deviceSessionPath;
        ValidationCaptureDevicePullFolderPath = devicePullFolderPath;
        ValidationCapturePdfPath = string.Empty;
        OnPropertyChanged(nameof(CanOpenValidationCaptureLocalFolder));
        OnPropertyChanged(nameof(CanOpenValidationCaptureDevicePullFolder));
        OnPropertyChanged(nameof(CanOpenValidationCapturePdf));
    }

    private void SetValidationCaptureFolders(string localFolderPath, string deviceSessionPath, string devicePullFolderPath, string pdfPath)
    {
        ValidationCaptureLocalFolderPath = localFolderPath;
        ValidationCaptureDeviceSessionPath = deviceSessionPath;
        ValidationCaptureDevicePullFolderPath = devicePullFolderPath;
        ValidationCapturePdfPath = pdfPath;
        OnPropertyChanged(nameof(CanOpenValidationCaptureLocalFolder));
        OnPropertyChanged(nameof(CanOpenValidationCaptureDevicePullFolder));
        OnPropertyChanged(nameof(CanOpenValidationCapturePdf));
    }

    private void SetValidationCaptureProgress(double percent, string label)
    {
        ValidationCaptureProgressPercent = Math.Clamp(percent, 0d, 100d);
        ValidationCaptureProgressLabel = label;
    }

    private void ClearValidationCapturePlots()
        => ApplyValidationCapturePlots(
            new ValidationCapturePlotLoadResult(false, "Breathing plot not loaded yet.", []),
            new ValidationCapturePlotLoadResult(false, "Coherence plot not loaded yet.", []));

    private void ApplyValidationCapturePlots(
        ValidationCapturePlotLoadResult breathingPlot,
        ValidationCapturePlotLoadResult coherencePlot)
    {
        ValidationCaptureBreathingPlotAvailable = breathingPlot.HasData;
        ValidationCaptureBreathingPlotSummary = breathingPlot.Summary;
        ValidationCaptureBreathingPlotPoints = breathingPlot.Points;

        ValidationCaptureCoherencePlotAvailable = coherencePlot.HasData;
        ValidationCaptureCoherencePlotSummary = coherencePlot.Summary;
        ValidationCaptureCoherencePlotPoints = coherencePlot.Points;
    }

    private static string NormalizeHostVisibleOperatorPath(string? path)
        => CompanionOperatorDataLayout.NormalizeHostVisiblePath(path);

    private async Task OpenValidationCaptureLocalFolderAsync()
    {
        var folderPath = await DispatchAsync(() => ValidationCaptureLocalFolderPath).ConfigureAwait(false);
        await OpenValidationCaptureFolderAsync(
            folderPath,
            "Open Windows Session Folder",
            "Windows session folder is not available yet.",
            "Run the validation capture first so the Windows-side session folder exists.").ConfigureAwait(false);
    }

    private async Task OpenRecordingSessionFolderAsync()
    {
        var folderPath = await DispatchAsync(() => RecordingFolderPath).ConfigureAwait(false);
        await OpenValidationCaptureFolderAsync(
            folderPath,
            "Open Recording Session Folder",
            "Recording session folder is not available yet.",
            "Start or finish a participant run first so the Windows session folder exists.").ConfigureAwait(false);
    }

    private async Task OpenRecordingSessionDevicePullFolderAsync()
    {
        var folderPath = await DispatchAsync(() => RecordingDevicePullFolderPath).ConfigureAwait(false);
        await OpenQuestBackupFolderAsync(
            folderPath,
            "Open Recording Quest Backup",
            "Recording Quest backup folder is not available yet.",
            "Finish a participant run first so the pulled Quest backup folder exists.").ConfigureAwait(false);
    }

    private async Task OpenValidationCaptureDevicePullFolderAsync()
    {
        var folderPath = await DispatchAsync(() => ValidationCaptureDevicePullFolderPath).ConfigureAwait(false);
        await OpenQuestBackupFolderAsync(
            folderPath,
            "Open Pulled Quest Backup",
            "Pulled Quest backup folder is not available yet.",
            "Finish the validation capture and Quest pullback first so the pulled backup folder exists.").ConfigureAwait(false);
    }

    private async Task OpenValidationCapturePdfAsync()
    {
        var pdfPath = await DispatchAsync(() => ValidationCapturePdfPath).ConfigureAwait(false);
        await OpenValidationCaptureFileAsync(
            pdfPath,
            "Open Validation PDF",
            "Validation PDF is not available yet.",
            "Finish the validation capture first so the formatted PDF preview can be generated.").ConfigureAwait(false);
    }

    private async Task OpenRecordingSessionPdfAsync()
    {
        var pdfPath = await DispatchAsync(() => RecordingPdfPath).ConfigureAwait(false);
        await OpenValidationCaptureFileAsync(
            pdfPath,
            "Open Recording Session PDF",
            "Recording session PDF is not available yet.",
            "Finish a participant run first so the formatted session review PDF can be generated.").ConfigureAwait(false);
    }

    private async Task OpenDiagnosticsReportFolderAsync()
    {
        var folderPath = await DispatchAsync(() => DiagnosticsReportFolderPath).ConfigureAwait(false);
        await OpenValidationCaptureFolderAsync(
            folderPath,
            "Open Diagnostics Report Folder",
            "Diagnostics report folder is not available yet.",
            "Generate the diagnostics report first so the shareable folder exists.").ConfigureAwait(false);
    }

    private async Task OpenDiagnosticsReportPdfAsync()
    {
        var pdfPath = await DispatchAsync(() => DiagnosticsReportPdfPath).ConfigureAwait(false);
        await OpenValidationCaptureFileAsync(
            pdfPath,
            "Open Diagnostics Report PDF",
            "Diagnostics report PDF is not available yet.",
            "Generate the diagnostics report first so the native PDF exists.").ConfigureAwait(false);
    }

    private async Task OpenValidationCaptureFolderAsync(string folderPath, string actionLabel, string unavailableSummary, string unavailableDetail)
    {
        if (!CompanionOperatorDataLayout.TryResolveExistingDirectory(folderPath, out var resolvedFolderPath))
        {
            await ApplyOutcomeAsync(
                actionLabel,
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    unavailableSummary,
                    unavailableDetail,
                    Items: string.IsNullOrWhiteSpace(folderPath) ? [] : [NormalizeHostVisibleOperatorPath(folderPath)])).ConfigureAwait(false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = resolvedFolderPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ApplyOutcomeAsync(
                actionLabel,
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    $"{actionLabel} failed.",
                    ex.Message,
                    Items: [resolvedFolderPath])).ConfigureAwait(false);
        }
    }

    private async Task OpenValidationCaptureFileAsync(string filePath, string actionLabel, string unavailableSummary, string unavailableDetail)
    {
        if (!CompanionOperatorDataLayout.TryResolveExistingFile(filePath, out var resolvedFilePath))
        {
            await ApplyOutcomeAsync(
                actionLabel,
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    unavailableSummary,
                    unavailableDetail,
                    Items: string.IsNullOrWhiteSpace(filePath) ? [] : [NormalizeHostVisibleOperatorPath(filePath)])).ConfigureAwait(false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = resolvedFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ApplyOutcomeAsync(
                actionLabel,
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    $"{actionLabel} failed.",
                    ex.Message,
                    Items: [resolvedFilePath])).ConfigureAwait(false);
        }
    }

    private Task<OperationOutcome> GenerateValidationCapturePdfAsync(string localSessionFolderPath)
        => GenerateSessionReviewPdfAsync(localSessionFolderPath, "validation_capture_preview.pdf");

    private async Task<OperationOutcome> GenerateDiagnosticsReportPdfAsync(SussexDiagnosticsReport report, string outputPdfPath)
    {
        if (report is null)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Diagnostics PDF skipped.",
                "The diagnostics report model was not available.");
        }

        try
        {
            await Task.Run(() => SussexDiagnosticsPdfRenderer.Render(report, outputPdfPath)).ConfigureAwait(false);
            return new OperationOutcome(
                OperationOutcomeKind.Success,
                "Diagnostics PDF generated.",
                outputPdfPath,
                Items: [outputPdfPath]);
        }
        catch (Exception ex)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Diagnostics PDF generation failed.",
                $"The JSON and LaTeX diagnostics reports were written. {ex.Message}");
        }
    }

    private async Task<OperationOutcome> GenerateSessionReviewPdfAsync(string localSessionFolderPath, string outputPdfFileName)
    {
        if (string.IsNullOrWhiteSpace(localSessionFolderPath) || !Directory.Exists(localSessionFolderPath))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Session review PDF could not be generated.",
                "The Windows session folder is missing.");
        }

        var outputPdfPath = Path.Combine(localSessionFolderPath, outputPdfFileName);
        try
        {
            await Task.Run(() => SussexValidationPdfRenderer.Render(localSessionFolderPath, outputPdfPath)).ConfigureAwait(false);
            return new OperationOutcome(
                OperationOutcomeKind.Success,
                "Session review PDF generated.",
                outputPdfPath,
                Items: [outputPdfPath]);
        }
        catch (Exception ex)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Session review PDF could not be generated automatically.",
                $"The Windows session folder is still available. {ex.Message}");
        }
    }

    private async Task<OperationOutcome> PullQuestRecordingArtifactsAsync(string remoteSessionDir, string localSessionFolderPath)
    {
        if (string.IsNullOrWhiteSpace(localSessionFolderPath) || !Directory.Exists(localSessionFolderPath))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Windows session folder is missing.",
                "The session completed locally, but the local Windows session folder could not be found afterward.");
        }

        if (string.IsNullOrWhiteSpace(remoteSessionDir))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Quest recording folder was not reported.",
                "The session completed locally, but the Quest runtime did not report a device-side session directory to pull from.");
        }

        if (!_hzdbService.IsAvailable)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "hzdb is not available for Quest file pullback.",
                "The session completed locally, but Quest-side backup files cannot be pulled until hzdb is available.");
        }

        var selector = await DispatchAsync(ResolveHzdbSelector).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(selector))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Quest selector missing for file pullback.",
                "The session completed locally, but the companion no longer has a headset selector for hzdb file pullback.");
        }

        var localPullFolderPath = Path.Combine(localSessionFolderPath, "device-session-pull");
        Directory.CreateDirectory(localPullFolderPath);

        var diagnostics = new List<string>();
        var stoppedAfterCanceledPull = false;
        var stoppedAfterTimedOutPull = false;
        OperationOutcome listOutcome;
        using (var listCts = new CancellationTokenSource(WorkflowHzdbListTimeout))
        {
            try
            {
                listOutcome = await _hzdbService.ListFilesAsync(selector, remoteSessionDir, listCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var listCancelReason = listCts.IsCancellationRequested
                    ? $"hzdb files ls exceeded {WorkflowHzdbListTimeout.TotalSeconds:0} seconds while listing {remoteSessionDir}"
                    : $"hzdb files ls was canceled while listing {remoteSessionDir}";
                diagnostics.Add($"{listCancelReason}; falling back to the known Sussex backup file names.");
                listOutcome = new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    listCts.IsCancellationRequested
                        ? "Quest backup listing timed out."
                        : "Quest backup listing was canceled.",
                    diagnostics[^1]);
            }
        }

        var remoteFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (listOutcome.Kind != OperationOutcomeKind.Failure)
        {
            foreach (var item in listOutcome.Items ?? [])
            {
                var fileName = ExtractDeviceListingFileName(item);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                remoteFiles[fileName] = BuildDeviceRemoteFilePath(remoteSessionDir, item, fileName);
            }
        }

        foreach (var fileName in WorkflowGuideExpectedDeviceRecordingFiles)
        {
            remoteFiles.TryAdd(fileName, BuildDeviceRemoteFilePath(remoteSessionDir, fileName, fileName));
        }

        var pulledFiles = new List<string>();
        var failures = new List<string>();
        foreach (var pair in remoteFiles.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            using var pullCts = new CancellationTokenSource(WorkflowHzdbPullTimeout);
            OperationOutcome pullOutcome;
            try
            {
                pullOutcome = await _hzdbService
                    .PullFileAsync(selector, pair.Value, Path.Combine(localPullFolderPath, pair.Key), pullCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var pullCancelReason = pullCts.IsCancellationRequested
                    ? $"hzdb files pull exceeded {WorkflowHzdbPullTimeout.TotalSeconds:0} seconds"
                    : "hzdb files pull was canceled";
                failures.Add($"{pair.Key}: {pullCancelReason}. Remaining Quest backup files were not attempted to keep stop/close bounded.");
                stoppedAfterCanceledPull = true;
                stoppedAfterTimedOutPull = pullCts.IsCancellationRequested;
                break;
            }

            if (pullOutcome.Kind == OperationOutcomeKind.Success)
            {
                pulledFiles.Add(pair.Key);
            }
            else
            {
                failures.Add($"{pair.Key}: {pullOutcome.Summary}");
            }
        }

        var pullbackDiagnostics = diagnostics.Count > 0
            ? diagnostics
            : listOutcome.Kind == OperationOutcomeKind.Success
                ? []
                : [listOutcome.Detail];

        if (pulledFiles.Count == 0)
        {
            TryDeleteEmptyDirectory(localPullFolderPath);
            var noPullDetail = BuildQuestPullbackDetail(
                localSessionFolderPath,
                remoteSessionDir,
                localPullFolderPath,
                pulledFiles,
                failures,
                pullbackDiagnostics);
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                stoppedAfterTimedOutPull
                    ? "Quest backup pullback timed out; Windows data saved."
                    : stoppedAfterCanceledPull
                        ? "Quest backup pullback was canceled; Windows data saved."
                    : "Quest backup files were not pulled; Windows data saved.",
                noPullDetail,
                Items: []);
        }

        var detail = BuildQuestPullbackDetail(
            localSessionFolderPath,
            remoteSessionDir,
            localPullFolderPath,
            pulledFiles,
            failures,
            pullbackDiagnostics);

        return new OperationOutcome(
            failures.Count == 0 ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning,
            failures.Count == 0
                ? "Quest backup files pulled."
                : stoppedAfterTimedOutPull
                    ? "Quest backup pullback timed out after partial pull; Windows data saved."
                    : stoppedAfterCanceledPull
                        ? "Quest backup pullback was canceled after partial pull; Windows data saved."
                    : "Quest backup files pulled with some gaps.",
            detail,
            Items:
            [
                localPullFolderPath
            ]);
    }

    private static string BuildQuestPullbackDetail(
        string localSessionFolderPath,
        string remoteSessionDir,
        string localPullFolderPath,
        IReadOnlyList<string> pulledFiles,
        IReadOnlyList<string> failures,
        IReadOnlyList<string> diagnostics)
    {
        var detailBuilder = new StringBuilder();
        detailBuilder.Append(BuildWindowsSessionInventoryDetail(localSessionFolderPath));
        detailBuilder.Append(' ');
        detailBuilder.Append($"Quest source folder: {remoteSessionDir}.");

        if (pulledFiles.Count > 0)
        {
            detailBuilder.Append($" Pulled {pulledFiles.Count} Quest file(s) into {localPullFolderPath}: {string.Join(", ", pulledFiles)}.");
        }
        else
        {
            detailBuilder.Append($" No Quest backup files were pulled into {localPullFolderPath}.");
        }

        if (diagnostics.Count > 0)
        {
            detailBuilder.Append(' ');
            detailBuilder.Append(string.Join(" ", diagnostics.Where(value => !string.IsNullOrWhiteSpace(value))));
        }

        if (failures.Count > 0)
        {
            detailBuilder.Append(" Pullback gaps: ");
            detailBuilder.Append(string.Join(" ", failures.Where(value => !string.IsNullOrWhiteSpace(value))));
        }

        detailBuilder.Append(" Treat this as a Quest backup pullback issue, not as proof that Windows-side recorder data was lost.");
        return detailBuilder.ToString();
    }

    private static string BuildWindowsSessionInventoryDetail(string localSessionFolderPath)
    {
        var normalizedPath = NormalizeHostVisibleOperatorPath(localSessionFolderPath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(normalizedPath))
        {
            return $"Windows session folder is missing or unavailable: {normalizedPath}.";
        }

        var present = new List<string>();
        var missing = new List<string>();
        foreach (var fileName in WorkflowExpectedWindowsRecordingFiles)
        {
            var path = Path.Combine(normalizedPath, fileName);
            if (File.Exists(path))
            {
                present.Add(fileName);
            }
            else
            {
                missing.Add(fileName);
            }
        }

        var presentLabel = present.Count == 0 ? "none of the expected core files" : string.Join(", ", present);
        var missingLabel = missing.Count == 0 ? "none" : string.Join(", ", missing);
        return $"Windows data saved in {normalizedPath}. Core Windows files present: {presentLabel}. Missing: {missingLabel}.";
    }

    private static string ExtractDeviceListingFileName(string listedItem)
    {
        var trimmed = (listedItem ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (trimmed.StartsWith("Permissions", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(trimmed, @"^-{3,}$", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(trimmed, @"^\d+\s+items?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return string.Empty;
        }

        var normalized = trimmed.Replace('\\', '/').TrimEnd('/');
        if (normalized.Contains('/', StringComparison.Ordinal))
        {
            var pathFileName = normalized[(normalized.LastIndexOf('/') + 1)..];
            return pathFileName is "." or ".." ? string.Empty : pathFileName;
        }

        var tokens = Regex.Split(trimmed, @"\s+")
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
        if (tokens.Length >= 4 && Regex.IsMatch(tokens[0], @"^[d\-lbcps][rwx\-]{9}$", RegexOptions.CultureInvariant))
        {
            return tokens[^1];
        }

        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        return fileName is "." or ".." ? string.Empty : fileName;
    }

    private static string BuildDeviceRemoteFilePath(string remoteSessionDir, string listedItem, string fileName)
    {
        var trimmedItem = (listedItem ?? string.Empty).Trim().Trim('"').Replace('\\', '/');
        if (trimmedItem.StartsWith(remoteSessionDir, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedItem;
        }

        var normalizedDir = remoteSessionDir.Replace('\\', '/').TrimEnd('/');
        return $"{normalizedDir}/{fileName}";
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static bool HasPulledQuestBackupFolder(string? folderPath)
    {
        if (!CompanionOperatorDataLayout.TryResolveExistingDirectory(folderPath, out var resolvedFolderPath))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFileSystemEntries(resolvedFolderPath).Any();
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteEmptyDirectory(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(folderPath).Any())
            {
                Directory.Delete(folderPath, recursive: false);
            }
        }
        catch
        {
        }
    }

    private async Task OpenQuestBackupFolderAsync(string folderPath, string actionLabel, string unavailableSummary, string unavailableDetail)
    {
        if (!HasPulledQuestBackupFolder(folderPath))
        {
            var detail = BuildQuestBackupFolderUnavailableDetail(folderPath, unavailableDetail);
            await ApplyOutcomeAsync(
                actionLabel,
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    unavailableSummary,
                    detail,
                    Items: string.IsNullOrWhiteSpace(folderPath) ? [] : [folderPath])).ConfigureAwait(false);
            return;
        }

        await OpenValidationCaptureFolderAsync(folderPath, actionLabel, unavailableSummary, unavailableDetail).ConfigureAwait(false);
    }

    private static string BuildQuestBackupFolderUnavailableDetail(string? folderPath, string fallbackDetail)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return $"{fallbackDetail} No pulled Quest backup path was recorded for this session yet.";
        }

        var normalizedFolderPath = NormalizeHostVisibleOperatorPath(folderPath);
        if (!Directory.Exists(normalizedFolderPath))
        {
            return $"{fallbackDetail} Expected folder: {normalizedFolderPath}. The Quest pullback either never completed or the folder was removed afterward.";
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(normalizedFolderPath).Any())
            {
                return $"{fallbackDetail} The folder exists but is empty: {normalizedFolderPath}. The Quest pullback did not produce any files.";
            }
        }
        catch (Exception exception)
        {
            return $"{fallbackDetail} The shell could not inspect {normalizedFolderPath}: {exception.Message}";
        }

        return fallbackDetail;
    }

    public Task RecenterAsync()
        => SendStudyTwinCommandAsync(_study.Controls.RecenterCommandActionId, "Recenter");

    private Task ToggleParticlesAsync()
        => GetCurrentReportedParticleVisibility() == true
            ? !CanToggleParticles
                ? Task.CompletedTask
                : ParticlesOffAsync()
            : !CanToggleParticles
                ? Task.CompletedTask
                : ParticlesOnAsync();

    public async Task ParticlesOnAsync()
    {
        var outcome = await SendStudyTwinCommandCoreAsync(_study.Controls.ParticleVisibleOnActionId, "Particles On").ConfigureAwait(false);
        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            await DispatchAsync(() =>
            {
                _workflowGuideParticlesOnVerified = true;
                UpdateWorkflowGuideState();
            }).ConfigureAwait(false);
        }
    }

    public async Task ParticlesOffAsync()
    {
        var outcome = await SendStudyTwinCommandCoreAsync(_study.Controls.ParticleVisibleOffActionId, "Particles Off").ConfigureAwait(false);
        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            await DispatchAsync(() =>
            {
                _workflowGuideParticlesOffVerified = true;
                UpdateWorkflowGuideState();
            }).ConfigureAwait(false);
        }
    }

    private Task SendStudyTwinCommandAsync(string actionId, string label)
        => SendStudyTwinCommandCoreAsync(actionId, label);

    private async Task<OperationOutcome> SendStudyTwinCommandCoreAsync(string actionId, string label)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            var unavailableOutcome = new OperationOutcome(
                OperationOutcomeKind.Warning,
                $"{label} unavailable.",
                $"The current public runtime does not expose a `{label}` twin command yet.");
            await DispatchAsync(() => AppendLog(
                OperatorLogLevel.Warning,
                unavailableOutcome.Summary,
                unavailableOutcome.Detail)).ConfigureAwait(false);
            return unavailableOutcome;
        }

        await TryWakeHeadsetBeforeStudyActionAsync(label).ConfigureAwait(false);
        var previousConfirmation = await DispatchAsync(() => CaptureCommandConfirmation(actionId)).ConfigureAwait(false);
        var outcome = await _twinBridge.SendCommandAsync(new TwinModeCommand(actionId, label)).ConfigureAwait(false);
        await ApplyOutcomeAsync(label, outcome).ConfigureAwait(false);

        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            var request = CreateCommandRequest(actionId, label, previousConfirmation, outcome);
            await DispatchAsync(() =>
            {
                RememberCommandRequest(request);
                TryRecordCommandRequestEvent(request, outcome);
                UpdateRecenterCard();
                UpdateParticlesCard();
                RefreshFocusRows(forceRebuild: true);
            }).ConfigureAwait(false);
        }

        return outcome;
    }

    private async Task<OperationOutcome?> TryWakeHeadsetBeforeStudyActionAsync(string actionLabel)
    {
        var shouldWake = await DispatchAsync(() =>
        {
            var selector = ResolveHeadsetActionSelector();
            if (string.IsNullOrWhiteSpace(selector))
            {
                return false;
            }

            return _headsetStatus is null
                || _headsetStatus.IsAwake == false
                || _headsetStatus.IsInWakeLimbo
                || IsHeadsetWakeBlockedByLockScreen();
        }).ConfigureAwait(false);

        if (!shouldWake)
        {
            return null;
        }

        var wakeOutcome = await _questService
            .RunUtilityAsync(QuestUtilityAction.Wake, allowWakeResumeTarget: false)
            .ConfigureAwait(false);
        if (wakeOutcome.Kind is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
        {
            await DispatchAsync(() => AppendLog(
                OperatorLogLevel.Warning,
                $"Wake before {actionLabel} needs attention.",
                wakeOutcome.Detail)).ConfigureAwait(false);
        }

        if (wakeOutcome.Kind != OperationOutcomeKind.Failure)
        {
            await Task.Delay(150).ConfigureAwait(false);
        }

        return wakeOutcome;
    }

    private async Task HandleConnectionOutcomeAsync(string actionLabel, OperationOutcome outcome, bool refreshAfter = true)
    {
        await DispatchAsync(() =>
        {
            RecordConnectionOutcome(actionLabel, outcome);
            ConnectionSummary = outcome.Summary;
            if (!string.IsNullOrWhiteSpace(outcome.Endpoint))
            {
                EndpointDraft = outcome.Endpoint;
                SaveSession(endpoint: outcome.Endpoint);
            }

            LastActionLabel = actionLabel;
            LastActionDetail = outcome.Detail;
            LastActionLevel = outcome.Kind;
            AppendLog(MapLevel(outcome.Kind), outcome.Summary, outcome.Detail);
            UpdateConnectionCardState();
            UpdateWorkflowGuideState();
        }).ConfigureAwait(false);

        if (refreshAfter)
        {
            await RefreshStatusAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> ConnectQuestCoreAsync(bool warnWhenMissingEndpoint)
    {
        var endpoint = await DispatchAsync(() => EndpointDraft.Trim()).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            if (warnWhenMissingEndpoint)
            {
                await DispatchAsync(() => AppendLog(
                    OperatorLogLevel.Warning,
                    "Quest connect blocked.",
                    "Enter an IP:port endpoint first.")).ConfigureAwait(false);
            }

            return false;
        }

        var outcome = await _questService.ConnectAsync(endpoint).ConfigureAwait(false);
        await HandleConnectionOutcomeAsync("Connect Quest", outcome).ConfigureAwait(false);
        return true;
    }

    private async Task RefreshLocalApkStatusAsync()
    {
        var stagedPath = await DispatchAsync(() => StagedApkPath).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(stagedPath) || !File.Exists(stagedPath))
        {
            await DispatchAsync(() =>
            {
                LocalApkHash = string.Empty;
                LocalApkLevel = OperationOutcomeKind.Preview;
                LocalApkSummary = "Waiting for the bundled Sussex APK.";
                LocalApkDetail = string.IsNullOrWhiteSpace(stagedPath)
                    ? _study.App.Notes
                    : CanChooseStudyApk
                        ? $"Saved study APK path not found: {stagedPath}"
                        : $"Bundled Sussex APK not found: {stagedPath}";
                OnPropertyChanged(nameof(HasValidPinnedLocalApk));
            }).ConfigureAwait(false);
            return;
        }

        try
        {
            var hash = await ComputeFileSha256Async(stagedPath).ConfigureAwait(false);
            var matches = HashMatches(hash, _study.App.Sha256);

            await DispatchAsync(() =>
            {
                LocalApkHash = hash;
                LocalApkLevel = matches ? OperationOutcomeKind.Success : OperationOutcomeKind.Failure;
                LocalApkSummary = matches
                    ? CanChooseStudyApk
                        ? "Selected study APK matches the Sussex study hash."
                        : "Bundled Sussex APK matches the Sussex study hash."
                    : CanChooseStudyApk
                        ? "Selected study APK does not match the Sussex study hash."
                        : "Bundled Sussex APK does not match the Sussex study hash.";
                LocalApkDetail = string.Join(
                    Environment.NewLine,
                    $"Path: {stagedPath}",
                    $"File SHA256: {hash}",
                    $"Expected SHA256: {_study.App.Sha256}");
                OnPropertyChanged(nameof(HasValidPinnedLocalApk));
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await DispatchAsync(() =>
            {
                LocalApkHash = string.Empty;
                LocalApkLevel = OperationOutcomeKind.Failure;
                LocalApkSummary = CanChooseStudyApk
                    ? "Could not verify the selected study APK."
                    : "Could not verify the bundled Sussex APK.";
                LocalApkDetail = ex.Message;
                OnPropertyChanged(nameof(HasValidPinnedLocalApk));
            }).ConfigureAwait(false);
        }
    }

    private async Task<bool> RefreshDeviceSnapshotBundleAsync(
        bool forceProximity,
        bool includeHostWifiStatus = true,
        bool forceInstalledAppStatusRefresh = true)
    {
        var entered = await DispatchAsync(() =>
        {
            if (_deviceSnapshotRefreshPending)
            {
                return false;
            }

            _deviceSnapshotRefreshPending = true;
            return true;
        }).ConfigureAwait(false);

        if (!entered)
        {
            return false;
        }

        try
        {
            await DispatchAsync(() => _deviceSnapshotRefreshPhase = "headset-status").ConfigureAwait(false);
            await RefreshHeadsetStatusAsync(includeHostWifiStatus).ConfigureAwait(false);
            if (ShouldRefreshInstalledAppStatusForSnapshot(
                    forceRefresh: forceInstalledAppStatusRefresh,
                    currentStatus: _installedAppStatus,
                    currentStagedApkPath: _stagedApkPath,
                    lastQueriedStagedApkPath: _lastInstalledAppStatusStagedApkPath))
            {
                await DispatchAsync(() => _deviceSnapshotRefreshPhase = "installed-app-status").ConfigureAwait(false);
                await RefreshInstalledAppStatusAsync().ConfigureAwait(false);
            }

            await DispatchAsync(() => _deviceSnapshotRefreshPhase = "device-profile-status").ConfigureAwait(false);
            await RefreshDeviceProfileStatusAsync().ConfigureAwait(false);

            await DispatchAsync(() => _deviceSnapshotRefreshPhase = "proximity-status").ConfigureAwait(false);
            await RefreshProximityStatusAsync(force: forceProximity).ConfigureAwait(false);

            await DispatchAsync(() => _deviceSnapshotRefreshPhase = "ui-update").ConfigureAwait(false);
            await DispatchAsync(() =>
            {
                UpdatePinnedBuildStatus();
                UpdateDeviceProfileRows();
                RefreshBenchToolsStatus();
                RefreshLiveTwinState();
                UpdateHeadsetSnapshotModeState();
            }).ConfigureAwait(false);
        }
        finally
        {
            await DispatchAsync(() =>
            {
                _deviceSnapshotRefreshPending = false;
                _deviceSnapshotRefreshPhase = string.Empty;
            }).ConfigureAwait(false);
        }

        return true;
    }

    private static string BuildSnapshotInlineSuffix(DateTimeOffset? snapshotAtUtc)
        => snapshotAtUtc.HasValue
            ? $" | snapshot {snapshotAtUtc.Value.ToLocalTime():HH:mm:ss}"
            : string.Empty;

    private void UpdateHeadsetSnapshotModeState()
    {
        HeadsetSnapshotModeLevel = RegularAdbSnapshotEnabled
            ? OperationOutcomeKind.Warning
            : OperationOutcomeKind.Success;
        HeadsetSnapshotModeSummary = RegularAdbSnapshotEnabled
            ? $"Regular ADB readouts are on for this session. Useful for debugging, but they add headset-query overhead during a live run. {AutomaticInstalledBuildHashingDetail}"
            : "Regular ADB readouts are off. Use Refresh Snapshot when you want a fresh device state. Automatic snapshot polling stays off after relaunch until you enable it again.";
        DeviceSnapshotTimestampLabel = _lastDeviceSnapshotAtUtc.HasValue
            ? $"Last headset snapshot {_lastDeviceSnapshotAtUtc.Value.ToLocalTime():HH:mm:ss}."
            : "No headset snapshot received yet.";
    }

    private void UpdateDeviceSnapshotTimerState()
    {
        if (_deviceSnapshotRefreshTimer is null)
        {
            return;
        }

        if (RegularAdbSnapshotEnabled)
        {
            if (!_deviceSnapshotRefreshTimer.IsEnabled)
            {
                _deviceSnapshotRefreshTimer.Start();
            }

            return;
        }

        _deviceSnapshotRefreshTimer.Stop();
    }

    private void OnDeviceSnapshotRefreshTimerTick(object? sender, EventArgs e)
    {
        _ = RefreshDeviceSnapshotBundleAsync(
            forceProximity: true,
            includeHostWifiStatus: false,
            forceInstalledAppStatusRefresh: false);
    }

    private void OnRecordingSampleTimerTick(object? sender, EventArgs e)
    {
        if (_activeRecordingSession is null)
        {
            _recordingSampleTimer?.Stop();
            return;
        }

        var recordedAtUtc = DateTimeOffset.UtcNow;
        TryRecordLiveTwinState(recordedAtUtc);
        TryRecordActiveSessionCommandObservations(recordedAtUtc);
    }

    private async Task RefreshInstalledAppStatusAsync()
    {
        var normalizedStagedApkPath = await DispatchAsync(() => NormalizeInstalledAppStatusStagedApkPath(StagedApkPath)).ConfigureAwait(false);
        var target = CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false));
        _installedAppStatus = await _questService.QueryInstalledAppAsync(target).ConfigureAwait(false);

        await DispatchAsync(() =>
        {
            _lastInstalledAppStatusStagedApkPath = normalizedStagedApkPath;
            var snapshotSuffix = _headsetStatus?.IsConnected == true
                ? BuildSnapshotInlineSuffix(_lastDeviceSnapshotAtUtc)
                : string.Empty;
            var runtimeHashFallback = GetReportedRuntimeApkHash();
            var effectiveInstalledHash = !string.IsNullOrWhiteSpace(_installedAppStatus?.InstalledSha256)
                ? _installedAppStatus!.InstalledSha256
                : runtimeHashFallback;

            if (_installedAppStatus is null)
            {
                InstalledApkLevel = OperationOutcomeKind.Preview;
                InstalledApkSummary = "Installed build has not been checked yet.";
                InstalledApkDetail = "Refresh the study status after connecting to the headset.";
            }
            else if (!_installedAppStatus.IsInstalled && !string.IsNullOrWhiteSpace(runtimeHashFallback))
            {
                InstalledApkLevel = HashMatches(runtimeHashFallback, _study.App.Sha256)
                    ? OperationOutcomeKind.Success
                    : OperationOutcomeKind.Warning;
                InstalledApkSummary = $"{_study.App.Label} runtime hash reported by the headset.{snapshotSuffix}";
                InstalledApkDetail =
                    string.Join(
                        Environment.NewLine,
                        $"Runtime hash fallback: {runtimeHashFallback}",
                        "ADB install-path query did not return a clean result.",
                        _installedAppStatus.Summary,
                        FormatSemicolonSeparatedDetail(_installedAppStatus.Detail));
            }
            else
            {
                InstalledApkLevel = !_installedAppStatus.IsInstalled
                    ? OperationOutcomeKind.Warning
                    : string.IsNullOrWhiteSpace(effectiveInstalledHash)
                        ? OperationOutcomeKind.Warning
                        : HashMatches(effectiveInstalledHash, _study.App.Sha256)
                            ? OperationOutcomeKind.Success
                            : OperationOutcomeKind.Warning;
                InstalledApkSummary = _installedAppStatus.Summary + snapshotSuffix;
                InstalledApkDetail = string.IsNullOrWhiteSpace(_installedAppStatus.InstalledSha256) &&
                                     !string.IsNullOrWhiteSpace(runtimeHashFallback)
                    ? string.Join(
                        Environment.NewLine,
                        FormatSemicolonSeparatedDetail(_installedAppStatus.Detail),
                        $"Runtime hash fallback: {runtimeHashFallback}")
                    : FormatSemicolonSeparatedDetail(_installedAppStatus.Detail);
            }
        }).ConfigureAwait(false);
    }

    private async Task RefreshDeviceProfileStatusAsync()
    {
        _deviceProfileStatus = await _questService.QueryDeviceProfileStatusAsync(CreatePinnedDeviceProfile()).ConfigureAwait(false);

        await DispatchAsync(() =>
        {
            var snapshotSuffix = _headsetStatus?.IsConnected == true
                ? BuildSnapshotInlineSuffix(_lastDeviceSnapshotAtUtc)
                : string.Empty;

            if (_deviceProfileStatus is null)
            {
                DeviceProfileLevel = OperationOutcomeKind.Preview;
                DeviceProfileSummary = "Pinned device profile has not been checked yet.";
                DeviceProfileDetail = "Refresh the study status after connecting to the headset.";
                UpdateDeviceProfileRows();
                UpdateDeviceProfileCardState();
                return;
            }

            DeviceProfileLevel = _deviceProfileStatus.IsActive ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning;
            DeviceProfileSummary = _deviceProfileStatus.Summary + snapshotSuffix;
            DeviceProfileDetail = _deviceProfileStatus.Detail;
            UpdateDeviceProfileRows();
            UpdateDeviceProfileCardState();
        }).ConfigureAwait(false);
    }

    private async Task RefreshHeadsetStatusAsync(bool includeHostWifiStatus = true)
    {
        _headsetStatus = await _questService.QueryHeadsetStatusAsync(
            CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false)),
            remoteOnlyControlEnabled: true,
            includeHostWifiStatus: includeHostWifiStatus).ConfigureAwait(false);

        await DispatchAsync(() =>
        {
            if (_headsetStatus is null)
            {
                return;
            }

            if (_headsetStatus.IsConnected)
            {
                _lastDeviceSnapshotAtUtc = _headsetStatus.Timestamp;
            }

            var snapshotSuffix = _headsetStatus.IsConnected
                ? BuildSnapshotInlineSuffix(_lastDeviceSnapshotAtUtc)
                : string.Empty;

            var studyRuntimeForeground = string.Equals(_headsetStatus.ForegroundPackageId, _study.App.PackageId, StringComparison.OrdinalIgnoreCase);
            ConnectionSummary = _headsetStatus.ConnectionLabel;
            if (!_headsetStatus.IsConnected)
            {
                QuestStatusLevel = OperationOutcomeKind.Warning;
                QuestStatusSummary = _headsetStatus.Summary;
                QuestStatusDetail = _headsetStatus.Detail;
            }
            else if (studyRuntimeForeground)
            {
                QuestStatusLevel = OperationOutcomeKind.Success;
                QuestStatusSummary = _headsetStatus.Summary;
                QuestStatusDetail = _headsetStatus.Detail;
            }
            else
            {
                var foregroundPackage = string.IsNullOrWhiteSpace(_headsetStatus.ForegroundPackageId)
                    ? "unknown"
                    : _headsetStatus.ForegroundPackageId;
                QuestStatusLevel = OperationOutcomeKind.Warning;
                QuestStatusSummary = $"{_study.App.Label} is not active.";
                QuestStatusDetail = $"Connected to the headset, but the active app is {foregroundPackage}. Refresh the active app or relaunch the Sussex APK before relying on session controls.";
            }

            HeadsetModel = string.IsNullOrWhiteSpace(_headsetStatus.DeviceModel) ? "Quest" : _headsetStatus.DeviceModel;
            BatteryPercent = Math.Clamp(_headsetStatus.BatteryLevel ?? 0, 0, 100);
            HeadsetBatteryLabel = BuildHeadsetBatteryLabel(_headsetStatus) + snapshotSuffix;
            HeadsetSoftwareVersionLabel = BuildHeadsetSoftwareVersionLabel(_headsetStatus) + snapshotSuffix;
            HeadsetPerformanceLabel = $"CPU {(_headsetStatus.CpuLevel?.ToString() ?? "n/a")} / GPU {(_headsetStatus.GpuLevel?.ToString() ?? "n/a")}{snapshotSuffix}";
            HeadsetForegroundLabel = string.IsNullOrWhiteSpace(_headsetStatus.ForegroundPackageId)
                ? $"Active app n/a{snapshotSuffix}"
                : $"{_headsetStatus.ForegroundPackageId}{snapshotSuffix}";
            UpdateConnectionCardState();
            UpdateHeadsetSnapshotModeState();
            RefreshBenchToolsStatus();
            RefreshStudyRuntimeLaunchState();
        }).ConfigureAwait(false);

        await DispatchAsync(() => QueueQuestWifiTransportDiagnosticsRefresh()).ConfigureAwait(false);

        if (_startupHotloadSyncDeferredUntilStudyStops &&
            _headsetStatus is { IsConnected: true, IsTargetForeground: false })
        {
            var target = CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false));
            _ = EnsurePinnedStartupHotloadStateAsync(
                target,
                forceWhenStudyNotForeground: true,
                reasonLabel: "after Sussex stopped");
        }
    }

    private void UpdatePinnedBuildStatus()
    {
        var baseline = _study.App.VerificationBaseline;
        var surfaceVerifiedBaseline = SurfaceVerifiedBaselineInShell && baseline is not null;
        var installedHash = InstalledApkHash;
        var installedMatchesPinnedHash = HashMatches(installedHash, _study.App.Sha256);
        var hasInstalledHash = !string.IsNullOrWhiteSpace(installedHash);
        var hasInstalledEvidence = _installedAppStatus?.IsInstalled == true || hasInstalledHash;
        var hasConnectedHeadset = _headsetStatus?.IsConnected == true;
        var hasReadyPinnedApkOnHeadset = hasConnectedHeadset && hasInstalledEvidence && installedMatchesPinnedHash;
        var hasSoftwareIdentity =
            !string.IsNullOrWhiteSpace(_headsetStatus?.SoftwareReleaseOrCodename) &&
            !string.IsNullOrWhiteSpace(_headsetStatus?.SoftwareBuildId);
        var reportedRuntimeEnvironmentHash = GetReportedRuntimeEnvironmentHash();
        var hasReportedRuntimeEnvironmentHash = !string.IsNullOrWhiteSpace(reportedRuntimeEnvironmentHash);
        var reportedRuntimeDeviceProfileId = GetReportedRuntimeDeviceProfileId();
        var softwareMatchesBaseline = false;
        var displayMatchesBaseline = true;

        if (surfaceVerifiedBaseline)
        {
            var activeBaseline = baseline!;
            var activeHeadsetStatus = _headsetStatus;
            var baselineMatchesPinnedHash = HashMatches(activeBaseline.ApkSha256, _study.App.Sha256);
            softwareMatchesBaseline =
                hasSoftwareIdentity &&
                activeHeadsetStatus is not null &&
                string.Equals(activeHeadsetStatus.SoftwareReleaseOrCodename, activeBaseline.SoftwareVersion, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(activeHeadsetStatus.SoftwareBuildId, activeBaseline.BuildId, StringComparison.OrdinalIgnoreCase);
            displayMatchesBaseline =
                string.IsNullOrWhiteSpace(activeBaseline.DisplayId) ||
                string.Equals(activeHeadsetStatus?.SoftwareDisplayId, activeBaseline.DisplayId, StringComparison.OrdinalIgnoreCase);
            var deviceProfileIdMatchesBaseline =
                string.IsNullOrWhiteSpace(activeBaseline.DeviceProfileId) ||
                string.Equals(activeBaseline.DeviceProfileId, _study.DeviceProfile.Id, StringComparison.OrdinalIgnoreCase);
            var runtimeDeviceProfileIdMatchesBaseline =
                string.IsNullOrWhiteSpace(reportedRuntimeDeviceProfileId) ||
                string.IsNullOrWhiteSpace(activeBaseline.DeviceProfileId) ||
                string.Equals(reportedRuntimeDeviceProfileId, activeBaseline.DeviceProfileId, StringComparison.OrdinalIgnoreCase);
            var fingerprintMatches =
                hasConnectedHeadset &&
                hasInstalledEvidence &&
                installedMatchesPinnedHash &&
                hasSoftwareIdentity &&
                deviceProfileIdMatchesBaseline &&
                StudyVerificationFingerprint.Matches(
                    activeBaseline,
                    _study.App.PackageId,
                    installedHash,
                    _headsetStatus!.SoftwareReleaseOrCodename,
                    _headsetStatus.SoftwareBuildId,
                    _study.DeviceProfile.Id,
                    _headsetStatus.SoftwareDisplayId);
            var runtimeEnvironmentMatchesBaseline =
                hasReadyPinnedApkOnHeadset &&
                hasReportedRuntimeEnvironmentHash &&
                runtimeDeviceProfileIdMatchesBaseline &&
                string.Equals(reportedRuntimeEnvironmentHash, activeBaseline.EnvironmentHash, StringComparison.OrdinalIgnoreCase);

            if (!baselineMatchesPinnedHash)
            {
                PinnedBuildLevel = OperationOutcomeKind.Failure;
                PinnedBuildSummary = "Pinned Sussex APK changed after the latest verified run.";
            }
            else if (_installedAppStatus?.IsInstalled == true && hasInstalledHash && !installedMatchesPinnedHash)
            {
                PinnedBuildLevel = OperationOutcomeKind.Failure;
                PinnedBuildSummary = "Headset APK does not match the latest verified Sussex build.";
            }
            else if (hasReadyPinnedApkOnHeadset)
            {
                PinnedBuildLevel = OperationOutcomeKind.Success;
                PinnedBuildSummary = fingerprintMatches || runtimeEnvironmentMatchesBaseline
                    ? "Headset matches the latest verified Sussex build."
                    : "Pinned Sussex APK matches the recorded Sussex build.";
            }
            else
            {
                PinnedBuildLevel = OperationOutcomeKind.Warning;
                PinnedBuildSummary = "Latest verified Sussex build recorded. Refresh the headset snapshot to compare it fully.";
            }
        }
        else if (hasInstalledEvidence)
        {
            if (installedMatchesPinnedHash)
            {
                PinnedBuildLevel = OperationOutcomeKind.Success;
                PinnedBuildSummary = "Sussex APK matches the pinned hash.";
            }
            else if (!hasInstalledHash)
            {
                PinnedBuildLevel = OperationOutcomeKind.Warning;
                PinnedBuildSummary = "Sussex runtime is installed, but the headset APK hash could not be verified.";
            }
            else
            {
                PinnedBuildLevel = OperationOutcomeKind.Failure;
                PinnedBuildSummary = "A different Sussex APK build is installed on the headset.";
            }
        }
        else if (HashMatches(LocalApkHash, _study.App.Sha256))
        {
            PinnedBuildLevel = OperationOutcomeKind.Warning;
            PinnedBuildSummary = CanChooseStudyApk
                ? "Approved Sussex APK is ready to install."
                : "Bundled Sussex APK is ready to install.";
        }
        else if (!string.IsNullOrWhiteSpace(StagedApkPath) && File.Exists(StagedApkPath))
        {
            PinnedBuildLevel = OperationOutcomeKind.Failure;
            PinnedBuildSummary = CanChooseStudyApk
                ? "Selected study APK does not match the Sussex study hash."
                : "Bundled Sussex APK does not match the Sussex study hash.";
        }
        else
        {
            PinnedBuildLevel = OperationOutcomeKind.Preview;
            PinnedBuildSummary = "Waiting for the bundled Sussex APK.";
        }

        var details = new List<string>();
        AddDetailLine(details, "Study package", _study.App.PackageId);
        AddDetailLine(details, "Study version", string.IsNullOrWhiteSpace(_study.App.VersionName) ? "n/a" : _study.App.VersionName);
        AddDetailLine(details, "Study SHA256", _study.App.Sha256);

        if (_lastDeviceSnapshotAtUtc.HasValue)
        {
            AddDetailLine(details, "Last headset snapshot", _lastDeviceSnapshotAtUtc.Value.ToLocalTime().ToString("HH:mm:ss"));
        }

        if (_headsetStatus is not null)
        {
            AddDetailLine(details, "Headset software", BuildCurrentHeadsetVerificationDetail(_headsetStatus));
        }

        if (surfaceVerifiedBaseline)
        {
            details.Add(string.Empty);
            details.Add(BuildVerificationBaselineDetail(baseline!));
        }

        if (surfaceVerifiedBaseline && hasReadyPinnedApkOnHeadset)
        {
            if (hasSoftwareIdentity && !softwareMatchesBaseline)
            {
                details.Add("Current headset OS/build differs from the verified baseline. Sussex currently keeps software identity as advisory-only bench context.");
            }

            if (!displayMatchesBaseline)
            {
                details.Add("Current headset display id differs from the verified baseline. Sussex currently keeps display identity as advisory-only bench context.");
            }
        }

        PinnedBuildDetail = string.Join(Environment.NewLine, details.Where(detail => detail is not null));
        UpdatePinnedBuildCardState();
    }

    private static string BuildVerificationBaselineDetail(StudyVerificationBaseline baseline)
    {
        var recordedAt = baseline.VerifiedAtUtc.HasValue
            ? baseline.VerifiedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "unknown time";
        var displayPart = string.IsNullOrWhiteSpace(baseline.DisplayId)
            ? string.Empty
            : $" Display {baseline.DisplayId}.";

        return
            $"Latest verified baseline: OS {FormatSoftwareIdentity(baseline.SoftwareVersion, baseline.BuildId)}.{displayPart} " +
            $"Device profile {baseline.DeviceProfileId}. Environment hash {baseline.EnvironmentHash}. " +
            $"Recorded {recordedAt} via {baseline.VerifiedBy}.";
    }

    private static string BuildCurrentHeadsetVerificationDetail(HeadsetAppStatus status)
    {
        var softwareIdentity = FormatSoftwareIdentity(status.SoftwareReleaseOrCodename, status.SoftwareBuildId);
        if (string.IsNullOrWhiteSpace(status.SoftwareReleaseOrCodename) && string.IsNullOrWhiteSpace(status.SoftwareBuildId))
        {
            return "n/a";
        }

        return string.IsNullOrWhiteSpace(status.SoftwareDisplayId)
            ? softwareIdentity
            : $"{softwareIdentity} | display {status.SoftwareDisplayId}";
    }

    private static string FormatSoftwareIdentity(string? softwareVersion, string? buildId)
        => string.IsNullOrWhiteSpace(buildId)
            ? (string.IsNullOrWhiteSpace(softwareVersion) ? "n/a" : softwareVersion.Trim())
            : string.IsNullOrWhiteSpace(softwareVersion)
                ? $"build {buildId.Trim()}"
                : $"{softwareVersion.Trim()} | build {buildId.Trim()}";

    private static void AddDetailLine(List<string> details, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        details.Add($"{label}: {value}");
    }

    private static string FormatSemicolonSeparatedDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        var lines = detail
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(line => line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        return lines.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, lines);
    }

    private void UpdateDeviceProfileRows()
    {
        DeviceProfileRows.Clear();

        if (_deviceProfileStatus?.Properties is not { Count: > 0 } properties)
        {
            return;
        }

        foreach (var property in properties)
        {
            DeviceProfileRows.Add(new StudyStatusRow(
                Label: FormatWorkflowDeviceProfileLabel(property.Key),
                Key: property.Key,
                Value: string.IsNullOrWhiteSpace(property.ReportedValue) ? "Not reported" : property.ReportedValue,
                Expected: property.ExpectedValue,
                Detail: BuildWorkflowDeviceProfileDetail(property),
                Level: property.Matches ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning));
        }
    }

    private static string FormatWorkflowDeviceProfileLabel(string key)
        => key switch
        {
            "debug.oculus.cpuLevel" => "CPU level",
            "debug.oculus.gpuLevel" => "GPU level",
            "viscereality.screen_brightness_percent" => "Screen brightness (%)",
            "viscereality.media_volume_music" => "Media volume",
            "viscereality.minimum_headset_battery_percent" => "Minimum headset battery (%)",
            "viscereality.minimum_right_controller_battery_percent" => "Minimum right controller battery (%)",
            _ => key
        };

    private static string BuildWorkflowDeviceProfileDetail(DevicePropertyStatus property)
        => property.Key switch
        {
            "viscereality.media_volume_music" when property.Matches
                => "Pinned Quest media volume matches.",
            "viscereality.media_volume_music"
                => "Pinned Quest media volume differs from the current headset value. If Apply Device Profile does not fix it, set volume manually on the headset and refresh.",
            _ => property.Matches
                ? "Pinned Quest property matches."
                : "Pinned Quest property differs from the current headset value."
        };

    private void RecordConnectionOutcome(string actionLabel, OperationOutcome outcome)
    {
        _lastConnectionActionLabel = actionLabel;
        _lastConnectionLevel = outcome.Kind;
        _lastConnectionDetail = outcome.Detail;
    }

    private void UpdateConnectionCardState()
    {
        var selector = _headsetStatus?.ConnectionLabel ?? string.Empty;
        var isWifiAdbActive = _headsetStatus?.IsWifiAdbTransport == true;
        var lastConnectionFailed = _lastConnectionLevel == OperationOutcomeKind.Failure;
        var wifiPreparedButNotConnected =
            string.Equals(_lastConnectionActionLabel, "Enable Wi-Fi ADB", StringComparison.Ordinal)
            && _lastConnectionLevel == OperationOutcomeKind.Success
            && !isWifiAdbActive;
        var headsetWifiLabel = FormatHeadsetWifiLabel(_headsetStatus)
            + (_headsetStatus?.IsConnected == true ? BuildSnapshotInlineSuffix(_lastDeviceSnapshotAtUtc) : string.Empty);
        var hostWifiLabel = string.IsNullOrWhiteSpace(_headsetStatus?.HostWifiSsid)
            ? "This PC Wi-Fi: n/a"
            : $"This PC Wi-Fi: {_headsetStatus.HostWifiSsid}";

        HeadsetWifiSummary = headsetWifiLabel;
        HostWifiSummary = hostWifiLabel;
        WifiNetworkMatchLevel = OperationOutcomeKind.Preview;
        WifiNetworkMatchSummary = "Wi-Fi match unknown.";

        if (_headsetStatus is null)
        {
            ConnectionCardLevel = OperationOutcomeKind.Warning;
            ConnectionTransportLevel = OperationOutcomeKind.Warning;
            ConnectionTransportSummary = "ADB over Wi-Fi: unknown.";
            ConnectionTransportDetail = "Connect the Quest and switch to Wi-Fi ADB before relying on remote-only study control.";
            return;
        }

        if (!_headsetStatus.IsConnected)
        {
            ConnectionCardLevel = lastConnectionFailed ? OperationOutcomeKind.Failure : OperationOutcomeKind.Warning;
            ConnectionTransportLevel = ConnectionCardLevel;
            ConnectionTransportSummary = lastConnectionFailed
                ? "ADB over Wi-Fi: unavailable."
                : "ADB over Wi-Fi: off.";
            ConnectionTransportDetail = lastConnectionFailed && !string.IsNullOrWhiteSpace(_lastConnectionDetail)
                ? _lastConnectionDetail
                : "Probe USB or connect a known Wi-Fi ADB endpoint before starting the session.";
            return;
        }

        var routedTopologyAccepted = _questWifiTransportDiagnostics?.RoutedTopologyAccepted == true;
        WifiNetworkMatchLevel = _headsetStatus.WifiSsidMatchesHost switch
        {
            true => OperationOutcomeKind.Success,
            false when routedTopologyAccepted => OperationOutcomeKind.Success,
            false => OperationOutcomeKind.Failure,
            _ when routedTopologyAccepted => OperationOutcomeKind.Success,
            _ => OperationOutcomeKind.Warning
        };
        WifiNetworkMatchSummary = _headsetStatus.WifiSsidMatchesHost switch
        {
            true => "Wi-Fi names match.",
            false when routedTopologyAccepted => "Different SSIDs, but the routed Quest path is valid.",
            false => "Wi-Fi names do not match.",
            _ when routedTopologyAccepted => "Routed Quest path is valid.",
            _ => "Wi-Fi match unknown."
        };

        if (isWifiAdbActive)
        {
            ConnectionCardLevel = _headsetStatus.WifiSsidMatchesHost == false && !routedTopologyAccepted
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success;
            ConnectionTransportLevel = _headsetStatus.WifiSsidMatchesHost == false && !routedTopologyAccepted
                ? OperationOutcomeKind.Failure
                : OperationOutcomeKind.Success;
            ConnectionTransportSummary = _headsetStatus.WifiSsidMatchesHost == false && !routedTopologyAccepted
                ? "ADB over Wi-Fi: on, but network names do not match."
                : routedTopologyAccepted
                    ? "ADB over Wi-Fi: on over a valid routed host path."
                : "ADB over Wi-Fi: on.";
            ConnectionTransportDetail = _headsetStatus.WifiSsidMatchesHost switch
            {
                true => $"Remote control is using {selector}, and the headset Wi-Fi matches this PC.",
                false when routedTopologyAccepted => $"Remote control is using {selector}, and the Quest endpoint is reachable over the current routed host path. Matching PC Wi-Fi names are not required on this topology.",
                false => $"Remote control is using {selector}, but the headset Wi-Fi does not match this PC.",
                _ => $"Remote control is using {selector}. Verify the headset Wi-Fi name before starting the session."
            };
            return;
        }

        ConnectionCardLevel = OperationOutcomeKind.Warning;
        ConnectionTransportLevel = OperationOutcomeKind.Warning;
        ConnectionTransportSummary = wifiPreparedButNotConnected
            ? "ADB over Wi-Fi: off. USB only, but Wi-Fi is prepared."
            : "ADB over Wi-Fi: off. USB only.";
        ConnectionTransportDetail = wifiPreparedButNotConnected
            ? "Run Connect Quest to switch the session from USB to the prepared Wi-Fi ADB endpoint."
            : "Enable Wi-Fi ADB and reconnect before leaving the headset unattended or relying on remote-only control.";
    }

    private static string FormatHeadsetWifiLabel(HeadsetAppStatus? status)
    {
        if (status is null)
        {
            return "Headset Wi-Fi: n/a";
        }

        if (!string.IsNullOrWhiteSpace(status.HeadsetWifiSsid) && !string.IsNullOrWhiteSpace(status.HeadsetWifiIpAddress))
        {
            return $"Headset Wi-Fi: {status.HeadsetWifiSsid} ({status.HeadsetWifiIpAddress})";
        }

        if (!string.IsNullOrWhiteSpace(status.HeadsetWifiSsid))
        {
            return $"Headset Wi-Fi: {status.HeadsetWifiSsid}";
        }

        if (!string.IsNullOrWhiteSpace(status.HeadsetWifiIpAddress))
        {
            return $"Headset Wi-Fi IP: {status.HeadsetWifiIpAddress}";
        }

        return "Headset Wi-Fi: n/a";
    }

    private static string FormatAutoSleepLabel(int? autoSleepTimeMs)
        => autoSleepTimeMs is > 0
            ? $"{autoSleepTimeMs.Value / 1000d:0.#} s"
            : "n/a";

    private static string FormatBroadcastLabel(QuestProximityStatus status)
    {
        if (string.IsNullOrWhiteSpace(status.LastBroadcastAction))
        {
            return "no recent control broadcast";
        }

        var label = status.LastBroadcastDurationMs.HasValue
            ? $"{status.LastBroadcastAction} ({status.LastBroadcastDurationMs.Value} ms)"
            : status.LastBroadcastAction;

        return status.LastBroadcastAgeSeconds.HasValue
            ? $"{label}, {status.LastBroadcastAgeSeconds.Value:0.#} s ago"
            : label;
    }

    private static string BuildProximityEvidenceLabel(QuestProximityStatus status)
    {
        var virtualState = string.IsNullOrWhiteSpace(status.VirtualState) ? "unknown" : status.VirtualState;
        var headsetState = string.IsNullOrWhiteSpace(status.HeadsetState) ? "unknown" : status.HeadsetState;
        return $"Latest readback {status.RetrievedAtUtc.ToLocalTime():HH:mm:ss}: control {FormatBroadcastLabel(status)} | virtual {virtualState} | headset {headsetState} | auto-sleep {FormatAutoSleepLabel(status.AutoSleepTimeMs)}.";
    }

    private static string BuildProximityControlInterpretation(QuestProximityStatus status)
    {
        if (string.Equals(status.LastBroadcastAction, "prox_close", StringComparison.OrdinalIgnoreCase) &&
            status.LastBroadcastDurationMs is > 0)
        {
            return "The last control broadcast is a timed virtual-close hold, so normal wear-sensor sleep is bypassed.";
        }

        if (string.Equals(status.LastBroadcastAction, "prox_close", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(status.VirtualState, "CLOSE", StringComparison.OrdinalIgnoreCase))
        {
            return "The last control broadcast is a direct prox_close override, so the virtual wear sensor is being held closed until normal proximity is restored.";
        }

        if (string.Equals(status.LastBroadcastAction, "prox_close", StringComparison.OrdinalIgnoreCase))
        {
            return "The last control broadcast requested virtual close, but the latest vrpowermanager readback no longer shows a forced close state.";
        }

        if (string.Equals(status.LastBroadcastAction, "automation_disable", StringComparison.OrdinalIgnoreCase))
        {
            return "The last control broadcast cleared automation control, so the physical wear sensor is in charge again.";
        }

        if (string.Equals(status.VirtualState, "CLOSE", StringComparison.OrdinalIgnoreCase))
        {
            return "Quest vrpowermanager still reports the virtual wear sensor as forced closed, so off-face wear-sensor sleep is bypassed until that state is cleared.";
        }

        return "No recent proximity control broadcast was parsed, so the shell is relying on the latest vrpowermanager state lines.";
    }

    private static string BuildHeadsetPowerEvidenceLabel(HeadsetAppStatus? status)
    {
        if (status is null || string.IsNullOrWhiteSpace(status.PowerStatusDetail))
        {
            return "Power readback unavailable.";
        }

        return $"Power readback: {status.PowerStatusDetail}.";
    }

    private static string BuildHeadsetBatteryLabel(HeadsetAppStatus status)
    {
        var headsetLabel = status.BatteryLevel is null ? "Battery n/a" : $"Battery {status.BatteryLevel}%";
        var controllerLabel = BuildControllerBatteryLabel(status.Controllers);
        return string.IsNullOrWhiteSpace(controllerLabel)
            ? headsetLabel
            : $"{headsetLabel} | {controllerLabel}";
    }

    private static string BuildHeadsetSoftwareVersionLabel(HeadsetAppStatus status)
        => string.IsNullOrWhiteSpace(status.SoftwareVersion)
            ? "Headset OS n/a"
            : $"Headset OS {status.SoftwareVersion}";

    private static string BuildControllerBatteryLabel(IReadOnlyList<QuestControllerStatus>? controllers)
    {
        if (controllers is not { Count: > 0 })
        {
            return string.Empty;
        }

        var left = controllers.FirstOrDefault(controller => string.Equals(controller.HandLabel, "Left", StringComparison.OrdinalIgnoreCase));
        var right = controllers.FirstOrDefault(controller => string.Equals(controller.HandLabel, "Right", StringComparison.OrdinalIgnoreCase));
        return $"Controllers {FormatControllerBatteryLabel("L", left)} / {FormatControllerBatteryLabel("R", right)}";
    }

    private static string FormatControllerBatteryLabel(string shortHandLabel, QuestControllerStatus? controller)
    {
        if (controller is null)
        {
            return $"{shortHandLabel} n/a";
        }

        var batteryLabel = controller.BatteryLevel is null ? "n/a" : $"{controller.BatteryLevel}%";
        var connectionLabel = controller.ConnectionState switch
        {
            "CONNECTED_ACTIVE" => "active",
            "CONNECTED_INACTIVE" => "idle",
            "DISCONNECTED" => "off",
            _ => string.IsNullOrWhiteSpace(controller.ConnectionState)
                ? string.Empty
                : controller.ConnectionState.Replace('_', ' ').ToLowerInvariant()
        };

        return string.IsNullOrWhiteSpace(connectionLabel)
            ? $"{shortHandLabel} {batteryLabel}"
            : $"{shortHandLabel} {batteryLabel} {connectionLabel}";
    }

    private static string BuildHeadsetPowerProximityContext(TrackedQuestProximityState tracked, QuestProximityStatus? liveStatus)
    {
        if (liveStatus?.HoldActive == true)
        {
            return liveStatus.HoldUntilUtc.HasValue
                ? $"Proximity bypass remains active until {liveStatus.HoldUntilUtc.Value.ToLocalTime():HH:mm}, but that bypass only affects wear-sensor sleep and does not keep the display awake after a manual power-button sleep."
                : "Proximity bypass is active, but it only affects wear-sensor sleep and does not keep the display awake after a manual power-button sleep.";
        }

        if (tracked.Known && !tracked.ExpectedEnabled && !tracked.DisableWindowExpired)
        {
            return tracked.DisableUntilUtc.HasValue
                ? $"Companion still expects a proximity bypass until {tracked.DisableUntilUtc.Value.ToLocalTime():HH:mm}, but headset sleep/wake is tracked separately from that hold."
                : "Companion still expects the keep-awake proximity override to be active, but headset sleep/wake is tracked separately from that hold.";
        }

        return "Headset sleep/wake is tracked separately from the proximity setting.";
    }

    private static bool IsProximityBypassExpected(TrackedQuestProximityState tracked, QuestProximityStatus? liveStatus)
        => liveStatus?.HoldActive == true
            || (tracked.Known && !tracked.ExpectedEnabled && !tracked.DisableWindowExpired);

    private void UpdatePinnedBuildCardState()
    {
        PinnedBuildCardLevel = NormalizePreSessionLevel(PinnedBuildLevel);
    }

    private void UpdateDeviceProfileCardState()
    {
        DeviceProfileCardLevel = NormalizePreSessionLevel(DeviceProfileLevel);
    }

    private void UpdateBenchToolsCardState()
    {
        if (QuestScreenshotLevel == OperationOutcomeKind.Failure)
        {
            BenchToolsCardLevel = OperationOutcomeKind.Failure;
            BenchToolsSummary = "Bench tools are blocked by a screenshot capture fault.";
            return;
        }

        if (TestLslSenderLevel == OperationOutcomeKind.Failure)
        {
            BenchToolsCardLevel = OperationOutcomeKind.Failure;
            BenchToolsSummary = "Bench tools are blocked by a TEST sender fault.";
            return;
        }

        if (ProximityLevel == OperationOutcomeKind.Failure)
        {
            BenchToolsCardLevel = OperationOutcomeKind.Failure;
            BenchToolsSummary = "Bench tools are blocked by a proximity control fault.";
            return;
        }

        if (WindowsEnvironmentAnalysisHasRun && WindowsEnvironmentAnalysisLevel == OperationOutcomeKind.Failure)
        {
            BenchToolsCardLevel = OperationOutcomeKind.Failure;
            BenchToolsSummary = "Bench tools are blocked by Windows environment issues.";
            return;
        }

        if (MachineLslStateHasRun && MachineLslStateLevel == OperationOutcomeKind.Failure)
        {
            BenchToolsCardLevel = OperationOutcomeKind.Failure;
            BenchToolsSummary = "Bench tools are blocked by machine LSL state issues.";
            return;
        }

        if (_testLslSignalService.IsRunning)
        {
            BenchToolsCardLevel = OperationOutcomeKind.Warning;
            BenchToolsSummary = MachineLslStateHasRun && MachineLslStateLevel == OperationOutcomeKind.Warning
                ? "Bench tools are active, but machine LSL state needs attention."
                : ProximityLevel == OperationOutcomeKind.Success
                    ? "Bench tools are active."
                    : "Bench tools are active. Proximity readback needs attention.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_questVisualConfirmationPendingReason))
        {
            BenchToolsCardLevel = OperationOutcomeKind.Warning;
            BenchToolsSummary = "Bench tools are ready. Quest visual confirmation is pending.";
            return;
        }

        if (WindowsEnvironmentAnalysisHasRun && WindowsEnvironmentAnalysisLevel == OperationOutcomeKind.Warning)
        {
            BenchToolsCardLevel = OperationOutcomeKind.Warning;
            BenchToolsSummary = "Bench tools are ready, but Windows environment advisories remain.";
            return;
        }

        if (MachineLslStateHasRun && MachineLslStateLevel == OperationOutcomeKind.Warning)
        {
            BenchToolsCardLevel = OperationOutcomeKind.Warning;
            BenchToolsSummary = "Bench tools are ready, but machine LSL state needs attention.";
            return;
        }

        if (ProximityLevel == OperationOutcomeKind.Success && _testLslSignalService.RuntimeState.Available)
        {
            BenchToolsCardLevel = OperationOutcomeKind.Success;
            BenchToolsSummary = "Bench tools are ready.";
            return;
        }

        BenchToolsCardLevel = OperationOutcomeKind.Warning;
        BenchToolsSummary = "Bench tools need attention.";
    }

    private void UpdateWindowsEnvironmentCardState()
    {
        if (WindowsEnvironmentAnalysisHasRun && WindowsEnvironmentAnalysisLevel == OperationOutcomeKind.Failure)
        {
            WindowsEnvironmentCardLevel = OperationOutcomeKind.Failure;
            WindowsEnvironmentCardSummary = WindowsEnvironmentAnalysisSummary;
            WindowsEnvironmentCardDetail = $"{WindowsEnvironmentAnalysisTimestampLabel} {WindowsEnvironmentAnalysisDetail}".Trim();
            return;
        }

        if (MachineLslStateHasRun && MachineLslStateLevel == OperationOutcomeKind.Failure)
        {
            WindowsEnvironmentCardLevel = OperationOutcomeKind.Failure;
            WindowsEnvironmentCardSummary = MachineLslStateSummary;
            WindowsEnvironmentCardDetail = $"{MachineLslStateTimestampLabel} {MachineLslStateDetail}".Trim();
            return;
        }

        if (TestLslSenderLevel == OperationOutcomeKind.Failure)
        {
            WindowsEnvironmentCardLevel = OperationOutcomeKind.Failure;
            WindowsEnvironmentCardSummary = TestLslSenderSummary;
            WindowsEnvironmentCardDetail = TestLslSenderDetail;
            return;
        }

        if (_testLslSignalService.IsRunning)
        {
            WindowsEnvironmentCardLevel = OperationOutcomeKind.Warning;
            WindowsEnvironmentCardSummary = "Companion TEST sender is active on Windows.";
            WindowsEnvironmentCardDetail = MachineLslStateHasRun
                ? $"{MachineLslStateSummary} {MachineLslStateTimestampLabel}".Trim()
                : "Stop the built-in TEST sender before switching to another upstream publisher so the Windows-side LSL inventory can settle cleanly.";
            return;
        }

        if (WindowsEnvironmentAnalysisHasRun && WindowsEnvironmentAnalysisLevel == OperationOutcomeKind.Warning)
        {
            WindowsEnvironmentCardLevel = OperationOutcomeKind.Warning;
            WindowsEnvironmentCardSummary = WindowsEnvironmentAnalysisSummary;
            WindowsEnvironmentCardDetail = $"{WindowsEnvironmentAnalysisTimestampLabel} {WindowsEnvironmentAnalysisDetail}".Trim();
            return;
        }

        if (MachineLslStateHasRun && MachineLslStateLevel == OperationOutcomeKind.Warning)
        {
            WindowsEnvironmentCardLevel = OperationOutcomeKind.Warning;
            WindowsEnvironmentCardSummary = MachineLslStateSummary;
            WindowsEnvironmentCardDetail = $"{MachineLslStateTimestampLabel} {MachineLslStateDetail}".Trim();
            return;
        }

        if (WindowsEnvironmentAnalysisHasRun || MachineLslStateHasRun)
        {
            WindowsEnvironmentCardLevel = OperationOutcomeKind.Success;
            WindowsEnvironmentCardSummary = WindowsEnvironmentAnalysisHasRun
                ? WindowsEnvironmentAnalysisSummary
                : "Windows environment checks are ready.";
            WindowsEnvironmentCardDetail = string.Join(
                " ",
                new[]
                {
                    WindowsEnvironmentAnalysisHasRun ? WindowsEnvironmentAnalysisTimestampLabel : string.Empty,
                    MachineLslStateHasRun ? MachineLslStateTimestampLabel : string.Empty,
                    "Managed tooling, liblsl, machine-visible LSL state, and the guided-installer workspace paths all live on the dedicated Windows environment page."
                }.Where(static part => !string.IsNullOrWhiteSpace(part)));
            return;
        }

        WindowsEnvironmentCardLevel = OperationOutcomeKind.Warning;
        WindowsEnvironmentCardSummary = "Run the dedicated Windows environment checks before blaming the headset.";
        WindowsEnvironmentCardDetail = "Use the dedicated Windows environment page to inspect managed tooling, liblsl, machine-visible LSL streams, and the host-visible operator-data paths exported by the guided installer.";
    }

    private void UpdateHeadsetAwakeStatus(string selector, TrackedQuestProximityState tracked, QuestProximityStatus? liveStatus)
    {
        if (_headsetStatus?.IsConnected != true)
        {
            HeadsetAwakeLevel = OperationOutcomeKind.Preview;
            HeadsetAwakeSummary = "Awake status not checked yet.";
            HeadsetAwakeDetail = "Connect the headset first so the shell can read the actual Quest power state.";
        }
        else
        {
            var powerEvidence = BuildHeadsetPowerEvidenceLabel(_headsetStatus);
            var proximityContext = BuildHeadsetPowerProximityContext(tracked, liveStatus);

            if (IsHeadsetWakeBlockedByLockScreen())
            {
                HeadsetAwakeLevel = OperationOutcomeKind.Warning;
                HeadsetAwakeSummary = "Headset wake blocked by lock screen.";
                HeadsetAwakeDetail = $"{powerEvidence} Quest woke into SensorLockActivity instead of a usable scene. Clear the lock screen on the headset, or press the physical power button again, before you trust the wake state or launch Sussex. {proximityContext}".Trim();
            }
            else if (_headsetStatus.IsInWakeLimbo)
            {
                HeadsetAwakeLevel = OperationOutcomeKind.Warning;
                HeadsetAwakeSummary = "Guardian or tracking blocker active.";
                HeadsetAwakeDetail = $"{powerEvidence} Quest reports the display awake, but Android still sees a concrete Guardian, tracking-loss, or ClearActivity blocker in front of the usable scene. {LaunchVisualBlockInstruction} Use Capture Quest Screenshot to confirm the actual visible state before deciding whether to launch or stop the runtime. {proximityContext}".Trim();
            }
            else if (_headsetStatus.IsAwake == true)
            {
                HeadsetAwakeLevel = OperationOutcomeKind.Success;
                HeadsetAwakeSummary = "Headset awake.";
                HeadsetAwakeDetail = $"{powerEvidence} {proximityContext}".Trim();
            }
            else if (_headsetStatus.IsAwake == false)
            {
                HeadsetAwakeLevel = OperationOutcomeKind.Warning;
                HeadsetAwakeSummary = "Headset asleep. Wake before launching.";
                HeadsetAwakeDetail = $"{powerEvidence} {proximityContext} {LaunchSleepBlockInstruction} Do not start Sussex while the headset is asleep on this Meta OS build, because the runtime can enter a black or limbo scene that may require a headset restart. Other headset action buttons can still wake the device before their command is sent.".Trim();
            }
            else
            {
                HeadsetAwakeLevel = OperationOutcomeKind.Preview;
                HeadsetAwakeSummary = string.IsNullOrWhiteSpace(selector)
                    ? "Awake status unavailable."
                    : "Headset power state unavailable.";
                HeadsetAwakeDetail = $"{powerEvidence} {proximityContext}".Trim();
            }
        }

        RefreshStudyRuntimeLaunchState();
    }

    private async Task ApplyOutcomeAsync(string actionLabel, OperationOutcome outcome)
    {
        await DispatchAsync(() =>
        {
            var detailForDisplay = BuildActionDetailForDisplay(actionLabel, outcome);
            LastActionLabel = actionLabel;
            LastActionDetail = detailForDisplay;
            LastActionLevel = outcome.Kind;
            AppendLog(MapLevel(outcome.Kind), outcome.Summary, detailForDisplay);
            UpdateWorkflowGuideState();
        }).ConfigureAwait(false);
    }

    private static string BuildActionDetailForDisplay(string actionLabel, OperationOutcome outcome)
    {
        if (IsBreathingCalibrationStartAction(actionLabel) &&
            outcome.Kind != OperationOutcomeKind.Failure)
        {
            var guidance = "Watch Calibration Telemetry for the real verdict: accepted, accepted with warnings, not accepted yet, or rejected.";
            return string.IsNullOrWhiteSpace(outcome.Detail)
                ? guidance
                : $"{outcome.Detail} {guidance}";
        }

        return outcome.Detail;
    }

    private static bool IsBreathingCalibrationStartAction(string actionLabel)
        => string.Equals(actionLabel, "Start Breathing Calibration", StringComparison.Ordinal)
           || string.Equals(actionLabel, "Start Dynamic-Axis Calibration", StringComparison.Ordinal)
           || string.Equals(actionLabel, "Start Fixed-Axis Calibration", StringComparison.Ordinal);

    private void RefreshBenchToolsStatus()
    {
        UpdateProximityCard();
        UpdateQuestScreenshotCard();
        UpdateTestLslSenderCard();
        UpdateBenchToolsCardState();
        UpdateWindowsEnvironmentCardState();
        UpdateBenchRefreshTimerState();
        OnPropertyChanged(nameof(CanStartBreathingCalibration));
        OnPropertyChanged(nameof(CanStartDynamicAxisCalibration));
        OnPropertyChanged(nameof(CanStartFixedAxisCalibration));
        OnPropertyChanged(nameof(CanResetBreathingCalibration));
        OnPropertyChanged(nameof(CanToggleAutomaticBreathingMode));
        OnPropertyChanged(nameof(CanToggleAutomaticBreathingRun));
        OnPropertyChanged(nameof(IsAutomaticBreathingRunToggleState));
        OnPropertyChanged(nameof(CanStartExperiment));
        OnPropertyChanged(nameof(CanEndExperiment));
        OnPropertyChanged(nameof(CanStartParticipantExperiment));
        OnPropertyChanged(nameof(CanEndParticipantExperiment));
        OnPropertyChanged(nameof(IsRecordingToggleState));
        OnPropertyChanged(nameof(RecordingToggleActionLabel));
        OnPropertyChanged(nameof(CanToggleRecording));
        OnPropertyChanged(nameof(CanApplySelectedCondition));
        OnPropertyChanged(nameof(CanRunWorkflowValidationCapture));
        OnPropertyChanged(nameof(CanToggleParticles));
        OnPropertyChanged(nameof(IsParticlesToggleState));
        OnPropertyChanged(nameof(ParticlesToggleActionLabel));
        OnPropertyChanged(nameof(CanToggleProximity));
        OnPropertyChanged(nameof(IsProximityToggleState));
        OnPropertyChanged(nameof(CanCaptureQuestScreenshot));
        RefreshStudyRuntimeLaunchState();
        OnPropertyChanged(nameof(CanOpenLastQuestScreenshot));
        OnPropertyChanged(nameof(CanToggleTestLslSender));
        OnPropertyChanged(nameof(IsTestLslSenderToggleState));
        StartDynamicAxisCalibrationCommand.RaiseCanExecuteChanged();
        StartFixedAxisCalibrationCommand.RaiseCanExecuteChanged();
        ApplySelectedConditionCommand.RaiseCanExecuteChanged();
        UpdateWorkflowStatus();
    }

    private void RefreshStudyRuntimeLaunchState()
    {
        OnPropertyChanged(nameof(StudyRuntimeActionLabel));
        OnPropertyChanged(nameof(WorkflowGuideLaunchActionLabel));
        OnPropertyChanged(nameof(IsStudyRuntimeToggleState));
        OnPropertyChanged(nameof(IsLaunchBlockedBySleepingHeadset));
        OnPropertyChanged(nameof(IsLaunchBlockedByHeadsetLockScreen));
        OnPropertyChanged(nameof(IsLaunchBlockedByHeadsetVisualBlocker));
        OnPropertyChanged(nameof(CanLaunchStudyRuntime));
        OnPropertyChanged(nameof(CanToggleStudyRuntime));

        if (LaunchStudyAppCommand is not null)
        {
            LaunchStudyAppCommand.RaiseCanExecuteChanged();
        }

        if (ToggleStudyRuntimeCommand is not null)
        {
            ToggleStudyRuntimeCommand.RaiseCanExecuteChanged();
        }
    }

    private string BuildLaunchBlockInstruction()
        => IsLaunchBlockedBySleepingHeadset
            ? LaunchSleepBlockInstruction
            : IsLaunchBlockedByHeadsetLockScreen
                ? LaunchLockScreenInstruction
            : IsLaunchBlockedByHeadsetVisualBlocker
                ? LaunchVisualBlockInstruction
                : string.Empty;

    private void RefreshAutomaticBreathingStateProperties()
    {
        OnPropertyChanged(nameof(AutomaticBreathingSummary));
        OnPropertyChanged(nameof(AutomaticBreathingDetail));
        OnPropertyChanged(nameof(AutomaticBreathingModeActionLabel));
        OnPropertyChanged(nameof(AutomaticBreathingRunActionLabel));
        OnPropertyChanged(nameof(IsAutomaticBreathingRunToggleState));
    }

    private void RefreshLiveTwinState()
    {
        if (_twinBridge is not LslTwinModeBridge lslBridge)
        {
            _reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _visualProfiles.RefreshReportedTwinState(_reportedTwinState);
            _controllerBreathingProfiles.RefreshReportedTwinState(_reportedTwinState);
            RefreshBenchToolsStatus();
            LiveRuntimeLevel = OperationOutcomeKind.Preview;
            LiveRuntimeSummary = _twinBridge.Status.Summary;
            LiveRuntimeDetail = _twinBridge.Status.Detail;
            LslLevel = OperationOutcomeKind.Preview;
            ControllerLevel = OperationOutcomeKind.Preview;
            HeartbeatLevel = OperationOutcomeKind.Preview;
            CoherenceLevel = OperationOutcomeKind.Preview;
            PerformanceLevel = OperationOutcomeKind.Preview;
            RecenterLevel = OperationOutcomeKind.Preview;
            ParticlesLevel = OperationOutcomeKind.Preview;
            LastTwinStateTimestampLabel = "No live app-state timestamp yet.";
            _activeFocusSectionId = string.Empty;
            FocusRows.Clear();
            RecentTwinEvents.Clear();
            RefreshAutomaticBreathingStateProperties();
            UpdateWorkflowStatus();
            return;
        }

        _reportedTwinState = lslBridge.ReportedSettings
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        _visualProfiles.RefreshReportedTwinState(_reportedTwinState);
        _controllerBreathingProfiles.RefreshReportedTwinState(_reportedTwinState);

        var reportedDeviceSessionDir = GetFirstValue("study.recording.device.session_dir");
        if (!string.IsNullOrWhiteSpace(reportedDeviceSessionDir))
        {
            _latestDeviceRecordingSessionDir = reportedDeviceSessionDir;
        }

        RecentTwinEvents.Clear();
        foreach (var stateEvent in lslBridge.StateEvents
                     .OrderByDescending(entry => entry.Timestamp)
                     .Take(18))
        {
            RecentTwinEvents.Add(stateEvent);
        }

        LastTwinStateTimestampLabel = lslBridge.LastStateReceivedAt is null
            ? "No live app-state timestamp yet."
            : DateTimeOffset.UtcNow - lslBridge.LastStateReceivedAt.Value > TwinStateIdleThreshold
                ? $"Last app-state frame {lslBridge.LastStateReceivedAt.Value.ToLocalTime():HH:mm:ss} (stale)."
                : $"Last app-state frame {lslBridge.LastStateReceivedAt.Value.ToLocalTime():HH:mm:ss}.";

        RefreshBenchToolsStatus();
        UpdateLiveRuntimeCard();
        UpdateLslCard();
        UpdateControllerCard();
        UpdateHeartbeatCard();
        UpdateCoherenceCard();
        UpdatePerformanceCard();
        UpdateRecenterCard();
        UpdateParticlesCard();
        UpdatePinnedBuildStatus();
        RefreshAutomaticBreathingStateProperties();
        RefreshFocusRows();
        TryRecordActiveSessionCommandObservations(lslBridge.LastStateReceivedAt ?? DateTimeOffset.UtcNow);
        UpdateWorkflowStatus();
    }

    private async Task<StudyDataRecordingSession?> TryBeginParticipantRecordingAsync()
    {
        var existingSession = await DispatchAsync(() => _activeRecordingSession).ConfigureAwait(false);
        if (existingSession is not null)
        {
            await ApplyOutcomeAsync(
                "Start Participant Run",
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Participant recording is already active.",
                    $"The recorder is already writing session {existingSession.SessionId}. End that run before starting another participant.")).ConfigureAwait(false);
            return null;
        }

        StudyParticipantStatus participantStatus;
        string normalizedParticipantId;
        try
        {
            normalizedParticipantId = await DispatchAsync(() => StudyDataRecorderService.NormalizeParticipantId(ParticipantIdDraft)).ConfigureAwait(false);
            participantStatus = _studyDataRecorderService.GetParticipantStatus(_study.Id, normalizedParticipantId);
        }
        catch (ArgumentException)
        {
            await ApplyOutcomeAsync(
                "Start Participant Run",
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Participant number missing.",
                    "Enter a participant number before starting the recorded experiment run.")).ConfigureAwait(false);
            return null;
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var sessionId = BuildParticipantSessionId(startedAtUtc);
        var request = await DispatchAsync(() => CreateRecordingStartRequest(normalizedParticipantId, sessionId, startedAtUtc)).ConfigureAwait(false);

        StudyDataRecordingSession recordingSession;
        try
        {
            recordingSession = _studyDataRecorderService.StartSession(request);
            recordingSession.RecordEvent(
                "recording.started",
                "Local Sussex participant recording session created.",
                null,
                "success",
                startedAtUtc);

            if (participantStatus.HasExistingSessions)
            {
                recordingSession.RecordEvent(
                    "participant.duplicate_warning",
                    $"Participant id already has local sessions: {string.Join(", ", participantStatus.ExistingSessionIds)}",
                    null,
                    "warning",
                    startedAtUtc);
            }

            var latestScreenshotPath = await DispatchAsync(() => QuestScreenshotPath).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(latestScreenshotPath) && File.Exists(latestScreenshotPath))
            {
                var extension = Path.GetExtension(latestScreenshotPath);
                var artifactName = $"participant-start-visual-check{extension}";
                recordingSession.CopyArtifact(latestScreenshotPath, artifactName);
                recordingSession.RecordEvent(
                    "artifact.quest_screenshot",
                    $"Archived the latest Quest screenshot as {artifactName}.",
                    null,
                    "success");
            }
        }
        catch (Exception exception)
        {
            await DispatchAsync(() => SetRecorderFault("Start participant recording", exception)).ConfigureAwait(false);
            await ApplyOutcomeAsync(
                "Start Participant Run",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Could not start the local participant recorder.",
                    exception.Message)).ConfigureAwait(false);
            return null;
        }

        await DispatchAsync(() =>
        {
            _recorderFaultDetail = string.Empty;
            _participantRunStopping = false;
            _activeRecordingSession = recordingSession;
            _lastCompletedRecordingFolderPath = recordingSession.SessionFolderPath;
            _participantIdDraft = normalizedParticipantId;
            ResetRecordingTelemetryTracking();
            OnPropertyChanged(nameof(ParticipantIdDraft));
            StartRegularRecordingSamples();
            UpdateParticipantSessionState();
            RefreshBenchToolsStatus();
        }).ConfigureAwait(false);

        return recordingSession;
    }

    private async Task<OperationOutcome?> FinishParticipantRecordingAsync(
        StudyDataRecordingSession recordingSession,
        DateTimeOffset endedAtUtc,
        string eventName,
        string eventDetail,
        string result,
        bool clearParticipantId,
        bool pullQuestArtifacts,
        string remoteSessionDir,
        bool generateSessionReviewPdf)
    {
        await DispatchAsync(() =>
        {
            if (ReferenceEquals(_activeRecordingSession, recordingSession))
            {
                _activeRecordingSession = null;
            }

            StopRegularRecordingSamples();
        }).ConfigureAwait(false);

        OperationOutcome? questPullOutcome = null;
        try
        {
            if (pullQuestArtifacts)
            {
                questPullOutcome = await TryPullQuestRecordingArtifactsAsync(remoteSessionDir, recordingSession.SessionFolderPath).ConfigureAwait(false);
                recordingSession.RecordEvent(
                    "recording.device_pullback",
                    BuildRecorderEventDetail(questPullOutcome),
                    null,
                    questPullOutcome.Kind.ToString());
            }

            var latestParameterState = await DispatchAsync(() => BuildSessionParameterStateNode(endedAtUtc)).ConfigureAwait(false);
            var latestSessionConditionsJson = await DispatchAsync(() => BuildSessionConditionsJson(endedAtUtc)).ConfigureAwait(false);
            recordingSession.UpdateSessionContext(
                latestParameterState,
                latestSessionConditionsJson,
                endedAtUtc,
                incrementParameterChangeCount: false);
            recordingSession.RecordEvent(eventName, eventDetail, null, result, endedAtUtc);
            recordingSession.Complete(endedAtUtc);
        }
        catch (Exception exception)
        {
            await DispatchAsync(() => SetRecorderFault("Stop participant recording", exception)).ConfigureAwait(false);
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Stop participant recording failed.",
                exception.Message);
        }

        var completedDevicePullFolderPath = questPullOutcome?.Items?.FirstOrDefault() ?? string.Empty;
        OperationOutcome? sessionReviewPdfOutcome = null;
        if (generateSessionReviewPdf)
        {
            sessionReviewPdfOutcome = await GenerateSessionReviewPdfAsync(
                    recordingSession.SessionFolderPath,
                    ParticipantSessionReviewPdfFileName)
                .ConfigureAwait(false);
        }

        var completedPdfPath = sessionReviewPdfOutcome?.Kind == OperationOutcomeKind.Success
            ? sessionReviewPdfOutcome.Items?.FirstOrDefault() ?? string.Empty
            : string.Empty;

        await DispatchAsync(() =>
        {
            _lastCompletedRecordingFolderPath = recordingSession.SessionFolderPath;
            _lastCompletedRecordingDevicePullFolderPath = completedDevicePullFolderPath;
            _lastCompletedRecordingPdfPath = completedPdfPath;
            _participantRunStopping = false;
            if (clearParticipantId)
            {
                _participantIdDraft = string.Empty;
                OnPropertyChanged(nameof(ParticipantIdDraft));
            }

            UpdateParticipantSessionState();
            RefreshBenchToolsStatus();
        }).ConfigureAwait(false);

        if (questPullOutcome is not null && questPullOutcome.Kind != OperationOutcomeKind.Success)
        {
            await DispatchAsync(() =>
                AppendLog(
                    MapLevel(questPullOutcome.Kind),
                    questPullOutcome.Summary,
                    questPullOutcome.Detail)).ConfigureAwait(false);
        }

        return questPullOutcome;
    }

    private async Task<OperationOutcome> TryPullQuestRecordingArtifactsAsync(string remoteSessionDir, string localSessionFolderPath)
    {
        try
        {
            return await PullQuestRecordingArtifactsAsync(remoteSessionDir, localSessionFolderPath).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Quest backup pullback was canceled; Windows data saved.",
                $"{BuildWindowsSessionInventoryDetail(localSessionFolderPath)} Quest backup pullback was canceled before completion: {exception.Message}");
        }
        catch (Exception exception)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Quest backup pullback failed; Windows data saved.",
                $"{BuildWindowsSessionInventoryDetail(localSessionFolderPath)} Quest backup pullback failed before completion: {exception.Message}");
        }
    }

    private StudyDataRecordingStartRequest CreateRecordingStartRequest(
        string participantId,
        string sessionId,
        DateTimeOffset startedAtUtc)
    {
        var versionName = string.IsNullOrWhiteSpace(_installedAppStatus?.VersionName)
            ? _study.App.VersionName
            : _installedAppStatus!.VersionName;
        var runtimeConfigHash = GetFirstValue("config.runtime_json_hash") ?? string.Empty;
        var runtimeHotloadProfileId = GetFirstValue("hotload.hotload_profile_id") ?? string.Empty;
        var runtimeHotloadProfileVersion = GetFirstValue("hotload.hotload_profile_version") ?? string.Empty;
        var runtimeHotloadProfileChannel = GetFirstValue("hotload.hotload_profile_channel") ?? string.Empty;
        var environmentHash = VerificationBaseline?.EnvironmentHash ?? string.Empty;
        var datasetId = BuildDatasetId(_study.Id, participantId, sessionId);
        var datasetHash = BuildDatasetHash(_study.Id, participantId, sessionId, startedAtUtc);
        var sessionParameterState = BuildSessionParameterStateNode(startedAtUtc);
        var sessionParameterStateHash = ComputeHashToken(sessionParameterState.ToJsonString());
        var sessionConditionsJson = BuildSessionConditionsJson(startedAtUtc);
        var settingsHash = BuildSettingsHash(
            _study.Id,
            _study.App.PackageId,
            _study.App.Sha256,
            versionName,
            _study.App.LaunchComponent,
            _headsetStatus?.SoftwareVersion ?? string.Empty,
            _headsetStatus?.SoftwareBuildId ?? string.Empty,
            _headsetStatus?.SoftwareDisplayId ?? string.Empty,
            _study.DeviceProfile.Id,
            _study.DeviceProfile.Properties,
            _study.Monitoring.ExpectedLslStreamName,
            _study.Monitoring.ExpectedLslStreamType,
            _study.Monitoring.RecenterDistanceThresholdUnits,
            runtimeConfigHash,
            runtimeHotloadProfileId,
            runtimeHotloadProfileVersion,
            runtimeHotloadProfileChannel,
            sessionParameterStateHash,
            environmentHash);
        return new StudyDataRecordingStartRequest(
            _study.Id,
            _study.Label,
            participantId,
            sessionId,
            datasetId,
            datasetHash,
            settingsHash,
            environmentHash,
            startedAtUtc,
            _study.App.PackageId,
            _study.App.Sha256,
            versionName,
            _study.App.LaunchComponent,
            _headsetStatus?.SoftwareVersion ?? string.Empty,
            _headsetStatus?.SoftwareBuildId ?? string.Empty,
            _headsetStatus?.SoftwareDisplayId ?? string.Empty,
            _study.DeviceProfile.Id,
            _study.DeviceProfile.Label,
            new Dictionary<string, string>(_study.DeviceProfile.Properties, StringComparer.OrdinalIgnoreCase),
            _study.Monitoring.ExpectedLslStreamName,
            _study.Monitoring.ExpectedLslStreamType,
            _study.Monitoring.RecenterDistanceThresholdUnits,
            runtimeConfigHash,
            runtimeHotloadProfileId,
            runtimeHotloadProfileVersion,
            runtimeHotloadProfileChannel,
            Environment.MachineName,
            ResolveHeadsetActionSelector(),
            sessionConditionsJson,
            sessionParameterStateHash,
            sessionParameterState);
    }

    private JsonObject BuildSessionParameterStateNode(DateTimeOffset capturedAtUtc)
        => new()
        {
            ["SchemaVersion"] = "sussex-session-parameter-state-v1",
            ["CapturedAtUtc"] = capturedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["RuntimeHotloadProfile"] = new JsonObject
            {
                ["Id"] = GetFirstValue("hotload.hotload_profile_id") ?? string.Empty,
                ["Version"] = GetFirstValue("hotload.hotload_profile_version") ?? string.Empty,
                ["Channel"] = GetFirstValue("hotload.hotload_profile_channel") ?? string.Empty,
                ["RuntimeConfigHash"] = GetFirstValue("config.runtime_json_hash") ?? string.Empty
            },
            ["SelectedCondition"] = JsonSerializer.SerializeToNode(CaptureConditionSnapshot(SelectedCondition)),
            ["VisualTuning"] = JsonSerializer.SerializeToNode(_visualProfiles.CaptureSessionSnapshot()),
            ["ControllerBreathingTuning"] = JsonSerializer.SerializeToNode(_controllerBreathingProfiles.CaptureSessionSnapshot())
        };

    private static string BuildSessionParameterActivityDetail(SussexSessionParameterActivity activity)
    {
        var payload = new
        {
            SchemaVersion = "sussex-session-parameter-activity-v1",
            activity.Surface,
            activity.Kind,
            activity.ProfileId,
            activity.ProfileName,
            RecordedAtUtc = activity.RecordedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Outcome = activity.OutcomeKind?.ToString(),
            activity.Summary,
            activity.Detail,
            Changes = activity.Changes.Select(change => new
            {
                change.Id,
                change.Label,
                change.Type,
                PreviousValue = change.PreviousValue,
                PreviousLabel = FormatSessionParameterValue(change.PreviousValue, change.Type),
                CurrentValue = change.CurrentValue,
                CurrentLabel = FormatSessionParameterValue(change.CurrentValue, change.Type)
            }),
            CurrentValues = activity.CurrentValues
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new
                {
                    Id = pair.Key,
                    Value = pair.Value,
                    Label = FormatSessionParameterValue(
                        pair.Value,
                        activity.Changes.FirstOrDefault(change => string.Equals(change.Id, pair.Key, StringComparison.OrdinalIgnoreCase))?.Type)
                }),
            PreviousReportedValues = activity.PreviousReportedValues?
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new
                {
                    Id = pair.Key,
                    pair.Value
                })
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string FormatSessionParameterValue(double? value, string? type)
    {
        if (!value.HasValue)
        {
            return "n/a";
        }

        if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return value.Value >= 0.5d ? "On" : "Off";
        }

        if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Round(value.Value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        }

        return value.Value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static object? CaptureConditionSnapshot(StudyConditionDefinition? condition)
        => condition is null
            ? null
            : new
            {
                condition.Id,
                condition.Label,
                condition.Description,
                condition.VisualProfileId,
                condition.ControllerBreathingProfileId,
                condition.IsActive,
                Properties = new Dictionary<string, string>(condition.Properties, StringComparer.OrdinalIgnoreCase)
            };

    private string BuildSessionConditionsJson(DateTimeOffset capturedAtUtc)
    {
        var twinStateSnapshot = new Dictionary<string, string>(_reportedTwinState, StringComparer.OrdinalIgnoreCase);
        var snapshot = new
        {
            SchemaVersion = "sussex-session-conditions-v1",
            CapturedAtUtc = capturedAtUtc,
            Study = new
            {
                _study.Id,
                _study.Label,
                _study.Description,
                App = new
                {
                    _study.App.Label,
                    _study.App.PackageId,
                    _study.App.Sha256,
                    _study.App.VersionName,
                    _study.App.LaunchComponent,
                    _study.App.ApkPath
                },
                DeviceProfile = new
                {
                    _study.DeviceProfile.Id,
                    _study.DeviceProfile.Label,
                    Properties = new Dictionary<string, string>(_study.DeviceProfile.Properties, StringComparer.OrdinalIgnoreCase)
                },
                Monitoring = new
                {
                    _study.Monitoring.ExpectedLslStreamName,
                    _study.Monitoring.ExpectedLslStreamType,
                    _study.Monitoring.RecenterDistanceThresholdUnits
                }
            },
            SelectedCondition = CaptureConditionSnapshot(SelectedCondition),
            AvailableConditions = Conditions.Select(CaptureConditionSnapshot).ToArray(),
            Runtime = new
            {
                StagedApkPath,
                InstalledAppStatus = _installedAppStatus,
                HeadsetStatus = _headsetStatus,
                DeviceProfileStatus = _deviceProfileStatus
            },
            ConditionHighlights = BuildSessionConditionHighlights(),
            ReportedTwinState = new
            {
                KeyCount = twinStateSnapshot.Count,
                Values = twinStateSnapshot
            },
            VisualTuning = _visualProfiles.CaptureSessionSnapshot(),
            ControllerBreathingTuning = _controllerBreathingProfiles.CaptureSessionSnapshot()
        };

        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private IReadOnlyDictionary<string, string?> BuildSessionConditionHighlights()
    {
        var highlights = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        AddSessionConditionHighlight(highlights, "study.session.state_label");
        AddSessionConditionHighlight(highlights, "study.session.run_index");
        AddSessionConditionHighlight(highlights, "study.session.dataset_id");
        AddSessionConditionHighlight(highlights, "study.session.dataset_hash");
        AddSessionConditionHighlight(highlights, "study.session.settings_hash");
        AddSessionConditionHighlight(highlights, "study.breathing.value01");
        AddSessionConditionHighlight(highlights, "study.coherence.value01");
        AddSessionConditionHighlight(highlights, "study.radius.sphere.progress01");
        AddSessionConditionHighlight(highlights, "study.radius.sphere.raw");
        AddSessionConditionHighlight(highlights, "tracker.breathing.controller.active");
        AddSessionConditionHighlight(highlights, "tracker.breathing.controller.calibrated");
        AddSessionConditionHighlight(highlights, "tracker.breathing.controller.state");
        AddSessionConditionHighlight(highlights, "study.pose.headset.local_pos_x");
        AddSessionConditionHighlight(highlights, "study.pose.headset.local_pos_y");
        AddSessionConditionHighlight(highlights, "study.pose.headset.local_pos_z");
        AddSessionConditionHighlight(highlights, "study.pose.headset.local_rot_x");
        AddSessionConditionHighlight(highlights, "study.pose.headset.local_rot_y");
        AddSessionConditionHighlight(highlights, "study.pose.headset.local_rot_z");
        AddSessionConditionHighlight(highlights, "study.pose.headset.local_rot_w");
        AddSessionConditionHighlight(highlights, "study.pose.controller.local_pos_x");
        AddSessionConditionHighlight(highlights, "study.pose.controller.local_pos_y");
        AddSessionConditionHighlight(highlights, "study.pose.controller.local_pos_z");
        AddSessionConditionHighlight(highlights, "study.pose.controller.local_rot_x");
        AddSessionConditionHighlight(highlights, "study.pose.controller.local_rot_y");
        AddSessionConditionHighlight(highlights, "study.pose.controller.local_rot_z");
        AddSessionConditionHighlight(highlights, "study.pose.controller.local_rot_w");
        AddSessionConditionHighlight(highlights, "study.particles.visible");
        AddSessionConditionHighlight(highlights, "study.recenter.distance_units");
        AddSessionConditionHighlight(highlights, "hotload.hotload_profile_id");
        AddSessionConditionHighlight(highlights, "hotload.hotload_profile_version");
        AddSessionConditionHighlight(highlights, "config.runtime_json_hash");
        return highlights;
    }

    private void AddSessionConditionHighlight(
        IDictionary<string, string?> highlights,
        string key)
        => highlights[key] = GetFirstValue(key);

    private async Task<OperationOutcome> PublishParticipantSessionMetadataAsync(StudyDataRecordingSession recordingSession)
    {
        var runtimeConfigJson = await DispatchAsync(() =>
        {
            TryGetReportedStudyRuntimeConfigJson(_reportedTwinState, out var reportedRuntimeConfigJson);
            return reportedRuntimeConfigJson;
        }).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(runtimeConfigJson))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Quest-side session metadata publish blocked.",
                "The live Sussex runtime has not yet reported showcase_active_runtime_config_json on quest_twin_state, so the participant session metadata cannot be merged into the active runtime config.");
        }

        string mergedRuntimeConfigJson;
        try
        {
            mergedRuntimeConfigJson = BuildParticipantSessionRuntimeConfigJson(runtimeConfigJson, recordingSession);
        }
        catch (Exception exception)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Quest-side session metadata publish blocked.",
                $"The companion could not merge participant session metadata into the active runtime config JSON: {exception.Message}");
        }

        var stagedApkPath = await DispatchAsync(() => StagedApkPath).ConfigureAwait(false);
        var target = CreateStudyTarget(stagedApkPath);
        var profile = new RuntimeConfigProfile(
            $"sussex-study-session-{recordingSession.SessionId}",
            "Sussex Study Session Metadata",
            string.Empty,
            DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture),
            "study",
            false,
            "Participant/session correlation metadata for mirrored Windows and Quest recording sessions.",
            [_study.App.PackageId],
            [
                new RuntimeConfigEntry("showcase_active_runtime_config_json", mergedRuntimeConfigJson),
                new RuntimeConfigEntry("study_session_study_id", _study.Id),
                new RuntimeConfigEntry("study_session_study_label", _study.Label),
                new RuntimeConfigEntry("study_session_participant_id", recordingSession.ParticipantId),
                new RuntimeConfigEntry("study_session_id", recordingSession.SessionId),
                new RuntimeConfigEntry("study_session_dataset_id", recordingSession.DatasetId),
                new RuntimeConfigEntry("study_session_dataset_hash", recordingSession.DatasetHash),
                new RuntimeConfigEntry("study_session_settings_hash", recordingSession.SettingsHash),
                new RuntimeConfigEntry("study_session_environment_hash", recordingSession.EnvironmentHash),
                new RuntimeConfigEntry("study_session_windows_machine_name", Environment.MachineName),
                new RuntimeConfigEntry("study_session_device_profile_id", _study.DeviceProfile.Id),
                new RuntimeConfigEntry("study_session_apk_sha256", _study.App.Sha256),
                new RuntimeConfigEntry("study_session_started_at_utc", recordingSession.SessionStartedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
            ]);

        return await _twinBridge.PublishRuntimeConfigAsync(profile, target).ConfigureAwait(false);
    }

    private string BuildParticipantSessionRuntimeConfigJson(
        string baselineRuntimeConfigJson,
        StudyDataRecordingSession recordingSession)
    {
        var rootNode = JsonNode.Parse(baselineRuntimeConfigJson)
                       ?? throw new InvalidOperationException("The reported runtime config JSON was empty.");
        if (rootNode is not JsonObject rootObject)
        {
            throw new InvalidOperationException("The reported runtime config JSON must be a JSON object.");
        }

        rootObject["study_session_study_id"] = _study.Id;
        rootObject["study_session_study_label"] = _study.Label;
        rootObject["study_session_participant_id"] = recordingSession.ParticipantId;
        rootObject["study_session_id"] = recordingSession.SessionId;
        rootObject["study_session_dataset_id"] = recordingSession.DatasetId;
        rootObject["study_session_dataset_hash"] = recordingSession.DatasetHash;
        rootObject["study_session_settings_hash"] = recordingSession.SettingsHash;
        rootObject["study_session_environment_hash"] = recordingSession.EnvironmentHash;
        rootObject["study_session_windows_machine_name"] = Environment.MachineName;
        rootObject["study_session_device_profile_id"] = _study.DeviceProfile.Id;
        rootObject["study_session_apk_sha256"] = _study.App.Sha256;
        rootObject["study_session_started_at_utc"] = recordingSession.SessionStartedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        return rootObject.ToJsonString();
    }

    private TwinSnapshotGate CaptureTwinSnapshotGate()
    {
        if (_twinBridge is not LslTwinModeBridge lslBridge)
        {
            return new TwinSnapshotGate(null, null);
        }

        return new TwinSnapshotGate(
            lslBridge.LastCommittedSnapshotRevision,
            lslBridge.LastCommittedSnapshotReceivedAt);
    }

    private async Task<bool> WaitForFreshRuntimeConfigBaselineAsync(
        TwinSnapshotGate snapshotGate,
        DateTimeOffset commandIssuedAtUtc)
    {
        if (_twinBridge is not LslTwinModeBridge)
        {
            return false;
        }

        var timeoutAtUtc = DateTimeOffset.UtcNow + StartupRuntimeConfigBaselineTimeout;
        while (DateTimeOffset.UtcNow < timeoutAtUtc)
        {
            var ready = await DispatchAsync(() =>
            {
                var bridge = _twinBridge as LslTwinModeBridge;
                return HasFreshRuntimeConfigTwinBaseline(
                    snapshotGate.Revision,
                    snapshotGate.CommittedAtUtc,
                    commandIssuedAtUtc,
                    bridge?.LastCommittedSnapshotRevision,
                    bridge?.LastCommittedSnapshotReceivedAt,
                    _reportedTwinState);
            }).ConfigureAwait(false);

            if (ready)
            {
                return true;
            }

            await Task.Delay(StartupRuntimeConfigBaselinePollInterval).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> WaitForFreshParticipantSessionRuntimeConfigBaselineAsync(
        TwinSnapshotGate snapshotGate,
        DateTimeOffset commandIssuedAtUtc,
        StudyDataRecordingSession recordingSession)
    {
        if (_twinBridge is not LslTwinModeBridge)
        {
            return false;
        }

        var timeoutAtUtc = DateTimeOffset.UtcNow + StartupRuntimeConfigBaselineTimeout;
        while (DateTimeOffset.UtcNow < timeoutAtUtc)
        {
            var ready = await DispatchAsync(() =>
            {
                var bridge = _twinBridge as LslTwinModeBridge;
                return HasFreshParticipantSessionRuntimeConfigBaseline(
                    snapshotGate.Revision,
                    snapshotGate.CommittedAtUtc,
                    commandIssuedAtUtc,
                    bridge?.LastCommittedSnapshotRevision,
                    bridge?.LastCommittedSnapshotReceivedAt,
                    _reportedTwinState,
                    recordingSession.SessionId,
                    recordingSession.DatasetHash);
            }).ConfigureAwait(false);

            if (ready)
            {
                return true;
            }

            await Task.Delay(StartupRuntimeConfigBaselinePollInterval).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<OperationOutcome> ConfirmDeviceRecordingSessionAsync(
        StudyDataRecordingSession recordingSession,
        TwinSnapshotGate snapshotGate,
        DateTimeOffset commandIssuedAtUtc)
    {
        var timeoutAtUtc = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(6);
        (string? ReportedSessionId, string? ReportedDatasetHash, bool DeviceRecordingActive, string? DeviceSessionDir) lastObservedState = default;
        while (DateTimeOffset.UtcNow < timeoutAtUtc)
        {
            var confirmation = await DispatchAsync(() =>
            {
                var currentRevision = (_twinBridge as LslTwinModeBridge)?.LastCommittedSnapshotRevision;
                var currentCommittedAtUtc = (_twinBridge as LslTwinModeBridge)?.LastCommittedSnapshotReceivedAt;
                var sessionId = GetFirstValue("study.session.id");
                var datasetHash = GetFirstValue("study.session.dataset_hash");
                var deviceRecordingActive = ParseBool(GetFirstValue("study.recording.device.active")) == true;
                var deviceSessionDir = GetFirstValue("study.recording.device.session_dir");
                var recorderFaultActive = ParseBool(GetFirstValue("study.recording.device.fault_active")) == true;
                var recorderFaultDetail = GetFirstValue("study.recording.device.fault_detail");
                var hasFreshSnapshot = HasFreshCommittedTwinSnapshot(
                    snapshotGate,
                    commandIssuedAtUtc,
                    currentRevision,
                    currentCommittedAtUtc);

                return (
                    HasFreshSnapshot: hasFreshSnapshot,
                    Matches: deviceRecordingActive &&
                             string.Equals(sessionId, recordingSession.SessionId, StringComparison.Ordinal) &&
                             string.Equals(datasetHash, recordingSession.DatasetHash, StringComparison.OrdinalIgnoreCase),
                    ReportedSessionId: sessionId,
                    ReportedDatasetHash: datasetHash,
                    DeviceRecordingActive: deviceRecordingActive,
                    DeviceSessionDir: deviceSessionDir,
                    RecorderFaultActive: recorderFaultActive,
                    RecorderFaultDetail: recorderFaultDetail,
                    CurrentRevision: currentRevision);
            }).ConfigureAwait(false);

            lastObservedState = (
                confirmation.ReportedSessionId,
                confirmation.ReportedDatasetHash,
                confirmation.DeviceRecordingActive,
                confirmation.DeviceSessionDir);

            if (confirmation.RecorderFaultActive)
            {
                var detail = string.IsNullOrWhiteSpace(confirmation.RecorderFaultDetail)
                    ? "Quest reported an explicit device-side recorder fault while starting the participant session."
                    : $"Quest reported an explicit device-side recorder fault while starting the participant session: {confirmation.RecorderFaultDetail}";
                return new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Quest-side participant recorder failed to start.",
                    detail);
            }

            if (confirmation.HasFreshSnapshot && confirmation.Matches)
            {
                if (!string.IsNullOrWhiteSpace(confirmation.DeviceSessionDir))
                {
                    await DispatchAsync(() => _latestDeviceRecordingSessionDir = confirmation.DeviceSessionDir).ConfigureAwait(false);
                }

                var detail = string.IsNullOrWhiteSpace(confirmation.DeviceSessionDir)
                    ? "Quest mirrored the participant session id/hash and reports device-side recording active."
                    : $"Quest mirrored the participant session id/hash and reports device-side recording active in {confirmation.DeviceSessionDir}.";
                return new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Quest-side participant recorder confirmed.",
                    detail);
            }

            await Task.Delay(150).ConfigureAwait(false);
        }

        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            "Participant run started, but Quest recorder confirmation is still missing.",
            BuildDeviceRecordingConfirmationTimeoutDetail(
                "The Start Experiment command succeeded, but no fresh committed quest_twin_state snapshot confirmed the expected session id/hash plus device-side recording activity within 6 seconds.",
                recordingSession.SessionId,
                recordingSession.DatasetHash,
                lastObservedState.ReportedSessionId,
                lastObservedState.ReportedDatasetHash,
                lastObservedState.DeviceRecordingActive,
                lastObservedState.DeviceSessionDir));
    }

    private void StartUpstreamLslMonitor(StudyDataRecordingSession recordingSession)
    {
        CancelUpstreamLslMonitor();
        var cts = new CancellationTokenSource();
        _upstreamLslMonitorCts = cts;
        _upstreamLslMonitorTask = Task.Factory.StartNew(
                () => MonitorUpstreamLslAsync(recordingSession, cts.Token),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap();
        QueueMachineLslStateRefresh();
    }

    private async Task MonitorUpstreamLslAsync(StudyDataRecordingSession recordingSession, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var subscription = new LslMonitorSubscription(
            HrvBiofeedbackStreamContract.StreamName,
            HrvBiofeedbackStreamContract.StreamType,
            HrvBiofeedbackStreamContract.DefaultChannelIndex);
        var observationSequence = 0;

        try
        {
            await foreach (var reading in _upstreamLslMonitorService.MonitorAsync(subscription, cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(reading.Status) &&
                    string.IsNullOrWhiteSpace(reading.Detail) &&
                    reading.Value is null &&
                    string.IsNullOrWhiteSpace(reading.TextValue))
                {
                    continue;
                }

                observationSequence++;
                recordingSession.RecordUpstreamLslObservation(new StudyUpstreamLslObservation(
                    reading.Timestamp,
                    reading.ObservedLocalClockSeconds,
                    reading.SampleTimestampSeconds,
                    HrvBiofeedbackStreamContract.StreamName,
                    HrvBiofeedbackStreamContract.StreamType,
                    HrvBiofeedbackStreamContract.DefaultChannelIndex,
                    reading.ChannelFormat,
                    reading.Value,
                    reading.TextValue,
                    observationSequence,
                    reading.Status,
                    reading.Detail,
                    reading.ResolvedSourceId));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await DispatchAsync(() => SetRecorderFault("Write passive upstream LSL monitor samples", exception)).ConfigureAwait(false);
        }
    }

    private OperationOutcome TryStartUpstreamLslMonitor(StudyDataRecordingSession recordingSession)
    {
        try
        {
            StartUpstreamLslMonitor(recordingSession);
            return new OperationOutcome(
                OperationOutcomeKind.Success,
                "Passive upstream LSL monitor armed.",
                $"The passive Windows-side monitor for {HrvBiofeedbackStreamContract.StreamName} / {HrvBiofeedbackStreamContract.StreamType} starts after the initial clock-alignment burst so it cannot stall the validation-capture startup path.");
        }
        catch (Exception exception)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Passive upstream LSL monitor did not start.",
                $"The Sussex run can continue, but the passive Windows-side LSL monitor could not be armed: {exception.Message}");
        }
    }

    private async Task<OperationOutcome> StopUpstreamLslMonitorAsync()
    {
        var cts = _upstreamLslMonitorCts;
        var task = _upstreamLslMonitorTask;
        _upstreamLslMonitorCts = null;
        _upstreamLslMonitorTask = null;

        if (cts is null && task is null)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Success,
                "Passive upstream LSL monitor already idle.",
                "No passive Windows-side HRV monitor task was running.");
        }

        cts?.Cancel();
        try
        {
            if (task is not null)
            {
                var completedTask = await Task.WhenAny(task, Task.Delay(WorkflowUpstreamMonitorStopTimeout)).ConfigureAwait(false);
                if (!ReferenceEquals(completedTask, task))
                {
                    return new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Passive upstream LSL monitor stop timed out.",
                        $"The passive Windows-side HRV monitor did not stop within {WorkflowUpstreamMonitorStopTimeout.TotalSeconds:0} seconds. The Sussex workflow can continue, but the monitor may still be unwinding.");
                }

                await task.ConfigureAwait(false);
            }

            return new OperationOutcome(
                OperationOutcomeKind.Success,
                "Passive upstream LSL monitor stopped.",
                $"Stopped the passive Windows-side monitor for {HrvBiofeedbackStreamContract.StreamName} / {HrvBiofeedbackStreamContract.StreamType}.");
        }
        catch (Exception exception)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Passive upstream LSL monitor stopped with issues.",
                exception.Message);
        }
        finally
        {
            cts?.Dispose();
            QueueMachineLslStateRefresh();
        }
    }

    private void StartBackgroundClockAlignmentMonitoring(StudyDataRecordingSession recordingSession)
    {
        CancelBackgroundClockAlignmentMonitoring();
        MarkValidationClockAlignmentBackgroundArmed();
        var cts = new CancellationTokenSource();
        _backgroundClockAlignmentCts = cts;
        _backgroundClockAlignmentTask = RunBackgroundClockAlignmentLoopAsync(recordingSession, cts.Token);
        QueueMachineLslStateRefresh();
    }

    private async Task RunBackgroundClockAlignmentLoopAsync(StudyDataRecordingSession recordingSession, CancellationToken cancellationToken)
    {
        try
        {
            var interval = TimeSpan.FromSeconds(WorkflowClockAlignmentBackgroundProbeIntervalSeconds);
            var nextProbeDueAtUtc = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(WorkflowClockAlignmentInitialBackgroundProbeDelaySeconds);
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = nextProbeDueAtUtc - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var outcome = await RunClockAlignmentAsync(
                        recordingSession,
                        StudyClockAlignmentWindowKind.BackgroundSparse,
                        showWindow: false,
                        updateUi: true,
                        durationOverride: WorkflowClockAlignmentSparseProbeDuration,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                recordingSession.RecordEvent(
                    "clock_alignment.background_sparse.result",
                    BuildRecorderEventDetail(outcome),
                    null,
                    outcome.Kind.ToString());
                nextProbeDueAtUtc += interval;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await DispatchAsync(() => SetRecorderFault("Run background clock-alignment probes", exception)).ConfigureAwait(false);
        }
    }

    private async Task<OperationOutcome> StopBackgroundClockAlignmentMonitoringAsync()
    {
        var cts = _backgroundClockAlignmentCts;
        var task = _backgroundClockAlignmentTask;
        _backgroundClockAlignmentCts = null;
        _backgroundClockAlignmentTask = null;

        if (cts is null && task is null)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Success,
                "Background clock-alignment monitor already idle.",
                "No sparse background clock-alignment loop was running.");
        }

        cts?.Cancel();
        try
        {
            if (task is not null)
            {
                var completedTask = await Task.WhenAny(task, Task.Delay(WorkflowClockAlignmentStopTimeout)).ConfigureAwait(false);
                if (!ReferenceEquals(completedTask, task))
                {
                    return new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Background clock-alignment monitor stop timed out.",
                        $"The sparse background probe did not stop within {WorkflowClockAlignmentStopTimeout.TotalSeconds:0} seconds. The Sussex run can continue, but the background probe may still be unwinding.");
                }

                await task.ConfigureAwait(false);
            }

            return new OperationOutcome(
                OperationOutcomeKind.Success,
                "Background clock-alignment monitor stopped.",
                $"Stopped the sparse background probe loop ({WorkflowClockAlignmentBackgroundProbeIntervalSeconds}s interval).");
        }
        catch (Exception exception)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Background clock-alignment monitor stopped with issues.",
                exception.Message);
        }
        finally
        {
            cts?.Dispose();
            QueueMachineLslStateRefresh();
        }
    }

    private void CancelExperimentTimingTasks()
    {
        CancelBackgroundClockAlignmentMonitoring();
        CancelUpstreamLslMonitor();
    }

    private void CancelBackgroundClockAlignmentMonitoring()
    {
        _backgroundClockAlignmentCts?.Cancel();
        _backgroundClockAlignmentCts?.Dispose();
        _backgroundClockAlignmentCts = null;
        _backgroundClockAlignmentTask = null;
    }

    private void CancelUpstreamLslMonitor()
    {
        _upstreamLslMonitorCts?.Cancel();
        _upstreamLslMonitorCts?.Dispose();
        _upstreamLslMonitorCts = null;
        _upstreamLslMonitorTask = null;
    }

    private void StartRegularRecordingSamples()
    {
        _recordingSampleTimer?.Stop();
        StopRegularRecordingSamples();
        if (_activeRecordingSession is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _recordingSampleLoopCts = cts;
        _recordingSampleLoopTask = Task.Run(() => RunRegularRecordingSampleLoopAsync(cts.Token), CancellationToken.None);
    }

    private void StopRegularRecordingSamples()
    {
        _recordingSampleTimer?.Stop();

        var cts = _recordingSampleLoopCts;
        _recordingSampleLoopCts = null;
        _recordingSampleLoopTask = null;

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task RunRegularRecordingSampleLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CaptureRegularRecordingSampleAsync(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(RecordingSampleInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await CaptureRegularRecordingSampleAsync(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CaptureRegularRecordingSampleAsync(DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
    {
        RecordingSampleSnapshot? snapshot;
        try
        {
            snapshot = await DispatchAsync(() => CaptureRegularRecordingSampleSnapshot(recordedAtUtc)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (snapshot is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            snapshot.Session.RecordTwinState(
                snapshot.TwinState,
                snapshot.RecordedAtUtc,
                snapshot.QuestSelector,
                snapshot.SourceTimestampUtc);
        }
        catch (Exception exception)
        {
            await DispatchAsync(() => SetRecorderFault("Write live Sussex telemetry", exception)).ConfigureAwait(false);
        }
    }

    private RecordingSampleSnapshot? CaptureRegularRecordingSampleSnapshot(DateTimeOffset recordedAtUtc)
    {
        if (_activeRecordingSession is null || _reportedTwinState.Count == 0)
        {
            return null;
        }

        TryRecordActiveSessionCommandObservations(recordedAtUtc);
        return new RecordingSampleSnapshot(
            _activeRecordingSession,
            new Dictionary<string, string>(_reportedTwinState, StringComparer.OrdinalIgnoreCase),
            recordedAtUtc,
            (_twinBridge as LslTwinModeBridge)?.LastStateReceivedAt ?? recordedAtUtc,
            ResolveHeadsetActionSelector());
    }

    private async Task<OperationOutcome> RunClockAlignmentAsync(
        StudyDataRecordingSession recordingSession,
        StudyClockAlignmentWindowKind windowKind,
        bool showWindow = true,
        bool updateUi = true,
        TimeSpan? durationOverride = null,
        CancellationToken cancellationToken = default)
    {
        var duration = durationOverride ?? TimeSpan.FromSeconds(WorkflowClockAlignmentDurationSeconds);
        var probeInterval = TimeSpan.FromMilliseconds(SussexClockAlignmentStreamContract.DefaultProbeIntervalMilliseconds);
        var firstProbeSequence = ReserveClockAlignmentProbeSequenceRange(duration, probeInterval);
        var expectedProbeCount = GetExpectedClockAlignmentProbeCount(duration, probeInterval);
        var request = new StudyClockAlignmentRunRequest(
            recordingSession.SessionId,
            recordingSession.DatasetHash,
            windowKind,
            duration,
            probeInterval,
            TimeSpan.FromMilliseconds(SussexClockAlignmentStreamContract.DefaultEchoGraceMilliseconds),
            firstProbeSequence);
        var windowToken = BuildClockAlignmentWindowToken(windowKind);
        var windowLabel = BuildClockAlignmentWindowLabel(windowKind);
        var timeout = request.Duration + request.EchoGracePeriod + WorkflowClockAlignmentRunSafetyMargin;

        if (!_clockAlignmentService.RuntimeState.Available)
        {
            var unavailableOutcome = new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Clock alignment unavailable on this machine.",
                _clockAlignmentService.RuntimeState.Detail);
            if (updateUi)
            {
                await DispatchAsync(() =>
                {
                    ClockAlignmentLevel = unavailableOutcome.Kind;
                    ClockAlignmentRunning = false;
                    ClockAlignmentSummary = unavailableOutcome.Summary;
                    ClockAlignmentDetail = unavailableOutcome.Detail;
                    ClockAlignmentProgressPercent = 0d;
                    ClockAlignmentProgressLabel = $"{windowLabel} did not run.";
                    ClockAlignmentProbeStatsLabel = "No probes sent.";
                    ClockAlignmentOffsetStatsLabel = "Offset estimate unavailable.";
                    ClockAlignmentRoundTripStatsLabel = "Round-trip estimate unavailable.";
                    UpdateValidationClockAlignmentStage(windowKind, unavailableOutcome.Kind, unavailableOutcome.Summary, unavailableOutcome.Detail);
                    UpdateParticipantSessionState();
                    RefreshBenchToolsStatus();
                }).ConfigureAwait(false);
            }
            return unavailableOutcome;
        }

        recordingSession.RecordEvent(
            $"clock_alignment.{windowToken}.started",
            $"{windowLabel} started for {request.Duration.TotalSeconds:0.#} seconds using probe sequences {firstProbeSequence.ToString(CultureInfo.InvariantCulture)}-{(firstProbeSequence + expectedProbeCount - 1).ToString(CultureInfo.InvariantCulture)}.",
            null,
            "pending");

        if (updateUi)
        {
            await DispatchAsync(() =>
            {
                ResetClockAlignmentStateForRun(windowKind, request.Duration);
                if (showWindow && !SuppressClockAlignmentWindows)
                {
                    OpenClockAlignmentWindow();
                }

                UpdateParticipantSessionState();
                RefreshBenchToolsStatus();
            }).ConfigureAwait(false);
        }

        var progress = new Progress<StudyClockAlignmentProgress>(update =>
        {
            if (!updateUi)
            {
                return;
            }

            _ = DispatchAsync(() =>
            {
                ApplyClockAlignmentProgress(windowKind, update);
                UpdateParticipantSessionState();
                RefreshBenchToolsStatus();
            });
        });

        StudyClockAlignmentRunResult result;
        using var alignmentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        alignmentCts.CancelAfter(timeout);
        try
        {
            result = await _clockAlignmentService.RunAsync(request, progress, alignmentCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (alignmentCts.IsCancellationRequested)
        {
            var timeoutOutcome = windowKind == StudyClockAlignmentWindowKind.BackgroundSparse && cancellationToken.IsCancellationRequested
                ? new OperationOutcome(
                    OperationOutcomeKind.Preview,
                    "Background drift probe stopped for wrap-up.",
                    "The current sparse background probe was interrupted because the companion was starting the end burst. Earlier completed background drift results remain valid for this run.")
                : new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    $"{windowLabel} timed out.",
                    cancellationToken.IsCancellationRequested
                        ? $"{windowLabel} was canceled while the companion was stopping the background timing probe."
                        : $"{windowLabel} did not finish within {timeout.TotalSeconds:0} seconds, so the companion continued without waiting forever.");
            if (updateUi)
            {
                await DispatchAsync(() =>
                {
                    ClockAlignmentLevel = timeoutOutcome.Kind;
                    ClockAlignmentRunning = false;
                    ClockAlignmentSummary = timeoutOutcome.Summary;
                    ClockAlignmentDetail = timeoutOutcome.Detail;
                    ClockAlignmentProgressLabel = $"{windowLabel} timed out.";
                    UpdateValidationClockAlignmentStage(windowKind, timeoutOutcome.Kind, timeoutOutcome.Summary, timeoutOutcome.Detail);
                    UpdateParticipantSessionState();
                    RefreshBenchToolsStatus();
                }).ConfigureAwait(false);
            }
            return timeoutOutcome;
        }
        catch (Exception exception)
        {
            var failureOutcome = new OperationOutcome(
                OperationOutcomeKind.Failure,
                $"{windowLabel} failed.",
                exception.Message);
            if (updateUi)
            {
                await DispatchAsync(() =>
                {
                    ClockAlignmentLevel = failureOutcome.Kind;
                    ClockAlignmentRunning = false;
                    ClockAlignmentSummary = failureOutcome.Summary;
                    ClockAlignmentDetail = failureOutcome.Detail;
                    ClockAlignmentProgressLabel = $"{windowLabel} aborted.";
                    UpdateValidationClockAlignmentStage(windowKind, failureOutcome.Kind, failureOutcome.Summary, failureOutcome.Detail);
                    UpdateParticipantSessionState();
                    RefreshBenchToolsStatus();
                }).ConfigureAwait(false);
            }
            return failureOutcome;
        }

        try
        {
            foreach (var sample in result.Samples)
            {
                recordingSession.RecordClockAlignmentSample(sample);
            }

            recordingSession.UpdateClockAlignmentSummary(windowKind, result.Summary);
        }
        catch (Exception exception)
        {
            await DispatchAsync(() => SetRecorderFault("Write clock alignment samples", exception)).ConfigureAwait(false);
            var persistenceOutcome = new OperationOutcome(
                OperationOutcomeKind.Failure,
                $"{windowLabel} ran, but the Windows recorder could not persist the samples.",
                exception.Message);
            if (updateUi)
            {
                await DispatchAsync(() =>
                {
                    ClockAlignmentLevel = persistenceOutcome.Kind;
                    ClockAlignmentRunning = false;
                    ClockAlignmentSummary = persistenceOutcome.Summary;
                    ClockAlignmentDetail = persistenceOutcome.Detail;
                    ClockAlignmentProgressLabel = $"{windowLabel} samples could not be written.";
                    UpdateValidationClockAlignmentStage(windowKind, persistenceOutcome.Kind, persistenceOutcome.Summary, persistenceOutcome.Detail);
                    UpdateParticipantSessionState();
                    RefreshBenchToolsStatus();
                }).ConfigureAwait(false);
            }
            return persistenceOutcome;
        }

        if (updateUi)
        {
            await DispatchAsync(() =>
            {
                ApplyClockAlignmentOutcome(windowKind, result);
                UpdateClockAlignmentConsistencyTelemetry(windowKind, result);
                UpdateParticipantSessionState();
                RefreshBenchToolsStatus();
            }).ConfigureAwait(false);
        }

        if (showWindow && !SuppressClockAlignmentWindows && updateUi && result.Outcome.Kind == OperationOutcomeKind.Success)
        {
            await DispatchAsync(CloseClockAlignmentWindow).ConfigureAwait(false);
        }

        return result.Outcome;
    }

    private async Task<OperationOutcome> ConfirmDeviceRecordingStoppedAsync(
        StudyDataRecordingSession recordingSession,
        TwinSnapshotGate snapshotGate,
        DateTimeOffset commandIssuedAtUtc)
    {
        var timeoutAtUtc = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTimeOffset.UtcNow < timeoutAtUtc)
        {
            var confirmation = await DispatchAsync(() =>
            {
                var currentRevision = (_twinBridge as LslTwinModeBridge)?.LastCommittedSnapshotRevision;
                var currentCommittedAtUtc = (_twinBridge as LslTwinModeBridge)?.LastCommittedSnapshotReceivedAt;
                var sessionId = GetFirstValue("study.session.id");
                var datasetHash = GetFirstValue("study.session.dataset_hash");
                var deviceRecordingActive = ParseBool(GetFirstValue("study.recording.device.active")) == true;
                var experimentActive = ParseBool(GetFirstValue("study.session.experiment_active")) == true;
                var deviceSessionDir = GetFirstValue("study.recording.device.session_dir");
                var recorderFaultActive = ParseBool(GetFirstValue("study.recording.device.fault_active")) == true;
                var recorderFaultDetail = GetFirstValue("study.recording.device.fault_detail");
                var matchesSession =
                    string.IsNullOrWhiteSpace(sessionId) ||
                    string.Equals(sessionId, recordingSession.SessionId, StringComparison.Ordinal);
                var matchesHash =
                    string.IsNullOrWhiteSpace(datasetHash) ||
                    string.Equals(datasetHash, recordingSession.DatasetHash, StringComparison.OrdinalIgnoreCase);
                var hasFreshSnapshot = HasFreshCommittedTwinSnapshot(
                    snapshotGate,
                    commandIssuedAtUtc,
                    currentRevision,
                    currentCommittedAtUtc);

                return (
                    HasFreshSnapshot: hasFreshSnapshot,
                    Confirmed: !deviceRecordingActive && !experimentActive && matchesSession && matchesHash,
                    DeviceSessionDir: deviceSessionDir,
                    RecorderFaultActive: recorderFaultActive,
                    RecorderFaultDetail: recorderFaultDetail);
            }).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(confirmation.DeviceSessionDir))
            {
                await DispatchAsync(() => _latestDeviceRecordingSessionDir = confirmation.DeviceSessionDir).ConfigureAwait(false);
            }

            if (confirmation.RecorderFaultActive)
            {
                var detail = string.IsNullOrWhiteSpace(confirmation.RecorderFaultDetail)
                    ? "Quest reported an explicit device-side recorder fault while stopping the participant session."
                    : $"Quest reported an explicit device-side recorder fault while stopping the participant session: {confirmation.RecorderFaultDetail}";
                return new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Quest-side participant recorder stop failed.",
                    detail);
            }

            if (confirmation.HasFreshSnapshot && confirmation.Confirmed)
            {
                var detail = string.IsNullOrWhiteSpace(confirmation.DeviceSessionDir)
                    ? "Quest reports the participant recorder inactive and the experiment session ended."
                    : $"Quest reports the participant recorder inactive and the experiment session ended. Last device session folder: {confirmation.DeviceSessionDir}.";
                return new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Quest-side participant recorder stopped.",
                    detail);
            }

            await Task.Delay(150).ConfigureAwait(false);
        }

        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            "End Experiment was sent, but Quest recorder stop confirmation is still missing.",
            "The command completed, but no fresh committed quest_twin_state snapshot confirmed the expected stopped recorder state within 6 seconds.");
    }

    private readonly record struct TwinSnapshotGate(string? Revision, DateTimeOffset? CommittedAtUtc);

    private static string BuildDeviceRecordingConfirmationTimeoutDetail(
        string baselineDetail,
        string expectedSessionId,
        string expectedDatasetHash,
        string? reportedSessionId,
        string? reportedDatasetHash,
        bool deviceRecordingActive,
        string? deviceSessionDir)
    {
        var detail = new StringBuilder(baselineDetail);
        detail.Append(" Expected session_id=`");
        detail.Append(expectedSessionId);
        detail.Append("`, dataset_hash=`");
        detail.Append(expectedDatasetHash);
        detail.Append("`.");
        detail.Append(" Latest reported device.active=");
        detail.Append(deviceRecordingActive ? "true" : "false");
        detail.Append(", session_id=`");
        detail.Append(string.IsNullOrWhiteSpace(reportedSessionId) ? "n/a" : reportedSessionId);
        detail.Append("`, dataset_hash=`");
        detail.Append(string.IsNullOrWhiteSpace(reportedDatasetHash) ? "n/a" : reportedDatasetHash);
        detail.Append("`.");

        if (!string.IsNullOrWhiteSpace(deviceSessionDir))
        {
            detail.Append(" Latest device session dir: ");
            detail.Append(deviceSessionDir);
            detail.Append('.');
        }

        return detail.ToString();
    }

    private void ResetClockAlignmentStateForRun(StudyClockAlignmentWindowKind windowKind, TimeSpan duration)
    {
        var label = BuildClockAlignmentWindowLabel(windowKind);
        ClockAlignmentLevel = OperationOutcomeKind.Preview;
        ClockAlignmentRunning = true;
        ClockAlignmentSummary = $"{label} is running for {duration.TotalSeconds:0.#} seconds.";
        ClockAlignmentDetail = windowKind switch
        {
            StudyClockAlignmentWindowKind.StartBurst => "The companion is sending dedicated Sussex clock probes and waiting for the Quest echo stream so the clocks can be aligned before the main run continues.",
            StudyClockAlignmentWindowKind.EndBurst => "The companion is capturing a matching end-of-run clock-alignment burst before it stops the Sussex runtime so clock drift over the session can be compared.",
            _ => "The companion is sending a sparse background clock probe to track Quest-minus-Windows clock drift during the run."
        };
        ClockAlignmentProgressPercent = 0d;
        ClockAlignmentProgressLabel = $"Starting {label}.";
        ClockAlignmentProbeStatsLabel = "Probes sent: 0 | Quest echoes: 0";
        ClockAlignmentOffsetStatsLabel = "Offset estimate pending.";
        ClockAlignmentRoundTripStatsLabel = "Round-trip estimate pending.";
        UpdateValidationClockAlignmentStage(windowKind, OperationOutcomeKind.Preview, $"{label} running.", ClockAlignmentDetail);
        if (ValidationCaptureRunning)
        {
            switch (windowKind)
            {
                case StudyClockAlignmentWindowKind.StartBurst:
                    SetValidationCaptureProgress(0d, "Phase 1 of 4: running the start clock-alignment burst.");
                    break;
                case StudyClockAlignmentWindowKind.EndBurst:
                    SetValidationCaptureProgress(100d, "Phase 3 of 4: running the end clock-alignment burst.");
                    break;
            }
        }
    }

    private void ApplyClockAlignmentProgress(StudyClockAlignmentWindowKind windowKind, StudyClockAlignmentProgress progress)
    {
        ClockAlignmentLevel = OperationOutcomeKind.Preview;
        ClockAlignmentRunning = true;
        ClockAlignmentSummary = $"{BuildClockAlignmentWindowLabel(windowKind)} in progress.";
        ClockAlignmentDetail = progress.Detail;
        ClockAlignmentProgressPercent = Math.Clamp(progress.PercentComplete, 0d, 100d);
        ClockAlignmentProgressLabel = $"Progress {ClockAlignmentProgressPercent:0}% | Probes {progress.ProbesSent} | Echoes {progress.EchoesReceived}";
        ClockAlignmentProbeStatsLabel = $"Probes sent: {progress.ProbesSent} | Quest echoes: {progress.EchoesReceived}";
    }

    private void ApplyClockAlignmentOutcome(StudyClockAlignmentWindowKind windowKind, StudyClockAlignmentRunResult result)
    {
        ClockAlignmentLevel = result.Outcome.Kind;
        ClockAlignmentRunning = false;
        ClockAlignmentSummary = result.Outcome.Kind == OperationOutcomeKind.Success
            ? $"{BuildClockAlignmentWindowLabel(windowKind)} completed."
            : result.Outcome.Summary;
        ClockAlignmentDetail = result.Outcome.Detail;
        ClockAlignmentProgressPercent = result.Summary.ProbesSent > 0 ? 100d : 0d;
        ClockAlignmentProgressLabel = result.Summary.EchoesReceived > 0
            ? $"Completed with {result.Summary.EchoesReceived} echoed probe(s) out of {result.Summary.ProbesSent}."
            : "No Quest echoes were captured during the alignment window.";
        ClockAlignmentProbeStatsLabel = $"Probes sent: {result.Summary.ProbesSent} | Quest echoes: {result.Summary.EchoesReceived}";
        ClockAlignmentOffsetStatsLabel = BuildClockAlignmentOffsetStatsLabel(result.Summary);
        ClockAlignmentRoundTripStatsLabel = BuildClockAlignmentRoundTripStatsLabel(result.Summary);
        UpdateValidationClockAlignmentStage(windowKind, ClockAlignmentLevel, ClockAlignmentSummary, ClockAlignmentDetail);
    }

    private static string BuildClockAlignmentWindowLabel(StudyClockAlignmentWindowKind windowKind)
        => windowKind switch
        {
            StudyClockAlignmentWindowKind.StartBurst => "Start clock-alignment burst",
            StudyClockAlignmentWindowKind.EndBurst => "End clock-alignment burst",
            _ => "Background clock-alignment probe"
        };

    private static string BuildClockAlignmentWindowToken(StudyClockAlignmentWindowKind windowKind)
        => windowKind switch
        {
            StudyClockAlignmentWindowKind.StartBurst => "start_burst",
            StudyClockAlignmentWindowKind.EndBurst => "end_burst",
            _ => "background_sparse"
        };

    private static string BuildClockAlignmentOffsetStatsLabel(StudyClockAlignmentSummary summary)
    {
        if (summary.RecommendedQuestMinusWindowsClockSeconds is not double recommendedOffset)
        {
            return "Offset estimate unavailable.";
        }

        var builder = new StringBuilder();
        builder.Append("Quest-minus-Windows monotonic offset ");
        builder.Append(FormatClockAlignmentOffset(recommendedOffset));
        if (summary.MedianQuestMinusWindowsClockSeconds is double medianOffset)
        {
            builder.Append(" | Median ");
            builder.Append(FormatClockAlignmentOffset(medianOffset));
        }

        if (summary.MeanQuestMinusWindowsClockSeconds is double meanOffset)
        {
            builder.Append(" | Mean ");
            builder.Append(FormatClockAlignmentOffset(meanOffset));
        }

        return builder.ToString();
    }

    private static string BuildClockAlignmentRoundTripStatsLabel(StudyClockAlignmentSummary summary)
    {
        if (summary.MeanRoundTripSeconds is not double meanRoundTrip)
        {
            return "Round-trip estimate unavailable.";
        }

        var builder = new StringBuilder();
        builder.Append("Mean RTT ");
        builder.Append(FormatClockAlignmentMilliseconds(meanRoundTrip));
        if (summary.MinRoundTripSeconds is double minRoundTrip)
        {
            builder.Append(" | Min ");
            builder.Append(FormatClockAlignmentMilliseconds(minRoundTrip));
        }

        if (summary.MaxRoundTripSeconds is double maxRoundTrip)
        {
            builder.Append(" | Max ");
            builder.Append(FormatClockAlignmentMilliseconds(maxRoundTrip));
        }

        return builder.ToString();
    }

    private void UpdateClockAlignmentConsistencyTelemetry(StudyClockAlignmentWindowKind windowKind, StudyClockAlignmentRunResult result)
    {
        var latestMetrics = BuildClockAlignmentConsistencyProbeMetrics(result);
        if (windowKind == StudyClockAlignmentWindowKind.BackgroundSparse)
        {
            if (_recentBackgroundClockAlignmentMetrics.Count >= ClockAlignmentConsistencyHistoryLimit)
            {
                _recentBackgroundClockAlignmentMetrics.Dequeue();
            }

            _recentBackgroundClockAlignmentMetrics.Enqueue(latestMetrics);
        }

        var metrics = _recentBackgroundClockAlignmentMetrics.Count > 0
            ? _recentBackgroundClockAlignmentMetrics.ToArray()
            : latestMetrics.EchoesReceived > 0
                ? [latestMetrics]
                : [];

        if (metrics.Length == 0)
        {
            ClockAlignmentConsistencyLevel = result.Outcome.Kind == OperationOutcomeKind.Success
                ? OperationOutcomeKind.Preview
                : result.Outcome.Kind;
            ClockAlignmentConsistencySummary = result.Outcome.Kind == OperationOutcomeKind.Success
                ? "Full-loop timing consistency is waiting for echoed probes."
                : result.Outcome.Summary;
            ClockAlignmentConsistencyDetail = result.Outcome.Detail;
            ClockAlignmentConsistencyMetricsLabel = "No echoed full-loop RTT samples yet.";
            return;
        }

        var sourceLabel = _recentBackgroundClockAlignmentMetrics.Count > 0
            ? $"{_recentBackgroundClockAlignmentMetrics.Count} recent background probe(s)"
            : windowKind == StudyClockAlignmentWindowKind.StartBurst
                ? "the start burst"
                : "the latest alignment burst";
        var steadyStateMeanRtts = metrics
            .Where(metric => metric.SteadyStateMeanRoundTripSeconds.HasValue)
            .Select(metric => metric.SteadyStateMeanRoundTripSeconds!.Value)
            .ToArray();
        var rawMeanRtts = metrics
            .Where(metric => metric.RawMeanRoundTripSeconds.HasValue)
            .Select(metric => metric.RawMeanRoundTripSeconds!.Value)
            .ToArray();
        var totalProbes = metrics.Sum(metric => metric.ProbesSent);
        var totalEchoes = metrics.Sum(metric => metric.EchoesReceived);
        var steadyStateMeanOfMeans = steadyStateMeanRtts.Length > 0 ? steadyStateMeanRtts.Average() : (double?)null;
        var rawMeanOfMeans = rawMeanRtts.Length > 0 ? rawMeanRtts.Average() : (double?)null;
        var worstWithinProbeSpan = metrics
            .Where(metric => metric.SteadyStateSpanSeconds.HasValue)
            .Select(metric => metric.SteadyStateSpanSeconds!.Value)
            .DefaultIfEmpty(0d)
            .Max();
        var worstRawWithinProbeSpan = metrics
            .Where(metric => metric.RawWithinProbeSpanSeconds.HasValue)
            .Select(metric => metric.RawWithinProbeSpanSeconds!.Value)
            .DefaultIfEmpty(0d)
            .Max();
        var probeToProbeMeanSpan = steadyStateMeanRtts.Length > 1 ? steadyStateMeanRtts.Max() - steadyStateMeanRtts.Min() : 0d;
        var coldStartOverheadSeconds = metrics
            .Where(metric => metric.RawMeanRoundTripSeconds.HasValue && metric.SteadyStateMeanRoundTripSeconds.HasValue)
            .Select(metric => Math.Max(0d, metric.RawMeanRoundTripSeconds!.Value - metric.SteadyStateMeanRoundTripSeconds!.Value))
            .DefaultIfEmpty(0d)
            .Average();
        var lowCoverage = totalEchoes <= 0 || totalEchoes < Math.Max(2, totalProbes / 2);

        var level = lowCoverage
            ? OperationOutcomeKind.Warning
            : steadyStateMeanOfMeans is > ClockAlignmentConsistencyFailureMeanRoundTripSeconds
              || worstWithinProbeSpan > ClockAlignmentConsistencyFailureSpanSeconds
              || probeToProbeMeanSpan > ClockAlignmentConsistencyFailureProbeToProbeSeconds
                ? OperationOutcomeKind.Failure
                : steadyStateMeanOfMeans is > ClockAlignmentConsistencyWarningMeanRoundTripSeconds
                  || worstWithinProbeSpan > ClockAlignmentConsistencyWarningSpanSeconds
                  || probeToProbeMeanSpan > ClockAlignmentConsistencyWarningProbeToProbeSeconds
                    ? OperationOutcomeKind.Warning
                    : OperationOutcomeKind.Success;

        ClockAlignmentConsistencyLevel = level;
        ClockAlignmentConsistencySummary = level switch
        {
            OperationOutcomeKind.Success => "Full-loop RTT looks stable.",
            OperationOutcomeKind.Failure => "Full-loop RTT looks unstable.",
            _ => "Full-loop RTT needs attention."
        };
        ClockAlignmentConsistencyMetricsLabel =
            $"{sourceLabel}: steady mean {(steadyStateMeanOfMeans is double steadyMean ? FormatClockAlignmentMilliseconds(steadyMean) : "n/a")} | " +
            $"steady span {FormatClockAlignmentMilliseconds(worstWithinProbeSpan)} | " +
            $"probe-to-probe span {FormatClockAlignmentMilliseconds(probeToProbeMeanSpan)}";
        ClockAlignmentConsistencyDetail =
            $"{sourceLabel} returned {totalEchoes} echo(es) from {totalProbes} sent probe(s). " +
            $"Steady-state RTT {(steadyStateMeanOfMeans is double computedSteadyMean ? FormatClockAlignmentMilliseconds(computedSteadyMean) : "n/a")}. " +
            $"Raw mean RTT {(rawMeanOfMeans is double computedRawMean ? FormatClockAlignmentMilliseconds(computedRawMean) : "n/a")}. " +
            $"Worst steady-state within-probe RTT span {FormatClockAlignmentMilliseconds(worstWithinProbeSpan)}. " +
            $"Worst raw within-probe RTT span {FormatClockAlignmentMilliseconds(worstRawWithinProbeSpan)}. " +
            $"Probe-to-probe steady-state mean RTT span {FormatClockAlignmentMilliseconds(probeToProbeMeanSpan)}. " +
            (coldStartOverheadSeconds > 0d
                ? $"Average cold-start overhead above the steady-state band was {FormatClockAlignmentMilliseconds(coldStartOverheadSeconds)}. "
                : string.Empty) +
            (level switch
            {
                OperationOutcomeKind.Success => " The current Wi-Fi loop timing looks steady enough for session overlay work.",
                OperationOutcomeKind.Failure => " The current Wi-Fi loop timing looks unstable enough to risk overlay drift or delayed command effects.",
                _ => " Watch the network and headset connection path if this stays elevated across multiple probes."
            });
    }

    private static string FormatClockAlignmentMilliseconds(double seconds)
        => $"{seconds * 1000d:0.0} ms";

    private static string FormatClockAlignmentOffset(double seconds)
    {
        var magnitude = TimeSpan.FromSeconds(Math.Abs(seconds));
        var sign = seconds < 0d ? "-" : "+";
        if (magnitude.TotalHours >= 1d)
        {
            return $"{sign}{Math.Floor(magnitude.TotalHours):0}h {magnitude.Minutes:00}m {magnitude.Seconds:00}.{magnitude.Milliseconds:000}s";
        }

        if (magnitude.TotalMinutes >= 1d)
        {
            return $"{sign}{magnitude.Minutes:0}m {magnitude.Seconds:00}.{magnitude.Milliseconds:000}s";
        }

        return FormatClockAlignmentMilliseconds(seconds);
    }

    private static ClockAlignmentConsistencyProbeMetrics BuildClockAlignmentConsistencyProbeMetrics(StudyClockAlignmentRunResult result)
    {
        if (result.Samples.Count == 0)
        {
            return new ClockAlignmentConsistencyProbeMetrics(
                result.Summary.ProbesSent,
                result.Summary.EchoesReceived,
                null,
                result.Summary.MeanRoundTripSeconds,
                null,
                BuildRawWithinProbeSpan(result.Summary));
        }

        var orderedByRoundTrip = result.Samples
            .OrderBy(sample => sample.RoundTripSeconds)
            .ToArray();
        var steadyStateCount = Math.Max(1, (int)Math.Ceiling(orderedByRoundTrip.Length * 0.25d));
        var steadyStateSamples = orderedByRoundTrip
            .Take(steadyStateCount)
            .ToArray();
        var steadyStateMean = steadyStateSamples.Average(sample => sample.RoundTripSeconds);
        var steadyStateSpan = steadyStateSamples.Length > 1
            ? steadyStateSamples[^1].RoundTripSeconds - steadyStateSamples[0].RoundTripSeconds
            : 0d;

        return new ClockAlignmentConsistencyProbeMetrics(
            result.Summary.ProbesSent,
            result.Summary.EchoesReceived,
            steadyStateMean,
            result.Summary.MeanRoundTripSeconds,
            steadyStateSpan,
            BuildRawWithinProbeSpan(result.Summary));
    }

    private static double? BuildRawWithinProbeSpan(StudyClockAlignmentSummary summary)
        => summary.MinRoundTripSeconds.HasValue && summary.MaxRoundTripSeconds.HasValue
            ? summary.MaxRoundTripSeconds.Value - summary.MinRoundTripSeconds.Value
            : null;

    private sealed record ClockAlignmentConsistencyProbeMetrics(
        int ProbesSent,
        int EchoesReceived,
        double? SteadyStateMeanRoundTripSeconds,
        double? RawMeanRoundTripSeconds,
        double? SteadyStateSpanSeconds,
        double? RawWithinProbeSpanSeconds);

    private int ReserveClockAlignmentProbeSequenceRange(TimeSpan duration, TimeSpan probeInterval)
    {
        var firstProbeSequence = Math.Max(1, _nextClockAlignmentProbeSequence);
        _nextClockAlignmentProbeSequence = checked(firstProbeSequence + GetExpectedClockAlignmentProbeCount(duration, probeInterval));
        return firstProbeSequence;
    }

    private static int GetExpectedClockAlignmentProbeCount(TimeSpan duration, TimeSpan probeInterval)
    {
        if (duration <= TimeSpan.Zero)
        {
            return 1;
        }

        if (probeInterval <= TimeSpan.Zero)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Ceiling(duration.TotalMilliseconds / probeInterval.TotalMilliseconds));
    }

    private void ResetRecordingTelemetryTracking()
    {
        _recentBackgroundClockAlignmentMetrics.Clear();
        ClockAlignmentConsistencyLevel = OperationOutcomeKind.Preview;
        ClockAlignmentConsistencySummary = "Full-loop timing consistency has not been sampled yet.";
        ClockAlignmentConsistencyDetail = "Start a participant run to collect background clock-alignment probes and watch for unstable loop timing.";
        ClockAlignmentConsistencyMetricsLabel = "No background RTT consistency samples yet.";
        _lastRecordedRecenterConfirmationSignature = string.Empty;
        _lastRecordedRecenterEffectSignature = string.Empty;
        _lastRecordedParticlesConfirmationSignature = string.Empty;
        _lastRecordedParticlesEffectSignature = string.Empty;
        _nextClockAlignmentProbeSequence = 1;
    }

    private void TryRecordLiveTwinState(DateTimeOffset recordedAtUtc)
    {
        if (_activeRecordingSession is null || _reportedTwinState.Count == 0)
        {
            return;
        }

        try
        {
            var sourceTimestampUtc = (_twinBridge as LslTwinModeBridge)?.LastStateReceivedAt ?? recordedAtUtc;
            _activeRecordingSession.RecordTwinState(
                _reportedTwinState,
                recordedAtUtc,
                ResolveHeadsetActionSelector(),
                sourceTimestampUtc);
        }
        catch (Exception exception)
        {
            SetRecorderFault("Write live Sussex telemetry", exception);
        }
    }

    private void TryRecordLiveTimingMarker(TwinTimingMarkerEvent marker)
    {
        if (_activeRecordingSession is null)
        {
            return;
        }

        try
        {
            _activeRecordingSession.RecordTimingMarker(marker);
        }
        catch (Exception exception)
        {
            SetRecorderFault("Write live Sussex timing marker", exception);
        }
    }

    private void TryRecordActiveSessionCommandObservations(DateTimeOffset observedAtUtc)
    {
        if (_activeRecordingSession is null || _reportedTwinState.Count == 0)
        {
            return;
        }

        try
        {
            TryRecordRecenterCommandObservation(_activeRecordingSession, observedAtUtc);
            TryRecordParticleCommandObservation(_activeRecordingSession, observedAtUtc);
        }
        catch (Exception exception)
        {
            SetRecorderFault("Write session command observation", exception);
        }
    }

    private void TryRecordRecenterCommandObservation(StudyDataRecordingSession recordingSession, DateTimeOffset observedAtUtc)
    {
        var request = _lastRecenterCommandRequest;
        if (request is null)
        {
            return;
        }

        var confirmation = CaptureCommandConfirmation(_study.Controls.RecenterCommandActionId);
        if (IsCommandConfirmed(request, confirmation))
        {
            var confirmationSignature = BuildCommandObservationSignature(request, confirmation.Sequence, confirmation.TimestampRaw);
            if (!string.Equals(confirmationSignature, _lastRecordedRecenterConfirmationSignature, StringComparison.Ordinal))
            {
                recordingSession.RecordEvent(
                    "command.recenter.confirmed",
                    BuildCommandTrackingDetail(request, confirmation, "recenter"),
                    request.ActionId,
                    "success",
                    recordedAtUtc: observedAtUtc,
                    sourceTimestampUtc: confirmation.Timestamp);
                _lastRecordedRecenterConfirmationSignature = confirmationSignature;
            }
        }

        var distance = ParseDouble(GetFirstValue(_study.Monitoring.RecenterDistanceKeys));
        var effect = CaptureRecenterEffect(request, distance);
        if (!effect.Observed)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lastRecordedRecenterEffectSignature))
        {
            return;
        }

        var effectSourceTimestampUtc = effect.AnchorTimestamp ?? GetLatestTwinStateSourceTimestamp() ?? observedAtUtc;
        var effectSignature = BuildCommandObservationSignature(
            request,
            confirmation.Sequence,
            effect.AnchorTimestampRaw,
            effect.AnchorUpdated ? "true" : "false",
            effect.DistanceImproved ? "true" : "false");
        recordingSession.RecordEvent(
            "command.recenter.effect_observed",
            BuildRecenterEffectDetail(effect),
            request.ActionId,
            "observed",
            recordedAtUtc: observedAtUtc,
            sourceTimestampUtc: effectSourceTimestampUtc);
        _lastRecordedRecenterEffectSignature = effectSignature;
    }

    private void TryRecordParticleCommandObservation(StudyDataRecordingSession recordingSession, DateTimeOffset observedAtUtc)
    {
        var request = _lastParticlesCommandRequest;
        if (request is null)
        {
            return;
        }

        var confirmation = CaptureCommandConfirmation(_study.Controls.ParticleVisibleOnActionId);
        if (IsCommandConfirmed(request, confirmation))
        {
            var confirmationSignature = BuildCommandObservationSignature(request, confirmation.Sequence, confirmation.TimestampRaw);
            if (!string.Equals(confirmationSignature, _lastRecordedParticlesConfirmationSignature, StringComparison.Ordinal))
            {
                recordingSession.RecordEvent(
                    "command.particles.confirmed",
                    BuildCommandTrackingDetail(request, confirmation, "particle visibility"),
                    request.ActionId,
                    "success",
                    recordedAtUtc: observedAtUtc,
                    sourceTimestampUtc: confirmation.Timestamp);
                _lastRecordedParticlesConfirmationSignature = confirmationSignature;
            }
        }

        var actualVisible = GetCurrentReportedParticleVisibility();
        var effect = CaptureParticleVisibilityEffect(request, actualVisible);
        if (!effect.Observed)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lastRecordedParticlesEffectSignature))
        {
            return;
        }

        var effectSignature = BuildCommandObservationSignature(
            request,
            confirmation.Sequence,
            confirmation.TimestampRaw,
            effect.PreviousVisible?.ToString(),
            effect.CurrentVisible?.ToString());
        var effectSourceTimestampUtc = GetLatestTwinStateSourceTimestamp() ?? observedAtUtc;
        if (string.Equals(effectSignature, _lastRecordedParticlesEffectSignature, StringComparison.Ordinal))
        {
            return;
        }

        recordingSession.RecordEvent(
            "command.particles.effect_observed",
            BuildParticleVisibilityEffectDetail(effect),
            request.ActionId,
            "observed",
            recordedAtUtc: observedAtUtc,
            sourceTimestampUtc: effectSourceTimestampUtc);
        _lastRecordedParticlesEffectSignature = effectSignature;
    }

    private DateTimeOffset? GetLatestTwinStateSourceTimestamp()
        => (_twinBridge as LslTwinModeBridge)?.LastStateReceivedAt;

    private static string BuildCommandObservationSignature(
        StudyTwinCommandRequest request,
        int? confirmationSequence,
        params string?[] values)
    {
        var builder = new StringBuilder()
            .Append(request.ActionId)
            .Append('|')
            .Append(request.SentAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
            .Append('|')
            .Append(confirmationSequence?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        foreach (var value in values)
        {
            builder.Append('|').Append(value ?? string.Empty);
        }

        return builder.ToString();
    }

    private async Task TryArchiveQuestScreenshotAsync(string screenshotPath, DateTimeOffset capturedAtUtc)
    {
        var recordingSession = await DispatchAsync(() => _activeRecordingSession).ConfigureAwait(false);
        if (recordingSession is null || string.IsNullOrWhiteSpace(screenshotPath) || !File.Exists(screenshotPath))
        {
            return;
        }

        try
        {
            var artifactName = $"quest-screenshot-{capturedAtUtc:yyyyMMddTHHmmssZ}{Path.GetExtension(screenshotPath)}";
            recordingSession.CopyArtifact(screenshotPath, artifactName);
            recordingSession.RecordEvent(
                "artifact.quest_screenshot",
                $"Archived Quest screenshot as {artifactName}.",
                null,
                "success",
                capturedAtUtc);
        }
        catch (Exception exception)
        {
            await DispatchAsync(() => SetRecorderFault("Archive Quest screenshot", exception)).ConfigureAwait(false);
        }
    }

    private void SetRecorderFault(string stage, Exception exception)
    {
        CancelExperimentTimingTasks();
        StopRegularRecordingSamples();
        _lastCompletedRecordingFolderPath = _activeRecordingSession?.SessionFolderPath ?? _lastCompletedRecordingFolderPath;
        _activeRecordingSession?.Dispose();
        _activeRecordingSession = null;
        _participantRunStopping = false;
        _recorderFaultDetail = $"{stage}: {exception.Message}";
        UpdateParticipantSessionState();
        RefreshBenchToolsStatus();
        AppendLog(
            OperatorLogLevel.Failure,
            "Study recorder faulted.",
            _recorderFaultDetail);
    }

    private void RefreshConditionSelectionState()
    {
        OnPropertyChanged(nameof(HasConditions));
        if (!HasConditions)
        {
            ConditionLevel = OperationOutcomeKind.Preview;
            ConditionSummary = "No Sussex conditions configured.";
            ConditionDetail = "This study shell can run without a named condition selector.";
            SelectedConditionVisualProfileLabel = "Visual profile: n/a";
            SelectedConditionControllerBreathingProfileLabel = "Breathing profile: n/a";
            OnPropertyChanged(nameof(CanApplySelectedCondition));
            ApplySelectedConditionCommand?.RaiseCanExecuteChanged();
            return;
        }

        if (SelectedCondition is null)
        {
            ConditionLevel = OperationOutcomeKind.Warning;
            ConditionSummary = "Choose a Sussex condition.";
            ConditionDetail = "Each condition links one visual profile and one controller-breathing profile.";
            SelectedConditionVisualProfileLabel = "Visual profile: n/a";
            SelectedConditionControllerBreathingProfileLabel = "Breathing profile: n/a";
            OnPropertyChanged(nameof(CanApplySelectedCondition));
            ApplySelectedConditionCommand?.RaiseCanExecuteChanged();
            return;
        }

        ConditionLevel = OperationOutcomeKind.Preview;
        ConditionSummary = $"{SelectedCondition.Label} selected.";
        ConditionDetail = string.IsNullOrWhiteSpace(SelectedCondition.Description)
            ? "Apply this condition before starting the participant recording."
            : $"{SelectedCondition.Description} Apply this condition before starting the participant recording.";
        SelectedConditionVisualProfileLabel = $"Visual profile: {SelectedCondition.VisualProfileId}";
        SelectedConditionControllerBreathingProfileLabel = $"Breathing profile: {SelectedCondition.ControllerBreathingProfileId}";
        OnPropertyChanged(nameof(CanApplySelectedCondition));
        ApplySelectedConditionCommand?.RaiseCanExecuteChanged();
    }

    private void OnActiveConditionsChanged(object? sender, IReadOnlyList<StudyConditionDefinition> activeConditions)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => ReplaceActiveConditions(activeConditions));
            return;
        }

        ReplaceActiveConditions(activeConditions);
    }

    private void ReplaceActiveConditions(IReadOnlyList<StudyConditionDefinition> activeConditions)
    {
        var selectedId = SelectedCondition?.Id;
        Conditions.Clear();
        foreach (var condition in activeConditions)
        {
            Conditions.Add(condition);
        }

        SelectedCondition = Conditions.FirstOrDefault(condition => string.Equals(condition.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                            ?? Conditions.FirstOrDefault();
        RefreshConditionSelectionState();
    }

    private void SetConditionStatus(OperationOutcomeKind level, string summary, string detail)
    {
        ConditionLevel = level;
        ConditionSummary = summary;
        ConditionDetail = detail;
        OnPropertyChanged(nameof(CanApplySelectedCondition));
        ApplySelectedConditionCommand?.RaiseCanExecuteChanged();
    }

    private void UpdateParticipantSessionState()
    {
        OnPropertyChanged(nameof(CanApplySelectedCondition));
        ApplySelectedConditionCommand?.RaiseCanExecuteChanged();
        StudyParticipantStatus? participantStatus = null;
        var normalizedParticipantId = string.Empty;
        if (!string.IsNullOrWhiteSpace(_participantIdDraft))
        {
            try
            {
                normalizedParticipantId = StudyDataRecorderService.NormalizeParticipantId(_participantIdDraft);
                participantStatus = _studyDataRecorderService.GetParticipantStatus(_study.Id, normalizedParticipantId);
            }
            catch (ArgumentException)
            {
                participantStatus = null;
            }
        }

        ParticipantHasExistingSessions = participantStatus?.HasExistingSessions == true;
        ParticipantExistingSessionsLabel = participantStatus?.HasExistingSessions == true
            ? $"Existing local sessions: {string.Join(", ", participantStatus.ExistingSessionIds)}"
            : "No previous participant sessions found on this machine.";

        if (string.IsNullOrWhiteSpace(normalizedParticipantId))
        {
            ParticipantEntryLevel = OperationOutcomeKind.Preview;
            ParticipantEntrySummary = "Enter a participant number before starting recorded data collection.";
            ParticipantEntryDetail = "Duplicate participant ids will warn but will not block the run.";
        }
        else if (_activeRecordingSession is not null)
        {
            ParticipantEntryLevel = ParticipantHasExistingSessions ? OperationOutcomeKind.Warning : OperationOutcomeKind.Success;
            ParticipantEntrySummary = $"Participant {normalizedParticipantId} is locked into the active recording session.";
            ParticipantEntryDetail = ParticipantHasExistingSessions
                ? $"Duplicate-id warning acknowledged. {ParticipantExistingSessionsLabel}"
                : "The active run keeps this participant id attached until the recorder stops.";
        }
        else if (ParticipantHasExistingSessions)
        {
            ParticipantEntryLevel = OperationOutcomeKind.Warning;
            ParticipantEntrySummary = $"Participant {normalizedParticipantId} already has local study data on this machine.";
            ParticipantEntryDetail = $"{ParticipantExistingSessionsLabel} The workflow will still allow a new run.";
        }
        else
        {
            ParticipantEntryLevel = OperationOutcomeKind.Success;
            ParticipantEntrySummary = $"Participant {normalizedParticipantId} is ready for the next run.";
            ParticipantEntryDetail = "No duplicate participant folder was found under the local Sussex study-data root.";
        }

        if (!string.IsNullOrWhiteSpace(_recorderFaultDetail))
        {
            RecorderStateLabel = "Faulted";
            RecordingLevel = OperationOutcomeKind.Failure;
            RecordingSummary = "Recorder faulted during the Sussex workflow.";
            RecordingDetail = string.IsNullOrWhiteSpace(_lastCompletedRecordingFolderPath)
                ? _recorderFaultDetail
                : $"{_recorderFaultDetail} Last session folder: {_lastCompletedRecordingFolderPath}";
            RecordingSessionLabel = string.IsNullOrWhiteSpace(_lastCompletedRecordingFolderPath)
                ? "Recorder faulted before a session folder could be confirmed."
                : $"Last session folder: {_lastCompletedRecordingFolderPath}";
            RecordingFolderPath = _lastCompletedRecordingFolderPath;
            RecordingDevicePullFolderPath = _lastCompletedRecordingDevicePullFolderPath;
            RecordingPdfPath = _lastCompletedRecordingPdfPath;
            return;
        }

        if (_participantRunStopping)
        {
            RecorderStateLabel = "Stopping";
            RecordingLevel = OperationOutcomeKind.Warning;
            RecordingSummary = "Recorder is closing the participant run.";
            RecordingDetail = "Finish the wrap-up commands and wait for the session files to flush to disk.";
            RecordingSessionLabel = _activeRecordingSession is null
                ? "Stopping after the last participant session."
                : $"Active session: {_activeRecordingSession.SessionId}";
            RecordingFolderPath = _activeRecordingSession?.SessionFolderPath ?? _lastCompletedRecordingFolderPath;
            RecordingDevicePullFolderPath = string.Empty;
            RecordingPdfPath = string.Empty;
            return;
        }

        if (_activeRecordingSession is not null)
        {
            RecorderStateLabel = ClockAlignmentRunning ? "Aligning" : "Recording";
            RecordingLevel = ClockAlignmentRunning ? OperationOutcomeKind.Preview : OperationOutcomeKind.Success;
            RecordingSummary = ClockAlignmentRunning
                ? $"Recording participant {_activeRecordingSession.ParticipantId} and aligning clocks."
                : $"Recording participant {_activeRecordingSession.ParticipantId}.";
            RecordingDetail = ClockAlignmentRunning
                ? $"Writing session events, long-form signals, breathing trace, passive upstream LSL observations, Quest timing markers, clock alignment samples, settings snapshot, and screenshots to {_activeRecordingSession.SessionFolderPath}. {ClockAlignmentDetail}"
                : $"Writing session events, long-form signals, breathing trace, passive upstream LSL observations, Quest timing markers, clock alignment samples, settings snapshot, and screenshots to {_activeRecordingSession.SessionFolderPath}. Quest backup files will be pulled into device-session-pull when recording stops.";
            RecordingSessionLabel = $"Active session: {_activeRecordingSession.SessionId}";
            RecordingFolderPath = _activeRecordingSession.SessionFolderPath;
            RecordingDevicePullFolderPath = string.Empty;
            RecordingPdfPath = string.Empty;
            return;
        }

        if (!string.IsNullOrWhiteSpace(normalizedParticipantId))
        {
            RecorderStateLabel = "Armed";
            RecordingLevel = ParticipantHasExistingSessions ? OperationOutcomeKind.Warning : OperationOutcomeKind.Success;
            RecordingSummary = ParticipantHasExistingSessions
                ? "Recorder armed with a duplicate-id warning."
                : "Recorder armed for the next participant run.";
            RecordingDetail = ParticipantHasExistingSessions
                ? $"{ParticipantExistingSessionsLabel} Starting the run from Experiment Session will still create a new session folder."
                : "Starting the run from Experiment Session will create a timestamped session folder with settings, events, signals, breathing trace files, pulled Quest backup files, and a formatted session review PDF when recording stops.";
            RecordingSessionLabel = string.IsNullOrWhiteSpace(_lastCompletedRecordingFolderPath)
                ? "No previous participant session completed in this shell instance."
                : $"Last completed session: {_lastCompletedRecordingFolderPath}";
            RecordingFolderPath = _lastCompletedRecordingFolderPath;
            RecordingDevicePullFolderPath = _lastCompletedRecordingDevicePullFolderPath;
            RecordingPdfPath = _lastCompletedRecordingPdfPath;
            return;
        }

        RecorderStateLabel = "Idle";
        RecordingLevel = OperationOutcomeKind.Preview;
        RecordingSummary = "Recorder idle.";
        RecordingDetail = "Open Experiment Session and enter a participant number to arm the recorder for the next participant run.";
        RecordingSessionLabel = string.IsNullOrWhiteSpace(_lastCompletedRecordingFolderPath)
            ? "No participant session has been recorded from this shell instance yet."
            : $"Last completed session: {_lastCompletedRecordingFolderPath}";
        RecordingFolderPath = _lastCompletedRecordingFolderPath;
        RecordingDevicePullFolderPath = _lastCompletedRecordingDevicePullFolderPath;
        RecordingPdfPath = _lastCompletedRecordingPdfPath;
    }

    private void UpdateWorkflowGuideState()
    {
        var stepIndex = Math.Clamp(_workflowGuideStepIndex, 0, WorkflowGuideCatalog.Length - 1);
        var stepChanged = stepIndex != _workflowGuideLastRenderedStepIndex;
        if (stepIndex != _workflowGuideStepIndex)
        {
            _workflowGuideStepIndex = stepIndex;
            OnPropertyChanged(nameof(WorkflowGuideStepIndex));
        }

        if (stepChanged)
        {
            _workflowGuideLastRenderedStepIndex = stepIndex;
            if (stepIndex == 9)
            {
                _workflowGuideParticleStepStartedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        var definition = WorkflowGuideCatalog[stepIndex];
        WorkflowGuideStepLabel = $"Step {definition.Number} of {WorkflowGuideCatalog.Length}";
        WorkflowGuideStepTitle = definition.Title;
        WorkflowGuideStepExplanation = definition.Explanation;

        var gate = BuildWorkflowGuideGateState(stepIndex);
        WorkflowGuideStepLevel = gate.Level;
        WorkflowGuideStepSummary = gate.Summary;
        WorkflowGuideStepDetail = gate.Detail;
        WorkflowGuideGateSummary = WorkflowGuideIsFinalStep
            ? "Finish the cleanup below, then open Experiment Session or return to the main window."
            : gate.Ready
                ? "This step is ready. You can continue."
                : stepIndex == 6
                    ? "This is a manual step. Continue once the operator has finished the boundary."
                    : "Finish this step before continuing.";
        WorkflowGuideGateDetail = WorkflowGuideIsFinalStep
            ? "Reset calibration, make sure particles are off, then open Experiment Session for the real participant or use Return To Main Window."
            : gate.Ready
                ? "Use Next to move to the next operator step."
                : stepIndex == 6
                    ? "The app does not verify the Guardian boundary automatically. The experimenter must confirm it manually."
                    : "Next stays disabled until the required live check or manual action is satisfied.";

        var checks = BuildWorkflowGuideCheckItems(stepIndex);
        var actions = BuildWorkflowGuideActionItems(stepIndex);
        ReplaceWorkflowGuideCheckItems(WorkflowGuideChecks, checks);
        ReplaceWorkflowGuideActionItems(WorkflowGuideActions, actions);
        ReplaceWorkflowGuideCheckItems(ValidationClockAlignmentChecks, BuildValidationClockAlignmentCheckItems());
        UpdateWorkflowGuideQuestScreenshotState(stepIndex);
        UpdateWorkflowGuideNextActionState(stepIndex, actions);
        UpdateWorkflowGuideActionFeedback(stepIndex, actions);

        OnPropertyChanged(nameof(CanGoToPreviousWorkflowGuideStep));
        OnPropertyChanged(nameof(CanGoToNextWorkflowGuideStep));
        OnPropertyChanged(nameof(WorkflowGuideIsFinalStep));
        OnPropertyChanged(nameof(WorkflowGuideShowsParticipantEntry));
        OnPropertyChanged(nameof(WorkflowGuideShowsRecordingState));
        OnPropertyChanged(nameof(WorkflowGuideShowsDeviceProfileRows));
        OnPropertyChanged(nameof(WorkflowGuideShowsCalibrationTelemetry));
        OnPropertyChanged(nameof(WorkflowGuideShowsWindowsEnvironmentAnalysis));
        OnPropertyChanged(nameof(WorkflowGuideShowsQuestScreenshotVerification));
        OnPropertyChanged(nameof(WorkflowGuideShowsValidationCaptureState));
        OnPropertyChanged(nameof(ValidationCaptureActionSummary));
        OnPropertyChanged(nameof(CanOpenWorkflowGuideQuestScreenshot));
        OnPropertyChanged(nameof(CanRunWorkflowValidationCapture));
    }

    private void UpdateWorkflowGuideActionFeedback(int stepIndex, IReadOnlyList<WorkflowGuideActionItem> visibleActions)
    {
        var runningAction = visibleActions.FirstOrDefault(action => action.IsRunning);
        if (runningAction is not null)
        {
            WorkflowGuideActionLevel = OperationOutcomeKind.Preview;
            WorkflowGuideActionSummary = runningAction.Label;
            WorkflowGuideActionDetail = "The click was accepted. Wait for the command to finish and for the live checks below to update.";
            return;
        }

        var latestConfirmation = CaptureLatestActionConfirmation();
        if (_lastStudyTwinCommandRequest is not null
            && DateTimeOffset.UtcNow - _lastStudyTwinCommandRequest.SentAtUtc <= WorkflowGuidePendingCommandWindow
            && IsCommandPending(_lastStudyTwinCommandRequest, latestConfirmation))
        {
            WorkflowGuideActionLevel = OperationOutcomeKind.Warning;
            WorkflowGuideActionSummary = $"{_lastStudyTwinCommandRequest.Label} was sent. Waiting for headset confirmation.";
            WorkflowGuideActionDetail = $"{BuildCommandTrackingDetail(_lastStudyTwinCommandRequest, latestConfirmation, "command")} {BuildTwinCommandTransportDetail()}".Trim();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_questVisualConfirmationPendingReason))
        {
            WorkflowGuideActionLevel = OperationOutcomeKind.Warning;
            WorkflowGuideActionSummary = "The command finished, but visual confirmation is still pending.";
            WorkflowGuideActionDetail = $"{_questVisualConfirmationPendingReason} Use Capture Quest Screenshot or the headset view to confirm what is actually visible.";
            return;
        }

        if (stepIndex == 1)
        {
            var wifiAdbGate = BuildWifiAdbWorkflowGuideGateState();
            WorkflowGuideActionLevel = wifiAdbGate.Ready ? OperationOutcomeKind.Success : wifiAdbGate.Level;
            WorkflowGuideActionSummary = wifiAdbGate.Ready
                ? "Wi-Fi ADB is ready."
                : wifiAdbGate.Summary;
            WorkflowGuideActionDetail = wifiAdbGate.Ready
                ? "Use Next to move to the Wi-Fi match step. After that, the guide will keep checking that the transport stays on the Wi-Fi endpoint."
                : wifiAdbGate.Detail;
            return;
        }

        if (stepIndex == 2)
        {
            var wifiMatchGate = BuildWifiMatchWorkflowGuideGateState();
            WorkflowGuideActionLevel = wifiMatchGate.Level;
            WorkflowGuideActionSummary = wifiMatchGate.Summary;
            WorkflowGuideActionDetail = wifiMatchGate.Ready
                ? "The headset Wi-Fi and the PC Wi-Fi already match. Use Next to continue."
                : wifiMatchGate.Detail;
            return;
        }

        if (stepIndex == 9)
        {
            if (BuildParticleWorkflowGuideGateState().Ready && !HasWorkflowGuideParticleStepScreenshot())
            {
                WorkflowGuideActionLevel = OperationOutcomeKind.Warning;
                WorkflowGuideActionSummary = "Visual confirmation still needed.";
                WorkflowGuideActionDetail = "After turning particles on and off, capture one Quest screenshot below and review the preview before you continue.";
                return;
            }

            if (HasWorkflowGuideParticleStepScreenshot())
            {
                WorkflowGuideActionLevel = OperationOutcomeKind.Success;
                WorkflowGuideActionSummary = "Visual confirmation captured.";
                WorkflowGuideActionDetail = "Review the screenshot preview below to confirm the particle scene looked correct.";
                return;
            }
        }

        if (stepIndex == 3)
        {
            var wifiOnlyGate = BuildUsbDisconnectWorkflowGuideGateState();
            WorkflowGuideActionLevel = wifiOnlyGate.Level;
            WorkflowGuideActionSummary = wifiOnlyGate.Summary;
            WorkflowGuideActionDetail = wifiOnlyGate.Ready
                ? "Wi-Fi ADB is established. Use Next to continue, and keep watching the connection card for the Wi-Fi endpoint in later steps."
                : wifiOnlyGate.Detail;
            return;
        }

        if (stepIndex == 6)
        {
            WorkflowGuideActionLevel = OperationOutcomeKind.Preview;
            WorkflowGuideActionSummary = "This step is manual.";
            WorkflowGuideActionDetail = "Draw the boundary on the headset, then use Next once the experiment area is covered.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(LastActionLabel))
        {
            WorkflowGuideActionLevel = LastActionLevel;
            WorkflowGuideActionSummary = BuildWorkflowGuideLastActionSummary(LastActionLabel, LastActionLevel);
            WorkflowGuideActionDetail = string.IsNullOrWhiteSpace(LastActionDetail)
                ? "The last workflow action completed without additional detail."
                : LastActionDetail;
            return;
        }

        WorkflowGuideActionLevel = OperationOutcomeKind.Preview;
        WorkflowGuideActionSummary = visibleActions.Count == 0
            ? "No app button is needed for this step."
            : "Use the step button below once.";
        WorkflowGuideActionDetail = visibleActions.Count == 0
            ? "This part of the onboarding is operator-only."
            : "The guide will show immediately when a click was accepted and whether it is still waiting for headset confirmation.";
    }

    private void UpdateWorkflowGuideNextActionState(int stepIndex, IReadOnlyList<WorkflowGuideActionItem> visibleActions)
    {
        var runningAction = visibleActions.FirstOrDefault(action => action.IsRunning);
        if (runningAction is not null)
        {
            WorkflowGuideNextActionSummary = "Wait for the current action to finish.";
            WorkflowGuideNextActionDetail = "Do not click again until the live checks or the feedback card updates.";
            return;
        }

        if (visibleActions.Count == 0)
        {
            WorkflowGuideNextActionSummary = "No app action is needed for this step.";
            WorkflowGuideNextActionDetail = "Finish the manual operator step on the headset, then use Next once the goal state above is satisfied.";
            return;
        }

        (WorkflowGuideNextActionSummary, WorkflowGuideNextActionDetail) = stepIndex switch
        {
            0 => (
                "Probe USB, then refresh if the live link still looks stale.",
                "The guide only needs one confirmed USB probe before Wi-Fi ADB can be enabled."),
            1 => (
                "Enable Wi-Fi ADB.",
                "If the session does not switch over cleanly, use Connect Quest on the saved endpoint instead of relying on USB."),
            2 => (
                "Refresh the snapshot and compare Wi-Fi names.",
                "If the headset and PC Wi-Fi names differ, change the headset Wi-Fi manually in-headset before continuing."),
            3 => (
                "Keep the active transport on Wi-Fi ADB.",
                "If the connection card stops showing the Wi-Fi endpoint, use Connect Quest to restore it before continuing."),
            4 => (
                "Install the pinned Sussex APK if needed, then refresh.",
                "This step is only about the Sussex APK identity. Bench advisories are reviewed separately afterward."),
            5 => (
                "Apply the pinned device profile, then refresh.",
                "Confirm the pinned settings and battery floors here. Remaining bench advisories stay visible but do not block Sussex."),
            6 => (
                "Draw the experiment boundary in-headset.",
                "Cover the participant position, the experimenter position, and the full experiment area before using Next."),
            7 => (
                "Launch Sussex from the guide.",
                $"{LaunchSleepBlockInstruction} {LaunchVisualBlockInstruction} Disable proximity before launching, wake the right controller first, confirm the connection still shows the Wi-Fi endpoint, and do not rely on the launch path to disable the controller Meta/menu button on the current Meta OS build."),
            8 => (
                "Turn on the test LSL sender if needed, then run Probe Connection or Analyze Windows Environment.",
                "Probe Connection checks the headset path. Analyze Windows Environment checks the Windows tooling, liblsl runtimes, twin bridge, and whether the expected upstream sender is visible on this PC. Keep the headset on-face or leave the keep-awake proximity override active while you probe."),
            9 => (
                "Send Particles On, confirm visually, then send Particles Off.",
                "This is still a recommended bench-confidence check even though it no longer blocks the guide. If the headset is off-face, keep the proximity override active or the return path can go stale between screenshots."),
            10 => (
                "Choose the calibration mode you want, then start calibration only if you need the optional bench readback.",
                "Dynamic motion axis solves the axis from recorded movement. Fixed controller orientation keeps the warmed-up controller axis. Calibration remains optional on the current Sussex path, but wake/proximity drift can still make the telemetry verdict look unstable."),
            11 => (
                "Enter a temporary validation id and run the 20 second capture.",
                "This step uses the dedicated validation card below rather than the generic action buttons. Keep the headset awake through the capture so the recorder and twin-state do not stall mid-run."),
            _ => (
                "Reset calibration and leave particles off.",
                "Open Experiment Session or return to the main runtime tab with a clean participant-ready state. Restore normal proximity later if you want the wear sensor back.")
        };
    }

    private void UpdateWorkflowGuideQuestScreenshotState(int stepIndex)
    {
        if (stepIndex != 9)
        {
            WorkflowGuideQuestScreenshotLevel = QuestScreenshotLevel;
            WorkflowGuideQuestScreenshotSummary = QuestScreenshotSummary;
            WorkflowGuideQuestScreenshotDetail = QuestScreenshotDetail;
            WorkflowGuideQuestScreenshotPath = string.Empty;
            WorkflowGuideQuestScreenshotPreview = null;
            return;
        }

        if (!_hzdbService.IsAvailable)
        {
            WorkflowGuideQuestScreenshotLevel = OperationOutcomeKind.Preview;
            WorkflowGuideQuestScreenshotSummary = "Quest screenshot capture unavailable.";
            WorkflowGuideQuestScreenshotDetail = "Run guided setup or install the official Quest tooling cache before using Quest screenshot capture in this verification step.";
            WorkflowGuideQuestScreenshotPath = string.Empty;
            WorkflowGuideQuestScreenshotPreview = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(ResolveQuestScreenshotSelector()))
        {
            WorkflowGuideQuestScreenshotLevel = OperationOutcomeKind.Preview;
            WorkflowGuideQuestScreenshotSummary = "Quest screenshot capture needs a headset selector.";
            WorkflowGuideQuestScreenshotDetail = "Probe USB or connect the Quest first so the guide can request a fresh Quest screenshot.";
            WorkflowGuideQuestScreenshotPath = string.Empty;
            WorkflowGuideQuestScreenshotPreview = null;
            return;
        }

        if (HasWorkflowGuideParticleStepScreenshot())
        {
            WorkflowGuideQuestScreenshotLevel = OperationOutcomeKind.Success;
            WorkflowGuideQuestScreenshotSummary = $"Particle-step screenshot captured at {_lastQuestScreenshotCapturedAtUtc!.Value.ToLocalTime():HH:mm:ss}.";
            WorkflowGuideQuestScreenshotDetail = "Use this preview to visually confirm that the particle scene looked correct during this step.";
            WorkflowGuideQuestScreenshotPath = QuestScreenshotPath;
            WorkflowGuideQuestScreenshotPreview = QuestScreenshotPreview;
            return;
        }

        WorkflowGuideQuestScreenshotLevel = OperationOutcomeKind.Warning;
        WorkflowGuideQuestScreenshotSummary = "Visual confirmation still needed.";
        WorkflowGuideQuestScreenshotDetail = string.IsNullOrWhiteSpace(QuestScreenshotPath)
            ? "After turning particles on and off, capture one Quest screenshot here and review it before continuing."
            : "The last Quest screenshot was captured before this particle-verification step. Capture a new one here to verify the current scene.";
        WorkflowGuideQuestScreenshotPath = string.Empty;
        WorkflowGuideQuestScreenshotPreview = null;
    }

    private bool HasWorkflowGuideParticleStepScreenshot()
        => _workflowGuideParticleStepStartedAtUtc.HasValue
            && _lastQuestScreenshotCapturedAtUtc.HasValue
            && _lastQuestScreenshotCapturedAtUtc.Value >= _workflowGuideParticleStepStartedAtUtc.Value
            && !string.IsNullOrWhiteSpace(QuestScreenshotPath);

    private bool IsWorkflowGuideStepReady(int stepIndex)
        => BuildWorkflowGuideGateState(stepIndex).Ready;

    private WorkflowGuideGateState BuildWorkflowGuideGateState(int stepIndex)
    {
        var deviceProfileState = EvaluateDeviceProfileGateState();
        var headsetBatteryState = EvaluateHeadsetBatteryGateState();
        var rightControllerBatteryState = EvaluateRightControllerBatteryGateState();

        return stepIndex switch
        {
            0 => BuildUsbWorkflowGuideGateState(),
            1 => BuildWifiAdbWorkflowGuideGateState(),
            2 => BuildWifiMatchWorkflowGuideGateState(),
            3 => BuildUsbDisconnectWorkflowGuideGateState(),
            4 => RequireWifiAdbForWorkflowStep(BuildInstalledApkWorkflowGuideGateState()),
            5 => RequireWifiAdbForWorkflowStep(BuildProfileWorkflowGuideGateState(deviceProfileState, headsetBatteryState, rightControllerBatteryState)),
            6 => RequireWifiAdbForWorkflowStep(new WorkflowGuideGateState(
                OperationOutcomeKind.Preview,
                "Boundary is a manual step.",
                "Ask the experimenter to draw a comfortable Guardian boundary that covers the participant position, the experimenter position, and the full experiment area before moving on.",
                true)),
            7 => RequireWifiAdbForWorkflowStep(BuildKioskWorkflowGuideGateState()),
            8 => RequireWifiAdbForWorkflowStep(BuildLslWorkflowGuideGateState()),
            9 => RequireWifiAdbForWorkflowStep(BuildParticleWorkflowGuideGateState()),
            10 => RequireWifiAdbForWorkflowStep(BuildCalibrationWorkflowGuideGateState()),
            11 => RequireWifiAdbForWorkflowStep(BuildValidationCaptureWorkflowGuideGateState()),
            _ => RequireWifiAdbForWorkflowStep(new WorkflowGuideGateState(
                OperationOutcomeKind.Preview,
                "Final manual cleanup step.",
                "Reset calibration, make sure particles are off, and then open Experiment Session or return to the main window so the real participant session can start from a clean state.",
                true))
        };
    }

    private WorkflowGuideGateState BuildUsbWorkflowGuideGateState()
    {
        var hasSavedUsbSerial = !string.IsNullOrWhiteSpace(_appSessionState.LastUsbSerial);
        var probeSucceeded = string.Equals(_lastConnectionActionLabel, "Probe USB", StringComparison.Ordinal)
            && _lastConnectionLevel == OperationOutcomeKind.Success;
        var ready = probeSucceeded || (hasSavedUsbSerial && _headsetStatus?.IsConnected == true);
        var level = ready
            ? OperationOutcomeKind.Success
            : string.Equals(_lastConnectionActionLabel, "Probe USB", StringComparison.Ordinal)
                && _lastConnectionLevel == OperationOutcomeKind.Failure
                ? OperationOutcomeKind.Failure
                : OperationOutcomeKind.Warning;
        var detail = ready
            ? $"USB visibility is confirmed via {_appSessionState.LastUsbSerial ?? "the last detected serial"}. Keep the cable attached until Wi-Fi ADB is enabled."
            : "Connect the headset over USB, accept the in-headset debugging prompt if Meta shows one, then press Probe USB.";
        return new WorkflowGuideGateState(level, ready ? "USB ADB confirmed." : "USB ADB is not confirmed yet.", detail, ready);
    }

    private WorkflowGuideGateState BuildWifiAdbWorkflowGuideGateState()
    {
        var ready = _headsetStatus?.IsWifiAdbTransport == true;
        var level = ready
            ? OperationOutcomeKind.Success
            : (string.Equals(_lastConnectionActionLabel, "Enable Wi-Fi ADB", StringComparison.Ordinal)
                || string.Equals(_lastConnectionActionLabel, "Connect Quest", StringComparison.Ordinal))
                && _lastConnectionLevel == OperationOutcomeKind.Failure
                ? OperationOutcomeKind.Failure
                : OperationOutcomeKind.Warning;
        var detail = ready
            ? "Wi-Fi ADB is active. Continue to the Wi-Fi match step. After that, the guide will keep checking that the transport stays on the Wi-Fi endpoint."
            : (string.Equals(_lastConnectionActionLabel, "Connect Quest", StringComparison.Ordinal)
                || string.Equals(_lastConnectionActionLabel, "Enable Wi-Fi ADB", StringComparison.Ordinal))
                && _lastConnectionLevel == OperationOutcomeKind.Failure
                && !string.IsNullOrWhiteSpace(_lastConnectionDetail)
                    ? _lastConnectionDetail
                    : "Press Enable Wi-Fi ADB while the USB cable is still attached. Once this turns green, continue to Wi-Fi matching.";
        return new WorkflowGuideGateState(level, ready ? "Wi-Fi ADB is active." : "Wi-Fi ADB is not active yet.", detail, ready);
    }

    private WorkflowGuideGateState BuildWifiMatchWorkflowGuideGateState()
    {
        var match = _headsetStatus?.WifiSsidMatchesHost;
        var routedTopologyAccepted = _questWifiTransportDiagnostics?.RoutedTopologyAccepted == true;
        if (_questWifiTransportDiagnostics is { Level: OperationOutcomeKind.Failure } wifiTransport)
        {
            return new WorkflowGuideGateState(
                OperationOutcomeKind.Failure,
                wifiTransport.Summary,
                $"{HeadsetWifiSummary} {HostWifiSummary} {wifiTransport.Detail}",
                false);
        }

        if (routedTopologyAccepted)
        {
            var routedDetail = _questWifiTransportDiagnostics is { Level: OperationOutcomeKind.Warning } wifiTransportWarning
                ? $"{HeadsetWifiSummary} {HostWifiSummary} {wifiTransportWarning.Detail}"
                : $"{HeadsetWifiSummary} {HostWifiSummary} Matching Wi-Fi names are not required here because the current routed host path can already reach the Quest endpoint.";
            return new WorkflowGuideGateState(
                _questWifiTransportDiagnostics?.Level == OperationOutcomeKind.Warning
                    ? OperationOutcomeKind.Warning
                    : OperationOutcomeKind.Success,
                "Quest router path is valid.",
                routedDetail,
                true);
        }

        var level = match switch
        {
            true => OperationOutcomeKind.Success,
            false => OperationOutcomeKind.Failure,
            _ => OperationOutcomeKind.Warning
        };
        var detail = match switch
        {
            true => _questWifiTransportDiagnostics is { Level: OperationOutcomeKind.Warning } wifiTransportWarning
                ? $"{HeadsetWifiSummary} {HostWifiSummary} {wifiTransportWarning.Detail}"
                : $"{HeadsetWifiSummary} {HostWifiSummary}",
            false => $"{HeadsetWifiSummary} {HostWifiSummary} Change the headset Wi-Fi manually inside the headset. Remote Wi-Fi switching is not considered reliable for the experiment path.",
            _ => "Refresh the headset snapshot until both the headset Wi-Fi and the PC Wi-Fi names are visible. If they differ, change the headset Wi-Fi manually."
        };
        return new WorkflowGuideGateState(level, WifiNetworkMatchSummary, detail, match == true);
    }

    private WorkflowGuideGateState BuildUsbDisconnectWorkflowGuideGateState()
    {
        var ready = _headsetStatus?.IsWifiAdbTransport == true;
        var level = ready
            ? OperationOutcomeKind.Success
            : _headsetStatus?.IsConnected == true
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Failure;
        var detail = ready
            ? _headsetStatus?.IsUsbAdbVisible == true
                ? $"Wi-Fi ADB is active on {_headsetStatus.ConnectionLabel}, and USB {_headsetStatus.VisibleUsbSerial} is still visible. Sussex now treats USB visibility as advisory only, but later steps should keep reporting the Wi-Fi endpoint as the active transport."
                : $"Remote control is still alive on {_headsetStatus?.ConnectionLabel ?? "the saved Wi-Fi endpoint"}. Keep watching the connection card so the remaining guided steps continue to use Wi-Fi ADB."
            : _headsetStatus?.IsConnected == true
                    ? $"Current transport is {_headsetStatus.ConnectionLabel}. Restore the saved Wi-Fi ADB endpoint before continuing so the remaining guided steps exercise the same transport used during the study."
                    : "The headset is not reachable right now. Use Connect Quest to restore the saved Wi-Fi ADB endpoint before continuing.";
        return new WorkflowGuideGateState(
            level,
            ready ? "Wi-Fi ADB continuity confirmed." : "Wi-Fi ADB continuity still needs confirmation.",
            detail,
            ready);
    }

    private WorkflowGuideGateState RequireWifiAdbForWorkflowStep(WorkflowGuideGateState stepState)
    {
        if (_headsetStatus?.IsWifiAdbTransport == true)
        {
            return stepState;
        }

        var detail = _headsetStatus?.IsConnected == true
            ? $"Current transport is {_headsetStatus.ConnectionLabel}. Restore the Wi-Fi ADB session before continuing. {stepState.Detail}"
            : $"The headset is not currently reachable over Wi-Fi ADB. Restore the Wi-Fi session first, then continue. {stepState.Detail}";

        return new WorkflowGuideGateState(
            stepState.Level == OperationOutcomeKind.Failure
                ? OperationOutcomeKind.Failure
                : OperationOutcomeKind.Warning,
            "Wi-Fi ADB is required for this step.",
            detail,
            false);
    }

    private WorkflowGuideGateState BuildInstalledApkWorkflowGuideGateState()
    {
        var ready = InstalledApkLevel == OperationOutcomeKind.Success;
        var level = ready
            ? OperationOutcomeKind.Success
            : InstalledApkLevel == OperationOutcomeKind.Failure
                ? OperationOutcomeKind.Failure
                : OperationOutcomeKind.Warning;
        var detail = ready
            ? InstalledApkDetail
            : $"{InstalledApkDetail} If the study build is missing or wrong, install the bundled Sussex Experiment APK before continuing.";
        return new WorkflowGuideGateState(level, ready ? "Correct Sussex APK installed." : "Correct Sussex APK not confirmed yet.", detail, ready);
    }

    private WorkflowGuideGateState BuildProfileWorkflowGuideGateState(
        WorkflowGuideGateState deviceProfileState,
        WorkflowGuideGateState headsetBatteryState,
        WorkflowGuideGateState rightControllerBatteryState)
    {
        var checksClear = deviceProfileState.Ready
            && headsetBatteryState.Ready
            && rightControllerBatteryState.Ready;
        var detail =
            $"{deviceProfileState.Summary} {headsetBatteryState.Summary} {rightControllerBatteryState.Summary}";
        return new WorkflowGuideGateState(
            checksClear ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning,
            checksClear ? "Device profile and bench safety checks confirmed." : "Device profile step completed with warnings.",
            detail,
            true);
    }

    private WorkflowGuideGateState BuildKioskWorkflowGuideGateState()
    {
        var ready = IsStudyRuntimeForeground();
        var level = ready ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning;
        var launchBlockInstruction = BuildLaunchBlockInstruction();
        var detail = ready
            ? $"Sussex Experiment is active in the foreground. The launch path should already have armed the keep-awake proximity override. {KioskMenuButtonAdvisory} If the controller was asleep at launch, wake it and relaunch before moving on."
            : $"{(string.IsNullOrWhiteSpace(launchBlockInstruction) ? string.Empty : $"{launchBlockInstruction} ")}Disable proximity, wake the right controller, confirm the connection still shows the Wi-Fi endpoint, then launch the runtime. {KioskMenuButtonAdvisory}";
        return new WorkflowGuideGateState(level, ready ? "Sussex runtime is active." : "Sussex runtime is not active yet.", detail, ready);
    }

    private OperationOutcome BuildLslConnectionProbeOutcome()
    {
        var probeState = BuildLslConnectionProbeState();
        return new OperationOutcome(probeState.Level, probeState.Summary, probeState.Detail);
    }

    private (OperationOutcomeKind Level, string Summary, string Detail, bool InletReady, bool ReturnPathReady) BuildLslConnectionProbeState()
    {
        var twinStatePublisherInventory = GetQuestTwinStatePublisherInventoryOrDefault();
        if (_headsetStatus?.IsConnected != true)
        {
            return (
                OperationOutcomeKind.Failure,
                "Quest is not reachable over ADB.",
                "Probe Connection needs a live headset selector before it can inspect Sussex runtime routing. Reconnect USB or Wi-Fi ADB, then run the probe again.",
                false,
                false);
        }

        var selector = ResolveHeadsetActionSelector();
        var selectorLabel = string.IsNullOrWhiteSpace(selector)
            ? "Selector n/a."
            : $"Selector {selector} over {DescribeSelectorTransport(selector)}.";
        var lastStateReceivedAt = _twinBridge is LslTwinModeBridge lslBridge
            ? lslBridge.LastStateReceivedAt
            : null;
        var hasTwinState = _reportedTwinState.Count > 0;
        var twinStateFresh = hasTwinState
            && (!lastStateReceivedAt.HasValue || DateTimeOffset.UtcNow - lastStateReceivedAt.Value <= TwinStateIdleThreshold);
        var twinStateSummary = !hasTwinState
            ? $"No {TwinStateStreamName} / {TwinStateStreamType} frame has reached Windows yet."
            : twinStateFresh
                ? lastStateReceivedAt.HasValue
                    ? $"Latest {TwinStateStreamName} / {TwinStateStreamType} frame {lastStateReceivedAt.Value.ToLocalTime():HH:mm:ss}."
                    : $"{TwinStateStreamName} / {TwinStateStreamType} is active."
                : $"Latest {TwinStateStreamName} / {TwinStateStreamType} frame {lastStateReceivedAt!.Value.ToLocalTime():HH:mm:ss}; the return path is stale.";
        var testSenderHint = _testLslSignalService?.IsRunning == true
            ? " Companion TEST sender is active on Windows. If Analyze Windows Environment is also green, focus next on the headset-side scene state, Wi-Fi client-isolation, or whether this Quest build is publishing quest_twin_state."
            : string.Empty;
        var connectedFlag = ParseBool(GetFirstValue("study.lsl.connected"));
        var connectedCount = ParseInt(GetFirstValue("connection.lsl.connected_count"));
        var connectedName = GetFirstValue("study.lsl.connected_name");
        var hasConnectedInput = connectedFlag == true
            || connectedCount.GetValueOrDefault() > 0
            || !string.IsNullOrWhiteSpace(connectedName);
        var inletFailureSummary = BuildLslInletFailureSummary(
            GetFirstValue("study.lsl.status") ?? LslStatusLineLabel,
            _study.Monitoring.ExpectedLslStreamName,
            _study.Monitoring.ExpectedLslStreamType);
        var inletReady = LslLevel == OperationOutcomeKind.Success || hasConnectedInput;
        var returnPathReady = twinStateFresh;
        var pinnedBuildReady = PinnedBuildLevel == OperationOutcomeKind.Success;
        var pinnedBuildBlocksProbe = PinnedBuildLevel == OperationOutcomeKind.Failure || InstalledApkLevel == OperationOutcomeKind.Failure;
        var deviceProfileReady = DeviceProfileLevel == OperationOutcomeKind.Success;
        var hazardState = BuildTransportHazardState(inletReady, returnPathReady);

        var detail = BuildGuideDetailLines(
            ("Selector", string.IsNullOrWhiteSpace(selectorLabel) ? "n/a" : selectorLabel),
            ("Foreground + snapshot", HeadsetForegroundLabel),
            ("Pinned build", $"{PinnedBuildSummary} {ShortHashLabel("pinned", _study.App.Sha256)} {ShortHashLabel("installed", InstalledApkHash)}".Trim()),
            ("Device profile", $"{DeviceProfileSummary} {DeviceProfileDetail}".Trim()),
            ("Expected inlet", LslExpectedStreamLabel),
            ("Runtime target", LslRuntimeTargetLabel),
            ("Connected inlet", LslConnectedStreamLabel),
            ("Counts", LslConnectionStateLabel),
            ("Quest status", LslStatusLineLabel),
            ("Quest echo", LslEchoStateLabel),
            ("Return path", twinStateSummary),
            ("Twin-state outlet", string.IsNullOrWhiteSpace(_questTwinStatePublisherInventoryDetail)
                ? "Quest twin-state outlet inventory has not run yet."
                : _questTwinStatePublisherInventoryDetail),
            ("Command channel", $"{TwinCommandStreamName} / {TwinCommandStreamType}"),
            ("Hotload channel", $"{TwinConfigStreamName} / {TwinConfigStreamType}"),
            ("Transport detail", BuildTwinCommandTransportDetail()),
            ("Potential hazards", hazardState.Detail));

        if (pinnedBuildBlocksProbe)
        {
            return (
                OperationOutcomeKind.Failure,
                "Pinned Sussex APK is not installed or does not match the study shell baseline.",
                detail + " Install the pinned Sussex APK before debugging LSL.",
                inletReady,
                false);
        }

        if (_headsetStatus.IsWifiAdbTransport != true)
        {
            var wifiStopperDetail = _questWifiTransportDiagnostics?.Detail;
            if (string.IsNullOrWhiteSpace(wifiStopperDetail))
            {
                wifiStopperDetail = "Wi-Fi ADB is still the gating requirement for this diagnostic. Even if the inlet and return path look alive over USB, this result cannot turn green until the active Quest transport is Wi-Fi ADB. Keep USB attached, accept any in-headset debugging prompt, and then use Connect Quest with the current headset Wi-Fi IP plus port 5555.";
            }

            return (
                OperationOutcomeKind.Warning,
                inletReady && returnPathReady
                    ? "Quest inlet and return path are live, but the session is still on USB ADB."
                    : "Quest is still not on Wi-Fi ADB, so this diagnostic cannot turn green yet.",
                $"{detail} {wifiStopperDetail}".Trim(),
                inletReady,
                false);
        }

        if (inletReady && returnPathReady)
        {
            var successSummary = !pinnedBuildReady
                ? "Quest path is live, but the pinned Sussex APK is not fully verified yet."
                : deviceProfileReady
                    ? "Quest inlet is connected and the Windows return path is live."
                    : "Quest path is live, but the required Sussex device profile still needs attention.";
            var successDetail = detail;
            if (!pinnedBuildReady)
            {
                successDetail += " Refresh or reinstall the pinned Sussex APK before participant validation.";
            }

            if (!deviceProfileReady)
            {
                successDetail += " Apply the Sussex study device profile before participant validation.";
            }

            return (
                pinnedBuildReady && deviceProfileReady ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning,
                successSummary,
                successDetail,
                inletReady,
                pinnedBuildReady && deviceProfileReady);
        }

        if (inletReady)
        {
            var summary = _headsetStatus?.IsAwake == false
                ? "Quest inlet is connected, but the headset is asleep and the Windows return path is stale."
                : _headsetStatus?.IsInWakeLimbo == true
                    ? "Quest inlet is connected, but Guardian or tracking-loss is blocking a fresh Windows return path."
                : IsStudyRuntimeForeground() && !twinStatePublisherInventory.ExpectedPublisherVisible
                ? "Quest inlet is connected, but the Quest twin-state publisher stalled or became undiscoverable."
                : !IsStudyRuntimeForeground()
                    ? "Quest inlet is connected, but Sussex is not foregrounded and the Windows return path is stale."
                    : "Quest inlet is connected, but Windows is not receiving a fresh return path yet.";
            return (
                OperationOutcomeKind.Warning,
                summary,
                detail + testSenderHint,
                inletReady,
                false);
        }

        if (returnPathReady)
        {
            return (
                inletFailureSummary is not null ? OperationOutcomeKind.Failure : OperationOutcomeKind.Warning,
                inletFailureSummary is not null
                    ? $"{inletFailureSummary} Windows is still receiving quest_twin_state."
                    : "Windows is receiving quest_twin_state, but Sussex has not confirmed an LSL inlet yet.",
                detail + testSenderHint,
                inletReady,
                false);
        }

        return (
            IsStudyRuntimeForeground() ? OperationOutcomeKind.Failure : OperationOutcomeKind.Warning,
            !IsStudyRuntimeForeground()
                ? "Sussex is not foregrounded, so neither the LSL inlet nor the Windows return path is confirmed."
                : !twinStatePublisherInventory.AnyPublisherVisible
                    ? "Sussex is in front, but the Quest twin-state publisher is absent and the inlet is not confirmed."
                    : "Sussex is in front, but neither the LSL inlet nor the Windows return path is confirmed.",
            detail + testSenderHint,
            inletReady,
            false);
    }

    private WorkflowGuideGateState BuildLslWorkflowGuideGateState()
    {
        var probeState = BuildLslConnectionProbeState();
        var ready = LslLevel == OperationOutcomeKind.Success && probeState.ReturnPathReady;
        var level = ready
            ? OperationOutcomeKind.Success
            : LslLevel == OperationOutcomeKind.Failure || probeState.Level == OperationOutcomeKind.Failure
                ? OperationOutcomeKind.Failure
                : OperationOutcomeKind.Warning;
        var detail = ready
            ? $"{LslDetail} Connected stream {LslConnectedStreamLabel}. {probeState.Summary}"
            : $"{LslDetail} {probeState.Summary} {probeState.Detail} The experiment heartbeat stream is the operator's responsibility, but this step must turn green before the onboarding run continues.";
        var summary = ready
            ? "Heartbeat LSL is reaching the headset."
            : level == OperationOutcomeKind.Failure && LooksLikeLslInletConnectFailure(LslStatusLineLabel)
                ? "Quest failed to subscribe the heartbeat LSL inlet."
                : "Heartbeat LSL is not confirmed yet.";
        return new WorkflowGuideGateState(level, summary, detail, ready);
    }

    private WorkflowGuideCheckItem BuildLslReturnPathWorkflowGuideCheckItem()
    {
        var probeState = BuildLslConnectionProbeState();
        return new WorkflowGuideCheckItem("Windows return path", probeState.Summary, probeState.Detail, probeState.Level);
    }

    private WorkflowGuideCheckItem BuildLslHazardsWorkflowGuideCheckItem()
    {
        var probeState = BuildLslConnectionProbeState();
        var hazardState = BuildTransportHazardState(probeState.InletReady, probeState.ReturnPathReady);
        return new WorkflowGuideCheckItem("Potential hazards", hazardState.Summary, hazardState.Detail, hazardState.Level);
    }

    private WorkflowGuideCheckItem BuildReconnectTargetWorkflowGuideCheckItem()
    {
        var reconnectState = EvaluateReconnectTargetGuideState();
        return new WorkflowGuideCheckItem("Reconnect target", reconnectState.Summary, reconnectState.Detail, reconnectState.Level);
    }

    private WorkflowGuideHazardState EvaluateReconnectTargetGuideState()
    {
        var savedReconnectTarget = GetSavedReconnectTarget();
        if (string.IsNullOrWhiteSpace(savedReconnectTarget))
        {
            return new WorkflowGuideHazardState(
                OperationOutcomeKind.Warning,
                "Reconnect target not saved yet.",
                "If Wi-Fi ADB does not fully switch over, run Connect Quest once the companion has a reconnect target.");
        }

        if (_headsetStatus?.IsConnected != true || _headsetStatus.IsWifiAdbTransport != true)
        {
            return new WorkflowGuideHazardState(
                OperationOutcomeKind.Success,
                "Reconnect target is saved.",
                $"Saved reconnect target {savedReconnectTarget}. The companion can reuse it if the Wi-Fi ADB session drops later.");
        }

        var liveSelector = _headsetStatus.ConnectionLabel;
        var savedIp = ExtractIpAddressFromSelector(savedReconnectTarget);
        var liveIp = ExtractIpAddressFromSelector(liveSelector);
        var headsetWifiIp = _headsetStatus.HeadsetWifiIpAddress;
        var ipMismatch = !string.IsNullOrWhiteSpace(savedIp) &&
                         (!string.IsNullOrWhiteSpace(liveIp) && !string.Equals(savedIp, liveIp, StringComparison.OrdinalIgnoreCase) ||
                          !string.IsNullOrWhiteSpace(headsetWifiIp) && !string.Equals(savedIp, headsetWifiIp, StringComparison.OrdinalIgnoreCase));

        if (!string.Equals(savedReconnectTarget, liveSelector, StringComparison.OrdinalIgnoreCase) || ipMismatch)
        {
            return new WorkflowGuideHazardState(
                OperationOutcomeKind.Warning,
                "Reconnect target is stale.",
                $"Saved reconnect target {savedReconnectTarget} differs from the live ADB selector {liveSelector}. Headset Wi-Fi IP {FormatOptionalValue(headsetWifiIp, "n/a")}. This usually means the headset's DHCP/IP changed or the remembered endpoint is stale. Run Connect Quest before continuing so recovery actions target the current headset endpoint.");
        }

        return new WorkflowGuideHazardState(
            OperationOutcomeKind.Success,
            "Reconnect target matches the current Quest endpoint.",
            $"Saved reconnect target {savedReconnectTarget} matches the live Wi-Fi ADB selector.");
    }

    private WorkflowGuideHazardState BuildTransportHazardState(bool inletReady, bool returnPathReady)
    {
        var twinStatePublisherInventory = GetQuestTwinStatePublisherInventoryOrDefault();
        if (_headsetStatus?.IsConnected != true)
        {
            return new WorkflowGuideHazardState(
                OperationOutcomeKind.Warning,
                "Quest link is unavailable.",
                "Reconnect the headset over Wi-Fi ADB before relying on live LSL or twin-state diagnostics.");
        }

        var hazardLevel = OperationOutcomeKind.Success;
        var hazardSummary = "No live transport hazards detected.";
        var hazardDetails = new List<string>();
        var liveSelector = _headsetStatus.ConnectionLabel;
        var selectedSelector = ResolveHeadsetActionSelector();
        var savedReconnectTarget = GetSavedReconnectTarget();
        var headsetWifiIp = _headsetStatus.HeadsetWifiIpAddress;

        void AddHazard(OperationOutcomeKind level, string summary, string detail)
        {
            if (GetOutcomeSeverity(level) > GetOutcomeSeverity(hazardLevel))
            {
                hazardLevel = level;
                hazardSummary = summary;
            }

            hazardDetails.Add(detail);
        }

        if (_headsetStatus.IsWifiAdbTransport && _headsetStatus.IsUsbAdbVisible)
        {
            AddHazard(
                OperationOutcomeKind.Warning,
                "USB visibility can destabilize the Wi-Fi ADB session.",
                $"USB {FormatOptionalValue(_headsetStatus.VisibleUsbSerial, "ADB")} is visible again while the study path is using Wi-Fi ADB on {liveSelector}. Reconnecting USB can restart ADB, interrupt runtime focus or task pinning, or leave the remembered Wi-Fi endpoint stale.");
        }

        if (_headsetStatus.IsWifiAdbTransport &&
            !string.IsNullOrWhiteSpace(savedReconnectTarget) &&
            LooksLikeTcpSelector(savedReconnectTarget) &&
            !string.Equals(savedReconnectTarget, liveSelector, StringComparison.OrdinalIgnoreCase))
        {
            AddHazard(
                OperationOutcomeKind.Warning,
                "Saved reconnect target does not match the current Quest endpoint.",
                $"Saved reconnect target {savedReconnectTarget} differs from the live ADB selector {liveSelector}. This usually means the headset's Wi-Fi IP changed or the remembered endpoint is stale.");
        }

        var selectedSelectorIp = ExtractIpAddressFromSelector(selectedSelector);
        if (_headsetStatus.IsWifiAdbTransport &&
            !string.IsNullOrWhiteSpace(headsetWifiIp) &&
            !string.IsNullOrWhiteSpace(selectedSelectorIp) &&
            !string.Equals(selectedSelectorIp, headsetWifiIp, StringComparison.OrdinalIgnoreCase))
        {
            AddHazard(
                OperationOutcomeKind.Warning,
                "The companion is about to use a stale Quest Wi-Fi endpoint.",
                $"The current action selector {selectedSelector} does not match the headset-reported Wi-Fi IP {headsetWifiIp}. This can happen after DHCP/IP changes or an ADB daemon reset. Run Connect Quest before probing or relaunching so actions target the current endpoint.");
        }

        if (_headsetStatus.IsAwake == false)
        {
            AddHazard(
                OperationOutcomeKind.Warning,
                "The headset is asleep.",
                "Quest power state is asleep. Keep the headset on-face or re-arm the keep-awake proximity override before relying on LSL, twin-state, particle, or calibration checks.");
        }

        if (_headsetStatus.IsInWakeLimbo)
        {
            AddHazard(
                OperationOutcomeKind.Warning,
                "Guardian or tracking loss is blocking the visible scene.",
                "Quest reports a Guardian, tracking-loss, or ClearActivity blocker in front of Sussex. Clear it or confirm the visible scene with a fresh screenshot before trusting the live return path.");
        }

        if (!IsStudyRuntimeForeground())
        {
            AddHazard(
                OperationOutcomeKind.Failure,
                "Sussex is backgrounded by the Meta shell or another app.",
                $"ADB snapshot shows {FormatOptionalValue(_headsetStatus.ForegroundPackageId, "an unknown foreground app")} instead of {_study.App.PackageId}. When Sussex is not in front it can stop publishing quest_twin_state, so return-path failures in this state are expected until the app is relaunched.");
        }

        if (IsStudyRuntimeForeground() && IsProximityKeepAwakeActive() != true)
        {
            AddHazard(
                OperationOutcomeKind.Warning,
                "Keep-awake proximity override is not active.",
                "The live Sussex flow now expects the prox_close keep-awake override through launch and bench validation. On this Quest build the active override reads as virtual proximity state CLOSE, while restored normal wear-sensor behavior reads as DISABLED after automation_disable. Without the override the headset can drift into sleep or stale-twin-state conditions between guide steps even while Sussex stays foregrounded.");
        }

        if (IsStudyRuntimeForeground() && inletReady && !returnPathReady)
        {
            AddHazard(
                OperationOutcomeKind.Warning,
                !twinStatePublisherInventory.ExpectedPublisherVisible
                    ? "Quest twin-state publisher stalled or became undiscoverable."
                    : "Quest twin-state is stale even though the inlet is connected.",
                !twinStatePublisherInventory.ExpectedPublisherVisible
                    ? "Sussex still reports the expected HRV inlet as connected, but Windows cannot see a fresh quest_twin_state publisher from the Sussex source_id. This is a separate failure mode from a missing inlet or a backgrounded APK."
                    : "The Quest-side inlet is still connected, but the return-path frame age exceeded 5 seconds. Keep the headset on-face, avoid USB reconnects, and relaunch Sussex if fresh frames do not resume.");
        }
        else if (IsStudyRuntimeForeground() && !inletReady && returnPathReady)
        {
            AddHazard(
                OperationOutcomeKind.Warning,
                "Quest twin-state is live, but Sussex has not connected the expected inlet.",
                "The return path is healthy, so focus next on the upstream HRV sender and inlet selection rather than on Quest foreground or twin-state publishing.");
        }

        if (_questWifiTransportDiagnostics is { Level: OperationOutcomeKind.Warning or OperationOutcomeKind.Failure } wifiTransport)
        {
            AddHazard(
                wifiTransport.Level,
                wifiTransport.Summary,
                wifiTransport.Detail);
        }

        return hazardDetails.Count == 0
            ? new WorkflowGuideHazardState(
                OperationOutcomeKind.Success,
                "No live transport hazards detected.",
                "No selector drift, router/Wi-Fi path issue, USB reconnect risk, or twin-state publication hazard is visible in the current snapshot.")
            : new WorkflowGuideHazardState(hazardLevel, hazardSummary, string.Join(" ", hazardDetails));
    }

    private string GetSavedReconnectTarget()
    {
        var endpointDraft = string.IsNullOrWhiteSpace(EndpointDraft) ? null : EndpointDraft.Trim();
        if (!string.IsNullOrWhiteSpace(endpointDraft))
        {
            return endpointDraft;
        }

        return _appSessionState.ActiveEndpoint ?? string.Empty;
    }

    private static int GetOutcomeSeverity(OperationOutcomeKind level)
        => level switch
        {
            OperationOutcomeKind.Failure => 3,
            OperationOutcomeKind.Warning => 2,
            OperationOutcomeKind.Success => 1,
            _ => 0
        };

    private static string BuildQuestWifiTransportDiagnosticsInputKey(HeadsetAppStatus headset, string? requestedSelector)
        => string.Join(
            "|",
            requestedSelector,
            headset.ConnectionLabel,
            headset.HeadsetWifiIpAddress,
            headset.HeadsetWifiSsid,
            headset.HostWifiSsid,
            headset.HostWifiInterfaceName);

    private static string ResolveQuestWifiDiagnosticSelector(
        QuestWifiAdbDiagnosticPreparationResult preparation,
        string fallbackSelector)
    {
        if (preparation.EffectiveHeadset.IsConnected &&
            !string.IsNullOrWhiteSpace(preparation.EffectiveHeadset.ConnectionLabel))
        {
            return preparation.EffectiveHeadset.ConnectionLabel;
        }

        if (!string.IsNullOrWhiteSpace(preparation.ReconnectOutcome?.Endpoint))
        {
            return preparation.ReconnectOutcome.Endpoint.Trim();
        }

        if (!string.IsNullOrWhiteSpace(preparation.BootstrapOutcome?.Endpoint))
        {
            return preparation.BootstrapOutcome.Endpoint.Trim();
        }

        return fallbackSelector;
    }

    private static string ExtractIpAddressFromSelector(string? selector)
    {
        if (!LooksLikeTcpSelector(selector))
        {
            return string.Empty;
        }

        var trimmed = selector!.Trim();
        var separatorIndex = trimmed.LastIndexOf(':');
        return separatorIndex > 0 ? trimmed[..separatorIndex] : trimmed;
    }

    private static string FormatOptionalValue(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private QuestTwinStatePublisherInventory GetQuestTwinStatePublisherInventoryOrDefault()
        => _questTwinStatePublisherInventory
           ?? new QuestTwinStatePublisherInventory(
               OperationOutcomeKind.Preview,
               "Quest twin-state outlet inventory has not run yet.",
               "The probe has not inspected Windows-visible Quest twin-state publishers yet.",
               AnyPublisherVisible: false,
               ExpectedPublisherVisible: false,
               ExpectedSourceId: string.Empty,
               ExpectedSourceIdPrefix: string.Empty,
               VisiblePublishers: []);

    private string BuildLslReceiptWorkflowGuideDetail()
        => BuildGuideDetailLines(
            ("Expected inlet", LslExpectedStreamLabel),
            ("Connected inlet", LslConnectedStreamLabel),
            ("Quest echo", LslEchoStateLabel),
            ("Bench sender", LslBenchStateLabel),
            ("Assessment", LslDetail));

    private string BuildLslStreamWorkflowGuideDetail()
        => BuildGuideDetailLines(
            ("Expected inlet", LslExpectedStreamLabel),
            ("Runtime target", LslRuntimeTargetLabel),
            ("Connected inlet", LslConnectedStreamLabel),
            ("Counts", LslConnectionStateLabel),
            ("Quest status", LslStatusLineLabel),
            ("Quest echo", LslEchoStateLabel));

    private static string BuildGuideDetailLines(params (string Label, string Value)[] lines)
        => string.Join(
            Environment.NewLine,
            lines
                .Where(line => !string.IsNullOrWhiteSpace(line.Value))
                .Select(line => $"{line.Label}: {line.Value.Trim()}"));

    private WorkflowGuideCheckItem BuildLslUpstreamInventoryWorkflowGuideCheckItem()
    {
        const string Label = "Windows upstream inventory";
        if (_lslExpectedUpstreamProbeState is null)
        {
            var expectedStreamName = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamName)
                ? HrvBiofeedbackStreamContract.StreamName
                : _study.Monitoring.ExpectedLslStreamName;
            var expectedStreamType = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamType)
                ? HrvBiofeedbackStreamContract.StreamType
                : _study.Monitoring.ExpectedLslStreamType;
            return new WorkflowGuideCheckItem(
                Label,
                $"{expectedStreamName} / {expectedStreamType} has not been probed on Windows yet.",
                "Run Probe Connection or Analyze Windows Environment to inspect whether the expected inlet is missing entirely or being shadowed by other Windows-side publishers.",
                OperationOutcomeKind.Preview);
        }

        if (!_lslExpectedUpstreamProbeState.DiscoveryAvailable || _lslExpectedUpstreamProbeState.ProbeFailed)
        {
            return new WorkflowGuideCheckItem(
                Label,
                _lslExpectedUpstreamProbeState.Summary,
                _lslExpectedUpstreamProbeState.Detail,
                OperationOutcomeKind.Warning);
        }

        var check = BuildExpectedUpstreamInventoryCheck(
            _lslExpectedUpstreamProbeState.ExpectedStreamName,
            _lslExpectedUpstreamProbeState.ExpectedStreamType,
            _lslExpectedUpstreamProbeState.VisibleMatches,
            BuildTestSenderSourceId());
        return new WorkflowGuideCheckItem(Label, check.Summary, check.Detail, check.Level);
    }

    private WorkflowGuideGateState BuildParticleWorkflowGuideGateState()
    {
        var verified = _workflowGuideParticlesOnVerified
            && _workflowGuideParticlesOffVerified
            && ParticlesLevel == OperationOutcomeKind.Success;
        var level = verified
            ? OperationOutcomeKind.Success
            : OperationOutcomeKind.Warning;
        var detail = verified
            ? "Particles were turned on and then off again from the companion, and the runtime reported the expected resulting state."
            : "Use the WPF buttons below to turn particles on once and then off again. This is still the recommended bench check even though the guide no longer blocks on it.";
        return new WorkflowGuideGateState(level, verified ? "Particle command path verified." : "Particle command path is still recommended.", detail, true);
    }

    private WorkflowGuideGateState BuildCalibrationWorkflowGuideGateState()
    {
        var calibrated = string.Equals(ControllerCalibrationLabel, "Calibrated", StringComparison.OrdinalIgnoreCase);
        var hasVolumeReadback = !string.Equals(ControllerValueLabel, "n/a", StringComparison.OrdinalIgnoreCase);
        var ready = calibrated && hasVolumeReadback;
        var level = ready
            ? OperationOutcomeKind.Success
            : OperationOutcomeKind.Warning;
        var detail = ready
            ? $"Calibration completed and the current controller-volume readback is {ControllerValueLabel}."
            : CanStartBreathingCalibration
                ? "Calibration is still unstable on the current Sussex APK. You can try it here, but the guide does not require it yet. Continue once the earlier LSL and particle checks are complete."
                : "Calibration is optional for now, and this build does not currently expose a runnable calibration command through the public shell.";
        return new WorkflowGuideGateState(
            level,
            ready ? "Controller calibration verified." : "Controller calibration is optional for now.",
            detail,
            Ready: true);
    }

    private WorkflowGuideGateState BuildValidationCaptureWorkflowGuideGateState()
    {
        var completed = ValidationCaptureCompleted
            && !string.IsNullOrWhiteSpace(ValidationCaptureLocalFolderPath)
            && !string.IsNullOrWhiteSpace(ValidationCaptureDevicePullFolderPath);
        var level = completed
            ? OperationOutcomeKind.Success
            : ValidationCaptureRunning
                ? OperationOutcomeKind.Preview
                : OperationOutcomeKind.Warning;
        return new WorkflowGuideGateState(
            level,
            ValidationCaptureSummary,
            ValidationCaptureDetail,
            true);
    }

    private IReadOnlyList<WorkflowGuideCheckItem> BuildValidationClockAlignmentCheckItems()
        =>
        [
            new WorkflowGuideCheckItem(
                "Start burst",
                _validationClockAlignmentStartSummary,
                _validationClockAlignmentStartDetail,
                _validationClockAlignmentStartLevel),
            new WorkflowGuideCheckItem(
                "Background drift",
                _validationClockAlignmentBackgroundSummary,
                _validationClockAlignmentBackgroundDetail,
                _validationClockAlignmentBackgroundLevel),
            new WorkflowGuideCheckItem(
                "End burst",
                _validationClockAlignmentEndSummary,
                _validationClockAlignmentEndDetail,
                _validationClockAlignmentEndLevel)
        ];

    private void ResetValidationClockAlignmentGuideState()
    {
        _validationClockAlignmentBackgroundProbeObserved = false;
        ClockAlignmentLevel = OperationOutcomeKind.Preview;
        ClockAlignmentRunning = false;
        ClockAlignmentSummary = "Clock alignment queued for this validation run.";
        ClockAlignmentDetail = "The validation flow will run a start burst first, keep sparse drift probes armed during the 20 second recording, and finish with a matching end burst.";
        ClockAlignmentProgressPercent = 0d;
        ClockAlignmentProgressLabel = "Waiting for the start clock-alignment burst.";
        ClockAlignmentProbeStatsLabel = "Probes sent: 0 | Quest echoes: 0";
        ClockAlignmentOffsetStatsLabel = "Offset estimate pending.";
        ClockAlignmentRoundTripStatsLabel = "Round-trip estimate pending.";
        _validationClockAlignmentStartLevel = OperationOutcomeKind.Preview;
        _validationClockAlignmentStartSummary = "Queued until recording starts.";
        _validationClockAlignmentStartDetail = "The validation flow begins with a dedicated 10 second echo burst before data collection.";
        _validationClockAlignmentBackgroundLevel = OperationOutcomeKind.Preview;
        _validationClockAlignmentBackgroundSummary = "Armed after the start burst.";
        _validationClockAlignmentBackgroundDetail = $"Sparse drift probes stay idle until the {WorkflowGuideValidationCaptureDurationSeconds} second recording is underway.";
        _validationClockAlignmentEndLevel = OperationOutcomeKind.Preview;
        _validationClockAlignmentEndSummary = "Queued until recording stops.";
        _validationClockAlignmentEndDetail = "A matching end burst runs after the 20 second recording so the session can compare start and end timing.";
    }

    private void UpdateValidationClockAlignmentStage(
        StudyClockAlignmentWindowKind windowKind,
        OperationOutcomeKind level,
        string summary,
        string detail)
    {
        switch (windowKind)
        {
            case StudyClockAlignmentWindowKind.StartBurst:
                _validationClockAlignmentStartLevel = level;
                _validationClockAlignmentStartSummary = summary;
                _validationClockAlignmentStartDetail = detail;
                break;
            case StudyClockAlignmentWindowKind.EndBurst:
                _validationClockAlignmentEndLevel = level;
                _validationClockAlignmentEndSummary = summary;
                _validationClockAlignmentEndDetail = detail;
                break;
            default:
                if (level == OperationOutcomeKind.Preview && _validationClockAlignmentBackgroundProbeObserved)
                {
                    break;
                }

                _validationClockAlignmentBackgroundProbeObserved = level != OperationOutcomeKind.Preview
                    || _validationClockAlignmentBackgroundProbeObserved;
                _validationClockAlignmentBackgroundLevel = level;
                _validationClockAlignmentBackgroundSummary = summary;
                _validationClockAlignmentBackgroundDetail = detail;
                break;
        }
    }

    private void MarkValidationClockAlignmentBackgroundArmed()
    {
        _validationClockAlignmentBackgroundLevel = OperationOutcomeKind.Preview;
        _validationClockAlignmentBackgroundSummary = "Armed during recording.";
        _validationClockAlignmentBackgroundDetail =
            $"The first {WorkflowClockAlignmentSparseProbeDuration.TotalSeconds:0.#} second sparse drift probe starts about {WorkflowClockAlignmentInitialBackgroundProbeDelaySeconds} second after recording begins, then repeats every {WorkflowClockAlignmentBackgroundProbeIntervalSeconds} seconds during the {WorkflowGuideValidationCaptureDurationSeconds} second capture.";
    }

    private void FinalizeValidationClockAlignmentBackgroundState()
    {
        if (_validationClockAlignmentBackgroundProbeObserved)
        {
            return;
        }

        _validationClockAlignmentBackgroundLevel = OperationOutcomeKind.Preview;
        _validationClockAlignmentBackgroundSummary = "No sparse drift probe finished.";
        _validationClockAlignmentBackgroundDetail =
            "This run still recorded the start and end bursts, but the short capture window finished before a background drift probe completed.";
    }

    private IReadOnlyList<WorkflowGuideCheckItem> BuildWorkflowGuideCheckItems(int stepIndex)
    {
        var usbState = BuildUsbWorkflowGuideGateState();
        var deviceProfileState = EvaluateDeviceProfileGateState();
        var headsetBatteryState = EvaluateHeadsetBatteryGateState();
        var rightControllerBatteryState = EvaluateRightControllerBatteryGateState();
        var particleCommandSummary = _workflowGuideParticlesOnVerified && _workflowGuideParticlesOffVerified
            ? "Particles were toggled on and off in this guide session."
            : _workflowGuideParticlesOnVerified
                ? "Particles were turned on once. Turn them off again before continuing."
                : "Particles have not been exercised yet in this guide session.";

        return stepIndex switch
        {
            0 =>
            [
                new WorkflowGuideCheckItem("USB probe", usbState.Summary, usbState.Detail, usbState.Level),
                new WorkflowGuideCheckItem("Current Quest link", ConnectionSummary, QuestStatusDetail, _headsetStatus?.IsConnected == true ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning)
            ],
            1 =>
            [
                new WorkflowGuideCheckItem("Wi-Fi ADB path", BuildWifiAdbGuideSummary(), BuildWifiAdbGuideDetail(), _headsetStatus?.IsWifiAdbTransport == true ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning),
                BuildReconnectTargetWorkflowGuideCheckItem()
            ],
            2 =>
            [
                new WorkflowGuideCheckItem(
                    "Headset Wi-Fi",
                    HeadsetWifiSummary,
                    $"Headset IP {_headsetStatus?.HeadsetWifiIpAddress ?? "n/a"}.",
                    _questWifiTransportDiagnostics?.RoutedTopologyAccepted == true
                        ? OperationOutcomeKind.Success
                        : _headsetStatus?.WifiSsidMatchesHost == true
                            ? OperationOutcomeKind.Success
                            : _headsetStatus?.WifiSsidMatchesHost == false
                                ? OperationOutcomeKind.Failure
                                : OperationOutcomeKind.Warning),
                new WorkflowGuideCheckItem(
                    "PC network",
                    HostWifiSummary,
                    _questWifiTransportDiagnostics?.RoutedTopologyAccepted == true
                        ? "The current routed host path can already reach the Quest endpoint, so matching PC Wi-Fi names are not required on this topology."
                        : string.IsNullOrWhiteSpace(_headsetStatus?.HostWifiSsid)
                            ? "If this PC is on Ethernet, that is still valid as long as the router-path check turns green. If you expect PC Wi-Fi here, refresh the snapshot until the Windows-side probe returns it."
                            : "If the names differ and the router-path check stays red, move the headset onto the same reachable router path before continuing.",
                    _questWifiTransportDiagnostics?.RoutedTopologyAccepted == true
                        ? OperationOutcomeKind.Success
                        : _headsetStatus?.WifiSsidMatchesHost == true
                            ? OperationOutcomeKind.Success
                            : _headsetStatus?.WifiSsidMatchesHost == false
                                ? OperationOutcomeKind.Failure
                                : OperationOutcomeKind.Warning),
                BuildWifiTransportWorkflowGuideCheckItem()
            ],
            3 =>
            [
                new WorkflowGuideCheckItem("Current transport", ConnectionTransportSummary, ConnectionTransportDetail, _headsetStatus?.IsWifiAdbTransport == true ? OperationOutcomeKind.Success : _headsetStatus?.IsConnected == true ? OperationOutcomeKind.Warning : OperationOutcomeKind.Failure),
                new WorkflowGuideCheckItem(
                    "USB visibility",
                    _headsetStatus?.IsUsbAdbVisible == true
                        ? $"USB ADB endpoint {_headsetStatus.VisibleUsbSerial} is still visible."
                        : "No USB ADB endpoint is currently visible.",
                    "USB visibility stays tracked here for awareness. The cable may still be physically attached; this card only reflects whether adb currently lists a USB endpoint. Sussex now only requires the active transport to remain on the Wi-Fi endpoint.",
                    _headsetStatus?.IsUsbAdbVisible == true ? OperationOutcomeKind.Warning : OperationOutcomeKind.Success)
            ],
            4 =>
            [
                new WorkflowGuideCheckItem("Installed Sussex build", InstalledApkSummary, InstalledApkDetail, InstalledApkLevel),
                new WorkflowGuideCheckItem("Bundled Sussex build", LocalApkSummary, LocalApkDetail, LocalApkLevel)
            ],
            5 =>
            [
                new WorkflowGuideCheckItem("Pinned device profile", deviceProfileState.Summary, DeviceProfileDetail, deviceProfileState.Level),
                new WorkflowGuideCheckItem("Headset battery floor", headsetBatteryState.Summary, headsetBatteryState.Detail, headsetBatteryState.Level),
                new WorkflowGuideCheckItem("Right controller battery floor", rightControllerBatteryState.Summary, rightControllerBatteryState.Detail, rightControllerBatteryState.Level)
            ],
            6 =>
            [
                new WorkflowGuideCheckItem("Boundary instruction", "Draw a large comfortable boundary.", "Cover the participant position, the experimenter position, and the whole experiment area before moving on.", OperationOutcomeKind.Preview)
            ],
            7 =>
            [
                BuildHeadsetWakeAndProximityWorkflowGuideCheckItem(),
                new WorkflowGuideCheckItem("Foreground runtime", IsStudyRuntimeForeground() ? "Sussex Experiment is in the foreground." : "Sussex Experiment is not in the foreground yet.", HeadsetForegroundLabel, IsStudyRuntimeForeground() ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning),
                new WorkflowGuideCheckItem("Controller wake reminder", BuildControllerWakeReminderSummary(), BuildControllerWakeReminderDetail(), BuildControllerWakeReminderLevel())
            ],
            8 =>
            [
                BuildHeadsetWakeAndProximityWorkflowGuideCheckItem(),
                new WorkflowGuideCheckItem("LSL receipt", LslSummary, BuildLslReceiptWorkflowGuideDetail(), LslLevel),
                new WorkflowGuideCheckItem("Expected vs connected stream", $"Expected {LslExpectedStreamLabel}", BuildLslStreamWorkflowGuideDetail(), LslLevel),
                BuildLslUpstreamInventoryWorkflowGuideCheckItem(),
                BuildLslReturnPathWorkflowGuideCheckItem(),
                BuildLslHazardsWorkflowGuideCheckItem()
            ],
            9 =>
            [
                BuildHeadsetWakeAndProximityWorkflowGuideCheckItem(),
                new WorkflowGuideCheckItem("Particle commands", particleCommandSummary, ParticlesDetail, _workflowGuideParticlesOnVerified && _workflowGuideParticlesOffVerified && ParticlesLevel == OperationOutcomeKind.Success ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning),
                new WorkflowGuideCheckItem("Runtime particle state", ParticlesSummary, "The particle step remains recommended for bench confidence, but it no longer blocks the guide.", ToAdvisoryLevel(ParticlesLevel))
            ],
            10 =>
            [
                BuildHeadsetWakeAndProximityWorkflowGuideCheckItem(),
                new WorkflowGuideCheckItem(
                    "Calibration setup",
                    _controllerBreathingProfiles?.CalibrationModeSummary ?? "Calibration setup not loaded yet.",
                    _controllerBreathingProfiles?.CalibrationSetupDetail ?? "The controller-breathing profile library is not ready yet.",
                    _controllerBreathingProfiles is null ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview),
                new WorkflowGuideCheckItem(
                    "Calibration quality",
                    ControllerCalibrationQualityVisible
                        ? $"{ControllerCalibrationQualityBadge}: {ControllerCalibrationQualitySummary}"
                        : ControllerSummary,
                    ControllerCalibrationQualityVisible
                        ? $"Calibration is optional on the current Sussex APK. {ControllerCalibrationQualityExpectation} {ControllerCalibrationQualityMetrics}{(string.IsNullOrWhiteSpace(ControllerCalibrationQualityCause) ? string.Empty : $" {ControllerCalibrationQualityCause}.")} {ControllerCalibrationQualityDetail}"
                        : $"Calibration is optional on the current Sussex APK. {ControllerCalibrationLabel}. {ControllerDetail}",
                    ControllerCalibrationQualityVisible ? ControllerCalibrationQualityLevel : ControllerLevel),
                new WorkflowGuideCheckItem("Volume readback", $"Current controller volume {ControllerValueLabel}.", "If calibration succeeds, a live volume readback should appear here. The guide can still continue while this path remains unstable.", string.Equals(ControllerValueLabel, "n/a", StringComparison.OrdinalIgnoreCase) ? OperationOutcomeKind.Warning : OperationOutcomeKind.Success)
            ],
            11 =>
            [
                BuildHeadsetWakeAndProximityWorkflowGuideCheckItem(),
                new WorkflowGuideCheckItem("Validation capture", ValidationCaptureSummary, ValidationCaptureDetail, ValidationCaptureCompleted ? OperationOutcomeKind.Success : ValidationCaptureRunning ? OperationOutcomeKind.Preview : OperationOutcomeKind.Warning),
                new WorkflowGuideCheckItem("Recorder state", RecordingSummary, RecordingDetail, RecordingLevel)
            ],
            _ =>
            [
                BuildHeadsetWakeAndProximityWorkflowGuideCheckItem(),
                new WorkflowGuideCheckItem("Reset calibration", "Use Reset Calibration before the real participant enters the headset.", "This prevents the experimenter's calibration from being reused accidentally.", CanResetBreathingCalibration ? OperationOutcomeKind.Preview : OperationOutcomeKind.Warning),
                new WorkflowGuideCheckItem("Particles off", ParticlesSummary, "Leave particles off before returning to the main runtime tab.", ParticlesLevel)
            ]
        };
    }

    private IReadOnlyList<WorkflowGuideActionItem> BuildWorkflowGuideActionItems(int stepIndex)
        => stepIndex switch
        {
            0 =>
            [
                BuildWorkflowGuideActionItem("Probe USB", ProbeUsbCommand, true, "Probing USB..."),
                BuildWorkflowGuideActionItem("Refresh Snapshot", RefreshDeviceSnapshotCommand, true, "Refreshing Snapshot...")
            ],
            1 =>
            [
                BuildWorkflowGuideActionItem("Enable Wi-Fi ADB", EnableWifiCommand, true, "Enabling Wi-Fi ADB..."),
                BuildWorkflowGuideActionItem("Connect Quest", ConnectQuestCommand, !string.IsNullOrWhiteSpace(EndpointDraft), "Connecting Quest...")
            ],
            2 =>
            [
                BuildWorkflowGuideActionItem("Refresh Snapshot", RefreshDeviceSnapshotCommand, true, "Refreshing Snapshot...")
            ],
            3 =>
            [
                BuildWorkflowGuideActionItem("Connect Quest", ConnectQuestCommand, !string.IsNullOrWhiteSpace(EndpointDraft), "Connecting Quest..."),
                BuildWorkflowGuideActionItem("Refresh Snapshot", RefreshDeviceSnapshotCommand, true, "Refreshing Snapshot...")
            ],
            4 =>
            [
                BuildWorkflowGuideActionItem(StudyApkInstallButtonLabel, InstallStudyAppCommand, HasValidPinnedLocalApk, "Installing Sussex APK..."),
                BuildWorkflowGuideActionItem("Refresh Snapshot", RefreshDeviceSnapshotCommand, true, "Refreshing Snapshot...")
            ],
            5 =>
            [
                BuildWorkflowGuideActionItem("Apply Device Profile", ApplyPinnedDeviceProfileCommand, true, "Applying Device Profile..."),
                BuildWorkflowGuideActionItem("Refresh Snapshot", RefreshDeviceSnapshotCommand, true, "Refreshing Snapshot...")
            ],
            6 =>
            [],
            7 =>
            [
                BuildWorkflowGuideActionItem(
                    string.IsNullOrWhiteSpace(ProximityActionLabel) ? "Disable Proximity" : ProximityActionLabel,
                    ToggleProximityCommand,
                    CanToggleProximity,
                    $"{(string.IsNullOrWhiteSpace(ProximityActionLabel) ? "Disable Proximity" : ProximityActionLabel)}...",
                    stateIsOn: IsProximityKeepAwakeActive()),
                BuildWorkflowGuideActionItem(WorkflowGuideLaunchActionLabel, LaunchStudyAppCommand, CanLaunchStudyRuntime, "Launching Study Runtime..."),
                BuildWorkflowGuideActionItem("Refresh Snapshot", RefreshDeviceSnapshotCommand, true, "Refreshing Snapshot...")
            ],
            8 =>
            [
                BuildWorkflowGuideActionItem(TestLslSenderActionLabel, ToggleTestLslSenderCommand, CanToggleTestLslSender, stateIsOn: IsTestLslSenderToggleState),
                BuildWorkflowGuideActionItem("Probe Connection", ProbeLslConnectionCommand, true, "Probing Connection..."),
                BuildWorkflowGuideActionItem("Analyze Windows Environment", AnalyzeWindowsEnvironmentCommand, true, "Analyzing Windows Environment...")
            ],
            9 =>
            [
                BuildWorkflowGuideActionItem(
                    ParticlesToggleActionLabel,
                    ToggleParticlesCommand,
                    CanToggleParticles,
                    IsParticlesToggleState == true ? "Sending Particles Off..." : "Sending Particles On...",
                    stateIsOn: IsParticlesToggleState)
            ],
            10 =>
            [
                BuildWorkflowGuideActionItem("Start Calibration", StartBreathingCalibrationCommand, CanStartBreathingCalibration, "Starting Calibration...")
            ],
            11 =>
            [
                BuildWorkflowGuideActionItem("Run 20 Second Validation Capture", RunWorkflowValidationCaptureCommand, CanRunWorkflowValidationCapture, "Running Validation Capture...")
            ],
            _ =>
            [
                BuildWorkflowGuideActionItem("Reset Calibration", ResetBreathingCalibrationCommand, CanResetBreathingCalibration, "Resetting Calibration..."),
                BuildWorkflowGuideActionItem("Particles Off", ParticlesOffCommand, CanToggleParticles, "Sending Particles Off...")
            ]
        };

    private WorkflowGuideActionItem BuildWorkflowGuideActionItem(
        string label,
        AsyncRelayCommand command,
        bool isEnabled,
        string? runningLabel = null,
        bool? stateIsOn = null)
    {
        bool? actionIsEnabling = stateIsOn.HasValue ? !stateIsOn.Value : null;
        return new WorkflowGuideActionItem(
            command.IsRunning ? runningLabel ?? $"{label}..." : label,
            command,
            isEnabled && !command.IsRunning,
            command.IsRunning,
            stateIsOn,
            actionIsEnabling);
    }

    private WorkflowGuideCheckItem BuildHeadsetWakeAndProximityWorkflowGuideCheckItem()
    {
        var proximityState = EvaluateKeepAwakeProximityGuideState();
        var level = GetOutcomeSeverity(HeadsetAwakeLevel) >= GetOutcomeSeverity(proximityState.Level)
            ? HeadsetAwakeLevel
            : proximityState.Level;
        var summary = GetOutcomeSeverity(HeadsetAwakeLevel) >= GetOutcomeSeverity(proximityState.Level)
            ? HeadsetAwakeSummary
            : proximityState.Summary;
        var detail = BuildGuideDetailLines(
            ("Wake state", $"{HeadsetAwakeSummary} {HeadsetAwakeDetail}".Trim()),
            ("Keep-awake proximity", $"{proximityState.Summary} {proximityState.Detail}".Trim()));
        return new WorkflowGuideCheckItem("Headset wake + proximity", summary, detail, level);
    }

    private WorkflowGuideHazardState EvaluateKeepAwakeProximityGuideState()
    {
        var selector = !string.IsNullOrWhiteSpace(_liveProximitySelector)
            ? _liveProximitySelector
            : ResolveHeadsetActionSelector();

        if (string.IsNullOrWhiteSpace(selector))
        {
            return new WorkflowGuideHazardState(
                OperationOutcomeKind.Warning,
                "Quest selector needed for keep-awake proximity.",
                "Probe USB or reconnect Wi-Fi ADB before relying on the keep-awake proximity override.");
        }

        var tracked = _appSessionState.GetTrackedProximity(selector);
        var liveStatus = string.Equals(_liveProximitySelector, selector, StringComparison.OrdinalIgnoreCase)
            ? _liveProximityStatus
            : null;

        if (IsProximityBypassExpected(tracked, liveStatus))
        {
            return new WorkflowGuideHazardState(
                OperationOutcomeKind.Success,
                liveStatus?.HoldActive == true
                    ? "Keep-awake proximity override is active."
                    : "Keep-awake proximity override is expected.",
                liveStatus?.HoldActive == true
                    ? "Quest vrpowermanager readback confirms the prox_close keep-awake override. On this build that means virtual proximity state CLOSE rather than DISABLED, so off-face wear-sensor sleep should not interrupt launch or later guide steps."
                    : "The companion is already tracking a keep-awake proximity override for this headset selector. Refresh the snapshot if you want fresh Quest vrpowermanager confirmation.");
        }

        return new WorkflowGuideHazardState(
            OperationOutcomeKind.Warning,
            "Keep-awake proximity override is not active.",
            "Disable proximity before launching Sussex and keep it active through the remaining guide steps. On this build, normal restored wear-sensor behavior reads as virtual proximity state DISABLED after automation_disable; the active override reads as CLOSE after prox_close. Without the override the headset can slip into sleep or stale-twin-state conditions even while Sussex stays foregrounded.");
    }

    private bool? IsProximityKeepAwakeActive()
    {
        var selector = !string.IsNullOrWhiteSpace(_liveProximitySelector)
            ? _liveProximitySelector
            : ResolveHeadsetActionSelector();
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var tracked = _appSessionState.GetTrackedProximity(selector);
        var liveStatus = string.Equals(_liveProximitySelector, selector, StringComparison.OrdinalIgnoreCase)
            ? _liveProximityStatus
            : null;
        return IsProximityBypassExpected(tracked, liveStatus);
    }

    private string BuildControllerWakeReminderSummary()
    {
        var active = ParseBool(GetFirstValue("tracker.breathing.controller.active"));
        return active == true
            ? "Right controller activity is already visible."
            : "Wake the right controller before launch.";
    }

    private string BuildControllerWakeReminderDetail()
    {
        var state = GetFirstValue("tracker.breathing.controller.state");
        return ParseBool(GetFirstValue("tracker.breathing.controller.active")) == true
            ? $"Runtime state {(string.IsNullOrWhiteSpace(state) ? "n/a" : state)}. The controller only needs to stay awake through launch."
            : "If the right controller is asleep when Sussex starts, it may not become active afterward. Wake it on the headset before you launch.";
    }

    private OperationOutcomeKind BuildControllerWakeReminderLevel()
        => ParseBool(GetFirstValue("tracker.breathing.controller.active")) == true
            ? OperationOutcomeKind.Success
            : OperationOutcomeKind.Warning;

    private string BuildWifiAdbGuideSummary()
        => _headsetStatus?.IsWifiAdbTransport == true
            ? "ADB over Wi-Fi is active."
            : _headsetStatus?.IsConnected == true
                ? "ADB over Wi-Fi is not active yet."
                : "Quest connection is not ready yet.";

    private string BuildWifiAdbGuideDetail()
    {
        if (_headsetStatus?.IsWifiAdbTransport == true)
        {
            return "The headset is reachable over Wi-Fi ADB. Continue to the Wi-Fi match step, then keep watching later steps to ensure the session stays on the Wi-Fi endpoint.";
        }

        if (_headsetStatus?.IsConnected == true)
        {
            return "The headset is still on USB only. Use Enable Wi-Fi ADB, then Connect Quest if the session does not switch to Wi-Fi automatically.";
        }

        return "Connect the headset over USB first, then enable Wi-Fi ADB.";
    }

    private static string BuildWorkflowGuideLastActionSummary(string actionLabel, OperationOutcomeKind level)
    {
        if (IsBreathingCalibrationStartAction(actionLabel) &&
            level != OperationOutcomeKind.Failure)
        {
            return "Calibration command sent. Watch telemetry for the verdict.";
        }

        return level switch
        {
            OperationOutcomeKind.Success => $"{actionLabel} completed.",
            OperationOutcomeKind.Warning => $"{actionLabel} needs attention.",
            OperationOutcomeKind.Failure => $"{actionLabel} needs attention.",
            _ => $"{actionLabel} finished."
        };
    }

    private static void ReplaceWorkflowGuideCheckItems(
        ObservableCollection<WorkflowGuideCheckItem> target,
        IReadOnlyList<WorkflowGuideCheckItem> source)
    {
        var canUpdateInPlace = target.Count == source.Count
            && target.Zip(source, (existing, incoming) => string.Equals(existing.Label, incoming.Label, StringComparison.Ordinal))
                .All(matches => matches);

        if (!canUpdateInPlace)
        {
            ReplaceItems(target, source);
            return;
        }

        for (var index = 0; index < source.Count; index++)
        {
            target[index].UpdateFrom(source[index]);
        }
    }

    private static void ReplaceWorkflowGuideActionItems(
        ObservableCollection<WorkflowGuideActionItem> target,
        IReadOnlyList<WorkflowGuideActionItem> source)
    {
        var canUpdateInPlace = target.Count == source.Count
            && target.Zip(source, (existing, incoming) => ReferenceEquals(existing.Command, incoming.Command))
                .All(matches => matches);

        if (!canUpdateInPlace)
        {
            ReplaceItems(target, source);
            return;
        }

        for (var index = 0; index < source.Count; index++)
        {
            target[index].UpdateFrom(source[index]);
        }
    }

    private WorkflowGuideGateState EvaluateDeviceProfileGateState()
        => new(
            ToAdvisoryLevel(DeviceProfileLevel),
            DeviceProfileSummary,
            DeviceProfileDetail,
            DeviceProfileLevel == OperationOutcomeKind.Success);

    private WorkflowGuideGateState EvaluateHeadsetBatteryGateState()
    {
        var minimum = GetDeviceProfileIntegerValue("viscereality.minimum_headset_battery_percent");
        var battery = _headsetStatus?.BatteryLevel;
        if (!minimum.HasValue)
        {
            return new WorkflowGuideGateState(OperationOutcomeKind.Preview, HeadsetBatteryLabel, "No minimum headset battery threshold is configured in the study profile yet.", true);
        }

        if (!battery.HasValue)
        {
            return new WorkflowGuideGateState(OperationOutcomeKind.Warning, "Headset battery not reported yet.", $"Expected at least {minimum.Value}% headset battery before leaving the bench.", false);
        }

        var ready = battery.Value >= minimum.Value;
        return new WorkflowGuideGateState(
            ready ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning,
            ready
                ? $"Headset battery {battery.Value}% meets the {minimum.Value}% floor."
                : $"Headset battery {battery.Value}% is below the {minimum.Value}% floor.",
            HeadsetBatteryLabel,
            ready);
    }

    private WorkflowGuideGateState EvaluateRightControllerBatteryGateState()
    {
        var minimum = GetDeviceProfileIntegerValue("viscereality.minimum_right_controller_battery_percent");
        var rightController = _headsetStatus?.Controllers?.FirstOrDefault(controller => string.Equals(controller.HandLabel, "Right", StringComparison.OrdinalIgnoreCase));
        if (!minimum.HasValue)
        {
            return new WorkflowGuideGateState(OperationOutcomeKind.Preview, BuildControllerBatteryLabel(_headsetStatus?.Controllers), "No minimum right-controller battery threshold is configured in the study profile yet.", true);
        }

        if (rightController?.BatteryLevel is null)
        {
            return new WorkflowGuideGateState(OperationOutcomeKind.Warning, "Right controller battery not reported yet.", $"Expected at least {minimum.Value}% on the right controller before leaving the bench.", false);
        }

        var ready = rightController.BatteryLevel.Value >= minimum.Value;
        var controllerLabel = FormatControllerBatteryLabel("R", rightController);
        return new WorkflowGuideGateState(
            ready ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning,
            ready
                ? $"Right controller {controllerLabel} meets the {minimum.Value}% floor."
                : $"Right controller {controllerLabel} is below the {minimum.Value}% floor.",
            "Wake the right controller before launching Sussex so it remains active afterward.",
            ready);
    }

    private WorkflowGuideGateState EvaluateSoftwareIdentityGateState()
    {
        if (!SurfaceVerifiedBaselineInShell)
        {
            var advisorySummary = string.IsNullOrWhiteSpace(PinnedBuildSummary)
                ? "Software identity baseline is currently advisory only."
                : PinnedBuildSummary;
            var advisoryDetail = string.IsNullOrWhiteSpace(PinnedBuildDetail)
                ? "Verified OS/build baseline tracking remains in code, but it is intentionally hidden in the operator shell for now. APK hash verification stays active in the Sussex APK step."
                : $"{PinnedBuildDetail}{Environment.NewLine}Verified OS/build baseline tracking remains in code, but it is intentionally hidden in the operator shell for now.";
            return new WorkflowGuideGateState(
                ToAdvisoryLevel(PinnedBuildLevel),
                advisorySummary,
                advisoryDetail,
                true);
        }

        if (VerificationBaseline is null)
        {
            return new WorkflowGuideGateState(
                OperationOutcomeKind.Warning,
                "No verified Sussex software baseline is recorded yet.",
                "This step requires a verified OS/build baseline so the headset software identity can be checked safely.",
                false);
        }

        var ready = PinnedBuildLevel == OperationOutcomeKind.Success || IsPinnedBuildReadyForApprovalRun();
        var summary = ready && PinnedBuildLevel != OperationOutcomeKind.Success
            ? "Pinned Sussex APK is newer than the last verified run."
            : PinnedBuildSummary;
        var detail = ready && PinnedBuildLevel != OperationOutcomeKind.Success
            ? $"{PinnedBuildDetail} OS/build/profile still match the approved Sussex baseline. Continue with this live session as the approval run for the new APK hash."
            : PinnedBuildDetail;
        return new WorkflowGuideGateState(
            ready ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning,
            summary,
            detail,
            ready);
    }

    private static OperationOutcomeKind ToAdvisoryLevel(OperationOutcomeKind level)
        => level == OperationOutcomeKind.Failure ? OperationOutcomeKind.Warning : level;

    private bool IsPinnedBuildReadyForApprovalRun()
    {
        var baseline = VerificationBaseline;
        if (baseline is null)
        {
            return false;
        }

        if (HashMatches(baseline.ApkSha256, _study.App.Sha256))
        {
            return false;
        }

        var installedHash = _installedAppStatus?.InstalledSha256 ?? string.Empty;
        var installedMatchesPinnedHash = _installedAppStatus?.IsInstalled == true && HashMatches(installedHash, _study.App.Sha256);
        var hasSoftwareIdentity =
            !string.IsNullOrWhiteSpace(_headsetStatus?.SoftwareReleaseOrCodename) &&
            !string.IsNullOrWhiteSpace(_headsetStatus?.SoftwareBuildId);
        var softwareMatchesBaseline =
            hasSoftwareIdentity &&
            string.Equals(_headsetStatus!.SoftwareReleaseOrCodename, baseline.SoftwareVersion, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_headsetStatus.SoftwareBuildId, baseline.BuildId, StringComparison.OrdinalIgnoreCase);
        var displayMatchesBaseline =
            string.IsNullOrWhiteSpace(baseline.DisplayId) ||
            string.Equals(_headsetStatus?.SoftwareDisplayId, baseline.DisplayId, StringComparison.OrdinalIgnoreCase);
        var deviceProfileMatchesBaseline =
            _deviceProfileStatus?.IsActive == true &&
            (string.IsNullOrWhiteSpace(baseline.DeviceProfileId) ||
             string.Equals(baseline.DeviceProfileId, _study.DeviceProfile.Id, StringComparison.OrdinalIgnoreCase));

        return installedMatchesPinnedHash && softwareMatchesBaseline && displayMatchesBaseline && deviceProfileMatchesBaseline;
    }

    private int? GetDeviceProfileIntegerValue(string key)
        => _study.DeviceProfile.Properties.TryGetValue(key, out var rawValue) && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static OperationOutcomeKind CombineWorkflowGuideLevels(params OperationOutcomeKind[] levels)
    {
        if (levels.Any(level => level == OperationOutcomeKind.Failure))
        {
            return OperationOutcomeKind.Failure;
        }

        if (levels.Any(level => level == OperationOutcomeKind.Warning))
        {
            return OperationOutcomeKind.Warning;
        }

        if (levels.Any(level => level == OperationOutcomeKind.Preview))
        {
            return OperationOutcomeKind.Preview;
        }

        return OperationOutcomeKind.Success;
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static string BuildParticipantSessionId(DateTimeOffset startedAtUtc)
        => $"session-{startedAtUtc:yyyyMMddTHHmmssZ}";

    private static string BuildDatasetId(string studyId, string participantId, string sessionId)
        => string.Join(
            "__",
            StudyDataRecorderService.SanitizePathSegment(studyId),
            StudyDataRecorderService.SanitizePathSegment(StudyDataRecorderService.NormalizeParticipantId(participantId)),
            StudyDataRecorderService.SanitizePathSegment(sessionId));

    private static string BuildDatasetHash(
        string studyId,
        string participantId,
        string sessionId,
        DateTimeOffset startedAtUtc)
        => ComputeHashToken(
            "dataset",
            studyId,
            StudyDataRecorderService.NormalizeParticipantId(participantId),
            sessionId,
            startedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

    private static string BuildSettingsHash(
        string studyId,
        string packageId,
        string apkSha256,
        string appVersionName,
        string launchComponent,
        string headsetSoftwareVersion,
        string headsetBuildId,
        string headsetDisplayId,
        string deviceProfileId,
        IReadOnlyDictionary<string, string> deviceProfileProperties,
        string expectedLslStreamName,
        string expectedLslStreamType,
        double recenterDistanceThresholdUnits,
        string runtimeConfigHash,
        string runtimeHotloadProfileId,
        string runtimeHotloadProfileVersion,
        string runtimeHotloadProfileChannel,
        string sessionParameterStateHash,
        string environmentHash)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine("settings");
        builder.AppendLine(studyId ?? string.Empty);
        builder.AppendLine(packageId ?? string.Empty);
        builder.AppendLine(apkSha256 ?? string.Empty);
        builder.AppendLine(appVersionName ?? string.Empty);
        builder.AppendLine(launchComponent ?? string.Empty);
        builder.AppendLine(headsetSoftwareVersion ?? string.Empty);
        builder.AppendLine(headsetBuildId ?? string.Empty);
        builder.AppendLine(headsetDisplayId ?? string.Empty);
        builder.AppendLine(deviceProfileId ?? string.Empty);
        builder.AppendLine(expectedLslStreamName ?? string.Empty);
        builder.AppendLine(expectedLslStreamType ?? string.Empty);
        builder.AppendLine(recenterDistanceThresholdUnits.ToString("0.######", CultureInfo.InvariantCulture));
        builder.AppendLine(runtimeConfigHash ?? string.Empty);
        builder.AppendLine(runtimeHotloadProfileId ?? string.Empty);
        builder.AppendLine(runtimeHotloadProfileVersion ?? string.Empty);
        builder.AppendLine(runtimeHotloadProfileChannel ?? string.Empty);
        builder.AppendLine(sessionParameterStateHash ?? string.Empty);
        builder.AppendLine(environmentHash ?? string.Empty);
        builder.AppendLine(SussexClockAlignmentStreamContract.ProbeStreamName);
        builder.AppendLine(SussexClockAlignmentStreamContract.ProbeStreamType);
        builder.AppendLine(SussexClockAlignmentStreamContract.EchoStreamName);
        builder.AppendLine(SussexClockAlignmentStreamContract.EchoStreamType);
        builder.AppendLine(WorkflowClockAlignmentDurationSeconds.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(SussexClockAlignmentStreamContract.DefaultProbeIntervalMilliseconds.ToString(CultureInfo.InvariantCulture));

        foreach (var pair in deviceProfileProperties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(pair.Key);
            builder.Append('=');
            builder.AppendLine(pair.Value ?? string.Empty);
        }

        return ComputeHashToken(builder.ToString());
    }

    private static string ComputeHashToken(params string[] values)
    {
        var payload = string.Join("\n", values.Select(value => value ?? string.Empty));
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildRecorderEventDetail(OperationOutcome outcome)
        => string.IsNullOrWhiteSpace(outcome.Detail)
            ? outcome.Summary
            : $"{outcome.Summary} {outcome.Detail}".Trim();

    private bool IsExperimentActive()
        => ParseBool(GetFirstValue("study.session.experiment_active")) == true;

    private static OperationOutcome CombineWorkflowOutcomes(
        string actionLabel,
        IReadOnlyList<(string Label, OperationOutcome Outcome)> outcomes,
        string fallbackDetail)
    {
        if (outcomes.Count == 0)
        {
            return new OperationOutcome(OperationOutcomeKind.Preview, $"{actionLabel} prepared.", fallbackDetail);
        }

        var kind = outcomes.Any(entry => entry.Outcome.Kind == OperationOutcomeKind.Failure)
            ? OperationOutcomeKind.Failure
            : outcomes.Any(entry => entry.Outcome.Kind == OperationOutcomeKind.Warning)
                ? OperationOutcomeKind.Warning
                : outcomes.Any(entry => entry.Outcome.Kind == OperationOutcomeKind.Preview)
                    ? OperationOutcomeKind.Preview
                    : OperationOutcomeKind.Success;

        var detail = string.Join(
            " ",
            outcomes.Select(entry => $"{entry.Label}: {entry.Outcome.Summary}"));
        if (!string.IsNullOrWhiteSpace(fallbackDetail))
        {
            detail = string.IsNullOrWhiteSpace(detail)
                ? fallbackDetail
                : $"{detail} {fallbackDetail}";
        }

        var summary = kind switch
        {
            OperationOutcomeKind.Failure => $"{actionLabel} finished with at least one failure.",
            OperationOutcomeKind.Warning => $"{actionLabel} finished with warnings.",
            OperationOutcomeKind.Preview => $"{actionLabel} is only partially live in this environment.",
            _ => $"{actionLabel} completed."
        };

        return new OperationOutcome(kind, summary, detail);
    }

    private void UpdateWorkflowStatus()
    {
        UpdateParticipantSessionState();

        var headsetConnected = _headsetStatus?.IsConnected == true;
        var wifiReady = _headsetStatus?.IsWifiAdbTransport == true;
        var wifiMatchReady = _headsetStatus?.WifiSsidMatchesHost == true;
        var buildReady = PinnedBuildLevel == OperationOutcomeKind.Success;
        var profileReady = DeviceProfileLevel == OperationOutcomeKind.Success;
        var runtimeForeground = IsStudyRuntimeForeground();
        var hasLiveTwinState = _reportedTwinState.Count > 0;
        var controllerCalibrated = string.Equals(ControllerCalibrationLabel, "Calibrated", StringComparison.OrdinalIgnoreCase);
        var lslHealthy = LslLevel == OperationOutcomeKind.Success;
        var recenterVerified = RecenterLevel != OperationOutcomeKind.Preview;
        var particlesVerified = ParticlesLevel != OperationOutcomeKind.Preview;
        var canResetCalibration = CanResetBreathingCalibration;
        var canStartExperiment = CanStartExperiment;
        var canEndExperiment = CanEndExperiment;
        var setupReady = headsetConnected && wifiReady && wifiMatchReady && buildReady;
        var participantIdReady = !string.IsNullOrWhiteSpace(ParticipantIdDraft);
        var recordingActive = _activeRecordingSession is not null;
        var experimentActive = IsExperimentActive();
        var participantStartReady = false;
        var participantScreenshotReady = _lastQuestScreenshotCapturedAtUtc.HasValue;

        WorkflowSetupLevel = setupReady
            ? profileReady
                ? OperationOutcomeKind.Success
                : OperationOutcomeKind.Warning
            : !headsetConnected
                ? OperationOutcomeKind.Warning
                : PinnedBuildLevel == OperationOutcomeKind.Failure
                    ? OperationOutcomeKind.Failure
                    : OperationOutcomeKind.Warning;
        WorkflowSetupSummary = setupReady
            ? profileReady
                ? "Headset, Sussex build, and device profile are ready."
                : "Headset and Sussex build are ready; device-profile warnings remain."
            : !headsetConnected
                ? "Connect the headset before starting the Sussex protocol."
                : !wifiReady
                    ? "Enable or restore Wi-Fi ADB before starting the stable Sussex flow."
                    : !wifiMatchReady
                        ? "Match the headset Wi-Fi to this PC before starting the stable Sussex flow."
                    : PinnedBuildLevel == OperationOutcomeKind.Failure
                        ? "Pinned Sussex build still needs attention."
                        : "Headset setup is still incomplete.";
        WorkflowSetupDetail =
            $"{ConnectionTransportSummary} {WifiNetworkMatchSummary} {PinnedBuildSummary} {DeviceProfileSummary} {HeadsetBatteryLabel}. {HeadsetSoftwareVersionLabel}. " +
            "Brightness, battery, and software-identity readouts stay visible as bench context, but the Sussex setup path only blocks on APK readiness, Wi-Fi ADB, and Wi-Fi match. LSL remains the required bench check before participant handoff.";

        WorkflowKioskLevel = !setupReady
            ? OperationOutcomeKind.Preview
            : runtimeForeground
                ? QuestScreenshotLevel == OperationOutcomeKind.Warning
                    ? OperationOutcomeKind.Warning
                    : OperationOutcomeKind.Success
                : OperationOutcomeKind.Warning;
        WorkflowKioskSummary = !setupReady
            ? "Finish headset setup before boundary work and runtime launch."
            : runtimeForeground
                ? QuestScreenshotLevel == OperationOutcomeKind.Warning
                    ? "Sussex runtime is up, but visual confirmation is still pending."
                    : "Sussex runtime is up and ready for bench verification."
                : "Boundary setup and runtime launch are still pending.";
        WorkflowKioskDetail =
            $"{HeadsetAwakeSummary} {ControllerSummary} {(string.IsNullOrWhiteSpace(BuildLaunchBlockInstruction()) ? string.Empty : $"{BuildLaunchBlockInstruction()} ")}Keep watching the connection card so the guided path stays on the Wi-Fi endpoint used during the study. {KioskMenuButtonAdvisory} " +
            "The launch path now disables the proximity wear-sensor before Sussex starts. Keep that keep-awake override active through the guide, then restore normal proximity when you are done with the live session.";

        WorkflowBenchLevel = !runtimeForeground
            ? OperationOutcomeKind.Preview
            : LslLevel == OperationOutcomeKind.Failure
                ? OperationOutcomeKind.Failure
                : hasLiveTwinState && lslHealthy
                    ? OperationOutcomeKind.Success
                    : OperationOutcomeKind.Warning;
        WorkflowBenchSummary = !runtimeForeground
            ? "Bench verification is waiting for the Sussex runtime."
            : WorkflowBenchLevel == OperationOutcomeKind.Failure
                ? "Bench verification failed the required LSL check."
                : WorkflowBenchLevel == OperationOutcomeKind.Success
                    ? "Bench verification cleared the required LSL check."
                    : "LSL verification is still pending before participant handoff.";
        WorkflowBenchDetail =
            $"{LslSummary} {ControllerSummary} Calibration {ControllerCalibrationLabel}. {RecenterSummary} {ParticlesSummary} "
            + (WindowsEnvironmentAnalysisHasRun
                ? $"{WindowsEnvironmentAnalysisSummary} {WindowsEnvironmentAnalysisTimestampLabel} "
                : "Use Analyze Windows Environment when the LSL step looks blocked by the local Windows machine rather than the headset. ")
            + "Recenter, particles, and controller calibration stay visible as bench warnings, but they no longer block the Sussex flow.";

        participantStartReady = runtimeForeground
            && WorkflowBenchLevel == OperationOutcomeKind.Success
            && canStartExperiment
            && participantIdReady
            && !recordingActive
            && !_participantRunStopping;

        WorkflowHandoffLevel = !runtimeForeground
            ? OperationOutcomeKind.Preview
            : canResetCalibration
                ? OperationOutcomeKind.Success
                : OperationOutcomeKind.Warning;
        WorkflowHandoffSummary = !runtimeForeground
            ? "Participant handoff follows runtime launch and bench verification."
            : !canResetCalibration
                ? "Calibration reset is still missing from the mirrored Sussex APK."
                : controllerCalibrated
                    ? "Participant handoff is ready for Experiment Session."
                    : "Reset calibration is available; use it before the headset changes hands, then move into Experiment Session.";
        WorkflowHandoffDetail =
            (canResetCalibration
                ? "The shell can send Reset Calibration before particles-off handoff, so the experimenter's controller calibration cannot leak into the participant run. "
                : "Reset Calibration is unavailable in the current Sussex shell metadata. ") +
            "After handoff, use the Experiment Session window for the real participant run. The handoff path should still end with the physical power button, not a remote sleep/wake command.";

        WorkflowParticipantStartLevel = !runtimeForeground
            ? OperationOutcomeKind.Preview
            : recordingActive || experimentActive
                ? OperationOutcomeKind.Success
                : participantStartReady && participantScreenshotReady && !ParticipantHasExistingSessions
                    ? OperationOutcomeKind.Success
                    : OperationOutcomeKind.Warning;
        WorkflowParticipantStartSummary = runtimeForeground
            ? recordingActive || experimentActive
                ? "Participant run is active."
                : participantStartReady
                    ? ParticipantHasExistingSessions
                        ? "Experiment Session is ready, with a duplicate-id warning."
                        : participantScreenshotReady
                            ? "Experiment Session is ready for Start Recording."
                            : "Experiment Session is ready, but the participant view screenshot is still pending."
                    : canStartExperiment
                        ? "Experiment Session is waiting for participant metadata or final visual checks."
                        : "Participant-start controls are still pending in the companion."
            : "Participant-start controls come after runtime launch and bench verification.";
        WorkflowParticipantStartDetail =
            $"{ParticipantEntrySummary} {ParticipantEntryDetail} " +
            (canStartExperiment
                    ? participantScreenshotReady
                    ? "The participant view screenshot has been captured for this handoff, and Start Recording in Experiment Session will publish the shared dataset and session ids to Quest before recording begins."
                    : "Capture one Quest screenshot after wake and recenter so the operator has a visual double-check before using Start Recording."
                : "Start Recording is unavailable because the Start Experiment command is missing from the current Sussex shell metadata.");

        WorkflowParticipantEndLevel = recordingActive || experimentActive || _participantRunStopping
            ? canEndExperiment
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Failure
            : OperationOutcomeKind.Preview;
        WorkflowParticipantEndSummary = recordingActive || experimentActive || _participantRunStopping
            ? _participantRunStopping
                ? "Participant wrap-up is in progress."
                : canEndExperiment
                    ? "Stop Recording is ready when the run is complete."
                    : "Participant-end cleanup is blocked because Stop Recording cannot reach End Experiment."
            : "Participant-end controls come after a participant run has started.";
        WorkflowParticipantEndDetail =
            $"{RecordingSummary} {RecordingDetail} " +
            (canEndExperiment
                ? "Stop Recording now stops the Quest-side participant recorder and the local Windows recorder, then runs reset calibration and particles off."
                : "Stop Recording is unavailable because End Experiment is missing from the current Sussex shell metadata.");

        WorkflowRuntimePendingSummary = CanStartBreathingCalibration && canResetCalibration && canStartExperiment && canEndExperiment
            ? "Current Sussex APK metadata exposes recenter, calibration start/reset, experiment start/end, and particle toggles."
            : CanStartBreathingCalibration
                ? "Current Sussex APK metadata exposes recenter, calibration start, and particle toggles."
                : "Current Sussex APK exposes recenter and particle toggles, but the newer session-control commands are not mirrored in this shell yet.";
        WorkflowRuntimePendingDetail =
            "Participant number intake, duplicate-id warning, local session recording, screenshot archiving, Quest-side participant recording, and shared dataset/session hash handoff are now wired into the Sussex shell contract.";

        if (!setupReady)
        {
            WorkflowCurrentStepLevel = WorkflowSetupLevel;
            WorkflowCurrentStepSummary = "1. Finish headset setup before starting the Sussex protocol.";
            WorkflowCurrentStepDetail = WorkflowSetupDetail;
            UpdateWorkflowGuideState();
            return;
        }

        if (!runtimeForeground)
        {
            WorkflowCurrentStepLevel = WorkflowKioskLevel;
            WorkflowCurrentStepSummary = "2. Boundary plus runtime launch is the next operator step.";
            WorkflowCurrentStepDetail = WorkflowKioskDetail;
            UpdateWorkflowGuideState();
            return;
        }

        if (WorkflowBenchLevel != OperationOutcomeKind.Success)
        {
            WorkflowCurrentStepLevel = WorkflowBenchLevel;
            WorkflowCurrentStepSummary = "3. Bench verification is the next operator step.";
            WorkflowCurrentStepDetail = WorkflowBenchDetail;
            UpdateWorkflowGuideState();
            return;
        }

        if (WorkflowHandoffLevel != OperationOutcomeKind.Success)
        {
            WorkflowCurrentStepLevel = WorkflowHandoffLevel;
            WorkflowCurrentStepSummary = "4. Participant handoff is the next operator step.";
            WorkflowCurrentStepDetail = WorkflowHandoffDetail;
            UpdateWorkflowGuideState();
            return;
        }

        if (recordingActive || experimentActive || _participantRunStopping)
        {
            WorkflowCurrentStepLevel = WorkflowParticipantEndLevel;
            WorkflowCurrentStepSummary = "6. Participant wrap-up in Experiment Session is the next operator step.";
            WorkflowCurrentStepDetail = WorkflowParticipantEndDetail;
            UpdateWorkflowGuideState();
            return;
        }

        WorkflowCurrentStepLevel = WorkflowParticipantStartLevel;
        WorkflowCurrentStepSummary = "5. Participant start in Experiment Session is the next operator step.";
        WorkflowCurrentStepDetail = WorkflowParticipantStartDetail;
        UpdateWorkflowGuideState();
    }

    private void UpdateLiveRuntimeCard()
    {
        var lastStateReceivedAt = _twinBridge is LslTwinModeBridge lslBridge
            ? lslBridge.LastStateReceivedAt
            : null;
        var studyRuntimeForeground = string.Equals(_headsetStatus?.ForegroundPackageId, _study.App.PackageId, StringComparison.OrdinalIgnoreCase);
        var visualConfirmationHint = BuildQuestVisualConfirmationHint();

        if (_reportedTwinState.Count == 0)
        {
            LiveRuntimeLevel = studyRuntimeForeground ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview;
            LiveRuntimeSummary = studyRuntimeForeground
                ? "Study runtime is active, but quest_twin_state is idle."
                : "Waiting for quest_twin_state.";
            LiveRuntimeDetail = studyRuntimeForeground
                ? $"The headset still reports the Sussex APK in front, but no fresh app-state frames are arriving yet. The Quest runtime may still be starting, paused, or off-face. {BuildTwinCommandTransportDetail()} {visualConfirmationHint}".Trim()
                : $"Launch the Sussex runtime and wait for quest_twin_state to start publishing before relying on the live study monitor. {BuildTwinCommandTransportDetail()} {visualConfirmationHint}".Trim();
            return;
        }

        var publisherPackage = GetFirstValue("app.package", "app.packageId", "foreground.package", "active.package", "package");
        if (string.IsNullOrWhiteSpace(publisherPackage))
        {
            publisherPackage = _headsetStatus?.ForegroundPackageId;
        }

        var matchesStudyRuntime = string.Equals(publisherPackage, _study.App.PackageId, StringComparison.OrdinalIgnoreCase);
        var isIdle = lastStateReceivedAt.HasValue && DateTimeOffset.UtcNow - lastStateReceivedAt.Value > TwinStateIdleThreshold;
        LiveRuntimeLevel = isIdle || !matchesStudyRuntime
            ? OperationOutcomeKind.Warning
            : OperationOutcomeKind.Success;
        LiveRuntimeSummary = isIdle
            ? studyRuntimeForeground
                ? "Live study state is stale. The headset may be paused or off-face."
                : "Live study state is stale."
            : matchesStudyRuntime
                ? "Live study runtime state is active."
                : "Live state is active, but the publisher does not clearly match the pinned study runtime.";
        LiveRuntimeDetail = isIdle
            ? $"The last quest_twin_state frame arrived at {lastStateReceivedAt!.Value.ToLocalTime():HH:mm:ss}. If the headset is not on-face, wake it or disable proximity before relying on live confirmation. {BuildTwinCommandTransportDetail()} {visualConfirmationHint}".Trim()
            : string.IsNullOrWhiteSpace(publisherPackage)
                ? $"Received {_reportedTwinState.Count} live key(s). The runtime did not include a package id in the current state frame. {BuildTwinCommandTransportDetail()} {visualConfirmationHint}".Trim()
                : $"Received {_reportedTwinState.Count} live key(s) from {publisherPackage}. {BuildTwinCommandTransportDetail()} {visualConfirmationHint}".Trim();
    }

    private void UpdateLslCard()
    {
        var expectedName = _study.Monitoring.ExpectedLslStreamName;
        var expectedType = _study.Monitoring.ExpectedLslStreamType;
        var testSenderActive = _testLslSignalService?.IsRunning == true;
        var lastBenchSendAt = _testLslSignalService?.LastSentAtUtc;
        var lastBenchValue = _testLslSignalService?.LastValue ?? 0f;
        var lastBenchSendLabel = lastBenchSendAt.HasValue
            ? $"{lastBenchValue:0.000} at {lastBenchSendAt.Value.ToLocalTime():HH:mm:ss}"
            : null;
        var lastStateReceivedAt = _twinBridge is LslTwinModeBridge lslBridge
            ? lslBridge.LastStateReceivedAt
            : null;
        var testSenderDetail = testSenderActive
            ? $"Companion TEST sender is publishing smoothed HRV biofeedback samples on {expectedName} / {expectedType}. Samples follow the study-style irregular heartbeat rhythm, packet arrival acts as the bench heartbeat trigger, and the payload is the routed coherence value."
            : $"Start the Windows TEST sender below to bench-check {expectedName} / {expectedType} with study-style smoothed-HRV payloads and irregular heartbeat-paced packet timing.";
        var streamName = GetFirstValue("study.lsl.filter_name") ?? GetFirstValue(_study.Monitoring.LslStreamNameKeys);
        var streamType = GetFirstValue("study.lsl.filter_type") ?? GetFirstValue(_study.Monitoring.LslStreamTypeKeys);
        var hasInputValue = TryGetObservedLslValue(out var inputValue, out var inputValueKey);
        var connectedName = GetFirstValue("study.lsl.connected_name");
        var connectedType = GetFirstValue("study.lsl.connected_type");
        var statusLine = GetFirstValue("study.lsl.status");
        var connectedFlag = ParseBool(GetFirstValue("study.lsl.connected"));
        var connectedCount = ParseInt(GetFirstValue("connection.lsl.connected_count"));
        var connectingCount = ParseInt(GetFirstValue("connection.lsl.connecting_count"));
        var totalCount = ParseInt(GetFirstValue("connection.lsl.total_count"));
        var inletFailureSummary = BuildLslInletFailureSummary(statusLine, expectedName, expectedType);
        var coherenceRouteLabel = GetFirstValue("routing.coherence.label");
        var coherenceRouteMode = GetFirstValue("routing.coherence.mode");
        var coherenceRouteIsExpected =
            string.Equals(coherenceRouteLabel, _study.Monitoring.ExpectedCoherenceLabel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(coherenceRouteMode, TestSenderCoherenceMode, StringComparison.Ordinal);

        LslValuePercent = hasInputValue ? inputValue * 100d : 0d;
        LslValueLabel = hasInputValue ? $"{inputValue:0.000}" : "Not echoed";
        LslExpectedStreamLabel = $"{expectedName} / {expectedType}";
        LslRuntimeTargetLabel =
            $"{(string.IsNullOrWhiteSpace(streamName) ? "n/a" : streamName)} / {(string.IsNullOrWhiteSpace(streamType) ? "n/a" : streamType)}";
        LslConnectedStreamLabel =
            $"{(string.IsNullOrWhiteSpace(connectedName) ? "n/a" : connectedName)} / {(string.IsNullOrWhiteSpace(connectedType) ? "n/a" : connectedType)}";
        LslConnectionStateLabel =
            $"Connected {connectedCount?.ToString() ?? (connectedFlag.HasValue ? (connectedFlag.Value ? "1" : "0") : "n/a")}, connecting {connectingCount?.ToString() ?? "n/a"}, total {totalCount?.ToString() ?? "n/a"}";
        LslBenchStateLabel = testSenderActive
            ? lastBenchSendLabel is not null
                ? $"Windows TEST sender active. Latest local send {lastBenchSendLabel}. Bench packets follow irregular heartbeat timing and carry smoothed HRV feedback 0..1."
                : "Windows TEST sender active. Bench packets follow irregular heartbeat timing and carry smoothed HRV feedback 0..1."
            : "Companion TEST sender off.";
        LslStatusLineLabel = string.IsNullOrWhiteSpace(statusLine)
            ? "Runtime did not publish an extra LSL status line."
            : statusLine;

        if (_reportedTwinState.Count == 0)
        {
            LslLevel = OperationOutcomeKind.Preview;
            LslSummary = "Waiting for LSL runtime state.";
            LslDetail = $"Expected stream: {expectedName} / {expectedType}. {testSenderDetail}";
            LslRuntimeTargetLabel = "Runtime target n/a";
            LslConnectedStreamLabel = "Connected stream n/a";
            LslConnectionStateLabel = "Connection counts n/a";
            LslEchoStateLabel = "No inlet value reported yet.";
            return;
        }

        var streamMatches = (string.IsNullOrWhiteSpace(expectedName) || string.Equals(streamName, expectedName, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(expectedType) || string.Equals(streamType, expectedType, StringComparison.OrdinalIgnoreCase));
        var hasConnectedInput = connectedFlag == true
            || connectedCount.GetValueOrDefault() > 0
            || !string.IsNullOrWhiteSpace(connectedName);
        LslEchoStateLabel = hasInputValue
            ? lastStateReceivedAt.HasValue
                ? $"Current Quest echo {inputValue:0.000} via {inputValueKey} in frame {lastStateReceivedAt.Value.ToLocalTime():HH:mm:ss}."
                : $"Current Quest echo {inputValue:0.000} via {inputValueKey}."
            : hasConnectedInput
                ? "Connected, but this public build does not echo the routed inlet value yet."
                : "No inlet value reported yet.";

        LslLevel = hasConnectedInput && streamMatches
            ? OperationOutcomeKind.Success
            : inletFailureSummary is not null
                ? OperationOutcomeKind.Failure
            : connectedFlag.HasValue || connectedCount.HasValue || connectingCount.HasValue || totalCount.HasValue
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Preview;
        LslSummary = hasConnectedInput
            ? string.IsNullOrWhiteSpace(connectedName)
                ? $"LSL input live: {connectedCount?.ToString() ?? "1"} connected."
                : $"LSL input live: {connectedName}."
            : inletFailureSummary is not null
                ? inletFailureSummary
            : connectingCount.GetValueOrDefault() > 0
                ? $"LSL input connecting: {connectingCount} stream(s) still resolving."
                : "No live LSL input reported yet.";
        if (hasInputValue)
        {
            LslSummary += hasConnectedInput
                ? $" Inlet coherence {inputValue:0.000}."
                : $" Quest echo {inputValue:0.000}.";
        }
        else if (hasConnectedInput)
        {
            LslSummary += " Inlet connected; this public build does not echo the routed inlet value yet.";
        }
        if (testSenderActive)
        {
            LslSummary += " Companion TEST sender active.";
        }
        var comparisonNote = testSenderActive && lastBenchSendLabel is not null
            ? hasInputValue
                ? $"The TEST sender line shows the latest Windows packet ({lastBenchSendLabel}). The Echo line shows the current Quest-reported public value, which can lag or reset between beats."
                : $"The TEST sender line shows the latest Windows packet ({lastBenchSendLabel}). Quest echo has not appeared in the current public frame yet."
            : string.Empty;
        var routeNote = hasConnectedInput && !coherenceRouteIsExpected && !string.IsNullOrWhiteSpace(coherenceRouteLabel)
            ? $"The inlet is healthy, but the runtime coherence route is still {coherenceRouteLabel} (mode {coherenceRouteMode ?? "n/a"}), so Sussex is not currently consuming this inlet for coherence."
            : hasConnectedInput
                ? "The inlet is healthy and matches the study stream target."
                : inletFailureSummary is not null
                    ? $"{inletFailureSummary} Windows can still look healthy here because the Quest twin-state return path is separate from the Sussex inlet subscription."
                : "The study runtime has not confirmed an active inlet connection yet.";
        LslDetail = $"{routeNote} {comparisonNote} {testSenderDetail}".Trim();
    }

    private static string? BuildLslInletFailureSummary(string? statusLine, string expectedName, string expectedType)
    {
        if (string.IsNullOrWhiteSpace(statusLine) || !LooksLikeLslInletConnectFailure(statusLine))
        {
            return null;
        }

        var expectedLabel = $"{(string.IsNullOrWhiteSpace(expectedName) ? "n/a" : expectedName)} / {(string.IsNullOrWhiteSpace(expectedType) ? "n/a" : expectedType)}";
        return LooksLikeTimeoutStatus(statusLine)
            ? $"Quest inlet connect failed: timeout while subscribing {expectedLabel}."
            : $"Quest inlet connect failed while subscribing {expectedLabel}.";
    }

    private static bool LooksLikeLslInletConnectFailure(string statusLine)
        => statusLine.Contains("connect failed", StringComparison.OrdinalIgnoreCase)
           || statusLine.Contains("connection failed", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeTimeoutStatus(string statusLine)
        => statusLine.Contains("timeoutexception", StringComparison.OrdinalIgnoreCase)
           || statusLine.Contains("timed out", StringComparison.OrdinalIgnoreCase)
           || statusLine.Contains("timeout", StringComparison.OrdinalIgnoreCase);

    private bool IsAutomaticBreathingActiveFromTwinState()
        => CaptureAutomaticBreathingTelemetry().AutomaticRoute;

    private bool IsAutomaticBreathingRunningFromTwinState()
        => CaptureAutomaticBreathingTelemetry().AutomaticRunning == true;

    private bool IsControllerVolumeBreathingActiveFromTwinState()
    {
        var routingMode = GetFirstValue("routing.breathing.mode");
        if (string.Equals(routingMode, "1", StringComparison.Ordinal))
        {
            return true;
        }

        var routingLabel = GetFirstValue("routing.breathing.label");
        return string.Equals(routingLabel, "Controller Volume", StringComparison.OrdinalIgnoreCase);
    }

    private AutomaticBreathingTelemetry CaptureAutomaticBreathingTelemetry()
    {
        var routingMode = GetFirstValue("routing.breathing.mode");
        var routingLabel = GetFirstValue("routing.breathing.label");
        var automaticRunning = ParseBool(GetFirstValue("routing.automatic_breathing.running"));
        var automaticValue = ParseUnitInterval(GetAutomaticBreathingValueRaw());
        var controllerVolumeRoute =
            string.Equals(routingMode, "1", StringComparison.Ordinal) ||
            string.Equals(routingLabel, "Controller Volume", StringComparison.OrdinalIgnoreCase);
        var automaticRoute =
            string.Equals(routingMode, "6", StringComparison.Ordinal) ||
            string.Equals(routingLabel, "Automatic Cycle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(routingLabel, "Automatic", StringComparison.OrdinalIgnoreCase);
        var hasAnyTelemetry =
            !string.IsNullOrWhiteSpace(routingMode) ||
            !string.IsNullOrWhiteSpace(routingLabel) ||
            automaticRunning.HasValue ||
            automaticValue.HasValue;

        return new AutomaticBreathingTelemetry(
            routingMode,
            routingLabel,
            automaticRoute,
            automaticRunning,
            controllerVolumeRoute,
            automaticValue,
            hasAnyTelemetry);
    }

    private bool IsAutomaticBreathingPending(AutomaticBreathingTelemetry telemetry)
        => _lastAutomaticBreathingRequest is not null
           && DateTimeOffset.UtcNow - _lastAutomaticBreathingRequest.RequestedAtUtc <= WorkflowGuidePendingCommandWindow
           && !AutomaticBreathingMatchesRequest(telemetry, _lastAutomaticBreathingRequest);

    private static bool AutomaticBreathingMatchesRequest(
        AutomaticBreathingTelemetry telemetry,
        AutomaticBreathingRequest request)
        => request.AutomaticModeSelected
            ? telemetry.AutomaticRoute && telemetry.AutomaticRunning == request.AutomaticRunning
            : telemetry.ControllerVolumeRoute;

    private string BuildAutomaticBreathingTwinStateDetail(AutomaticBreathingTelemetry telemetry)
    {
        if (!telemetry.HasAnyTelemetry)
        {
            return "No `routing.breathing.*`, `routing.automatic_breathing.running`, or automatic-cycle value readback has arrived from `quest_twin_state` yet.";
        }

        var frameLabel = string.IsNullOrWhiteSpace(LastTwinStateTimestampLabel)
            ? "the latest live frame"
            : LastTwinStateTimestampLabel;
        var routingLabel = string.IsNullOrWhiteSpace(telemetry.RoutingLabel) ? "n/a" : telemetry.RoutingLabel;
        var routingMode = string.IsNullOrWhiteSpace(telemetry.RoutingMode) ? "n/a" : telemetry.RoutingMode;
        var automaticState = telemetry.AutomaticRoute
            ? telemetry.AutomaticRunning == true
                ? "running"
                : telemetry.AutomaticRunning == false
                    ? "paused"
                    : "state n/a"
            : "not selected";
        var automaticValue = telemetry.AutomaticValue.HasValue
            ? $" Automatic value {telemetry.AutomaticValue.Value:0.000}."
            : string.Empty;
        return $"`quest_twin_state` reports route {routingLabel} (mode {routingMode}) with automatic cycle {automaticState} in {frameLabel}.{automaticValue}".Trim();
    }

    private string? GetAutomaticBreathingValueRaw()
    {
        if (_study?.Monitoring?.AutomaticBreathingValueKeys is { Count: > 0 } automaticValueKeys)
        {
            return GetFirstValue(automaticValueKeys);
        }

        return GetFirstValue("study.breathing.value01", "signal01.mock_pacer_breathing");
    }

    private void RememberAutomaticBreathingRequest(bool automaticModeSelected, bool automaticRunning, string requestedLabel)
        => _lastAutomaticBreathingRequest = new AutomaticBreathingRequest(
            automaticModeSelected,
            automaticRunning,
            requestedLabel,
            DateTimeOffset.UtcNow);

    private static string BuildControllerTrackingDetail(
        string? hand,
        bool? connected,
        bool? tracked,
        string? status)
    {
        var handLabel = string.IsNullOrWhiteSpace(hand)
            ? "controller"
            : $"{hand.Trim()} controller";
        var connectedLabel = connected switch
        {
            true => "connected",
            false => "not connected",
            _ => "connection n/a"
        };
        var trackedLabel = tracked switch
        {
            true => "tracked",
            false => "not tracked",
            _ => "tracking n/a"
        };
        var statusLabel = string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : $" ({status.Trim()})";

        return $"{handLabel} {connectedLabel}, {trackedLabel}{statusLabel}";
    }

    private void UpdateControllerCard()
    {
        var automaticTelemetry = CaptureAutomaticBreathingTelemetry();
        var volume = ParseUnitInterval(GetFirstValue(_study.Monitoring.ControllerValueKeys));
        var state = GetFirstValue("tracker.breathing.controller.state");
        var active = ParseBool(GetFirstValue("tracker.breathing.controller.active"));
        var calibrated = ParseBool(GetFirstValue("tracker.breathing.controller.calibrated"));
        var validating = ParseBool(GetFirstValue("tracker.breathing.controller.validating"));
        var validationProgress = ParseUnitInterval(GetFirstValue("tracker.breathing.controller.validation_progress01"));
        var validationAxisMode = GetFirstValue("tracker.breathing.controller.validation_axis_mode");
        var validationFramesObserved = ParseInt(GetFirstValue("tracker.breathing.controller.validation_frames_observed"));
        var validationFramesAccepted = ParseInt(GetFirstValue("tracker.breathing.controller.validation_frames_accepted"));
        var validationFramesRejected = ParseInt(GetFirstValue("tracker.breathing.controller.validation_frames_rejected"));
        var validationFramesRejectedBadTracking = ParseInt(GetFirstValue("tracker.breathing.controller.validation_frames_rejected_bad_tracking"));
        var validationFramesRejectedLowMotion = ParseInt(GetFirstValue("tracker.breathing.controller.validation_frames_rejected_low_motion"));
        var validationAcceptance = ParseUnitInterval(GetFirstValue("tracker.breathing.controller.validation_acceptance01"));
        var failureReason = GetFirstValue("tracker.breathing.controller.failure_reason");
        var selectedConnected = ParseBool(GetFirstValue(
            "tracker.breathing.controller.selected_controller_connected",
            "study.pose.controller.connected"));
        var selectedTracked = ParseBool(GetFirstValue(
            "tracker.breathing.controller.selected_controller_tracked",
            "study.pose.controller.tracked"));
        var activeHand = GetFirstValue(
            "tracker.breathing.controller.active_hand",
            "study.pose.controller.hand");
        var trackingStatus = GetFirstValue(
            "tracker.breathing.controller.tracking_status",
            "study.pose.controller.tracking_status");
        var routingLabel = GetFirstValue("routing.breathing.label");
        var routingMode = GetFirstValue("routing.breathing.mode");

        ControllerValuePercent = volume.HasValue ? volume.Value * 100d : 0d;
        ControllerValueLabel = volume.HasValue ? $"{volume.Value:0.000}" : "n/a";
        if (automaticTelemetry.AutomaticRoute)
        {
            BreathingStatusTitle = "Automatic Breathing";
            BreathingDriverValuePercent = automaticTelemetry.AutomaticValue.HasValue ? automaticTelemetry.AutomaticValue.Value * 100d : 0d;
            BreathingDriverValueText = automaticTelemetry.AutomaticValue.HasValue
                ? $"Current automatic cycle {automaticTelemetry.AutomaticValue.Value:0.000}"
                : automaticTelemetry.AutomaticRunning == false
                    ? "Automatic cycle paused."
                    : "Automatic cycle readback pending.";
            ControllerCalibrationPercent = 0d;
            ControllerCalibrationLabel = automaticTelemetry.AutomaticRunning == false
                ? "Automatic mode paused"
                : "Calibration ignored in automatic mode";
            ApplyControllerCalibrationQuality(new ControllerCalibrationQualityStatus(
                Visible: true,
                Level: OperationOutcomeKind.Preview,
                Accepted: false,
                Badge: "Not in use",
                Summary: "Controller calibration is not used while the automatic breathing cycle drives the sphere.",
                Expectation: "The automatic breathing cycle is in control, so controller calibration does not affect live breath tracking right now.",
                Metrics: automaticTelemetry.AutomaticValue.HasValue
                    ? $"Automatic value {automaticTelemetry.AutomaticValue.Value:0.000} · Cycle {(automaticTelemetry.AutomaticRunning == true ? "running" : "paused")}"
                    : $"Cycle {(automaticTelemetry.AutomaticRunning == true ? "running" : "paused")}",
                Cause: string.Empty,
                Detail: "Controller tracking, calibration, and failure counters remain available for bench checks, but they do not affect the automatic cycle."));

            if (_reportedTwinState.Count == 0)
            {
                ControllerLevel = OperationOutcomeKind.Preview;
                ControllerSummary = "Waiting for automatic breathing state.";
                ControllerDetail = "The Sussex study can switch into standalone automatic breathing, but no live twin-state values have arrived yet.";
                return;
            }

            bool automaticRunning = automaticTelemetry.AutomaticRunning == true;
            ControllerLevel = automaticRunning && automaticTelemetry.AutomaticValue.HasValue
                ? OperationOutcomeKind.Success
                : automaticRunning
                    ? OperationOutcomeKind.Warning
                    : OperationOutcomeKind.Preview;
            ControllerSummary = automaticRunning
                ? automaticTelemetry.AutomaticValue.HasValue
                    ? "Automatic breathing is driving the sphere."
                    : "Automatic breathing is active but its live cycle readback has not arrived yet."
                : "Automatic breathing is selected but paused.";
            ControllerDetail =
                $"Route {(string.IsNullOrWhiteSpace(routingLabel) ? "n/a" : routingLabel)} (mode {routingMode ?? "n/a"}). " +
                $"Automatic cycle {(automaticTelemetry.AutomaticRunning == true ? "running" : automaticTelemetry.AutomaticRunning == false ? "paused" : "state n/a")}. " +
                $"Automatic value {(automaticTelemetry.AutomaticValue.HasValue ? automaticTelemetry.AutomaticValue.Value.ToString("0.000", CultureInfo.InvariantCulture) : "n/a")}. " +
                $"Controller value {ControllerValueLabel}.";
            return;
        }

        BreathingStatusTitle = "Controller Breathing";
        BreathingDriverValuePercent = ControllerValuePercent;
        BreathingDriverValueText = $"Current controller volume {ControllerValueLabel}";
        ControllerCalibrationPercent = calibrated == true ? 100d : validationProgress.HasValue ? validationProgress.Value * 100d : 0d;
        var calibrationQuality = BuildControllerCalibrationQualityStatus(
            validating,
            calibrated,
            validationProgress,
            validationAxisMode,
            validationFramesObserved,
            validationFramesAccepted,
            validationFramesRejected,
            validationFramesRejectedBadTracking,
            validationFramesRejectedLowMotion,
            validationAcceptance,
            failureReason);
        ApplyControllerCalibrationQuality(calibrationQuality);
        ControllerCalibrationLabel = BuildControllerCalibrationLabel(calibrated, validating, validationProgress, calibrationQuality);

        if (_reportedTwinState.Count == 0)
        {
            ControllerLevel = OperationOutcomeKind.Preview;
            ControllerSummary = "Waiting for controller breathing state.";
            ControllerDetail = "The Sussex study expects controller-based breathing, but no live twin-state values have arrived yet.";
            return;
        }

        var controllerStateDetail =
            $"Route {(string.IsNullOrWhiteSpace(routingLabel) ? "n/a" : routingLabel)} (mode {routingMode ?? "n/a"}). " +
            $"State {(string.IsNullOrWhiteSpace(state) ? "n/a" : state)}. " +
            $"Controller value {ControllerValueLabel}. " +
            $"Tracking {BuildControllerTrackingDetail(activeHand, selectedConnected, selectedTracked, trackingStatus)}.";

        if (selectedConnected == false || selectedTracked == false)
        {
            ControllerLevel = OperationOutcomeKind.Warning;
            ControllerSummary = selectedConnected == false
                ? "Active controller is not connected."
                : "Active controller is not currently tracked.";
            ControllerDetail = $"{controllerStateDetail} Calibration buttons are blocked until controller tracking returns.".Trim();
            return;
        }

        if (calibrationQuality.Visible && calibrationQuality.Accepted)
        {
            ControllerLevel = active == true
                ? calibrationQuality.Level
                : OperationOutcomeKind.Warning;
            ControllerSummary = calibrationQuality.Level == OperationOutcomeKind.Warning
                ? "Breath tracking should work, but calibration quality is degraded."
                : active == true
                    ? "Breath tracking ready."
                    : "Calibration accepted, but controller breathing is not active yet.";
            ControllerDetail = $"{calibrationQuality.Expectation} {controllerStateDetail}".Trim();
            return;
        }

        if (validating == true)
        {
            ControllerLevel = OperationOutcomeKind.Preview;
            ControllerSummary = "Calibration in progress. Breath tracking is not ready yet.";
            ControllerDetail = $"{calibrationQuality.Expectation} {controllerStateDetail}".Trim();
            return;
        }

        if (calibrationQuality.Visible)
        {
            ControllerLevel = calibrationQuality.Level;
            ControllerSummary = calibrationQuality.Level == OperationOutcomeKind.Failure
                ? "Calibration rejected. Do not rely on breath tracking."
                : "Calibration not accepted yet. Do not rely on breath tracking.";
            ControllerDetail = $"{calibrationQuality.Expectation} {controllerStateDetail}".Trim();
            return;
        }

        ControllerLevel = active == true
            ? OperationOutcomeKind.Warning
            : state is null && routingMode is null
                ? OperationOutcomeKind.Preview
                : OperationOutcomeKind.Warning;
        ControllerSummary = active == true
            ? "Controller breathing is visible, but calibration has not started yet."
            : "Controller breathing is not active yet.";
        ControllerDetail = $"{controllerStateDetail} Start calibration before relying on controller breath tracking.".Trim();
    }

    private void ApplyControllerCalibrationQuality(ControllerCalibrationQualityStatus status)
    {
        ControllerCalibrationQualityVisible = status.Visible;
        ControllerCalibrationQualityLevel = status.Level;
        ControllerCalibrationQualityBadge = status.Badge;
        ControllerCalibrationQualitySummary = status.Summary;
        ControllerCalibrationQualityExpectation = status.Expectation;
        ControllerCalibrationQualityMetrics = status.Metrics;
        ControllerCalibrationQualityCause = status.Cause;
        ControllerCalibrationQualityDetail = status.Detail;
    }

    private ControllerCalibrationQualityStatus BuildControllerCalibrationQualityStatus(
        bool? validating,
        bool? calibrated,
        double? validationProgress,
        string? validationAxisMode,
        int? validationFramesObserved,
        int? validationFramesAccepted,
        int? validationFramesRejected,
        int? validationFramesRejectedBadTracking,
        int? validationFramesRejectedLowMotion,
        double? validationAcceptance,
        string? failureReason)
    {
        var hasValidationTelemetry =
            validating == true
            || calibrated == true
            || validationProgress.HasValue
            || validationFramesObserved.HasValue
            || validationFramesAccepted.HasValue
            || validationAcceptance.HasValue
            || !string.IsNullOrWhiteSpace(failureReason);

        if (!hasValidationTelemetry)
        {
            ResetControllerCalibrationStallTracking();
            return new ControllerCalibrationQualityStatus(
                Visible: false,
                Level: OperationOutcomeKind.Preview,
                Accepted: false,
                Badge: "Calibration quality n/a",
                Summary: "Quality guidance will appear during controller validation.",
                Expectation: string.Empty,
                Metrics: "Progress n/a · Observed n/a · Accepted n/a · Rejected n/a · Target n/a · Acceptance n/a",
                Cause: string.Empty,
                Detail: "Raw validation counters stay hidden until you expand details.");
        }

        var effectiveAcceptance = validationAcceptance
            ?? (validationFramesObserved is > 0 && validationFramesAccepted.HasValue
                ? Math.Clamp((double)validationFramesAccepted.Value / validationFramesObserved.Value, 0d, 1d)
                : null);
        var estimatedTargetFrames = EstimateControllerValidationTargetFrames(validationFramesAccepted, validationProgress, calibrated == true);
        var stalled = UpdateControllerCalibrationStallTracking(validating == true, validationFramesObserved, validationFramesAccepted);
        var acceptedFrames = validationFramesAccepted.GetValueOrDefault();
        var rejectedFrames = validationFramesRejected.GetValueOrDefault();
        var badTrackingRejects = validationFramesRejectedBadTracking.GetValueOrDefault();
        var lowMotionRejects = validationFramesRejectedLowMotion.GetValueOrDefault();
        var knownRejectBreakdown = badTrackingRejects + lowMotionRejects;
        var badTrackingDominant = knownRejectBreakdown > 0 && badTrackingRejects >= lowMotionRejects && badTrackingRejects >= Math.Ceiling(knownRejectBreakdown * 0.55);
        var lowMotionDominant = knownRejectBreakdown > 0 && lowMotionRejects > badTrackingRejects && lowMotionRejects >= Math.Ceiling(knownRejectBreakdown * 0.55);
        var lowAcceptance = effectiveAcceptance is <= 0.35d;
        var middlingAcceptance = effectiveAcceptance is > 0.35d and < 0.65d;
        var acceptedTargetReached = estimatedTargetFrames is int estimatedTarget
            ? acceptedFrames >= estimatedTarget
            : acceptedFrames > 0;
        var completedWithHealthyMargin =
            calibrated == true
            && acceptedTargetReached
            && effectiveAcceptance is null or >= 0.65d;
        var accepted = calibrated == true;

        var level = validating == true ? OperationOutcomeKind.Preview : OperationOutcomeKind.Success;
        var badge = accepted
            ? "Accepted"
            : validating == true
                ? "In progress"
                : "Not accepted";
        var summary = accepted
            ? "Breath tracking should work normally."
            : validating == true
                ? "Calibration is still collecting accepted frames."
                : "Breath tracking is not ready yet.";
        var expectation = accepted
            ? "Breath tracking should work normally."
            : validating == true
                ? "Wait for calibration to finish before relying on controller breath tracking."
                : "Do not rely on controller breath tracking yet.";
        var cause = string.Empty;

        if (stalled)
        {
            level = OperationOutcomeKind.Failure;
            badge = "Rejected";
            summary = "Calibration stalled";
            expectation = "Do not rely on controller breath tracking yet. Re-run calibration before expecting a usable signal.";
            cause = "No accepted frames in the last few updates";
        }
        else if (badTrackingDominant || lowAcceptance || ContainsCalibrationKeyword(failureReason, "tracking"))
        {
            if (accepted)
            {
                level = OperationOutcomeKind.Warning;
                badge = "Accepted with warnings";
                summary = "Breath tracking should work, but calibration quality is degraded.";
                expectation = "Breath tracking should work, but expect noisier or less stable tracking until you recalibrate.";
                cause = "Target reached, but tracking rejects were still high enough to reduce confidence";
            }
            else
            {
                level = OperationOutcomeKind.Failure;
                badge = "Rejected";
                summary = "Calibration was rejected because tracking was unstable.";
                expectation = "Do not rely on controller breath tracking yet. Re-run calibration with steadier controller tracking.";
                cause = "Many frames were rejected by tracking";
            }
        }
        else if (lowMotionDominant || middlingAcceptance || ContainsCalibrationKeyword(failureReason, "motion", "movement"))
        {
            if (accepted && completedWithHealthyMargin && !middlingAcceptance)
            {
                level = OperationOutcomeKind.Success;
                badge = "Accepted";
                summary = "Breath tracking should work, but the calibration margin was narrow.";
                expectation = "Breath tracking should work, but expect a weaker or less robust response until you recalibrate with broader motion.";
                cause = "Target reached; most rejected frames were low-motion rather than tracking loss";
            }
            else
            {
                level = OperationOutcomeKind.Warning;
                badge = "Not accepted";
                summary = "Calibration has not been accepted yet.";
                expectation = "Some controller volume may appear, but do not rely on controller breath tracking yet. Treat it as bench-only until you recalibrate with broader motion.";
                cause = "Many frames were rejected because the controller motion stayed too small";
            }
        }

        var progressLabel = calibrated == true && validationProgress is null
            ? "100%"
            : validationProgress.HasValue
                ? $"{validationProgress.Value * 100d:0}%"
                : "n/a";
        var acceptedLabel = validationFramesAccepted?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
        var targetLabel = estimatedTargetFrames?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
        var observedLabel = validationFramesObserved?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
        var rejectedLabel = validationFramesRejected?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
        var acceptanceLabel = effectiveAcceptance.HasValue
            ? $"{effectiveAcceptance.Value * 100d:0}%"
            : "n/a";
        var metrics = $"Progress {progressLabel} · Observed {observedLabel} · Accepted {acceptedLabel} · Rejected {rejectedLabel} · Target {targetLabel} · Acceptance {acceptanceLabel}";

        var detailParts = new List<string>
        {
            $"Axis {FormatControllerCalibrationAxisModeLabel(validationAxisMode)}.",
            $"Observed {FormatControllerValidationCount(validationFramesObserved)}.",
            $"Accepted {FormatControllerValidationCount(validationFramesAccepted)}.",
            $"Rejected {rejectedFrames.ToString(CultureInfo.InvariantCulture)} (tracking {FormatControllerValidationCount(validationFramesRejectedBadTracking)}, low motion {FormatControllerValidationCount(validationFramesRejectedLowMotion)})."
        };
        if (!string.IsNullOrWhiteSpace(failureReason))
        {
            detailParts.Add($"Failure reason {failureReason.Trim()}.");
        }

        return new ControllerCalibrationQualityStatus(
            Visible: true,
            Level: level,
            Accepted: accepted,
            Badge: badge,
            Summary: summary,
            Expectation: expectation,
            Metrics: metrics,
            Cause: cause,
            Detail: string.Join(" ", detailParts));
    }

    private static string FormatControllerCalibrationAxisModeLabel(string? axisMode)
    {
        if (string.IsNullOrWhiteSpace(axisMode))
        {
            return "n/a";
        }

        return axisMode.Trim() switch
        {
            "CalibrationMotionPrincipalAxis" => "Dynamic motion axis",
            "LiveControllerAxisWarmup" => "Fixed controller orientation",
            _ => axisMode.Trim()
        };
    }

    private static string BuildControllerCalibrationLabel(
        bool? calibrated,
        bool? validating,
        double? validationProgress,
        ControllerCalibrationQualityStatus qualityStatus)
    {
        if (calibrated == true)
        {
            return qualityStatus.Level == OperationOutcomeKind.Warning
                ? "Calibration accepted with warnings"
                : "Calibration accepted";
        }

        if (validating == true)
        {
            return validationProgress.HasValue
                ? $"Calibration in progress ({validationProgress.Value:P0})"
                : "Calibration in progress";
        }

        if (!qualityStatus.Visible)
        {
            return "Calibration not started";
        }

        return qualityStatus.Level == OperationOutcomeKind.Failure
            ? "Calibration rejected"
            : "Calibration not accepted yet";
    }

    private string BuildValidationCaptureActionSummary()
    {
        if (ValidationCaptureRunning)
        {
            return "Validation capture is already running. Watch the timing card below for the live burst and recording progress.";
        }

        if (!string.IsNullOrWhiteSpace(_recorderFaultDetail))
        {
            return "Validation capture is blocked by the current recorder fault. Clear the earlier failure before starting another run.";
        }

        if (string.IsNullOrWhiteSpace(ParticipantIdDraft))
        {
            return "Enter a temporary subject id first. The button stays disabled until the capture has a participant label.";
        }

        if (!CanStartExperiment || !CanEndExperiment)
        {
            return "This Sussex shell is missing the Start or End Experiment command, so the guided validation capture cannot run from here.";
        }

        if (!IsStudyRuntimeForeground())
        {
            return "Bring the Sussex runtime back to the foreground before starting the validation capture.";
        }

        if (_participantRunStopping)
        {
            return "Wait for the current participant run to finish closing before starting another validation capture.";
        }

        if (ClockAlignmentRunning)
        {
            return "Wait for the current clock-alignment burst to finish before starting another validation capture.";
        }

        if (ValidationCaptureCompleted)
        {
            return "The guide is ready to run another 20 second validation capture with the subject id shown above.";
        }

        return "The guide is ready to run the 20 second validation capture from this card.";
    }

    private bool UpdateControllerCalibrationStallTracking(bool validating, int? observedFrames, int? acceptedFrames)
    {
        if (!validating || !observedFrames.HasValue || !acceptedFrames.HasValue)
        {
            ResetControllerCalibrationStallTracking();
            return false;
        }

        if (_controllerValidationLastObservedFrames.HasValue && _controllerValidationLastAcceptedFrames.HasValue)
        {
            var observedDelta = observedFrames.Value - _controllerValidationLastObservedFrames.Value;
            var acceptedDelta = acceptedFrames.Value - _controllerValidationLastAcceptedFrames.Value;
            if (observedDelta >= 6 && acceptedDelta <= 0)
            {
                _controllerValidationStalledUpdateCount++;
            }
            else
            {
                _controllerValidationStalledUpdateCount = 0;
            }
        }
        else
        {
            _controllerValidationStalledUpdateCount = 0;
        }

        _controllerValidationLastObservedFrames = observedFrames;
        _controllerValidationLastAcceptedFrames = acceptedFrames;
        return _controllerValidationStalledUpdateCount >= 3;
    }

    private void ResetControllerCalibrationStallTracking()
    {
        _controllerValidationLastObservedFrames = null;
        _controllerValidationLastAcceptedFrames = null;
        _controllerValidationStalledUpdateCount = 0;
    }

    private static bool ContainsCalibrationKeyword(string? value, params string[] keywords)
        => !string.IsNullOrWhiteSpace(value)
            && keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static int? EstimateControllerValidationTargetFrames(int? acceptedFrames, double? progress01, bool calibrated)
    {
        if (acceptedFrames is not > 0)
        {
            return calibrated ? acceptedFrames : null;
        }

        if (progress01 is > 0.001d and <= 1d)
        {
            var estimate = (int)Math.Round(acceptedFrames.Value / progress01.Value, MidpointRounding.AwayFromZero);
            return Math.Max(acceptedFrames.Value, estimate);
        }

        return calibrated ? acceptedFrames : null;
    }

    private static string FormatControllerValidationCount(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    private void UpdateHeartbeatCard()
    {
        var valueText = GetFirstValue(_study.Monitoring.HeartbeatValueKeys);
        var routeLabel = GetFirstValue("routing.heartbeat.label");
        var routeMode = GetFirstValue("routing.heartbeat.mode");

        if (_reportedTwinState.Count == 0)
        {
            HeartbeatLevel = OperationOutcomeKind.Preview;
            HeartbeatSummary = "Waiting for heartbeat state.";
            HeartbeatDetail = "Heartbeat route and live value will appear once quest_twin_state is active.";
            return;
        }

        HeartbeatLevel = string.IsNullOrWhiteSpace(valueText) && string.IsNullOrWhiteSpace(routeLabel)
            ? OperationOutcomeKind.Warning
            : OperationOutcomeKind.Success;
        HeartbeatSummary = string.IsNullOrWhiteSpace(valueText)
            ? "Heartbeat route visible, but no live heartbeat value yet."
            : $"Heartbeat live value: {valueText}.";
        HeartbeatDetail = $"Route {(string.IsNullOrWhiteSpace(routeLabel) ? "n/a" : routeLabel)} (mode {routeMode ?? "n/a"}).";
    }

    private void UpdateCoherenceCard()
    {
        var routeLabel = GetFirstValue("routing.coherence.label");
        var routeMode = GetFirstValue("routing.coherence.mode");
        var usesHeartbeat = GetFirstValue("routing.coherence.uses_heartbeat_source");
        var lslConnected = ParseBool(GetFirstValue("study.lsl.connected")) == true
            || ParseInt(GetFirstValue("connection.lsl.connected_count")).GetValueOrDefault() > 0
            || !string.IsNullOrWhiteSpace(GetFirstValue("study.lsl.connected_name"));
        var routeMatchesExpected =
            string.Equals(routeLabel, _study.Monitoring.ExpectedCoherenceLabel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(routeMode, TestSenderCoherenceMode, StringComparison.Ordinal);
        var hasValue = TryGetRoutedCoherenceValue(routeMatchesExpected, out var value, out var sourceKey);

        CoherencePercent = hasValue ? value * 100d : 0d;
        CoherenceValueLabel = hasValue ? value.ToString("0.000", CultureInfo.InvariantCulture) : "n/a";
        CoherenceRouteSummary =
            $"Current route {(string.IsNullOrWhiteSpace(routeLabel) ? "n/a" : routeLabel)} (mode {routeMode ?? "n/a"}). Uses heartbeat source {usesHeartbeat ?? "n/a"}.";

        if (_reportedTwinState.Count == 0)
        {
            CoherenceLevel = OperationOutcomeKind.Preview;
            CoherenceSummary = "Waiting for coherence state.";
            CoherenceDetail = "Coherence route and live value will appear once quest_twin_state is active.";
            CoherenceRouteSummary = "Current route n/a.";
            return;
        }

        if (!routeMatchesExpected && lslConnected)
        {
            CoherenceLevel = OperationOutcomeKind.Warning;
            CoherenceSummary = $"LSL inlet connected, but coherence is still routed to {(string.IsNullOrWhiteSpace(routeLabel) ? "another source" : routeLabel)}.";
            CoherenceDetail = "This explains the apparent mismatch: the headset is listening on the expected LSL inlet, but Sussex is not currently using that inlet as its active coherence source. A hotload/config path is still selecting a different coherence route.";
            return;
        }

        CoherenceLevel = hasValue
            ? routeMatchesExpected ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning
            : OperationOutcomeKind.Warning;
        CoherenceSummary = hasValue
            ? $"Coherence live value: {CoherenceValueLabel}."
            : routeMatchesExpected
                ? "Coherence route is LSL Direct, but no live routed biofeedback value is reported yet."
                : "Coherence route visible, but no live coherence value yet.";
        CoherenceDetail = routeMatchesExpected
            ? _testLslSignalService.IsRunning
                ? $"The runtime is reporting the expected direct-LSL coherence route. This field shows the current Quest-reported routed biofeedback value{FormatSourceKeySuffix(sourceKey)}, not the latest local TEST packet, so it can briefly lag or return to baseline between beats."
                : $"The runtime is reporting the expected direct-LSL coherence route. This field follows the Quest-reported routed biofeedback value{FormatSourceKeySuffix(sourceKey)}."
            : "The runtime is reporting a non-study coherence route.";
    }

    private void UpdatePerformanceCard()
    {
        var fps = ParseDouble(GetFirstValue(_study.Monitoring.PerformanceFpsKeys));
        var frameMs = ParseDouble(GetFirstValue(_study.Monitoring.PerformanceFrameTimeKeys));
        var targetFps = ParseDouble(GetFirstValue(_study.Monitoring.PerformanceTargetFpsKeys));
        var refreshHz = ParseDouble(GetFirstValue(_study.Monitoring.PerformanceRefreshRateKeys));

        PerformancePercent = targetFps.HasValue && targetFps.Value > 0d
            ? Math.Clamp((fps.GetValueOrDefault() / targetFps.Value) * 100d, 0d, 100d)
            : fps.HasValue
                ? Math.Clamp((fps.Value / 120d) * 100d, 0d, 100d)
                : 0d;
        PerformanceValueLabel = fps.HasValue || frameMs.HasValue
            ? $"{(fps.HasValue ? $"{fps.Value:0.0} FPS" : "FPS n/a")} / {(frameMs.HasValue ? $"{frameMs.Value:0.0} ms" : "frame ms n/a")}"
            : "n/a";

        if (_reportedTwinState.Count == 0)
        {
            PerformanceLevel = OperationOutcomeKind.Preview;
            PerformanceSummary = "Waiting for performance telemetry.";
            PerformanceDetail = "Current fps, frame time, and the runtime target will appear once quest_twin_state is active.";
            return;
        }

        if (!fps.HasValue && !frameMs.HasValue && !targetFps.HasValue)
        {
            PerformanceLevel = OperationOutcomeKind.Warning;
            PerformanceSummary = "Performance telemetry not reported yet.";
            PerformanceDetail = "The Sussex runtime is live, but it has not published fps or frame-time values yet.";
            return;
        }

        var onTarget = !fps.HasValue || !targetFps.HasValue || targetFps.Value <= 0d || fps.Value >= targetFps.Value * 0.9d;
        PerformanceLevel = onTarget ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning;
        PerformanceSummary = fps.HasValue
            ? onTarget
                ? $"Performance live: {fps.Value:0.0} FPS."
                : $"Performance below target: {fps.Value:0.0} FPS."
            : "Performance telemetry is live.";
        PerformanceDetail =
            $"Frame time {(frameMs.HasValue ? $"{frameMs.Value:0.0} ms" : "n/a")}. " +
            $"Target {(targetFps.HasValue ? $"{targetFps.Value:0.#} FPS" : "n/a")}. " +
            $"Display refresh {(refreshHz.HasValue ? $"{refreshHz.Value:0.#} Hz" : "n/a")}.";
    }

    private void UpdateRecenterCard()
    {
        var distance = ParseDouble(GetFirstValue(_study.Monitoring.RecenterDistanceKeys));
        var anchorRecorded = ParseBool(GetFirstValue("study.recenter.anchor_recorded"));
        var confirmation = CaptureCommandConfirmation(_study.Controls.RecenterCommandActionId);
        var commandDetail = BuildCommandTrackingDetail(_lastRecenterCommandRequest, confirmation, "recenter");
        var waitingForConfirmation = IsCommandPending(_lastRecenterCommandRequest, confirmation);
        var hasConfirmedCommand = IsCommandConfirmed(_lastRecenterCommandRequest, confirmation) || HasConfirmationSignal(confirmation);
        var inferredEffect = CaptureRecenterEffect(_lastRecenterCommandRequest, distance);
        if (_reportedTwinState.Count == 0)
        {
            RecenterLevel = waitingForConfirmation ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview;
            RecenterSummary = waitingForConfirmation
                ? "Recenter sent from companion. Waiting for live headset confirmation."
                : "Waiting for recenter telemetry.";
            RecenterDetail = waitingForConfirmation
                ? $"{commandDetail} {BuildTwinCommandTransportDetail()}".Trim()
                : "The recenter action is available, but the live drift distance appears only when the APK publishes it.";
            RecenterDistancePercent = 0d;
            RecenterDistanceLabel = "n/a";
            return;
        }

        if (waitingForConfirmation && !inferredEffect.Observed)
        {
            RecenterLevel = OperationOutcomeKind.Warning;
            RecenterSummary = "Recenter sent from companion. Waiting for headset confirmation.";
            RecenterDetail = $"{commandDetail} {BuildTwinCommandTransportDetail()}".Trim();
            RecenterDistancePercent = 0d;
            RecenterDistanceLabel = "n/a";
            return;
        }

        if (anchorRecorded == false)
        {
            RecenterLevel = OperationOutcomeKind.Preview;
            RecenterSummary = hasConfirmedCommand
                ? "Recenter confirmed. Waiting for the first recenter anchor."
                : "Waiting for the first recenter anchor.";
            RecenterDetail = $"{commandDetail} {BuildTwinCommandTransportDetail()} The study runtime has not recorded a recenter anchor yet, so camera drift cannot be evaluated.".Trim();
            RecenterDistancePercent = 0d;
            RecenterDistanceLabel = "n/a";
            return;
        }

        if (!distance.HasValue)
        {
            RecenterLevel = hasConfirmedCommand
                ? OperationOutcomeKind.Preview
                : inferredEffect.Observed
                    ? OperationOutcomeKind.Warning
                    : OperationOutcomeKind.Preview;
            RecenterSummary = hasConfirmedCommand
                ? "Recenter confirmed. Drift telemetry not exposed yet."
                : inferredEffect.Observed
                    ? "Recenter effect observed, but no explicit headset ack was published."
                    : "Recenter drift telemetry not exposed yet.";
            RecenterDetail = $"{commandDetail} {BuildTwinCommandTransportDetail()} {BuildRecenterEffectDetail(inferredEffect)} The current public runtime does not publish camera distance from the last recenter point yet. The recenter action can still be sent.".Trim();
            RecenterDistancePercent = 0d;
            RecenterDistanceLabel = "n/a";
            return;
        }

        RecenterDistancePercent = Math.Clamp(distance.Value / Math.Max(_study.Monitoring.RecenterDistanceThresholdUnits, 0.01d), 0d, 1d) * 100d;
        RecenterDistanceLabel = $"{distance.Value:0.000} u";
        if (hasConfirmedCommand)
        {
            RecenterLevel = distance.Value > _study.Monitoring.RecenterDistanceThresholdUnits
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success;
            RecenterSummary = distance.Value > _study.Monitoring.RecenterDistanceThresholdUnits
                ? $"Recenter confirmed, but camera drift remains {distance.Value:0.000} units."
                : $"Recenter confirmed. Camera drift is within threshold at {distance.Value:0.000} units.";
        }
        else if (inferredEffect.Observed)
        {
            RecenterLevel = distance.Value > _study.Monitoring.RecenterDistanceThresholdUnits
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Warning;
            RecenterSummary = distance.Value > _study.Monitoring.RecenterDistanceThresholdUnits
                ? $"Recenter effect observed, but camera drift still reads {distance.Value:0.000} units."
                : $"Recenter effect observed. Camera drift is now {distance.Value:0.000} units, but the headset did not publish an explicit ack.";
        }
        else
        {
            RecenterLevel = distance.Value > _study.Monitoring.RecenterDistanceThresholdUnits
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success;
            RecenterSummary = distance.Value > _study.Monitoring.RecenterDistanceThresholdUnits
                ? $"Camera drift is {distance.Value:0.000} units from the last recenter."
                : $"Camera drift is within threshold at {distance.Value:0.000} units.";
        }

        RecenterDetail = $"{commandDetail} {BuildTwinCommandTransportDetail()} {BuildRecenterEffectDetail(inferredEffect)} Study threshold: {_study.Monitoring.RecenterDistanceThresholdUnits:0.000} units. Recenter anchor recorded: {(anchorRecorded == true ? "yes" : "unknown")}.".Trim();
    }

    private void UpdateParticlesCard()
    {
        var visibility = ParseBool(GetFirstValue(_study.Monitoring.ParticleVisibilityKeys));
        var requestedVisible = ParseBool(GetFirstValue("study.particles.requested_visible"));
        var renderOutputEnabled = ParseBool(GetFirstValue("study.particles.render_output_enabled"));
        var suppressedByOperator = ParseBool(GetFirstValue("study.particles.suppressed_by_operator"));
        var suppressedByHud = ParseBool(GetFirstValue("study.particles.suppressed_by_hud"));
        var confirmation = CaptureCommandConfirmation(_study.Controls.ParticleVisibleOnActionId);
        var commandDetail = BuildCommandTrackingDetail(_lastParticlesCommandRequest, confirmation, "particle visibility");
        var waitingForConfirmation = IsCommandPending(_lastParticlesCommandRequest, confirmation);
        var hasExplicitRequestedVisibility = requestedVisible.HasValue;
        var actualVisible = hasExplicitRequestedVisibility
            ? visibility
            : renderOutputEnabled ?? visibility;
        var inferredEffect = CaptureParticleVisibilityEffect(_lastParticlesCommandRequest, actualVisible);

        requestedVisible ??= visibility;
        if (_reportedTwinState.Count == 0)
        {
            ParticlesLevel = waitingForConfirmation ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview;
            ParticlesSummary = waitingForConfirmation
                ? $"{_lastParticlesCommandRequest?.Label ?? "Particle visibility"} sent from companion. Waiting for live headset confirmation."
                : "Waiting for runtime particle state.";
            ParticlesDetail = waitingForConfirmation
                ? $"{commandDetail} {BuildTwinCommandTransportDetail()}".Trim()
                : "Particle visibility reporting appears only if the study APK publishes it.";
            return;
        }

        if (waitingForConfirmation && !inferredEffect.Observed)
        {
            ParticlesLevel = OperationOutcomeKind.Warning;
            ParticlesSummary = $"{_lastParticlesCommandRequest?.Label ?? "Particle visibility"} sent from companion. Waiting for headset confirmation.";
            ParticlesDetail = $"{commandDetail} {BuildTwinCommandTransportDetail()} {BuildParticleRuntimeDetail(requestedVisible, actualVisible, renderOutputEnabled, suppressedByOperator, suppressedByHud)}".Trim();
            return;
        }

        if (!actualVisible.HasValue && !requestedVisible.HasValue)
        {
            ParticlesLevel = CanToggleParticles ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview;
            ParticlesSummary = "Particle visibility state not exposed yet.";
            ParticlesDetail = CanToggleParticles
                ? $"{commandDetail} {BuildTwinCommandTransportDetail()} The study shell can still send particle visibility commands even though the current runtime is not reporting the resulting state.".Trim()
                : "The current public runtime does not expose particle visibility commands or public state keys yet.";
            return;
        }

        ParticlesLevel = actualVisible == true && requestedVisible != false
            ? hasExplicitRequestedVisibility && inferredEffect.Observed && !IsCommandConfirmed(_lastParticlesCommandRequest, confirmation)
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success
            : actualVisible == false && requestedVisible == false
                ? hasExplicitRequestedVisibility && inferredEffect.Observed && !IsCommandConfirmed(_lastParticlesCommandRequest, confirmation)
                    ? OperationOutcomeKind.Warning
                    : OperationOutcomeKind.Success
                : OperationOutcomeKind.Warning;
        ParticlesSummary = actualVisible == true && requestedVisible == false
            ? "Particles are still visible even though the last operator request was Off."
            : actualVisible == true
                ? inferredEffect.Observed && !IsCommandConfirmed(_lastParticlesCommandRequest, confirmation)
                    ? "Particles are visible, but the headset did not publish an explicit command ack."
                    : "Particles are visible."
                : actualVisible == false && requestedVisible == true
                    ? "Particles were requested on, but render output is still suppressed."
                    : actualVisible == false
                        ? inferredEffect.Observed && !IsCommandConfirmed(_lastParticlesCommandRequest, confirmation)
                            ? "Particles are hidden, but the headset did not publish an explicit command ack."
                            : "Particles are hidden."
                        : "Particle visibility is partially reported.";
        ParticlesDetail = $"{commandDetail} {BuildTwinCommandTransportDetail()} {BuildParticleVisibilityEffectDetail(inferredEffect)} {BuildParticleRuntimeDetail(requestedVisible, actualVisible, renderOutputEnabled, suppressedByOperator, suppressedByHud)}".Trim();
    }

    private void UpdateProximityCard()
    {
        var selector = !string.IsNullOrWhiteSpace(_liveProximitySelector)
            ? _liveProximitySelector
            : ResolveHzdbSelector();
        ProximityActionLabel = "Disable Proximity";
        ProximityEvidenceLabel = "Latest readback n/a.";
        var tracked = _appSessionState.GetTrackedProximity(selector);
        var liveStatus = string.Equals(_liveProximitySelector, selector, StringComparison.OrdinalIgnoreCase)
            ? _liveProximityStatus
            : null;
        var trackedKeepAwakeExpected = IsProximityBypassExpected(tracked, liveStatus);

        UpdateHeadsetAwakeStatus(selector, tracked, liveStatus);

        if (!_hzdbService.IsAvailable)
        {
            ProximityLevel = trackedKeepAwakeExpected ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview;
            ProximityActionLabel = trackedKeepAwakeExpected ? "Enable Proximity" : "Disable Proximity";
            ProximitySummary = trackedKeepAwakeExpected
                ? "Keep-awake proximity override was requested."
                : "Live proximity readback unavailable.";
            ProximityDetail = trackedKeepAwakeExpected
                ? "The companion requested the Quest Multi Stream-style prox_close keep-awake override, but live vrpowermanager readback is unavailable because hzdb is not available."
                : "Run guided setup or install the official Quest tooling cache to read live Quest vrpowermanager proximity state. The direct ADB proximity broadcast still works when a headset selector is available.";
            ProximityEvidenceLabel = "Latest readback unavailable because hzdb is not available.";
            return;
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            ProximityLevel = OperationOutcomeKind.Preview;
            ProximitySummary = "Quest selector needed for proximity hold.";
            ProximityDetail = "Probe USB or connect the Quest first. The shell prefers the live ADB transport and falls back across saved Wi-Fi and USB selectors.";
            ProximityEvidenceLabel = "Latest readback unavailable because no active headset selector is available.";
            return;
        }

        var updatedLabel = tracked.UpdatedAtUtc.HasValue
            ? tracked.UpdatedAtUtc.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : "n/a";

        if (liveStatus?.Available == true)
        {
            ProximityEvidenceLabel = BuildProximityEvidenceLabel(liveStatus);
            var controlInterpretation = BuildProximityControlInterpretation(liveStatus);

            if (liveStatus.HoldActive)
            {
                ProximityLevel = OperationOutcomeKind.Success;
                ProximityActionLabel = "Enable Proximity";
                ProximitySummary = liveStatus.HoldUntilUtc.HasValue
                    ? $"Keep-awake proximity override is active until {liveStatus.HoldUntilUtc.Value.ToLocalTime():HH:mm}."
                    : "Keep-awake proximity override is active.";
                ProximityDetail =
                    $"{controlInterpretation} Normal wear-sensor sleep is bypassed until the hold is cleared or expires. " +
                    (tracked.Known && !tracked.ExpectedEnabled
                        ? tracked.DisableUntilUtc.HasValue
                            ? $"Companion last requested a hold until {tracked.DisableUntilUtc.Value.ToLocalTime():HH:mm}."
                            : "Companion last requested the keep-awake override through the direct Quest proximity broadcast."
                        : "Quest readback is authoritative even if the hold was toggled outside the companion.");
                return;
            }

            ProximityLevel = OperationOutcomeKind.Success;
            ProximitySummary = "Normal proximity sensor behavior is active.";
            ProximityDetail =
                $"{controlInterpretation} " +
                (tracked.Known && !tracked.ExpectedEnabled && !tracked.DisableWindowExpired
                    ? "The last companion-tracked hold is no longer active, which usually means the headset rebooted or proximity was re-enabled outside the companion."
                    : tracked.Known
                        ? $"Companion last updated proximity for {selector} at {updatedLabel}."
                        : $"No active companion-issued proximity hold is tracked for {selector}.");
            return;
        }

        if (liveStatus is { Available: false })
        {
            ProximityLevel = OperationOutcomeKind.Warning;
            ProximitySummary = "Live proximity readback unavailable.";
            ProximityDetail = $"{liveStatus.StatusDetail} Falling back to companion-tracked proximity state.";
            ProximityEvidenceLabel = "Latest readback unavailable; falling back to companion-tracked state.";
        }

        if (tracked.Known && !tracked.ExpectedEnabled && !tracked.DisableWindowExpired)
        {
            if (liveStatus is not { Available: false })
            {
                ProximityLevel = OperationOutcomeKind.Warning;
                ProximitySummary = tracked.DisableUntilUtc.HasValue
                    ? $"Proximity sensor expected disabled until {tracked.DisableUntilUtc.Value.ToLocalTime():HH:mm}."
                    : "Keep-awake proximity override was requested.";
                ProximityDetail =
                    tracked.DisableUntilUtc.HasValue
                        ? $"Companion last sent a timed proximity disable for {selector} at {updatedLabel}. Waiting for a fresh Quest vrpowermanager readback."
                        : $"Companion last sent the direct prox_close keep-awake override for {selector} at {updatedLabel}. Waiting for a fresh Quest vrpowermanager readback.";
            }

            ProximityActionLabel = "Enable Proximity";
            return;
        }

        if (tracked.DisableWindowExpired && tracked.UpdatedAtUtc.HasValue)
        {
            var expiredAt = tracked.DisableUntilUtc?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) ?? "n/a";
            if (liveStatus is not { Available: false })
            {
                ProximityLevel = OperationOutcomeKind.Preview;
                ProximitySummary = "Tracked proximity hold expired. Sensor expected on.";
                ProximityDetail =
                    $"The last companion-issued disable window ended at {expiredAt}. " +
                    "The shell is waiting for the next Quest vrpowermanager readback to confirm the live state.";
            }

            return;
        }

        if (liveStatus is not { Available: false })
        {
            ProximityLevel = OperationOutcomeKind.Success;
            ProximitySummary = "Proximity sensor expected on.";
            ProximityDetail = tracked.Known
                ? $"Companion last requested normal proximity behavior for {selector} at {updatedLabel}. Waiting for a fresh Quest vrpowermanager readback."
                : $"No active companion-issued proximity hold is tracked for {selector}. Waiting for the first Quest vrpowermanager readback.";
        }
    }

    private void UpdateQuestScreenshotCard()
    {
        if (!_hzdbService.IsAvailable)
        {
            QuestScreenshotLevel = OperationOutcomeKind.Preview;
            QuestScreenshotSummary = "Quest screenshot capture unavailable.";
            QuestScreenshotDetail = "Run guided setup or install the official Quest tooling cache before using Quest screenshot capture.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ResolveQuestScreenshotSelector()))
        {
            QuestScreenshotLevel = OperationOutcomeKind.Preview;
            QuestScreenshotSummary = "Quest screenshot capture needs a headset selector.";
            QuestScreenshotDetail = "Probe USB or connect the Quest first so the study shell can request a fresh Quest screenshot.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_questVisualConfirmationPendingReason))
        {
            QuestScreenshotLevel = OperationOutcomeKind.Warning;
            QuestScreenshotSummary = "Quest visual confirmation pending.";
            QuestScreenshotDetail = $"{_questVisualConfirmationPendingReason} Use Capture Quest Screenshot below to review the actual visible scene.";
            return;
        }

        if (_lastQuestScreenshotCapturedAtUtc.HasValue && !string.IsNullOrWhiteSpace(QuestScreenshotPath))
        {
            QuestScreenshotLevel = OperationOutcomeKind.Success;
            QuestScreenshotSummary = $"Quest screenshot captured at {_lastQuestScreenshotCapturedAtUtc.Value.ToLocalTime():HH:mm:ss}.";
            QuestScreenshotDetail = $"Latest Quest screenshot: {QuestScreenshotPath}";
            return;
        }

        QuestScreenshotLevel = OperationOutcomeKind.Preview;
        QuestScreenshotSummary = "No Quest screenshot captured yet.";
        QuestScreenshotDetail = "Capture a Quest screenshot whenever the visible headset scene needs confirmation.";
    }

    private string BuildQuestVisualConfirmationHint()
    {
        if (!_study.App.LaunchInKioskMode)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(_questVisualConfirmationPendingReason))
        {
            return $"{_questVisualConfirmationPendingReason} Use Capture Quest Screenshot in Bench tools because Quest shell focus can still disagree with the visible scene on this HorizonOS build.";
        }

        if (_lastQuestScreenshotCapturedAtUtc.HasValue && !string.IsNullOrWhiteSpace(QuestScreenshotPath))
        {
            return $"Latest Quest screenshot captured at {_lastQuestScreenshotCapturedAtUtc.Value.ToLocalTime():HH:mm:ss}. Review it whenever the shell state and the visible headset scene disagree.";
        }

        return "Quest shell focus can still disagree with the visible scene on this HorizonOS build. Capture a Quest screenshot in Bench tools whenever a visual verification step needs confirmation.";
    }

    private void UpdateTestLslSenderCard()
    {
        var expectedName = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : _study.Monitoring.ExpectedLslStreamName;
        var expectedType = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : _study.Monitoring.ExpectedLslStreamType;

        TestLslSenderActionLabel = _testLslSignalService.IsRunning ? "Stop TEST Sender" : "Start TEST Sender";
        UpdateLslRuntimeLibraryState();

        if (_testLslSignalService.IsRunning)
        {
            TestLslSenderLevel = OperationOutcomeKind.Warning;
            TestLslSenderSummary = "Windows TEST sender active.";
            TestLslSenderValueLabel = _testLslSignalService.LastSentAtUtc.HasValue
                ? $"Latest local send {_testLslSignalService.LastValue:0.000} at {_testLslSignalService.LastSentAtUtc.Value.ToLocalTime():HH:mm:ss}."
                : "Starting local smoothed-HRV stream...";
            TestLslSenderDetail =
                $"Smoothed HRV biofeedback samples are publishing locally on {expectedName} / {expectedType} with irregular beat-timed spacing and a {HrvBiofeedbackStreamContract.FeedbackDispatchDelayMs} ms post-beat dispatch profile. The study shell also requests Heartbeat Mode = LSL and Coherence Mode = LSL Direct while this bench sender is active.";
            return;
        }

        if (!_testLslSignalService.RuntimeState.Available)
        {
            TestLslSenderLevel = OperationOutcomeKind.Preview;
            TestLslSenderSummary = "Windows TEST sender unavailable.";
            TestLslSenderValueLabel = "Unavailable";
            TestLslSenderDetail = _testLslSignalService.RuntimeState.Detail;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_testLslSignalService.LastFaultDetail))
        {
            TestLslSenderLevel = OperationOutcomeKind.Failure;
            TestLslSenderSummary = "Windows TEST sender stopped after a local fault.";
            TestLslSenderValueLabel = "Faulted";
            TestLslSenderDetail = _testLslSignalService.LastFaultDetail;
            return;
        }

        TestLslSenderLevel = OperationOutcomeKind.Preview;
        TestLslSenderSummary = "Windows TEST sender off.";
        TestLslSenderValueLabel = "Not running";
        TestLslSenderDetail =
            $"Start the Windows TEST sender to publish smoothed HRV biofeedback samples on {expectedName} / {expectedType}. This is bench-only traffic; packet arrival acts as heartbeat timing and the payload is the routed coherence value.";
    }

    private void UpdateLslRuntimeLibraryState()
    {
        var runtimeState = _testLslSignalService.RuntimeState;
        LslRuntimeLibrarySummary = runtimeState.Available
            ? "liblsl runtime ready for the Windows TEST sender."
            : "liblsl runtime unavailable for the Windows TEST sender.";
        LslRuntimeLibraryDetail = runtimeState.Detail;
    }

    private void RefreshFocusRows(bool forceRebuild = false)
    {
        var sectionId = SelectedLiveSection?.Id ?? "lsl";
        var nextRows = BuildFocusRows().ToArray();
        var canPatchInPlace = !forceRebuild
            && string.Equals(_activeFocusSectionId, sectionId, StringComparison.OrdinalIgnoreCase)
            && FocusRows.Count == nextRows.Length;

        if (canPatchInPlace)
        {
            for (var index = 0; index < nextRows.Length; index++)
            {
                if (!string.Equals(FocusRows[index].Key, nextRows[index].Key, StringComparison.OrdinalIgnoreCase))
                {
                    canPatchInPlace = false;
                    break;
                }
            }
        }

        if (!canPatchInPlace)
        {
            FocusRows.Clear();
            foreach (var row in nextRows)
            {
                FocusRows.Add(new StudyStatusRowViewModel(row));
            }
        }
        else
        {
            for (var index = 0; index < nextRows.Length; index++)
            {
                FocusRows[index].Apply(nextRows[index]);
            }
        }

        _activeFocusSectionId = sectionId;
    }

    private IReadOnlyList<StudyStatusRow> BuildFocusRows()
    {
        var sectionId = SelectedLiveSection?.Id ?? "lsl";
        return sectionId switch
        {
            "lsl" => BuildLslRows(),
            "controller" => BuildControllerRows(),
            "heartbeat" => BuildHeartbeatRows(),
            "coherence" => BuildCoherenceRows(),
            "performance" => BuildPerformanceRows(),
            "controls" => BuildControlRows(),
            _ => BuildAllRows()
        };
    }

    private IReadOnlyList<StudyStatusRow> BuildLslRows()
    {
        return
        [
            CreateStudyRow("Target stream name", ["study.lsl.filter_name", .. _study.Monitoring.LslStreamNameKeys], _study.Monitoring.ExpectedLslStreamName, "Study-configured stream name."),
            CreateStudyRow("Target stream type", ["study.lsl.filter_type", .. _study.Monitoring.LslStreamTypeKeys], _study.Monitoring.ExpectedLslStreamType, "Study-configured stream type."),
            CreateLslValueRow(),
            CreateStudyRow("Connected stream name", ["study.lsl.connected_name"], string.Empty, "Currently connected LSL input name."),
            CreateStudyRow("Connected stream type", ["study.lsl.connected_type"], string.Empty, "Currently connected LSL input type."),
            CreateStudyRow("LSL connected", ["study.lsl.connected"], string.Empty, "Whether the study LSL inlet is currently connected."),
            CreateStudyRow("LSL status", ["study.lsl.status"], string.Empty, "Runtime status line published by the study telemetry bridge."),
            CreateStudyRow("Connected inputs", ["connection.lsl.connected_count"], string.Empty, "Count of connected LSL inputs."),
            CreateStudyRow("Connecting inputs", ["connection.lsl.connecting_count"], string.Empty, "Count of LSL inputs still resolving."),
            CreateStudyRow("Known inputs", ["connection.lsl.total_count"], string.Empty, "Total LSL inputs the runtime is currently tracking.")
        ];
    }

    private IReadOnlyList<StudyStatusRow> BuildControllerRows()
    {
        return
        [
            CreateStudyRow("Breathing route label", ["routing.breathing.label"], _study.Monitoring.ExpectedBreathingLabel, "Runtime-reported breathing route label."),
            CreateStudyRow("Breathing route mode", ["routing.breathing.mode"], string.Empty, "Runtime-reported breathing route mode."),
            CreateStudyRow("Controller active", ["tracker.breathing.controller.active"], string.Empty, "Whether controller breathing is currently active."),
            CreateStudyRow("Controller state", ["tracker.breathing.controller.state"], string.Empty, "Runtime-reported controller state."),
            CreateStudyRow("Automatic running", ["routing.automatic_breathing.running"], string.Empty, "Whether the standalone automatic breathing cycle is currently running."),
            CreateStudyRow("Controller calibrated", ["tracker.breathing.controller.calibrated"], string.Empty, "Whether controller breathing is calibrated."),
            CreateStudyRow("Controller validating", ["tracker.breathing.controller.validating"], string.Empty, "Whether controller validation is currently running."),
            CreateStudyRow("Validation progress", ["tracker.breathing.controller.validation_progress01"], string.Empty, "Controller validation progress."),
            CreateStudyRow("Validation acceptance", ["tracker.breathing.controller.validation_acceptance01"], string.Empty, "Accepted-frame ratio for controller validation."),
            CreateStudyRow("Validation axis", ["tracker.breathing.controller.validation_axis_mode"], string.Empty, "Controller validation axis mode."),
            CreateStudyRow("Frames observed", ["tracker.breathing.controller.validation_frames_observed"], string.Empty, "Observed controller-validation frames."),
            CreateStudyRow("Frames accepted", ["tracker.breathing.controller.validation_frames_accepted"], string.Empty, "Accepted controller-validation frames."),
            CreateStudyRow("Frames rejected", ["tracker.breathing.controller.validation_frames_rejected"], string.Empty, "Rejected controller-validation frames."),
            CreateStudyRow("Rejected: bad tracking", ["tracker.breathing.controller.validation_frames_rejected_bad_tracking"], string.Empty, "Validation frames rejected because controller tracking was unstable."),
            CreateStudyRow("Rejected: low motion", ["tracker.breathing.controller.validation_frames_rejected_low_motion"], string.Empty, "Validation frames rejected because controller movement was too small."),
            CreateStudyRow("Controller value", _study.Monitoring.ControllerValueKeys, string.Empty, "Latest breathing-control value."),
            CreateStudyRow("Automatic value", _study.Monitoring.AutomaticBreathingValueKeys, string.Empty, "Latest standalone automatic breathing-cycle value."),
            CreateStudyRow("Failure reason", ["tracker.breathing.controller.failure_reason"], string.Empty, "Failure reason when controller breathing cannot activate.")
        ];
    }

    private IReadOnlyList<StudyStatusRow> BuildHeartbeatRows()
    {
        return
        [
            CreateStudyRow("Heartbeat route label", ["routing.heartbeat.label"], _study.Monitoring.ExpectedHeartbeatLabel, "Runtime-reported heartbeat route label."),
            CreateStudyRow("Heartbeat route mode", ["routing.heartbeat.mode"], string.Empty, "Runtime-reported heartbeat route mode."),
            CreateStudyRow("Heartbeat value", _study.Monitoring.HeartbeatValueKeys, string.Empty, "Latest heartbeat value received by the runtime.")
        ];
    }

    private IReadOnlyList<StudyStatusRow> BuildCoherenceRows()
    {
        return
        [
            CreateStudyRow("Coherence route label", ["routing.coherence.label"], _study.Monitoring.ExpectedCoherenceLabel, "Runtime-reported coherence route label."),
            CreateStudyRow("Coherence route mode", ["routing.coherence.mode"], string.Empty, "Runtime-reported coherence route mode."),
            CreateStudyRow("Uses heartbeat source", ["routing.coherence.uses_heartbeat_source"], string.Empty, "Whether coherence derives from the active heartbeat source."),
            CreateCoherenceValueRow()
        ];
    }

    private StudyStatusRow CreateCoherenceValueRow()
    {
        var routeLabel = GetFirstValue("routing.coherence.label");
        var routeMode = GetFirstValue("routing.coherence.mode");
        var routeMatchesExpected =
            string.Equals(routeLabel, _study.Monitoring.ExpectedCoherenceLabel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(routeMode, TestSenderCoherenceMode, StringComparison.Ordinal);
        var configuredKey = _study.Monitoring.CoherenceValueKeys.FirstOrDefault() ?? "signal01.coherence_lsl";

        if (TryGetRoutedCoherenceValue(routeMatchesExpected, out var value, out var sourceKey))
        {
            return new StudyStatusRow(
                "Coherence value",
                sourceKey,
                value.ToString("0.000", CultureInfo.InvariantCulture),
                string.Empty,
                "Latest routed coherence value reported by quest_twin_state.",
                routeMatchesExpected ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning);
        }

        return new StudyStatusRow(
            "Coherence value",
            configuredKey,
            "Not reported",
            string.Empty,
            routeMatchesExpected
                ? "The runtime is routed to LSL Direct, but no Quest-reported direct-LSL coherence value has appeared in the current twin-state frame."
                : "Latest coherence value received by the runtime.",
            OperationOutcomeKind.Preview);
    }

    private IReadOnlyList<StudyStatusRow> BuildPerformanceRows()
    {
        return
        [
            CreateStudyRow("Current fps", _study.Monitoring.PerformanceFpsKeys, string.Empty, "Smoothed live framerate reported by the runtime."),
            CreateStudyRow("Frame time", _study.Monitoring.PerformanceFrameTimeKeys, string.Empty, "Smoothed live frame time in milliseconds."),
            CreateStudyRow("Target fps", _study.Monitoring.PerformanceTargetFpsKeys, string.Empty, "Current target frame-rate cap on the headset."),
            CreateStudyRow("Display refresh", _study.Monitoring.PerformanceRefreshRateKeys, string.Empty, "Current observed display refresh rate when reported by the runtime.")
        ];
    }

    private IReadOnlyList<StudyStatusRow> BuildControlRows()
    {
        return
        [
            CreateTwinCommandOutletRow(),
            CreateStudyRow("Last headset action id", ["study.command.last_action_id"], string.Empty, "Latest command action id acknowledged by the headset runtime."),
            CreateStudyRow("Last headset action label", ["study.command.last_action_label"], string.Empty, "Latest command label acknowledged by the headset runtime."),
            CreateStudyRow("Last headset action sequence", ["study.command.last_action_sequence"], string.Empty, "Latest command sequence acknowledged by the headset runtime."),
            CreateStudyRow("Last headset action time", ["study.command.last_action_at_utc"], string.Empty, "Latest command timestamp acknowledged by the headset runtime."),
            CreateStudyRow("Recenter distance", _study.Monitoring.RecenterDistanceKeys, _study.Monitoring.RecenterDistanceThresholdUnits.ToString("0.000", CultureInfo.InvariantCulture), "Distance from the last recenter point."),
            CreateStudyRow("Recenter anchor", ["study.recenter.anchor_recorded"], string.Empty, "Whether the study runtime has recorded a recenter anchor."),
            CreateStudyRow("Recenter command sequence", ["study.recenter.last_command_sequence"], string.Empty, "Latest headset-confirmed recenter command sequence."),
            CreateStudyRow("Recenter command time", ["study.recenter.last_command_at_utc"], string.Empty, "Latest headset-confirmed recenter command timestamp."),
            CreateStudyRow("Recenter anchor time", ["study.recenter.last_anchor_recorded_at_utc"], string.Empty, "Latest recenter-anchor timestamp reported by the headset."),
            CreateStudyRow("Particle visibility", _study.Monitoring.ParticleVisibilityKeys, string.Empty, "Published particle visibility state."),
            CreateStudyRow("Particle requested visibility", ["study.particles.requested_visible"], string.Empty, "Latest operator-requested particle visibility reported by the headset."),
            CreateStudyRow("Particle render output", ["study.particles.render_output_enabled"], string.Empty, "Whether the particle engine currently renders output."),
            CreateStudyRow("Particle operator suppression", ["study.particles.suppressed_by_operator"], string.Empty, "Whether the operator has suppressed particle rendering."),
            CreateStudyRow("Particle HUD suppression", ["study.particles.suppressed_by_hud"], string.Empty, "Whether the in-headset HUD has suppressed particle rendering."),
            CreateStudyRow("Particle command sequence", ["study.particles.last_command_sequence"], string.Empty, "Latest headset-confirmed particle-visibility command sequence."),
            CreateStudyRow("Particle command time", ["study.particles.last_command_at_utc"], string.Empty, "Latest headset-confirmed particle-visibility command timestamp."),
            CreateSupportRow("Recenter command", CanSendRecenterCommand, "Twin command is configured for this study shell."),
            CreateSupportRow("Particle toggle commands", CanToggleParticles, "Twin commands are configured for particle visibility.")
        ];
    }

    private IReadOnlyList<StudyStatusRow> BuildAllRows()
    {
        var rows = new List<StudyStatusRow>();
        rows.AddRange(BuildLslRows());
        rows.AddRange(BuildControllerRows());
        rows.AddRange(BuildHeartbeatRows());
        rows.AddRange(BuildCoherenceRows());
        rows.AddRange(BuildPerformanceRows());
        rows.AddRange(BuildControlRows());
        return rows
            .GroupBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private StudyStatusRow CreateStudyRow(string label, IReadOnlyList<string> keys, string expected, string detail)
    {
        var key = keys.FirstOrDefault() ?? "not-configured";
        var value = GetFirstValue(keys);
        var renderedValue = string.IsNullOrWhiteSpace(value) ? "Not reported" : value;
        var level = ClassifyValue(value, expected);
        return new StudyStatusRow(label, key, renderedValue, expected, detail, level);
    }

    private StudyStatusRow CreateLslValueRow()
    {
        var configuredKey = _study.Monitoring.LslValueKeys.FirstOrDefault() ?? "signal01.coherence_lsl";
        if (TryGetObservedLslValue(out var value, out var sourceKey))
        {
            return new StudyStatusRow(
                "Inlet value",
                sourceKey,
                value.ToString("0.000", CultureInfo.InvariantCulture),
                string.Empty,
                "Latest routed HRV biofeedback value echoed by quest_twin_state when public signal mirroring is available.",
                OperationOutcomeKind.Success);
        }

        return new StudyStatusRow(
            "Inlet value",
            configuredKey,
            "Not echoed by current public build",
            string.Empty,
            "The Sussex runtime confirms inlet connectivity here, but the current public twin-state frame only echoes the routed biofeedback value when signal mirroring is enabled.",
            OperationOutcomeKind.Preview);
    }

    private StudyStatusRow CreateTwinCommandOutletRow()
    {
        if (_twinBridge is not LslTwinModeBridge lslBridge)
        {
            return CreateComputedRow(
                "Companion command outlet",
                "local.twin.command_outlet",
                "Unavailable",
                string.Empty,
                "The live command-outlet transport counters are only available when the public LSL twin bridge is active.",
                OperationOutcomeKind.Preview);
        }

        var status = lslBridge.IsCommandOutletOpen ? "Publishing" : "Idle";
        var renderedSequence = lslBridge.LastPublishedCommandSequence > 0
            ? lslBridge.LastPublishedCommandSequence.ToString(CultureInfo.InvariantCulture)
            : "n/a";
        return CreateComputedRow(
            "Companion command outlet",
            "local.twin.command_outlet",
            $"{status} | sent {lslBridge.PublishedCommandCount.ToString(CultureInfo.InvariantCulture)} | last seq {renderedSequence}",
            string.Empty,
            "Local twin-command outlet status, matching the runtime-side sender counters.",
            lslBridge.IsCommandOutletOpen ? OperationOutcomeKind.Success : OperationOutcomeKind.Preview);
    }

    private static StudyStatusRow CreateComputedRow(
        string label,
        string key,
        string value,
        string expected,
        string detail,
        OperationOutcomeKind level)
        => new(label, key, value, expected, detail, level);

    private StudyStatusRow CreateSupportRow(string label, bool isAvailable, string detail)
        => new(
            label,
            label,
            isAvailable ? "Available" : "Not configured",
            string.Empty,
            detail,
            isAvailable ? OperationOutcomeKind.Success : OperationOutcomeKind.Preview);

    private OperationOutcomeKind ClassifyValue(string? value, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return string.IsNullOrWhiteSpace(value) ? OperationOutcomeKind.Preview : OperationOutcomeKind.Success;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return OperationOutcomeKind.Warning;
        }

        return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)
            ? OperationOutcomeKind.Success
            : OperationOutcomeKind.Warning;
    }

    private void EnsureTwinBridgeMonitoringStarted()
    {
        if (_twinBridge is not LslTwinModeBridge lslBridge)
        {
            return;
        }

        var outcome = lslBridge.Open();
        if (outcome.Kind == OperationOutcomeKind.Failure)
        {
            LiveRuntimeLevel = OperationOutcomeKind.Failure;
            LiveRuntimeSummary = outcome.Summary;
            LiveRuntimeDetail = outcome.Detail;
        }
    }

    private void UpdateBenchRefreshTimerState()
    {
        if (_benchRefreshTimer is null)
        {
            return;
        }

        var selector = ResolveHzdbSelector();
        var tracked = _appSessionState.GetTrackedProximity(selector);
        var needsLiveRefresh = _testLslSignalService.IsRunning
            || (RegularAdbSnapshotEnabled && _hzdbService.IsAvailable && !string.IsNullOrWhiteSpace(selector))
            || (tracked.Known && !tracked.ExpectedEnabled && !tracked.DisableWindowExpired);

        if (needsLiveRefresh)
        {
            if (!_benchRefreshTimer.IsEnabled)
            {
                _benchRefreshTimer.Start();
            }

            return;
        }

        _benchRefreshTimer.Stop();
    }

    private void OnBenchRefreshTimerTick(object? sender, EventArgs e)
    {
        RefreshBenchToolsStatus();
        UpdateLslCard();
        var selector = ResolveHzdbSelector();
        var tracked = _appSessionState.GetTrackedProximity(selector);
        if (RegularAdbSnapshotEnabled || (tracked.Known && !tracked.ExpectedEnabled && !tracked.DisableWindowExpired))
        {
            _ = RefreshProximityStatusAsync();
        }
    }

    private void OnTestLslSignalServiceStateChanged(object? sender, EventArgs e)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            RefreshBenchToolsStatus();
            UpdateLslCard();
            QueueMachineLslStateRefresh(MachineLslStateRefreshSettleDelay);
        });
    }

    private void OnTwinBridgeStateChanged(object? sender, EventArgs e)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            _twinRefreshPending = true;
            QueueMachineLslStateRefresh(MachineLslStateRefreshSettleDelay);
            if (_twinRefreshTimer is null)
            {
                RefreshLiveTwinState();
                _twinRefreshPending = false;
                return;
            }

            if (!_twinRefreshTimer.IsEnabled)
            {
                _twinRefreshTimer.Start();
            }
        });
    }

    private void OnTwinTimingMarkerReceived(object? sender, TwinTimingMarkerEvent marker)
    {
        _ = _dispatcher.InvokeAsync(() => TryRecordLiveTimingMarker(marker));
    }

    private void OnTwinRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!_twinRefreshPending)
        {
            _twinRefreshTimer?.Stop();
            return;
        }

        _twinRefreshPending = false;
        RefreshLiveTwinState();
        if (!_twinRefreshPending)
        {
            _twinRefreshTimer?.Stop();
        }
    }

    private QuestAppTarget CreateStudyTarget(string? apkPath)
        => StudyShellOperatorBindings.CreateQuestTarget(_study, apkPath);

    private DeviceProfile CreatePinnedDeviceProfile()
        => StudyShellOperatorBindings.CreateDeviceProfile(_study);

    private string ResolveInitialApkPath()
    {
        if (!string.IsNullOrWhiteSpace(_study.App.ApkPath) && File.Exists(_study.App.ApkPath))
        {
            return Path.GetFullPath(_study.App.ApkPath);
        }

        if (!_study.App.AllowManualSelection)
        {
            return string.Empty;
        }

        var savedPath = _studySessionState.GetApkPath(_study.Id);
        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            return Path.GetFullPath(savedPath);
        }

        return string.Empty;
    }

    private string ResolveHeadsetActionSelector()
        => ResolveHeadsetActionSelectorCandidates().FirstOrDefault() ?? string.Empty;

    private IReadOnlyList<string> ResolveHeadsetActionSelectorCandidates()
    {
        var connectedSelector = _headsetStatus?.IsConnected == true ? _headsetStatus.ConnectionLabel : null;
        var endpointDraft = string.IsNullOrWhiteSpace(EndpointDraft) ? null : EndpointDraft.Trim();
        var candidates = new List<string>(4);

        AddSelectorCandidate(candidates, connectedSelector);
        AddSelectorCandidate(candidates, endpointDraft);
        AddSelectorCandidate(candidates, _appSessionState.ActiveEndpoint);
        AddSelectorCandidate(candidates, _appSessionState.LastUsbSerial);

        return candidates;
    }

    private string ResolveHzdbSelector()
        => ResolveHeadsetActionSelector();

    private string ResolveQuestScreenshotSelector()
        => ResolveQuestScreenshotSelectorCandidates().FirstOrDefault() ?? string.Empty;

    private IReadOnlyList<string> ResolveQuestScreenshotSelectorCandidates()
    {
        var candidates = ResolveHeadsetActionSelectorCandidates();
        var ordered = new List<string>(candidates.Count);
        var wifiCandidates = candidates.Where(LooksLikeTcpSelector).ToArray();

        foreach (var candidate in wifiCandidates)
        {
            AddSelectorCandidate(ordered, candidate);
        }

        if (ordered.Count > 0)
        {
            return ordered;
        }

        foreach (var candidate in candidates)
        {
            if (!LooksLikeTcpSelector(candidate))
            {
                AddSelectorCandidate(ordered, candidate);
            }
        }

        return ordered;
    }

    private IReadOnlyList<string> ResolveQuestScreenshotCaptureMethods()
    {
        var currentStepUsesRuntimeView = WorkflowGuideStepIndex is 9 or 11;
        var runtimeIsForeground = _headsetStatus?.IsTargetForeground == true || IsStudyRuntimeForeground();
        return currentStepUsesRuntimeView || runtimeIsForeground
            ? QuestScreenshotRuntimeCaptureMethods
            : QuestScreenshotShellCaptureMethods;
    }

    private string BuildQuestScreenshotOutputPath()
    {
        var screenshotRoot = Path.Combine(
            CompanionOperatorDataLayout.ScreenshotsRootPath,
            _study.Id);
        Directory.CreateDirectory(screenshotRoot);
        return Path.Combine(screenshotRoot, $"quest_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fffffff}.png");
    }

    private static BitmapImage? LoadQuestScreenshotPreview(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeQuestScreenshotHash(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string DescribeSelectorTransport(string selector)
        => LooksLikeTcpSelector(selector)
            ? "Wi-Fi ADB"
            : "USB ADB";

    private void MarkQuestVisualConfirmationPending(string reason)
    {
        _questVisualConfirmationPendingReason = reason;
        UpdateQuestScreenshotCard();
        UpdateLiveRuntimeCard();
    }

    private void ClearQuestVisualConfirmationPending()
    {
        _questVisualConfirmationPendingReason = string.Empty;
        UpdateQuestScreenshotCard();
        UpdateLiveRuntimeCard();
    }

    private IReadOnlyList<string> ResolveHzdbSelectorCandidates()
        => ResolveHeadsetActionSelectorCandidates();

    private async Task<QuestProximityStatus?> RefreshProximityStatusAsync(bool force = false)
    {
        if (!_hzdbService.IsAvailable)
        {
            await DispatchAsync(() =>
            {
                _liveProximityStatus = null;
                _liveProximitySelector = null;
                _lastProximityRefreshAtUtc = null;
                RefreshBenchToolsStatus();
            }).ConfigureAwait(false);
            return null;
        }

        var selectors = await DispatchAsync(() => ResolveHzdbSelectorCandidates().ToArray()).ConfigureAwait(false);
        if (selectors.Length == 0)
        {
            await DispatchAsync(() =>
            {
                _liveProximityStatus = null;
                _liveProximitySelector = null;
                _lastProximityRefreshAtUtc = null;
                RefreshBenchToolsStatus();
            }).ConfigureAwait(false);
            return null;
        }

        var cachedStatus = await DispatchAsync(() =>
            selectors.Any(selector => string.Equals(_liveProximitySelector, selector, StringComparison.OrdinalIgnoreCase))
                ? _liveProximityStatus
                : null).ConfigureAwait(false);
        var shouldRefresh = await DispatchAsync(() =>
            force
            || _liveProximitySelector is null
            || !selectors.Any(selector => string.Equals(_liveProximitySelector, selector, StringComparison.OrdinalIgnoreCase))
            || _liveProximityStatus is null
            || !_lastProximityRefreshAtUtc.HasValue
            || DateTimeOffset.UtcNow - _lastProximityRefreshAtUtc.Value >= ProximityReadbackRefreshInterval).ConfigureAwait(false);

        if (!shouldRefresh)
            return cachedStatus;

        var entered = await DispatchAsync(() =>
        {
            if (_proximityRefreshPending)
                return false;

            _proximityRefreshPending = true;
            return true;
        }).ConfigureAwait(false);

        if (!entered)
            return cachedStatus;

        try
        {
            QuestProximityStatus? status = null;
            string? selectedSelector = null;

            foreach (var selector in selectors)
            {
                var current = await _hzdbService.GetProximityStatusAsync(selector).ConfigureAwait(false);
                if (selectedSelector is null)
                {
                    selectedSelector = selector;
                    status = current;
                }

                if (current.Available)
                {
                    selectedSelector = selector;
                    status = current;
                    break;
                }
            }

            if (status is null || string.IsNullOrWhiteSpace(selectedSelector))
            {
                return null;
            }

            await DispatchAsync(() =>
            {
                _liveProximitySelector = selectedSelector;
                _liveProximityStatus = status;
                _lastProximityRefreshAtUtc = DateTimeOffset.UtcNow;
                RefreshBenchToolsStatus();
            }).ConfigureAwait(false);
            return status;
        }
        finally
        {
            await DispatchAsync(() => _proximityRefreshPending = false).ConfigureAwait(false);
        }
    }

    private void SaveSession(string? endpoint = null, string? usbSerial = null)
    {
        _appSessionState = _appSessionState
            .WithEndpoint(endpoint)
            .WithUsbSerial(usbSerial);
        _appSessionState.Save();
    }

    private void SaveStudySession(string? apkPath)
    {
        _studySessionState = _studySessionState.WithApkPath(_study.Id, apkPath);
        _studySessionState.Save();
    }

    private string? GetFirstValue(params string[] keys)
        => GetFirstValue((IReadOnlyList<string>)keys);

    private string? GetFirstValue(IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            if (_reportedTwinState.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private bool? TryGetCurrentControllerCalibrationModeSelection()
        => ParseBool(GetFirstValue(
            ControllerCalibrationModeHotloadKey,
            "hotload." + ControllerCalibrationModeHotloadKey));

    private bool TryGetConfiguredUnitIntervalValue(IReadOnlyList<string> keys, out double value, out string sourceKey)
    {
        foreach (var key in keys)
        {
            if (_reportedTwinState.TryGetValue(key, out var rawValue) &&
                ParseUnitInterval(rawValue) is double parsedValue)
            {
                value = parsedValue;
                sourceKey = key;
                return true;
            }
        }

        value = 0d;
        sourceKey = string.Empty;
        return false;
    }

    private string GetReportedRuntimeApkHash()
        => GetFirstValue("study.session.apk_sha256") ?? string.Empty;

    private string GetReportedRuntimeEnvironmentHash()
        => GetFirstValue("study.session.environment_hash") ?? string.Empty;

    private string GetReportedRuntimeDeviceProfileId()
        => GetFirstValue("study.session.device_profile_id") ?? string.Empty;

    private bool TryGetSignalMirrorValue(string signalName, out double value, out string sourceKey)
    {
        const string NameSuffix = ".name";
        const string ValueSuffix = ".value01";

        foreach (var entry in _reportedTwinState)
        {
            if (!entry.Key.StartsWith("driver.stream.", StringComparison.OrdinalIgnoreCase) ||
                !entry.Key.EndsWith(NameSuffix, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.Value, signalName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var keyBase = entry.Key[..^NameSuffix.Length];
            var valueKey = keyBase + ValueSuffix;
            if (_reportedTwinState.TryGetValue(valueKey, out var rawValue) &&
                ParseUnitInterval(rawValue) is double parsedValue)
            {
                value = parsedValue;
                sourceKey = valueKey;
                return true;
            }
        }

        value = 0d;
        sourceKey = string.Empty;
        return false;
    }

    private void RememberCommandRequest(StudyTwinCommandRequest request)
    {
        _lastStudyTwinCommandRequest = request;

        if (string.Equals(request.ActionId, _study.Controls.RecenterCommandActionId, StringComparison.OrdinalIgnoreCase))
        {
            _lastRecenterCommandRequest = request;
            _lastRecordedRecenterConfirmationSignature = string.Empty;
            _lastRecordedRecenterEffectSignature = string.Empty;
            return;
        }

        if (MatchesParticleActionId(request.ActionId))
        {
            _lastParticlesCommandRequest = request;
            _lastRecordedParticlesConfirmationSignature = string.Empty;
            _lastRecordedParticlesEffectSignature = string.Empty;
        }
    }

    private void TryRecordCommandRequestEvent(StudyTwinCommandRequest request, OperationOutcome outcome)
    {
        if (_activeRecordingSession is null)
        {
            return;
        }

        if (string.Equals(request.ActionId, _study.Controls.StartExperimentActionId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.ActionId, _study.Controls.EndExperimentActionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            _activeRecordingSession.RecordEvent(
                "command.requested",
                $"Companion requested {request.Label} {FormatCommandSequence(request.Sequence)}. {BuildRecorderEventDetail(outcome)}",
                request.ActionId,
                outcome.Kind.ToString().ToLowerInvariant(),
                recordedAtUtc: request.SentAtUtc,
                sourceTimestampUtc: request.SentAtUtc);
        }
        catch (Exception exception)
        {
            SetRecorderFault("Write command request event", exception);
        }
    }

    private StudyTwinCommandRequest CreateCommandRequest(
        string actionId,
        string label,
        StudyTwinCommandConfirmation previousConfirmation,
        OperationOutcome outcome)
    {
        bool? requestedVisible = string.Equals(actionId, _study.Controls.ParticleVisibleOnActionId, StringComparison.OrdinalIgnoreCase)
            ? true
            : string.Equals(actionId, _study.Controls.ParticleVisibleOffActionId, StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
        var previousRecenterAnchorTimestampRaw = string.Equals(actionId, _study.Controls.RecenterCommandActionId, StringComparison.OrdinalIgnoreCase)
            ? GetFirstValue("study.recenter.last_anchor_recorded_at_utc")
            : null;
        var previousRecenterDistance = string.Equals(actionId, _study.Controls.RecenterCommandActionId, StringComparison.OrdinalIgnoreCase)
            ? ParseDouble(GetFirstValue(_study.Monitoring.RecenterDistanceKeys))
            : null;
        var previousObservedVisible = MatchesParticleActionId(actionId)
            ? GetCurrentReportedParticleVisibility()
            : null;

        return new StudyTwinCommandRequest(
            actionId,
            label,
            requestedVisible,
            ParsePublishedCommandSequence(outcome.Detail),
            DateTimeOffset.UtcNow,
            previousConfirmation.Sequence,
            previousConfirmation.TimestampRaw,
            previousRecenterAnchorTimestampRaw,
            previousRecenterDistance,
            previousObservedVisible);
    }

    private bool? GetCurrentReportedParticleVisibility()
    {
        var visibility = ParseBool(GetFirstValue(_study.Monitoring.ParticleVisibilityKeys));
        var requestedVisible = ParseBool(GetFirstValue("study.particles.requested_visible"));
        var renderOutputEnabled = ParseBool(GetFirstValue("study.particles.render_output_enabled"));
        return requestedVisible.HasValue
            ? visibility
            : renderOutputEnabled ?? visibility;
    }

    private bool? GetCurrentProximityEnabledState()
    {
        var selector = !string.IsNullOrWhiteSpace(_liveProximitySelector)
            ? _liveProximitySelector
            : ResolveHzdbSelector();
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var liveStatus = string.Equals(_liveProximitySelector, selector, StringComparison.OrdinalIgnoreCase)
            ? _liveProximityStatus
            : null;
        if (liveStatus?.Available == true)
        {
            return !liveStatus.HoldActive;
        }

        var tracked = _appSessionState.GetTrackedProximity(selector);
        return tracked.Known ? tracked.ExpectedEnabled : null;
    }

    private StudyTwinCommandConfirmation CaptureCommandConfirmation(string actionId)
    {
        var candidates = new List<StudyTwinCommandConfirmation>(2);

        if (string.Equals(actionId, _study.Controls.RecenterCommandActionId, StringComparison.OrdinalIgnoreCase))
        {
            var specific = CaptureCommandConfirmation(
                "study.recenter.last_command_sequence",
                "study.recenter.last_command_at_utc",
                "study.recenter.last_command_label",
                "study.recenter.last_command_source");
            if (HasConfirmationSignal(specific))
            {
                candidates.Add(specific);
            }
        }
        else if (MatchesParticleActionId(actionId))
        {
            var specific = CaptureCommandConfirmation(
                "study.particles.last_command_sequence",
                "study.particles.last_command_at_utc",
                "study.particles.last_command_label",
                "study.particles.last_command_source");
            if (HasConfirmationSignal(specific))
            {
                candidates.Add(specific);
            }
        }

        var generic = CaptureLatestActionConfirmation();
        if (HasConfirmationSignal(generic))
        {
            candidates.Add(generic);
        }

        if (candidates.Count == 0)
        {
            return default;
        }

        return candidates
            .OrderByDescending(candidate => ConfirmationMatchesAction(candidate, actionId))
            .ThenByDescending(candidate => candidate.Sequence ?? int.MinValue)
            .ThenByDescending(candidate => candidate.Timestamp ?? DateTimeOffset.MinValue)
            .First();
    }

    private StudyTwinCommandConfirmation CaptureCommandConfirmation(
        string sequenceKey,
        string timestampKey,
        string labelKey,
        string sourceKey,
        string? actionId = null)
    {
        var timestampRaw = GetFirstValue(timestampKey);
        return new StudyTwinCommandConfirmation(
            ParseInt(GetFirstValue(sequenceKey)),
            timestampRaw,
            ParseTimestamp(timestampRaw),
            GetFirstValue(labelKey),
            GetFirstValue(sourceKey),
            actionId);
    }

    private StudyTwinCommandConfirmation CaptureLatestActionConfirmation()
        => CaptureCommandConfirmation(
            "study.command.last_action_sequence",
            "study.command.last_action_at_utc",
            "study.command.last_action_label",
            "study.command.last_action_source",
            GetFirstValue("study.command.last_action_id"));

    private bool MatchesParticleActionId(string actionId)
        => string.Equals(actionId, _study.Controls.ParticleVisibleOnActionId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionId, _study.Controls.ParticleVisibleOffActionId, StringComparison.OrdinalIgnoreCase);

    private static string BuildCommandTrackingDetail(
        StudyTwinCommandRequest? request,
        StudyTwinCommandConfirmation confirmation,
        string fallbackLabel)
    {
        if (request is not null)
        {
            if (IsCommandConfirmed(request, confirmation))
            {
                return $"Companion sent {request.Label} {FormatCommandSequence(request.Sequence)} at {request.SentAtUtc:HH:mm:ss}. Headset confirmed {FormatCommandLabel(confirmation, fallbackLabel)} {FormatCommandSequence(confirmation.Sequence)} via {FormatCommandSource(confirmation.Source)} at {FormatCommandTime(confirmation.Timestamp)}.";
            }

            if (HasConfirmationSignal(confirmation))
            {
                return $"Companion sent {request.Label} {FormatCommandSequence(request.Sequence)} at {request.SentAtUtc:HH:mm:ss}. Headset last reported {FormatCommandLabel(confirmation, fallbackLabel)} {FormatCommandSequence(confirmation.Sequence)} via {FormatCommandSource(confirmation.Source)} at {FormatCommandTime(confirmation.Timestamp)}, which does not yet match that command.";
            }

            return $"Companion sent {request.Label} {FormatCommandSequence(request.Sequence)} at {request.SentAtUtc:HH:mm:ss}. Waiting for headset confirmation.";
        }

        if (HasConfirmationSignal(confirmation))
        {
            return $"Headset last confirmed {FormatCommandLabel(confirmation, fallbackLabel)} {FormatCommandSequence(confirmation.Sequence)} via {FormatCommandSource(confirmation.Source)} at {FormatCommandTime(confirmation.Timestamp)}.";
        }

        return "No headset command confirmation reported yet.";
    }

    private static string BuildParticleRuntimeDetail(
        bool? requestedVisible,
        bool? actualVisible,
        bool? renderOutputEnabled,
        bool? suppressedByOperator,
        bool? suppressedByHud)
    {
        return
            $"Requested visible {FormatVisibilityState(requestedVisible)}. " +
            $"Current visible {FormatVisibilityState(actualVisible)}. " +
            $"Render output {FormatEnabledState(renderOutputEnabled)}. " +
            $"Operator suppression {FormatEnabledState(suppressedByOperator)}. " +
            $"HUD suppression {FormatEnabledState(suppressedByHud)}.";
    }

    private RecenterEffectObservation CaptureRecenterEffect(StudyTwinCommandRequest? request, double? currentDistance)
    {
        if (request is null)
        {
            return default;
        }

        var currentAnchorTimestampRaw = GetFirstValue("study.recenter.last_anchor_recorded_at_utc");
        var currentAnchorTimestamp = ParseTimestamp(currentAnchorTimestampRaw);
        var anchorUpdated = !string.IsNullOrWhiteSpace(currentAnchorTimestampRaw) &&
            !string.Equals(currentAnchorTimestampRaw, request.PreviousRecenterAnchorTimestampRaw, StringComparison.Ordinal);

        var distanceImproved = request.PreviousRecenterDistance.HasValue &&
            currentDistance.HasValue &&
            currentDistance.Value + 0.05d < request.PreviousRecenterDistance.Value;

        return new RecenterEffectObservation(
            Observed: anchorUpdated || distanceImproved,
            AnchorUpdated: anchorUpdated,
            AnchorTimestampRaw: currentAnchorTimestampRaw,
            AnchorTimestamp: currentAnchorTimestamp,
            PreviousDistance: request.PreviousRecenterDistance,
            CurrentDistance: currentDistance,
            DistanceImproved: distanceImproved);
    }

    private static string BuildRecenterEffectDetail(RecenterEffectObservation observation)
    {
        if (!observation.Observed)
        {
            return "No recenter effect has been observed in the live state yet.";
        }

        var parts = new List<string>(2);
        if (observation.AnchorUpdated)
        {
            parts.Add($"Observed recenter anchor update at {FormatCommandTime(observation.AnchorTimestamp)}.");
        }

        if (observation.DistanceImproved && observation.PreviousDistance.HasValue && observation.CurrentDistance.HasValue)
        {
            parts.Add(
                $"Observed drift change from {observation.PreviousDistance.Value:0.000} to {observation.CurrentDistance.Value:0.000} units.");
        }

        return parts.Count == 0
            ? "A recenter effect was inferred from the live state."
            : string.Join(" ", parts);
    }

    private static ParticleVisibilityEffectObservation CaptureParticleVisibilityEffect(
        StudyTwinCommandRequest? request,
        bool? currentVisible)
    {
        if (request is null || !request.RequestedVisible.HasValue)
        {
            return default;
        }

        var matchedRequestedState = currentVisible.HasValue && currentVisible.Value == request.RequestedVisible.Value;
        var visibilityChanged = request.PreviousObservedVisible.HasValue &&
            currentVisible.HasValue &&
            request.PreviousObservedVisible.Value != currentVisible.Value;

        return new ParticleVisibilityEffectObservation(
            Observed: matchedRequestedState && visibilityChanged,
            PreviousVisible: request.PreviousObservedVisible,
            CurrentVisible: currentVisible,
            RequestedVisible: request.RequestedVisible);
    }

    private static string BuildParticleVisibilityEffectDetail(ParticleVisibilityEffectObservation observation)
    {
        if (!observation.Observed || !observation.PreviousVisible.HasValue || !observation.CurrentVisible.HasValue)
        {
            return "No particle-visibility effect has been observed in the live state yet.";
        }

        return
            $"Observed particle visibility change from {FormatVisibilityState(observation.PreviousVisible)} " +
            $"to {FormatVisibilityState(observation.CurrentVisible)} toward the requested {FormatVisibilityState(observation.RequestedVisible)} state.";
    }

    private static bool HashMatches(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string ShortHashLabel(string label, string? hash)
        => $"{label} {ShortHash(hash)}";

    private static string ShortHash(string? hash)
        => string.IsNullOrWhiteSpace(hash)
            ? "n/a"
            : hash.Length <= 12
                ? hash
                : hash[..12];

    private static OperationOutcomeKind NormalizePreSessionLevel(OperationOutcomeKind level)
        => level switch
        {
            OperationOutcomeKind.Success => OperationOutcomeKind.Success,
            OperationOutcomeKind.Failure => OperationOutcomeKind.Failure,
            _ => OperationOutcomeKind.Warning
        };

    private static void AddSelectorCandidate(ICollection<string> candidates, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return;
        }

        var normalized = selector.Trim();
        if (candidates.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidates.Add(normalized);
    }

    private static bool LooksLikeTcpSelector(string? selector)
        => !string.IsNullOrWhiteSpace(selector) && selector.Contains(':', StringComparison.Ordinal);

    private static async Task<string> ComputeFileSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static int? ParseInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static int? ParsePublishedCommandSequence(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        var match = CommandSequenceRegex.Match(detail);
        return match.Success
            ? ParseInt(match.Groups[1].Value)
            : null;
    }

    private static double? ParseDouble(string? value)
        => double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? ParseTimestamp(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static double? ParseUnitInterval(string? value)
    {
        var parsed = ParseDouble(value);
        return parsed is null ? null : Math.Clamp(parsed.Value, 0d, 1d);
    }

    private static bool? ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => null
        };
    }

    private static bool HasConfirmationSignal(StudyTwinCommandConfirmation confirmation)
        => confirmation.Sequence.HasValue
            || !string.IsNullOrWhiteSpace(confirmation.TimestampRaw)
            || !string.IsNullOrWhiteSpace(confirmation.Label)
            || !string.IsNullOrWhiteSpace(confirmation.Source);

    private static bool IsCommandPending(StudyTwinCommandRequest? request, StudyTwinCommandConfirmation confirmation)
        => request is not null && !IsCommandConfirmed(request, confirmation);

    private static bool IsCommandConfirmed(StudyTwinCommandRequest? request, StudyTwinCommandConfirmation confirmation)
    {
        if (request is null || !HasConfirmationSignal(confirmation) || !HasConfirmationMoved(request, confirmation))
        {
            return false;
        }

        if (!ConfirmationMatchesAction(confirmation, request.ActionId))
        {
            return false;
        }

        if (request.Sequence.HasValue)
        {
            return confirmation.Sequence == request.Sequence.Value;
        }

        return true;
    }

    private static bool HasConfirmationMoved(StudyTwinCommandRequest request, StudyTwinCommandConfirmation confirmation)
        => confirmation.Sequence != request.PreviousConfirmedSequence
           || !string.Equals(confirmation.TimestampRaw, request.PreviousConfirmedTimestampRaw, StringComparison.Ordinal);

    private static bool ConfirmationMatchesAction(StudyTwinCommandConfirmation confirmation, string actionId)
        => string.IsNullOrWhiteSpace(confirmation.ActionId)
            || string.IsNullOrWhiteSpace(actionId)
            || string.Equals(confirmation.ActionId, actionId, StringComparison.OrdinalIgnoreCase);

    private static string FormatCommandLabel(StudyTwinCommandConfirmation confirmation, string fallbackLabel)
        => !string.IsNullOrWhiteSpace(confirmation.Label)
            ? confirmation.Label
            : fallbackLabel;

    private static string FormatCommandSequence(int? sequence)
        => sequence.HasValue
            ? $"seq {sequence.Value.ToString(CultureInfo.InvariantCulture)}"
            : "without a reported sequence";

    private static string FormatCommandSource(string? source)
        => string.IsNullOrWhiteSpace(source)
            ? "an unknown source"
            : source.Trim();

    private static string FormatCommandTime(DateTimeOffset? timestamp)
        => timestamp.HasValue
            ? timestamp.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : "an unspecified time";

    private static string FormatVisibilityState(bool? visible)
        => visible == true
            ? "on"
            : visible == false
                ? "off"
                : "unknown";

    private static string FormatEnabledState(bool? enabled)
        => enabled == true
            ? "enabled"
            : enabled == false
                ? "disabled"
                : "unknown";

    private string BuildTwinCommandTransportDetail()
    {
        var parts = new List<string>(3);

        if (_twinBridge is LslTwinModeBridge lslBridge)
        {
            var renderedSequence = lslBridge.LastPublishedCommandSequence > 0
                ? lslBridge.LastPublishedCommandSequence.ToString(CultureInfo.InvariantCulture)
                : "n/a";
            parts.Add(
                $"Companion command outlet {(lslBridge.IsCommandOutletOpen ? "publishing" : "idle")}: " +
                $"sent {lslBridge.PublishedCommandCount.ToString(CultureInfo.InvariantCulture)}, last seq {renderedSequence}.");

            if (!string.IsNullOrWhiteSpace(lslBridge.LastCommittedSnapshotRevision))
            {
                parts.Add(
                    $"Latest quest snapshot rev {lslBridge.LastCommittedSnapshotRevision} " +
                    $"({lslBridge.LastCommittedSnapshotEntryCount.ToString(CultureInfo.InvariantCulture)} entries).");
            }
        }

        var latestAction = CaptureLatestActionConfirmation();
        if (HasConfirmationSignal(latestAction))
        {
            parts.Add(
                $"Headset last executed {FormatCommandLabel(latestAction, "command")} " +
                $"{FormatCommandSequence(latestAction.Sequence)} via {FormatCommandSource(latestAction.Source)} at {FormatCommandTime(latestAction.Timestamp)}.");
        }
        else
        {
            parts.Add("Headset has not reported any executed twin command yet.");
        }

        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private void AppendLog(OperatorLogLevel level, string message, string detail)
    {
        Logs.Insert(0, new OperatorLogEntry(DateTimeOffset.Now, level, message, detail));
        while (Logs.Count > 50)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private readonly record struct ProfileConditionSelectionResult(
        bool Success,
        string Label,
        string? Error);

    private static OperatorLogLevel MapLevel(OperationOutcomeKind kind)
        => kind switch
        {
            OperationOutcomeKind.Warning => OperatorLogLevel.Warning,
            OperationOutcomeKind.Failure => OperatorLogLevel.Failure,
            _ => OperatorLogLevel.Info
        };

    private Task DispatchAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action).Task;
    }

    private Task<T> DispatchAsync<T>(Func<T> action)
    {
        if (_dispatcher.CheckAccess())
        {
            return Task.FromResult(action());
        }

        return _dispatcher.InvokeAsync(action).Task;
    }
}
