using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class QuestSessionKitCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<QuestSessionKitCatalog> LoadAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return await LoadSingleRootAsync(rootPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QuestSessionKitCatalog> LoadMultipleAsync(
        IReadOnlyList<string> rootPaths,
        CancellationToken cancellationToken = default)
    {
        if (rootPaths.Count == 0)
        {
            throw new ArgumentException("At least one catalog root path is required.", nameof(rootPaths));
        }

        if (rootPaths.Count == 1)
        {
            return await LoadSingleRootAsync(rootPaths[0], cancellationToken).ConfigureAwait(false);
        }

        var catalogs = new List<QuestSessionKitCatalog>(rootPaths.Count);
        foreach (var rootPath in rootPaths)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                continue;
            }

            catalogs.Add(await LoadSingleRootAsync(rootPath, cancellationToken).ConfigureAwait(false));
        }

        if (catalogs.Count == 0)
        {
            throw new DirectoryNotFoundException($"None of the catalog roots exist: {string.Join(", ", rootPaths)}");
        }

        var seenAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mergedApps = new List<QuestAppTarget>();
        var mergedBundles = new List<QuestBundle>();
        var mergedHotloadProfiles = new List<HotloadProfile>();
        var mergedDeviceProfiles = new List<DeviceProfile>();

        foreach (var catalog in catalogs)
        {
            foreach (var app in catalog.Apps)
            {
                if (seenAppIds.Add(app.Id))
                {
                    mergedApps.Add(app);
                }
            }

            mergedBundles.AddRange(catalog.Bundles);
            mergedHotloadProfiles.AddRange(catalog.HotloadProfiles);
            mergedDeviceProfiles.AddRange(catalog.DeviceProfiles);
        }

        var primarySource = catalogs[0].Source;
        return new QuestSessionKitCatalog(
            new QuestSessionKitSource(
                $"{primarySource.Label} (+{catalogs.Count - 1} merged)",
                primarySource.RootPath),
            mergedApps,
            mergedBundles,
            mergedHotloadProfiles,
            mergedDeviceProfiles);
    }

    private async Task<QuestSessionKitCatalog> LoadSingleRootAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var fullRoot = Path.GetFullPath(rootPath);
        var libraryPath = Path.Combine(fullRoot, "APKs", "library.json");
        var apkMapPath = Path.Combine(fullRoot, "APKs", "apk_map.json");
        var hotloadPath = Path.Combine(fullRoot, "HotloadProfiles", "profiles.json");
        var deviceProfilesPath = Path.Combine(fullRoot, "DeviceProfiles", "profiles.json");

        if (!File.Exists(libraryPath) && !File.Exists(apkMapPath))
        {
            throw new FileNotFoundException("Quest library manifest not found.", libraryPath);
        }

        if (!File.Exists(hotloadPath))
        {
            throw new FileNotFoundException("Quest hotload profile manifest not found.", hotloadPath);
        }

        if (!File.Exists(deviceProfilesPath))
        {
            throw new FileNotFoundException("Quest device profile manifest not found.", deviceProfilesPath);
        }

        var hotloadJson = await File.ReadAllTextAsync(hotloadPath, cancellationToken).ConfigureAwait(false);
        var deviceProfilesJson = await File.ReadAllTextAsync(deviceProfilesPath, cancellationToken).ConfigureAwait(false);

        var compatibilityIndex = await LoadCompatibilityIndexAsync(fullRoot, cancellationToken).ConfigureAwait(false);
        var library = await LoadLibraryAsync(libraryPath, apkMapPath, cancellationToken).ConfigureAwait(false);
        var hotload = JsonSerializer.Deserialize<HotloadManifestDto>(hotloadJson, JsonOptions)
            ?? throw new InvalidDataException("Could not deserialize HotloadProfiles/profiles.json.");
        var devices = JsonSerializer.Deserialize<DeviceProfilesDto>(deviceProfilesJson, JsonOptions)
            ?? throw new InvalidDataException("Could not deserialize DeviceProfiles/profiles.json.");

        var defaultPackageIds = hotload.DefaultPackageIds ?? Array.Empty<string>();
        var sourceLabel = File.Exists(apkMapPath) && !File.Exists(libraryPath)
            ? $"{Path.GetFileName(Path.GetDirectoryName(fullRoot)) ?? "Quest"} Session Kit"
            : "Repo sample session kit";

        return new QuestSessionKitCatalog(
            new QuestSessionKitSource(sourceLabel, fullRoot),
            await BuildQuestAppsAsync(fullRoot, library.Apps, compatibilityIndex, cancellationToken).ConfigureAwait(false),
            library.Bundles.Select(bundle => new QuestBundle(
                bundle.Id ?? string.Empty,
                bundle.Label ?? bundle.Id ?? "Unnamed bundle",
                bundle.Description ?? string.Empty,
                (IReadOnlyList<string>)(bundle.AppIds ?? Array.Empty<string>()))).ToArray(),
            hotload.Profiles.Select(profile => new HotloadProfile(
                profile.Id ?? string.Empty,
                profile.Label ?? profile.Id ?? "Unnamed profile",
                profile.File ?? string.Empty,
                profile.Version ?? string.Empty,
                profile.Channel ?? string.Empty,
                profile.StudyLock,
                profile.Description ?? string.Empty,
                (IReadOnlyList<string>)(profile.PackageIds is { Length: > 0 } ? profile.PackageIds : defaultPackageIds))).ToArray(),
            devices.Profiles.Select(profile => new DeviceProfile(
                profile.Id ?? string.Empty,
                profile.Label ?? profile.Id ?? "Unnamed device profile",
                profile.Description ?? string.Empty,
                new Dictionary<string, string>(profile.Props ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase))).ToArray());
    }

    private static async Task<IReadOnlyList<QuestAppTarget>> BuildQuestAppsAsync(
        string fullRoot,
        IReadOnlyList<AppDto> apps,
        IReadOnlyDictionary<string, ApkCompatibilityDto> compatibilityIndex,
        CancellationToken cancellationToken)
    {
        var results = new List<QuestAppTarget>(apps.Count);

        foreach (var app in apps)
        {
            var apkFile = app.ApkFile ?? string.Empty;
            var apkPath = string.IsNullOrWhiteSpace(apkFile)
                ? string.Empty
                : Path.Combine(fullRoot, "APKs", apkFile);

            var apkSha256 = File.Exists(apkPath)
                ? await ComputeSha256Async(apkPath, cancellationToken).ConfigureAwait(false)
                : null;

            compatibilityIndex.TryGetValue(apkSha256 ?? string.Empty, out var compatibility);

            var tags = MergeTags(app.Tags, compatibility?.Tags);
            var compatibilityStatus = ResolveCompatibilityStatus(app, compatibility, apkSha256, apkPath);

            results.Add(new QuestAppTarget(
                app.Id ?? string.Empty,
                app.Label ?? app.Id ?? "Unnamed app",
                app.PackageId ?? string.Empty,
                apkFile,
                app.LaunchComponent ?? string.Empty,
                app.BrowserPackageId ?? string.Empty,
                app.Description ?? string.Empty,
                tags,
                apkSha256,
                compatibilityStatus,
                compatibility?.Label,
                compatibility?.Notes,
                compatibility?.Verification is null
                    ? null
                    : new StudyVerificationBaseline(
                        compatibility.Verification.ApkSha256 ?? string.Empty,
                        compatibility.Verification.SoftwareVersion ?? string.Empty,
                        compatibility.Verification.BuildId ?? string.Empty,
                        compatibility.Verification.DisplayId ?? string.Empty,
                        compatibility.Verification.DeviceProfileId ?? string.Empty,
                        compatibility.Verification.EnvironmentHash ?? string.Empty,
                        compatibility.Verification.VerifiedAtUtc,
                        compatibility.Verification.VerifiedBy ?? string.Empty)));
        }

        return results;
    }

    private static async Task<LibraryDto> LoadLibraryAsync(
        string libraryPath,
        string apkMapPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(libraryPath))
        {
            var libraryJson = await File.ReadAllTextAsync(libraryPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<LibraryDto>(libraryJson, JsonOptions)
                ?? throw new InvalidDataException("Could not deserialize library.json.");
        }

        var apkMapJson = await File.ReadAllTextAsync(apkMapPath, cancellationToken).ConfigureAwait(false);
        var apkMap = JsonSerializer.Deserialize<ApkMapDto>(apkMapJson, JsonOptions)
            ?? throw new InvalidDataException("Could not deserialize APKs/apk_map.json.");

        var apkDirectory = Path.GetDirectoryName(apkMapPath)
            ?? throw new InvalidOperationException("Could not resolve APK directory.");
        var mappedFiles = new HashSet<string>(
            apkMap.Apks
                .Select(apk => apk.File)
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Select(file => file!),
            StringComparer.OrdinalIgnoreCase);

        var apkEntries = apkMap.Apks
            .Concat(Directory.EnumerateFiles(apkDirectory, "*.apk", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(file => !string.IsNullOrWhiteSpace(file) && !mappedFiles.Contains(file!))
                .Select(file => new ApkMapEntryDto
                {
                    File = file,
                    PackageId = InferPackageIdFromStem(Path.GetFileNameWithoutExtension(file) ?? string.Empty)
                }))
            .ToArray();

        var apps = apkEntries.Select(apk =>
        {
            var stem = Path.GetFileNameWithoutExtension(apk.File ?? string.Empty);
            var id = BuildAppId(stem, apk.PackageId);
            return new AppDto
            {
                Id = id,
                Label = HumanizeStem(stem),
                PackageId = apk.PackageId,
                ApkFile = apk.File,
                BrowserPackageId = "com.oculus.browser",
                Description = $"Imported from Quest Session Kit APK catalog ({apk.File}).",
                Tags = BuildTags(stem, apk.PackageId)
            };
        }).ToList();

        if (!apps.Any(app => string.Equals(app.PackageId, "com.oculus.browser", StringComparison.OrdinalIgnoreCase)))
        {
            apps.Add(new AppDto
            {
                Id = "quest-browser",
                Label = "Quest Browser",
                PackageId = "com.oculus.browser",
                ApkFile = string.Empty,
                BrowserPackageId = "com.oculus.browser",
                Description = "Browser target used for open-URL flows.",
                Tags = ["browser", "utility"]
            });
        }

        return new LibraryDto
        {
            Apps = apps.ToArray(),
            Bundles =
            [
                new BundleDto
                {
                    Id = "quest-session-kit-stack",
                    Label = "Quest Session Kit Stack",
                    Description = "APK targets discovered from APKs/apk_map.json.",
                    AppIds = apps
                        .Where(app => !string.Equals(app.PackageId, "com.oculus.browser", StringComparison.OrdinalIgnoreCase))
                        .Select(app => app.Id ?? string.Empty)
                        .ToArray()
                }
            ]
        };
    }

    private static async Task<IReadOnlyDictionary<string, ApkCompatibilityDto>> LoadCompatibilityIndexAsync(
        string fullRoot,
        CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            Path.Combine(fullRoot, "APKs", "compatibility.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "samples", "quest-session-kit", "APKs", "compatibility.json")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "quest-session-kit", "APKs", "compatibility.json"))
        };

        var merged = new Dictionary<string, ApkCompatibilityDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var json = await File.ReadAllTextAsync(candidate, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<ApkCompatibilityManifestDto>(json, JsonOptions);
            if (manifest?.Apps is not { Length: > 0 })
            {
                continue;
            }

            foreach (var app in manifest.Apps)
            {
                if (string.IsNullOrWhiteSpace(app.Sha256))
                {
                    continue;
                }

                merged[app.Sha256] = app;
            }
        }

        return merged;
    }

    private static async Task<string> ComputeSha256Async(string apkPath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(apkPath);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hashBytes);
    }

    private static IReadOnlyList<string> MergeTags(string[]? appTags, string[]? compatibilityTags)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in appTags ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                tags.Add(tag);
            }
        }

        foreach (var tag in compatibilityTags ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                tags.Add(tag);
            }
        }

        return tags.ToArray();
    }

    private static ApkCompatibilityStatus ResolveCompatibilityStatus(AppDto app, ApkCompatibilityDto? compatibility, string? apkSha256, string apkPath)
    {
        if (compatibility is not null)
        {
            return compatibility.Status?.Equals("incompatible", StringComparison.OrdinalIgnoreCase) == true
                ? ApkCompatibilityStatus.Incompatible
                : ApkCompatibilityStatus.Compatible;
        }

        if (string.Equals(app.PackageId, "com.oculus.browser", StringComparison.OrdinalIgnoreCase))
        {
            return ApkCompatibilityStatus.Compatible;
        }

        if (!string.IsNullOrWhiteSpace(apkSha256) && File.Exists(apkPath))
        {
            return ApkCompatibilityStatus.Unclassified;
        }

        return ApkCompatibilityStatus.Compatible;
    }

    private static string BuildAppId(string stem, string? packageId)
    {
        if (!string.IsNullOrWhiteSpace(packageId))
        {
            return packageId.Replace('.', '-');
        }

        return string.IsNullOrWhiteSpace(stem)
            ? "quest-app"
            : stem.Replace(' ', '-').ToLowerInvariant();
    }

    private static string HumanizeStem(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            return "Quest App";
        }

        return string.Concat(stem.Select((character, index) =>
            index > 0 && char.IsUpper(character) && !char.IsUpper(stem[index - 1])
                ? $" {character}"
                : character.ToString()));
    }

    private static string[] BuildTags(string stem, string? packageId)
    {
        var tags = new List<string> { "viscereality", "runtime" };
        var package = packageId ?? string.Empty;

        if (stem.Contains("lsl", StringComparison.OrdinalIgnoreCase) || package.Contains("lsl", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("lsl");
        }

        if (stem.Contains("twin", StringComparison.OrdinalIgnoreCase) || package.Contains("twin", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("twin");
        }

        if ((stem.Contains("inandout", StringComparison.OrdinalIgnoreCase) || stem.Contains("lslout", StringComparison.OrdinalIgnoreCase)) && !tags.Contains("twin"))
        {
            tags.Add("twin");
        }

        return tags.ToArray();
    }

    private static string InferPackageIdFromStem(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            return string.Empty;
        }

        return $"com.Viscereality.{stem}";
    }

    private sealed class LibraryDto
    {
        [JsonPropertyName("apps")]
        public AppDto[] Apps { get; init; } = Array.Empty<AppDto>();

        [JsonPropertyName("bundles")]
        public BundleDto[] Bundles { get; init; } = Array.Empty<BundleDto>();
    }

    private sealed class AppDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("packageId")]
        public string? PackageId { get; init; }

        [JsonPropertyName("apkFile")]
        public string? ApkFile { get; init; }

        [JsonPropertyName("launchComponent")]
        public string? LaunchComponent { get; init; }

        [JsonPropertyName("browserPackageId")]
        public string? BrowserPackageId { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("tags")]
        public string[]? Tags { get; init; }
    }

    private sealed class BundleDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("appIds")]
        public string[]? AppIds { get; init; }
    }

    private sealed class HotloadManifestDto
    {
        [JsonPropertyName("defaultPackageIds")]
        public string[]? DefaultPackageIds { get; init; }

        [JsonPropertyName("profiles")]
        public HotloadProfileDto[] Profiles { get; init; } = Array.Empty<HotloadProfileDto>();
    }

    private sealed class ApkCompatibilityManifestDto
    {
        [JsonPropertyName("apps")]
        public ApkCompatibilityDto[] Apps { get; init; } = Array.Empty<ApkCompatibilityDto>();
    }

    private sealed class ApkCompatibilityDto
    {
        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("notes")]
        public string? Notes { get; init; }

        [JsonPropertyName("tags")]
        public string[]? Tags { get; init; }

        [JsonPropertyName("verification")]
        public StudyVerificationBaselineDto? Verification { get; init; }
    }

    private sealed class StudyVerificationBaselineDto
    {
        [JsonPropertyName("apkSha256")]
        public string? ApkSha256 { get; init; }

        [JsonPropertyName("softwareVersion")]
        public string? SoftwareVersion { get; init; }

        [JsonPropertyName("buildId")]
        public string? BuildId { get; init; }

        [JsonPropertyName("displayId")]
        public string? DisplayId { get; init; }

        [JsonPropertyName("deviceProfileId")]
        public string? DeviceProfileId { get; init; }

        [JsonPropertyName("environmentHash")]
        public string? EnvironmentHash { get; init; }

        [JsonPropertyName("verifiedAtUtc")]
        public DateTimeOffset? VerifiedAtUtc { get; init; }

        [JsonPropertyName("verifiedBy")]
        public string? VerifiedBy { get; init; }
    }

    private sealed class HotloadProfileDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("file")]
        public string? File { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("channel")]
        public string? Channel { get; init; }

        [JsonPropertyName("studyLock")]
        public bool StudyLock { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("packageIds")]
        public string[]? PackageIds { get; init; }
    }

    private sealed class DeviceProfilesDto
    {
        [JsonPropertyName("profiles")]
        public DeviceProfileDto[] Profiles { get; init; } = Array.Empty<DeviceProfileDto>();
    }

    private sealed class ApkMapDto
    {
        [JsonPropertyName("apks")]
        public ApkMapEntryDto[] Apks { get; init; } = Array.Empty<ApkMapEntryDto>();
    }

    private sealed class ApkMapEntryDto
    {
        [JsonPropertyName("file")]
        public string? File { get; init; }

        [JsonPropertyName("packageId")]
        public string? PackageId { get; init; }
    }

    private sealed class DeviceProfileDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("props")]
        public Dictionary<string, string>? Props { get; init; }
    }
}
