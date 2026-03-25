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
