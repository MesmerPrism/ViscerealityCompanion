using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace ViscerealityCompanion.App;

internal sealed record PackagedAppUpdateProgress(string Status, string Detail, int PercentComplete);

internal sealed class PackagedAppUpdateInstaller
{
    public async Task ApplyPublishedUpdateAsync(
        string appInstallerUri,
        IProgress<PackagedAppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!AppBuildIdentity.Current.IsPackaged)
        {
            throw new InvalidOperationException("Packaged app updates can only be applied from an installed MSIX build.");
        }

        if (!Uri.TryCreate(appInstallerUri, UriKind.Absolute, out var appInstallerFile))
        {
            throw new InvalidOperationException("The published App Installer URL is missing or invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        RegisterForRestart();

        progress?.Report(new PackagedAppUpdateProgress(
            "Starting Windows package update",
            "Downloading and staging the published MSIX update directly from the App Installer feed.",
            1));

        var packageManager = new PackageManager();
        var operation = packageManager.AddPackageByAppInstallerFileAsync(
            appInstallerFile,
            AddPackageByAppInstallerOptions.ForceTargetAppShutdown,
            packageManager.GetDefaultPackageVolume());

        operation.Progress = new AsyncOperationProgressHandler<DeploymentResult, DeploymentProgress>((_, deploymentProgress) =>
        {
            progress?.Report(new PackagedAppUpdateProgress(
                $"Windows package update {deploymentProgress.state}",
                "Windows is downloading and applying the new package in the background. The companion will restart when the update is ready.",
                Math.Clamp((int)deploymentProgress.percentage, 1, 99)));
        });

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                operation.Cancel();
            }
            catch
            {
            }
        });

        var result = await operation;
        if (!string.IsNullOrWhiteSpace(result?.ErrorText))
        {
            throw new InvalidOperationException(result.ErrorText);
        }
    }

    private static void RegisterForRestart()
    {
        try
        {
            _ = RegisterApplicationRestart(null, RestartFlags.None);
        }
        catch
        {
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterApplicationRestart(string? commandLine, RestartFlags flags);

    [Flags]
    private enum RestartFlags
    {
        None = 0,
        RestartNoCrash = 1,
        RestartNoHang = 2,
        RestartNoPatch = 4,
        RestartNoReboot = 8
    }
}
