using System.Xml.Linq;

namespace ViscerealityCompanion.Core.Services;

public sealed record PublishedAppUpdateStatus(
    bool IsApplicable,
    bool UpdateAvailable,
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
        string? currentVersion,
        bool isPackaged,
        CancellationToken cancellationToken = default)
    {
        var appInstallerXml = await _httpClient.GetStringAsync(AppInstallerDownloadUri, cancellationToken).ConfigureAwait(false);
        var availableVersion = TryParseAppInstallerVersion(appInstallerXml);
        return BuildStatus(currentVersion, isPackaged, availableVersion);
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

    public static PublishedAppUpdateStatus BuildStatus(string? currentVersion, bool isPackaged, string? availableVersion)
    {
        if (!isPackaged)
        {
            return new PublishedAppUpdateStatus(
                IsApplicable: false,
                UpdateAvailable: false,
                CurrentVersion: currentVersion,
                AvailableVersion: availableVersion,
                Summary: "Published Windows package updates apply only to installed MSIX builds.",
                Detail: "This copy is running unpackaged, so the launch-time package update handoff is not shown here.",
                AppInstallerUri: AppInstallerDownloadUri,
                ReleasePageUri: ReleasePageUri);
        }

        var updateAvailable = IsUpdateAvailable(currentVersion, availableVersion);
        return new PublishedAppUpdateStatus(
            IsApplicable: true,
            UpdateAvailable: updateAvailable,
            CurrentVersion: currentVersion,
            AvailableVersion: availableVersion,
            Summary: updateAvailable
                ? $"Windows package update {availableVersion} is available."
                : "Windows package is current.",
            Detail: updateAvailable
                ? $"Installed package {currentVersion ?? "n/a"} can be updated directly from the published App Installer feed."
                : $"Installed package {currentVersion ?? "n/a"} matches the latest published .appinstaller metadata.",
            AppInstallerUri: AppInstallerDownloadUri,
            ReleasePageUri: ReleasePageUri);
    }

    internal static string? TryParseAppInstallerVersion(string appInstallerXml)
    {
        if (string.IsNullOrWhiteSpace(appInstallerXml))
        {
            return null;
        }

        var document = XDocument.Parse(appInstallerXml, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root is null)
        {
            return null;
        }

        var rootVersion = root.Attribute("Version")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(rootVersion))
        {
            return rootVersion;
        }

        var ns = root.Name.Namespace;
        return root.Element(ns + "MainPackage")?.Attribute("Version")?.Value?.Trim();
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
}
