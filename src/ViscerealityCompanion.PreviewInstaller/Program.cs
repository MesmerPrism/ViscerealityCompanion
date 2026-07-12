using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Windows.Forms;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.PreviewInstaller;

internal readonly record struct InstallerProgressUpdate(string Status, string Detail, int PercentComplete);

internal readonly record struct InstallerCompletionResult(
    string AppInstallerPath,
    string Summary,
    string Detail,
    string? ToolingWarning);

internal sealed record SetupReleaseConfiguration(
    string ProductName,
    string AppInstallerDownloadUri,
    string CertificateDownloadUri,
    string ReleasePageUri,
    string ExpectedPackageId,
    string DownloadDirectoryName,
    string AppInstallerFileName,
    string CertificateFileName)
{
    private const string MetadataPrefix = "ViscerealityCompanion.Setup.";

    public static SetupReleaseConfiguration Load()
    {
        var metadata = typeof(SetupReleaseConfiguration).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);

        return new SetupReleaseConfiguration(
            Read(metadata, "ProductName", "Viscereality Companion"),
            Read(metadata, "AppInstallerDownloadUri", "https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller"),
            Read(metadata, "CertificateDownloadUri", "https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer"),
            Read(metadata, "ReleasePageUri", "https://github.com/MesmerPrism/ViscerealityCompanion/releases"),
            Read(metadata, "ExpectedPackageId", PackagedAppIdentity.ReleasePackageName),
            Read(metadata, "DownloadDirectoryName", "ViscerealityCompanionSetup"),
            Read(metadata, "AppInstallerFileName", "ViscerealityCompanion.appinstaller"),
            Read(metadata, "CertificateFileName", "ViscerealityCompanion.cer"));
    }

    private static string Read(IReadOnlyDictionary<string, string?> metadata, string name, string fallback)
        => metadata.TryGetValue(MetadataPrefix + name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
}

internal static class Program
{
    private static readonly SetupReleaseConfiguration ReleaseConfiguration = SetupReleaseConfiguration.Load();

    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            EnsureAdministrator();
            using var installerForm = new InstallerStatusForm(
                (progress, cancellationToken) => InstallPublishedPackageAsync(progress, cancellationToken),
                (progress, cancellationToken) => InstallPublishedPackageAsync(progress, cancellationToken, removeLegacyPackagesBeforeInstall: true),
                ReleaseConfiguration.ReleasePageUri,
                ReleaseConfiguration.ProductName);
            Application.Run(installerForm);
            return 0;
        }
        catch (Exception exception)
        {
            ShowError(exception);
            return 1;
        }
    }

    private static async Task<InstallerCompletionResult> InstallPublishedPackageAsync(
        IProgress<InstallerProgressUpdate> progress,
        CancellationToken cancellationToken,
        bool removeLegacyPackagesBeforeInstall = false)
    {
        progress.Report(new InstallerProgressUpdate(
            "Preparing guided setup",
            "Creating a temporary staging folder for the packaged Viscereality Companion installer.",
            5));

        var downloadDirectory = Path.Combine(Path.GetTempPath(), ReleaseConfiguration.DownloadDirectoryName);
        Directory.CreateDirectory(downloadDirectory);

        var certificatePath = Path.Combine(downloadDirectory, ReleaseConfiguration.CertificateFileName);
        var appInstallerPath = Path.Combine(downloadDirectory, ReleaseConfiguration.AppInstallerFileName);

        using var httpClient = new HttpClient();

        progress.Report(new InstallerProgressUpdate(
            "Downloading trust certificate",
            $"Pulling the package signing certificate for {ReleaseConfiguration.ProductName}.",
            25));
        await DownloadFileAsync(httpClient, ReleaseConfiguration.CertificateDownloadUri, certificatePath, cancellationToken);

        progress.Report(new InstallerProgressUpdate(
            "Downloading App Installer metadata",
            $"Fetching the .appinstaller feed pinned for {ReleaseConfiguration.ProductName}.",
            50));
        await DownloadFileAsync(httpClient, ReleaseConfiguration.AppInstallerDownloadUri, appInstallerPath, cancellationToken);

        string? toolingWarning = null;
        try
        {
            using var tooling = new OfficialQuestToolingService(httpClient);
            var toolingProgress = new Progress<OfficialQuestToolingProgress>(update =>
                progress.Report(new InstallerProgressUpdate(
                    update.Status,
                    update.Detail,
                    50 + (int)Math.Round(update.PercentComplete * 0.3))));

            await tooling.InstallOrUpdateAsync(toolingProgress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception toolingException)
        {
            toolingWarning =
                "The packaged app can still be installed, but the official Quest tooling cache could not be refreshed automatically. " +
                $"{toolingException.Message}";
        }

        progress.Report(new InstallerProgressUpdate(
            "Trusting the package certificate",
            "Adding the public package certificate to Trusted People so the MSIX package can be installed cleanly.",
            85));
        TrustCertificate(certificatePath);

        progress.Report(new InstallerProgressUpdate(
            "Inspecting existing install",
            "Checking whether a previous packaged Viscereality Companion install is already registered on this machine.",
            87));
        var packageInstaller = new PreviewPackageInstaller();
        var packageIdentity = PreviewPackageInstaller.ParseAppInstallerManifest(appInstallerPath);
        ValidatePublishedPackageIdentity(packageIdentity, ReleaseConfiguration.ExpectedPackageId);
        var existingPackage = PreviewPackageInstaller.FindExistingPackage(packageIdentity);
        var legacyPackages = PreviewPackageInstaller.FindLegacyPackagesToRetire(packageIdentity);
        var installResult = await packageInstaller
            .InstallOrUpdateAsync(
                packageIdentity,
                existingPackage,
                legacyPackages,
                progress,
                cancellationToken,
                removeLegacyPackagesBeforeInstall)
            .ConfigureAwait(false);

        var installedPackage = PreviewPackageInstaller.FindExistingPackage(packageIdentity);
        _ = PreviewPackageInstaller.TryLaunchInstalledPackage(installedPackage, out var launchDetail);
        var completionSummary = BuildCompletionSummary(installResult);
        var completionDetail = BuildCompletionDetail(installResult, launchDetail);
        progress.Report(new InstallerProgressUpdate(
            completionSummary,
            completionDetail,
            100));

        return new InstallerCompletionResult(appInstallerPath, completionSummary, completionDetail, toolingWarning);
    }

    internal static string GetDownloadedAppInstallerPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            ReleaseConfiguration.DownloadDirectoryName,
            ReleaseConfiguration.AppInstallerFileName);
    }

    internal static void ValidatePublishedPackageIdentity(PreviewPackageIdentity packageIdentity, string? expectedPackageId = null)
    {
        var expected = string.IsNullOrWhiteSpace(expectedPackageId)
            ? ReleaseConfiguration.ExpectedPackageId
            : expectedPackageId.Trim();
        if (string.Equals(packageIdentity.Name, expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The downloaded App Installer feed currently targets package {packageIdentity.Name}, not the expected package family {expected}. " +
            "The release assets are inconsistent. Open the release page and refresh the published .appinstaller, MSIX, and guided setup helper together.");
    }

    private static void EnsureAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new InvalidOperationException(
                "Administrator permission is required to trust the package signing certificate. " +
                "Run the setup again and accept the Windows UAC prompt.");
        }
    }

    private static async Task DownloadFileAsync(HttpClient httpClient, string sourceUri, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var output = File.Create(destinationPath);
        await response.Content.CopyToAsync(output, cancellationToken);
    }

    private static void TrustCertificate(string certificatePath)
    {
        using var certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);

        store.Open(OpenFlags.ReadWrite);

        var alreadyTrusted = store.Certificates
            .Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false)
            .Count > 0;

        if (!alreadyTrusted)
        {
            // The public package remains self-signed today, so the public cert must be trusted explicitly.
            store.Add(certificate);
        }
    }

    private static void ShowError(Exception exception)
    {
        var message =
            $"{ReleaseConfiguration.ProductName} Setup could not finish.\n\n" +
            $"{exception.Message}\n\n" +
            "If the Windows release is not available yet, open the release page or use the source-build path instead.\n" +
            ReleaseConfiguration.ReleasePageUri;

        MessageBox.Show(
            message,
            $"{ReleaseConfiguration.ProductName} Setup",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static string BuildCompletionSummary(PreviewPackageInstallResult installResult)
    {
        if (installResult.RemovedPreviousInstall)
        {
            return $"{ReleaseConfiguration.ProductName} {installResult.InstalledVersion} replaced the previous install.";
        }

        if (installResult.RemovedLegacyInstall)
        {
            return $"{ReleaseConfiguration.ProductName} {installResult.InstalledVersion} installed and retired the previous preview-family entry.";
        }

        if (installResult.UpdatedExistingInstall)
        {
            return string.Equals(installResult.PreviousVersion, installResult.InstalledVersion, StringComparison.OrdinalIgnoreCase)
                ? $"{ReleaseConfiguration.ProductName} {installResult.InstalledVersion} is installed."
                : $"{ReleaseConfiguration.ProductName} updated to {installResult.InstalledVersion}.";
        }

        return $"{ReleaseConfiguration.ProductName} {installResult.InstalledVersion} installed.";
    }

    private static string BuildCompletionDetail(PreviewPackageInstallResult installResult, string? launchDetail)
    {
        var installDetail = installResult switch
        {
            { RemovedPreviousInstall: true } => $"The existing packaged install {installResult.PreviousVersion ?? "n/a"} blocked the in-place update, so the helper removed it and installed {installResult.InstalledVersion} cleanly.",
            { RemovedLegacyInstall: true } => $"Windows installed {ReleaseConfiguration.ProductName} {installResult.InstalledVersion} and removed the older preview-family packaged install {installResult.PreviousVersion ?? "n/a"}.",
            { UpdatedExistingInstall: true } when string.Equals(installResult.PreviousVersion, installResult.InstalledVersion, StringComparison.OrdinalIgnoreCase)
                => "The packaged install already matched the published release. The helper refreshed that install cleanly.",
            { UpdatedExistingInstall: true } => $"Windows updated the packaged install from {installResult.PreviousVersion ?? "n/a"} to {installResult.InstalledVersion} and closed the running app first if needed.",
            _ => $"The packaged app was installed directly from the published App Installer feed and is ready to launch from the Start menu as {ReleaseConfiguration.ProductName} {installResult.InstalledVersion}."
        };

        return string.IsNullOrWhiteSpace(launchDetail)
            ? installDetail
            : $"{installDetail} {launchDetail}";
    }
}
