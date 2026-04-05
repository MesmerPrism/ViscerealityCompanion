using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class SussexProfileWorkspaceInvalidEditTests
{
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

            using var workspace = new SussexVisualProfilesWorkspaceViewModel(CreateStudy(studyId), new ConnectedQuestControlService());
            await workspace.InitializeAsync();
            workspace.SelectedProfile = workspace.Profiles.First(profile => string.Equals(profile.Id, record.Id, StringComparison.OrdinalIgnoreCase));

            workspace.ApplySelectedCommand.Execute(null);
            await WaitForConditionAsync(() => new SussexVisualProfileApplyStateStore(studyId).Load() is not null, TimeSpan.FromSeconds(10));
            compiledCsvPath = workspace.LastCompiledCsvPath;

            var applyRecord = new SussexVisualProfileApplyStateStore(studyId).Load()
                ?? throw new InvalidOperationException("Expected a saved visual apply record.");
            var appliedDocument = compiler.CreateDocument(record.Document.Profile.Name, record.Document.Profile.Notes, applyRecord.RequestedValues);
            var compiled = compiler.Compile(appliedDocument);

            workspace.RefreshReportedTwinState(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SussexVisualTuningCompiler.ExpectedHotloadTargetKey] = compiled.CompactRuntimeConfigJson
            });

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

            using var workspace = new SussexControllerBreathingProfilesWorkspaceViewModel(CreateStudy(studyId), new ConnectedQuestControlService());
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
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
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
}
