namespace ViscerealityCompanion.Core.Models;

public sealed record StudyShellCatalog(
    StudyShellSource Source,
    IReadOnlyList<StudyShellDefinition> Studies,
    StudyShellLaunchOptions LaunchOptions)
{
    public StudyShellCatalog(
        StudyShellSource Source,
        IReadOnlyList<StudyShellDefinition> Studies)
        : this(Source, Studies, new StudyShellLaunchOptions(string.Empty, false))
    {
    }
}

public sealed record StudyShellLaunchOptions(
    string StartupStudyId,
    bool LockToStartupStudy)
{
    public bool HasStartupStudy => !string.IsNullOrWhiteSpace(StartupStudyId);
}

public sealed record StudyShellSource(string Label, string RootPath)
{
    public override string ToString() => Label;
}

public sealed record StudyShellDefinition(
    string Id,
    string Label,
    string Partner,
    string Description,
    StudyPinnedApp App,
    StudyPinnedDeviceProfile DeviceProfile,
    StudyMonitoringProfile Monitoring,
    StudyControlProfile Controls,
    IReadOnlyList<StudyConditionDefinition>? Conditions = null)
{
    public IReadOnlyList<StudyConditionDefinition> Conditions { get; init; } =
        Conditions ?? Array.Empty<StudyConditionDefinition>();

    public override string ToString() => Label;
}

public sealed record StudyConditionDefinition
{
    public StudyConditionDefinition(
        string id,
        string label,
        string description,
        string visualProfileId,
        string controllerBreathingProfileId,
        bool isActive = true,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        Id = id;
        Label = label;
        Description = description;
        VisualProfileId = visualProfileId;
        ControllerBreathingProfileId = controllerBreathingProfileId;
        IsActive = isActive;
        Properties = properties is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase);
    }

    public string Id { get; init; }
    public string Label { get; init; }
    public string Description { get; init; }
    public string VisualProfileId { get; init; }
    public string ControllerBreathingProfileId { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyDictionary<string, string> Properties { get; init; }

    public override string ToString() => Label;
}

public sealed record StudyPinnedApp(
    string Label,
    string PackageId,
    string ApkPath,
    string LaunchComponent,
    string Sha256,
    string VersionName,
    string Notes,
    bool AllowManualSelection,
    bool LaunchInKioskMode,
    StudyVerificationBaseline? VerificationBaseline = null);

public sealed record StudyVerificationBaseline(
    string ApkSha256,
    string SoftwareVersion,
    string BuildId,
    string DisplayId,
    string DeviceProfileId,
    string EnvironmentHash,
    DateTimeOffset? VerifiedAtUtc,
    string VerifiedBy);

public sealed record StudyPinnedDeviceProfile(
    string Id,
    string Label,
    string Description,
    IReadOnlyDictionary<string, string> Properties);

public sealed record StudyMonitoringProfile(
    string ExpectedBreathingLabel,
    string ExpectedHeartbeatLabel,
    string ExpectedCoherenceLabel,
    string ExpectedLslStreamName,
    string ExpectedLslStreamType,
    double RecenterDistanceThresholdUnits,
    IReadOnlyList<string> LslConnectivityKeys,
    IReadOnlyList<string> LslStreamNameKeys,
    IReadOnlyList<string> LslStreamTypeKeys,
    IReadOnlyList<string> LslValueKeys,
    IReadOnlyList<string> ControllerValueKeys,
    IReadOnlyList<string> ControllerStateKeys,
    IReadOnlyList<string> ControllerTrackingKeys,
    IReadOnlyList<string> AutomaticBreathingValueKeys,
    IReadOnlyList<string> HeartbeatValueKeys,
    IReadOnlyList<string> HeartbeatStateKeys,
    IReadOnlyList<string> CoherenceValueKeys,
    IReadOnlyList<string> CoherenceStateKeys,
    IReadOnlyList<string> PerformanceFpsKeys,
    IReadOnlyList<string> PerformanceFrameTimeKeys,
    IReadOnlyList<string> PerformanceTargetFpsKeys,
    IReadOnlyList<string> PerformanceRefreshRateKeys,
    IReadOnlyList<string> RecenterDistanceKeys,
    IReadOnlyList<string> ParticleVisibilityKeys);

public sealed record StudyControlProfile(
    string RecenterCommandActionId,
    string ParticleVisibleOnActionId,
    string ParticleVisibleOffActionId,
    string StartBreathingCalibrationActionId = "",
    string ResetBreathingCalibrationActionId = "",
    string StartExperimentActionId = "",
    string EndExperimentActionId = "",
    string SetBreathingModeControllerVolumeActionId = "",
    string SetBreathingModeAutomaticCycleActionId = "",
    string StartAutomaticBreathingActionId = "",
    string PauseAutomaticBreathingActionId = "");
