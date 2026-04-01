using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Windows.Forms;

namespace ViscerealityCompanion.PreviewInstaller;

internal readonly record struct InstallerProgressUpdate(string Status, string Detail, int PercentComplete);

internal readonly record struct InstallerCompletionResult(string AppInstallerPath);

internal static class Program
{
    private const string AppInstallerDownloadUri = "https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller";
    private const string CertificateDownloadUri = "https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer";
    private const string ReleasePageUri = "https://github.com/MesmerPrism/ViscerealityCompanion/releases";
    private const string DownloadDirectoryName = "ViscerealityCompanionPreviewSetup";

    [STAThread]
    private static int Main()
    {
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

        var certificatePath = Path.Combine(downloadDirectory, "ViscerealityCompanion.cer");
        var appInstallerPath = Path.Combine(downloadDirectory, "ViscerealityCompanion.appinstaller");

        using var httpClient = new HttpClient();

        progress.Report(new InstallerProgressUpdate(
            "Downloading trust certificate",
            "Pulling the preview signing certificate from the latest public GitHub release.",
            25));
        await DownloadFileAsync(httpClient, CertificateDownloadUri, certificatePath, cancellationToken);

        progress.Report(new InstallerProgressUpdate(
            "Downloading App Installer metadata",
            "Fetching the current .appinstaller file that points Windows App Installer at the latest Sussex-focused preview package.",
            50));
        await DownloadFileAsync(httpClient, AppInstallerDownloadUri, appInstallerPath, cancellationToken);

        progress.Report(new InstallerProgressUpdate(
            "Trusting the preview certificate",
            "Adding the public preview certificate to Trusted People so the MSIX package can be installed cleanly.",
            70));
        TrustCertificate(certificatePath);

        progress.Report(new InstallerProgressUpdate(
            "Opening Windows App Installer",
            "The package metadata is ready. Windows App Installer will open next so you can finish the install or update.",
            95));
        LaunchAppInstaller(appInstallerPath);

        progress.Report(new InstallerProgressUpdate(
            "Windows App Installer opened",
            "Continue the install in the Windows App Installer window. If it did not open, use the retry button below.",
            100));

        return new InstallerCompletionResult(appInstallerPath);
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

    private static void LaunchAppInstaller(string appInstallerPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = appInstallerPath,
            UseShellExecute = true
        };

        Process.Start(startInfo);
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
}
