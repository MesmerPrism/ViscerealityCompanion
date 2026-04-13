using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace ViscerealityCompanion.Core.Services;

public sealed record OfficialQuestToolingProgress(string Status, string Detail, int PercentComplete);

public sealed record OfficialQuestToolStatus(
    string Id,
    string DisplayName,
    bool IsInstalled,
    string? InstalledVersion,
    string? AvailableVersion,
    bool UpdateAvailable,
    string InstallPath,
    string SourceUri,
    string LicenseSummary,
    string LicenseUri);

public sealed record OfficialQuestToolingStatus(
    OfficialQuestToolStatus Hzdb,
    OfficialQuestToolStatus PlatformTools)
{
    public bool IsReady => Hzdb.IsInstalled && PlatformTools.IsInstalled;
}

public sealed record OfficialQuestToolingInstallResult(
    OfficialQuestToolingStatus Status,
    bool Changed,
    string Summary,
    string Detail);

internal sealed record OfficialQuestToolMetadata(string Version, string SourceUri, string LicenseSummary, string LicenseUri);

public static class OfficialQuestToolingLayout
{
    public static string RootPath => CompanionOperatorDataLayout.ToolingRootPath;

    public static string HzdbRootPath => Path.Combine(RootPath, "hzdb");
    public static string HzdbCurrentPath => Path.Combine(HzdbRootPath, "current");
    public static string HzdbExecutablePath => Path.Combine(HzdbCurrentPath, "hzdb.exe");
    public static string HzdbMetadataPath => Path.Combine(HzdbCurrentPath, "metadata.json");

    public static string PlatformToolsRootPath => Path.Combine(RootPath, "platform-tools");
    public static string PlatformToolsCurrentPath => Path.Combine(PlatformToolsRootPath, "current");
    public static string PlatformToolsDirectoryPath => Path.Combine(PlatformToolsCurrentPath, "platform-tools");
    public static string AdbExecutablePath => Path.Combine(PlatformToolsDirectoryPath, "adb.exe");
    public static string PlatformToolsMetadataPath => Path.Combine(PlatformToolsCurrentPath, "metadata.json");
    public static string PlatformToolsSourcePropertiesPath => Path.Combine(PlatformToolsDirectoryPath, "source.properties");

    public static string? TryReadInstalledHzdbVersion()
        => TryReadInstalledVersionFromMetadata(HzdbMetadataPath);

    public static string? TryReadInstalledPlatformToolsVersion()
        => TryReadInstalledVersionFromSourceProperties(PlatformToolsSourcePropertiesPath)
           ?? TryReadInstalledVersionFromMetadata(PlatformToolsMetadataPath);

    private static string? TryReadInstalledVersionFromMetadata(string metadataPath)
    {
        if (!File.Exists(metadataPath))
            return null;

        try
        {
            var metadata = JsonSerializer.Deserialize<OfficialQuestToolMetadata>(File.ReadAllText(metadataPath));
            return string.IsNullOrWhiteSpace(metadata?.Version) ? null : metadata.Version.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadInstalledVersionFromSourceProperties(string sourcePropertiesPath)
    {
        if (!File.Exists(sourcePropertiesPath))
            return null;

        foreach (var line in File.ReadLines(sourcePropertiesPath))
        {
            if (!line.StartsWith("Pkg.Revision", StringComparison.OrdinalIgnoreCase))
                continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0 || separatorIndex >= line.Length - 1)
                continue;

            var value = line[(separatorIndex + 1)..].Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}

public sealed class OfficialQuestToolingService : IDisposable
{
    public const string MetaHzdbPackageName = "@meta-quest/hzdb-win32-x64";
    public const string MetaHzdbMetadataUri = "https://registry.npmjs.org/@meta-quest/hzdb-win32-x64/latest";
    public const string MetaHzdbLicenseSummary = "Meta Platform Technologies SDK License Agreement";
    public const string MetaHzdbLicenseUri = "https://developers.meta.com/horizon/licenses/";
    public const string AndroidPlatformToolsRepositoryUri = "https://dl.google.com/android/repository/repository2-1.xml";
    public const string AndroidPlatformToolsDownloadBaseUri = "https://dl.google.com/android/repository/";
    public const string AndroidPlatformToolsLicenseSummary = "Android Software Development Kit License Agreement";
    public const string AndroidPlatformToolsLicenseUri = "https://developer.android.com/studio/releases/platform-tools";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions WebJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    public OfficialQuestToolingService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient is null;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ViscerealityCompanion/1.0");
        }
    }

    public OfficialQuestToolingStatus GetLocalStatus()
        => new(
            BuildHzdbStatus(OfficialQuestToolingLayout.TryReadInstalledHzdbVersion(), availableVersion: null),
            BuildPlatformToolsStatus(OfficialQuestToolingLayout.TryReadInstalledPlatformToolsVersion(), availableVersion: null));

    public async Task<OfficialQuestToolingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var hzdbRelease = await FetchHzdbReleaseAsync(cancellationToken).ConfigureAwait(false);
        var platformToolsRelease = await FetchPlatformToolsReleaseAsync(cancellationToken).ConfigureAwait(false);

        return new OfficialQuestToolingStatus(
            BuildHzdbStatus(OfficialQuestToolingLayout.TryReadInstalledHzdbVersion(), hzdbRelease.Version),
            BuildPlatformToolsStatus(OfficialQuestToolingLayout.TryReadInstalledPlatformToolsVersion(), platformToolsRelease.Version));
    }

    public async Task<OfficialQuestToolingInstallResult> InstallOrUpdateAsync(
        IProgress<OfficialQuestToolingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new OfficialQuestToolingProgress(
            "Checking Meta hzdb release",
            "Reading the latest published Windows hzdb metadata from Meta's npm package.",
            5));
        var hzdbRelease = await FetchHzdbReleaseAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(new OfficialQuestToolingProgress(
            "Checking Android platform-tools release",
            "Reading the latest published Android SDK Platform-Tools revision from Google's official repository metadata.",
            20));
        var platformToolsRelease = await FetchPlatformToolsReleaseAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(new OfficialQuestToolingProgress(
            "Installing Meta hzdb",
            $"Downloading Meta's published Windows hzdb package {hzdbRelease.Version}.",
            35));
        var hzdbChanged = await EnsureHzdbAsync(hzdbRelease, cancellationToken).ConfigureAwait(false);

        progress?.Report(new OfficialQuestToolingProgress(
            "Installing Android platform-tools",
            $"Downloading Google's published Android SDK Platform-Tools {platformToolsRelease.Version}.",
            70));
        var platformToolsChanged = await EnsurePlatformToolsAsync(platformToolsRelease, cancellationToken).ConfigureAwait(false);

        var status = new OfficialQuestToolingStatus(
            BuildHzdbStatus(OfficialQuestToolingLayout.TryReadInstalledHzdbVersion(), hzdbRelease.Version),
            BuildPlatformToolsStatus(OfficialQuestToolingLayout.TryReadInstalledPlatformToolsVersion(), platformToolsRelease.Version));

        progress?.Report(new OfficialQuestToolingProgress(
            "Official Quest tooling ready",
            "The managed LocalAppData tool cache now points at the latest fetched official Quest developer tools.",
            100));

        var changed = hzdbChanged || platformToolsChanged;
        return new OfficialQuestToolingInstallResult(
            status,
            changed,
            changed ? "Official Quest tooling installed or updated." : "Official Quest tooling was already current.",
            $"hzdb {status.Hzdb.InstalledVersion ?? "n/a"} | Android platform-tools {status.PlatformTools.InstalledVersion ?? "n/a"}");
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    internal static bool NeedsInstall(
        string? installedVersion,
        string targetPath,
        string availableVersion,
        Func<string, bool>? fileExists = null)
        => string.IsNullOrWhiteSpace(installedVersion)
           || !(fileExists ?? File.Exists).Invoke(targetPath)
           || !string.Equals(installedVersion.Trim(), availableVersion.Trim(), StringComparison.OrdinalIgnoreCase);

    internal static bool IntegrityMatchesSha512(byte[] payloadBytes, string integrity)
    {
        if (string.IsNullOrWhiteSpace(integrity))
            return false;

        const string prefix = "sha512-";
        if (!integrity.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var expected = integrity[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(expected))
            return false;

        var actual = Convert.ToBase64String(SHA512.HashData(payloadBytes));
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }

    internal static bool ChecksumMatchesSha1(byte[] payloadBytes, string checksum)
    {
        if (string.IsNullOrWhiteSpace(checksum))
            return false;

        var actual = Convert.ToHexString(SHA1.HashData(payloadBytes)).ToLowerInvariant();
        return string.Equals(actual, checksum.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    internal static string? ParsePlatformToolsRevision(string sourcePropertiesText)
    {
        if (string.IsNullOrWhiteSpace(sourcePropertiesText))
            return null;

        foreach (var line in sourcePropertiesText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("Pkg.Revision", StringComparison.OrdinalIgnoreCase))
                continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0 || separatorIndex >= line.Length - 1)
                continue;

            var value = line[(separatorIndex + 1)..].Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    internal static (string Version, string TarballUri, string Integrity, string License) ParseHzdbReleaseMetadataJson(string json)
    {
        var metadata = JsonSerializer.Deserialize<HzdbPackageResponse>(json, WebJsonOptions)
                       ?? throw new InvalidOperationException("Meta hzdb metadata response was empty.");

        if (string.IsNullOrWhiteSpace(metadata.Version) ||
            string.IsNullOrWhiteSpace(metadata.License) ||
            string.IsNullOrWhiteSpace(metadata.Dist?.Tarball) ||
            string.IsNullOrWhiteSpace(metadata.Dist?.Integrity))
        {
            throw new InvalidOperationException("Meta hzdb metadata response did not include the expected version, license, or tarball fields.");
        }

        return (
            metadata.Version.Trim(),
            metadata.Dist.Tarball.Trim(),
            metadata.Dist.Integrity.Trim(),
            metadata.License.Trim());
    }

    private OfficialQuestToolStatus BuildHzdbStatus(string? installedVersion, string? availableVersion)
        => new(
            Id: "meta-hzdb",
            DisplayName: "Meta Horizon Debug Bridge (hzdb)",
            IsInstalled: File.Exists(OfficialQuestToolingLayout.HzdbExecutablePath),
            InstalledVersion: installedVersion,
            AvailableVersion: availableVersion,
            UpdateAvailable: !string.IsNullOrWhiteSpace(availableVersion)
                             && !string.Equals(installedVersion?.Trim(), availableVersion.Trim(), StringComparison.OrdinalIgnoreCase),
            InstallPath: OfficialQuestToolingLayout.HzdbExecutablePath,
            SourceUri: MetaHzdbMetadataUri,
            LicenseSummary: MetaHzdbLicenseSummary,
            LicenseUri: MetaHzdbLicenseUri);

    private OfficialQuestToolStatus BuildPlatformToolsStatus(string? installedVersion, string? availableVersion)
        => new(
            Id: "android-platform-tools",
            DisplayName: "Android SDK Platform-Tools",
            IsInstalled: File.Exists(OfficialQuestToolingLayout.AdbExecutablePath),
            InstalledVersion: installedVersion,
            AvailableVersion: availableVersion,
            UpdateAvailable: !string.IsNullOrWhiteSpace(availableVersion)
                             && !string.Equals(installedVersion?.Trim(), availableVersion.Trim(), StringComparison.OrdinalIgnoreCase),
            InstallPath: OfficialQuestToolingLayout.AdbExecutablePath,
            SourceUri: AndroidPlatformToolsRepositoryUri,
            LicenseSummary: AndroidPlatformToolsLicenseSummary,
            LicenseUri: AndroidPlatformToolsLicenseUri);

    private async Task<HzdbReleaseMetadata> FetchHzdbReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(MetaHzdbMetadataUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var metadata = ParseHzdbReleaseMetadataJson(json);

        return new HzdbReleaseMetadata(
            metadata.Version,
            metadata.TarballUri,
            metadata.Integrity,
            metadata.License);
    }

    private async Task<PlatformToolsReleaseMetadata> FetchPlatformToolsReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(AndroidPlatformToolsRepositoryUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var document = XDocument.Load(stream);

        var package = document
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "remotePackage"
                && string.Equals(element.Attribute("path")?.Value, "platform-tools", StringComparison.Ordinal));

        if (package is null)
            throw new InvalidOperationException("Could not locate the platform-tools package in Google's Android repository metadata.");

        var revision = package.Elements().FirstOrDefault(element => element.Name.LocalName == "revision")
                       ?? throw new InvalidOperationException("Google's platform-tools metadata did not include a revision node.");

        var version = string.Join(
            ".",
            revision.Elements()
                .Where(element =>
                    element.Name.LocalName is "major" or "minor" or "micro")
                .Select(element => element.Value.Trim()));

        var archive = package
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "archive"
                && string.Equals(
                    element.Elements().FirstOrDefault(candidate => candidate.Name.LocalName == "host-os")?.Value.Trim(),
                    "windows",
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Google's platform-tools metadata did not include a Windows archive.");

        var complete = archive.Elements().FirstOrDefault(element => element.Name.LocalName == "complete")
                       ?? throw new InvalidOperationException("Google's platform-tools archive metadata did not include a complete payload entry.");

        var relativeUrl = complete.Elements().FirstOrDefault(element => element.Name.LocalName == "url")?.Value.Trim();
        var checksum = complete.Elements().FirstOrDefault(element => element.Name.LocalName == "checksum")?.Value.Trim();

        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(relativeUrl) || string.IsNullOrWhiteSpace(checksum))
        {
            throw new InvalidOperationException("Google's platform-tools metadata did not include the expected version, download URL, or checksum.");
        }

        return new PlatformToolsReleaseMetadata(
            version,
            new Uri(new Uri(AndroidPlatformToolsDownloadBaseUri), relativeUrl).AbsoluteUri,
            checksum);
    }

    private async Task<bool> EnsureHzdbAsync(HzdbReleaseMetadata release, CancellationToken cancellationToken)
    {
        var installedVersion = OfficialQuestToolingLayout.TryReadInstalledHzdbVersion();
        if (!NeedsInstall(installedVersion, OfficialQuestToolingLayout.HzdbExecutablePath, release.Version))
            return false;

        var payloadBytes = await DownloadBytesAsync(release.TarballUri, cancellationToken).ConfigureAwait(false);
        if (!IntegrityMatchesSha512(payloadBytes, release.Integrity))
            throw new InvalidOperationException($"Meta hzdb package integrity verification failed for {release.TarballUri}.");

        var componentRoot = OfficialQuestToolingLayout.HzdbRootPath;
        var stagingPath = CreateComponentStagingPath(componentRoot);
        Directory.CreateDirectory(stagingPath);

        try
        {
            var hzdbBinaryPath = Path.Combine(stagingPath, "hzdb.exe");
            await ExtractHzdbExecutableAsync(payloadBytes, hzdbBinaryPath, cancellationToken).ConfigureAwait(false);
            WriteMetadata(
                Path.Combine(stagingPath, "metadata.json"),
                new OfficialQuestToolMetadata(release.Version, release.TarballUri, MetaHzdbLicenseSummary, MetaHzdbLicenseUri));
            ReplaceCurrentDirectory(componentRoot, stagingPath);
        }
        catch
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }

            throw;
        }

        return true;
    }

    private async Task<bool> EnsurePlatformToolsAsync(PlatformToolsReleaseMetadata release, CancellationToken cancellationToken)
    {
        var installedVersion = OfficialQuestToolingLayout.TryReadInstalledPlatformToolsVersion();
        if (!NeedsInstall(installedVersion, OfficialQuestToolingLayout.AdbExecutablePath, release.Version))
            return false;

        var payloadBytes = await DownloadBytesAsync(release.DownloadUri, cancellationToken).ConfigureAwait(false);
        if (!ChecksumMatchesSha1(payloadBytes, release.ChecksumSha1))
            throw new InvalidOperationException($"Android platform-tools checksum verification failed for {release.DownloadUri}.");

        var componentRoot = OfficialQuestToolingLayout.PlatformToolsRootPath;
        var stagingPath = CreateComponentStagingPath(componentRoot);
        Directory.CreateDirectory(stagingPath);

        try
        {
            ExtractPlatformToolsArchive(payloadBytes, stagingPath);
            var sourcePropertiesPath = Path.Combine(stagingPath, "platform-tools", "source.properties");
            var extractedVersion = File.Exists(sourcePropertiesPath)
                ? ParsePlatformToolsRevision(File.ReadAllText(sourcePropertiesPath))
                : null;

            WriteMetadata(
                Path.Combine(stagingPath, "metadata.json"),
                new OfficialQuestToolMetadata(extractedVersion ?? release.Version, release.DownloadUri, AndroidPlatformToolsLicenseSummary, AndroidPlatformToolsLicenseUri));
            ReplaceCurrentDirectory(componentRoot, stagingPath);
        }
        catch
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }

            throw;
        }

        return true;
    }

    private async Task<byte[]> DownloadBytesAsync(string uri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExtractHzdbExecutableAsync(byte[] payloadBytes, string destinationPath, CancellationToken cancellationToken)
    {
        await using var archiveStream = new MemoryStream(payloadBytes, writable: false);
        await using var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress);
        using var reader = new TarReader(gzipStream);

        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            if (!string.Equals(NormalizeArchivePath(entry.Name), "package/bin/hzdb.exe", StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var outputStream = File.Create(destinationPath);
            if (entry.DataStream is null)
                throw new InvalidOperationException("Meta hzdb archive entry did not include a binary payload stream.");

            await entry.DataStream.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("Meta hzdb archive did not include package/bin/hzdb.exe.");
    }

    private static void ExtractPlatformToolsArchive(byte[] payloadBytes, string destinationPath)
    {
        using var archiveStream = new MemoryStream(payloadBytes, writable: false);
        using var zipArchive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        foreach (var entry in zipArchive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
                continue;

            var normalized = NormalizeArchivePath(entry.FullName);
            if (!normalized.StartsWith("platform-tools/", StringComparison.OrdinalIgnoreCase))
                continue;

            var targetPath = Path.Combine(destinationPath, normalized.Replace('/', Path.DirectorySeparatorChar));
            if (normalized.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }

        if (!File.Exists(Path.Combine(destinationPath, "platform-tools", "adb.exe")))
            throw new InvalidOperationException("Android platform-tools archive did not include platform-tools/adb.exe.");
    }

    private static string CreateComponentStagingPath(string componentRoot)
        => Path.Combine(componentRoot, "_staging_" + Guid.NewGuid().ToString("N"));

    private static void ReplaceCurrentDirectory(string componentRoot, string stagingPath)
    {
        Directory.CreateDirectory(componentRoot);
        var currentPath = Path.Combine(componentRoot, "current");
        if (Directory.Exists(currentPath))
        {
            Directory.Delete(currentPath, recursive: true);
        }

        Directory.Move(stagingPath, currentPath);
    }

    private static string NormalizeArchivePath(string path)
        => path.Replace('\\', '/').TrimStart('/');

    private static void WriteMetadata(string destinationPath, OfficialQuestToolMetadata metadata)
        => File.WriteAllText(destinationPath, JsonSerializer.Serialize(metadata, JsonOptions));

    private sealed record HzdbPackageResponse(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("license")] string License,
        [property: JsonPropertyName("dist")] HzdbDistResponse? Dist);

    private sealed record HzdbDistResponse(
        [property: JsonPropertyName("tarball")] string Tarball,
        [property: JsonPropertyName("integrity")] string Integrity);

    private sealed record HzdbReleaseMetadata(string Version, string TarballUri, string Integrity, string License);

    private sealed record PlatformToolsReleaseMetadata(string Version, string DownloadUri, string ChecksumSha1);
}
