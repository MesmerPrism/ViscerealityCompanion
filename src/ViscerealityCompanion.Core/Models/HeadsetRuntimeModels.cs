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
    IReadOnlyList<string>? VisibleActivityComponents = null,
    bool IsWifiAdbTransport = false,
    string HeadsetWifiSsid = "",
    string HeadsetWifiIpAddress = "",
    string HostWifiSsid = "",
    bool? WifiSsidMatchesHost = null,
    bool? IsAwake = null,
    bool? IsInteractive = null,
    string Wakefulness = "",
    string DisplayPowerState = "",
    string PowerStatusDetail = "",
    bool IsInWakeLimbo = false,
    IReadOnlyList<QuestControllerStatus>? Controllers = null,
    string SoftwareVersion = "",
    string SoftwareReleaseOrCodename = "",
    string SoftwareBuildId = "",
    string SoftwareDisplayId = "",
    int? ScreenBrightnessPercent = null,
    int? MediaVolumeLevel = null,
    int? MediaVolumeMax = null,
    bool IsUsbAdbVisible = false,
    string VisibleUsbSerial = "");

public sealed record QuestControllerStatus(
    string HandLabel,
    int? BatteryLevel,
    string ConnectionState,
    string DeviceId);

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
    bool Matches,
    bool BlocksActivation = true);

public sealed record DeviceProfileStatus(
    string ProfileId,
    string Label,
    bool IsActive,
    string Summary,
    string Detail,
    IReadOnlyList<DevicePropertyStatus> Properties);
