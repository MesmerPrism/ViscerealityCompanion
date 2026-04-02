namespace ViscerealityCompanion.Core.Models;

public sealed record RuntimeConfigCatalog(
    RuntimeConfigSource Source,
    IReadOnlyList<RuntimeConfigProfile> Profiles);

public sealed record RuntimeConfigSource(string Label, string RootPath)
{
    public override string ToString() => Label;
}

public sealed record RuntimeConfigProfile(
    string Id,
    string Label,
    string File,
    string Version,
    string Channel,
    bool StudyLock,
    string Description,
    IReadOnlyList<string> PackageIds,
    IReadOnlyList<RuntimeConfigEntry> Entries)
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

public sealed record RuntimeConfigEntry(string Key, string Value);

public sealed record LslRuntimeState(bool Available, string Detail);

public sealed record LslMonitorSession(
    nint Handle,
    string ResolvedName,
    string ResolvedType,
    string ResolvedSourceId,
    double CreatedAtSeconds,
    int ChannelCount,
    int SelectedChannelIndex,
    float SampleRateHz,
    LslChannelFormat ChannelFormat);

public sealed record LslMonitorSample(
    double TimestampSeconds,
    float? NumericValue,
    string? TextValue,
    IReadOnlyList<string> SampleValues,
    LslChannelFormat ChannelFormat,
    string? ResolvedSourceId = null);
