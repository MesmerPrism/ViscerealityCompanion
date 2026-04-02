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

public sealed class SussexVisualProfilesWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly StudyShellDefinition _study;
    private readonly IQuestControlService _questService;
    private readonly RuntimeConfigWriter _runtimeConfigWriter = new();
    private readonly SussexVisualTuningCompiler? _compiler;
    private readonly SussexVisualProfileStore? _profileStore;
    private readonly SussexVisualProfileApplyStateStore? _applyStateStore;
    private readonly DispatcherTimer? _persistTimer;

    private bool _initialized;
    private bool _suppressEditorPersistence;
    private bool _persistPending;
    private string? _reportedRuntimeConfigJson;
    private SussexVisualProfileApplyRecord? _lastApplyRecord;
    private SussexVisualProfileListItemViewModel? _selectedProfile;
    private SussexVisualProfileListItemViewModel? _compareProfile;
    private string _selectedProfileName = string.Empty;
    private string _selectedProfileNotes = string.Empty;
    private string _librarySummary = "Loading Sussex visual profiles...";
    private string _libraryDetail = "The Sussex shell stores one self-describing json file per visual profile.";
    private OperationOutcomeKind _libraryLevel = OperationOutcomeKind.Preview;
    private string _applySummary = "No Sussex visual profile has been applied yet.";
    private string _applyDetail = "Select or create a profile, then upload it through the normal Sussex hotload path.";
    private OperationOutcomeKind _applyLevel = OperationOutcomeKind.Preview;
    private string _lastCompiledCsvPath = "No Sussex visual hotload CSV written yet.";
    private string _templatePathLabel = "Bundled Sussex visual tuning template not found.";
    private string _libraryRootLabel = string.Empty;

    public SussexVisualProfilesWorkspaceViewModel(
        StudyShellDefinition study,
        IQuestControlService questService)
    {
        _study = study;
        _questService = questService;

        try
        {
            string? templatePath = AppAssetLocator.TryResolveSussexVisualTuningTemplatePath();
            if (!string.IsNullOrWhiteSpace(templatePath))
            {
                var templateJson = File.ReadAllText(templatePath);
                _compiler = new SussexVisualTuningCompiler(templateJson);
                _profileStore = new SussexVisualProfileStore(_compiler);
                _applyStateStore = new SussexVisualProfileApplyStateStore(_study.Id);
                _lastApplyRecord = _applyStateStore.Load();
                _templatePathLabel = templatePath;
                _libraryRootLabel = _profileStore.RootPath;
                BuildEditorGroups();
                _librarySummary = "Sussex visual profile library ready.";
                _libraryDetail = "Create or import named visual profiles, then apply them through the existing file hotload path.";
                _libraryLevel = OperationOutcomeKind.Success;
            }
            else
            {
                _librarySummary = "Sussex visual profile template missing.";
                _libraryDetail = "The shell could not resolve the bundled sussex-visual-tuning-v1.template.json asset.";
                _libraryLevel = OperationOutcomeKind.Warning;
            }
        }
        catch (Exception ex)
        {
            _librarySummary = "Sussex visual profile library unavailable.";
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
        ClearCompareCommand = new AsyncRelayCommand(ClearCompareAsync, () => CompareProfile is not null);
        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync, () => IsAvailable && !string.IsNullOrWhiteSpace(LibraryRootLabel));
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => SelectedProfile is not null && IsAvailable);
        ApplySelectedCommand = new AsyncRelayCommand(ApplySelectedAsync, () => SelectedProfile is not null && IsAvailable);
        ResetFieldCommand = new AsyncRelayCommand(ResetFieldAsync, parameter => parameter is SussexVisualProfileFieldViewModel && IsAvailable);
    }

    public bool IsAvailable => _compiler is not null && _profileStore is not null;

    public ObservableCollection<SussexVisualProfileListItemViewModel> Profiles { get; } = new();
    public ObservableCollection<SussexVisualProfileGroupViewModel> Groups { get; } = new();
    public ObservableCollection<SussexVisualComparisonRowViewModel> ComparisonRows { get; } = new();

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

    public SussexVisualProfileListItemViewModel? SelectedProfile
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
                RaiseCommandStates();
            }
        }
    }

    public SussexVisualProfileListItemViewModel? CompareProfile
    {
        get => _compareProfile;
        set
        {
            if (SetProperty(ref _compareProfile, value))
            {
                ClearCompareCommand.RaiseCanExecuteChanged();
                RefreshComparisonState();
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

    public AsyncRelayCommand NewFromBaselineCommand { get; }
    public AsyncRelayCommand DuplicateSelectedCommand { get; }
    public AsyncRelayCommand RenameSelectedCommand { get; }
    public AsyncRelayCommand ImportProfileCommand { get; }
    public AsyncRelayCommand ExportSelectedCommand { get; }
    public AsyncRelayCommand ClearCompareCommand { get; }
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
        if (!TryGetReportedRuntimeConfigJson(reportedTwinState, out var runtimeConfigJson))
        {
            _reportedRuntimeConfigJson = null;
        }
        else
        {
            _reportedRuntimeConfigJson = runtimeConfigJson;
        }

        RefreshComparisonState();
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

        foreach (var groupTitle in new[] { "Shape", "Size", "Depth Wave", "Transparency", "Saturation", "Brightness", "Orbit" })
        {
            var group = new SussexVisualProfileGroupViewModel(groupTitle);
            foreach (var control in _compiler.TemplateDocument.Controls.Where(control => ResolveGroupTitle(control.Id) == groupTitle))
            {
                group.Fields.Add(new SussexVisualProfileFieldViewModel(control, OnFieldValueChanged));
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

        var profiles = await _profileStore.LoadAllAsync();
        await DispatchAsync(() =>
        {
            Profiles.Clear();
            foreach (var profile in profiles)
            {
                Profiles.Add(new SussexVisualProfileListItemViewModel(profile));
            }

            if (Profiles.Count == 0)
            {
                SelectedProfile = null;
                CompareProfile = null;
                LibrarySummary = "No Sussex visual profiles saved yet.";
                LibraryDetail = $"Use New From Baseline to create the first profile in {LibraryRootLabel}.";
                LibraryLevel = OperationOutcomeKind.Preview;
                return;
            }

            SelectedProfile = Profiles.FirstOrDefault(profile =>
                                 string.Equals(profile.Id, selectProfileId, StringComparison.OrdinalIgnoreCase))
                             ?? Profiles[0];

            if (CompareProfile is not null)
            {
                CompareProfile = Profiles.FirstOrDefault(profile =>
                    string.Equals(profile.Id, CompareProfile.Id, StringComparison.OrdinalIgnoreCase));
            }

            LibrarySummary = $"Loaded {Profiles.Count.ToString(CultureInfo.InvariantCulture)} Sussex visual profile(s).";
            LibraryDetail = $"Profiles live in {LibraryRootLabel}. The selected profile can be applied over the existing Sussex hotload file path.";
            LibraryLevel = OperationOutcomeKind.Success;
        });
    }

    private void LoadSelectedProfileIntoEditor(SussexVisualProfileListItemViewModel? profile)
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
        await PersistCurrentProfileAsync();
    }

    private async Task<SussexVisualProfileRecord?> PersistCurrentProfileAsync()
    {
        if (!_persistPending)
        {
            return SelectedProfile?.ToRecord();
        }

        var snapshot = CaptureCurrentSnapshot();
        if (snapshot is null)
        {
            return null;
        }

        _persistPending = false;
        return await PersistSnapshotAsync(snapshot);
    }

    private SussexVisualProfileSnapshot? CaptureCurrentSnapshot()
    {
        if (SelectedProfile is null)
        {
            return null;
        }

        return new SussexVisualProfileSnapshot(
            SelectedProfile,
            string.IsNullOrWhiteSpace(SelectedProfileName) ? SelectedProfile.Document.Profile.Name : SelectedProfileName.Trim(),
            string.IsNullOrWhiteSpace(SelectedProfileNotes) ? null : SelectedProfileNotes.Trim(),
            Groups
                .SelectMany(group => group.Fields)
                .ToDictionary(field => field.Id, field => field.Value, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<SussexVisualProfileRecord?> PersistSnapshotAsync(SussexVisualProfileSnapshot snapshot)
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
                snapshot.ControlValues);

            await DispatchAsync(() =>
            {
                snapshot.Profile.Apply(saved);
                if (ReferenceEquals(SelectedProfile, snapshot.Profile))
                {
                    RefreshComparisonState();
                }
            });

            return saved;
        }
        catch (Exception ex)
        {
            await DispatchAsync(() =>
            {
                LibrarySummary = "Saving Sussex visual profile failed.";
                LibraryDetail = ex.Message;
                LibraryLevel = OperationOutcomeKind.Failure;
            });
            return null;
        }
    }

    private async Task NewFromBaselineAsync()
    {
        if (_profileStore is null)
        {
            return;
        }

        var created = await _profileStore.CreateFromTemplateAsync(BuildUniqueProfileName("Sussex Visual Profile"));
        await ReloadProfilesAsync(created.Id);
    }

    private async Task DuplicateSelectedAsync()
    {
        if (_profileStore is null || SelectedProfile is null)
        {
            return;
        }

        var source = await PersistCurrentProfileAsync();
        if (source is null)
        {
            return;
        }

        var duplicated = await _profileStore.SaveAsync(
            existingPath: null,
            BuildUniqueProfileName(source.Document.Profile.Name + " Copy"),
            source.Document.Profile.Notes,
            source.Document.ControlValues);
        await ReloadProfilesAsync(duplicated.Id);
    }

    private async Task RenameSelectedAsync()
    {
        await PersistCurrentProfileAsync();
    }

    private async Task ImportProfileAsync()
    {
        if (_profileStore is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import Sussex Visual Profile",
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
            var imported = await _profileStore.ImportAsync(dialog.FileName);
            await ReloadProfilesAsync(imported.Id);
        }
        catch (Exception ex)
        {
            LibrarySummary = "Sussex visual profile import failed.";
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

        var saved = await PersistCurrentProfileAsync();
        if (saved is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Sussex Visual Profile",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = saved.Id + ".json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _profileStore.ExportAsync(saved.Document, dialog.FileName);
        LibrarySummary = "Sussex visual profile exported.";
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

    private Task ClearCompareAsync()
    {
        CompareProfile = null;
        return Task.CompletedTask;
    }

    private async Task DeleteSelectedAsync()
    {
        if (_profileStore is null || SelectedProfile is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"Delete '{SelectedProfile.Document.Profile.Name}' from the Sussex visual profile library?",
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

        await _profileStore.DeleteAsync(SelectedProfile.FilePath);
        await ReloadProfilesAsync();
    }

    private async Task ApplySelectedAsync()
    {
        if (_compiler is null || SelectedProfile is null)
        {
            return;
        }

        try
        {
            var saved = await PersistCurrentProfileAsync();
            if (saved is null)
            {
                return;
            }

            var compiled = _compiler.Compile(saved.Document);
            var previousReportedValues = _compiler.ExtractRuntimeValues(_reportedRuntimeConfigJson ?? string.Empty);
            var runtimeProfile = new RuntimeConfigProfile(
                $"sussex_visual_tuning_v1_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}",
                $"Sussex Visual Profile - {saved.Document.Profile.Name}",
                string.Empty,
                DateTimeOffset.UtcNow.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture),
                "study",
                false,
                $"Compiled from {saved.Document.Profile.Name}. Only the Sussex-approved visual envelope fields were changed.",
                [saved.Document.PackageId],
                [new RuntimeConfigEntry(compiled.HotloadTargetKey, compiled.CompactRuntimeConfigJson)]);

            var csvPath = await _runtimeConfigWriter.WriteAsync(runtimeProfile);
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

            var headsetStatus = await _questService.QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false);
            if (!headsetStatus.IsConnected)
            {
                ApplySummary = headsetStatus.Summary;
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
                    ApplySummary = "Sussex visual profile apply blocked by headset wake failure.";
                    ApplyDetail = BuildWakeFailureDetail(wakeOutcome, csvPath);
                    ApplyLevel = OperationOutcomeKind.Failure;
                    RefreshComparisonState();
                    return;
                }

                headsetStatus = await _questService.QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false);
                if (headsetStatus.IsAwake != true || headsetStatus.IsInWakeLimbo)
                {
                    ApplySummary = "Sussex visual profile apply is waiting for an awake headset.";
                    ApplyDetail = BuildHeadsetNotReadyDetail(headsetStatus, wakeOutcome, csvPath);
                    ApplyLevel = OperationOutcomeKind.Warning;
                    RefreshComparisonState();
                    return;
                }
            }

            var outcome = await _questService.ApplyHotloadProfileAsync(hotloadProfile, target);
            if (outcome.Kind != OperationOutcomeKind.Failure)
            {
                _lastApplyRecord = new SussexVisualProfileApplyRecord(
                    saved.Id,
                    saved.Document.Profile.Name,
                    saved.FileHash,
                    ComputeTextHash(compiled.CompactRuntimeConfigJson),
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
            ApplySummary = "Sussex visual profile apply failed.";
            ApplyDetail = BuildApplyExceptionDetail(ex);
            ApplyLevel = OperationOutcomeKind.Failure;
            RefreshComparisonState();
        }
    }

    private Task ResetFieldAsync(object? parameter)
    {
        if (parameter is SussexVisualProfileFieldViewModel field)
        {
            field.ResetToBaseline(notify: true);
        }

        return Task.CompletedTask;
    }

    private void RefreshComparisonState(bool syncCurrentValueText = false)
    {
        if (_compiler is null || SelectedProfile is null)
        {
            ComparisonRows.Clear();
            return;
        }

        var selectedDocument = _compiler.CreateDocument(
            SelectedProfileName,
            SelectedProfileNotes,
            Groups.SelectMany(group => group.Fields)
                .ToDictionary(field => field.Id, field => field.Value, StringComparer.OrdinalIgnoreCase));
        var fieldsById = Groups
            .SelectMany(group => group.Fields)
            .ToDictionary(field => field.Id, StringComparer.OrdinalIgnoreCase);
        var compareDocument = CompareProfile?.Document;
        var rows = _compiler.BuildComparisonRows(selectedDocument, compareDocument);
        var selectedMatchesLastApply = _lastApplyRecord is not null &&
            string.Equals(SelectedProfile.Id, _lastApplyRecord.ProfileId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(SelectedProfile.FileHash, _lastApplyRecord.FileHash, StringComparison.OrdinalIgnoreCase);

        IReadOnlyDictionary<string, SussexVisualConfirmationRow>? confirmationRows = null;
        if (selectedMatchesLastApply)
        {
            var confirmation = _compiler.EvaluateConfirmation(_lastApplyRecord!, _reportedRuntimeConfigJson);
            confirmationRows = confirmation.Rows.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
            ApplySummary = confirmation.Summary;
            ApplyDetail = confirmation.WaitingCount > 0
                ? $"Applied {_lastApplyRecord!.ProfileName} at {_lastApplyRecord.AppliedAtUtc.ToLocalTime():HH:mm:ss}. Upload succeeded; waiting for a fresh quest_twin_state report to mirror showcase_active_runtime_config_json."
                : $"Applied {_lastApplyRecord!.ProfileName} at {_lastApplyRecord.AppliedAtUtc.ToLocalTime():HH:mm:ss}.";
            ApplyLevel = confirmation.MismatchCount > 0
                ? OperationOutcomeKind.Warning
                : confirmation.WaitingCount > 0
                    ? OperationOutcomeKind.Warning
                    : OperationOutcomeKind.Success;
        }
        else if (_lastApplyRecord is not null &&
                 string.Equals(SelectedProfile.Id, _lastApplyRecord.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            ApplySummary = "This profile changed since the last apply.";
            ApplyDetail = "Apply it again to request the currently edited values and refresh headset confirmation.";
            ApplyLevel = OperationOutcomeKind.Warning;
        }
        else if (_lastApplyRecord is not null)
        {
            ApplySummary = $"Last applied profile: {_lastApplyRecord.ProfileName}.";
            ApplyDetail = "Select that profile to inspect per-field confirmation, or apply the current profile.";
            ApplyLevel = OperationOutcomeKind.Success;
        }
        else
        {
            ApplySummary = "No Sussex visual profile has been applied yet.";
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

                SussexVisualConfirmationRow? confirmationRow = null;
                if (confirmationRows is not null && confirmationRows.TryGetValue(row.Id, out var matchedRow))
                {
                    confirmationRow = matchedRow;
                }

                ComparisonRows.Add(new SussexVisualComparisonRowViewModel(
                    ResolveGroupTitle(row.Id),
                    field,
                    row,
                    confirmationRow));
            }
        }
        else
        {
            for (var i = 0; i < rows.Count; i++)
            {
                SussexVisualConfirmationRow? confirmationRow = null;
                if (confirmationRows is not null && confirmationRows.TryGetValue(rows[i].Id, out var matchedRow))
                {
                    confirmationRow = matchedRow;
                }

                ComparisonRows[i].Apply(rows[i], confirmationRow, syncCurrentValueText);
            }
        }

        foreach (var field in Groups.SelectMany(group => group.Fields))
        {
            if (confirmationRows is not null && confirmationRows.TryGetValue(field.Id, out var confirmationRow))
            {
                field.SetConfirmation(
                    confirmationRow.State switch
                    {
                        SussexVisualConfirmationState.Confirmed => "Confirmed",
                        SussexVisualConfirmationState.Mismatch => "Mismatch",
                        _ => "Waiting"
                    },
                    confirmationRow.State switch
                    {
                        SussexVisualConfirmationState.Confirmed => OperationOutcomeKind.Success,
                        SussexVisualConfirmationState.Mismatch => OperationOutcomeKind.Warning,
                        _ => OperationOutcomeKind.Warning
                    });
            }
            else
            {
                field.SetConfirmation("Not applied", OperationOutcomeKind.Preview);
            }
        }
    }

    private bool TryGetReportedRuntimeConfigJson(IReadOnlyDictionary<string, string> reportedTwinState, out string runtimeConfigJson)
    {
        runtimeConfigJson = string.Empty;
        if (reportedTwinState.Count == 0)
        {
            return false;
        }

        if (reportedTwinState.TryGetValue("showcase_active_runtime_config_json", out var directRuntimeConfigJson) &&
            !string.IsNullOrWhiteSpace(directRuntimeConfigJson))
        {
            runtimeConfigJson = directRuntimeConfigJson.Trim();
            return true;
        }

        if (reportedTwinState.TryGetValue("hotload.showcase_active_runtime_config_json", out var hotloadRuntimeConfigJson) &&
            !string.IsNullOrWhiteSpace(hotloadRuntimeConfigJson))
        {
            runtimeConfigJson = hotloadRuntimeConfigJson.Trim();
            return true;
        }

        runtimeConfigJson = string.Empty;
        return false;
    }

    private string BuildUniqueProfileName(string baseName)
    {
        var seed = string.IsNullOrWhiteSpace(baseName) ? "Sussex Visual Profile" : baseName.Trim();
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
        DeleteSelectedCommand.RaiseCanExecuteChanged();
        ApplySelectedCommand.RaiseCanExecuteChanged();
    }

    private static string ResolveGroupTitle(string controlId)
        => controlId switch
        {
            "sphere_deformation_enabled" => "Shape",
            string id when id.StartsWith("particle_size", StringComparison.Ordinal) => "Size",
            string id when id.StartsWith("depth_wave", StringComparison.Ordinal) => "Depth Wave",
            string id when id.StartsWith("transparency", StringComparison.Ordinal) => "Transparency",
            string id when id.StartsWith("saturation", StringComparison.Ordinal) => "Saturation",
            string id when id.StartsWith("brightness", StringComparison.Ordinal) => "Brightness",
            string id when id.StartsWith("orbit_distance", StringComparison.Ordinal) => "Orbit",
            _ => "Other"
        };

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
                    ? "The Sussex APK is running but not currently foreground, so the visual change may not be immediately visible."
                    : "The Sussex APK is not currently running in foreground. The staged file should load the next time the runtime starts.");
        }

        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            detailParts.Add("Twin-state confirmation will track the approved visual profile values after the headset reports them.");
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
        detailParts.Add(
            "The Sussex runtime must be awake and visible enough to poll runtime_hotload/runtime_overrides.csv before a visual apply can take effect. Wake the headset or use the bench-tools proximity hold, then apply again.");
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
