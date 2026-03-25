using System.Collections.ObjectModel;
using System.Windows;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App.ViewModels;

public sealed class RuntimeConfigWorkspaceViewModel : ObservableObject
{
    private static readonly RuntimeConfigSectionDefinition[] SectionDefinitions =
    [
        new(
            "metadata",
            "Session Metadata",
            "Session metadata stays in the CSV so staged profiles remain traceable across Quest Session Kit runs.",
            RuntimeConfigInspectorPane.SessionRouting,
            [
                Text("hotload_profile_id", "Profile Id", "Stable profile identifier stored in the hotload file.", string.Empty),
                Text("hotload_profile_version", "Profile Version", "Human-readable profile version tag.", string.Empty),
                Text("hotload_profile_channel", "Profile Channel", "Channel marker such as dev, study, or showcase.", string.Empty),
                Toggle("hotload_profile_lock", "Profile Lock", "When enabled, the runtime rejects later hash drift for the active study session.", false)
            ]),
        new(
            "showcase",
            "Showcase Routing",
            "These are the public scene-routing keys owned by RuntimeBiofeedbackShowcaseRouter and the LSL inlet defaults.",
            RuntimeConfigInspectorPane.SessionRouting,
            [
                Choice("showcase_breathing_mode", "Breathing Mode", "0 Controller State, 1 Controller Volume, 2 Polar Volume, 3 Headset Motion, 4 Mock, 5 LSL Belt.", "4", ["0", "1", "2", "3", "4", "5"]),
                Choice("showcase_heartbeat_mode", "Heartbeat Mode", "0 Polar H10, 1 Mock, 2 Headset Motion.", "1", ["0", "1", "2"]),
                Choice("showcase_coherence_mode", "Coherence Mode", "0 Heartbeat Derived, 1 Mock.", "1", ["0", "1"]),
                Toggle("showcase_adaptive_pacer_enabled", "Adaptive Pacer Enabled", "Enable the runtime adaptive breathing pacer.", false),
                Text("showcase_lsl_in_stream_name", "LSL In Stream Name", "Default inbound biofeedback LSL stream name.", "quest_biofeedback_in"),
                Text("showcase_lsl_in_stream_type", "LSL In Stream Type", "Default inbound biofeedback LSL stream type.", "quest.biofeedback"),
                Toggle("showcase_lsl_in_auto_connect", "LSL Auto Connect", "Attempt automatic inbound LSL connect on start.", false),
                Toggle("showcase_lsl_in_auto_reconnect", "LSL Auto Reconnect", "Reconnect when the inbound LSL stream disappears.", true),
                Text("showcase_lsl_in_default_channel", "LSL Default Channel", "Default channel index consumed by the LSL breathing source.", "0"),
                Text("showcase_active_config_index", "Active Config Index", "Current runtime config slot selected inside the Viscereality scene.", "0")
            ]),
        new(
            "twin",
            "Twin Link",
            "Twin direction and apply policy stay public even though the live headset-side coupling runtime remains private.",
            RuntimeConfigInspectorPane.TwinTiming,
            [
                Choice("twin_sync_mode", "Twin Sync Mode", "0 APK -> Playmode, 1 Auto, 2 Playmode -> APK.", "2", ["0", "1", "2"]),
                Toggle("twin_auto_first_sync_apk_priority", "Prefer Remote First Sync", "In Auto mode, prefer the Quest snapshot on first sync.", true),
                Toggle("twin_parameter_apply_enabled", "Twin Parameter Apply", "Allow incoming twin parameter and routing application.", false),
                Toggle("twin_signal_mirror_enabled", "Twin Signal Mirror", "Allow incoming twin signal mirroring into the local registry.", true)
            ]),
        new(
            "study",
            "Study / HUD",
            "Study-mode keys suppress debugging overhead and control which HUD or runtime pages remain visible during research sessions.",
            RuntimeConfigInspectorPane.ApkRuntime,
            [
                Toggle("study_runtime_logging_enabled", "Runtime Logging", "Enable runtime logging in the headset app.", false),
                Toggle("study_enable_terminal_command_menu", "Terminal Command Menu", "Expose the in-headset terminal command menu.", false),
                Toggle("study_include_engine_runtime_debug_page", "Engine Debug Page", "Include the engine-runtime debug HUD page.", false),
                Toggle("study_include_runtime_events_page", "Runtime Events Page", "Include the runtime events HUD page.", false),
                Toggle("study_echo_runtime_events_to_console", "Echo Events To Console", "Mirror runtime events into the Unity or adb console.", false),
                Toggle("study_log_runtime_hotload_console", "Hotload Console Logging", "Log hotload file activity to the console.", false),
                Toggle("study_log_runtime_hotload_applied_entries", "Log Applied Entries", "Log per-entry hotload application results.", false),
                Toggle("study_watch_hotload_file_changes", "Watch Hotload Files", "Keep watching the control file for new staged profile changes.", false)
            ]),
        new(
            "performance",
            "Quest Performance",
            "These keys line up with the public RuntimeHotloadBindingTarget performance surface used for Quest study and profiling passes.",
            RuntimeConfigInspectorPane.Headset,
            [
                Toggle("performance_hints_enabled", "Performance Hints Enabled", "Allow the app runtime to reapply Quest CPU/GPU hint levels.", true),
                Choice("performance_hint_cpu_level", "CPU Hint Level", "Quest CPU hint level (0..4).", "2", ["0", "1", "2", "3", "4"]),
                Choice("performance_hint_gpu_level", "GPU Hint Level", "Quest GPU hint level (0..4).", "2", ["0", "1", "2", "3", "4"]),
                Toggle("performance_hint_write_direct_levels", "Write Direct Levels", "Also write direct OVRManager cpuLevel and gpuLevel after suggested hints.", true),
                Text("performance_hint_reapply_seconds", "Hint Reapply Seconds", "Reapply cadence for Quest performance hints.", "2.0")
            ]),
        new(
            "display",
            "Display + Foveation",
            "Display refresh and foveation remain public because they are runtime policy rather than part of the private coupling implementation.",
            RuntimeConfigInspectorPane.Headset,
            [
                Toggle("display_refresh_request_enabled", "Display Refresh Request", "Allow the runtime to request a specific display refresh rate.", false),
                Text("display_refresh_request_hz", "Display Refresh Hz", "Requested display refresh rate.", "72.0"),
                Text("display_refresh_request_reapply_seconds", "Refresh Reapply Seconds", "How often the request is re-applied.", "5.0"),
                Toggle("quest_foveation_enabled", "Foveation Enabled", "Allow the runtime to manage Quest foveation settings.", false),
                Choice("quest_foveation_level", "Foveation Level", "Quest foveation level (0..4).", "1", ["0", "1", "2", "3", "4"]),
                Toggle("quest_foveation_dynamic", "Dynamic Foveation", "Enable dynamic foveation in runtime policy.", false),
                Text("quest_foveation_reapply_seconds", "Foveation Reapply Seconds", "How often the foveation request is refreshed.", "2.0")
            ]),
        new(
            "render",
            "Render + Transparency",
            "Public rendering and transparency keys matter for profiling, study reproducibility, and what the operator can safely tune from Windows.",
            RuntimeConfigInspectorPane.ApkRuntime,
            [
                Choice("render_overdraw_footprint_mode", "Overdraw Footprint Mode", "0 = quad radial clip, 1 = disc polygon.", "1", ["0", "1"]),
                Text("render_overdraw_disc_segments", "Disc Segments", "Polygon detail for disc overdraw mode.", "12"),
                Text("render_overdraw_radial_clip", "Radial Clip", "Radial clip amount used by the overdraw footprint.", "1.0"),
                Choice("render_transparency_blend_mode", "Transparency Blend Mode", "Blend-mode enum used by runtime transparency handling.", "0", ["0", "1", "2", "3"]),
                Choice("render_transparency_composition_mode", "Transparency Composition", "Composition enum for transparency pass behavior.", "1", ["0", "1"]),
                Text("render_transparency_depth_suppression_strength", "Depth Suppression", "Depth suppression strength for transparency sorting artifacts.", "1.5"),
                Choice("render_transparency_sort_mode", "Transparency Sort Mode", "Transparency sort mode enum.", "2", ["0", "1", "2"]),
                Choice("render_transparency_sort_implementation", "Sort Implementation", "Transparency sort implementation enum.", "1", ["0", "1"]),
                Text("render_transparency_sort_interval_frames", "Sort Interval Frames", "How often transparency sorting is recomputed.", "1")
            ]),
        new(
            "unity",
            "Unity Runtime",
            "These keys expose the Unity-side runtime policy surface used by the Astral scene and keep it available from the operator app.",
            RuntimeConfigInspectorPane.ApkRuntime,
            [
                Toggle("unity_run_in_background_enabled", "Run In Background Gate", "Enable or disable background-run policy writes.", false),
                Toggle("unity_run_in_background", "Run In Background", "Requested Unity run-in-background value.", true),
                Toggle("unity_target_frame_rate_enabled", "Target Frame Rate Gate", "Enable writes to Unity target frame rate.", false),
                Text("unity_target_frame_rate", "Target Frame Rate", "Requested Unity target frame rate.", "90"),
                Toggle("unity_background_loading_priority_enabled", "Background Loading Gate", "Enable writes to Unity background loading priority.", false),
                Choice("unity_background_loading_priority", "Background Loading Priority", "Unity background loading priority enum (0..4).", "1", ["0", "1", "2", "3", "4"]),
                Toggle("unity_sleep_timeout_enabled", "Sleep Timeout Gate", "Enable writes to Unity sleep timeout policy.", false),
                Choice("unity_sleep_timeout_mode", "Sleep Timeout Mode", "0 = system, 1 = never sleep, 2 = fixed seconds.", "0", ["0", "1", "2"]),
                Text("unity_sleep_timeout_seconds", "Sleep Timeout Seconds", "Sleep timeout when mode 2 is active.", "300"),
                Text("unity_settings_reapply_seconds", "Unity Reapply Seconds", "How often Unity runtime policy is refreshed.", "2.0")
            ]),
        new(
            "timing",
            "Simulation Timing",
            "These keys shape the public runtime timing envelope without exposing the private coupling job implementation.",
            RuntimeConfigInspectorPane.TwinTiming,
            [
                Toggle("internal_tick_lock_60hz", "Lock 60 Hz", "Keep the internal update loop pinned to 60 Hz.", true),
                Text("internal_tick_rate_hz", "Tick Rate Hz", "Requested internal simulation tick rate.", "60"),
                Toggle("internal_tick_use_xr_refresh_rate", "Use XR Refresh", "When enabled, runtime timing tracks the active XR display refresh rate.", false),
                Text("internal_tick_xr_probe_interval_seconds", "XR Probe Interval", "How often the runtime re-queries XR refresh rate.", "1.0"),
                Text("internal_tick_max_catchup", "Max Catch-Up", "Maximum backlog catch-up steps per frame.", "1"),
                Text("internal_tick_drop_backlog_seconds", "Drop Backlog Seconds", "Backlog threshold after which extra delayed time is discarded.", "0.25"),
                Text("internal_simulation_time_scale", "Simulation Time Scale", "Public simulation speed multiplier.", "1.0"),
                Text("internal_non_coupling_decimation", "Non-Coupling Decimation", "Public decimation knob for non-coupling work.", "1")
            ]),
        new(
            "advanced",
            "Runtime JSON Bridge",
            "The serialized runtime-config bridge key remains public. Only the actual coupling jobs and their live orchestration stay private.",
            RuntimeConfigInspectorPane.TwinTiming,
            [
                Multiline("showcase_active_runtime_config_json", "Active Runtime Config JSON", "Serialized runtime-config payload mirrored by ParticleEngineRuntimeConfigHotloadBridge.", string.Empty)
            ])
    ];

    private readonly RuntimeConfigCatalogLoader _catalogLoader = new();
    private readonly RuntimeConfigWriter _writer = new();
    private string _catalogStatus = "Loading Astral runtime config profiles...";
    private string _catalogSourcePath = string.Empty;
    private string _selectedProfileSummary = "No runtime config selected.";
    private string _lastExportPath = "No runtime config export written yet.";
    private RuntimeConfigProfile? _selectedProfile;
    private readonly ObservableCollection<RuntimeConfigSectionViewModel> _sessionRoutingSections = new();
    private readonly ObservableCollection<RuntimeConfigSectionViewModel> _headsetSections = new();
    private readonly ObservableCollection<RuntimeConfigSectionViewModel> _apkRuntimeSections = new();
    private readonly ObservableCollection<RuntimeConfigSectionViewModel> _twinTimingSections = new();
    private readonly ObservableCollection<RuntimeConfigSectionViewModel> _allSections = new();

    public ObservableCollection<RuntimeConfigProfile> Profiles { get; } = new();

    public ObservableCollection<RuntimeConfigSectionViewModel> Sections { get; } = new();

    public ObservableCollection<RuntimeConfigSectionViewModel> SessionRoutingSections => _sessionRoutingSections;

    public ObservableCollection<RuntimeConfigSectionViewModel> HeadsetSections => _headsetSections;

    public ObservableCollection<RuntimeConfigSectionViewModel> ApkRuntimeSections => _apkRuntimeSections;

    public ObservableCollection<RuntimeConfigSectionViewModel> TwinTimingSections => _twinTimingSections;

    public ObservableCollection<RuntimeConfigSectionViewModel> AllSections => _allSections;

    public string CatalogStatus
    {
        get => _catalogStatus;
        private set => SetProperty(ref _catalogStatus, value);
    }

    public string CatalogSourcePath
    {
        get => _catalogSourcePath;
        private set => SetProperty(ref _catalogSourcePath, value);
    }

    public string SelectedProfileSummary
    {
        get => _selectedProfileSummary;
        private set => SetProperty(ref _selectedProfileSummary, value);
    }

    public string LastExportPath
    {
        get => _lastExportPath;
        private set => SetProperty(ref _lastExportPath, value);
    }

    public RuntimeConfigProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                RebuildSections();
                OnPropertyChanged(nameof(SelectedProfileLabel));
            }
        }
    }

    public string SelectedProfileLabel => SelectedProfile?.Label ?? "No runtime config selected.";

    public async Task LoadAsync(
        string questSessionKitRoot,
        IReadOnlyList<HotloadProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        var catalog = await _catalogLoader.LoadAsync(questSessionKitRoot, profiles, cancellationToken).ConfigureAwait(false);

        await DispatchAsync(() =>
        {
            var previousId = SelectedProfile?.Id;

            Profiles.Clear();
            foreach (var profile in catalog.Profiles)
            {
                Profiles.Add(profile);
            }

            CatalogStatus = $"Loaded {catalog.Source.Label}: {catalog.Profiles.Count} runtime config profile(s).";
            CatalogSourcePath = catalog.Source.RootPath;
            SelectedProfile = Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, previousId, StringComparison.OrdinalIgnoreCase))
                ?? Profiles.FirstOrDefault();

            if (SelectedProfile is null)
            {
                Sections.Clear();
                _sessionRoutingSections.Clear();
                _headsetSections.Clear();
                _apkRuntimeSections.Clear();
                _twinTimingSections.Clear();
                _allSections.Clear();
                SelectedProfileSummary = "No runtime config profiles are available.";
            }
        }).ConfigureAwait(false);
    }

    public void SelectProfile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        SelectedProfile = Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
            ?? SelectedProfile;
    }

    public void SelectProfileForPackage(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            SelectedProfile = null;
            return;
        }

        if (SelectedProfile?.MatchesPackage(packageId) == true)
        {
            return;
        }

        SelectedProfile = Profiles.FirstOrDefault(profile => profile.MatchesPackage(packageId));
    }

    public void ResetSelectedProfile()
    {
        RebuildSections();
    }

    public RuntimeConfigProfile BuildEditedProfile()
    {
        var selectedProfile = SelectedProfile ?? throw new InvalidOperationException("Select a runtime config profile first.");
        var rowLookup = Sections
            .SelectMany(section => section.Rows)
            .ToDictionary(row => row.Key, StringComparer.OrdinalIgnoreCase);

        var entries = new List<RuntimeConfigEntry>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in SectionDefinitions.SelectMany(section => section.Settings))
        {
            if (!rowLookup.TryGetValue(definition.Key, out var row))
            {
                continue;
            }

            var value = GetRowValue(row);
            if (string.IsNullOrWhiteSpace(value) && definition.EditorKind is RuntimeSettingEditorKind.Text or RuntimeSettingEditorKind.Multiline)
            {
                continue;
            }

            entries.Add(new RuntimeConfigEntry(definition.Key, value));
            seenKeys.Add(definition.Key);
        }

        foreach (var row in Sections
                     .Where(section => string.Equals(section.SectionId, "additional", StringComparison.OrdinalIgnoreCase))
                     .SelectMany(section => section.Rows))
        {
            var value = GetRowValue(row);
            if (string.IsNullOrWhiteSpace(value) || !seenKeys.Add(row.Key))
            {
                continue;
            }

            entries.Add(new RuntimeConfigEntry(row.Key, value));
        }

        return new RuntimeConfigProfile(
            GetEntryValue(entries, "hotload_profile_id", selectedProfile.Id),
            selectedProfile.Label,
            selectedProfile.File,
            GetEntryValue(entries, "hotload_profile_version", selectedProfile.Version),
            GetEntryValue(entries, "hotload_profile_channel", selectedProfile.Channel),
            bool.TryParse(GetEntryValue(entries, "hotload_profile_lock", selectedProfile.StudyLock ? "true" : "false"), out var studyLock) && studyLock,
            selectedProfile.Description,
            selectedProfile.PackageIds,
            entries);
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        var profile = await DispatchAsync(BuildEditedProfile).ConfigureAwait(false);
        var path = await _writer.WriteAsync(profile, cancellationToken).ConfigureAwait(false);
        await DispatchAsync(() => LastExportPath = path).ConfigureAwait(false);
        return path;
    }

    private void RebuildSections()
    {
        Sections.Clear();
        _sessionRoutingSections.Clear();
        _headsetSections.Clear();
        _apkRuntimeSections.Clear();
        _twinTimingSections.Clear();
        _allSections.Clear();

        if (SelectedProfile is null)
        {
            SelectedProfileSummary = "No runtime config selected.";
            return;
        }

        var entryLookup = new Dictionary<string, RuntimeConfigEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in SelectedProfile.Entries)
        {
            entryLookup[entry.Key] = entry;
        }

        var builtSections = new List<(RuntimeConfigInspectorPane Pane, RuntimeConfigSectionViewModel Section)>();

        foreach (var section in SectionDefinitions)
        {
            var rows = new List<ConfigSettingRowViewModel>();
            foreach (var setting in section.Settings)
            {
                entryLookup.TryGetValue(setting.Key, out var existingEntry);
                var rawValue = existingEntry?.Value ?? setting.DefaultValue;
                rows.Add(BuildRow(setting, rawValue));
                entryLookup.Remove(setting.Key);
            }

            var sectionViewModel = new RuntimeConfigSectionViewModel(section.Id, section.Title, section.Description, rows);
            Sections.Add(sectionViewModel);
            builtSections.Add((section.Pane, sectionViewModel));
        }

        if (entryLookup.Count > 0)
        {
            var additionalRows = entryLookup.Values
                .Select(entry => new SingleValueRowViewModel(
                    entry.Key,
                    entry.Key,
                    "Additional key preserved from the source profile.",
                    entry.Value))
                .Cast<ConfigSettingRowViewModel>()
                .ToArray();

            var additionalSection = new RuntimeConfigSectionViewModel(
                "additional",
                "Additional Keys",
                "Keys outside the current public schema stay editable and are preserved on export.",
                additionalRows);

            Sections.Add(additionalSection);
            builtSections.Add((RuntimeConfigInspectorPane.TwinTiming, additionalSection));
        }

        foreach (var entry in builtSections)
        {
            _allSections.Add(entry.Section);
            switch (entry.Pane)
            {
                case RuntimeConfigInspectorPane.SessionRouting:
                    _sessionRoutingSections.Add(entry.Section);
                    break;
                case RuntimeConfigInspectorPane.Headset:
                    _headsetSections.Add(entry.Section);
                    break;
                case RuntimeConfigInspectorPane.ApkRuntime:
                    _apkRuntimeSections.Add(entry.Section);
                    break;
                case RuntimeConfigInspectorPane.TwinTiming:
                    _twinTimingSections.Add(entry.Section);
                    break;
            }
        }

        SelectedProfileSummary =
            $"{SelectedProfile.Label} ({SelectedProfile.Channel}/{SelectedProfile.Version}) — {SelectedProfile.Description} {SelectedProfile.Entries.Count} staged key(s).";
    }

    private static ConfigSettingRowViewModel BuildRow(RuntimeSettingDefinition definition, string rawValue)
        => definition.EditorKind switch
        {
            RuntimeSettingEditorKind.Toggle => new ToggleRowViewModel(
                definition.Key,
                definition.Label,
                definition.Description,
                bool.TryParse(rawValue, out var boolValue) && boolValue),
            RuntimeSettingEditorKind.Choice => new ChoiceRowViewModel(
                definition.Key,
                definition.Label,
                definition.Description,
                definition.Options ?? Array.Empty<string>(),
                string.IsNullOrWhiteSpace(rawValue) ? definition.DefaultValue : rawValue),
            RuntimeSettingEditorKind.Multiline => new MultilineRowViewModel(
                definition.Key,
                definition.Label,
                definition.Description,
                rawValue,
                minimumLines: 6),
            _ => new SingleValueRowViewModel(
                definition.Key,
                definition.Label,
                definition.Description,
                rawValue)
        };

    private static string GetRowValue(ConfigSettingRowViewModel row)
        => row switch
        {
            SingleValueRowViewModel single => single.ValueText.Trim(),
            ToggleRowViewModel toggle => toggle.Value ? "true" : "false",
            ChoiceRowViewModel choice => choice.SelectedOption.Trim(),
            MultilineRowViewModel multiline => string.Join(
                " ",
                multiline.ValueText
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            _ => string.Empty
        };

    private static string GetEntryValue(
        IEnumerable<RuntimeConfigEntry> entries,
        string key,
        string fallback)
        => entries.LastOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))?.Value ?? fallback;

    private static RuntimeSettingDefinition Text(string key, string label, string description, string defaultValue)
        => new(key, label, description, RuntimeSettingEditorKind.Text, defaultValue, null);

    private static RuntimeSettingDefinition Toggle(string key, string label, string description, bool defaultValue)
        => new(key, label, description, RuntimeSettingEditorKind.Toggle, defaultValue ? "true" : "false", null);

    private static RuntimeSettingDefinition Choice(
        string key,
        string label,
        string description,
        string defaultValue,
        string[] options)
        => new(key, label, description, RuntimeSettingEditorKind.Choice, defaultValue, options);

    private static RuntimeSettingDefinition Multiline(string key, string label, string description, string defaultValue)
        => new(key, label, description, RuntimeSettingEditorKind.Multiline, defaultValue, null);

    public void ApplyTwinDelta(IReadOnlyList<TwinSettingsDelta> deltas)
    {
        var deltaLookup = new Dictionary<string, TwinSettingsDelta>(StringComparer.OrdinalIgnoreCase);
        foreach (var delta in deltas)
        {
            deltaLookup[delta.Key] = delta;
        }

        foreach (var row in Sections.SelectMany(section => section.Rows))
        {
            if (deltaLookup.Count == 0)
            {
                row.SyncState = SettingSyncState.Inactive;
                continue;
            }

            if (!deltaLookup.TryGetValue(row.Key, out var delta))
            {
                row.SyncState = SettingSyncState.Unknown;
                continue;
            }

            if (delta.Reported is null)
            {
                row.SyncState = SettingSyncState.Unknown;
                continue;
            }

            var currentValue = GetRowValue(row);
            row.SyncState = string.Equals(currentValue, delta.Reported, StringComparison.Ordinal)
                ? SettingSyncState.Verified
                : SettingSyncState.Pending;
        }
    }

    private static Task DispatchAsync(Action action)
        => Application.Current.Dispatcher.InvokeAsync(action).Task;

    private static Task<T> DispatchAsync<T>(Func<T> action)
        => Application.Current.Dispatcher.InvokeAsync(action).Task;

    private sealed record RuntimeConfigSectionDefinition(
        string Id,
        string Title,
        string Description,
        RuntimeConfigInspectorPane Pane,
        IReadOnlyList<RuntimeSettingDefinition> Settings);

    private sealed record RuntimeSettingDefinition(
        string Key,
        string Label,
        string Description,
        RuntimeSettingEditorKind EditorKind,
        string DefaultValue,
        IReadOnlyList<string>? Options);

    private enum RuntimeSettingEditorKind
    {
        Text,
        Toggle,
        Choice,
        Multiline
    }

    private enum RuntimeConfigInspectorPane
    {
        SessionRouting,
        Headset,
        ApkRuntime,
        TwinTiming
    }
}

public sealed class RuntimeConfigSectionViewModel
{
    public RuntimeConfigSectionViewModel(
        string sectionId,
        string title,
        string description,
        IReadOnlyList<ConfigSettingRowViewModel> rows)
    {
        SectionId = sectionId;
        Title = title;
        Description = description;
        Rows = new ObservableCollection<ConfigSettingRowViewModel>(rows);
    }

    public string SectionId { get; }

    public string Title { get; }

    public string Description { get; }

    public ObservableCollection<ConfigSettingRowViewModel> Rows { get; }
}

public enum SettingSyncState
{
    Inactive,
    Unknown,
    Pending,
    Verified
}

public abstract class ConfigSettingRowViewModel : ObservableObject
{
    private SettingSyncState _syncState = SettingSyncState.Inactive;

    protected ConfigSettingRowViewModel(string key, string label, string description)
    {
        Key = key;
        Label = label;
        Description = description;
    }

    public string Key { get; }

    public string Label { get; }

    public string Description { get; }

    public SettingSyncState SyncState
    {
        get => _syncState;
        set => SetProperty(ref _syncState, value);
    }
}

public sealed class SingleValueRowViewModel : ConfigSettingRowViewModel
{
    private string _valueText;

    public SingleValueRowViewModel(string key, string label, string description, string valueText)
        : base(key, label, description)
    {
        _valueText = valueText;
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }
}

public sealed class ToggleRowViewModel : ConfigSettingRowViewModel
{
    private bool _value;

    public ToggleRowViewModel(string key, string label, string description, bool value)
        : base(key, label, description)
    {
        _value = value;
    }

    public bool Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

public sealed class ChoiceRowViewModel : ConfigSettingRowViewModel
{
    private string _selectedOption;

    public ChoiceRowViewModel(
        string key,
        string label,
        string description,
        IEnumerable<string> options,
        string selectedOption)
        : base(key, label, description)
    {
        Options = new ObservableCollection<string>(options);
        _selectedOption = selectedOption;
    }

    public ObservableCollection<string> Options { get; }

    public string SelectedOption
    {
        get => _selectedOption;
        set => SetProperty(ref _selectedOption, value);
    }
}

public sealed class MultilineRowViewModel : ConfigSettingRowViewModel
{
    private string _valueText;

    public MultilineRowViewModel(
        string key,
        string label,
        string description,
        string valueText,
        int minimumLines)
        : base(key, label, description)
    {
        _valueText = valueText;
        MinimumLines = minimumLines;
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    public int MinimumLines { get; }
}

public sealed class RangeRowViewModel : ConfigSettingRowViewModel
{
    private string _minimumText;
    private string _maximumText;

    public RangeRowViewModel(
        string key,
        string label,
        string description,
        string minimumText,
        string maximumText)
        : base(key, label, description)
    {
        _minimumText = minimumText;
        _maximumText = maximumText;
    }

    public string MinimumText
    {
        get => _minimumText;
        set => SetProperty(ref _minimumText, value);
    }

    public string MaximumText
    {
        get => _maximumText;
        set => SetProperty(ref _maximumText, value);
    }
}

public sealed class Vector3RowViewModel : ConfigSettingRowViewModel
{
    private string _xText;
    private string _yText;
    private string _zText;

    public Vector3RowViewModel(
        string key,
        string label,
        string description,
        string xText,
        string yText,
        string zText)
        : base(key, label, description)
    {
        _xText = xText;
        _yText = yText;
        _zText = zText;
    }

    public string XText
    {
        get => _xText;
        set => SetProperty(ref _xText, value);
    }

    public string YText
    {
        get => _yText;
        set => SetProperty(ref _yText, value);
    }

    public string ZText
    {
        get => _zText;
        set => SetProperty(ref _zText, value);
    }
}
