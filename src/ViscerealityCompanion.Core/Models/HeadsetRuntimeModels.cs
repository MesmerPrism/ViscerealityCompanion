namespace ViscerealityCompanion.Core.Models;

public sealed record HeadsetAppStatus(
    bool IsConnected,
    string ConnectionLabel,
    string DeviceModel,
    int? BatteryLevel,
    int? CpuLevel,
    int? GpuLevel,
    string ForegroundPackageId,
    bool IsTargetInstalled,
    bool IsTargetRunning,
    bool IsTargetForeground,
    bool RemoteOnlyControlEnabled,
    DateTimeOffset Timestamp,
    string Summary,
    string Detail,
    IReadOnlyDictionary<string, string>? ReportedSettings = null,
    IReadOnlyList<TwinSettingsDelta>? SettingsDelta = null,
    string? ForegroundComponent = null,
    IReadOnlyList<string>? VisibleActivityComponents = null);

public sealed record InstalledAppStatus(
    string PackageId,
    bool IsInstalled,
    string VersionName,
    string VersionCode,
    string InstalledSha256,
    string InstalledPath,
    string Summary,
    string Detail);

public sealed record DevicePropertyStatus(
    string Key,
    string ExpectedValue,
    string ReportedValue,
    bool Matches);

public sealed record DeviceProfileStatus(
    string ProfileId,
    string Label,
    bool IsActive,
    string Summary,
    string Detail,
    IReadOnlyList<DevicePropertyStatus> Properties);
