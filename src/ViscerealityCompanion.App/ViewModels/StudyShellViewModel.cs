using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ViscerealityCompanion.App;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App.ViewModels;

public sealed class StudyShellViewModel : ObservableObject, IDisposable
{
    private const int ProximityDisableDurationMs = 8 * 60 * 60 * 1000;
    private const string TestSenderHeartbeatMode = "3";
    private const string TestSenderCoherenceMode = "2";
    private const string QuestSensorLockActivity = "SensorLockActivity";
    private const string QuestScreenshotCaptureMethod = "metacam";
    private static readonly TimeSpan TwinStateIdleThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BenchRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DeviceSnapshotRefreshInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProximityReadbackRefreshInterval = TimeSpan.FromSeconds(3);
    private static readonly Regex CommandSequenceRegex = new(@"\bseq=(\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly StudyValueSection[] SectionCatalog =
    [
        new("lsl", "LSL Routing", "Track the stream target and current LSL input connectivity."),
        new("controller", "Controller Breathing", "Follow controller breathing state, calibration, and live control value."),
        new("heartbeat", "Heartbeat", "Inspect heartbeat route selection and the latest incoming heartbeat value."),
        new("coherence", "Coherence", "Inspect coherence routing and the current coherence value."),
        new("performance", "Performance", "Track current fps, frame time, and the runtime target."),
        new("controls", "Recenter + Particles", "Study-specific controls and the runtime telemetry that backs them."),
        new("all", "All Pinned Keys", "Every live key this study shell is currently watching.")
    ];

    private readonly StudyShellDefinition _study;
    private AppSessionState _appSessionState;
    private StudyShellSessionState _studySessionState;
    private readonly IQuestControlService _questService;
    private readonly IHzdbService _hzdbService = HzdbServiceFactory.CreateDefault();
    private readonly ITwinModeBridge _twinBridge = TwinModeBridgeFactory.CreateShared();
    private readonly ITestLslSignalService _testLslSignalService = TestLslSignalServiceFactory.CreateDefault();
    private readonly DispatcherTimer? _twinRefreshTimer;
    private readonly DispatcherTimer? _benchRefreshTimer;
    private readonly DispatcherTimer? _deviceSnapshotRefreshTimer;
    private Window? _twinEventsWindow;
    private bool _initialized;
    private bool _twinRefreshPending;
    private bool _proximityRefreshPending;
    private bool _deviceSnapshotRefreshPending;
    private string _activeFocusSectionId = string.Empty;
    private IReadOnlyDictionary<string, string> _reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private HeadsetAppStatus? _headsetStatus;
    private InstalledAppStatus? _installedAppStatus;
    private DeviceProfileStatus? _deviceProfileStatus;
    private QuestProximityStatus? _liveProximityStatus;
    private string? _liveProximitySelector;
    private DateTimeOffset? _lastProximityRefreshAtUtc;
    private DateTimeOffset? _lastDeviceSnapshotAtUtc;
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
    private string _proximityActionLabel = "Disable for 8h";
    private string _benchToolsSummary = "Bench tools need attention before bench checks.";
    private string _questScreenshotSummary = "No Quest screenshot captured yet.";
    private string _questScreenshotDetail = "Capture a Quest screenshot after kiosk launch or exit to confirm what is actually visible on the headset.";
    private string _questScreenshotPath = string.Empty;
    private BitmapImage? _questScreenshotPreview;
    private string _testLslSenderSummary = "Windows TEST sender off.";
    private string _testLslSenderDetail = "Start the Windows TEST sender only for bench checks. It publishes smoothed HRV biofeedback samples on an irregular heartbeat-timed profile; Sussex treats packet arrival as heartbeat timing and the payload as the routed coherence value.";
    private string _testLslSenderValueLabel = "Not running";
    private string _testLslSenderActionLabel = "Start TEST Sender";
    private string _lastTwinStateTimestampLabel = "No live app-state timestamp yet.";
    private string _lastActionLabel = "None";
    private string _lastActionDetail = "No study action has run yet.";
    private OperationOutcomeKind _lastActionLevel = OperationOutcomeKind.Preview;
    private string _lastConnectionActionLabel = string.Empty;
    private string _lastConnectionDetail = string.Empty;
    private OperationOutcomeKind _lastConnectionLevel = OperationOutcomeKind.Preview;
    private double _controllerValuePercent;
    private string _controllerValueLabel = "n/a";
    private double _controllerCalibrationPercent;
    private string _controllerCalibrationLabel = "Calibration n/a";
    private double _coherencePercent;
    private string _coherenceValueLabel = "n/a";
    private double _performancePercent;
    private string _performanceValueLabel = "n/a";
    private double _recenterDistancePercent;
    private string _recenterDistanceLabel = "n/a";
    private int _selectedPhaseTabIndex;
    private StudyTwinCommandRequest? _lastRecenterCommandRequest;
    private StudyTwinCommandRequest? _lastParticlesCommandRequest;
    private string? _testSenderRestoreHeartbeatMode;
    private string? _testSenderRestoreCoherenceMode;
    private StudyValueSection? _selectedLiveSection;
    private DateTimeOffset? _lastQuestScreenshotCapturedAtUtc;
    private string _questVisualConfirmationPendingReason = string.Empty;

    public StudyShellViewModel(StudyShellDefinition study)
    {
        _study = study;
        _appSessionState = AppSessionState.Load();
        _studySessionState = StudyShellSessionState.Load();
        _regularAdbSnapshotEnabled = _appSessionState.RegularAdbSnapshotEnabled;
        _questService = QuestControlServiceFactory.CreateDefault(_appSessionState.ActiveEndpoint);
        _endpointDraft = _appSessionState.ActiveEndpoint ?? string.Empty;
        _stagedApkPath = ResolveInitialApkPath();

        foreach (var section in SectionCatalog)
        {
            LiveSections.Add(section);
        }

        _selectedLiveSection = LiveSections.FirstOrDefault();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            _twinRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _twinRefreshTimer.Tick += OnTwinRefreshTimerTick;

            _benchRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = BenchRefreshInterval
            };
            _benchRefreshTimer.Tick += OnBenchRefreshTimerTick;

            _deviceSnapshotRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = DeviceSnapshotRefreshInterval
            };
            _deviceSnapshotRefreshTimer.Tick += OnDeviceSnapshotRefreshTimerTick;
        }

        if (_twinBridge is LslTwinModeBridge lslBridge)
        {
            lslBridge.StateChanged += OnTwinBridgeStateChanged;
        }

        _testLslSignalService.StateChanged += OnTestLslSignalServiceStateChanged;

        ProbeUsbCommand = new AsyncRelayCommand(ProbeUsbAsync);
        DiscoverWifiCommand = new AsyncRelayCommand(DiscoverWifiAsync);
        EnableWifiCommand = new AsyncRelayCommand(EnableWifiAsync);
        ConnectQuestCommand = new AsyncRelayCommand(ConnectQuestAsync);
        RefreshStatusCommand = new AsyncRelayCommand(RefreshStatusAsync);
        RefreshDeviceSnapshotCommand = new AsyncRelayCommand(RefreshDeviceSnapshotAsync);
        BrowseApkCommand = new AsyncRelayCommand(BrowseApkAsync);
        InstallStudyAppCommand = new AsyncRelayCommand(InstallStudyAppAsync);
        LaunchStudyAppCommand = new AsyncRelayCommand(LaunchStudyAppAsync);
        StopStudyAppCommand = new AsyncRelayCommand(StopStudyAppAsync);
        ToggleStudyRuntimeCommand = new AsyncRelayCommand(ToggleStudyRuntimeAsync);
        ApplyPinnedDeviceProfileCommand = new AsyncRelayCommand(ApplyPinnedDeviceProfileAsync);
        ToggleHeadsetPowerCommand = new AsyncRelayCommand(ToggleHeadsetPowerAsync);
        ToggleProximityCommand = new AsyncRelayCommand(ToggleProximityAsync);
        CaptureQuestScreenshotCommand = new AsyncRelayCommand(CaptureQuestScreenshotAsync);
        OpenLastQuestScreenshotCommand = new AsyncRelayCommand(OpenLastQuestScreenshotAsync);
        ToggleTestLslSenderCommand = new AsyncRelayCommand(ToggleTestLslSenderAsync);
        RecenterCommand = new AsyncRelayCommand(RecenterAsync);
        ParticlesOnCommand = new AsyncRelayCommand(ParticlesOnAsync);
        ParticlesOffCommand = new AsyncRelayCommand(ParticlesOffAsync);
        OpenTwinEventsWindowCommand = new AsyncRelayCommand(OpenTwinEventsWindowAsync);
        UpdateHeadsetSnapshotModeState();
        UpdateDeviceSnapshotTimerState();
    }

    public string StudyLabel => _study.Label;
    public string StudyId => _study.Id;
    public string StudyPartner => _study.Partner;
    public string StudyDescription => _study.Description;
    public string PinnedPackageId => _study.App.PackageId;
    public string PinnedBuildVersion => string.IsNullOrWhiteSpace(_study.App.VersionName) ? "n/a" : _study.App.VersionName;
    public string PinnedBuildHash => _study.App.Sha256;
    public string PinnedLaunchComponent => string.IsNullOrWhiteSpace(_study.App.LaunchComponent) ? "n/a" : _study.App.LaunchComponent;
    public string PinnedAppNotes => string.IsNullOrWhiteSpace(_study.App.Notes) ? "No extra study-build notes." : _study.App.Notes;
    public string PinnedDeviceProfileLabel => _study.DeviceProfile.Label;
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
                OnPropertyChanged(nameof(CanToggleHeadsetPower));
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

    public OperationOutcomeKind BenchToolsCardLevel
    {
        get => _benchToolsCardLevel;
        private set => SetProperty(ref _benchToolsCardLevel, value);
    }

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
        private set => SetProperty(ref _questScreenshotPath, value);
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

    public string HeadsetAwakeActionLabel
        => _headsetStatus is { IsAwake: true, IsInWakeLimbo: false } && !IsHeadsetWakeBlockedByLockScreen()
            ? "Sleep Headset"
            : "Wake Headset";

    public string StudyRuntimeActionLabel
        => IsStudyRuntimeForeground()
            ? (_study.App.LaunchInKioskMode ? "Exit Kiosk Runtime" : "Stop Study Runtime")
            : (_study.App.LaunchInKioskMode ? "Launch Kiosk Runtime" : "Launch Study Runtime");

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

    public bool CanToggleParticles
        => !string.IsNullOrWhiteSpace(_study.Controls.ParticleVisibleOnActionId)
            && !string.IsNullOrWhiteSpace(_study.Controls.ParticleVisibleOffActionId);

    public bool CanToggleProximity
        => _hzdbService.IsAvailable
            && !string.IsNullOrWhiteSpace(ResolveHeadsetActionSelector());

    public bool CanToggleHeadsetPower
        => !string.IsNullOrWhiteSpace(ResolveHeadsetActionSelector());

    public bool CanToggleTestLslSender
        => _testLslSignalService.IsRunning
            || _testLslSignalService.RuntimeState.Available;

    public bool CanCaptureQuestScreenshot
        => _hzdbService.IsAvailable
            && !string.IsNullOrWhiteSpace(ResolveHzdbSelector());

    public bool CanOpenLastQuestScreenshot
        => !string.IsNullOrWhiteSpace(QuestScreenshotPath)
            && File.Exists(QuestScreenshotPath);

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

    public bool TryGetObservedLslValue(out double value, out string sourceKey)
    {
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
    public IReadOnlyDictionary<string, string> ReportedTwinStateSnapshot => _reportedTwinState;

    public AsyncRelayCommand ProbeUsbCommand { get; }
    public AsyncRelayCommand DiscoverWifiCommand { get; }
    public AsyncRelayCommand EnableWifiCommand { get; }
    public AsyncRelayCommand ConnectQuestCommand { get; }
    public AsyncRelayCommand RefreshStatusCommand { get; }
    public AsyncRelayCommand RefreshDeviceSnapshotCommand { get; }
    public AsyncRelayCommand BrowseApkCommand { get; }
    public AsyncRelayCommand InstallStudyAppCommand { get; }
    public AsyncRelayCommand LaunchStudyAppCommand { get; }
    public AsyncRelayCommand StopStudyAppCommand { get; }
    public AsyncRelayCommand ToggleStudyRuntimeCommand { get; }
    public AsyncRelayCommand ApplyPinnedDeviceProfileCommand { get; }
    public AsyncRelayCommand ToggleHeadsetPowerCommand { get; }
    public AsyncRelayCommand ToggleProximityCommand { get; }
    public AsyncRelayCommand CaptureQuestScreenshotCommand { get; }
    public AsyncRelayCommand OpenLastQuestScreenshotCommand { get; }
    public AsyncRelayCommand ToggleTestLslSenderCommand { get; }
    public AsyncRelayCommand RecenterCommand { get; }
    public AsyncRelayCommand ParticlesOnCommand { get; }
    public AsyncRelayCommand ParticlesOffCommand { get; }
    public AsyncRelayCommand OpenTwinEventsWindowCommand { get; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        EnsureTwinBridgeMonitoringStarted();
        await DispatchAsync(RefreshBenchToolsStatus).ConfigureAwait(false);
        var autoConnected = await ConnectQuestCoreAsync(warnWhenMissingEndpoint: false).ConfigureAwait(false);
        if (!autoConnected)
        {
            await RefreshStatusAsync().ConfigureAwait(false);
        }

        if (ShouldDefaultToDuringSession())
        {
            await DispatchAsync(() => SelectedPhaseTabIndex = 1).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_twinBridge is LslTwinModeBridge lslBridge)
        {
            lslBridge.StateChanged -= OnTwinBridgeStateChanged;
        }

        _testLslSignalService.StateChanged -= OnTestLslSignalServiceStateChanged;

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

        CloseTwinEventsWindow();
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
        await RefreshDeviceSnapshotBundleAsync(forceProximity: true).ConfigureAwait(false);
    }

    public Task RefreshDeviceSnapshotAsync()
        => RefreshDeviceSnapshotBundleAsync(forceProximity: true);

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
        await RefreshStatusAsync().ConfigureAwait(false);
    }

    public async Task LaunchStudyAppAsync()
    {
        var outcome = await _questService
            .LaunchAppAsync(
                CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false)),
                kioskMode: _study.App.LaunchInKioskMode)
            .ConfigureAwait(false);
        if (_study.App.LaunchInKioskMode && outcome.Kind != OperationOutcomeKind.Failure)
        {
            await DispatchAsync(() =>
            {
                MarkQuestVisualConfirmationPending("Kiosk launch is shell-confirmed only. Capture a Quest screenshot to verify the visible scene.");
            }).ConfigureAwait(false);
            outcome = BuildKioskVisualVerificationOutcome(
                outcome,
                "Kiosk launch is shell-confirmed. Visual confirmation pending.");
        }

        await ApplyOutcomeAsync(
            _study.App.LaunchInKioskMode ? "Launch Sussex APK In Kiosk Mode" : "Launch Sussex APK",
            outcome).ConfigureAwait(false);
        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        await RefreshStatusAsync().ConfigureAwait(false);

        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            var runtimeForeground = await DispatchAsync(IsStudyRuntimeForeground).ConfigureAwait(false);
            if (runtimeForeground)
            {
                var performanceOutcome = await TryStabilizeStudyPerformancePolicyAsync().ConfigureAwait(false);
                if (performanceOutcome is not null)
                {
                    await DispatchAsync(() => AppendLog(MapLevel(performanceOutcome.Kind), performanceOutcome.Summary, performanceOutcome.Detail)).ConfigureAwait(false);
                    await RefreshDeviceProfileStatusAsync().ConfigureAwait(false);
                    await RefreshHeadsetStatusAsync().ConfigureAwait(false);
                }

                await DispatchAsync(() => SelectedPhaseTabIndex = 1).ConfigureAwait(false);
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
        var outcome = await _questService
            .StopAppAsync(
                CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false)),
                exitKioskMode: _study.App.LaunchInKioskMode)
            .ConfigureAwait(false);
        if (_study.App.LaunchInKioskMode && outcome.Kind != OperationOutcomeKind.Failure)
        {
            await DispatchAsync(() =>
            {
                MarkQuestVisualConfirmationPending("Kiosk exit was sent. Capture a Quest screenshot to verify that Meta Home is actually visible.");
            }).ConfigureAwait(false);
            outcome = BuildKioskVisualVerificationOutcome(
                outcome,
                "Kiosk exit was sent. Visual confirmation pending.");
        }

        await ApplyOutcomeAsync(
            _study.App.LaunchInKioskMode ? "Exit Sussex Kiosk Mode" : "Stop Sussex APK",
            outcome).ConfigureAwait(false);
        await RefreshStatusAsync().ConfigureAwait(false);
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

    private async Task ToggleHeadsetPowerAsync()
    {
        var action = _headsetStatus is { IsAwake: true, IsInWakeLimbo: false } && !IsHeadsetWakeBlockedByLockScreen()
            ? QuestUtilityAction.Sleep
            : QuestUtilityAction.Wake;
        var actionLabel = action == QuestUtilityAction.Sleep ? "Sleep Headset" : "Wake Headset";
        var outcome = await _questService.RunUtilityAsync(action).ConfigureAwait(false);

        if (action == QuestUtilityAction.Wake)
        {
            if (outcome.Kind != OperationOutcomeKind.Failure)
            {
                await Task.Delay(350).ConfigureAwait(false);
            }

            await RefreshStatusAsync().ConfigureAwait(false);
            await ApplyOutcomeAsync(actionLabel, BuildWakeActionOutcome(outcome)).ConfigureAwait(false);
            return;
        }

        await ApplyOutcomeAsync(actionLabel, outcome).ConfigureAwait(false);
        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            await Task.Delay(350).ConfigureAwait(false);
        }

        await RefreshStatusAsync().ConfigureAwait(false);
    }

    private async Task ToggleProximityAsync()
    {
        await TryWakeHeadsetBeforeStudyActionAsync("Toggle proximity hold").ConfigureAwait(false);
        var selector = await DispatchAsync(ResolveHzdbSelector).ConfigureAwait(false);
        if (!_hzdbService.IsAvailable)
        {
            await ApplyOutcomeAsync(
                "Toggle proximity",
                new OperationOutcome(
                    OperationOutcomeKind.Preview,
                    "hzdb not available.",
                    "Install or expose @meta-quest/hzdb before using the experiment-shell proximity hold.")).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            await DispatchAsync(() => AppendLog(
                OperatorLogLevel.Warning,
                "Proximity hold blocked.",
                "Probe USB or connect the Quest first so the shell has a selector for hzdb.")).ConfigureAwait(false);
            return;
        }

        var liveStatus = await RefreshProximityStatusAsync(force: true).ConfigureAwait(false);
        var tracked = await DispatchAsync(() => _appSessionState.GetTrackedProximity(selector)).ConfigureAwait(false);
        var disableHoldIsActive = liveStatus?.Available == true
            ? liveStatus.HoldActive
            : tracked.Known && !tracked.ExpectedEnabled && !tracked.DisableWindowExpired;
        var enable = disableHoldIsActive;
        var actionLabel = enable ? "Enable Proximity" : "Disable Proximity For 8h";
        var disableUntilUtc = enable ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddMilliseconds(ProximityDisableDurationMs);
        var outcome = await _hzdbService
            .SetProximityAsync(selector, enabled: enable, durationMs: enable ? null : ProximityDisableDurationMs)
            .ConfigureAwait(false);

        if (outcome.Kind == OperationOutcomeKind.Success)
        {
            await DispatchAsync(() =>
            {
                _appSessionState = _appSessionState.WithTrackedProximity(selector, enable, disableUntilUtc);
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
        await TryWakeHeadsetBeforeStudyActionAsync("Capture Quest Screenshot").ConfigureAwait(false);
        var selector = await DispatchAsync(ResolveHzdbSelector).ConfigureAwait(false);
        if (!_hzdbService.IsAvailable)
        {
            await ApplyOutcomeAsync(
                "Capture Quest Screenshot",
                new OperationOutcome(
                    OperationOutcomeKind.Preview,
                    "hzdb not available.",
                    "Install or expose @meta-quest/hzdb before using Quest screenshot capture.")).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            await ApplyOutcomeAsync(
                "Capture Quest Screenshot",
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Quest screenshot blocked.",
                    "Probe USB or connect the Quest first so the study shell has a selector for hzdb screenshot capture.")).ConfigureAwait(false);
            return;
        }

        var outputPath = BuildQuestScreenshotOutputPath();
        var outcome = await _hzdbService
            .CaptureScreenshotAsync(selector, outputPath, QuestScreenshotCaptureMethod)
            .ConfigureAwait(false);

        if (outcome.Kind == OperationOutcomeKind.Success && File.Exists(outputPath))
        {
            var capturedAtUtc = DateTimeOffset.UtcNow;
            var preview = LoadQuestScreenshotPreview(outputPath);
            await DispatchAsync(() =>
            {
                QuestScreenshotPath = outputPath;
                QuestScreenshotPreview = preview;
                _lastQuestScreenshotCapturedAtUtc = capturedAtUtc;
                ClearQuestVisualConfirmationPending();
                RefreshBenchToolsStatus();
                UpdateLiveRuntimeCard();
            }).ConfigureAwait(false);

            outcome = new OperationOutcome(
                OperationOutcomeKind.Success,
                "Quest screenshot captured.",
                $"Saved metacam Quest screenshot to {outputPath}. Review the screenshot preview to confirm what is actually visible on the headset.",
                Items: [outputPath]);
        }
        else if (outcome.Kind == OperationOutcomeKind.Success)
        {
            outcome = new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Quest screenshot capture did not produce a file.",
                $"hzdb reported success, but no screenshot was found at {outputPath}.",
                Items: [outputPath]);
        }

        await ApplyOutcomeAsync("Capture Quest Screenshot", outcome).ConfigureAwait(false);
    }

    private async Task OpenLastQuestScreenshotAsync()
    {
        var screenshotPath = await DispatchAsync(() => QuestScreenshotPath).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(screenshotPath) || !File.Exists(screenshotPath))
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
                FileName = screenshotPath,
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
                    Items: [screenshotPath])).ConfigureAwait(false);
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
        var sourceId = $"viscereality.companion.study-shell.test.{_study.Id}";

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
           _headsetStatus.IsAwake == true &&
           !IsStudyRuntimeForeground() &&
           !string.IsNullOrWhiteSpace(_headsetStatus.ForegroundComponent) &&
           _headsetStatus.ForegroundComponent.Contains(QuestSensorLockActivity, StringComparison.OrdinalIgnoreCase);

    private OperationOutcome BuildWakeActionOutcome(OperationOutcome outcome)
    {
        if (outcome.Kind == OperationOutcomeKind.Failure || !IsHeadsetWakeBlockedByLockScreen())
        {
            return outcome;
        }

        var detailParts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(outcome.Detail))
        {
            detailParts.Add(outcome.Detail);
        }

        detailParts.Add("Quest woke into SensorLockActivity instead of returning to the previous app.");
        detailParts.Add(_headsetStatus?.IsTargetRunning == true
            ? "Use Launch Study Runtime to bring Sussex back to the foreground."
            : "Use Launch Study Runtime if you want to start Sussex from this state.");

        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            "Headset wake ended in the Quest lock screen.",
            string.Join(" ", detailParts));
    }

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

    public Task RecenterAsync()
        => SendStudyTwinCommandAsync(_study.Controls.RecenterCommandActionId, "Recenter");

    public Task ParticlesOnAsync()
        => SendStudyTwinCommandAsync(_study.Controls.ParticleVisibleOnActionId, "Particles On");

    public Task ParticlesOffAsync()
        => SendStudyTwinCommandAsync(_study.Controls.ParticleVisibleOffActionId, "Particles Off");

    private async Task SendStudyTwinCommandAsync(string actionId, string label)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            await DispatchAsync(() => AppendLog(
                OperatorLogLevel.Warning,
                $"{label} unavailable.",
                $"The current public runtime does not expose a `{label}` twin command yet.")).ConfigureAwait(false);
            return;
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
                UpdateRecenterCard();
                UpdateParticlesCard();
                RefreshFocusRows(forceRebuild: true);
            }).ConfigureAwait(false);
        }
    }

    private async Task<OperationOutcome?> TryWakeHeadsetBeforeStudyActionAsync(string actionLabel)
    {
        var selector = await DispatchAsync(ResolveHeadsetActionSelector).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var wakeOutcome = await _questService.RunUtilityAsync(QuestUtilityAction.Wake).ConfigureAwait(false);
        if (wakeOutcome.Kind is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
        {
            await DispatchAsync(() => AppendLog(
                OperatorLogLevel.Warning,
                $"Wake before {actionLabel} needs attention.",
                wakeOutcome.Detail)).ConfigureAwait(false);
        }

        if (wakeOutcome.Kind != OperationOutcomeKind.Failure)
        {
            await Task.Delay(250).ConfigureAwait(false);
            await RefreshHeadsetStatusAsync().ConfigureAwait(false);
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
                LocalApkDetail = $"Path: {stagedPath}. SHA256 {hash}. Expected {_study.App.Sha256}.";
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

    private async Task RefreshDeviceSnapshotBundleAsync(bool forceProximity)
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
            return;
        }

        try
        {
            await RefreshHeadsetStatusAsync().ConfigureAwait(false);
            await RefreshInstalledAppStatusAsync().ConfigureAwait(false);
            await RefreshDeviceProfileStatusAsync().ConfigureAwait(false);
            await RefreshProximityStatusAsync(force: forceProximity).ConfigureAwait(false);
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
            await DispatchAsync(() => _deviceSnapshotRefreshPending = false).ConfigureAwait(false);
        }
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
            ? "Regular ADB readouts are on. Useful for debugging, but they add headset-query overhead during a live run."
            : "Regular ADB readouts are off. Use Refresh Snapshot when you want a fresh device state.";
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
        _ = RefreshDeviceSnapshotBundleAsync(forceProximity: true);
    }

    private async Task RefreshInstalledAppStatusAsync()
    {
        var target = CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false));
        _installedAppStatus = await _questService.QueryInstalledAppAsync(target).ConfigureAwait(false);

        await DispatchAsync(() =>
        {
            var snapshotSuffix = _headsetStatus?.IsConnected == true
                ? BuildSnapshotInlineSuffix(_lastDeviceSnapshotAtUtc)
                : string.Empty;

            if (_installedAppStatus is null)
            {
                InstalledApkLevel = OperationOutcomeKind.Preview;
                InstalledApkSummary = "Installed build has not been checked yet.";
                InstalledApkDetail = "Refresh the study status after connecting to the headset.";
            }
            else
            {
                InstalledApkLevel = !_installedAppStatus.IsInstalled
                    ? OperationOutcomeKind.Warning
                    : string.IsNullOrWhiteSpace(_installedAppStatus.InstalledSha256)
                        ? OperationOutcomeKind.Warning
                        : HashMatches(_installedAppStatus.InstalledSha256, _study.App.Sha256)
                            ? OperationOutcomeKind.Success
                            : OperationOutcomeKind.Warning;
                InstalledApkSummary = _installedAppStatus.Summary + snapshotSuffix;
                InstalledApkDetail = _installedAppStatus.Detail;
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

    private async Task RefreshHeadsetStatusAsync()
    {
        _headsetStatus = await _questService.QueryHeadsetStatusAsync(CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false)), remoteOnlyControlEnabled: true).ConfigureAwait(false);

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
            HeadsetPerformanceLabel = $"CPU {(_headsetStatus.CpuLevel?.ToString() ?? "n/a")} / GPU {(_headsetStatus.GpuLevel?.ToString() ?? "n/a")}{snapshotSuffix}";
            HeadsetForegroundLabel = string.IsNullOrWhiteSpace(_headsetStatus.ForegroundPackageId)
                ? $"Active app n/a{snapshotSuffix}"
                : $"{_headsetStatus.ForegroundPackageId}{snapshotSuffix}";
            UpdateConnectionCardState();
            UpdateHeadsetSnapshotModeState();
            RefreshBenchToolsStatus();
            OnPropertyChanged(nameof(HeadsetAwakeActionLabel));
            OnPropertyChanged(nameof(StudyRuntimeActionLabel));
            OnPropertyChanged(nameof(CanToggleHeadsetPower));
        }).ConfigureAwait(false);
    }

    private void UpdatePinnedBuildStatus()
    {
        if (_installedAppStatus?.IsInstalled == true)
        {
            if (!string.IsNullOrWhiteSpace(_installedAppStatus.InstalledSha256) &&
                HashMatches(_installedAppStatus.InstalledSha256, _study.App.Sha256))
            {
                PinnedBuildLevel = OperationOutcomeKind.Success;
                PinnedBuildSummary = "Sussex APK is installed on the headset.";
            }
            else if (string.IsNullOrWhiteSpace(_installedAppStatus.InstalledSha256))
            {
                PinnedBuildLevel = OperationOutcomeKind.Warning;
                PinnedBuildSummary = "Sussex runtime is installed, but the headset APK hash could not be verified.";
            }
            else
            {
                PinnedBuildLevel = OperationOutcomeKind.Warning;
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

        var details = new List<string>
        {
            $"Study package: {_study.App.PackageId}",
            $"Study version: {_study.App.VersionName}",
            $"Study SHA256: {_study.App.Sha256}"
        };

        if (!string.IsNullOrWhiteSpace(StagedApkPath))
        {
            details.Add(LocalApkSummary);
        }

        if (_installedAppStatus is not null)
        {
            details.Add(InstalledApkSummary);
        }

        PinnedBuildDetail = string.Join(" ", details);
        UpdatePinnedBuildCardState();
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
                Label: property.Key,
                Key: property.Key,
                Value: string.IsNullOrWhiteSpace(property.ReportedValue) ? "Not reported" : property.ReportedValue,
                Expected: property.ExpectedValue,
                Detail: property.Matches ? "Pinned Quest property matches." : "Pinned Quest property differs from the current headset value.",
                Level: property.Matches ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning));
        }
    }

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

        WifiNetworkMatchLevel = _headsetStatus.WifiSsidMatchesHost switch
        {
            true => OperationOutcomeKind.Success,
            false => OperationOutcomeKind.Failure,
            _ => OperationOutcomeKind.Warning
        };
        WifiNetworkMatchSummary = _headsetStatus.WifiSsidMatchesHost switch
        {
            true => "Wi-Fi names match.",
            false => "Wi-Fi names do not match.",
            _ => "Wi-Fi match unknown."
        };

        if (isWifiAdbActive)
        {
            ConnectionCardLevel = _headsetStatus.WifiSsidMatchesHost == false
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success;
            ConnectionTransportLevel = _headsetStatus.WifiSsidMatchesHost == false
                ? OperationOutcomeKind.Failure
                : OperationOutcomeKind.Success;
            ConnectionTransportSummary = _headsetStatus.WifiSsidMatchesHost == false
                ? "ADB over Wi-Fi: on, but network names do not match."
                : "ADB over Wi-Fi: on.";
            ConnectionTransportDetail = _headsetStatus.WifiSsidMatchesHost switch
            {
                true => $"Remote control is using {selector}, and the headset Wi-Fi matches this PC.",
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

        if (string.Equals(status.LastBroadcastAction, "prox_close", StringComparison.OrdinalIgnoreCase))
        {
            return "The last control broadcast is a normal prox_close event with no timed hold, so this is not a forced bypass.";
        }

        if (string.Equals(status.LastBroadcastAction, "automation_disable", StringComparison.OrdinalIgnoreCase))
        {
            return "The last control broadcast cleared automation control, so the physical wear sensor is in charge again.";
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

        if (tracked.Known && !tracked.ExpectedEnabled && !tracked.DisableWindowExpired && tracked.DisableUntilUtc.HasValue)
        {
            return $"Companion still expects a proximity bypass until {tracked.DisableUntilUtc.Value.ToLocalTime():HH:mm}, but headset sleep/wake is tracked separately from that hold.";
        }

        return "Headset sleep/wake is tracked separately from the proximity setting.";
    }

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

        if (_testLslSignalService.IsRunning)
        {
            BenchToolsCardLevel = OperationOutcomeKind.Warning;
            BenchToolsSummary = ProximityLevel == OperationOutcomeKind.Success
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

        if (ProximityLevel == OperationOutcomeKind.Success && _testLslSignalService.RuntimeState.Available)
        {
            BenchToolsCardLevel = OperationOutcomeKind.Success;
            BenchToolsSummary = "Bench tools are ready.";
            return;
        }

        BenchToolsCardLevel = OperationOutcomeKind.Warning;
        BenchToolsSummary = "Bench tools need attention.";
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
                HeadsetAwakeDetail = $"{powerEvidence} Quest woke into SensorLockActivity instead of returning to the previous app. Use Launch Study Runtime to resume Sussex from this state. {proximityContext}".Trim();
            }
            else if (_headsetStatus.IsInWakeLimbo)
            {
                HeadsetAwakeLevel = OperationOutcomeKind.Warning;
                HeadsetAwakeSummary = "Meta shell wake blocker active.";
                HeadsetAwakeDetail = $"{powerEvidence} Quest reports the display awake, but a Meta visual blocker is still active, so the visible headset scene can still be black or Guardian-blocked even though Android says it is awake. Use Capture Quest Screenshot to confirm the actual visible state before deciding whether to launch or exit kiosk mode. {proximityContext}".Trim();
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
                HeadsetAwakeSummary = "Headset asleep.";
                HeadsetAwakeDetail = $"{powerEvidence} {proximityContext} Any headset action button will wake the device before its command is sent.".Trim();
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

        OnPropertyChanged(nameof(HeadsetAwakeActionLabel));
        OnPropertyChanged(nameof(StudyRuntimeActionLabel));
        OnPropertyChanged(nameof(CanToggleHeadsetPower));
    }

    private async Task ApplyOutcomeAsync(string actionLabel, OperationOutcome outcome)
    {
        await DispatchAsync(() =>
        {
            LastActionLabel = actionLabel;
            LastActionDetail = outcome.Detail;
            LastActionLevel = outcome.Kind;
            AppendLog(MapLevel(outcome.Kind), outcome.Summary, outcome.Detail);
        }).ConfigureAwait(false);
    }

    private void RefreshBenchToolsStatus()
    {
        UpdateProximityCard();
        UpdateQuestScreenshotCard();
        UpdateTestLslSenderCard();
        UpdateBenchToolsCardState();
        UpdateBenchRefreshTimerState();
        OnPropertyChanged(nameof(CanToggleHeadsetPower));
        OnPropertyChanged(nameof(HeadsetAwakeActionLabel));
        OnPropertyChanged(nameof(StudyRuntimeActionLabel));
        OnPropertyChanged(nameof(CanToggleProximity));
        OnPropertyChanged(nameof(CanCaptureQuestScreenshot));
        OnPropertyChanged(nameof(CanOpenLastQuestScreenshot));
        OnPropertyChanged(nameof(CanToggleTestLslSender));
    }

    private void RefreshLiveTwinState()
    {
        if (_twinBridge is not LslTwinModeBridge lslBridge)
        {
            _reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            return;
        }

        _reportedTwinState = lslBridge.ReportedSettings
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

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
        RefreshFocusRows();
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
        var testSenderActive = _testLslSignalService.IsRunning;
        var lastBenchSendAt = _testLslSignalService.LastSentAtUtc;
        var lastBenchSendLabel = lastBenchSendAt.HasValue
            ? $"{_testLslSignalService.LastValue:0.000} at {lastBenchSendAt.Value.ToLocalTime():HH:mm:ss}"
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
            : connectedFlag.HasValue || connectedCount.HasValue || connectingCount.HasValue || totalCount.HasValue
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Preview;
        LslSummary = hasConnectedInput
            ? string.IsNullOrWhiteSpace(connectedName)
                ? $"LSL input live: {connectedCount?.ToString() ?? "1"} connected."
                : $"LSL input live: {connectedName}."
            : connectingCount.GetValueOrDefault() > 0
                ? $"LSL input connecting: {connectingCount} stream(s) still resolving."
                : "No live LSL input reported yet.";
        if (hasInputValue)
        {
            LslSummary += $" Inlet coherence {inputValue:0.000}.";
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
                : "The study runtime has not confirmed an active inlet connection yet.";
        LslDetail = $"{routeNote} {comparisonNote} {testSenderDetail}".Trim();
    }

    private void UpdateControllerCard()
    {
        var volume = ParseUnitInterval(GetFirstValue(_study.Monitoring.ControllerValueKeys));
        var state = GetFirstValue("tracker.breathing.controller.state");
        var active = ParseBool(GetFirstValue("tracker.breathing.controller.active"));
        var calibrated = ParseBool(GetFirstValue("tracker.breathing.controller.calibrated"));
        var validating = ParseBool(GetFirstValue("tracker.breathing.controller.validating"));
        var validationProgress = ParseUnitInterval(GetFirstValue("tracker.breathing.controller.validation_progress01"));
        var failureReason = GetFirstValue("tracker.breathing.controller.failure_reason");
        var routingLabel = GetFirstValue("routing.breathing.label");
        var routingMode = GetFirstValue("routing.breathing.mode");

        ControllerValuePercent = volume.HasValue ? volume.Value * 100d : 0d;
        ControllerValueLabel = volume.HasValue ? $"{volume.Value:0.000}" : "n/a";
        ControllerCalibrationPercent = calibrated == true ? 100d : validationProgress.HasValue ? validationProgress.Value * 100d : 0d;
        ControllerCalibrationLabel = calibrated == true
            ? "Calibrated"
            : validating == true && validationProgress.HasValue
                ? $"Validating {validationProgress.Value:P0}"
                : "Calibration n/a";

        if (_reportedTwinState.Count == 0)
        {
            ControllerLevel = OperationOutcomeKind.Preview;
            ControllerSummary = "Waiting for controller breathing state.";
            ControllerDetail = "The Sussex study expects controller-based breathing, but no live twin-state values have arrived yet.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(failureReason))
        {
            ControllerLevel = OperationOutcomeKind.Failure;
            ControllerSummary = "Controller breathing reported a failure.";
            ControllerDetail = failureReason;
            return;
        }

        ControllerLevel = active == true
            ? calibrated == true
                ? OperationOutcomeKind.Success
                : OperationOutcomeKind.Warning
            : state is null && routingMode is null
                ? OperationOutcomeKind.Preview
                : OperationOutcomeKind.Warning;
        ControllerSummary = active == true
            ? calibrated == true
                ? "Controller breathing active and calibrated."
                : "Controller breathing active but not calibrated yet."
            : "Controller breathing is not active yet.";
        ControllerDetail = $"Route {(string.IsNullOrWhiteSpace(routingLabel) ? "n/a" : routingLabel)} (mode {routingMode ?? "n/a"}). State {(string.IsNullOrWhiteSpace(state) ? "n/a" : state)}. Controller value {ControllerValueLabel}.";
    }

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
        var value = ParseUnitInterval(GetFirstValue(_study.Monitoring.CoherenceValueKeys));
        var rawValue = GetFirstValue(_study.Monitoring.CoherenceValueKeys);
        var routeLabel = GetFirstValue("routing.coherence.label");
        var routeMode = GetFirstValue("routing.coherence.mode");
        var usesHeartbeat = GetFirstValue("routing.coherence.uses_heartbeat_source");
        var lslConnected = ParseBool(GetFirstValue("study.lsl.connected")) == true
            || ParseInt(GetFirstValue("connection.lsl.connected_count")).GetValueOrDefault() > 0
            || !string.IsNullOrWhiteSpace(GetFirstValue("study.lsl.connected_name"));
        var routeMatchesExpected =
            string.Equals(routeLabel, _study.Monitoring.ExpectedCoherenceLabel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(routeMode, TestSenderCoherenceMode, StringComparison.Ordinal);

        CoherencePercent = value.HasValue ? value.Value * 100d : 0d;
        CoherenceValueLabel = value.HasValue ? $"{value.Value:0.000}" : rawValue ?? "n/a";
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

        CoherenceLevel = value.HasValue || !string.IsNullOrWhiteSpace(rawValue)
            ? routeMatchesExpected ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning
            : OperationOutcomeKind.Warning;
        CoherenceSummary = value.HasValue || !string.IsNullOrWhiteSpace(rawValue)
            ? $"Coherence live value: {CoherenceValueLabel}."
            : routeMatchesExpected
                ? "Coherence route is LSL Direct, but no live routed biofeedback value is reported yet."
                : "Coherence route visible, but no live coherence value yet.";
        CoherenceDetail = routeMatchesExpected
            ? _testLslSignalService.IsRunning
                ? "The runtime is reporting the expected direct-LSL coherence route. This field shows the current Quest-reported routed biofeedback value, not the latest local TEST packet, so it can briefly lag or return to baseline between beats."
                : "The runtime is reporting the expected direct-LSL coherence route."
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
        ProximityActionLabel = "Disable for 8h";
        ProximityEvidenceLabel = "Latest readback n/a.";
        var tracked = _appSessionState.GetTrackedProximity(selector);
        var liveStatus = string.Equals(_liveProximitySelector, selector, StringComparison.OrdinalIgnoreCase)
            ? _liveProximityStatus
            : null;

        UpdateHeadsetAwakeStatus(selector, tracked, liveStatus);

        if (!_hzdbService.IsAvailable)
        {
            ProximityLevel = OperationOutcomeKind.Preview;
            ProximitySummary = "Proximity hold unavailable.";
            ProximityDetail = "Install or expose @meta-quest/hzdb before using the experiment-shell proximity hold.";
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
            var mountedCloseWithoutHold =
                string.Equals(liveStatus.VirtualState, "CLOSE", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(liveStatus.HeadsetState, "HEADSET_MOUNTED", StringComparison.OrdinalIgnoreCase);
            var controlInterpretation = BuildProximityControlInterpretation(liveStatus);

            if (liveStatus.HoldActive)
            {
                ProximityLevel = OperationOutcomeKind.Success;
                ProximityActionLabel = "Enable Proximity";
                ProximitySummary = liveStatus.HoldUntilUtc.HasValue
                    ? $"Proximity bypass active on headset until {liveStatus.HoldUntilUtc.Value.ToLocalTime():HH:mm}."
                    : "Proximity bypass active on headset.";
                ProximityDetail =
                    $"{controlInterpretation} Normal wear-sensor sleep is bypassed until the hold is cleared or expires. " +
                    (tracked.Known && !tracked.ExpectedEnabled && tracked.DisableUntilUtc.HasValue
                        ? $"Companion last requested a hold until {tracked.DisableUntilUtc.Value.ToLocalTime():HH:mm}."
                        : "Quest readback is authoritative even if the hold was toggled outside the companion.");
                return;
            }

            ProximityLevel = OperationOutcomeKind.Success;
            ProximitySummary = "Normal proximity sensor behavior is active.";
            ProximityDetail =
                $"{controlInterpretation} " +
                (mountedCloseWithoutHold
                    ? "The current CLOSE readback reflects a normal mounted/on-face state, not a forced bypass. "
                    : string.Empty) +
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

        if (tracked.Known && !tracked.ExpectedEnabled && !tracked.DisableWindowExpired && tracked.DisableUntilUtc.HasValue)
        {
            var untilLocal = tracked.DisableUntilUtc.Value.ToLocalTime();
            if (liveStatus is not { Available: false })
            {
                ProximityLevel = OperationOutcomeKind.Warning;
                ProximitySummary = $"Proximity sensor expected disabled until {untilLocal:HH:mm}.";
                ProximityDetail =
                    $"Companion last sent an 8h disable for {selector} at {updatedLabel}. " +
                    "Waiting for a fresh Quest vrpowermanager readback.";
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
            QuestScreenshotDetail = "Install or expose @meta-quest/hzdb before using Quest screenshot capture.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ResolveHzdbSelector()))
        {
            QuestScreenshotLevel = OperationOutcomeKind.Preview;
            QuestScreenshotSummary = "Quest screenshot capture needs a headset selector.";
            QuestScreenshotDetail = "Probe USB or connect the Quest first so the study shell can request a metacam screenshot.";
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
            QuestScreenshotDetail = $"Latest metacam screenshot: {QuestScreenshotPath}";
            return;
        }

        QuestScreenshotLevel = OperationOutcomeKind.Preview;
        QuestScreenshotSummary = "No Quest screenshot captured yet.";
        QuestScreenshotDetail = "Capture a Quest screenshot after kiosk launch or exit to confirm what is actually visible on the headset.";
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

        return "Quest shell focus can still disagree with the visible scene on this HorizonOS build. Capture a Quest screenshot in Bench tools when launch or exit needs visual confirmation.";
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
            CreateStudyRow("Controller calibrated", ["tracker.breathing.controller.calibrated"], string.Empty, "Whether controller breathing is calibrated."),
            CreateStudyRow("Validation progress", ["tracker.breathing.controller.validation_progress01"], string.Empty, "Controller validation progress."),
            CreateStudyRow("Controller value", _study.Monitoring.ControllerValueKeys, string.Empty, "Latest breathing-control value."),
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
            CreateStudyRow("Coherence value", _study.Monitoring.CoherenceValueKeys, string.Empty, "Latest coherence value received by the runtime.")
        ];
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
            "Local twin-command outlet status, matching the Astral HUD transport counters on the sender side.",
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
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(() =>
        {
            RefreshBenchToolsStatus();
            UpdateLslCard();
        });
    }

    private void OnTwinBridgeStateChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(() =>
        {
            _twinRefreshPending = true;
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
        => new(
            Id: _study.Id,
            Label: _study.App.Label,
            PackageId: _study.App.PackageId,
            ApkFile: string.IsNullOrWhiteSpace(apkPath) ? _study.App.ApkPath : apkPath,
            LaunchComponent: _study.App.LaunchComponent,
            BrowserPackageId: "com.oculus.browser",
            Description: _study.Description,
            Tags: ["viscereality", "runtime", "lsl", "twin"],
            ApkSha256: _study.App.Sha256,
            CompatibilityStatus: ApkCompatibilityStatus.Compatible,
            CompatibilityProfile: _study.Label,
            CompatibilityNotes: _study.App.Notes);

    private DeviceProfile CreatePinnedDeviceProfile()
        => new(
            _study.DeviceProfile.Id,
            _study.DeviceProfile.Label,
            _study.DeviceProfile.Description,
            new Dictionary<string, string>(_study.DeviceProfile.Properties, StringComparer.OrdinalIgnoreCase));

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
        AddSelectorCandidate(candidates, _appSessionState.ActiveEndpoint);
        AddSelectorCandidate(candidates, endpointDraft);
        AddSelectorCandidate(candidates, _appSessionState.LastUsbSerial);

        return candidates;
    }

    private string ResolveHzdbSelector()
        => ResolveHeadsetActionSelector();

    private string BuildQuestScreenshotOutputPath()
    {
        var screenshotRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ViscerealityCompanion",
            "screenshots",
            _study.Id);
        Directory.CreateDirectory(screenshotRoot);
        return Path.Combine(screenshotRoot, $"quest_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.png");
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

    private static OperationOutcome BuildKioskVisualVerificationOutcome(OperationOutcome outcome, string summary)
    {
        if (outcome.Kind == OperationOutcomeKind.Failure)
        {
            return outcome;
        }

        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            summary,
            $"{outcome.Summary} {outcome.Detail} Capture a Quest screenshot in Bench tools to confirm what is actually visible on the headset."
                .Trim(),
            outcome.Endpoint,
            outcome.PackageId,
            outcome.Items);
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
        if (string.Equals(request.ActionId, _study.Controls.RecenterCommandActionId, StringComparison.OrdinalIgnoreCase))
        {
            _lastRecenterCommandRequest = request;
            return;
        }

        if (MatchesParticleActionId(request.ActionId))
        {
            _lastParticlesCommandRequest = request;
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

    private bool ShouldDefaultToDuringSession()
        => _reportedTwinState.Count > 0
           && string.Equals(_headsetStatus?.ForegroundPackageId, _study.App.PackageId, StringComparison.OrdinalIgnoreCase);

    private void AppendLog(OperatorLogLevel level, string message, string detail)
    {
        Logs.Insert(0, new OperatorLogEntry(DateTimeOffset.Now, level, message, detail));
        while (Logs.Count > 50)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private async Task OpenTwinEventsWindowAsync()
    {
        await DispatchAsync(() =>
        {
            if (_twinEventsWindow is { IsLoaded: true })
            {
                if (_twinEventsWindow.WindowState == WindowState.Minimized)
                {
                    _twinEventsWindow.WindowState = WindowState.Normal;
                }

                _twinEventsWindow.Activate();
                return;
            }

            var owner = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive)
                ?? Application.Current?.MainWindow;

            var window = new StudyTwinEventsWindow(this)
            {
                Owner = owner
            };
            window.Closed += OnTwinEventsWindowClosed;
            _twinEventsWindow = window;
            window.Show();
            window.Activate();
        }).ConfigureAwait(false);
    }

    private void OnTwinEventsWindowClosed(object? sender, EventArgs e)
    {
        if (_twinEventsWindow is not null)
        {
            _twinEventsWindow.Closed -= OnTwinEventsWindowClosed;
            _twinEventsWindow = null;
        }
    }

    private void CloseTwinEventsWindow()
    {
        if (_twinEventsWindow is null)
        {
            return;
        }

        _twinEventsWindow.Closed -= OnTwinEventsWindowClosed;
        _twinEventsWindow.Close();
        _twinEventsWindow = null;
    }

    private static OperatorLogLevel MapLevel(OperationOutcomeKind kind)
        => kind switch
        {
            OperationOutcomeKind.Warning => OperatorLogLevel.Warning,
            OperationOutcomeKind.Failure => OperatorLogLevel.Failure,
            _ => OperatorLogLevel.Info
        };

    private static Task DispatchAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private static Task<T> DispatchAsync<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return Task.FromResult(action());
        }

        return dispatcher.InvokeAsync(action).Task;
    }
}

internal sealed record StudyTwinCommandRequest(
    string ActionId,
    string Label,
    bool? RequestedVisible,
    int? Sequence,
    DateTimeOffset SentAtUtc,
    int? PreviousConfirmedSequence,
    string? PreviousConfirmedTimestampRaw,
    string? PreviousRecenterAnchorTimestampRaw,
    double? PreviousRecenterDistance,
    bool? PreviousObservedVisible);

internal readonly record struct StudyTwinCommandConfirmation(
    int? Sequence,
    string? TimestampRaw,
    DateTimeOffset? Timestamp,
    string? Label,
    string? Source,
    string? ActionId);

internal readonly record struct RecenterEffectObservation(
    bool Observed,
    bool AnchorUpdated,
    string? AnchorTimestampRaw,
    DateTimeOffset? AnchorTimestamp,
    double? PreviousDistance,
    double? CurrentDistance,
    bool DistanceImproved);

internal readonly record struct ParticleVisibilityEffectObservation(
    bool Observed,
    bool? PreviousVisible,
    bool? CurrentVisible,
    bool? RequestedVisible);

public sealed record StudyValueSection(string Id, string Label, string Description)
{
    public override string ToString() => Label;
}

public sealed class StudyStatusRowViewModel : ObservableObject
{
    private string _label;
    private string _key;
    private string _value;
    private string _expected;
    private string _detail;
    private OperationOutcomeKind _level;

    public StudyStatusRowViewModel(StudyStatusRow source)
    {
        _label = source.Label;
        _key = source.Key;
        _value = source.Value;
        _expected = source.Expected;
        _detail = source.Detail;
        _level = source.Level;
    }

    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    public string Key
    {
        get => _key;
        private set => SetProperty(ref _key, value);
    }

    public string Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    public string Expected
    {
        get => _expected;
        private set => SetProperty(ref _expected, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public OperationOutcomeKind Level
    {
        get => _level;
        private set => SetProperty(ref _level, value);
    }

    public void Apply(StudyStatusRow source)
    {
        Label = source.Label;
        Key = source.Key;
        Value = source.Value;
        Expected = source.Expected;
        Detail = source.Detail;
        Level = source.Level;
    }
}

public sealed record StudyStatusRow(
    string Label,
    string Key,
    string Value,
    string Expected,
    string Detail,
    OperationOutcomeKind Level);
