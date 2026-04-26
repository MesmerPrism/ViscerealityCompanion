using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App.ViewModels;

public sealed class SussexConditionWorkspaceViewModel : ObservableObject
{
    private readonly StudyShellDefinition _study;
    private readonly SussexVisualProfilesWorkspaceViewModel _visualProfiles;
    private readonly SussexControllerBreathingProfilesWorkspaceViewModel _controllerBreathingProfiles;
    private readonly SussexStudyConditionStore _conditionStore;

    private SussexConditionItemViewModel? _selectedCondition;
    private SussexConditionProfileOptionViewModel? _selectedVisualProfileOption;
    private SussexConditionProfileOptionViewModel? _selectedControllerBreathingProfileOption;
    private bool _initialized;
    private bool _suppressOptionWrite;
    private string _librarySummary = "Loading Sussex conditions...";
    private string _libraryDetail = "Conditions combine one visual profile and one breathing profile.";
    private OperationOutcomeKind _libraryLevel = OperationOutcomeKind.Preview;

    public SussexConditionWorkspaceViewModel(
        StudyShellDefinition study,
        SussexVisualProfilesWorkspaceViewModel visualProfiles,
        SussexControllerBreathingProfilesWorkspaceViewModel controllerBreathingProfiles,
        SussexStudyConditionStore? conditionStore = null)
    {
        _study = study;
        _visualProfiles = visualProfiles;
        _controllerBreathingProfiles = controllerBreathingProfiles;
        _conditionStore = conditionStore ?? new SussexStudyConditionStore(study.Id);

        NewConditionCommand = new AsyncRelayCommand(NewConditionAsync, () => IsAvailable);
        DuplicateSelectedCommand = new AsyncRelayCommand(DuplicateSelectedAsync, () => SelectedCondition is not null && IsAvailable);
        SaveSelectedCommand = new AsyncRelayCommand(SaveSelectedAsync, () => SelectedCondition is not null && IsAvailable);
        LoadConditionCommand = new AsyncRelayCommand(LoadConditionAsync, () => IsAvailable);
        ShareSelectedCommand = new AsyncRelayCommand(ShareSelectedAsync, () => SelectedCondition is not null && IsAvailable);
        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync, () => IsAvailable);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => SelectedCondition?.HasLocalFile == true && IsAvailable);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsAvailable);
    }

    public event EventHandler<IReadOnlyList<StudyConditionDefinition>>? ActiveConditionsChanged;

    public bool IsAvailable => true;

    public ObservableCollection<SussexConditionItemViewModel> ConditionItems { get; } = new();
    public ObservableCollection<SussexConditionProfileOptionViewModel> VisualProfileOptions { get; } = new();
    public ObservableCollection<SussexConditionProfileOptionViewModel> ControllerBreathingProfileOptions { get; } = new();

    public string LibraryRootLabel => _conditionStore.RootPath;

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

    public string ActiveConditionsSummary
        => $"{ConditionItems.Count(item => item.IsActive)} active of {ConditionItems.Count} configured conditions.";

    public SussexConditionItemViewModel? SelectedCondition
    {
        get => _selectedCondition;
        set
        {
            if (SetProperty(ref _selectedCondition, value))
            {
                SyncSelectedProfileOptions();
                RaiseCommandStates();
            }
        }
    }

    public SussexConditionProfileOptionViewModel? SelectedVisualProfileOption
    {
        get => _selectedVisualProfileOption;
        set
        {
            if (SetProperty(ref _selectedVisualProfileOption, value) && !_suppressOptionWrite && value is not null && SelectedCondition is not null)
            {
                SelectedCondition.VisualProfileId = value.ReferenceId;
            }
        }
    }

    public SussexConditionProfileOptionViewModel? SelectedControllerBreathingProfileOption
    {
        get => _selectedControllerBreathingProfileOption;
        set
        {
            if (SetProperty(ref _selectedControllerBreathingProfileOption, value) && !_suppressOptionWrite && value is not null && SelectedCondition is not null)
            {
                SelectedCondition.ControllerBreathingProfileId = value.ReferenceId;
            }
        }
    }

    public AsyncRelayCommand NewConditionCommand { get; }
    public AsyncRelayCommand DuplicateSelectedCommand { get; }
    public AsyncRelayCommand SaveSelectedCommand { get; }
    public AsyncRelayCommand LoadConditionCommand { get; }
    public AsyncRelayCommand ShareSelectedCommand { get; }
    public AsyncRelayCommand OpenFolderCommand { get; }
    public AsyncRelayCommand DeleteSelectedCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await _visualProfiles.InitializeAsync();
        await _controllerBreathingProfiles.InitializeAsync();
        await DispatchAsync(RefreshProfileOptions);
        await ReloadConditionsAsync(SelectedCondition?.Id);
    }

    private async Task ReloadConditionsAsync(string? selectConditionId = null)
    {
        var localRecords = await _conditionStore.LoadAllAsync();
        var localById = localRecords.ToDictionary(record => record.Id, StringComparer.OrdinalIgnoreCase);
        var nextItems = new List<SussexConditionItemViewModel>();

        foreach (var bundled in _study.Conditions)
        {
            if (localById.Remove(bundled.Id, out var localOverride))
            {
                nextItems.Add(CreateItem(localOverride.Definition, localOverride, isBundled: true, isLocalOverride: true));
            }
            else
            {
                nextItems.Add(CreateItem(bundled, localRecord: null, isBundled: true, isLocalOverride: false));
            }
        }

        nextItems.AddRange(localById.Values
            .OrderBy(record => record.Definition.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .Select(record => CreateItem(record.Definition, record, isBundled: false, isLocalOverride: false)));

        await DispatchAsync(() =>
        {
            ConditionItems.Clear();
            foreach (var item in nextItems)
            {
                ConditionItems.Add(item);
            }

            SelectedCondition = ConditionItems.FirstOrDefault(item => string.Equals(item.Id, selectConditionId, StringComparison.OrdinalIgnoreCase))
                                ?? ConditionItems.FirstOrDefault();
            LibrarySummary = "Sussex condition library ready.";
            LibraryDetail = $"Local condition files are stored in {_conditionStore.RootPath}. Bundled conditions can be saved as local overrides.";
            LibraryLevel = OperationOutcomeKind.Success;
            RefreshActiveConditions();
        });
    }

    private SussexConditionItemViewModel CreateItem(
        StudyConditionDefinition condition,
        SussexStudyConditionRecord? localRecord,
        bool isBundled,
        bool isLocalOverride)
        => new(
            condition,
            localRecord,
            isBundled,
            isLocalOverride,
            OnConditionItemChanged);

    private async Task NewConditionAsync()
    {
        if (!TryGetDefaultProfileReferences(out var visualProfileId, out var controllerProfileId))
        {
            return;
        }

        var id = BuildUniqueConditionId("condition");
        var condition = new StudyConditionDefinition(
            id,
            "New Condition",
            string.Empty,
            visualProfileId,
            controllerProfileId,
            isActive: false);
        var saved = await _conditionStore.SaveAsync(existingPath: null, condition);
        await ReloadConditionsAsync(saved.Id);
    }

    private async Task DuplicateSelectedAsync()
    {
        if (SelectedCondition is null)
        {
            return;
        }

        var id = BuildUniqueConditionId(SelectedCondition.Id);
        var condition = SelectedCondition.ToDefinition() with
        {
            Id = id,
            Label = BuildUniqueConditionLabel($"{SelectedCondition.Label} Copy"),
            IsActive = false
        };
        var saved = await _conditionStore.SaveAsync(existingPath: null, condition);
        await ReloadConditionsAsync(saved.Id);
    }

    private async Task SaveSelectedAsync()
    {
        if (SelectedCondition is null)
        {
            return;
        }

        try
        {
            if (ConditionItems.Any(item =>
                    !ReferenceEquals(item, SelectedCondition) &&
                    string.Equals(item.Id, SelectedCondition.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Another condition already uses that id.");
            }

            var saved = await _conditionStore.SaveAsync(
                SelectedCondition.HasLocalFile ? SelectedCondition.LocalFilePath : null,
                SelectedCondition.ToDefinition());
            await ReloadConditionsAsync(saved.Id);
            LibrarySummary = "Saved Sussex condition.";
            LibraryDetail = saved.FilePath;
            LibraryLevel = OperationOutcomeKind.Success;
        }
        catch (Exception ex)
        {
            LibrarySummary = "Sussex condition save failed.";
            LibraryDetail = ex.Message;
            LibraryLevel = OperationOutcomeKind.Failure;
        }
    }

    private async Task LoadConditionAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load Sussex Condition",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(LibraryRootLabel)
                ? LibraryRootLabel
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var imported = await _conditionStore.ImportAsync(dialog.FileName);
            await ReloadConditionsAsync(imported.Id);
            LibrarySummary = "Loaded Sussex condition.";
            LibraryDetail = imported.FilePath;
            LibraryLevel = OperationOutcomeKind.Success;
        }
        catch (Exception ex)
        {
            LibrarySummary = "Sussex condition load failed.";
            LibraryDetail = ex.Message;
            LibraryLevel = OperationOutcomeKind.Failure;
        }
    }

    private async Task ShareSelectedAsync()
    {
        if (SelectedCondition is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Share Sussex Condition",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = SelectedCondition.Id + ".json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _conditionStore.ExportAsync(SelectedCondition.ToDefinition(), dialog.FileName);
        LibrarySummary = "Shared Sussex condition.";
        LibraryDetail = dialog.FileName;
        LibraryLevel = OperationOutcomeKind.Success;
    }

    private Task OpenFolderAsync()
    {
        Directory.CreateDirectory(LibraryRootLabel);
        Process.Start(new ProcessStartInfo
        {
            FileName = LibraryRootLabel,
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedCondition?.HasLocalFile != true)
        {
            return;
        }

        var removedId = SelectedCondition.Id;
        await _conditionStore.DeleteAsync(SelectedCondition.LocalFilePath);
        await ReloadConditionsAsync(removedId);
        LibrarySummary = SelectedCondition is not null && string.Equals(SelectedCondition.Id, removedId, StringComparison.OrdinalIgnoreCase)
            ? "Removed local condition override."
            : "Deleted local Sussex condition.";
        LibraryDetail = "Bundled conditions remain available; local-only conditions are removed from the list.";
        LibraryLevel = OperationOutcomeKind.Success;
    }

    private void RefreshProfileOptions()
    {
        VisualProfileOptions.Clear();
        foreach (var profile in _visualProfiles.Profiles)
        {
            VisualProfileOptions.Add(new SussexConditionProfileOptionViewModel(
                ToPortableProfileReference(profile.Id),
                profile.DisplayLabel,
                profile.SecondaryLabel));
        }

        ControllerBreathingProfileOptions.Clear();
        foreach (var profile in _controllerBreathingProfiles.Profiles)
        {
            ControllerBreathingProfileOptions.Add(new SussexConditionProfileOptionViewModel(
                ToPortableProfileReference(profile.Id),
                profile.DisplayLabel,
                profile.SecondaryLabel));
        }

        SyncSelectedProfileOptions();
    }

    private void SyncSelectedProfileOptions()
    {
        _suppressOptionWrite = true;
        try
        {
            SelectedVisualProfileOption = ResolveProfileOption(VisualProfileOptions, SelectedCondition?.VisualProfileId);
            SelectedControllerBreathingProfileOption = ResolveProfileOption(ControllerBreathingProfileOptions, SelectedCondition?.ControllerBreathingProfileId);
        }
        finally
        {
            _suppressOptionWrite = false;
        }
    }

    private void OnConditionItemChanged(SussexConditionItemViewModel item)
    {
        if (ReferenceEquals(item, SelectedCondition))
        {
            SyncSelectedProfileOptions();
        }

        OnPropertyChanged(nameof(ActiveConditionsSummary));
        RefreshActiveConditions();
        RaiseCommandStates();
    }

    private void RefreshActiveConditions()
    {
        OnPropertyChanged(nameof(ActiveConditionsSummary));
        ActiveConditionsChanged?.Invoke(
            this,
            ConditionItems
                .Where(item => item.IsActive)
                .Select(item => item.ToDefinition())
                .ToArray());
    }

    private bool TryGetDefaultProfileReferences(out string visualProfileId, out string controllerProfileId)
    {
        visualProfileId = VisualProfileOptions.FirstOrDefault()?.ReferenceId ?? string.Empty;
        controllerProfileId = ControllerBreathingProfileOptions.FirstOrDefault()?.ReferenceId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(visualProfileId) && !string.IsNullOrWhiteSpace(controllerProfileId))
        {
            return true;
        }

        LibrarySummary = "Cannot create a condition yet.";
        LibraryDetail = "Load at least one visual profile and one breathing profile before creating a condition.";
        LibraryLevel = OperationOutcomeKind.Warning;
        return false;
    }

    private string BuildUniqueConditionId(string seed)
    {
        var baseId = BuildConditionIdStem(seed);
        var id = baseId;
        var suffix = 2;
        while (ConditionItems.Any(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            id = $"{baseId}-{suffix}";
            suffix++;
        }

        return id;
    }

    private string BuildUniqueConditionLabel(string seed)
    {
        var label = string.IsNullOrWhiteSpace(seed) ? "New Condition" : seed.Trim();
        var candidate = label;
        var suffix = 2;
        while (ConditionItems.Any(item => string.Equals(item.Label, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{label} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private void RaiseCommandStates()
    {
        DuplicateSelectedCommand.RaiseCanExecuteChanged();
        SaveSelectedCommand.RaiseCanExecuteChanged();
        ShareSelectedCommand.RaiseCanExecuteChanged();
        DeleteSelectedCommand.RaiseCanExecuteChanged();
    }

    private static SussexConditionProfileOptionViewModel? ResolveProfileOption(
        IEnumerable<SussexConditionProfileOptionViewModel> options,
        string? reference)
        => options.FirstOrDefault(option =>
            string.Equals(option.ReferenceId, reference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option.Label, reference, StringComparison.OrdinalIgnoreCase));

    private static string ToPortableProfileReference(string profileId)
    {
        const string marker = "::";
        var markerIndex = profileId.LastIndexOf(marker, StringComparison.Ordinal);
        return markerIndex >= 0
            ? profileId[(markerIndex + marker.Length)..]
            : profileId;
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

    private static string BuildConditionIdStem(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "condition" : value.Trim().ToLowerInvariant();
        var chars = new List<char>(trimmed.Length);
        var previousDash = false;
        foreach (var character in trimmed)
        {
            if (char.IsLetterOrDigit(character))
            {
                chars.Add(character);
                previousDash = false;
                continue;
            }

            if (previousDash)
            {
                continue;
            }

            chars.Add('-');
            previousDash = true;
        }

        var stem = new string(chars.ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(stem) ? "condition" : stem;
    }
}

public sealed class SussexConditionProfileOptionViewModel
{
    public SussexConditionProfileOptionViewModel(string referenceId, string label, string detail)
    {
        ReferenceId = referenceId;
        Label = label;
        Detail = detail;
    }

    public string ReferenceId { get; }
    public string Label { get; }
    public string Detail { get; }
    public string DisplayLabel => $"{Label} ({ReferenceId})";

    public override string ToString() => DisplayLabel;
}

public sealed class SussexConditionItemViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions PropertiesJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly Action<SussexConditionItemViewModel> _onChanged;
    private string _id;
    private string _label;
    private string _description;
    private string _visualProfileId;
    private string _controllerBreathingProfileId;
    private string _propertiesText;
    private bool _isActive;
    private bool _isDirty;

    public SussexConditionItemViewModel(
        StudyConditionDefinition condition,
        SussexStudyConditionRecord? localRecord,
        bool isBundled,
        bool isLocalOverride,
        Action<SussexConditionItemViewModel> onChanged)
    {
        _id = condition.Id;
        _label = condition.Label;
        _description = condition.Description;
        _visualProfileId = condition.VisualProfileId;
        _controllerBreathingProfileId = condition.ControllerBreathingProfileId;
        _isActive = condition.IsActive;
        _propertiesText = JsonSerializer.Serialize(
            new Dictionary<string, string>(condition.Properties, StringComparer.OrdinalIgnoreCase),
            PropertiesJsonOptions);
        LocalFilePath = localRecord?.FilePath ?? string.Empty;
        LocalFileHash = localRecord?.FileHash ?? string.Empty;
        ModifiedAtUtc = localRecord?.ModifiedAtUtc;
        IsBundled = isBundled;
        IsLocalOverride = isLocalOverride;
        _onChanged = onChanged;
    }

    public bool IsBundled { get; }
    public bool IsLocalOverride { get; }
    public bool HasLocalFile => !string.IsNullOrWhiteSpace(LocalFilePath);
    public string LocalFilePath { get; }
    public string LocalFileHash { get; }
    public DateTimeOffset? ModifiedAtUtc { get; }

    public string Id
    {
        get => _id;
        set
        {
            if (SetProperty(ref _id, value?.Trim() ?? string.Empty))
            {
                MarkDirty();
                OnPropertyChanged(nameof(SecondaryLabel));
            }
        }
    }

    public string Label
    {
        get => _label;
        set
        {
            if (SetProperty(ref _label, value?.Trim() ?? string.Empty))
            {
                MarkDirty();
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(SecondaryLabel));
            }
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value ?? string.Empty))
            {
                MarkDirty();
            }
        }
    }

    public string VisualProfileId
    {
        get => _visualProfileId;
        set
        {
            if (SetProperty(ref _visualProfileId, value?.Trim() ?? string.Empty))
            {
                MarkDirty();
                OnPropertyChanged(nameof(SecondaryLabel));
            }
        }
    }

    public string ControllerBreathingProfileId
    {
        get => _controllerBreathingProfileId;
        set
        {
            if (SetProperty(ref _controllerBreathingProfileId, value?.Trim() ?? string.Empty))
            {
                MarkDirty();
                OnPropertyChanged(nameof(SecondaryLabel));
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                MarkDirty();
                OnPropertyChanged(nameof(ActiveLabel));
                OnPropertyChanged(nameof(SecondaryLabel));
            }
        }
    }

    public string PropertiesText
    {
        get => _propertiesText;
        set
        {
            if (SetProperty(ref _propertiesText, value ?? "{}"))
            {
                MarkDirty();
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(StatusLabel));
            }
        }
    }

    public string DisplayLabel => Label;
    public string ActiveLabel => IsActive ? "Active" : "Inactive";
    public string OriginLabel
        => IsLocalOverride
            ? "Local override"
            : IsBundled
                ? "Bundled condition"
                : "Local condition";

    public string SecondaryLabel
        => $"{ActiveLabel} | {OriginLabel} | visual {VisualProfileId} | breathing {ControllerBreathingProfileId}";

    public string StatusLabel
        => IsDirty
            ? "Unsaved changes"
            : HasLocalFile
                ? $"Saved local file: {LocalFilePath}"
                : "Bundled read-only source";

    public StudyConditionDefinition ToDefinition()
    {
        var properties = string.IsNullOrWhiteSpace(PropertiesText)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(PropertiesText, new JsonSerializerOptions
              {
                  PropertyNameCaseInsensitive = true
              }) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new StudyConditionDefinition(
            Id,
            Label,
            Description,
            VisualProfileId,
            ControllerBreathingProfileId,
            IsActive,
            properties);
    }

    private void MarkDirty()
    {
        IsDirty = true;
        _onChanged(this);
    }

    public override string ToString() => DisplayLabel;
}
