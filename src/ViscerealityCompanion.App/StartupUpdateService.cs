using System.Net.Http;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App;

internal sealed record StartupUpdateSnapshot(
    PublishedAppUpdateStatus App,
    OfficialQuestToolingStatus Tooling)
{
    public bool HasToolingUpdates => Tooling.Hzdb.UpdateAvailable || Tooling.PlatformTools.UpdateAvailable;
    public bool HasUpdates => App.UpdateAvailable || HasToolingUpdates;
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

        await Task.WhenAll(appTask, toolingTask).ConfigureAwait(false);

        var snapshot = new StartupUpdateSnapshot(
            await appTask.ConfigureAwait(false),
            await toolingTask.ConfigureAwait(false));
        return snapshot.HasUpdates ? snapshot : null;
    }

    private static async Task<PublishedAppUpdateStatus> GetAppStatusSafeAsync(
        PublishedPreviewUpdateService publishedUpdates,
        AppBuildIdentity.AppBuildStamp currentBuild,
        CancellationToken cancellationToken)
    {
        try
        {
            return await publishedUpdates
                .GetStatusAsync(currentBuild.ShortId, currentBuild.IsPackaged, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return PublishedPreviewUpdateService.BuildStatus(currentBuild.ShortId, currentBuild.IsPackaged, availableVersion: null);
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
