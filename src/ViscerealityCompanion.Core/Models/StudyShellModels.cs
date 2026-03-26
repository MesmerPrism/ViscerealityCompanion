namespace ViscerealityCompanion.Core.Models;

public sealed record StudyShellCatalog(
    StudyShellSource Source,
    IReadOnlyList<StudyShellDefinition> Studies);

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
    StudyControlProfile Controls)
{
    public override string ToString() => Label;
}

public sealed record StudyPinnedApp(
    string Label,
    string PackageId,
    string ApkPath,
    string LaunchComponent,
    string Sha256,
    string VersionName,
    string Notes);

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
    IReadOnlyList<string> HeartbeatValueKeys,
    IReadOnlyList<string> HeartbeatStateKeys,
    IReadOnlyList<string> CoherenceValueKeys,
    IReadOnlyList<string> CoherenceStateKeys,
    IReadOnlyList<string> RecenterDistanceKeys,
    IReadOnlyList<string> ParticleVisibilityKeys);

public sealed record StudyControlProfile(
    string RecenterCommandActionId,
    string ParticleVisibleOnActionId,
    string ParticleVisibleOffActionId);
