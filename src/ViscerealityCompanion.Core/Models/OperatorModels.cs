namespace ViscerealityCompanion.Core.Models;

public sealed record QuestSessionKitCatalog(
    QuestSessionKitSource Source,
    IReadOnlyList<QuestAppTarget> Apps,
    IReadOnlyList<QuestBundle> Bundles,
    IReadOnlyList<HotloadProfile> HotloadProfiles,
    IReadOnlyList<DeviceProfile> DeviceProfiles);

public sealed record QuestSessionKitSource(string Label, string RootPath)
{
    public override string ToString() => Label;
}

public sealed record QuestAppTarget(
    string Id,
    string Label,
    string PackageId,
    string ApkFile,
    string LaunchComponent,
    string BrowserPackageId,
    string Description,
    IReadOnlyList<string> Tags,
    string? ApkSha256 = null,
    ApkCompatibilityStatus CompatibilityStatus = ApkCompatibilityStatus.Unclassified,
    string? CompatibilityProfile = null,
    string? CompatibilityNotes = null,
    StudyVerificationBaseline? VerificationBaseline = null)
{
    public override string ToString() => Label;
}

public enum ApkCompatibilityStatus
{
    Unclassified,
    Compatible,
    Incompatible
}

public sealed record QuestBundle(
    string Id,
    string Label,
    string Description,
    IReadOnlyList<string> AppIds)
{
    public override string ToString() => Label;
}

public sealed record HotloadProfile(
    string Id,
    string Label,
    string File,
    string Version,
    string Channel,
    bool StudyLock,
    string Description,
    IReadOnlyList<string> PackageIds)
{
    public override string ToString() => Label;

    public bool MatchesPackage(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return false;
        }

        return PackageIds.Any(candidate => string.Equals(candidate, packageId, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record DeviceProfile(
    string Id,
    string Label,
    string Description,
    IReadOnlyDictionary<string, string> Properties)
{
    public override string ToString() => Label;
}

public enum OperationOutcomeKind
{
    Preview,
    Success,
    Warning,
    Failure
}

public sealed record OperationOutcome(
    OperationOutcomeKind Kind,
    string Summary,
    string Detail,
    string? Endpoint = null,
    string? PackageId = null,
    IReadOnlyList<string>? Items = null)
{
    public IReadOnlyList<string> SafeItems => Items ?? Array.Empty<string>();
}

public sealed record QuestProximityStatus(
    bool Available,
    bool HoldActive,
    string VirtualState,
    bool IsAutosleepDisabled,
    string HeadsetState,
    int? AutoSleepTimeMs,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset? HoldUntilUtc,
    string StatusDetail,
    string LastBroadcastAction = "",
    int? LastBroadcastDurationMs = null,
    double? LastBroadcastAgeSeconds = null);

public enum QuestUtilityAction
{
    Home,
    Back,
    Wake,
    Sleep,
    ListInstalledPackages,
    Reboot
}

public sealed record LslMonitorSubscription(
    string StreamName,
    string StreamType,
    int ChannelIndex,
    string? ExactSourceId = null,
    string? SourceIdPrefix = null,
    bool PreferNewestMatch = true);

public sealed record LslStreamDiscoveryRequest(
    string? StreamName,
    string? StreamType,
    string? ExactSourceId = null,
    string? SourceIdPrefix = null,
    int Limit = 16,
    bool PreferNewestFirst = true);

public enum LslChannelFormat
{
    Unknown,
    Float32,
    String
}

public sealed record LslMonitorReading(
    string Status,
    string Detail,
    float? Value,
    float SampleRateHz,
    DateTimeOffset Timestamp,
    string? TextValue = null,
    IReadOnlyList<string>? SampleValues = null,
    LslChannelFormat ChannelFormat = LslChannelFormat.Unknown,
    double? SampleTimestampSeconds = null,
    double? ObservedLocalClockSeconds = null,
    string? ResolvedSourceId = null);

public sealed record LslVisibleStreamInfo(
    string Name,
    string Type,
    string SourceId,
    int ChannelCount,
    float SampleRateHz,
    double CreatedAtSeconds);

public sealed record QuestTwinStatePublisherInventory(
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    bool AnyPublisherVisible,
    bool ExpectedPublisherVisible,
    string ExpectedSourceId,
    string ExpectedSourceIdPrefix,
    IReadOnlyList<LslVisibleStreamInfo> VisiblePublishers);

public sealed record TwinModeCommand(string ActionId, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record TwinBridgeStatus(
    bool IsAvailable,
    bool UsesPrivateImplementation,
    string Summary,
    string Detail);

public enum OperatorLogLevel
{
    Info,
    Warning,
    Failure
}

public sealed record TwinSettingsDelta(
    string Key,
    string? Requested,
    string? Reported,
    bool Matches);

public sealed record TwinStateEvent(
    DateTimeOffset Timestamp,
    string Category,
    string Payload,
    string Detail);

public sealed record OperatorLogEntry(
    DateTimeOffset Timestamp,
    OperatorLogLevel Level,
    string Message,
    string Detail);
