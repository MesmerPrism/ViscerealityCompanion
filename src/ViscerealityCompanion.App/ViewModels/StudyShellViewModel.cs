using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App.ViewModels;

public sealed class StudyShellViewModel : ObservableObject, IDisposable
{
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
    private readonly ITwinModeBridge _twinBridge = TwinModeBridgeFactory.CreateDefault();
    private readonly DispatcherTimer? _twinRefreshTimer;
    private bool _initialized;
    private bool _twinRefreshPending;
    private IReadOnlyDictionary<string, string> _reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private HeadsetAppStatus? _headsetStatus;
    private InstalledAppStatus? _installedAppStatus;
    private DeviceProfileStatus? _deviceProfileStatus;
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
    private string _deviceProfileSummary = "Pinned device profile has not been checked yet.";
    private string _deviceProfileDetail = "Refresh the study status after connecting to the headset.";
    private string _liveRuntimeSummary = "Waiting for quest_twin_state.";
    private string _liveRuntimeDetail = "Once the study APK starts publishing quest_twin_state, this window will focus on the Sussex-specific signals instead of the full raw keyspace.";
    private string _lslSummary = "Waiting for LSL runtime state.";
    private string _lslDetail = "The pinned stream target and live LSL connectivity will appear here once the study runtime is active.";
    private string _controllerSummary = "Waiting for controller breathing state.";
    private string _controllerDetail = "The Sussex study expects controller-based breathing. Calibration and controller value reporting will appear here.";
    private string _heartbeatSummary = "Waiting for heartbeat state.";
    private string _heartbeatDetail = "The study runtime should report the latest heartbeat source and value over quest_twin_state.";
    private string _coherenceSummary = "Waiting for coherence state.";
    private string _coherenceDetail = "The study runtime should report the latest coherence route and value over quest_twin_state.";
    private string _recenterSummary = "Recenter drift telemetry not exposed yet.";
    private string _recenterDetail = "The current public runtime does not publish camera distance from the last recenter point yet. The recenter action can still be sent.";
    private string _particlesSummary = "Particle visibility control not exposed yet.";
    private string _particlesDetail = "The current public runtime does not expose a particle visibility command or public state key yet.";
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
        }

        if (_twinBridge is LslTwinModeBridge lslBridge)
        {
            lslBridge.StateChanged += OnTwinBridgeStateChanged;
        }

        ProbeUsbCommand = new AsyncRelayCommand(ProbeUsbAsync);
        DiscoverWifiCommand = new AsyncRelayCommand(DiscoverWifiAsync);
        EnableWifiCommand = new AsyncRelayCommand(EnableWifiAsync);
        ConnectQuestCommand = new AsyncRelayCommand(ConnectQuestAsync);
        RefreshStatusCommand = new AsyncRelayCommand(RefreshStatusAsync);
        BrowseApkCommand = new AsyncRelayCommand(BrowseApkAsync);
        InstallStudyAppCommand = new AsyncRelayCommand(InstallStudyAppAsync);
        LaunchStudyAppCommand = new AsyncRelayCommand(LaunchStudyAppAsync);
        ApplyPinnedDeviceProfileCommand = new AsyncRelayCommand(ApplyPinnedDeviceProfileAsync);
        RecenterCommand = new AsyncRelayCommand(RecenterAsync);
        ParticlesOnCommand = new AsyncRelayCommand(ParticlesOnAsync);
        ParticlesOffCommand = new AsyncRelayCommand(ParticlesOffAsync);
    }

    public string StudyLabel => _study.Label;
    public string StudyPartner => _study.Partner;
    public string StudyDescription => _study.Description;

    public string EndpointDraft
    {
        get => _endpointDraft;
        set => SetProperty(ref _endpointDraft, value);
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

    public ObservableCollection<StudyValueSection> LiveSections { get; } = new();

    public StudyValueSection? SelectedLiveSection
    {
        get => _selectedLiveSection;
        set
        {
            if (SetProperty(ref _selectedLiveSection, value))
            {
                RefreshFocusRows();
            }
        }
    }

    public ObservableCollection<StudyStatusRow> DeviceProfileRows { get; } = new();
    public ObservableCollection<StudyStatusRow> FocusRows { get; } = new();
    public ObservableCollection<TwinStateEvent> RecentTwinEvents { get; } = new();
    public ObservableCollection<OperatorLogEntry> Logs { get; } = new();

    public AsyncRelayCommand ProbeUsbCommand { get; }
    public AsyncRelayCommand DiscoverWifiCommand { get; }
    public AsyncRelayCommand EnableWifiCommand { get; }
    public AsyncRelayCommand ConnectQuestCommand { get; }
    public AsyncRelayCommand RefreshStatusCommand { get; }
    public AsyncRelayCommand BrowseApkCommand { get; }
    public AsyncRelayCommand InstallStudyAppCommand { get; }
    public AsyncRelayCommand LaunchStudyAppCommand { get; }
    public AsyncRelayCommand ApplyPinnedDeviceProfileCommand { get; }
    public AsyncRelayCommand RecenterCommand { get; }
    public AsyncRelayCommand ParticlesOnCommand { get; }
    public AsyncRelayCommand ParticlesOffCommand { get; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        EnsureTwinBridgeMonitoringStarted();
        await RefreshStatusAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_twinBridge is LslTwinModeBridge lslBridge)
        {
            lslBridge.StateChanged -= OnTwinBridgeStateChanged;
        }

        if (_twinRefreshTimer is not null)
        {
            _twinRefreshTimer.Tick -= OnTwinRefreshTimerTick;
            _twinRefreshTimer.Stop();
        }

        (_twinBridge as IDisposable)?.Dispose();
    }

    private async Task ProbeUsbAsync()
    {
        var outcome = await _questService.ProbeUsbAsync().ConfigureAwait(false);
        await ApplyOutcomeAsync("Probe USB", outcome).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(outcome.Endpoint))
        {
            SaveSession(usbSerial: outcome.Endpoint);
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

    private async Task RefreshStatusAsync()
    {
        await RefreshLocalApkStatusAsync().ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
        await RefreshInstalledAppStatusAsync().ConfigureAwait(false);
        await RefreshDeviceProfileStatusAsync().ConfigureAwait(false);
        await DispatchAsync(UpdatePinnedBuildStatus).ConfigureAwait(false);
        await DispatchAsync(UpdateDeviceProfileRows).ConfigureAwait(false);
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

    private async Task InstallStudyAppAsync()
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

    private async Task LaunchStudyAppAsync()
    {
        var outcome = await _questService.LaunchAppAsync(CreateStudyTarget(await DispatchAsync(() => StagedApkPath).ConfigureAwait(false))).ConfigureAwait(false);
        await ApplyOutcomeAsync("Launch Sussex APK", outcome).ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
    }

    private async Task ApplyPinnedDeviceProfileAsync()
    {
        var outcome = await _questService.ApplyDeviceProfileAsync(CreatePinnedDeviceProfile()).ConfigureAwait(false);
        await ApplyOutcomeAsync("Apply Study Device Profile", outcome).ConfigureAwait(false);
        await RefreshDeviceProfileStatusAsync().ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
    }

    private Task RecenterAsync()
        => SendStudyTwinCommandAsync(_study.Controls.RecenterCommandActionId, "Recenter");

    private Task ParticlesOnAsync()
        => SendStudyTwinCommandAsync(_study.Controls.ParticleVisibleOnActionId, "Particles On");

    private Task ParticlesOffAsync()
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

        var outcome = await _twinBridge.SendCommandAsync(new TwinModeCommand(actionId, label)).ConfigureAwait(false);
        await ApplyOutcomeAsync(label, outcome).ConfigureAwait(false);
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

    private void RefreshLiveTwinState()
    {
        if (_twinBridge is not LslTwinModeBridge lslBridge)
        {
            _reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            : $"Last app-state frame {lslBridge.LastStateReceivedAt.Value:HH:mm:ss}.";

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
        if (_reportedTwinState.Count == 0)
        {
            LiveRuntimeLevel = OperationOutcomeKind.Preview;
            LiveRuntimeSummary = "Waiting for quest_twin_state.";
            LiveRuntimeDetail = "Launch the Sussex runtime and wait for quest_twin_state to start publishing before relying on the live study monitor.";
            return;
        }

        var publisherPackage = GetFirstValue("app.package", "app.packageId", "foreground.package", "active.package", "package");
        if (string.IsNullOrWhiteSpace(publisherPackage))
        {
            publisherPackage = _headsetStatus?.ForegroundPackageId;
        }

        var matchesStudyRuntime = string.Equals(publisherPackage, _study.App.PackageId, StringComparison.OrdinalIgnoreCase);
        LiveRuntimeLevel = matchesStudyRuntime ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning;
        LiveRuntimeSummary = matchesStudyRuntime
            ? "Live study runtime state is active."
            : "Live state is active, but the publisher does not clearly match the pinned study runtime.";
        LiveRuntimeDetail = string.IsNullOrWhiteSpace(publisherPackage)
            ? $"Received {_reportedTwinState.Count} live key(s). The runtime did not include a package id in the current state frame."
            : $"Received {_reportedTwinState.Count} live key(s) from {publisherPackage}.";
    }

    private void UpdateLslCard()
    {
        var expectedName = _study.Monitoring.ExpectedLslStreamName;
        var expectedType = _study.Monitoring.ExpectedLslStreamType;
        var streamName = GetFirstValue(_study.Monitoring.LslStreamNameKeys);
        var streamType = GetFirstValue(_study.Monitoring.LslStreamTypeKeys);
        var connectedCount = ParseInt(GetFirstValue("connection.lsl.connected_count"));
        var connectingCount = ParseInt(GetFirstValue("connection.lsl.connecting_count"));
        var totalCount = ParseInt(GetFirstValue("connection.lsl.total_count"));

        if (_reportedTwinState.Count == 0)
        {
            LslLevel = OperationOutcomeKind.Preview;
            LslSummary = "Waiting for LSL runtime state.";
            LslDetail = $"Expected stream: {expectedName} / {expectedType}.";
            return;
        }

        var streamMatches = (string.IsNullOrWhiteSpace(expectedName) || string.Equals(streamName, expectedName, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(expectedType) || string.Equals(streamType, expectedType, StringComparison.OrdinalIgnoreCase));
        var hasConnectedInput = connectedCount.GetValueOrDefault() > 0;

        LslLevel = hasConnectedInput && streamMatches
            ? OperationOutcomeKind.Success
            : connectedCount.HasValue || connectingCount.HasValue || totalCount.HasValue
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Preview;
        LslSummary = hasConnectedInput
            ? $"LSL input live: {connectedCount} connected."
            : connectingCount.GetValueOrDefault() > 0
                ? $"LSL input connecting: {connectingCount} stream(s) still resolving."
                : "No live LSL input reported yet.";
        LslDetail = $"Expected {expectedName} / {expectedType}. Runtime reports {(string.IsNullOrWhiteSpace(streamName) ? "stream name unavailable" : streamName)} / {(string.IsNullOrWhiteSpace(streamType) ? "stream type unavailable" : streamType)}. Connected {connectedCount?.ToString() ?? "n/a"}, connecting {connectingCount?.ToString() ?? "n/a"}, total {totalCount?.ToString() ?? "n/a"}.";
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
        if (_reportedTwinState.Count == 0)
        {
            RecenterLevel = OperationOutcomeKind.Preview;
            RecenterSummary = "Waiting for recenter telemetry.";
            RecenterDetail = "The recenter action is available, but the live drift distance appears only when the APK publishes it.";
            RecenterDistancePercent = 0d;
            RecenterDistanceLabel = "n/a";
            return;
        }

        if (!distance.HasValue)
        {
            RecenterLevel = OperationOutcomeKind.Preview;
            RecenterSummary = "Recenter drift telemetry not exposed yet.";
            RecenterDetail = "The current public runtime does not publish camera distance from the last recenter point yet. The recenter action can still be sent.";
            RecenterDistancePercent = 0d;
            RecenterDistanceLabel = "n/a";
            return;
        }

        RecenterDistancePercent = Math.Clamp(distance.Value / Math.Max(_study.Monitoring.RecenterDistanceThresholdUnits, 0.01d), 0d, 1d) * 100d;
        RecenterDistanceLabel = $"{distance.Value:0.000} u";
        RecenterLevel = distance.Value > _study.Monitoring.RecenterDistanceThresholdUnits
            ? OperationOutcomeKind.Warning
            : OperationOutcomeKind.Success;
        RecenterSummary = distance.Value > _study.Monitoring.RecenterDistanceThresholdUnits
            ? $"Camera drift is {distance.Value:0.000} units from the last recenter."
            : $"Camera drift is within threshold at {distance.Value:0.000} units.";
        RecenterDetail = $"Study threshold: {_study.Monitoring.RecenterDistanceThresholdUnits:0.000} units.";
    }

    private void UpdateParticlesCard()
    {
        var visibility = GetFirstValue(_study.Monitoring.ParticleVisibilityKeys);
        if (_reportedTwinState.Count == 0)
        {
            ParticlesLevel = OperationOutcomeKind.Preview;
            ParticlesSummary = "Waiting for runtime particle state.";
            ParticlesDetail = "Particle visibility reporting appears only if the study APK publishes it.";
            return;
        }

        if (string.IsNullOrWhiteSpace(visibility))
        {
            ParticlesLevel = CanToggleParticles ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview;
            ParticlesSummary = "Particle visibility state not exposed yet.";
            ParticlesDetail = CanToggleParticles
                ? "The study shell can still send particle visibility commands even though the current runtime is not reporting the resulting state."
                : "The current public runtime does not expose particle visibility commands or public state keys yet.";
            return;
        }

        ParticlesLevel = OperationOutcomeKind.Success;
        ParticlesSummary = $"Particles currently report `{visibility}`.";
        ParticlesDetail = "The Sussex study shell is reading the published particle visibility state from quest_twin_state.";
    }

    private void RefreshFocusRows()
    {
        FocusRows.Clear();

        foreach (var row in BuildFocusRows())
        {
            FocusRows.Add(row);
        }
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
            CreateStudyRow("Target stream name", _study.Monitoring.LslStreamNameKeys, _study.Monitoring.ExpectedLslStreamName, "Study-configured stream name."),
            CreateStudyRow("Target stream type", _study.Monitoring.LslStreamTypeKeys, _study.Monitoring.ExpectedLslStreamType, "Study-configured stream type."),
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
            CreateStudyRow("Recenter distance", _study.Monitoring.RecenterDistanceKeys, _study.Monitoring.RecenterDistanceThresholdUnits.ToString("0.000", CultureInfo.InvariantCulture), "Distance from the last recenter point."),
            CreateStudyRow("Particle visibility", _study.Monitoring.ParticleVisibilityKeys, string.Empty, "Published particle visibility state."),
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

    private static double? ParseDouble(string? value)
        => double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed)
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

    private void AppendLog(OperatorLogLevel level, string message, string detail)
    {
        Logs.Insert(0, new OperatorLogEntry(DateTimeOffset.Now, level, message, detail));
        while (Logs.Count > 50)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
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

public sealed record StudyValueSection(string Id, string Label, string Description)
{
    public override string ToString() => Label;
}

public sealed record StudyStatusRow(
    string Label,
    string Key,
    string Value,
    string Expected,
    string Detail,
    OperationOutcomeKind Level);
