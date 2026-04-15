using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class QuestTwinStatePublisherInventoryServiceTests
{
    [Fact]
    public void Inspect_reports_success_when_expected_sussex_source_is_visible()
    {
        var discovery = new FakeLslStreamDiscoveryService(
        [
            new LslVisibleStreamInfo(
                "quest_twin_state",
                "quest.twin.state",
                "viscereality.quest.com-viscereality-sussexexperiment.quest-twin-state.quest-twin-state",
                4,
                0f,
                123d)
        ]);

        var inventory = QuestTwinStatePublisherInventoryService.Inspect(discovery, "com.Viscereality.SussexExperiment");

        Assert.Equal(OperationOutcomeKind.Success, inventory.Level);
        Assert.True(inventory.AnyPublisherVisible);
        Assert.True(inventory.ExpectedPublisherVisible);
        Assert.Contains("expected Sussex source_id", inventory.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_distinguishes_missing_quest_return_publisher_from_forward_hrv_path()
    {
        var discovery = new FakeLslStreamDiscoveryService([]);

        var inventory = QuestTwinStatePublisherInventoryService.Inspect(discovery, "com.Viscereality.SussexExperiment");

        Assert.Equal(OperationOutcomeKind.Warning, inventory.Level);
        Assert.False(inventory.AnyPublisherVisible);
        Assert.False(inventory.ExpectedPublisherVisible);
        Assert.Contains("No Quest twin-state outlet", inventory.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Forward HRV can still reach the headset", inventory.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_reports_source_id_contract_mismatch_for_legacy_outlet()
    {
        var discovery = new FakeLslStreamDiscoveryService(
        [
            new LslVisibleStreamInfo(
                "quest_twin_state",
                "quest.twin.state",
                "quest.twin.state",
                4,
                0f,
                123d)
        ]);

        var inventory = QuestTwinStatePublisherInventoryService.Inspect(discovery, "com.Viscereality.SussexExperiment");

        Assert.Equal(OperationOutcomeKind.Warning, inventory.Level);
        Assert.True(inventory.AnyPublisherVisible);
        Assert.False(inventory.ExpectedPublisherVisible);
        Assert.Contains("does not match the Quest source-id contract", inventory.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("companion will ignore", inventory.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeLslStreamDiscoveryService(IReadOnlyList<LslVisibleStreamInfo> streams) : ILslStreamDiscoveryService
    {
        public LslRuntimeState RuntimeState { get; } = new(true, "Fake LSL discovery.");

        public IReadOnlyList<LslVisibleStreamInfo> Discover(LslStreamDiscoveryRequest request)
            => streams
                .Where(stream =>
                    (string.IsNullOrWhiteSpace(request.StreamName) || string.Equals(stream.Name, request.StreamName, StringComparison.Ordinal)) &&
                    (string.IsNullOrWhiteSpace(request.StreamType) || string.Equals(stream.Type, request.StreamType, StringComparison.Ordinal)))
                .ToArray();
    }
}
