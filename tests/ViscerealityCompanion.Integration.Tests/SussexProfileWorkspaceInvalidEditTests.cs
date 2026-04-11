using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class SussexProfileWorkspaceInvalidEditTests
{
    [Fact]
    public async Task Visual_workspace_loads_read_only_bundled_release_profiles_before_local_profiles()
    {
        var templatePath = AppAssetLocator.TryResolveSussexVisualTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex visual tuning template.");
        var compiler = new SussexVisualTuningCompiler(File.ReadAllText(templatePath));
        var store = new SussexVisualProfileStore(compiler);
        var studyId = $"visual-bundled-profiles-{Guid.NewGuid():N}";
        var bundledRoot = Path.Combine(Path.GetTempPath(), $"sussex-visual-bundle-{Guid.NewGuid():N}");
        var previousBundleRoot = Environment.GetEnvironmentVariable("VISCEREALITY_SUSSEX_VISUAL_PROFILE_BUNDLE_ROOT");
        var bundledName = $"ZZZ Bundled Visual {Guid.NewGuid():N}";
        var localName = $"ZZZ Local Visual {Guid.NewGuid():N}";

        SussexVisualProfileRecord? localRecord = null;
        try
        {
            Directory.CreateDirectory(bundledRoot);
            var bundledDocument = compiler.CreateDocument(
                bundledName,
                profileNotes: "Bundled release profile.",
                new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase)
                {
                    ["tracers_per_oscillator"] = 5d,
                    ["sphere_radius_max"] = 2.6d
                });
            await File.WriteAllTextAsync(
                Path.Combine(bundledRoot, "bundled-release-profile.json"),
                compiler.Serialize(bundledDocument));

            Environment.SetEnvironmentVariable("VISCEREALITY_SUSSEX_VISUAL_PROFILE_BUNDLE_ROOT", bundledRoot);
            localRecord = await store.CreateFromTemplateAsync(localName);

            using var workspace = new SussexVisualProfilesWorkspaceViewModel(CreateStudy(studyId), new ConnectedQuestControlService());
            await workspace.InitializeAsync();

            Assert.Equal("Bundled Sussex Baseline", workspace.Profiles[0].DisplayLabel);
            var bundledProfile = workspace.Profiles.FirstOrDefault(profile => profile.IsBundledProfile);
            Assert.NotNull(bundledProfile);
            Assert.Equal(bundledName, bundledProfile!.DisplayLabel);
            Assert.False(bundledProfile.IsWritableLocalProfile);
            Assert.Contains("Bundled profile", bundledProfile.SecondaryLabel, StringComparison.Ordinal);

            workspace.SelectedProfile = bundledProfile;

            Assert.False(workspace.SaveSelectedCommand.CanExecute(null));
            Assert.True(workspace.SetStartupProfileCommand.CanExecute(null));
            Assert.Contains(bundledName, workspace.DraftSourceSummary, StringComparison.Ordinal);
            Assert.Contains("read-only bundled profile", workspace.DraftDetail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VISCEREALITY_SUSSEX_VISUAL_PROFILE_BUNDLE_ROOT", previousBundleRoot);
            DeleteFileIfPresent(localRecord?.FilePath);
            DeleteDirectoryIfPresent(bundledRoot);
            DeleteStudySessionArtifacts(
                studyId,
                "sussex-visual-startup",
                "sussex-visual-apply");
        }
    }

    [Fact]
    public async Task Visual_workspace_loads_the_pinned_launch_profile_into_the_runtime_draft_on_startup()
    {
        var templatePath = AppAssetLocator.TryResolveSussexVisualTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex visual tuning template.");
        var compiler = new SussexVisualTuningCompiler(File.ReadAllText(templatePath));
        var store = new SussexVisualProfileStore(compiler);
        var studyId = $"visual-startup-load-{Guid.NewGuid():N}";
        var profileName = $"ZZZ Startup Visual {Guid.NewGuid():N}";

        SussexVisualProfileRecord? record = null;
        try
        {
            record = await store.SaveAsync(
                existingPath: null,
                profileName,
                notes: null,
                new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase)
                {
                    ["tracers_per_oscillator"] = 5d,
                    ["oblateness_by_radius_min"] = 0.3d,
                    ["sphere_radius_max"] = 3d,
                    ["particle_size_relative_to_radius"] = 0d
                });

            var startupState = new SussexVisualProfileStartupState(
                record.Id,
                record.Document.Profile.Name,
                DateTimeOffset.UtcNow,
                record.Document.Profile.Notes,
                new Dictionary<string, double>(record.Document.ControlValues, StringComparer.OrdinalIgnoreCase));
            new SussexVisualProfileStartupStateStore(studyId).Save(startupState);

            using var workspace = new SussexVisualProfilesWorkspaceViewModel(CreateStudy(studyId), new ConnectedQuestControlService());
            await workspace.InitializeAsync();

            Assert.NotNull(workspace.SelectedProfile);
            Assert.Equal(record.Id, workspace.SelectedProfile!.Id);
            Assert.False(workspace.HasUnsavedDraftChanges);
            Assert.Equal(
                "5",
                workspace.ComparisonRows.First(row => string.Equals(row.Field.Id, "tracers_per_oscillator", StringComparison.OrdinalIgnoreCase)).Selected);
            Assert.Equal(
                "3",
                workspace.ComparisonRows.First(row => string.Equals(row.Field.Id, "sphere_radius_max", StringComparison.OrdinalIgnoreCase)).Selected);
            Assert.Equal(
                "Off",
                workspace.ComparisonRows.First(row => string.Equals(row.Field.Id, "particle_size_relative_to_radius", StringComparison.OrdinalIgnoreCase)).Selected);
            Assert.Contains(profileName, workspace.DraftSourceSummary, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFileIfPresent(record?.FilePath);
            DeleteStudySessionArtifacts(
                studyId,
                "sussex-visual-startup",
                "sussex-visual-apply");
        }
    }

    [Fact]
    public async Task Visual_workspace_invalid_edit_only_marks_the_edited_row()
    {
        var templatePath = AppAssetLocator.TryResolveSussexVisualTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex visual tuning template.");
        var compiler = new SussexVisualTuningCompiler(File.ReadAllText(templatePath));
        var store = new SussexVisualProfileStore(compiler);
        var studyId = $"visual-invalid-edit-{Guid.NewGuid():N}";
        var profileName = $"ZZZ Invalid Visual {Guid.NewGuid():N}";

        SussexVisualProfileRecord? record = null;
        string? compiledCsvPath = null;
        try
        {
            record = await store.CreateFromTemplateAsync(profileName);

            using var workspace = new SussexVisualProfilesWorkspaceViewModel(
                CreateStudy(studyId),
                new ConnectedQuestControlService(),
                new ConnectedTwinModeBridge());
            await workspace.InitializeAsync();
            workspace.SelectedProfile = workspace.Profiles.First(profile => string.Equals(profile.Id, record.Id, StringComparison.OrdinalIgnoreCase));

            workspace.ApplySelectedCommand.Execute(null);
            await WaitForConditionAsync(() => new SussexVisualProfileApplyStateStore(studyId).Load() is not null, TimeSpan.FromSeconds(10));
            compiledCsvPath = workspace.LastCompiledCsvPath;

            var applyRecord = new SussexVisualProfileApplyStateStore(studyId).Load()
                ?? throw new InvalidOperationException("Expected a saved visual apply record.");
            var appliedDocument = compiler.CreateDocument(record.Document.Profile.Name, record.Document.Profile.Notes, applyRecord.RequestedValues);
            var compiled = compiler.Compile(appliedDocument);

            workspace.RefreshReportedTwinState(compiled.Entries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase));

            await WaitForConditionAsync(
                () => string.Equals(FindVisualRow(workspace, "sphere_deformation_enabled").Confirmation, "Confirmed", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));

            var minimumField = FindVisualField(workspace, "particle_size_min");
            var maximumField = FindVisualField(workspace, "particle_size_max");
            minimumField.SetValue(maximumField.Value + 0.01d, notify: true);

            await WaitForConditionAsync(
                () =>
                    string.Equals(workspace.ApplySummary, "Current visual values are invalid.", StringComparison.Ordinal) &&
                    string.Equals(FindVisualRow(workspace, "particle_size_min").Confirmation, "Edited", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));

            Assert.Equal("Edited", FindVisualRow(workspace, "particle_size_min").Confirmation);
            Assert.Equal("Confirmed", FindVisualRow(workspace, "particle_size_max").Confirmation);
            Assert.Equal("Confirmed", FindVisualRow(workspace, "sphere_deformation_enabled").Confirmation);
        }
        finally
        {
            DeleteFileIfPresent(record?.FilePath);
            DeleteFileIfPresent(compiledCsvPath);
            DeleteStudySessionArtifacts(
                studyId,
                "sussex-visual-startup",
                "sussex-visual-apply");
        }
    }

    [Fact]
    public async Task Visual_workspace_valid_single_edit_only_marks_the_edited_row()
    {
        var templatePath = AppAssetLocator.TryResolveSussexVisualTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex visual tuning template.");
        var compiler = new SussexVisualTuningCompiler(File.ReadAllText(templatePath));
        var store = new SussexVisualProfileStore(compiler);
        var studyId = $"visual-single-edit-{Guid.NewGuid():N}";
        var profileName = $"ZZZ Valid Visual {Guid.NewGuid():N}";

        SussexVisualProfileRecord? record = null;
        string? compiledCsvPath = null;
        try
        {
            record = await store.CreateFromTemplateAsync(profileName);

            using var workspace = new SussexVisualProfilesWorkspaceViewModel(
                CreateStudy(studyId),
                new ConnectedQuestControlService(),
                new ConnectedTwinModeBridge());
            await workspace.InitializeAsync();
            workspace.SelectedProfile = workspace.Profiles.First(profile => string.Equals(profile.Id, record.Id, StringComparison.OrdinalIgnoreCase));

            workspace.ApplySelectedCommand.Execute(null);
            await WaitForConditionAsync(() => new SussexVisualProfileApplyStateStore(studyId).Load() is not null, TimeSpan.FromSeconds(10));
            compiledCsvPath = workspace.LastCompiledCsvPath;

            var applyRecord = new SussexVisualProfileApplyStateStore(studyId).Load()
                ?? throw new InvalidOperationException("Expected a saved visual apply record.");
            var appliedDocument = compiler.CreateDocument(record.Document.Profile.Name, record.Document.Profile.Notes, applyRecord.RequestedValues);
            var compiled = compiler.Compile(appliedDocument);

            workspace.RefreshReportedTwinState(compiled.Entries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase));

            await WaitForConditionAsync(
                () => string.Equals(FindVisualRow(workspace, "sphere_deformation_enabled").Confirmation, "Confirmed", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));

            var editedField = FindVisualField(workspace, "sphere_radius_max");
            editedField.SetValue(editedField.Value - 0.1d, notify: true);

            await WaitForConditionAsync(
                () => string.Equals(FindVisualRow(workspace, "sphere_radius_max").Confirmation, "Edited", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));

            Assert.Equal("Edited", FindVisualRow(workspace, "sphere_radius_max").Confirmation);
            Assert.Equal("Confirmed", FindVisualRow(workspace, "sphere_radius_min").Confirmation);
            Assert.Equal("Confirmed", FindVisualRow(workspace, "sphere_deformation_enabled").Confirmation);
        }
        finally
        {
            DeleteFileIfPresent(record?.FilePath);
            DeleteFileIfPresent(compiledCsvPath);
            DeleteStudySessionArtifacts(
                studyId,
                "sussex-visual-startup",
                "sussex-visual-apply");
        }
    }

    [Fact]
    public async Task Controller_workspace_valid_single_edit_only_marks_the_edited_row()
    {
        var templatePath = AppAssetLocator.TryResolveSussexControllerBreathingTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex controller-breathing tuning template.");
        var compiler = new SussexControllerBreathingTuningCompiler(File.ReadAllText(templatePath));
        var store = new SussexControllerBreathingProfileStore(compiler);
        var studyId = $"controller-single-edit-{Guid.NewGuid():N}";
        var profileName = $"ZZZ Invalid Controller {Guid.NewGuid():N}";

        SussexControllerBreathingProfileRecord? record = null;
        string? compiledCsvPath = null;
        try
        {
            record = await store.CreateFromTemplateAsync(profileName);

            using var workspace = new SussexControllerBreathingProfilesWorkspaceViewModel(
                CreateStudy(studyId),
                new ConnectedQuestControlService(),
                new ConnectedTwinModeBridge());
            await workspace.InitializeAsync();
            workspace.SelectedProfile = workspace.Profiles.First(profile => string.Equals(profile.Id, record.Id, StringComparison.OrdinalIgnoreCase));

            workspace.ApplySelectedCommand.Execute(null);
            await WaitForConditionAsync(() => new SussexControllerBreathingProfileApplyStateStore(studyId).Load() is not null, TimeSpan.FromSeconds(10));
            compiledCsvPath = workspace.LastCompiledCsvPath;

            var applyRecord = new SussexControllerBreathingProfileApplyStateStore(studyId).Load()
                ?? throw new InvalidOperationException("Expected a saved controller-breathing apply record.");
            var appliedDocument = compiler.CreateDocument(record.Document.Profile.Name, record.Document.Profile.Notes, applyRecord.RequestedValues);
            var compiled = compiler.Compile(appliedDocument);

            workspace.RefreshReportedTwinState(compiled.Entries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase));

            await WaitForConditionAsync(
                () => string.Equals(FindControllerRow(workspace, "use_principal_axis_calibration").Confirmation, "Confirmed", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));

            var editedField = FindControllerField(workspace, "median_window");
            var nextValue = editedField.Value < editedField.Maximum
                ? editedField.Value + 1d
                : editedField.Value - 1d;
            editedField.SetValue(nextValue, notify: true);

            await WaitForConditionAsync(
                () => string.Equals(FindControllerRow(workspace, "median_window").Confirmation, "Edited", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));

            Assert.Equal("Edited", FindControllerRow(workspace, "median_window").Confirmation);
            Assert.Equal("Confirmed", FindControllerRow(workspace, "use_principal_axis_calibration").Confirmation);
        }
        finally
        {
            DeleteFileIfPresent(record?.FilePath);
            DeleteFileIfPresent(compiledCsvPath);
            DeleteStudySessionArtifacts(
                studyId,
                "sussex-controller-breathing-startup",
                "sussex-controller-breathing-apply");
        }
    }

    [Fact]
    public async Task Controller_workspace_exposes_dynamic_vs_fixed_axis_setup_in_profile_section()
    {
        var templatePath = AppAssetLocator.TryResolveSussexControllerBreathingTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex controller-breathing tuning template.");
        var compiler = new SussexControllerBreathingTuningCompiler(File.ReadAllText(templatePath));
        var store = new SussexControllerBreathingProfileStore(compiler);
        var studyId = $"controller-setup-summary-{Guid.NewGuid():N}";
        var profileName = $"ZZZ Setup Summary {Guid.NewGuid():N}";

        SussexControllerBreathingProfileRecord? record = null;
        try
        {
            record = await store.CreateFromTemplateAsync(profileName);

            using var workspace = new SussexControllerBreathingProfilesWorkspaceViewModel(
                CreateStudy(studyId),
                new ConnectedQuestControlService(),
                new ConnectedTwinModeBridge());
            await workspace.InitializeAsync();
            workspace.SelectedProfile = workspace.Profiles.First(profile => string.Equals(profile.Id, record.Id, StringComparison.OrdinalIgnoreCase));

            var modeField = FindControllerField(workspace, "use_principal_axis_calibration");
            var deltaField = FindControllerField(workspace, "min_accepted_delta");
            var travelField = FindControllerField(workspace, "min_acceptable_travel");

            Assert.Equal("Calibration Setup", modeField.Group);
            Assert.Equal("Use Dynamic Motion Axis", modeField.Label);
            Assert.Equal("Calibration Acceptance", deltaField.Group);
            Assert.Equal("Minimum Accepted Movement", deltaField.Label);
            Assert.Equal(0.0008d, deltaField.BaselineValue, 4);
            Assert.Equal("Calibration Acceptance", travelField.Group);
            Assert.Equal("Minimum Calibration Travel", travelField.Label);
            Assert.Equal(0.02d, travelField.BaselineValue, 3);
            Assert.Equal("Dynamic motion axis selected.", workspace.CalibrationModeSummary);
            Assert.Contains("0.8 mm", workspace.CalibrationAcceptanceSummary, StringComparison.Ordinal);
            Assert.Contains("2 cm", workspace.CalibrationAcceptanceSummary, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFileIfPresent(record?.FilePath);
            DeleteStudySessionArtifacts(
                studyId,
                "sussex-controller-breathing-startup",
                "sussex-controller-breathing-apply");
        }
    }

    [Fact]
    public async Task Controller_workspace_quick_mode_switch_applies_fixed_orientation_choice()
    {
        var templatePath = AppAssetLocator.TryResolveSussexControllerBreathingTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex controller-breathing tuning template.");
        var compiler = new SussexControllerBreathingTuningCompiler(File.ReadAllText(templatePath));
        var store = new SussexControllerBreathingProfileStore(compiler);
        var studyId = $"controller-quick-mode-{Guid.NewGuid():N}";

        SussexControllerBreathingProfileRecord? record = null;
        string? compiledCsvPath = null;
        try
        {
            record = await store.CreateFromTemplateAsync($"ZZZ Quick Mode {Guid.NewGuid():N}");

            using var workspace = new SussexControllerBreathingProfilesWorkspaceViewModel(
                CreateStudy(studyId),
                new ConnectedQuestControlService(),
                new ConnectedTwinModeBridge());
            await workspace.InitializeAsync();
            workspace.SelectedProfile = workspace.Profiles.First(profile => string.Equals(profile.Id, record.Id, StringComparison.OrdinalIgnoreCase));

            workspace.UseFixedOrientationCalibrationCommand.Execute(null);

            await WaitForConditionAsync(
                () => workspace.SelectedProfile?.Document.ControlValues.TryGetValue("use_principal_axis_calibration", out var savedValue) == true &&
                      Math.Abs(savedValue) < 0.000001d,
                TimeSpan.FromSeconds(10));

            Assert.NotNull(workspace.SelectedProfile);
            Assert.True(workspace.IsFixedControllerOrientationSelected);
            Assert.False(workspace.IsDynamicMotionAxisSelected);
            Assert.Equal(0d, workspace.SelectedProfile!.Document.ControlValues["use_principal_axis_calibration"], 6);
            Assert.Contains("Fixed controller orientation", workspace.CalibrationModeSummary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteFileIfPresent(record?.FilePath);
            DeleteFileIfPresent(compiledCsvPath);
            DeleteStudySessionArtifacts(
                studyId,
                "sussex-controller-breathing-startup",
                "sussex-controller-breathing-apply");
        }
    }

    private static SussexVisualProfileFieldViewModel FindVisualField(
        SussexVisualProfilesWorkspaceViewModel workspace,
        string fieldId)
        => workspace.Groups
            .SelectMany(group => group.Fields)
            .First(field => string.Equals(field.Id, fieldId, StringComparison.OrdinalIgnoreCase));

    private static SussexVisualComparisonRowViewModel FindVisualRow(
        SussexVisualProfilesWorkspaceViewModel workspace,
        string fieldId)
        => workspace.ComparisonRows
            .First(row => string.Equals(row.Field.Id, fieldId, StringComparison.OrdinalIgnoreCase));

    private static SussexControllerBreathingProfileFieldViewModel FindControllerField(
        SussexControllerBreathingProfilesWorkspaceViewModel workspace,
        string fieldId)
        => workspace.Groups
            .SelectMany(group => group.Fields)
            .First(field => string.Equals(field.Id, fieldId, StringComparison.OrdinalIgnoreCase));

    private static SussexControllerBreathingComparisonRowViewModel FindControllerRow(
        SussexControllerBreathingProfilesWorkspaceViewModel workspace,
        string fieldId)
        => workspace.ComparisonRows
            .First(row => string.Equals(row.Field.Id, fieldId, StringComparison.OrdinalIgnoreCase));

    private static StudyShellDefinition CreateStudy(string studyId)
        => new(
            studyId,
            "Sussex Test",
            "Sussex",
            "Test study shell definition.",
            new StudyPinnedApp(
                "Sussex",
                "com.Viscereality.SussexExperiment",
                string.Empty,
                string.Empty,
                string.Empty,
                "preview",
                string.Empty,
                AllowManualSelection: true,
                LaunchInKioskMode: false),
            new StudyPinnedDeviceProfile(
                "sussex-test-device-profile",
                "Sussex Test Device Profile",
                string.Empty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new StudyMonitoringProfile(
                ExpectedBreathingLabel: string.Empty,
                ExpectedHeartbeatLabel: string.Empty,
                ExpectedCoherenceLabel: string.Empty,
                ExpectedLslStreamName: string.Empty,
                ExpectedLslStreamType: string.Empty,
                RecenterDistanceThresholdUnits: 0d,
                LslConnectivityKeys: [],
                LslStreamNameKeys: [],
                LslStreamTypeKeys: [],
                LslValueKeys: [],
                ControllerValueKeys: [],
                ControllerStateKeys: [],
                ControllerTrackingKeys: [],
                AutomaticBreathingValueKeys: [],
                HeartbeatValueKeys: [],
                HeartbeatStateKeys: [],
                CoherenceValueKeys: [],
                CoherenceStateKeys: [],
                PerformanceFpsKeys: [],
                PerformanceFrameTimeKeys: [],
                PerformanceTargetFpsKeys: [],
                PerformanceRefreshRateKeys: [],
                RecenterDistanceKeys: [],
                ParticleVisibilityKeys: []),
            new StudyControlProfile(
                "recenter",
                "particles_on",
                "particles_off"));

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(condition(), "Timed out waiting for the expected workspace state.");
    }

    private static void DeleteFileIfPresent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        RetryIoCleanup(() =>
        {
            if (!File.Exists(path))
            {
                return true;
            }

            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return !File.Exists(path);
        });
    }

    private static void DeleteDirectoryIfPresent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        RetryIoCleanup(() =>
        {
            if (!Directory.Exists(path))
            {
                return true;
            }

            Directory.Delete(path, recursive: true);
            return !Directory.Exists(path);
        });
    }

    private static void RetryIoCleanup(Func<bool> action)
    {
        IOException? lastIo = null;
        UnauthorizedAccessException? lastUnauthorized = null;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (action())
                {
                    return;
                }
            }
            catch (IOException ex)
            {
                lastIo = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastUnauthorized = ex;
            }

            System.Threading.Thread.Sleep(100);
        }

        if (lastIo is not null)
        {
            throw lastIo;
        }

        if (lastUnauthorized is not null)
        {
            throw lastUnauthorized;
        }
    }

    private static void DeleteStudySessionArtifacts(string studyId, params string[] prefixes)
    {
        var sessionRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ViscerealityCompanion",
            "session");
        var token = studyId
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        var sanitizedStudyId = new string(token).Trim('-');

        foreach (var prefix in prefixes)
        {
            var path = Path.Combine(sessionRoot, $"{prefix}-{sanitizedStudyId}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class ConnectedQuestControlService : IQuestControlService
    {
        private readonly PreviewQuestControlService _preview = new();

        public Task<OperationOutcome> ProbeUsbAsync(CancellationToken cancellationToken = default)
            => _preview.ProbeUsbAsync(cancellationToken);

        public Task<OperationOutcome> DiscoverWifiAsync(CancellationToken cancellationToken = default)
            => _preview.DiscoverWifiAsync(cancellationToken);

        public Task<OperationOutcome> EnableWifiFromUsbAsync(CancellationToken cancellationToken = default)
            => _preview.EnableWifiFromUsbAsync(cancellationToken);

        public Task<OperationOutcome> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
            => _preview.ConnectAsync(endpoint, cancellationToken);

        public Task<OperationOutcome> ApplyPerformanceLevelsAsync(int cpuLevel, int gpuLevel, CancellationToken cancellationToken = default)
            => _preview.ApplyPerformanceLevelsAsync(cpuLevel, gpuLevel, cancellationToken);

        public Task<OperationOutcome> InstallAppAsync(QuestAppTarget target, CancellationToken cancellationToken = default)
            => _preview.InstallAppAsync(target, cancellationToken);

        public Task<OperationOutcome> InstallBundleAsync(
            QuestBundle bundle,
            IReadOnlyList<QuestAppTarget> targets,
            CancellationToken cancellationToken = default)
            => _preview.InstallBundleAsync(bundle, targets, cancellationToken);

        public Task<OperationOutcome> ApplyHotloadProfileAsync(
            HotloadProfile profile,
            QuestAppTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(
                OperationOutcomeKind.Success,
                $"Applied {profile.Label}.",
                $"Test transport uploaded {profile.File} to {target.PackageId}.",
                PackageId: target.PackageId));

        public Task<OperationOutcome> ClearHotloadOverrideAsync(
            QuestAppTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(
                OperationOutcomeKind.Success,
                $"Cleared staged override for {target.Label}.",
                $"Test transport removed runtime_overrides.csv for {target.PackageId}.",
                PackageId: target.PackageId));

        public Task<OperationOutcome> ApplyDeviceProfileAsync(DeviceProfile profile, CancellationToken cancellationToken = default)
            => _preview.ApplyDeviceProfileAsync(profile, cancellationToken);

        public Task<OperationOutcome> LaunchAppAsync(QuestAppTarget target, bool kioskMode = false, CancellationToken cancellationToken = default)
            => _preview.LaunchAppAsync(target, kioskMode, cancellationToken);

        public Task<OperationOutcome> StopAppAsync(QuestAppTarget target, bool exitKioskMode = false, CancellationToken cancellationToken = default)
            => _preview.StopAppAsync(target, exitKioskMode, cancellationToken);

        public Task<OperationOutcome> OpenBrowserAsync(string url, QuestAppTarget browserTarget, CancellationToken cancellationToken = default)
            => _preview.OpenBrowserAsync(url, browserTarget, cancellationToken);

        public Task<OperationOutcome> QueryForegroundAsync(CancellationToken cancellationToken = default)
            => _preview.QueryForegroundAsync(cancellationToken);

        public Task<InstalledAppStatus> QueryInstalledAppAsync(QuestAppTarget target, CancellationToken cancellationToken = default)
            => _preview.QueryInstalledAppAsync(target, cancellationToken);

        public Task<DeviceProfileStatus> QueryDeviceProfileStatusAsync(DeviceProfile profile, CancellationToken cancellationToken = default)
            => _preview.QueryDeviceProfileStatusAsync(profile, cancellationToken);

        public Task<HeadsetAppStatus> QueryHeadsetStatusAsync(
            QuestAppTarget? target,
            bool remoteOnlyControlEnabled,
            bool includeHostWifiStatus = true,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new HeadsetAppStatus(
                IsConnected: true,
                ConnectionLabel: "Test Quest",
                DeviceModel: "Quest Test",
                BatteryLevel: 92,
                CpuLevel: 2,
                GpuLevel: 2,
                ForegroundPackageId: target?.PackageId ?? "com.Viscereality.SussexExperiment",
                IsTargetInstalled: target is not null,
                IsTargetRunning: target is not null,
                IsTargetForeground: target is not null,
                RemoteOnlyControlEnabled: remoteOnlyControlEnabled,
                Timestamp: DateTimeOffset.UtcNow,
                Summary: "Connected test status.",
                Detail: "Headset connection ready for apply tests.",
                IsAwake: true,
                IsInteractive: true,
                Wakefulness: "Awake",
                DisplayPowerState: "ON",
                PowerStatusDetail: "wakefulness Awake; interactive true; display ON",
                Controllers:
                [
                    new QuestControllerStatus("Left", 88, "CONNECTED_ACTIVE", "test-left"),
                    new QuestControllerStatus("Right", 91, "CONNECTED_ACTIVE", "test-right")
                ],
                SoftwareVersion: "14 | build test",
                SoftwareReleaseOrCodename: "14",
                SoftwareBuildId: "test",
                SoftwareDisplayId: "test"));

        public Task<OperationOutcome> RunUtilityAsync(
            QuestUtilityAction action,
            bool allowWakeResumeTarget = true,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(
                OperationOutcomeKind.Success,
                $"{action} completed.",
                "Test utility transport succeeded."));
    }

    private sealed class ConnectedTwinModeBridge : ITwinModeBridge
    {
        public TwinBridgeStatus Status { get; } = new(
            IsAvailable: true,
            UsesPrivateImplementation: false,
            Summary: "Connected test twin bridge.",
            Detail: "Test twin bridge publishes runtime config snapshots successfully.");

        public Task<OperationOutcome> SendCommandAsync(TwinModeCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(
                OperationOutcomeKind.Success,
                $"Sent {command.DisplayName}.",
                "Test twin bridge command succeeded."));

        public Task<OperationOutcome> ApplyConfigAsync(
            HotloadProfile profile,
            QuestAppTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(
                OperationOutcomeKind.Success,
                $"Tracked {profile.Label}.",
                $"Test twin bridge staged {profile.File} for {target.PackageId}.",
                PackageId: target.PackageId));

        public Task<OperationOutcome> PublishRuntimeConfigAsync(
            RuntimeConfigProfile profile,
            QuestAppTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(
                OperationOutcomeKind.Success,
                $"Published runtime config: {profile.Label}.",
                $"Test twin bridge published {profile.Entries.Count} entries for {target.PackageId}.",
                PackageId: target.PackageId));
    }
}
