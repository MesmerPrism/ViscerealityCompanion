using System.Diagnostics;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace ViscerealityCompanion.PreviewInstaller;

internal sealed record PreviewPackageIdentity(
    string Name,
    string Publisher,
    string Version,
    Uri AppInstallerUri);

internal sealed record PreviewPackageCandidate(
    string FullName,
    string Name,
    string Publisher,
    string Version,
    string FamilyName);

internal sealed record ExistingPreviewPackage(
    string FullName,
    string Version,
    string FamilyName);

internal sealed record PreviewPackageInstallResult(
    bool UpdatedExistingInstall,
    bool RemovedPreviousInstall,
    string? PreviousVersion,
    string InstalledVersion);

internal sealed class PreviewPackageInstaller
{
    internal const string MainApplicationId = "App";

    internal static PreviewPackageIdentity ParseAppInstallerManifest(string appInstallerPath)
    {
        var xml = File.ReadAllText(appInstallerPath);
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("The downloaded .appinstaller file is empty.");
        var ns = root.Name.Namespace;
        var mainPackage = root.Element(ns + "MainPackage")
                          ?? throw new InvalidOperationException("The downloaded .appinstaller file does not define a MainPackage entry.");

        var name = mainPackage.Attribute("Name")?.Value?.Trim();
        var publisher = mainPackage.Attribute("Publisher")?.Value?.Trim();
        var version = mainPackage.Attribute("Version")?.Value?.Trim();

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(publisher) ||
            string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException("The downloaded .appinstaller file is missing package identity metadata.");
        }

        return new PreviewPackageIdentity(
            name,
            publisher,
            version,
            new Uri(appInstallerPath, UriKind.Absolute));
    }

    internal static ExistingPreviewPackage? FindExistingPackage(
        PreviewPackageIdentity packageIdentity,
        IEnumerable<Package>? packages = null)
    {
        packages ??= new PackageManager().FindPackages();
        return FindExistingPackage(
            packageIdentity,
            packages.Select(candidate => new PreviewPackageCandidate(
                candidate.Id.FullName ?? string.Empty,
                candidate.Id.Name ?? string.Empty,
                candidate.Id.Publisher ?? string.Empty,
                candidate.Id.Version.ToString() ?? string.Empty,
                candidate.Id.FamilyName ?? string.Empty)));
    }

    internal static ExistingPreviewPackage? FindExistingPackage(
        PreviewPackageIdentity packageIdentity,
        IEnumerable<PreviewPackageCandidate> packages)
    {
        var package = packages
            .Where(candidate =>
                string.Equals(candidate.Name, packageIdentity.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Publisher, packageIdentity.Publisher, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => ParseVersionOrDefault(candidate.Version))
            .FirstOrDefault();

        return package is null
            ? null
            : new ExistingPreviewPackage(
                package.FullName,
                package.Version,
                package.FamilyName);
    }

    internal static string BuildAppsFolderLaunchTarget(string packageFamilyName, string applicationId = MainApplicationId)
        => $@"shell:AppsFolder\{packageFamilyName}!{applicationId}";

    internal static ProcessStartInfo CreateLaunchStartInfo(string packageFamilyName, string applicationId = MainApplicationId)
        => new()
        {
            FileName = "explorer.exe",
            Arguments = BuildAppsFolderLaunchTarget(packageFamilyName, applicationId),
            UseShellExecute = true
        };

    internal static bool TryLaunchInstalledPackage(
        ExistingPreviewPackage? package,
        out string detail,
        Action<ProcessStartInfo>? startProcess = null)
    {
        if (package is null)
        {
            detail = "The package is installed, but the helper could not resolve its packaged app registration afterward. Launch Viscereality Companion from the Start menu.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(package.FamilyName))
        {
            detail = "The package is installed, but Windows did not report a usable package family name for automatic launch. Launch Viscereality Companion from the Start menu.";
            return false;
        }

        try
        {
            var startInfo = CreateLaunchStartInfo(package.FamilyName);
            if (startProcess is null)
            {
                Process.Start(startInfo);
            }
            else
            {
                startProcess(startInfo);
            }

            detail = "The helper then asked Windows to open the installed app automatically.";
            return true;
        }
        catch (Exception exception)
        {
            detail = $"The package is installed, but Windows did not accept the automatic launch request ({exception.Message}). Launch Viscereality Companion from the Start menu.";
            return false;
        }
    }

    public async Task<PreviewPackageInstallResult> InstallOrUpdateAsync(
        PreviewPackageIdentity packageIdentity,
        ExistingPreviewPackage? existingPackage,
        IProgress<InstallerProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (existingPackage is null)
        {
            progress?.Report(new InstallerProgressUpdate(
                "Installing packaged app",
                $"No previous installed package was found. Installing Viscereality Companion {packageIdentity.Version} directly from the published App Installer feed.",
                88));
        }
        else
        {
            progress?.Report(new InstallerProgressUpdate(
                "Updating packaged app",
                $"Found installed package {existingPackage.Version}. Windows will update it in place and close the running app first if needed.",
                88));
        }

        try
        {
            await AddPackageByAppInstallerAsync(packageIdentity.AppInstallerUri, progress, 88, 99, cancellationToken).ConfigureAwait(false);
            return new PreviewPackageInstallResult(
                UpdatedExistingInstall: existingPackage is not null,
                RemovedPreviousInstall: false,
                PreviousVersion: existingPackage?.Version,
                InstalledVersion: packageIdentity.Version);
        }
        catch (Exception firstFailure) when (existingPackage is not null && ShouldRetryAfterRemoval(firstFailure))
        {
            progress?.Report(new InstallerProgressUpdate(
                "Replacing previous install",
                $"The existing package {existingPackage.Version} blocked the in-place update. Removing it and retrying the install once.",
                90));

            await RemoveExistingPackageAsync(existingPackage.FullName, cancellationToken).ConfigureAwait(false);
            await AddPackageByAppInstallerAsync(packageIdentity.AppInstallerUri, progress, 92, 99, cancellationToken).ConfigureAwait(false);

            return new PreviewPackageInstallResult(
                UpdatedExistingInstall: true,
                RemovedPreviousInstall: true,
                PreviousVersion: existingPackage.Version,
                InstalledVersion: packageIdentity.Version);
        }
    }

    private static async Task AddPackageByAppInstallerAsync(
        Uri appInstallerUri,
        IProgress<InstallerProgressUpdate>? progress,
        int minPercent,
        int maxPercent,
        CancellationToken cancellationToken)
    {
        var packageManager = new PackageManager();
        var operation = packageManager.AddPackageByAppInstallerFileAsync(
            appInstallerUri,
            AddPackageByAppInstallerOptions.ForceTargetAppShutdown,
            packageManager.GetDefaultPackageVolume());

        operation.Progress = new AsyncOperationProgressHandler<DeploymentResult, DeploymentProgress>((_, deploymentProgress) =>
        {
            var percent = minPercent + (int)Math.Round((maxPercent - minPercent) * (deploymentProgress.percentage / 100d));
            progress?.Report(new InstallerProgressUpdate(
                $"Installing packaged app ({deploymentProgress.state})",
                "Windows is downloading and applying the packaged preview directly from the App Installer feed.",
                Math.Clamp(percent, minPercent, maxPercent)));
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
            ThrowDeploymentFailure(result.ErrorText, result.ExtendedErrorCode);
        }
    }

    private static async Task RemoveExistingPackageAsync(string packageFullName, CancellationToken cancellationToken)
    {
        var packageManager = new PackageManager();
        var operation = packageManager.RemovePackageAsync(packageFullName);

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
            ThrowDeploymentFailure(result.ErrorText, result.ExtendedErrorCode);
        }
    }

    private static void ThrowDeploymentFailure(string message, Exception? extendedError)
    {
        var exception = new InvalidOperationException(message, extendedError);
        if (extendedError is not null)
        {
            exception.HResult = extendedError.HResult;
        }

        throw exception;
    }

    private static bool ShouldRetryAfterRemoval(Exception exception)
    {
        return exception.HResult is
            unchecked((int)0x80073CF3) or
            unchecked((int)0x80073CFB) or
            unchecked((int)0x80073D02);
    }

    private static Version ParseVersionOrDefault(string? version)
    {
        return Version.TryParse(version, out var parsed)
            ? parsed
            : new Version(0, 0);
    }
}
