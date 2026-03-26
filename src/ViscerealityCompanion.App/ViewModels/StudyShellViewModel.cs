using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
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
    private const string TestSenderCoherenceMode = "0";
    private static readonly TimeSpan TwinStateIdleThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BenchRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProximityReadbackRefreshInterval = TimeSpan.FromSeconds(3);
    private static readonly Regex CommandSequenceRegex = new(@"\bseq=(\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly StudyValueSection[] SectionCatalog =
    [
        new("lsl", "LSL Routing", "Track the stream target and current LSL input connectivity."),
        new("controller", "Controller Breathing", "Follow controller breathing state, calibration, and live control value."),
        new("heartbeat", "Heartbeat", "Inspect heartbeat route selection and the latest incoming heartbeat value."),
        new("coherence", "Coherence", "Inspect coherence routing and the current coherence value."),
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
    private Window? _twinEventsWindow;
    private bool _initialized;
    private bool _twinRefreshPending;
    private bool _proximityRefreshPending;
    private string _activeFocusSectionId = string.Empty;
    private IReadOnlyDictionary<string, string> _reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private HeadsetAppStatus? _headsetStatus;
    private InstalledAppStatus? _installedAppStatus;
    private DeviceProfileStatus? _deviceProfileStatus;
    private QuestProximityStatus? _liveProximityStatus;
    private string? _liveProximitySelector;
    private DateTimeOffset? _lastProximityRefreshAtUtc;
    private string _endpointDraft;
    private string _connectionSummary = "Quest connection has not been checked yet.";
    private string _questStatusSummary = "Waiting for Quest connection.";
    private string _questStatusDetail = "Connect to the headset to verify the Sussex study runtime and profile.";
    private string _headsetModel = "Unknown";
    private int _batteryPercent;
    private string _headsetBatteryLabel = "Battery n/a";
    private string _headsetPerformanceLabel = "CPU n/a / GPU n/a";
    private string _headsetForegroundLabel = "Foreground n/a";
    private string _pinnedBuildSummary = "Choose the supplied Sussex APK.";
    private string _pinnedBuildDetail = "The window will compare both the local file and the installed Quest build against the pinned Sussex hash.";
    private string _localApkSummary = "Waiting for the supplied Sussex APK file.";
    private string _localApkDetail = "Browse to the supplied APK once. The study shell will remember that file path on this machine.";
    private string _installedApkSummary = "Installed build has not been checked yet.";
    private string _installedApkDetail = "Refresh the study status after connecting to the headset.";
    private string _stagedApkPath;
    private string _localApkHash = string.Empty;
    private OperationOutcomeKind _questStatusLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _pinnedBuildLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _localApkLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _installedApkLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _deviceProfileLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _liveRuntimeLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _lslLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _controllerLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _heartbeatLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _coherenceLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _recenterLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _particlesLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _proximityLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _testLslSenderLevel = OperationOutcomeKind.Preview;
    private string _deviceProfileSummary = "Pinned device profile has not been checked yet.";
    private string _deviceProfileDetail = "Refresh the study status after connecting to the headset.";
    private string _liveRuntimeSummary = "Waiting for quest_twin_state.";
    private string _liveRuntimeDetail = "Once the study APK starts publishing quest_twin_state, this window will focus on the Sussex-specific signals instead of the full raw keyspace.";
    private string _lslSummary = "Waiting for LSL runtime state.";
    private string _lslDetail = "The pinned stream target and live LSL connectivity will appear here once the study runtime is active.";
    private double _lslValuePercent;
    private string _lslValueLabel = "n/a";
    private string _controllerSummary = "Waiting for controller breathing state.";
    private string _controllerDetail = "The Sussex study expects controller-based breathing. Calibration and controller value reporting will appear here.";
    private string _heartbeatSummary = "Waiting for heartbeat state.";
    private string _heartbeatDetail = "The study runtime should report the latest heartbeat source and value over quest_twin_state.";
    private string _coherenceSummary = "Waiting for coherence state.";
    private string _coherenceDetail = "The study runtime should report the latest coherence route and value over quest_twin_state.";
    private string _recenterSummary = "Waiting for recenter telemetry.";
    private string _recenterDetail = "The recenter action is available, and camera drift will appear here once the study runtime starts publishing it.";
    private string _particlesSummary = "Waiting for runtime particle state.";
    private string _particlesDetail = "Particle visibility and render suppression will appear here once the study runtime starts publishing them.";
    private string _proximitySummary = "Proximity hold has not been checked yet.";
    private string _proximityDetail = "Quest vrpowermanager readback will appear here once the shell can reach the active headset selector.";
    private string _proximityActionLabel = "Disable for 8h";
    private string _testLslSenderSummary = "Windows TEST sender off.";
    private string _testLslSenderDetail = "Start the Windows TEST sender only for bench checks. It publishes synthetic heartbeat pulses and requests the Sussex coherence route over the shared inlet.";
    private string _testLslSenderValueLabel = "Not running";
    private string _testLslSenderActionLabel = "Start TEST Sender";
    private string _lastTwinStateTimestampLabel = "No live app-state timestamp yet.";
    private string _lastActionLabel = "None";
    private string _lastActionDetail = "No study action has run yet.";
    private OperationOutcomeKind _lastActionLevel = OperationOutcomeKind.Preview;
    private double _controllerValuePercent;
    private string _controllerValueLabel = "n/a";
    private double _controllerCalibrationPercent;
    private string _controllerCalibrationLabel = "Calibration n/a";
    private double _coherencePercent;
    private string _coherenceValueLabel = "n/a";
    private double _recenterDistancePercent;
    private string _recenterDistanceLabel = "n/a";
    private StudyTwinCommandRequest? _lastRecenterCommandRequest;
    private StudyTwinCommandRequest? _lastParticlesCommandRequest;
    private string? _testSenderRestoreHeartbeatMode;
    private string? _testSenderRestoreCoherenceMode;
    private StudyValueSection? _selectedLiveSection;

    public StudyShellViewModel(StudyShellDefinition study)
    {
        _study = study;
        _appSessionState = AppSessionState.Load();
        _studySessionState = StudyShellSessionState.Load();
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
        BrowseApkCommand = new AsyncRelayCommand(BrowseApkAsync);
        InstallStudyAppCommand = new AsyncRelayCommand(InstallStudyAppAsync);
        LaunchStudyAppCommand = new AsyncRelayCommand(LaunchStudyAppAsync);
        ApplyPinnedDeviceProfileCommand = new AsyncRelayCommand(ApplyPinnedDeviceProfileAsync);
        ToggleProximityCommand = new AsyncRelayCommand(ToggleProximityAsync);
        ToggleTestLslSenderCommand = new AsyncRelayCommand(ToggleTestLslSenderAsync);
        RecenterCommand = new AsyncRelayCommand(RecenterAsync);
        ParticlesOnCommand = new AsyncRelayCommand(ParticlesOnAsync);
        ParticlesOffCommand = new AsyncRelayCommand(ParticlesOffAsync);
        OpenTwinEventsWindowCommand = new AsyncRelayCommand(OpenTwinEventsWindowAsync);
    }

    public string StudyLabel => _study.Label;
    public string StudyId => _study.Id;
    public string StudyPartner => _study.Partner;
    public string StudyDescription => _study.Description;
    public string PinnedPackageId => _study.App.PackageId;

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

    public OperationOutcomeKind PinnedBuildLevel
    {
        get => _pinnedBuildLevel;
        private set => SetProperty(ref _pinnedBuildLevel, value);
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

    public OperationOutcomeKind TestLslSenderLevel
    {
        get => _testLslSenderLevel;
        private set => SetProperty(ref _testLslSenderLevel, value);
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

    public string ProximityActionLabel
    {
        get => _proximityActionLabel;
        private set => SetProperty(ref _proximityActionLabel, value);
    }

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
            && !string.IsNullOrWhiteSpace(ResolveHzdbSelector());

    public bool CanToggleTestLslSender
        => _testLslSignalService.IsRunning
            || _testLslSignalService.RuntimeState.Available;

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
    public AsyncRelayCommand BrowseApkCommand { get; }
    public AsyncRelayCommand InstallStudyAppCommand { get; }
    public AsyncRelayCommand LaunchStudyAppCommand { get; }
    public AsyncRelayCommand ApplyPinnedDeviceProfileCommand { get; }
    public AsyncRelayCommand ToggleProximityCommand { get; }
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
        await RefreshStatusAsync().ConfigureAwait(false);
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

        CloseTwinEventsWindow();
        _testLslSignalService.Dispose();
    }

    private async Task ProbeUsbAsync()
    {
        var outcome = await _questService.ProbeUsbAsync().ConfigureAwait(false);
        await ApplyOutcomeAsync("Probe USB", outcome).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(outcome.Endpoint))
        {
            SaveSession(usbSerial: outcome.Endpoint);
            await DispatchAsync(RefreshBenchToolsStatus).ConfigureAwait(false);
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
        await HandleConnectionOutcomeAsync("Enable Wi-Fi ADB", outcome, refreshAfter: false).ConfigureAwait(false);
    }

    private async Task ConnectQuestAsync()
    {
        var endpoint = await DispatchAsync(() => EndpointDraft.Trim()).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Quest connect blocked.", "Enter an IP:port endpoint first.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _questService.ConnectAsync(endpoint).ConfigureAwait(false);
        await HandleConnectionOutcomeAsync("Connect Quest", outcome).ConfigureAwait(false);
    }

    public async Task RefreshStatusAsync()
    {
        await RefreshLocalApkStatusAsync().ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
        await RefreshInstalledAppStatusAsync().ConfigureAwait(false);
        await RefreshDeviceProfileStatusAsync().ConfigureAwait(false);
        await RefreshProximityStatusAsync(force: true).ConfigureAwait(false);
        await DispatchAsync(UpdatePinnedBuildStatus).ConfigureAwait(false);
        await DispatchAsync(UpdateDeviceProfileRows).ConfigureAwait(false);
        await DispatchAsync(RefreshBenchToolsStatus).ConfigureAwait(false);
        await DispatchAsync(RefreshLiveTwinState).ConfigureAwait(false);
    }

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
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Study install blocked.", "Choose the supplied Sussex APK first.")).ConfigureAwait(false);
            return;
        }

        if (!HashMatches(LocalApkHash, _study.App.Sha256))
        {
            await DispatchAsync(() => AppendLog(
                OperatorLogLevel.Failure,
                "Study install blocked.",
                "The selected APK does not match the pinned Sussex hash. Choose the supplied study build before installing.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _questService.InstallAppAsync(CreateStudyTarget(localPath)).ConfigureAwait(false);
        await ApplyOutcomeAsync("Install Sussex APK", outcome).ConfigureAwait(false);
        await RefreshStatusAsync().ConfigureAwait(false);
    }

    public async Task LaunchStudyAppAsync()
    {
        var outcome = await _questService.LaunchAppAsync(CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false))).ConfigureAwait(false);
        await ApplyOutcomeAsync("Launch Sussex APK", outcome).ConfigureAwait(false);
        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        await RefreshStatusAsync().ConfigureAwait(false);

        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            await DispatchAsync(() =>
            {
                if (!string.Equals(_headsetStatus?.ForegroundPackageId, _study.App.PackageId, StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog(
                        OperatorLogLevel.Warning,
                        "Launch command sent, but Sussex is not foreground.",
                        $"Foreground package is {_headsetStatus?.ForegroundPackageId ?? "unknown"}. The headset may be in Guardian, lockscreen, or the Meta shell instead of the study runtime.");
                }
            }).ConfigureAwait(false);
        }
    }

    public async Task ApplyPinnedDeviceProfileAsync()
    {
        var outcome = await _questService.ApplyDeviceProfileAsync(CreatePinnedDeviceProfile()).ConfigureAwait(false);
        await ApplyOutcomeAsync("Apply Study Device Profile", outcome).ConfigureAwait(false);
        await RefreshDeviceProfileStatusAsync().ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
    }

    private async Task ToggleProximityAsync()
    {
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

    private async Task ToggleTestLslSenderAsync()
    {
        var isRunning = _testLslSignalService.IsRunning;
        var actionLabel = isRunning ? "Stop TEST Sender" : "Start TEST Sender";
        var streamName = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamName)
            ? "quest_biofeedback_in"
            : _study.Monitoring.ExpectedLslStreamName;
        var streamType = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamType)
            ? "quest.biofeedback"
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
            "Heartbeat mode switched to LSL and coherence mode switched to heartbeat-derived for bench checks.")
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

    private async Task HandleConnectionOutcomeAsync(string actionLabel, OperationOutcome outcome, bool refreshAfter = true)
    {
        await DispatchAsync(() =>
        {
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
        }).ConfigureAwait(false);

        if (refreshAfter)
        {
            await RefreshStatusAsync().ConfigureAwait(false);
        }
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
                LocalApkSummary = "Waiting for the supplied Sussex APK file.";
                LocalApkDetail = string.IsNullOrWhiteSpace(stagedPath)
                    ? _study.App.Notes
                    : $"Saved study APK path not found: {stagedPath}";
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
                    ? "Selected local file matches the pinned Sussex build."
                    : "Selected local file does not match the pinned Sussex build.";
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
                LocalApkSummary = "Could not verify the selected APK file.";
                LocalApkDetail = ex.Message;
                OnPropertyChanged(nameof(HasValidPinnedLocalApk));
            }).ConfigureAwait(false);
        }
    }

    private async Task RefreshInstalledAppStatusAsync()
    {
        var target = CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false));
        _installedAppStatus = await _questService.QueryInstalledAppAsync(target).ConfigureAwait(false);

        await DispatchAsync(() =>
        {
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
                InstalledApkSummary = _installedAppStatus.Summary;
                InstalledApkDetail = _installedAppStatus.Detail;
            }
        }).ConfigureAwait(false);
    }

    private async Task RefreshDeviceProfileStatusAsync()
    {
        _deviceProfileStatus = await _questService.QueryDeviceProfileStatusAsync(CreatePinnedDeviceProfile()).ConfigureAwait(false);

        await DispatchAsync(() =>
        {
            if (_deviceProfileStatus is null)
            {
                DeviceProfileLevel = OperationOutcomeKind.Preview;
                DeviceProfileSummary = "Pinned device profile has not been checked yet.";
                DeviceProfileDetail = "Refresh the study status after connecting to the headset.";
                UpdateDeviceProfileRows();
                return;
            }

            DeviceProfileLevel = _deviceProfileStatus.IsActive ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning;
            DeviceProfileSummary = _deviceProfileStatus.Summary;
            DeviceProfileDetail = _deviceProfileStatus.Detail;
            UpdateDeviceProfileRows();
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

            ConnectionSummary = _headsetStatus.ConnectionLabel;
            QuestStatusLevel = _headsetStatus.IsConnected ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning;
            QuestStatusSummary = _headsetStatus.Summary;
            QuestStatusDetail = _headsetStatus.Detail;
            HeadsetModel = string.IsNullOrWhiteSpace(_headsetStatus.DeviceModel) ? "Quest" : _headsetStatus.DeviceModel;
            BatteryPercent = Math.Clamp(_headsetStatus.BatteryLevel ?? 0, 0, 100);
            HeadsetBatteryLabel = _headsetStatus.BatteryLevel is null ? "Battery n/a" : $"{_headsetStatus.BatteryLevel}%";
            HeadsetPerformanceLabel = $"CPU {(_headsetStatus.CpuLevel?.ToString() ?? "n/a")} / GPU {(_headsetStatus.GpuLevel?.ToString() ?? "n/a")}";
            HeadsetForegroundLabel = string.IsNullOrWhiteSpace(_headsetStatus.ForegroundPackageId)
                ? "Foreground n/a"
                : _headsetStatus.ForegroundPackageId;
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
                PinnedBuildSummary = "Pinned Sussex build is installed on the headset.";
            }
            else if (string.IsNullOrWhiteSpace(_installedAppStatus.InstalledSha256))
            {
                PinnedBuildLevel = OperationOutcomeKind.Warning;
                PinnedBuildSummary = "Sussex runtime is installed, but the headset build hash could not be verified.";
            }
            else
            {
                PinnedBuildLevel = OperationOutcomeKind.Warning;
                PinnedBuildSummary = "A different LslTwin build is installed on the headset.";
            }
        }
        else if (HashMatches(LocalApkHash, _study.App.Sha256))
        {
            PinnedBuildLevel = OperationOutcomeKind.Warning;
            PinnedBuildSummary = "Pinned Sussex build is ready to install.";
        }
        else if (!string.IsNullOrWhiteSpace(StagedApkPath) && File.Exists(StagedApkPath))
        {
            PinnedBuildLevel = OperationOutcomeKind.Failure;
            PinnedBuildSummary = "Selected local file does not match the pinned Sussex build.";
        }
        else
        {
            PinnedBuildLevel = OperationOutcomeKind.Preview;
            PinnedBuildSummary = "Choose the supplied Sussex APK.";
        }

        var details = new List<string>
        {
            $"Pinned package: {_study.App.PackageId}",
            $"Pinned version: {_study.App.VersionName}",
            $"Pinned SHA256: {_study.App.Sha256}"
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
        UpdateTestLslSenderCard();
        UpdateBenchRefreshTimerState();
        OnPropertyChanged(nameof(CanToggleProximity));
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

        if (_reportedTwinState.Count == 0)
        {
            LiveRuntimeLevel = studyRuntimeForeground ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview;
            LiveRuntimeSummary = studyRuntimeForeground
                ? "Study runtime is foreground, but quest_twin_state is idle."
                : "Waiting for quest_twin_state.";
            LiveRuntimeDetail = studyRuntimeForeground
                ? $"The headset still reports the Sussex APK in front, but no fresh app-state frames are arriving yet. The Quest runtime may still be starting, paused, or off-face. {BuildTwinCommandTransportDetail()}".Trim()
                : $"Launch the Sussex runtime and wait for quest_twin_state to start publishing before relying on the live study monitor. {BuildTwinCommandTransportDetail()}".Trim();
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
            ? $"The last quest_twin_state frame arrived at {lastStateReceivedAt!.Value.ToLocalTime():HH:mm:ss}. If the headset is not on-face, wake it or disable proximity before relying on live confirmation. {BuildTwinCommandTransportDetail()}".Trim()
            : string.IsNullOrWhiteSpace(publisherPackage)
                ? $"Received {_reportedTwinState.Count} live key(s). The runtime did not include a package id in the current state frame. {BuildTwinCommandTransportDetail()}".Trim()
                : $"Received {_reportedTwinState.Count} live key(s) from {publisherPackage}. {BuildTwinCommandTransportDetail()}".Trim();
    }

    private void UpdateLslCard()
    {
        var expectedName = _study.Monitoring.ExpectedLslStreamName;
        var expectedType = _study.Monitoring.ExpectedLslStreamType;
        var testSenderActive = _testLslSignalService.IsRunning;
        var testSenderDetail = testSenderActive
            ? $"Companion TEST sender is publishing heartbeat-pulse bench traffic on {expectedName} / {expectedType} and requesting the heartbeat-derived coherence route."
            : $"Start the Windows TEST sender below to bench-check {expectedName} / {expectedType} and the headset coherence path without a live sensor.";
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

        LslValuePercent = hasInputValue ? inputValue * 100d : 0d;
        LslValueLabel = hasInputValue ? $"{inputValue:0.000}" : "Not echoed";

        if (_reportedTwinState.Count == 0)
        {
            LslLevel = OperationOutcomeKind.Preview;
            LslSummary = "Waiting for LSL runtime state.";
            LslDetail = $"Expected stream: {expectedName} / {expectedType}. {testSenderDetail}";
            return;
        }

        var streamMatches = (string.IsNullOrWhiteSpace(expectedName) || string.Equals(streamName, expectedName, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(expectedType) || string.Equals(streamType, expectedType, StringComparison.OrdinalIgnoreCase));
        var hasConnectedInput = connectedFlag == true
            || connectedCount.GetValueOrDefault() > 0
            || !string.IsNullOrWhiteSpace(connectedName);

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
            LslSummary += $" Inlet value {inputValue:0.000}.";
        }
        else if (hasConnectedInput)
        {
            LslSummary += " Inlet connected; this public build does not echo the routed inlet value yet.";
        }
        if (testSenderActive)
        {
            LslSummary += " Companion TEST sender active.";
        }
        LslDetail =
            $"Expected {expectedName} / {expectedType}. Runtime target {(string.IsNullOrWhiteSpace(streamName) ? "stream name unavailable" : streamName)} / {(string.IsNullOrWhiteSpace(streamType) ? "stream type unavailable" : streamType)}. " +
            $"Connected {(string.IsNullOrWhiteSpace(connectedName) ? "stream name unavailable" : connectedName)} / {(string.IsNullOrWhiteSpace(connectedType) ? "stream type unavailable" : connectedType)}. " +
            $"Connected {connectedCount?.ToString() ?? (connectedFlag.HasValue ? (connectedFlag.Value ? "1" : "0") : "n/a")}, connecting {connectingCount?.ToString() ?? "n/a"}, total {totalCount?.ToString() ?? "n/a"}. " +
            (hasInputValue
                ? $"Latest normalized inlet value {inputValue:0.000} via {inputValueKey}. "
                : "The current public state frame confirms inlet connectivity. The live link is healthy, but this build did not echo the routed inlet value yet. ") +
            $"{testSenderDetail} {(string.IsNullOrWhiteSpace(statusLine) ? string.Empty : statusLine)}".Trim();
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

        CoherencePercent = value.HasValue ? value.Value * 100d : 0d;
        CoherenceValueLabel = value.HasValue ? $"{value.Value:0.000}" : rawValue ?? "n/a";

        if (_reportedTwinState.Count == 0)
        {
            CoherenceLevel = OperationOutcomeKind.Preview;
            CoherenceSummary = "Waiting for coherence state.";
            CoherenceDetail = "Coherence route and live value will appear once quest_twin_state is active.";
            return;
        }

        CoherenceLevel = value.HasValue || !string.IsNullOrWhiteSpace(rawValue)
            ? OperationOutcomeKind.Success
            : OperationOutcomeKind.Warning;
        CoherenceSummary = value.HasValue || !string.IsNullOrWhiteSpace(rawValue)
            ? $"Coherence live value: {CoherenceValueLabel}."
            : "Coherence route visible, but no live coherence value yet.";
        CoherenceDetail = $"Route {(string.IsNullOrWhiteSpace(routeLabel) ? "n/a" : routeLabel)} (mode {routeMode ?? "n/a"}). Uses heartbeat source {usesHeartbeat ?? "n/a"}.";
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
        var selector = ResolveHzdbSelector();
        ProximityActionLabel = "Disable for 8h";

        if (!_hzdbService.IsAvailable)
        {
            ProximityLevel = OperationOutcomeKind.Preview;
            ProximitySummary = "Proximity hold unavailable.";
            ProximityDetail = "Install or expose @meta-quest/hzdb before using the experiment-shell proximity hold.";
            return;
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            ProximityLevel = OperationOutcomeKind.Preview;
            ProximitySummary = "Quest selector needed for proximity hold.";
            ProximityDetail = "Probe USB or connect the Quest first. The shell prefers the last USB serial and falls back to the active endpoint.";
            return;
        }

        var tracked = _appSessionState.GetTrackedProximity(selector);
        var updatedLabel = tracked.UpdatedAtUtc.HasValue
            ? tracked.UpdatedAtUtc.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : "n/a";
        var liveStatus = string.Equals(_liveProximitySelector, selector, StringComparison.OrdinalIgnoreCase)
            ? _liveProximityStatus
            : null;

        if (liveStatus?.Available == true)
        {
            var readAt = liveStatus.RetrievedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var autoSleepLabel = liveStatus.AutoSleepTimeMs.HasValue
                ? $"{liveStatus.AutoSleepTimeMs.Value / 1000d:0.#}s"
                : "n/a";
            var stateLabel = string.IsNullOrWhiteSpace(liveStatus.HeadsetState) ? "unknown" : liveStatus.HeadsetState;

            if (liveStatus.HoldActive)
            {
                ProximityLevel = OperationOutcomeKind.Warning;
                ProximityActionLabel = "Enable Proximity";
                ProximitySummary = liveStatus.HoldUntilUtc.HasValue
                    ? $"Proximity hold active on headset until {liveStatus.HoldUntilUtc.Value.ToLocalTime():HH:mm}."
                    : "Proximity hold active on headset.";
                ProximityDetail =
                    $"Quest vrpowermanager reports virtual proximity {liveStatus.VirtualState} at {readAt}, so normal wear-sensor sleep is bypassed. " +
                    $"Headset state {stateLabel}. Base auto-sleep {autoSleepLabel}. " +
                    (tracked.Known && !tracked.ExpectedEnabled && tracked.DisableUntilUtc.HasValue
                        ? $"Companion last requested a hold until {tracked.DisableUntilUtc.Value.ToLocalTime():HH:mm}."
                        : "Quest readback is authoritative even if the hold was toggled outside the companion.");
                return;
            }

            ProximityLevel = OperationOutcomeKind.Success;
            ProximitySummary = "Proximity sensor enabled on headset.";
            ProximityDetail =
                $"Quest vrpowermanager reports virtual proximity {liveStatus.VirtualState} at {readAt}, so normal wear-sensor behavior is active. " +
                $"Headset state {stateLabel}. Base auto-sleep {autoSleepLabel}. " +
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
        }

        if (tracked.Known && !tracked.ExpectedEnabled && !tracked.DisableWindowExpired && tracked.DisableUntilUtc.HasValue)
        {
            var untilLocal = tracked.DisableUntilUtc.Value.ToLocalTime();
            if (liveStatus is not { Available: false })
            {
                ProximityLevel = OperationOutcomeKind.Warning;
                ProximitySummary = $"Proximity sensor expected off until {untilLocal:HH:mm}.";
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

    private void UpdateTestLslSenderCard()
    {
        var expectedName = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamName)
            ? "quest_biofeedback_in"
            : _study.Monitoring.ExpectedLslStreamName;
        var expectedType = string.IsNullOrWhiteSpace(_study.Monitoring.ExpectedLslStreamType)
            ? "quest.biofeedback"
            : _study.Monitoring.ExpectedLslStreamType;

        TestLslSenderActionLabel = _testLslSignalService.IsRunning ? "Stop TEST Sender" : "Start TEST Sender";

        if (_testLslSignalService.IsRunning)
        {
            TestLslSenderLevel = OperationOutcomeKind.Warning;
            TestLslSenderSummary = "Windows TEST sender active.";
            TestLslSenderValueLabel = _testLslSignalService.LastSentAtUtc.HasValue
                ? $"Latest synthetic heartbeat pulse {_testLslSignalService.LastValue:0.000} at {_testLslSignalService.LastSentAtUtc.Value.ToLocalTime():HH:mm:ss}."
                : "Starting synthetic heartbeat pulse stream...";
            TestLslSenderDetail =
                $"Synthetic heartbeat-pulse samples are publishing locally on {expectedName} / {expectedType}. The study shell also requests Heartbeat Mode = LSL and Coherence Mode = Heartbeat Derived while this bench sender is active.";
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
            $"Start the Windows TEST sender to publish synthetic heartbeat pulses on {expectedName} / {expectedType}. This is bench-only traffic and temporarily routes the Sussex coherence path to LSL heartbeat.";
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
        var configuredKey = _study.Monitoring.LslValueKeys.FirstOrDefault() ?? "signal01.breathing_lsl";
        if (TryGetObservedLslValue(out var value, out var sourceKey))
        {
            return new StudyStatusRow(
                "Inlet value",
                sourceKey,
                value.ToString("0.000", CultureInfo.InvariantCulture),
                string.Empty,
                "Latest normalized inlet value echoed by quest_twin_state when public signal mirroring is available.",
                OperationOutcomeKind.Success);
        }

        return new StudyStatusRow(
            "Inlet value",
            configuredKey,
            "Not echoed by current public build",
            string.Empty,
            "The Sussex runtime confirms inlet connectivity here, but the current public twin-state frame only echoes the routed inlet value when signal mirroring is enabled.",
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
            || (_hzdbService.IsAvailable && !string.IsNullOrWhiteSpace(selector))
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
        _ = RefreshProximityStatusAsync();
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
        var savedPath = _studySessionState.GetApkPath(_study.Id);
        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            return Path.GetFullPath(savedPath);
        }

        if (!string.IsNullOrWhiteSpace(_study.App.ApkPath) && File.Exists(_study.App.ApkPath))
        {
            return Path.GetFullPath(_study.App.ApkPath);
        }

        return string.Empty;
    }

    private string ResolveHzdbSelector()
        => _appSessionState.LastUsbSerial
            ?? _appSessionState.ActiveEndpoint
            ?? (string.IsNullOrWhiteSpace(EndpointDraft) ? string.Empty : EndpointDraft.Trim());

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

        var selector = await DispatchAsync(ResolveHzdbSelector).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(selector))
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
            string.Equals(_liveProximitySelector, selector, StringComparison.OrdinalIgnoreCase) ? _liveProximityStatus : null).ConfigureAwait(false);
        var shouldRefresh = await DispatchAsync(() =>
            force
            || !string.Equals(_liveProximitySelector, selector, StringComparison.OrdinalIgnoreCase)
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
            var status = await _hzdbService.GetProximityStatusAsync(selector).ConfigureAwait(false);
            await DispatchAsync(() =>
            {
                _liveProximitySelector = selector;
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
