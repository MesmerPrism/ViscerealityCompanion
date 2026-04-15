using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public static class QuestTwinStatePublisherInventoryService
{
    public const string StreamName = "quest_twin_state";
    public const string StreamType = "quest.twin.state";

    public static QuestTwinStatePublisherInventory Inspect(
        ILslStreamDiscoveryService? discoveryService,
        string? packageId,
        int limit = 8)
    {
        var expectedSourceId = string.IsNullOrWhiteSpace(packageId)
            ? string.Empty
            : TwinLslSourceId.BuildQuestStateSourceId(packageId, StreamName, StreamType);
        var expectedSourceIdPrefix = TwinLslSourceId.QuestSourcePrefix;

        if (discoveryService is null)
        {
            return new QuestTwinStatePublisherInventory(
                OperationOutcomeKind.Preview,
                "Quest twin-state outlet inventory has not run yet.",
                "The probe has not inspected Windows-visible Quest twin-state publishers yet.",
                AnyPublisherVisible: false,
                ExpectedPublisherVisible: false,
                ExpectedSourceId: expectedSourceId,
                ExpectedSourceIdPrefix: expectedSourceIdPrefix,
                VisiblePublishers: []);
        }

        if (!discoveryService.RuntimeState.Available)
        {
            return new QuestTwinStatePublisherInventory(
                OperationOutcomeKind.Warning,
                "Quest twin-state outlet inventory could not run because Windows LSL discovery is unavailable.",
                discoveryService.RuntimeState.Detail,
                AnyPublisherVisible: false,
                ExpectedPublisherVisible: false,
                ExpectedSourceId: expectedSourceId,
                ExpectedSourceIdPrefix: expectedSourceIdPrefix,
                VisiblePublishers: []);
        }

        IReadOnlyList<LslVisibleStreamInfo> visiblePublishers;
        try
        {
            visiblePublishers = discoveryService.Discover(new LslStreamDiscoveryRequest(
                StreamName,
                StreamType,
                Limit: Math.Max(1, limit),
                PreferNewestFirst: true));
        }
        catch (Exception exception)
        {
            return new QuestTwinStatePublisherInventory(
                OperationOutcomeKind.Warning,
                "Quest twin-state outlet inventory failed.",
                $"Windows LSL discovery could not enumerate {StreamName} / {StreamType}: {exception.Message}",
                AnyPublisherVisible: false,
                ExpectedPublisherVisible: false,
                ExpectedSourceId: expectedSourceId,
                ExpectedSourceIdPrefix: expectedSourceIdPrefix,
                VisiblePublishers: []);
        }

        var exactMatch = !string.IsNullOrWhiteSpace(expectedSourceId)
            ? visiblePublishers.FirstOrDefault(stream => string.Equals(stream.SourceId, expectedSourceId, StringComparison.Ordinal))
            : null;
        var prefixMatches = visiblePublishers
            .Where(stream => !string.IsNullOrWhiteSpace(stream.SourceId) &&
                             stream.SourceId.StartsWith(expectedSourceIdPrefix, StringComparison.Ordinal))
            .ToArray();

        if (exactMatch is not null)
        {
            return new QuestTwinStatePublisherInventory(
                OperationOutcomeKind.Success,
                "Expected Quest twin-state outlet is visible on Windows.",
                $"Windows can see {StreamName} / {StreamType} from the expected Sussex source_id {exactMatch.SourceId}.",
                AnyPublisherVisible: true,
                ExpectedPublisherVisible: true,
                ExpectedSourceId: expectedSourceId,
                ExpectedSourceIdPrefix: expectedSourceIdPrefix,
                VisiblePublishers: visiblePublishers);
        }

        if (visiblePublishers.Count == 0)
        {
            var expectedDetail = string.IsNullOrWhiteSpace(expectedSourceId)
                ? $"Expected source_id prefix {expectedSourceIdPrefix}."
                : $"Expected source_id {expectedSourceId}.";
            return new QuestTwinStatePublisherInventory(
                OperationOutcomeKind.Warning,
                "No Quest twin-state outlet is visible on Windows.",
                $"{StreamName} / {StreamType} did not appear in Windows LSL discovery. {expectedDetail} Forward HRV can still reach the headset in this state; this specifically tests the Quest-to-Windows return path.",
                AnyPublisherVisible: false,
                ExpectedPublisherVisible: false,
                ExpectedSourceId: expectedSourceId,
                ExpectedSourceIdPrefix: expectedSourceIdPrefix,
                VisiblePublishers: visiblePublishers);
        }

        if (prefixMatches.Length > 0)
        {
            return new QuestTwinStatePublisherInventory(
                OperationOutcomeKind.Warning,
                "Quest twin-state outlet is visible, but not from the expected Sussex source_id.",
                $"Windows can see {StreamName} / {StreamType} with Quest source_id {RenderStreamList(prefixMatches)}, but the bridge expects {RenderExpectedSource(expectedSourceId, expectedSourceIdPrefix)}. This points at a wrong Quest build, package id, or twin-state source-id contract mismatch.",
                AnyPublisherVisible: true,
                ExpectedPublisherVisible: false,
                ExpectedSourceId: expectedSourceId,
                ExpectedSourceIdPrefix: expectedSourceIdPrefix,
                VisiblePublishers: visiblePublishers);
        }

        return new QuestTwinStatePublisherInventory(
            OperationOutcomeKind.Warning,
            "A twin-state stream is visible, but it does not match the Quest source-id contract.",
            $"Windows can see {StreamName} / {StreamType} with source_id {RenderStreamList(visiblePublishers)}, but the bridge expects {RenderExpectedSource(expectedSourceId, expectedSourceIdPrefix)}. The companion will ignore that stream to avoid binding to stale or wrong-runtime telemetry.",
            AnyPublisherVisible: true,
            ExpectedPublisherVisible: false,
            ExpectedSourceId: expectedSourceId,
            ExpectedSourceIdPrefix: expectedSourceIdPrefix,
            VisiblePublishers: visiblePublishers);
    }

    public static string RenderForOperator(QuestTwinStatePublisherInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var visible = inventory.VisiblePublishers.Count == 0
            ? "visible sources none"
            : $"visible sources {RenderStreamList(inventory.VisiblePublishers)}";
        return $"{inventory.Summary} {inventory.Detail} {visible}".Trim();
    }

    private static string RenderExpectedSource(string expectedSourceId, string expectedSourceIdPrefix)
        => string.IsNullOrWhiteSpace(expectedSourceId)
            ? $"prefix {expectedSourceIdPrefix}"
            : expectedSourceId;

    private static string RenderStreamList(IReadOnlyCollection<LslVisibleStreamInfo> streams)
        => string.Join(
            ", ",
            streams
                .Take(4)
                .Select(stream => string.IsNullOrWhiteSpace(stream.SourceId) ? "source_id n/a" : stream.SourceId)
                .Concat(streams.Count > 4 ? [$"+{streams.Count - 4} more"] : []));
}
