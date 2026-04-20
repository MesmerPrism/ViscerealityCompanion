using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class RuntimeConfigCatalogLoader
{
    public async Task<RuntimeConfigCatalog> LoadAsync(
        string questSessionKitRoot,
        IReadOnlyList<HotloadProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(questSessionKitRoot);
        ArgumentNullException.ThrowIfNull(profiles);

        var fullRoot = Path.GetFullPath(questSessionKitRoot);
        var hotloadRoot = Path.Combine(fullRoot, "HotloadProfiles");
        if (!Directory.Exists(hotloadRoot))
        {
            throw new DirectoryNotFoundException($"Hotload profile directory not found: {hotloadRoot}");
        }

        var runtimeProfiles = new List<RuntimeConfigProfile>(profiles.Count);
        foreach (var profile in profiles)
        {
            var path = Path.Combine(hotloadRoot, profile.File);
            var entries = File.Exists(path)
                ? await ParseEntriesAsync(path, cancellationToken).ConfigureAwait(false)
                : Array.Empty<RuntimeConfigEntry>();

            runtimeProfiles.Add(new RuntimeConfigProfile(
                profile.Id,
                profile.Label,
                profile.File,
                profile.Version,
                profile.Channel,
                profile.StudyLock,
                profile.Description,
                profile.PackageIds,
                entries));
        }

        return new RuntimeConfigCatalog(
            new RuntimeConfigSource("Runtime config profiles", hotloadRoot),
            runtimeProfiles);
    }

    internal static async Task<IReadOnlyList<RuntimeConfigEntry>> ParseEntriesAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<RuntimeConfigEntry>();
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("#", StringComparison.Ordinal) ||
                line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(line, "key,value", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(',');
            if (separatorIndex < 0)
            {
                separatorIndex = line.IndexOf('=');
            }

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            entries.Add(new RuntimeConfigEntry(key, value));
        }

        return entries;
    }
}
