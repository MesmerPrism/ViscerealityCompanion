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
    private const string BundledBaselineProfileId = "__bundled_sussex_visual_baseline__";
    private static readonly HashSet<string> TracerControlIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "tracers_enabled",
        "tracers_lifetime_seconds",
        "tracers_per_oscillator"
    };

    private readonly StudyShellDefinition _study;
    private readonly IQuestControlService _questService;
    private readonly ITwinModeBridge? _twinBridge;
    private readonly RuntimeConfigWriter _runtimeConfigWriter = new();
    private readonly SussexVisualTuningCompiler? _compiler;
    private readonly SussexVisualProfileStore? _profileStore;
    private readonly SussexVisualProfileApplyStateStore? _applyStateStore;
    private readonly SussexVisualProfileStartupStateStore? _startupStateStore;

    private bool _initialized;
    private IReadOnlyDictionary<string, string> _reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private string? _reportedRuntimeConfigJson;
    private SussexVisualProfileApplyRecord? _lastApplyRecord;
    private SussexVisualProfileListItemViewModel? _selectedProfile;
    private SussexVisualProfileListItemViewModel? _compareProfile;
    private bool _suppressSelectionChangePrompt;
    private string _selectedProfileName = string.Empty;
    private string _selectedProfileNotes = string.Empty;
    private SussexVisualTuningDocument? _draftSourceDocument;
    private string _draftSourceProfileId = string.Empty;
    private string _draftSourceLabel = "Bundled Sussex Baseline";
    private string _librarySummary = "Loading Sussex visual profiles...";
    private string _libraryDetail = "The Sussex shell stores one self-describing json file per visual profile.";
    private OperationOutcomeKind _libraryLevel = OperationOutcomeKind.Preview;
    private string _applySummary = "No Sussex visual profile has been applied yet.";
    private string _applyDetail = "Select or create a profile, then upload it through the normal Sussex hotload path.";
    private OperationOutcomeKind _applyLevel = OperationOutcomeKind.Preview;
    private string _lastCompiledCsvPath = "No Sussex visual hotload CSV written yet.";
    private string _templatePathLabel = "Bundled Sussex visual tuning template not found.";
    private string _libraryRootLabel = string.Empty;
    private SussexVisualProfileStartupState? _startupState;

    public SussexVisualProfilesWorkspaceViewModel(
        StudyShellDefinition study,
        IQuestControlService questService,
        ITwinModeBridge? twinBridge = null)
    {
        _study = study;
        _questService = questService;
        _twinBridge = twinBridge;

        try
        {
            string? templatePath = AppAssetLocator.TryResolveSussexVisualTuningTemplatePath();
            if (!string.IsNullOrWhiteSpace(templatePath))
            {
                var templateJson = File.ReadAllText(templatePath);
                _compiler = new SussexVisualTuningCompiler(templateJson);
                _profileStore = new SussexVisualProfileStore(_compiler);
                _applyStateStore = new SussexVisualProfileApplyStateStore(_study.Id);
                _startupStateStore = new SussexVisualProfileStartupStateStore(_study.Id);
                _lastApplyRecord = _applyStateStore.Load();
                _startupState = _startupStateStore.Load();
                _templatePathLabel = templatePath;
                _libraryRootLabel = _profileStore.RootPath;
                BuildEditorGroups();
                _librarySummary = "Sussex visual profile library ready.";
                _libraryDetail = "The bundled Sussex baseline is always available. Table edits stay in the working draft until you explicitly save them to the library or pin a saved profile for launch.";
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

        NewFromBaselineCommand = new AsyncRelayCommand(NewFromBaselineAsync, () => IsAvailable);
        SaveAsNewProfileCommand = new AsyncRelayCommand(SaveAsNewProfileAsync, CanSaveDraftAsNewProfile);
        SaveSelectedCommand = new AsyncRelayCommand(SaveSelectedAsync, CanSaveSelectedProfile);
        ImportProfileCommand = new AsyncRelayCommand(ImportProfileAsync, () => IsAvailable);
        ExportSelectedCommand = new AsyncRelayCommand(ExportSelectedAsync, () => SelectedProfile is not null && IsAvailable);
        SetStartupProfileCommand = new AsyncRelayCommand(SetStartupProfileAsync, CanSetSelectedProfileAsStartup);
        UseBundledStartupCommand = new AsyncRelayCommand(UseBundledStartupAsync, () => HasPinnedStartupProfile && IsAvailable);
        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync, () => IsAvailable && !string.IsNullOrWhiteSpace(LibraryRootLabel));
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => SelectedProfile is { IsBundledBaseline: false } && IsAvailable);
        ApplySelectedCommand = new AsyncRelayCommand(ApplySelectedAsync, () => SelectedProfile is not null && IsAvailable);
        ResetFieldCommand = new AsyncRelayCommand(ResetFieldAsync, parameter => parameter is SussexVisualProfileFieldViewModel && IsAvailable);
    }

    public event EventHandler? StartupProfileChanged;

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

            if (!CanSwitchToProfile(value))
            {
                OnPropertyChanged(nameof(SelectedProfile));
                return;
            }

            if (SetProperty(ref _selectedProfile, value))
            {
                LoadSelectedProfileIntoEditor(value);
                NotifyStartupStateChanged();
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
                RaiseCommandStates();
                RefreshDraftState();
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
                RaiseCommandStates();
                RefreshDraftState();
            }
        }
    }

    public bool HasPinnedStartupProfile => _startupState is not null;

    public bool HasUnsavedDraftChanges
        => ComputeHasUnsavedDraftChanges();

    public string DraftSourceSummary
        => _draftSourceDocument is null
            ? "Runtime draft source unavailable."
            : $"Runtime draft loaded from: {_draftSourceLabel}.";

    public string DraftSourceDetail
        => _draftSourceDocument is null
            ? "Select the bundled Sussex baseline or a saved profile to create a runtime working draft."
            : "Selecting a saved profile copies it into the runtime working draft. Editing the table changes only that draft until you explicitly save it into the library.";

    public string DraftSummary
        => _draftSourceDocument is null
            ? "No runtime draft loaded."
            : HasUnsavedDraftChanges
                ? "Working draft has unsaved changes."
                : $"Working draft matches {_draftSourceLabel}.";

    public string DraftDetail
        => _draftSourceDocument is null
            ? "Select the bundled baseline or a saved profile to load it into the runtime working draft."
            : string.Equals(_draftSourceProfileId, BundledBaselineProfileId, StringComparison.OrdinalIgnoreCase)
                ? "The bundled baseline is read-only. Apply it directly, or change the draft and use Save Draft As New Profile to add a writable copy to the library."
                : HasUnsavedDraftChanges
                    ? $"Table edits change only the runtime working draft loaded from {_draftSourceLabel}. Use Save Changes To Selected Profile or Save Draft As New Profile to write the current draft into the profile library."
                    : $"Apply To Current Session uses the runtime working draft only. Set Selected Profile For Next Launch pins the saved launch profile separately from the current runtime draft.";

    public OperationOutcomeKind DraftLevel
        => _draftSourceDocument is null
            ? OperationOutcomeKind.Preview
            : HasUnsavedDraftChanges
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success;

    public bool IsSelectedProfileStartupDefault
        => _compiler is not null &&
           SelectedProfile is { IsBundledBaseline: false } &&
           SussexVisualStartupSnapshotResolver.MatchesCurrentSelection(
               _compiler,
               _startupState,
               SelectedProfile.Id,
               SelectedProfile.Document,
               ResolvePinnedStartupProfile()?.Document);

    public string StartupProfileSummary
        => _startupState is null
            ? "Next-launch override: bundled Sussex baseline."
            : $"Next-launch override: {_startupState.ProfileName}.";

    public string StartupProfileDetail
        => _startupState is null
            ? "Sussex launches on the bundled baseline visual state unless you explicitly pin a saved profile below. Applying the runtime draft does not change future launches."
            : $"The saved launch profile '{_startupState.ProfileName}' is the device-side Sussex startup baseline. Table edits and current-session applies do not change that launch pin until you save a profile and set it for launch.";

    public string StartupProfileActionLabel
        => IsSelectedProfileStartupDefault
            ? "Selected Profile Already Used For Next Launch"
            : "Set Selected Profile For Next Launch";

    public AsyncRelayCommand NewFromBaselineCommand { get; }
    public AsyncRelayCommand SaveAsNewProfileCommand { get; }
    public AsyncRelayCommand SaveSelectedCommand { get; }
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

    public SussexVisualSessionSnapshot CaptureSessionSnapshot()
    {
        var currentProfile = CreateCurrentProfileRecord();
        // Keep the last apply record attached to the selected saved profile, not to the entire
        // draft payload, so untouched rows keep their last headset confirmation while the
        // operator experiments with one field in the working draft.
        var selectedMatchesLastApplied = currentProfile is not null &&
                                         _lastApplyRecord is not null &&
                                         string.Equals(currentProfile.Id, _lastApplyRecord.ProfileId, StringComparison.OrdinalIgnoreCase);
        var reportedValues = _compiler is null
            ? new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, double?>(
                _compiler.ExtractRuntimeValues(_reportedRuntimeConfigJson ?? string.Empty, _reportedTwinState),
                StringComparer.OrdinalIgnoreCase);

        var hasUnappliedEdits = false;
        if (_compiler is not null && _lastApplyRecord is not null && selectedMatchesLastApplied)
        {
            var fields = Groups.SelectMany(group => group.Fields).ToArray();
            var confirmationRows = _compiler
                .EvaluateConfirmation(_lastApplyRecord, _reportedRuntimeConfigJson, _reportedTwinState)
                .Rows
                .ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
            var computation = SussexVisualRowConfirmationResolver.Compute(fields, _lastApplyRecord, confirmationRows);
            hasUnappliedEdits = computation.ChangedSinceApplyCount > 0;
        }

        return new SussexVisualSessionSnapshot(
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
        // No-op; the visual workspace keeps draft state in memory only.
    }

    private void BuildEditorGroups()
    {
        Groups.Clear();
        if (_compiler is null)
        {
            return;
        }

        foreach (var groupTitle in new[] { "Shape", "Size", "Depth Wave", "Transparency", "Saturation", "Brightness", "Orbit", "Tracers" })
        {
            var group = new SussexVisualProfileGroupViewModel(groupTitle);
            foreach (var control in _compiler.TemplateDocument.Controls.Where(control => ResolveGroupTitle(control.Id) == groupTitle))
            {
                group.Fields.Add(new SussexVisualProfileFieldViewModel(control, OnFieldValueChanged));
            }

            Groups.Add(group);
        }
    }

    private SussexVisualProfileListItemViewModel CreateBundledBaselineListItem()
    {
        if (_compiler is null)
        {
            throw new InvalidOperationException("Sussex visual tuning compiler is not available.");
        }

        var bundledDocument = _compiler.TemplateDocument;
        var payload = _compiler.Serialize(bundledDocument);
        return new SussexVisualProfileListItemViewModel(
            new SussexVisualProfileRecord(
                BundledBaselineProfileId,
                string.Empty,
                ComputeTextHash(payload),
                DateTimeOffset.MinValue,
                bundledDocument),
            isBundledBaseline: true,
            displayLabelOverride: "Bundled Sussex Baseline",
            modifiedLabelOverride: "Bundled with the APK");
    }

    private bool CanSwitchToProfile(SussexVisualProfileListItemViewModel? nextProfile)
    {
        if (_suppressSelectionChangePrompt || !_initialized || _selectedProfile is null || !HasUnsavedDraftChanges)
        {
            return true;
        }

        var currentLabel = string.IsNullOrWhiteSpace(SelectedProfileName)
            ? _selectedProfile.DisplayLabel
            : SelectedProfileName.Trim();
        return MessageBox.Show(
                   $"Discard the current unsaved visual draft for '{currentLabel}' and switch profiles?",
                   "Viscereality Companion",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void RefreshDraftState()
    {
        OnPropertyChanged(nameof(HasUnsavedDraftChanges));
        OnPropertyChanged(nameof(DraftSourceSummary));
        OnPropertyChanged(nameof(DraftSourceDetail));
        OnPropertyChanged(nameof(DraftSummary));
        OnPropertyChanged(nameof(DraftDetail));
        OnPropertyChanged(nameof(DraftLevel));
        NotifyStartupStateChanged();
        RaiseCommandStates();
    }

    private bool ComputeHasUnsavedDraftChanges()
    {
        if (_compiler is null || _draftSourceDocument is null)
        {
            return false;
        }

        if (!TryCreateCurrentDocument(out var currentDocument, out _))
        {
            return true;
        }

        return !SussexVisualDocumentComparer.Matches(_compiler, currentDocument!, _draftSourceDocument);
    }

    private bool CanSaveSelectedProfile()
        => IsAvailable &&
           SelectedProfile is { IsBundledBaseline: false } &&
           HasUnsavedDraftChanges &&
           TryCreateCurrentDocument(out _, out _);

    private bool CanSaveDraftAsNewProfile()
        => IsAvailable &&
           SelectedProfile is not null &&
           TryCreateCurrentDocument(out _, out _);

    private bool CanSetSelectedProfileAsStartup()
        => IsAvailable &&
           SelectedProfile is { IsBundledBaseline: false } &&
           !HasUnsavedDraftChanges &&
           !IsSelectedProfileStartupDefault;

    private async Task ReloadProfilesAsync(string? selectProfileId = null)
    {
        if (_profileStore is null)
        {
            return;
        }

        var savedProfiles = await _profileStore.LoadAllAsync();
        await DispatchAsync(() =>
        {
            _suppressSelectionChangePrompt = true;
            try
            {
                var preferredProfileId = selectProfileId ??
                                         _selectedProfile?.Id ??
                                         _startupState?.ProfileId;
                Profiles.Clear();
                Profiles.Add(CreateBundledBaselineListItem());
                foreach (var profile in savedProfiles)
                {
                    Profiles.Add(new SussexVisualProfileListItemViewModel(profile));
                }

                if (savedProfiles.Count == 0)
                {
                    if (_startupState is not null)
                    {
                        _startupState = null;
                        _startupStateStore?.Save(null);
                    }

                    SelectedProfile = Profiles[0];
                    LibrarySummary = "No saved Sussex visual profiles yet.";
                    LibraryDetail = "The bundled Sussex baseline is still available below. Use Save Draft As New Profile to create the first editable copy in the local library.";
                    LibraryLevel = OperationOutcomeKind.Preview;
                    NotifyStartupStateChanged();
                    return;
                }

                SelectedProfile = Profiles.FirstOrDefault(profile =>
                                     string.Equals(profile.Id, preferredProfileId, StringComparison.OrdinalIgnoreCase))
                                 ?? Profiles[0];

                if (_startupState is not null &&
                    Profiles.Any(profile =>
                        !profile.IsBundledBaseline &&
                        string.Equals(profile.Id, _startupState.ProfileId, StringComparison.OrdinalIgnoreCase)) is false)
                {
                    _startupState = null;
                    _startupStateStore?.Save(null);
                }

                LibrarySummary = $"Loaded {savedProfiles.Count.ToString(CultureInfo.InvariantCulture)} saved Sussex visual profile(s) plus the bundled baseline.";
                LibraryDetail = $"Saved profiles live in {LibraryRootLabel}. Selecting a profile loads it into the working draft; nothing in the library changes until you explicitly save.";
                LibraryLevel = OperationOutcomeKind.Success;
                NotifyStartupStateChanged();
            }
            finally
            {
                _suppressSelectionChangePrompt = false;
            }
        });
    }

    private void LoadSelectedProfileIntoEditor(SussexVisualProfileListItemViewModel? profile)
    {
        if (profile is null)
        {
            _draftSourceDocument = null;
            _draftSourceProfileId = string.Empty;
            _draftSourceLabel = "Bundled Sussex Baseline";
            foreach (var field in Groups.SelectMany(group => group.Fields))
            {
                field.ResetToBaseline(notify: false);
                field.SetConfirmation("Not applied", OperationOutcomeKind.Preview);
            }

            SelectedProfileName = string.Empty;
            SelectedProfileNotes = string.Empty;
            ComparisonRows.Clear();
            RefreshDraftState();
            return;
        }

        _draftSourceDocument = profile.Document;
        _draftSourceProfileId = profile.Id;
        _draftSourceLabel = profile.DisplayLabel;
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

        RefreshComparisonState();
        RefreshDraftState();
    }

    private void OnFieldValueChanged()
    {
        RefreshComparisonState(syncCurrentValueText: true);
        RefreshDraftState();
    }

    private SussexVisualProfileRecord? CreateCurrentProfileRecord()
    {
        if (_draftSourceDocument is null)
        {
            return null;
        }

        if (!TryCreateCurrentDocument(out var document, out _))
        {
            return null;
        }

        var sourceProfile = SelectedProfile;
        var payload = _compiler is null ? string.Empty : _compiler.Serialize(document!);
        return new SussexVisualProfileRecord(
            string.IsNullOrWhiteSpace(_draftSourceProfileId) ? BundledBaselineProfileId : _draftSourceProfileId,
            sourceProfile?.FilePath ?? string.Empty,
            string.IsNullOrWhiteSpace(payload) ? string.Empty : ComputeTextHash(payload),
            DateTimeOffset.UtcNow,
            document!);
    }

    private SussexVisualProfileRecord? ResolveEffectiveProfileRecord(SussexVisualProfileRecord? currentProfile)
    {
        if (_lastApplyRecord is null || _compiler is null)
        {
            return null;
        }

        if (currentProfile is not null &&
            CurrentDraftMatchesLastApplyRequest(currentProfile.Document))
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

        return new SussexVisualProfileRecord(
            _lastApplyRecord.ProfileId,
            string.Empty,
            _lastApplyRecord.FileHash,
            _lastApplyRecord.AppliedAtUtc,
            _compiler.CreateDocument(
                _lastApplyRecord.ProfileName,
                null,
                _lastApplyRecord.RequestedValues));
    }

    private bool CurrentDraftMatchesLastApplyRequest(SussexVisualTuningDocument? currentDocument = null)
    {
        if (_compiler is null || _lastApplyRecord is null)
        {
            return false;
        }

        if (currentDocument is null &&
            !TryCreateCurrentDocument(out currentDocument, out _))
        {
            return false;
        }

        return currentDocument is not null &&
               SussexVisualDocumentComparer.MatchesControlValues(
                   _compiler,
                   currentDocument,
                   _lastApplyRecord.RequestedValues);
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

    private async Task<SussexVisualProfileRecord?> SaveSnapshotAsync(
        SussexVisualProfileSnapshot snapshot,
        string? existingPath,
        string summary)
    {
        if (_profileStore is null)
        {
            return null;
        }

        try
        {
            var saved = await _profileStore.SaveAsync(
                existingPath,
                snapshot.ProfileName,
                snapshot.ProfileNotes,
                snapshot.ControlValues);

            LibrarySummary = summary;
            LibraryDetail = saved.FilePath;
            LibraryLevel = OperationOutcomeKind.Success;
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

    private async Task SaveAsNewProfileAsync()
    {
        if (_profileStore is null || SelectedProfile is null)
        {
            return;
        }

        var snapshot = CaptureCurrentSnapshot();
        if (snapshot is null)
        {
            return;
        }

        var profileName = string.IsNullOrWhiteSpace(snapshot.ProfileName)
            ? BuildUniqueProfileName("Sussex Visual Profile")
            : BuildUniqueProfileName(snapshot.ProfileName);
        var saved = await SaveSnapshotAsync(
            snapshot with { ProfileName = profileName },
            existingPath: null,
            "Saved Sussex visual draft as a new profile.");
        if (saved is not null)
        {
            await ReloadProfilesAsync(saved.Id);
            LibrarySummary = "Saved Sussex visual draft as a new profile.";
            LibraryDetail = saved.FilePath;
            LibraryLevel = OperationOutcomeKind.Success;
        }
    }

    private async Task SaveSelectedAsync()
    {
        if (_profileStore is null || SelectedProfile is not { IsBundledBaseline: false })
        {
            return;
        }

        var snapshot = CaptureCurrentSnapshot();
        if (snapshot is null)
        {
            return;
        }

        var previousSelectedId = SelectedProfile.Id;
        var saved = await SaveSnapshotAsync(
            snapshot,
            existingPath: SelectedProfile.FilePath,
            "Saved changes to the selected Sussex visual profile.");
        if (saved is null)
        {
            return;
        }

        if (_startupState is not null &&
            string.Equals(_startupState.ProfileId, previousSelectedId, StringComparison.OrdinalIgnoreCase))
        {
            _startupState = new SussexVisualProfileStartupState(
                saved.Id,
                saved.Document.Profile.Name,
                DateTimeOffset.UtcNow,
                saved.Document.Profile.Notes,
                new Dictionary<string, double>(saved.Document.ControlValues, StringComparer.OrdinalIgnoreCase));
            _startupStateStore?.Save(_startupState);
            StartupProfileChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_lastApplyRecord is not null &&
            string.Equals(_lastApplyRecord.ProfileId, previousSelectedId, StringComparison.OrdinalIgnoreCase))
        {
            _lastApplyRecord = new SussexVisualProfileApplyRecord(
                saved.Id,
                saved.Document.Profile.Name,
                saved.FileHash,
                _lastApplyRecord.CompiledJsonHash,
                _lastApplyRecord.AppliedAtUtc,
                _lastApplyRecord.RequestedValues,
                _lastApplyRecord.PreviousReportedValues);
            _applyStateStore?.Save(_lastApplyRecord);
        }

        await ReloadProfilesAsync(saved.Id);
        LibrarySummary = "Saved changes to the selected Sussex visual profile.";
        LibraryDetail = saved.FilePath;
        LibraryLevel = OperationOutcomeKind.Success;
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
        if (_profileStore is null || SelectedProfile is null)
        {
            return;
        }

        var saved = SelectedProfile.ToRecord();

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

    private async Task SetStartupProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        if (SelectedProfile.IsBundledBaseline)
        {
            await UseBundledStartupAsync().ConfigureAwait(false);
            return;
        }

        if (HasUnsavedDraftChanges)
        {
            LibrarySummary = "Save the visual draft before pinning it for launch.";
            LibraryDetail = "Set Selected Profile For Next Launch uses the selected saved profile, not the unsaved working draft shown in the table.";
            LibraryLevel = OperationOutcomeKind.Warning;
            return;
        }

        var saved = SelectedProfile.ToRecord();
        var startupState = new SussexVisualProfileStartupState(
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
        StartupProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task UseBundledStartupAsync()
    {
        _startupState = null;
        _startupStateStore?.Save(null);
        NotifyStartupStateChanged();
        RefreshComparisonState();
        StartupProfileChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    internal SussexVisualStartupHotloadPlan? CapturePinnedStartupHotloadPlan()
    {
        if (_compiler is null)
        {
            return null;
        }

        var startupRecord = CreateStartupProfileRecord();
        if (startupRecord is null)
        {
            return null;
        }

        var compiled = _compiler.Compile(startupRecord.Document);
        var previousReportedValues = _compiler.ExtractRuntimeValues(_reportedRuntimeConfigJson ?? string.Empty, _reportedTwinState);
        return new SussexVisualStartupHotloadPlan(
            startupRecord,
            compiled.Entries,
            new Dictionary<string, double?>(previousReportedValues, StringComparer.OrdinalIgnoreCase));
    }

    internal void TrackPinnedStartupLaunch(SussexVisualStartupHotloadPlan plan, DateTimeOffset appliedAtUtc, string? csvPath = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _lastApplyRecord = new SussexVisualProfileApplyRecord(
            plan.Profile.Id,
            plan.Profile.Document.Profile.Name,
            plan.Profile.FileHash,
            ComputeTextHash(string.Join("\n", plan.Entries.Select(entry => $"{entry.Key}={entry.Value}"))),
            appliedAtUtc,
            new Dictionary<string, double>(plan.Profile.Document.ControlValues, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, double?>(plan.PreviousReportedValues, StringComparer.OrdinalIgnoreCase));
        _applyStateStore?.Save(_lastApplyRecord);
        if (!string.IsNullOrWhiteSpace(csvPath))
        {
            LastCompiledCsvPath = csvPath;
        }

        RefreshComparisonState();
    }

    public async Task ApplyStartupProfileOnLaunchAsync()
    {
        SussexVisualProfileRecord? startupRecord = null;
        await DispatchAsync(() =>
        {
            startupRecord = CreateStartupProfileRecord();
        }).ConfigureAwait(false);

        if (startupRecord is null)
        {
            await DispatchAsync(() =>
            {
                ApplySummary = "APK launch is using the bundled Sussex baseline visual state.";
                ApplyDetail = "No saved startup profile is pinned, so Sussex keeps the bundled baseline until you apply another profile.";
                ApplyLevel = OperationOutcomeKind.Preview;
                RefreshComparisonState();
            }).ConfigureAwait(false);
            return;
        }

        await ApplyProfileRecordAsync(
            startupRecord,
            "Apply Startup Visual Profile").ConfigureAwait(false);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_profileStore is null || SelectedProfile is not { IsBundledBaseline: false })
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

        if (_startupState is not null &&
            string.Equals(_startupState.ProfileId, deletedId, StringComparison.OrdinalIgnoreCase))
        {
            _startupState = null;
            _startupStateStore?.Save(null);
            NotifyStartupStateChanged();
            StartupProfileChanged?.Invoke(this, EventArgs.Empty);
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

        if (!TryCreateCurrentDocument(out var document, out var error))
        {
            ApplySummary = "Current visual values are invalid.";
            ApplyDetail = error ?? "The current visual draft could not be compiled safely.";
            ApplyLevel = OperationOutcomeKind.Warning;
            RefreshComparisonState();
            return;
        }

        var payload = _compiler.Serialize(document!);
        var draftRecord = new SussexVisualProfileRecord(
            SelectedProfile.Id,
            SelectedProfile.FilePath,
            ComputeTextHash(payload),
            DateTimeOffset.UtcNow,
            document!);

        await ApplyProfileRecordAsync(draftRecord, "Apply Visual Draft", preferTwinRuntimePublish: true).ConfigureAwait(false);
    }

    private async Task ApplyProfileRecordAsync(
        SussexVisualProfileRecord saved,
        string actionLabel,
        bool preferTwinRuntimePublish = false)
    {
        if (_compiler is null)
        {
            return;
        }

        try
        {
            var compiled = _compiler.Compile(saved.Document);
            var previousReportedValues = _compiler.ExtractRuntimeValues(_reportedRuntimeConfigJson ?? string.Empty, _reportedTwinState);
            var runtimeProfile = new RuntimeConfigProfile(
                $"sussex_visual_tuning_v1_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}",
                $"Sussex Visual Profile - {saved.Document.Profile.Name}",
                string.Empty,
                DateTimeOffset.UtcNow.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture),
                "study",
                false,
                $"Compiled from {saved.Document.Profile.Name}. Only the Sussex-approved visual envelope fields plus the simplified tracer wrapper were changed.",
                [saved.Document.PackageId],
                compiled.Entries);

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

            OperationOutcome outcome;
            if (preferTwinRuntimePublish)
            {
                if (headsetStatus.IsTargetForeground != true)
                {
                    ApplySummary = $"{actionLabel} requires an active Sussex session.";
                    ApplyDetail = $"Current-session visual applies are live-only and do not rewrite the saved launch profile. Bring {target.Label} to the foreground, then apply the runtime draft again.";
                    ApplyLevel = OperationOutcomeKind.Warning;
                    RefreshComparisonState();
                    return;
                }

                if (_twinBridge is null || !_twinBridge.Status.IsAvailable)
                {
                    ApplySummary = $"{actionLabel} requires the live twin bridge.";
                    ApplyDetail = "Current-session visual applies now use the live quest_hotload_config channel so they stay temporary and do not overwrite the saved launch profile on device.";
                    ApplyLevel = OperationOutcomeKind.Warning;
                    RefreshComparisonState();
                    return;
                }

                outcome = await _twinBridge.PublishRuntimeConfigAsync(runtimeProfile, target).ConfigureAwait(false);
            }
            else
            {
                outcome = await _questService.ApplyHotloadProfileAsync(hotloadProfile, target).ConfigureAwait(false);
            }

            if (outcome.Kind != OperationOutcomeKind.Failure)
            {
                _lastApplyRecord = new SussexVisualProfileApplyRecord(
                    saved.Id,
                    saved.Document.Profile.Name,
                    saved.FileHash,
                    ComputeTextHash(string.Join("\n", compiled.Entries.Select(entry => $"{entry.Key}={entry.Value}"))),
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, double>(saved.Document.ControlValues, StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, double?>(previousReportedValues, StringComparer.OrdinalIgnoreCase));
                _applyStateStore?.Save(_lastApplyRecord);
            }

            ApplySummary = outcome.Summary;
            ApplyDetail = BuildApplyOutcomeDetail(outcome, headsetStatus, csvPath, wakeOutcome, preferTwinRuntimePublish);
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
        if (parameter is SussexVisualProfileFieldViewModel field)
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

            var startupComparisonDocument = ResolveStartupComparisonDocument();
            var fields = Groups.SelectMany(group => group.Fields).ToArray();
            var fieldsById = fields.ToDictionary(field => field.Id, StringComparer.OrdinalIgnoreCase);
            var rows = SussexVisualComparisonRowBuilder.Build(_compiler, fields, startupComparisonDocument);
            var currentDocumentIsValid = TryCreateCurrentDocument(out var currentDocument, out var editorError);
            // The working draft is always the editable surface. Saved profiles remain library
            // snapshots until the operator explicitly saves the draft back into the library.
            var selectedMatchesLastApplyProfile = _lastApplyRecord is not null &&
                                                 string.Equals(
                                                     SelectedProfile.Id,
                                                     _lastApplyRecord.ProfileId,
                                                     StringComparison.OrdinalIgnoreCase);
            var editorErrorDetail = editorError ?? "The current editor values could not be compiled.";

            var rowConfirmationStates = new Dictionary<string, SussexVisualRowConfirmationState>(StringComparer.OrdinalIgnoreCase);
            var changedSinceApplyCount = 0;
            var unchangedSinceApplyCount = 0;
            var missingTracerTelemetryCount = 0;
            if (selectedMatchesLastApplyProfile)
            {
                var confirmation = _compiler.EvaluateConfirmation(_lastApplyRecord!, _reportedRuntimeConfigJson, _reportedTwinState);
                var confirmationRows = confirmation.Rows.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
                var computation = SussexVisualRowConfirmationResolver.Compute(fieldsById.Values, _lastApplyRecord!, confirmationRows);
                rowConfirmationStates = new Dictionary<string, SussexVisualRowConfirmationState>(computation.States, StringComparer.OrdinalIgnoreCase);
                changedSinceApplyCount = computation.ChangedSinceApplyCount;
                unchangedSinceApplyCount = computation.UnchangedSinceApplyCount;
                missingTracerTelemetryCount = AnnotateMissingTracerTelemetryStates(confirmationRows, rowConfirmationStates);

                if (!currentDocumentIsValid)
                {
                    ApplySummary = "Current visual values are invalid.";
                    ApplyDetail = $"{editorErrorDetail} Adjust the edited rows or use Reset before applying or saving them for the next Sussex launch.";
                    ApplyLevel = OperationOutcomeKind.Warning;
                }
                else if (changedSinceApplyCount > 0)
                {
                    ApplySummary = changedSinceApplyCount == 1
                        ? "1 Sussex parameter value changed since the last apply."
                        : $"{changedSinceApplyCount.ToString(CultureInfo.InvariantCulture)} Sussex parameter values changed since the last apply.";
                    ApplyDetail = unchangedSinceApplyCount > 0
                        ? $"Apply again to request the edited values. The other {unchangedSinceApplyCount.ToString(CultureInfo.InvariantCulture)} row(s) still show the last headset confirmation for {_lastApplyRecord!.ProfileName}."
                        : "Apply again to request the edited values and refresh headset confirmation.";
                    ApplyLevel = OperationOutcomeKind.Warning;
                }
                else
                {
                    ApplySummary = confirmation.Summary;
                    if (missingTracerTelemetryCount > 0)
                    {
                        var remainingWaitingCount = Math.Max(0, confirmation.WaitingCount - missingTracerTelemetryCount);
                        ApplyDetail = remainingWaitingCount > 0
                            ? $"Applied {_lastApplyRecord!.ProfileName} at {_lastApplyRecord.AppliedAtUtc.ToLocalTime():HH:mm:ss}. Upload succeeded, but the current APK is not publishing tracer hotload keys, so {missingTracerTelemetryCount.ToString(CultureInfo.InvariantCulture)} tracer row(s) cannot confirm in this build. The other {remainingWaitingCount.ToString(CultureInfo.InvariantCulture)} row(s) are still waiting for a fresh quest_twin_state report."
                            : $"Applied {_lastApplyRecord!.ProfileName} at {_lastApplyRecord.AppliedAtUtc.ToLocalTime():HH:mm:ss}. Upload succeeded, but the current APK is not publishing tracer hotload keys, so the tracer rows cannot confirm in this build even though the standard visual JSON rows can.";
                    }
                    else
                    {
                        ApplyDetail = confirmation.WaitingCount > 0
                            ? $"Applied {_lastApplyRecord!.ProfileName} at {_lastApplyRecord.AppliedAtUtc.ToLocalTime():HH:mm:ss}. Upload succeeded; waiting for a fresh quest_twin_state report to mirror the requested Sussex visual and tracer values."
                            : $"Applied {_lastApplyRecord!.ProfileName} at {_lastApplyRecord.AppliedAtUtc.ToLocalTime():HH:mm:ss}.";
                    }

                    ApplyLevel = confirmation.MismatchCount > 0
                        ? OperationOutcomeKind.Warning
                        : confirmation.WaitingCount > 0
                            ? OperationOutcomeKind.Warning
                            : OperationOutcomeKind.Success;
                }
            }
            else if (!currentDocumentIsValid)
            {
                ApplySummary = "Current visual values are invalid.";
                ApplyDetail = $"{editorErrorDetail} Adjust the edited rows or use Reset before applying or saving them for the next Sussex launch.";
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

                    ComparisonRows.Add(new SussexVisualComparisonRowViewModel(
                        ResolveGroupTitle(row.Id),
                        field,
                        row,
                        ResolveRowConfirmationState(rowConfirmationStates, row.Id)));
                }
            }
            else
            {
                for (var i = 0; i < rows.Count; i++)
                {
                    ComparisonRows[i].Apply(
                        rows[i],
                        ResolveRowConfirmationState(rowConfirmationStates, rows[i].Id),
                        syncCurrentValueText);
                }
            }

            foreach (var field in fields)
            {
                var state = ResolveRowConfirmationState(rowConfirmationStates, field.Id);
                field.SetConfirmation(state.Label, state.Level);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or InvalidOperationException)
        {
            ApplySummary = "Current visual values could not be compared safely.";
            ApplyDetail = $"{ex.Message} Adjust the edited rows or refresh the live runtime state before applying or saving them for the next Sussex launch.";
            ApplyLevel = OperationOutcomeKind.Warning;
        }
    }

    private static SussexVisualRowConfirmationState ResolveRowConfirmationState(
        IReadOnlyDictionary<string, SussexVisualRowConfirmationState> states,
        string fieldId)
        => states.TryGetValue(fieldId, out var state)
            ? state
            : SussexVisualRowConfirmationResolver.DefaultConfirmationState;

    private int AnnotateMissingTracerTelemetryStates(
        IReadOnlyDictionary<string, SussexVisualConfirmationRow> confirmationRows,
        IDictionary<string, SussexVisualRowConfirmationState> rowConfirmationStates)
    {
        if (!ShouldFlagMissingTracerTelemetry(confirmationRows))
        {
            return 0;
        }

        var missingCount = 0;
        foreach (var pair in confirmationRows)
        {
            if (!IsTracerControlId(pair.Key) || pair.Value.State != SussexVisualConfirmationState.Waiting)
            {
                continue;
            }

            rowConfirmationStates[pair.Key] = SussexVisualRowConfirmationResolver.MissingTelemetryConfirmationState;
            missingCount++;
        }

        return missingCount;
    }

    private bool ShouldFlagMissingTracerTelemetry(IReadOnlyDictionary<string, SussexVisualConfirmationRow> confirmationRows)
    {
        if (confirmationRows.Count == 0 || _reportedTwinState.Count == 0)
        {
            return false;
        }

        if (!TryGetReportedRuntimeConfigJson(_reportedTwinState, out _))
        {
            return false;
        }

        var hasWaitingTracerRow = false;
        foreach (var row in confirmationRows.Values)
        {
            if (!IsTracerControlId(row.Id) || row.State != SussexVisualConfirmationState.Waiting)
            {
                continue;
            }

            hasWaitingTracerRow = true;
            break;
        }

        if (!hasWaitingTracerRow)
        {
            return false;
        }

        foreach (var key in _reportedTwinState.Keys)
        {
            if (key.StartsWith("hotload.integrated_tracers_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("integrated_tracers_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTracerControlId(string controlId)
        => TracerControlIds.Contains(controlId);

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
        SaveAsNewProfileCommand.RaiseCanExecuteChanged();
        SaveSelectedCommand.RaiseCanExecuteChanged();
        ExportSelectedCommand.RaiseCanExecuteChanged();
        SetStartupProfileCommand.RaiseCanExecuteChanged();
        UseBundledStartupCommand.RaiseCanExecuteChanged();
        DeleteSelectedCommand.RaiseCanExecuteChanged();
        ApplySelectedCommand.RaiseCanExecuteChanged();
    }

    private SussexVisualProfileListItemViewModel? ResolvePinnedStartupProfile()
        => _startupState is null
            ? null
            : Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, _startupState.ProfileId, StringComparison.OrdinalIgnoreCase));

    private SussexVisualTuningDocument ResolveStartupComparisonDocument()
    {
        if (_compiler is null)
        {
            throw new InvalidOperationException("Sussex visual tuning compiler is not available.");
        }

        return SussexVisualStartupSnapshotResolver.ResolveDocument(
            _compiler,
            _startupState,
            ResolvePinnedStartupProfile()?.Document);
    }

    private SussexVisualProfileRecord? CreateStartupProfileRecord()
    {
        if (_compiler is null || _startupState is null)
        {
            return ResolvePinnedStartupProfile()?.ToRecord();
        }

        SussexVisualTuningDocument startupDocument;
        try
        {
            startupDocument = ResolveStartupComparisonDocument();
        }
        catch (InvalidDataException)
        {
            return ResolvePinnedStartupProfile()?.ToRecord();
        }

        var payload = _compiler.Serialize(startupDocument);
        return new SussexVisualProfileRecord(
            _startupState.ProfileId,
            string.Empty,
            ComputeTextHash(payload),
            _startupState.UpdatedAtUtc,
            startupDocument);
    }

    private bool TryCreateCurrentDocument(
        out SussexVisualTuningDocument? document,
        out string? error)
    {
        if (_compiler is null || SelectedProfile is null)
        {
            document = null;
            error = null;
            return false;
        }

        return SussexVisualCurrentDocumentResolver.TryCreate(
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

    private static string ResolveGroupTitle(string controlId)
        => controlId switch
        {
            "sphere_deformation_enabled" => "Shape",
            string id when id.StartsWith("oblateness_by_radius", StringComparison.Ordinal) => "Shape",
            string id when id.StartsWith("sphere_radius", StringComparison.Ordinal) => "Size",
            "particle_size_relative_to_radius" => "Size",
            string id when id.StartsWith("particle_size", StringComparison.Ordinal) => "Size",
            string id when id.StartsWith("depth_wave", StringComparison.Ordinal) => "Depth Wave",
            string id when id.StartsWith("transparency", StringComparison.Ordinal) => "Transparency",
            string id when id.StartsWith("saturation", StringComparison.Ordinal) => "Saturation",
            string id when id.StartsWith("brightness", StringComparison.Ordinal) => "Brightness",
            string id when id.StartsWith("orbit_distance", StringComparison.Ordinal) => "Orbit",
            string id when id.StartsWith("tracers_", StringComparison.Ordinal) => "Tracers",
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
        OperationOutcome? wakeOutcome,
        bool usedTwinRuntimePublish)
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

        if (usedTwinRuntimePublish)
        {
            detailParts.Add("The live quest_hotload_config channel changed only the active Sussex session. The saved launch profile on device was left untouched.");
        }
        else if (!headsetStatus.IsTargetForeground)
        {
            detailParts.Add(
                headsetStatus.IsTargetRunning
                    ? "The Sussex APK is running but not currently foreground, so the visual change may not be immediately visible."
                    : "The Sussex APK is not currently running in foreground. The staged file should load the next time the runtime starts.");
        }

        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            detailParts.Add("Twin-state confirmation will track the approved Sussex visual and tracer values after the headset reports them.");
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
