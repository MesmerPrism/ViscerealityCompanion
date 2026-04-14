using System.Diagnostics;
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

internal static class Program
{
    private const string AppInstallerDownloadUri = "https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller";
    private const string CertificateDownloadUri = "https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer";
    private const string ReleasePageUri = "https://github.com/MesmerPrism/ViscerealityCompanion/releases";
    private const string DownloadDirectoryName = "ViscerealityCompanionPreviewSetup";
    private const string AppInstallerFileName = "ViscerealityCompanion.appinstaller";
    private const string CertificateFileName = "ViscerealityCompanion.cer";

    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            EnsureAdministrator();
            using var installerForm = new InstallerStatusForm(InstallPreviewAsync, ReleasePageUri);
            Application.Run(installerForm);
            return 0;
        }
        catch (Exception exception)
        {
            ShowError(exception);
            return 1;
        }
    }

    private static async Task<InstallerCompletionResult> InstallPreviewAsync(
        IProgress<InstallerProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new InstallerProgressUpdate(
            "Preparing guided setup",
            "Creating a temporary staging folder for the Sussex-focused research preview installer.",
            5));

        var downloadDirectory = Path.Combine(Path.GetTempPath(), DownloadDirectoryName);
        Directory.CreateDirectory(downloadDirectory);

        var certificatePath = Path.Combine(downloadDirectory, CertificateFileName);
        var appInstallerPath = Path.Combine(downloadDirectory, AppInstallerFileName);

        using var httpClient = new HttpClient();

        progress.Report(new InstallerProgressUpdate(
            "Downloading trust certificate",
            "Pulling the preview signing certificate from the latest public GitHub release.",
            25));
        await DownloadFileAsync(httpClient, CertificateDownloadUri, certificatePath, cancellationToken);

        progress.Report(new InstallerProgressUpdate(
            "Downloading App Installer metadata",
            "Fetching the current .appinstaller feed that points at the latest Sussex-focused preview package.",
            50));
        await DownloadFileAsync(httpClient, AppInstallerDownloadUri, appInstallerPath, cancellationToken);

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
                "The preview package can still be installed, but the official Quest tooling cache could not be refreshed automatically. " +
                $"{toolingException.Message}";
        }

        progress.Report(new InstallerProgressUpdate(
            "Trusting the preview certificate",
            "Adding the public preview certificate to Trusted People so the MSIX package can be installed cleanly.",
            85));
        TrustCertificate(certificatePath);

        progress.Report(new InstallerProgressUpdate(
            "Inspecting existing install",
            "Checking whether a previous packaged Viscereality Companion install is already registered on this machine.",
            87));
        var packageInstaller = new PreviewPackageInstaller();
        var packageIdentity = PreviewPackageInstaller.ParseAppInstallerManifest(appInstallerPath);
        var existingPackage = PreviewPackageInstaller.FindExistingPackage(packageIdentity);
        var installResult = await packageInstaller
            .InstallOrUpdateAsync(packageIdentity, existingPackage, progress, cancellationToken)
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
        return Path.Combine(Path.GetTempPath(), DownloadDirectoryName, AppInstallerFileName);
    }

    private static void EnsureAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new InvalidOperationException(
                "Administrator permission is required to trust the preview signing certificate. " +
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
            // Research preview packages are self-signed, so the public cert must be trusted explicitly.
            store.Add(certificate);
        }
    }

    private static void ShowError(Exception exception)
    {
        var message =
            "Viscereality Companion Preview Setup could not finish.\n\n" +
            $"{exception.Message}\n\n" +
            "If the public preview release is not available yet, open the release page or use the source-build path instead.\n" +
            $"{ReleasePageUri}";

        MessageBox.Show(
            message,
            "Viscereality Companion Preview Setup",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static string BuildCompletionSummary(PreviewPackageInstallResult installResult)
    {
        if (installResult.RemovedPreviousInstall)
        {
            return $"Viscereality Companion {installResult.InstalledVersion} replaced the previous install.";
        }

        if (installResult.UpdatedExistingInstall)
        {
            return string.Equals(installResult.PreviousVersion, installResult.InstalledVersion, StringComparison.OrdinalIgnoreCase)
                ? $"Viscereality Companion {installResult.InstalledVersion} is installed."
                : $"Viscereality Companion updated to {installResult.InstalledVersion}.";
        }

        return $"Viscereality Companion {installResult.InstalledVersion} installed.";
    }

    private static string BuildCompletionDetail(PreviewPackageInstallResult installResult, string? launchDetail)
    {
        var installDetail = installResult switch
        {
            { RemovedPreviousInstall: true } => $"The existing packaged install {installResult.PreviousVersion ?? "n/a"} blocked the in-place update, so the helper removed it and installed {installResult.InstalledVersion} cleanly.",
            { UpdatedExistingInstall: true } when string.Equals(installResult.PreviousVersion, installResult.InstalledVersion, StringComparison.OrdinalIgnoreCase)
                => "The packaged install already matched the published release. The helper refreshed that install cleanly.",
            { UpdatedExistingInstall: true } => $"Windows updated the packaged install from {installResult.PreviousVersion ?? "n/a"} to {installResult.InstalledVersion} and closed the running app first if needed.",
            _ => $"The packaged preview was installed directly from the published App Installer feed and is ready to launch from the Start menu as Viscereality Companion {installResult.InstalledVersion}."
        };

        return string.IsNullOrWhiteSpace(launchDetail)
            ? installDetail
            : $"{installDetail} {launchDetail}";
    }
}
