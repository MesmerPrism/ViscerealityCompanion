using System.Xml.Linq;

namespace ViscerealityCompanion.Core.Services;

public sealed record PublishedAppUpdateStatus(
    bool IsApplicable,
    bool UpdateAvailable,
    bool RequiresGuidedInstaller,
    string? CurrentPackageName,
    string? AvailablePackageName,
    string? CurrentVersion,
    string? AvailableVersion,
    string Summary,
    string Detail,
    string AppInstallerUri,
    string ReleasePageUri);

public sealed class PublishedPreviewUpdateService : IDisposable
{
    public const string AppInstallerDownloadUri = "https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller";
    public const string ReleasePageUri = "https://github.com/MesmerPrism/ViscerealityCompanion/releases";

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    public PublishedPreviewUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient is null;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ViscerealityCompanion/1.0");
        }
    }

    public async Task<PublishedAppUpdateStatus> GetStatusAsync(
        string? currentPackageName,
        string? currentVersion,
        bool isPackaged,
        CancellationToken cancellationToken = default)
    {
        var appInstallerXml = await _httpClient.GetStringAsync(AppInstallerDownloadUri, cancellationToken).ConfigureAwait(false);
        var availableIdentity = TryParseAppInstallerIdentity(appInstallerXml);
        return BuildStatus(currentPackageName, currentVersion, isPackaged, availableIdentity.PackageName, availableIdentity.Version);
    }

    public async Task<string> DownloadLatestAppInstallerAsync(
        string? destinationDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var targetDirectory = string.IsNullOrWhiteSpace(destinationDirectory)
            ? Path.Combine(Path.GetTempPath(), "ViscerealityCompanionUpdates")
            : destinationDirectory;
        Directory.CreateDirectory(targetDirectory);

        var targetPath = Path.Combine(targetDirectory, "ViscerealityCompanion.appinstaller");
        using var response = await _httpClient.GetAsync(AppInstallerDownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(targetPath);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return targetPath;
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public static PublishedAppUpdateStatus BuildStatus(
        string? currentPackageName,
        string? currentVersion,
        bool isPackaged,
        string? availablePackageName,
        string? availableVersion)
    {
        if (!isPackaged)
        {
            return new PublishedAppUpdateStatus(
                IsApplicable: false,
                UpdateAvailable: false,
                RequiresGuidedInstaller: false,
                CurrentPackageName: currentPackageName,
                AvailablePackageName: availablePackageName,
                CurrentVersion: currentVersion,
                AvailableVersion: availableVersion,
                Summary: "Published Windows package updates apply only to installed MSIX builds.",
                Detail: "This copy is running unpackaged, so the launch-time package update handoff is not shown here.",
                AppInstallerUri: AppInstallerDownloadUri,
                ReleasePageUri: ReleasePageUri);
        }

        var updateAvailable = SamePackageIdentity(currentPackageName, availablePackageName) && IsUpdateAvailable(currentVersion, availableVersion);
        var requiresGuidedInstaller =
            !SamePackageIdentity(currentPackageName, availablePackageName) &&
            IsUpdateAvailable(currentVersion, availableVersion);
        var currentDisplayName = PackagedAppIdentity.GetDisplayName(currentPackageName);
        var availableDisplayName = PackagedAppIdentity.GetDisplayName(availablePackageName);
        return new PublishedAppUpdateStatus(
            IsApplicable: true,
            UpdateAvailable: updateAvailable,
            RequiresGuidedInstaller: requiresGuidedInstaller,
            CurrentPackageName: currentPackageName,
            AvailablePackageName: availablePackageName,
            CurrentVersion: currentVersion,
            AvailableVersion: availableVersion,
            Summary: requiresGuidedInstaller
                ? $"{availableDisplayName} {availableVersion ?? "n/a"} is available through the guided installer."
                : updateAvailable
                ? $"Windows package update {availableVersion} is available."
                : "Windows package is current.",
            Detail: requiresGuidedInstaller
                ? $"{currentDisplayName} {currentVersion ?? "n/a"} is installed under package name {currentPackageName ?? "n/a"}, but the published App Installer feed now targets {availablePackageName ?? "n/a"}. Use the guided installer or the manual certificate + App Installer path once instead of the in-app update button."
                : updateAvailable
                ? $"Installed package {currentVersion ?? "n/a"} can be updated directly from the published App Installer feed."
                : $"Installed package {currentVersion ?? "n/a"} matches the latest published .appinstaller metadata.",
            AppInstallerUri: AppInstallerDownloadUri,
            ReleasePageUri: ReleasePageUri);
    }

    internal static PublishedAppInstallerIdentity TryParseAppInstallerIdentity(string appInstallerXml)
    {
        if (string.IsNullOrWhiteSpace(appInstallerXml))
        {
            return new PublishedAppInstallerIdentity(null, null);
        }

        var document = XDocument.Parse(appInstallerXml, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root is null)
        {
            return new PublishedAppInstallerIdentity(null, null);
        }

        var ns = root.Name.Namespace;
        var mainPackage = root.Element(ns + "MainPackage");
        var rootVersion = root.Attribute("Version")?.Value?.Trim();
        var version = !string.IsNullOrWhiteSpace(rootVersion)
            ? rootVersion
            : mainPackage?.Attribute("Version")?.Value?.Trim();
        var packageName = mainPackage?.Attribute("Name")?.Value?.Trim();
        return new PublishedAppInstallerIdentity(packageName, version);
    }

    internal static bool IsUpdateAvailable(string? currentVersion, string? availableVersion)
    {
        if (!Version.TryParse(currentVersion, out var current))
        {
            return false;
        }

        if (!Version.TryParse(availableVersion, out var available))
        {
            return false;
        }

        return available > current;
    }

    private static bool SamePackageIdentity(string? currentPackageName, string? availablePackageName)
        => !string.IsNullOrWhiteSpace(currentPackageName) &&
           !string.IsNullOrWhiteSpace(availablePackageName) &&
           string.Equals(currentPackageName, availablePackageName, StringComparison.OrdinalIgnoreCase);
}

internal sealed record PublishedAppInstallerIdentity(string? PackageName, string? Version);
