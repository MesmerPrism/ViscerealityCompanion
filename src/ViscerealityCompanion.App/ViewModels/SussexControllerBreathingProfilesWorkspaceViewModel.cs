using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App.ViewModels;

public sealed class SussexControllerBreathingProfilesWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly StudyShellDefinition _study;
    private readonly IQuestControlService _questService;
    private readonly RuntimeConfigWriter _runtimeConfigWriter = new();
    private readonly SussexControllerBreathingTuningCompiler? _compiler;
    private readonly SussexControllerBreathingProfileStore? _profileStore;
    private readonly SussexControllerBreathingProfileApplyStateStore? _applyStateStore;
    private readonly SussexControllerBreathingProfileStartupStateStore? _startupStateStore;
    private readonly DispatcherTimer? _persistTimer;

    private bool _initialized;
    private bool _suppressEditorPersistence;
    private bool _persistPending;
    private IReadOnlyDictionary<string, string> _reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private SussexControllerBreathingProfileApplyRecord? _lastApplyRecord;
    private SussexControllerBreathingProfileListItemViewModel? _selectedProfile;
    private string _selectedProfileName = string.Empty;
    private string _selectedProfileNotes = string.Empty;
    private string _librarySummary = "Loading Sussex controller-breathing profiles...";
    private string _libraryDetail = "The Sussex shell stores one self-describing json file per controller-breathing profile.";
    private OperationOutcomeKind _libraryLevel = OperationOutcomeKind.Preview;
    private string _applySummary = "No Sussex controller-breathing profile has been applied yet.";
    private string _applyDetail = "Select or create a profile, then upload it through the normal Sussex hotload path.";
    private OperationOutcomeKind _applyLevel = OperationOutcomeKind.Preview;
    private string _lastCompiledCsvPath = "No Sussex controller-breathing hotload CSV written yet.";
    private string _templatePathLabel = "Bundled Sussex controller-breathing tuning template not found.";
    private string _libraryRootLabel = string.Empty;
    private SussexControllerBreathingProfileStartupState? _startupState;

    public SussexControllerBreathingProfilesWorkspaceViewModel(
        StudyShellDefinition study,
        IQuestControlService questService)
    {
        _study = study;
        _questService = questService;

        try
        {
            string? templatePath = AppAssetLocator.TryResolveSussexControllerBreathingTuningTemplatePath();
            if (!string.IsNullOrWhiteSpace(templatePath))
            {
                var templateJson = File.ReadAllText(templatePath);
                _compiler = new SussexControllerBreathingTuningCompiler(templateJson);
                _profileStore = new SussexControllerBreathingProfileStore(_compiler);
                _applyStateStore = new SussexControllerBreathingProfileApplyStateStore(_study.Id);
                _startupStateStore = new SussexControllerBreathingProfileStartupStateStore(_study.Id);
                _lastApplyRecord = _applyStateStore.Load();
                _startupState = _startupStateStore.Load();
                _templatePathLabel = templatePath;
                _libraryRootLabel = _profileStore.RootPath;
                BuildEditorGroups();
                _librarySummary = "Sussex controller-breathing profile library ready.";
                _libraryDetail = "Create or import named controller-breathing profiles, then apply them through the existing file hotload path.";
                _libraryLevel = OperationOutcomeKind.Success;
            }
            else
            {
                _librarySummary = "Sussex controller-breathing profile template missing.";
                _libraryDetail = "The shell could not resolve the bundled sussex-controller-breathing-tuning-v1.template.json asset.";
                _libraryLevel = OperationOutcomeKind.Warning;
            }
        }
        catch (Exception ex)
        {
            _librarySummary = "Sussex controller-breathing profile library unavailable.";
            _libraryDetail = ex.Message;
            _libraryLevel = OperationOutcomeKind.Failure;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            _persistTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _persistTimer.Tick += OnPersistTimerTick;
        }

        NewFromBaselineCommand = new AsyncRelayCommand(NewFromBaselineAsync, () => IsAvailable);
        DuplicateSelectedCommand = new AsyncRelayCommand(DuplicateSelectedAsync, () => SelectedProfile is not null && IsAvailable);
        RenameSelectedCommand = new AsyncRelayCommand(RenameSelectedAsync, () => SelectedProfile is not null && !string.IsNullOrWhiteSpace(SelectedProfileName) && IsAvailable);
        ImportProfileCommand = new AsyncRelayCommand(ImportProfileAsync, () => IsAvailable);
        ExportSelectedCommand = new AsyncRelayCommand(ExportSelectedAsync, () => SelectedProfile is not null && IsAvailable);
        SetStartupProfileCommand = new AsyncRelayCommand(SetStartupProfileAsync, () => SelectedProfile is not null && !IsSelectedProfileStartupDefault && IsAvailable);
        UseBundledStartupCommand = new AsyncRelayCommand(UseBundledStartupAsync, () => HasPinnedStartupProfile && IsAvailable);
        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync, () => IsAvailable && !string.IsNullOrWhiteSpace(LibraryRootLabel));
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => SelectedProfile is not null && IsAvailable);
        ApplySelectedCommand = new AsyncRelayCommand(ApplySelectedAsync, () => SelectedProfile is not null && IsAvailable);
        ResetFieldCommand = new AsyncRelayCommand(ResetFieldAsync, parameter => parameter is SussexControllerBreathingProfileFieldViewModel && IsAvailable);
    }

    public bool IsAvailable => _compiler is not null && _profileStore is not null;

    public ObservableCollection<SussexControllerBreathingProfileListItemViewModel> Profiles { get; } = new();
    public ObservableCollection<SussexControllerBreathingProfileGroupViewModel> Groups { get; } = new();
    public ObservableCollection<SussexControllerBreathingComparisonRowViewModel> ComparisonRows { get; } = new();

    public string LibrarySummary
    {
        get => _librarySummary;
        private set => SetProperty(ref _librarySummary, value);
    }

    public string LibraryDetail
    {
        get => _libraryDetail;
        private set => SetProperty(ref _libraryDetail, value);
    }

    public OperationOutcomeKind LibraryLevel
    {
        get => _libraryLevel;
        private set => SetProperty(ref _libraryLevel, value);
    }

    public string ApplySummary
    {
        get => _applySummary;
        private set => SetProperty(ref _applySummary, value);
    }

    public string ApplyDetail
    {
        get => _applyDetail;
        private set => SetProperty(ref _applyDetail, value);
    }

    public OperationOutcomeKind ApplyLevel
    {
        get => _applyLevel;
        private set => SetProperty(ref _applyLevel, value);
    }

    public string LastCompiledCsvPath
    {
        get => _lastCompiledCsvPath;
        private set => SetProperty(ref _lastCompiledCsvPath, value);
    }

    public string TemplatePathLabel
    {
        get => _templatePathLabel;
        private set => SetProperty(ref _templatePathLabel, value);
    }

    public string LibraryRootLabel
    {
        get => _libraryRootLabel;
        private set => SetProperty(ref _libraryRootLabel, value);
    }

    public SussexControllerBreathingProfileListItemViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value))
            {
                return;
            }

            FlushPendingCurrentProfileSave();
            if (SetProperty(ref _selectedProfile, value))
            {
                LoadSelectedProfileIntoEditor(value);
                NotifyStartupStateChanged();
                RaiseCommandStates();
            }
        }
    }

    public string SelectedProfileName
    {
        get => _selectedProfileName;
        set
        {
            if (SetProperty(ref _selectedProfileName, value))
            {
                SchedulePersist();
                RaiseCommandStates();
                RefreshComparisonState();
            }
        }
    }

    public string SelectedProfileNotes
    {
        get => _selectedProfileNotes;
        set
        {
            if (SetProperty(ref _selectedProfileNotes, value))
            {
                SchedulePersist();
            }
        }
    }

    public bool HasPinnedStartupProfile => _startupState is not null;

    public bool IsSelectedProfileStartupDefault
        => TryCreateCurrentDocument(out var selectedDocument, out _) &&
           SelectedProfile is not null &&
           SussexControllerBreathingStartupSnapshotResolver.MatchesCurrentSelection(
               _compiler!,
               _startupState,
               SelectedProfile.Id,
               selectedDocument!,
               ResolvePinnedStartupProfile()?.Document);

    public string StartupProfileSummary
        => _startupState is null
            ? "Next-launch override: bundled Sussex controller-breathing baseline."
            : $"Next-launch override: {_startupState.ProfileName}.";

    public string StartupProfileDetail
        => _startupState is null
            ? "Sussex launches on the bundled controller-breathing baseline. Use the startup button to save the current editor values for future Sussex launches from this shell. The override may land a few seconds after Sussex starts. Applying values during the current session does not change that launch override."
            : $"This shell will auto-apply the saved launch override '{_startupState.ProfileName}' when you launch the Sussex APK here. The runtime may briefly show the bundled baseline before the override lands. Applying values during the current session does not change that launch override. The runtime will still reset controller-breathing calibration after apply, so recalibrate on-headset before a participant run.";

    public string StartupProfileActionLabel
        => IsSelectedProfileStartupDefault
            ? "Current Values Saved For Next Launch"
            : "Save Current Values For Next Launch";

    public AsyncRelayCommand NewFromBaselineCommand { get; }
    public AsyncRelayCommand DuplicateSelectedCommand { get; }
    public AsyncRelayCommand RenameSelectedCommand { get; }
    public AsyncRelayCommand ImportProfileCommand { get; }
    public AsyncRelayCommand ExportSelectedCommand { get; }
    public AsyncRelayCommand SetStartupProfileCommand { get; }
    public AsyncRelayCommand UseBundledStartupCommand { get; }
    public AsyncRelayCommand OpenFolderCommand { get; }
    public AsyncRelayCommand DeleteSelectedCommand { get; }
    public AsyncRelayCommand ApplySelectedCommand { get; }
    public AsyncRelayCommand ResetFieldCommand { get; }

    public async Task InitializeAsync()
    {
        if (_initialized || !IsAvailable)
        {
            return;
        }

        _initialized = true;
        await ReloadProfilesAsync();
        RefreshComparisonState(syncCurrentValueText: true);
    }

    public void RefreshReportedTwinState(IReadOnlyDictionary<string, string> reportedTwinState)
    {
        _reportedTwinState = new Dictionary<string, string>(reportedTwinState, StringComparer.OrdinalIgnoreCase);
        RefreshComparisonState();
    }

    public SussexControllerBreathingSessionSnapshot CaptureSessionSnapshot()
    {
        var currentProfile = CreateCurrentProfileRecord();
        var selectedMatchesLastApplied = currentProfile is not null &&
                                         _lastApplyRecord is not null &&
                                         string.Equals(currentProfile.Id, _lastApplyRecord.ProfileId, StringComparison.OrdinalIgnoreCase);
        var reportedValues = _compiler is null
            ? new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, double?>(
                _compiler.ExtractReportedValues(_reportedTwinState),
                StringComparer.OrdinalIgnoreCase);

        var hasUnappliedEdits = false;
        if (_compiler is not null && _lastApplyRecord is not null && selectedMatchesLastApplied)
        {
            var fields = Groups.SelectMany(group => group.Fields).ToArray();
            var confirmationRows = _compiler
                .EvaluateConfirmation(_lastApplyRecord, _reportedTwinState)
                .Rows
                .ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
            var computation = SussexControllerBreathingRowConfirmationResolver.Compute(fields, _lastApplyRecord, confirmationRows);
            hasUnappliedEdits = computation.ChangedSinceApplyCount > 0;
        }

        return new SussexControllerBreathingSessionSnapshot(
            IsAvailable,
            ApplyLevel,
            ApplySummary,
            ApplyDetail,
            currentProfile,
            ResolveEffectiveProfileRecord(currentProfile),
            _startupState,
            _lastApplyRecord,
            reportedValues,
            selectedMatchesLastApplied,
            hasUnappliedEdits);
    }

    public void Dispose()
    {
        if (_persistTimer is not null)
        {
            _persistTimer.Tick -= OnPersistTimerTick;
            _persistTimer.Stop();
        }
    }

    private void BuildEditorGroups()
    {
        Groups.Clear();
        if (_compiler is null)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in _compiler.TemplateDocument.Controls)
        {
            if (!seen.Add(control.Group))
            {
                continue;
            }

            var group = new SussexControllerBreathingProfileGroupViewModel(control.Group);
            foreach (var groupedControl in _compiler.TemplateDocument.Controls.Where(candidate =>
                         string.Equals(candidate.Group, control.Group, StringComparison.OrdinalIgnoreCase)))
            {
                group.Fields.Add(new SussexControllerBreathingProfileFieldViewModel(groupedControl, OnFieldValueChanged));
            }

            Groups.Add(group);
        }
    }

    private async Task ReloadProfilesAsync(string? selectProfileId = null)
    {
        if (_profileStore is null)
        {
            return;
        }

        var profiles = await _profileStore.LoadAllAsync().ConfigureAwait(false);
        await DispatchAsync(() =>
        {
            Profiles.Clear();
            foreach (var profile in profiles)
            {
                Profiles.Add(new SussexControllerBreathingProfileListItemViewModel(profile));
            }

            if (Profiles.Count == 0)
            {
                SelectedProfile = null;
                _startupState = null;
                _startupStateStore?.Save(null);
                LibrarySummary = "No Sussex controller-breathing profiles saved yet.";
                LibraryDetail = $"Use New From Baseline to create the first profile in {LibraryRootLabel}.";
                LibraryLevel = OperationOutcomeKind.Preview;
                NotifyStartupStateChanged();
                return;
            }

            SelectedProfile = Profiles.FirstOrDefault(profile =>
                                 string.Equals(profile.Id, selectProfileId, StringComparison.OrdinalIgnoreCase))
                             ?? Profiles[0];

            if (_startupState is not null &&
                Profiles.Any(profile => string.Equals(profile.Id, _startupState.ProfileId, StringComparison.OrdinalIgnoreCase)) is false)
            {
                _startupState = null;
                _startupStateStore?.Save(null);
            }

            LibrarySummary = $"Loaded {Profiles.Count.ToString(CultureInfo.InvariantCulture)} Sussex controller-breathing profile(s).";
            LibraryDetail = $"Profiles live in {LibraryRootLabel}. The selected profile can be applied over the existing Sussex hotload file path, and the saved next-launch override is shown on the right.";
            LibraryLevel = OperationOutcomeKind.Success;
            NotifyStartupStateChanged();
        }).ConfigureAwait(false);
    }

    private void LoadSelectedProfileIntoEditor(SussexControllerBreathingProfileListItemViewModel? profile)
    {
        _suppressEditorPersistence = true;
        try
        {
            if (profile is null)
            {
                SelectedProfileName = string.Empty;
                SelectedProfileNotes = string.Empty;
                foreach (var field in Groups.SelectMany(group => group.Fields))
                {
                    field.ResetToBaseline(notify: false);
                    field.SetConfirmation("Not applied", OperationOutcomeKind.Preview);
                }

                ComparisonRows.Clear();
                return;
            }

            SelectedProfileName = profile.Document.Profile.Name;
            SelectedProfileNotes = profile.Document.Profile.Notes ?? string.Empty;
            var values = profile.Document.ControlValues;
            foreach (var field in Groups.SelectMany(group => group.Fields))
            {
                if (values.TryGetValue(field.Id, out var value))
                {
                    field.SetValue(value, notify: false);
                }
                else
                {
                    field.ResetToBaseline(notify: false);
                }

                field.SetConfirmation("Not applied", OperationOutcomeKind.Preview);
            }
        }
        finally
        {
            _suppressEditorPersistence = false;
        }

        RefreshComparisonState();
    }

    private void OnFieldValueChanged()
    {
        SchedulePersist();
        RefreshComparisonState(syncCurrentValueText: true);
        NotifyStartupStateChanged();
    }

    private SussexControllerBreathingProfileRecord? CreateCurrentProfileRecord()
    {
        if (SelectedProfile is null)
        {
            return null;
        }

        if (_compiler is null)
        {
            return SelectedProfile.ToRecord();
        }

        if (!TryCreateCurrentDocument(out var document, out _))
        {
            return null;
        }

        return SelectedProfile.ToRecord() with { Document = document };
    }

    private SussexControllerBreathingProfileRecord? ResolveEffectiveProfileRecord(
        SussexControllerBreathingProfileRecord? currentProfile)
    {
        if (_lastApplyRecord is null || _compiler is null)
        {
            return null;
        }

        if (currentProfile is not null &&
            string.Equals(currentProfile.Id, _lastApplyRecord.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return currentProfile with
            {
                FileHash = _lastApplyRecord.FileHash,
                ModifiedAtUtc = _lastApplyRecord.AppliedAtUtc,
                Document = _compiler.CreateDocument(
                    _lastApplyRecord.ProfileName,
                    currentProfile.Document.Profile.Notes,
                    _lastApplyRecord.RequestedValues)
            };
        }

        var storedProfile = Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, _lastApplyRecord.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (storedProfile is not null)
        {
            return storedProfile.ToRecord();
        }

        return new SussexControllerBreathingProfileRecord(
            _lastApplyRecord.ProfileId,
            string.Empty,
            _lastApplyRecord.FileHash,
            _lastApplyRecord.AppliedAtUtc,
            _compiler.CreateDocument(
                _lastApplyRecord.ProfileName,
                null,
                _lastApplyRecord.RequestedValues));
    }

    private void SchedulePersist()
    {
        if (_suppressEditorPersistence || SelectedProfile is null || !IsAvailable)
        {
            return;
        }

        _persistPending = true;
        if (_persistTimer is null)
        {
            _ = PersistCurrentProfileAsync();
            return;
        }

        _persistTimer.Stop();
        _persistTimer.Start();
    }

    private void FlushPendingCurrentProfileSave()
    {
        if (!_persistPending || SelectedProfile is null || !IsAvailable)
        {
            return;
        }

        _persistTimer?.Stop();
        _persistPending = false;
        var snapshot = CaptureCurrentSnapshot();
        if (snapshot is not null)
        {
            _ = PersistSnapshotAsync(snapshot);
        }
    }

    private async void OnPersistTimerTick(object? sender, EventArgs e)
    {
        _persistTimer?.Stop();
        await PersistCurrentProfileAsync().ConfigureAwait(false);
    }

    private async Task<SussexControllerBreathingProfileRecord?> PersistCurrentProfileAsync(bool forceCurrentSnapshot = false)
    {
        if (!forceCurrentSnapshot && !_persistPending)
        {
            return SelectedProfile?.ToRecord();
        }

        var snapshot = CaptureCurrentSnapshot();
        if (snapshot is null)
        {
            return null;
        }

        _persistPending = false;
        return await PersistSnapshotAsync(snapshot).ConfigureAwait(false);
    }

    private SussexControllerBreathingProfileSnapshot? CaptureCurrentSnapshot()
    {
        if (SelectedProfile is null)
        {
            return null;
        }

        return new SussexControllerBreathingProfileSnapshot(
            SelectedProfile,
            string.IsNullOrWhiteSpace(SelectedProfileName) ? SelectedProfile.Document.Profile.Name : SelectedProfileName.Trim(),
            string.IsNullOrWhiteSpace(SelectedProfileNotes) ? null : SelectedProfileNotes.Trim(),
            Groups
                .SelectMany(group => group.Fields)
                .ToDictionary(field => field.Id, field => field.Value, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<SussexControllerBreathingProfileRecord?> PersistSnapshotAsync(SussexControllerBreathingProfileSnapshot snapshot)
    {
        if (_profileStore is null)
        {
            return null;
        }

        try
        {
            var saved = await _profileStore.SaveAsync(
                snapshot.Profile.FilePath,
                snapshot.ProfileName,
                snapshot.ProfileNotes,
                snapshot.ControlValues).ConfigureAwait(false);

            await DispatchAsync(() =>
            {
                snapshot.Profile.Apply(saved);

                if (ReferenceEquals(SelectedProfile, snapshot.Profile))
                {
                    RefreshComparisonState();
                }
            }).ConfigureAwait(false);

            return saved;
        }
        catch (Exception ex)
        {
            await DispatchAsync(() =>
            {
                LibrarySummary = "Saving Sussex controller-breathing profile failed.";
                LibraryDetail = ex.Message;
                LibraryLevel = OperationOutcomeKind.Failure;
            }).ConfigureAwait(false);
            return null;
        }
    }

    private async Task NewFromBaselineAsync()
    {
        if (_profileStore is null)
        {
            return;
        }

        var created = await _profileStore.CreateFromTemplateAsync(BuildUniqueProfileName("Sussex Controller Breathing Profile")).ConfigureAwait(false);
        await ReloadProfilesAsync(created.Id).ConfigureAwait(false);
    }

    private async Task DuplicateSelectedAsync()
    {
        if (_profileStore is null || SelectedProfile is null)
        {
            return;
        }

        var source = await PersistCurrentProfileAsync(forceCurrentSnapshot: true).ConfigureAwait(false);
        if (source is null)
        {
            return;
        }

        var duplicated = await _profileStore.SaveAsync(
            existingPath: null,
            BuildUniqueProfileName(source.Document.Profile.Name + " Copy"),
            source.Document.Profile.Notes,
            source.Document.ControlValues).ConfigureAwait(false);
        await ReloadProfilesAsync(duplicated.Id).ConfigureAwait(false);
    }

    private async Task RenameSelectedAsync()
    {
        await PersistCurrentProfileAsync(forceCurrentSnapshot: true).ConfigureAwait(false);
    }

    private async Task ImportProfileAsync()
    {
        if (_profileStore is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import Sussex Controller Breathing Profile",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = !string.IsNullOrWhiteSpace(LibraryRootLabel) && Directory.Exists(LibraryRootLabel)
                ? LibraryRootLabel
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var imported = await _profileStore.ImportAsync(dialog.FileName).ConfigureAwait(false);
            await ReloadProfilesAsync(imported.Id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LibrarySummary = "Sussex controller-breathing profile import failed.";
            LibraryDetail = ex.Message;
            LibraryLevel = OperationOutcomeKind.Failure;
        }
    }

    private async Task ExportSelectedAsync()
    {
        if (_profileStore is null)
        {
            return;
        }

        var saved = await PersistCurrentProfileAsync(forceCurrentSnapshot: true).ConfigureAwait(false);
        if (saved is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Sussex Controller Breathing Profile",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = saved.Id + ".json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _profileStore.ExportAsync(saved.Document, dialog.FileName).ConfigureAwait(false);
        LibrarySummary = "Sussex controller-breathing profile exported.";
        LibraryDetail = dialog.FileName;
        LibraryLevel = OperationOutcomeKind.Success;
    }

    private Task OpenFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(LibraryRootLabel))
        {
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = LibraryRootLabel,
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    private async Task SetStartupProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var saved = await PersistCurrentProfileAsync(forceCurrentSnapshot: true).ConfigureAwait(false);
        if (saved is null)
        {
            return;
        }

        var startupState = new SussexControllerBreathingProfileStartupState(
            saved.Id,
            saved.Document.Profile.Name,
            DateTimeOffset.UtcNow,
            saved.Document.Profile.Notes,
            new Dictionary<string, double>(saved.Document.ControlValues, StringComparer.OrdinalIgnoreCase));
        _startupStateStore?.Save(startupState);
        await DispatchAsync(() =>
        {
            _startupState = startupState;
            NotifyStartupStateChanged();
            RefreshComparisonState();
        }).ConfigureAwait(false);
    }

    private Task UseBundledStartupAsync()
    {
        _startupState = null;
        _startupStateStore?.Save(null);
        NotifyStartupStateChanged();
        RefreshComparisonState();
        return Task.CompletedTask;
    }

    public async Task ApplyStartupProfileOnLaunchAsync()
    {
        SussexControllerBreathingProfileRecord? startupRecord = null;
        await DispatchAsync(() =>
        {
            startupRecord = CreateStartupProfileRecord();
        }).ConfigureAwait(false);

        if (startupRecord is null)
        {
            await DispatchAsync(() =>
            {
                ApplySummary = "APK launch is using the bundled Sussex controller-breathing baseline.";
                ApplyDetail = "No saved startup profile is pinned, so Sussex keeps the bundled controller-breathing baseline until you apply another profile.";
                ApplyLevel = OperationOutcomeKind.Preview;
                RefreshComparisonState();
            }).ConfigureAwait(false);
            return;
        }

        await ApplyProfileRecordAsync(
            startupRecord,
            "Apply Startup Controller Breathing Profile").ConfigureAwait(false);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_profileStore is null || SelectedProfile is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"Delete '{SelectedProfile.Document.Profile.Name}' from the Sussex controller-breathing profile library?",
                "Viscereality Companion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var deletedId = SelectedProfile.Id;
        if (_lastApplyRecord is not null &&
            string.Equals(_lastApplyRecord.ProfileId, deletedId, StringComparison.OrdinalIgnoreCase))
        {
            _lastApplyRecord = null;
            _applyStateStore?.Save(null);
        }

        if (_startupState is not null &&
            string.Equals(_startupState.ProfileId, deletedId, StringComparison.OrdinalIgnoreCase))
        {
            _startupState = null;
            _startupStateStore?.Save(null);
            NotifyStartupStateChanged();
        }

        await _profileStore.DeleteAsync(SelectedProfile.FilePath).ConfigureAwait(false);
        await ReloadProfilesAsync().ConfigureAwait(false);
    }

    private async Task ApplySelectedAsync()
    {
        if (_compiler is null || SelectedProfile is null)
        {
            return;
        }

        var saved = await PersistCurrentProfileAsync(forceCurrentSnapshot: true).ConfigureAwait(false);
        if (saved is null)
        {
            return;
        }

        await ApplyProfileRecordAsync(saved, "Apply Controller Breathing Profile").ConfigureAwait(false);
    }

    private async Task ApplyProfileRecordAsync(
        SussexControllerBreathingProfileRecord saved,
        string actionLabel)
    {
        if (_compiler is null)
        {
            return;
        }

        try
        {
            var compiled = _compiler.Compile(saved.Document);
            var previousReportedValues = _compiler.ExtractReportedValues(_reportedTwinState);
            var runtimeProfile = new RuntimeConfigProfile(
                $"sussex_controller_breathing_tuning_v1_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}",
                $"Sussex Controller Breathing Profile - {saved.Document.Profile.Name}",
                string.Empty,
                DateTimeOffset.UtcNow.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture),
                "study",
                false,
                $"Compiled from {saved.Document.Profile.Name}. Only the Sussex-approved controller-breathing approximation fields were changed.",
                [saved.Document.PackageId],
                compiled.Entries.ToArray());

            var csvPath = await _runtimeConfigWriter.WriteAsync(runtimeProfile).ConfigureAwait(false);
            LastCompiledCsvPath = csvPath;

            var hotloadProfile = new HotloadProfile(
                runtimeProfile.Id,
                runtimeProfile.Label,
                csvPath,
                runtimeProfile.Version,
                runtimeProfile.Channel,
                runtimeProfile.StudyLock,
                runtimeProfile.Description,
                runtimeProfile.PackageIds);

            var target = new QuestAppTarget(
                _study.Id,
                _study.App.Label,
                _study.App.PackageId,
                _study.App.ApkPath,
                _study.App.LaunchComponent,
                string.Empty,
                _study.Description,
                []);

            var headsetStatus = await _questService.QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false).ConfigureAwait(false);
            if (!headsetStatus.IsConnected)
            {
                ApplySummary = $"{actionLabel} blocked.";
                ApplyDetail = $"{headsetStatus.Detail} The CSV was compiled locally at {csvPath}, but it was not uploaded.";
                ApplyLevel = OperationOutcomeKind.Failure;
                RefreshComparisonState();
                return;
            }

            OperationOutcome? wakeOutcome = null;
            if (headsetStatus.IsAwake != true || headsetStatus.IsInWakeLimbo)
            {
                wakeOutcome = await _questService.RunUtilityAsync(
                    QuestUtilityAction.Wake,
                    allowWakeResumeTarget: false).ConfigureAwait(false);

                if (wakeOutcome.Kind == OperationOutcomeKind.Failure)
                {
                    ApplySummary = $"{actionLabel} blocked by headset wake failure.";
                    ApplyDetail = BuildWakeFailureDetail(wakeOutcome, csvPath);
                    ApplyLevel = OperationOutcomeKind.Failure;
                    RefreshComparisonState();
                    return;
                }

                headsetStatus = await _questService.QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false).ConfigureAwait(false);
                if (headsetStatus.IsAwake != true || headsetStatus.IsInWakeLimbo)
                {
                    ApplySummary = $"{actionLabel} is waiting for an awake headset.";
                    ApplyDetail = BuildHeadsetNotReadyDetail(headsetStatus, wakeOutcome, csvPath);
                    ApplyLevel = OperationOutcomeKind.Warning;
                    RefreshComparisonState();
                    return;
                }
            }

            var outcome = await _questService.ApplyHotloadProfileAsync(hotloadProfile, target).ConfigureAwait(false);
            if (outcome.Kind != OperationOutcomeKind.Failure)
            {
                _lastApplyRecord = new SussexControllerBreathingProfileApplyRecord(
                    saved.Id,
                    saved.Document.Profile.Name,
                    saved.FileHash,
                    ComputeEntriesHash(compiled.Entries),
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, double>(saved.Document.ControlValues, StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, double?>(previousReportedValues, StringComparer.OrdinalIgnoreCase));
                _applyStateStore?.Save(_lastApplyRecord);
            }

            ApplySummary = outcome.Summary;
            ApplyDetail = BuildApplyOutcomeDetail(outcome, headsetStatus, csvPath, wakeOutcome);
            ApplyLevel = outcome.Kind;
            RefreshComparisonState();
        }
        catch (Exception ex)
        {
            ApplySummary = $"{actionLabel} failed.";
            ApplyDetail = BuildApplyExceptionDetail(ex);
            ApplyLevel = OperationOutcomeKind.Failure;
            RefreshComparisonState();
        }
    }

    private Task ResetFieldAsync(object? parameter)
    {
        if (parameter is SussexControllerBreathingProfileFieldViewModel field)
        {
            field.ResetToBaseline(notify: true);
        }

        return Task.CompletedTask;
    }

    private void RefreshComparisonState(bool syncCurrentValueText = false)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => RefreshComparisonState(syncCurrentValueText));
            return;
        }

        try
        {
            if (_compiler is null || SelectedProfile is null)
            {
                ComparisonRows.Clear();
                return;
            }

            if (!TryCreateCurrentDocument(out var selectedDocument, out var editorError))
            {
                ComparisonRows.Clear();
                ApplySummary = "Current controller-breathing values are invalid.";
                ApplyDetail = $"{editorError} Adjust the edited rows or use Reset before applying or saving them for the next Sussex launch.";
                ApplyLevel = OperationOutcomeKind.Warning;
                foreach (var field in Groups.SelectMany(group => group.Fields))
                {
                    field.SetConfirmation("Edited", OperationOutcomeKind.Warning);
                }

                return;
            }

            var startupComparisonDocument = ResolveStartupComparisonDocument();
            var fieldsById = Groups
                .SelectMany(group => group.Fields)
                .ToDictionary(field => field.Id, StringComparer.OrdinalIgnoreCase);
            var rows = _compiler.BuildComparisonRows(selectedDocument!, startupComparisonDocument);
            var selectedMatchesLastApplyProfile = _lastApplyRecord is not null &&
                string.Equals(SelectedProfile.Id, _lastApplyRecord.ProfileId, StringComparison.OrdinalIgnoreCase);

            IReadOnlyDictionary<string, SussexControllerBreathingConfirmationRow>? confirmationRows = null;
            var rowConfirmationStates = new Dictionary<string, SussexControllerBreathingRowConfirmationState>(StringComparer.OrdinalIgnoreCase);
            var changedSinceApplyCount = 0;
            var unchangedSinceApplyCount = 0;
            if (selectedMatchesLastApplyProfile)
            {
                var confirmation = _compiler.EvaluateConfirmation(_lastApplyRecord!, _reportedTwinState);
                confirmationRows = confirmation.Rows.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
                var computation = SussexControllerBreathingRowConfirmationResolver.Compute(fieldsById.Values, _lastApplyRecord!, confirmationRows);
                rowConfirmationStates = new Dictionary<string, SussexControllerBreathingRowConfirmationState>(computation.States, StringComparer.OrdinalIgnoreCase);
                changedSinceApplyCount = computation.ChangedSinceApplyCount;
                unchangedSinceApplyCount = computation.UnchangedSinceApplyCount;

                if (changedSinceApplyCount > 0)
                {
                    ApplySummary = changedSinceApplyCount == 1
                        ? "1 controller-tuning value changed since the last apply."
                        : $"{changedSinceApplyCount.ToString(CultureInfo.InvariantCulture)} controller-tuning values changed since the last apply.";
                    ApplyDetail = unchangedSinceApplyCount > 0
                        ? $"Apply again to request the edited values, refresh headset confirmation, and reset controller calibration. The other {unchangedSinceApplyCount.ToString(CultureInfo.InvariantCulture)} row(s) still show the last headset confirmation for {_lastApplyRecord!.ProfileName}."
                        : "Apply again to request the edited values, refresh headset confirmation, and reset controller calibration to the new tuning envelope.";
                    ApplyLevel = OperationOutcomeKind.Warning;
                }
                else
                {
                    ApplySummary = confirmation.Summary;
                    ApplyDetail = confirmation.WaitingCount > 0
                        ? $"Applied {_lastApplyRecord!.ProfileName} at {_lastApplyRecord.AppliedAtUtc.ToLocalTime():HH:mm:ss}. Upload succeeded; waiting for a fresh quest_twin_state report to mirror the controller-breathing hotload values."
                        : $"Applied {_lastApplyRecord!.ProfileName} at {_lastApplyRecord.AppliedAtUtc.ToLocalTime():HH:mm:ss}. Recalibrate controller breathing on the headset before the participant run.";
                    ApplyLevel = confirmation.MismatchCount > 0
                        ? OperationOutcomeKind.Warning
                        : confirmation.WaitingCount > 0
                            ? OperationOutcomeKind.Warning
                            : OperationOutcomeKind.Success;
                }
            }
            else if (_lastApplyRecord is not null)
            {
                ApplySummary = $"Last applied profile: {_lastApplyRecord.ProfileName}.";
                ApplyDetail = "Select that profile to inspect per-field confirmation, or apply the current profile.";
                ApplyLevel = OperationOutcomeKind.Success;
            }
            else
            {
                ApplySummary = "No Sussex controller-breathing profile has been applied yet.";
                ApplyDetail = "Apply the current profile to start headset confirmation tracking.";
                ApplyLevel = OperationOutcomeKind.Preview;
            }

            if (ComparisonRows.Count != rows.Count ||
                ComparisonRows.Select(row => row.Field.Id).SequenceEqual(rows.Select(row => row.Id), StringComparer.OrdinalIgnoreCase) is false)
            {
                ComparisonRows.Clear();
                foreach (var row in rows)
                {
                    if (!fieldsById.TryGetValue(row.Id, out var field))
                    {
                        continue;
                    }

                    ComparisonRows.Add(new SussexControllerBreathingComparisonRowViewModel(
                        field,
                        row,
                        ResolveRowConfirmationState(rowConfirmationStates, row.Id)));
                }
            }
            else
            {
                for (var index = 0; index < rows.Count; index++)
                {
                    ComparisonRows[index].Apply(
                        rows[index],
                        ResolveRowConfirmationState(rowConfirmationStates, rows[index].Id),
                        syncCurrentValueText);
                }
            }

            foreach (var field in Groups.SelectMany(group => group.Fields))
            {
                var state = ResolveRowConfirmationState(rowConfirmationStates, field.Id);
                field.SetConfirmation(state.Label, state.Level);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or InvalidOperationException)
        {
            ComparisonRows.Clear();
            ApplySummary = "Current controller-breathing values could not be compared safely.";
            ApplyDetail = $"{ex.Message} Adjust the edited rows or refresh the live runtime state before applying or saving them for the next Sussex launch.";
            ApplyLevel = OperationOutcomeKind.Warning;
            foreach (var field in Groups.SelectMany(group => group.Fields))
            {
                field.SetConfirmation("Edited", OperationOutcomeKind.Warning);
            }
        }
    }

    private static SussexControllerBreathingRowConfirmationState ResolveRowConfirmationState(
        IReadOnlyDictionary<string, SussexControllerBreathingRowConfirmationState> states,
        string fieldId)
        => states.TryGetValue(fieldId, out var state)
            ? state
            : SussexControllerBreathingRowConfirmationResolver.DefaultConfirmationState;

    private string BuildUniqueProfileName(string baseName)
    {
        var seed = string.IsNullOrWhiteSpace(baseName) ? "Sussex Controller Breathing Profile" : baseName.Trim();
        if (Profiles.All(profile => !string.Equals(profile.Document.Profile.Name, seed, StringComparison.OrdinalIgnoreCase)))
        {
            return seed;
        }

        var suffix = 2;
        while (true)
        {
            var candidate = $"{seed} {suffix}";
            if (Profiles.All(profile => !string.Equals(profile.Document.Profile.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private void RaiseCommandStates()
    {
        DuplicateSelectedCommand.RaiseCanExecuteChanged();
        RenameSelectedCommand.RaiseCanExecuteChanged();
        ExportSelectedCommand.RaiseCanExecuteChanged();
        SetStartupProfileCommand.RaiseCanExecuteChanged();
        UseBundledStartupCommand.RaiseCanExecuteChanged();
        DeleteSelectedCommand.RaiseCanExecuteChanged();
        ApplySelectedCommand.RaiseCanExecuteChanged();
    }

    private SussexControllerBreathingProfileListItemViewModel? ResolvePinnedStartupProfile()
        => _startupState is null
            ? null
            : Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, _startupState.ProfileId, StringComparison.OrdinalIgnoreCase));

    private SussexControllerBreathingTuningDocument ResolveStartupComparisonDocument()
    {
        if (_compiler is null)
        {
            throw new InvalidOperationException("Sussex controller-breathing tuning compiler is not available.");
        }

        return SussexControllerBreathingStartupSnapshotResolver.ResolveDocument(
            _compiler,
            _startupState,
            ResolvePinnedStartupProfile()?.Document);
    }

    private SussexControllerBreathingProfileRecord? CreateStartupProfileRecord()
    {
        if (_compiler is null || _startupState is null)
        {
            return ResolvePinnedStartupProfile()?.ToRecord();
        }

        SussexControllerBreathingTuningDocument startupDocument;
        try
        {
            startupDocument = ResolveStartupComparisonDocument();
        }
        catch (InvalidDataException)
        {
            return ResolvePinnedStartupProfile()?.ToRecord();
        }

        var payload = _compiler.Serialize(startupDocument);
        return new SussexControllerBreathingProfileRecord(
            _startupState.ProfileId,
            string.Empty,
            ComputeTextHash(payload),
            _startupState.UpdatedAtUtc,
            startupDocument);
    }

    private bool TryCreateCurrentDocument(
        out SussexControllerBreathingTuningDocument? document,
        out string? error)
    {
        if (_compiler is null || SelectedProfile is null)
        {
            document = null;
            error = null;
            return false;
        }

        return SussexControllerBreathingCurrentDocumentResolver.TryCreate(
            _compiler,
            SelectedProfileName,
            SelectedProfileNotes,
            Groups.SelectMany(group => group.Fields),
            out document,
            out error);
    }

    private void NotifyStartupStateChanged()
    {
        OnPropertyChanged(nameof(HasPinnedStartupProfile));
        OnPropertyChanged(nameof(IsSelectedProfileStartupDefault));
        OnPropertyChanged(nameof(StartupProfileSummary));
        OnPropertyChanged(nameof(StartupProfileDetail));
        OnPropertyChanged(nameof(StartupProfileActionLabel));
        RaiseCommandStates();
    }

    private static string ComputeEntriesHash(IReadOnlyList<RuntimeConfigEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.Append(entry.Key);
            builder.Append('=');
            builder.Append(entry.Value);
            builder.AppendLine();
        }

        byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static string ComputeTextHash(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static string BuildApplyOutcomeDetail(
        OperationOutcome outcome,
        HeadsetAppStatus headsetStatus,
        string csvPath,
        OperationOutcome? wakeOutcome)
    {
        var detailParts = new List<string>();
        if (wakeOutcome is not null && !string.IsNullOrWhiteSpace(wakeOutcome.Detail))
        {
            detailParts.Add(wakeOutcome.Detail);
        }

        if (!string.IsNullOrWhiteSpace(outcome.Detail))
        {
            detailParts.Add(outcome.Detail);
        }

        detailParts.Add($"Compiled CSV: {csvPath}.");

        if (!headsetStatus.IsTargetForeground)
        {
            detailParts.Add(
                headsetStatus.IsTargetRunning
                    ? "The Sussex APK is running but not currently foreground, so the controller-breathing change may not be immediately visible."
                    : "The Sussex APK is not currently running in foreground. The staged file should load the next time the runtime starts.");
        }

        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            detailParts.Add("The runtime resets controller-breathing calibration after apply. Recalibrate on the headset, then use twin-state confirmation to confirm the new hotload values.");
        }

        return string.Join(" ", detailParts);
    }

    private static string BuildHeadsetNotReadyDetail(
        HeadsetAppStatus headsetStatus,
        OperationOutcome? wakeOutcome,
        string csvPath)
    {
        var detailParts = new List<string>();
        if (wakeOutcome is not null && !string.IsNullOrWhiteSpace(wakeOutcome.Detail))
        {
            detailParts.Add(wakeOutcome.Detail);
        }

        if (!string.IsNullOrWhiteSpace(headsetStatus.Detail))
        {
            detailParts.Add(headsetStatus.Detail);
        }

        detailParts.Add($"Compiled CSV: {csvPath}.");
        detailParts.Add("The Sussex runtime must be awake and visible enough to poll runtime_hotload/runtime_overrides.csv before a controller-breathing apply can take effect. Wake the headset or use the bench-tools proximity hold, then apply again.");
        return string.Join(" ", detailParts);
    }

    private static string BuildWakeFailureDetail(OperationOutcome wakeOutcome, string csvPath)
    {
        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(wakeOutcome.Detail))
        {
            detailParts.Add(wakeOutcome.Detail);
        }

        detailParts.Add($"Compiled CSV: {csvPath}.");
        detailParts.Add("The profile was not uploaded because the Sussex runtime could not be brought to an awake state first.");
        return string.Join(" ", detailParts);
    }

    private static string BuildApplyExceptionDetail(Exception exception)
    {
        if (exception is InvalidOperationException &&
            exception.Message.Contains("USB ADB device", StringComparison.OrdinalIgnoreCase))
        {
            return "No active Quest transport was available. Run Probe USB or Connect Quest in the Sussex shell first, then apply the profile again.";
        }

        return exception.Message;
    }

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
}
