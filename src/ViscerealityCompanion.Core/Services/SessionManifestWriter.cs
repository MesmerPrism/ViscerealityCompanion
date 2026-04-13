using System.Text.Json;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class SessionManifestWriter
{
    private readonly string _outputRoot;

    public SessionManifestWriter(string? outputRoot = null)
    {
        _outputRoot = outputRoot ?? CompanionOperatorDataLayout.SessionRootPath;
    }

    public async Task<string> WriteAsync(
        SessionManifestSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_outputRoot);

        var fileName = $"session_manifest_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.json";
        var outputPath = Path.Combine(_outputRoot, fileName);

        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            catalogSourceLabel = snapshot.CatalogSourceLabel,
            catalogRootPath = snapshot.CatalogRootPath,
            endpointDraft = snapshot.EndpointDraft,
            activeEndpoint = snapshot.ActiveEndpoint,
            selectedAppId = snapshot.SelectedAppId,
            selectedBundleId = snapshot.SelectedBundleId,
            selectedHotloadProfileId = snapshot.SelectedHotloadProfileId,
            selectedRuntimeConfigId = snapshot.SelectedRuntimeConfigId,
            selectedDeviceProfileId = snapshot.SelectedDeviceProfileId,
            connectionSummary = snapshot.ConnectionSummary,
            runtimeConfigSummary = snapshot.RuntimeConfigSummary,
            remoteOnlyControlEnabled = snapshot.RemoteOnlyControlEnabled,
            monitor = new
            {
                summary = snapshot.MonitorSummary,
                detail = snapshot.MonitorDetail,
                value = snapshot.MonitorValue,
                sampleRateHz = snapshot.MonitorSampleRateHz
            },
            twin = new
            {
                summary = snapshot.TwinSummary,
                detail = snapshot.TwinDetail
            },
            lastAction = new
            {
                label = snapshot.LastActionLabel,
                detail = snapshot.LastActionDetail
            },
            browserUrl = snapshot.BrowserUrl,
            logs = snapshot.RecentLogs.Select(log => new
            {
                timestamp = log.Timestamp,
                level = log.Level.ToString(),
                log.Message,
                log.Detail
            }).ToArray()
        };

        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(
            stream,
            payload,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken).ConfigureAwait(false);

        return outputPath;
    }
}
