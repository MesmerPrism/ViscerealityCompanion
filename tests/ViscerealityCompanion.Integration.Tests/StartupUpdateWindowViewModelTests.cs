using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class StartupUpdateWindowViewModelTests
{
    [Fact]
    public async Task Primary_action_refreshes_tooling_cards_to_post_install_versions()
    {
        var snapshot = new StartupUpdateSnapshot(
            PublishedPreviewUpdateService.BuildStatus(
                currentPackageName: "MesmerPrism.ViscerealityCompanion",
                currentVersion: "0.1.46.0",
                isPackaged: true,
                availablePackageName: "MesmerPrism.ViscerealityCompanion",
                availableVersion: "0.1.46.0"),
            new OfficialQuestToolingStatus(
                new OfficialQuestToolStatus(
                    Id: "meta-hzdb",
                    DisplayName: "Meta Horizon Debug Bridge (hzdb)",
                    IsInstalled: false,
                    InstalledVersion: null,
                    AvailableVersion: "1.0.1",
                    UpdateAvailable: true,
                    InstallPath: @"C:\tooling\hzdb\current\hzdb.exe",
                    SourceUri: OfficialQuestToolingService.MetaHzdbMetadataUri,
                    LicenseSummary: OfficialQuestToolingService.MetaHzdbLicenseSummary,
                    LicenseUri: OfficialQuestToolingService.MetaHzdbLicenseUri),
                new OfficialQuestToolStatus(
                    Id: "android-platform-tools",
                    DisplayName: "Android SDK Platform-Tools",
                    IsInstalled: false,
                    InstalledVersion: null,
                    AvailableVersion: "37.0.0",
                    UpdateAvailable: true,
                    InstallPath: @"C:\tooling\platform-tools\current\platform-tools\adb.exe",
                    SourceUri: OfficialQuestToolingService.AndroidPlatformToolsRepositoryUri,
                    LicenseSummary: OfficialQuestToolingService.AndroidPlatformToolsLicenseSummary,
                    LicenseUri: OfficialQuestToolingService.AndroidPlatformToolsLicenseUri)));

        var updatedToolingStatus = new OfficialQuestToolingStatus(
            snapshot.Tooling.Hzdb with
            {
                IsInstalled = true,
                InstalledVersion = "1.0.1",
                UpdateAvailable = false
            },
            snapshot.Tooling.PlatformTools with
            {
                IsInstalled = true,
                InstalledVersion = "37.0.0",
                UpdateAvailable = false
            });

        var viewModel = new StartupUpdateWindowViewModel(
            snapshot,
            (_, _) => Task.FromResult(new OfficialQuestToolingInstallResult(
                updatedToolingStatus,
                Changed: true,
                Summary: "Official Quest tooling installed or updated.",
                Detail: "hzdb 1.0.1 | Android platform-tools 37.0.0")),
            (_, _, _) => Task.CompletedTask);

        viewModel.PrimaryActionCommand.Execute(null);
        await WaitForAsync(() => viewModel.IsCompleted);

        Assert.Equal("Official Quest tooling updated", viewModel.Heading);
        Assert.Equal("1.0.1", viewModel.HzdbCurrentVersion);
        Assert.Equal("Current", viewModel.HzdbStatusLabel);
        Assert.Equal("37.0.0", viewModel.PlatformToolsCurrentVersion);
        Assert.Equal("Current", viewModel.PlatformToolsStatusLabel);
        Assert.Equal("hzdb 1.0.1 | Android platform-tools 37.0.0", viewModel.Detail);
    }

    [Fact]
    public void Migration_only_snapshot_uses_close_primary_action()
    {
        var snapshot = new StartupUpdateSnapshot(
            PublishedPreviewUpdateService.BuildStatus(
                currentPackageName: "MesmerPrism.ViscerealityCompanionPreview",
                currentVersion: "0.1.57.0",
                isPackaged: true,
                availablePackageName: "MesmerPrism.ViscerealityCompanion",
                availableVersion: "0.1.60.0"),
            new OfficialQuestToolingStatus(
                new OfficialQuestToolStatus(
                    Id: "meta-hzdb",
                    DisplayName: "Meta Horizon Debug Bridge (hzdb)",
                    IsInstalled: true,
                    InstalledVersion: "1.0.1",
                    AvailableVersion: "1.0.1",
                    UpdateAvailable: false,
                    InstallPath: @"C:\tooling\hzdb\current\hzdb.exe",
                    SourceUri: OfficialQuestToolingService.MetaHzdbMetadataUri,
                    LicenseSummary: OfficialQuestToolingService.MetaHzdbLicenseSummary,
                    LicenseUri: OfficialQuestToolingService.MetaHzdbLicenseUri),
                new OfficialQuestToolStatus(
                    Id: "android-platform-tools",
                    DisplayName: "Android SDK Platform-Tools",
                    IsInstalled: true,
                    InstalledVersion: "37.0.0",
                    AvailableVersion: "37.0.0",
                    UpdateAvailable: false,
                    InstallPath: @"C:\tooling\platform-tools\current\platform-tools\adb.exe",
                    SourceUri: OfficialQuestToolingService.AndroidPlatformToolsRepositoryUri,
                    LicenseSummary: OfficialQuestToolingService.AndroidPlatformToolsLicenseSummary,
                    LicenseUri: OfficialQuestToolingService.AndroidPlatformToolsLicenseUri)));

        var viewModel = new StartupUpdateWindowViewModel(
            snapshot,
            (_, _) => throw new InvalidOperationException("Tooling should not update in this scenario."),
            (_, _, _) => throw new InvalidOperationException("In-app MSIX update should not run for migration-only snapshots."));

        Assert.Equal("Close", viewModel.PrimaryActionLabel);
        Assert.Equal("Use installer", viewModel.AppStatusLabel);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeoutAt)
            {
                throw new TimeoutException("Condition was not met before the timeout elapsed.");
            }

            await Task.Delay(25);
        }
    }
}
