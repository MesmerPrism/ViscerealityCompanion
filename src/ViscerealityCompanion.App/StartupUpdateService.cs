using System.Net.Http;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App;

internal sealed record StartupUpdateSnapshot(
    PublishedAppUpdateStatus App,
    OfficialQuestToolingStatus Tooling)
{
    public bool HasToolingUpdates => Tooling.Hzdb.UpdateAvailable || Tooling.PlatformTools.UpdateAvailable;
    public bool HasUpdates => App.UpdateAvailable || App.RequiresGuidedInstaller || HasToolingUpdates;
}

internal static class StartupUpdateService
{
    public static async Task<StartupUpdateSnapshot?> CheckForUpdatesAsync(
        AppBuildIdentity.AppBuildStamp currentBuild,
        CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        using var publishedUpdates = new PublishedPreviewUpdateService(httpClient);
        using var tooling = new OfficialQuestToolingService(httpClient);

        var appTask = GetAppStatusSafeAsync(publishedUpdates, currentBuild, cancellationToken);
        var toolingTask = GetToolingStatusSafeAsync(tooling, cancellationToken);
        var localToolingStatus = tooling.GetLocalStatus();

        var appStatus = await appTask.ConfigureAwait(false);
        if (appStatus.UpdateAvailable)
        {
            var toolingStatus = await GetCompletedOrFallbackToolingStatusAsync(toolingTask, localToolingStatus).ConfigureAwait(false);
            return new StartupUpdateSnapshot(appStatus, toolingStatus);
        }

        var snapshot = new StartupUpdateSnapshot(
            appStatus,
            await toolingTask.ConfigureAwait(false));
        return snapshot.HasUpdates ? snapshot : null;
    }

    private static async Task<OfficialQuestToolingStatus> GetCompletedOrFallbackToolingStatusAsync(
        Task<OfficialQuestToolingStatus> toolingTask,
        OfficialQuestToolingStatus fallbackStatus)
    {
        if (toolingTask.IsCompletedSuccessfully)
        {
            return toolingTask.Result;
        }

        var completed = await Task.WhenAny(toolingTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        if (completed == toolingTask)
        {
            try
            {
                return await toolingTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        return fallbackStatus;
    }

    private static async Task<PublishedAppUpdateStatus> GetAppStatusSafeAsync(
        PublishedPreviewUpdateService publishedUpdates,
        AppBuildIdentity.AppBuildStamp currentBuild,
        CancellationToken cancellationToken)
    {
        try
        {
            return await publishedUpdates
                .GetStatusAsync(currentBuild.PackageName, currentBuild.ShortId, currentBuild.IsPackaged, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return PublishedPreviewUpdateService.BuildStatus(
                currentBuild.PackageName,
                currentBuild.ShortId,
                currentBuild.IsPackaged,
                availablePackageName: null,
                availableVersion: null);
        }
    }

    private static async Task<OfficialQuestToolingStatus> GetToolingStatusSafeAsync(
        OfficialQuestToolingService tooling,
        CancellationToken cancellationToken)
    {
        try
        {
            return await tooling.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return tooling.GetLocalStatus();
        }
    }
}
