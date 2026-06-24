using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class TwinCommandAcknowledgementContractTests
{
    [Fact]
    public void Evaluate_AcknowledgesExactPublishedSequenceAndAction()
    {
        var result = TwinCommandAcknowledgementContract.Evaluate(
            new TwinCommandAcknowledgementExpectation(
                ActionId: "14",
                DisplayName: "Start Breathing Calibration",
                PublishedSequence: 42,
                PreviousAcknowledgedSequence: 41,
                PreviousAcknowledgedTimestampRaw: "2026-06-24T10:00:00.0000000Z",
                IssuedAtUtc: DateTimeOffset.Parse("2026-06-24T10:00:01.0000000Z")),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TwinCommandAcknowledgementContract.LastActionIdKey] = "14",
                [TwinCommandAcknowledgementContract.LastActionSequenceKey] = "42",
                [TwinCommandAcknowledgementContract.LastActionLabelKey] = "Start Breathing Calibration",
                [TwinCommandAcknowledgementContract.LastActionSourceKey] = "lsl twin command",
                [TwinCommandAcknowledgementContract.LastActionTimeUtcKey] = "2026-06-24T10:00:01.5000000Z"
            });

        Assert.True(result.Acknowledged);
    }

    [Fact]
    public void Evaluate_RejectsExpectedSequenceWithDifferentAction()
    {
        var result = TwinCommandAcknowledgementContract.Evaluate(
            new TwinCommandAcknowledgementExpectation(
                ActionId: "14",
                DisplayName: "Start Breathing Calibration",
                PublishedSequence: 42,
                PreviousAcknowledgedSequence: 41,
                PreviousAcknowledgedTimestampRaw: null,
                IssuedAtUtc: DateTimeOffset.Parse("2026-06-24T10:00:01.0000000Z")),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TwinCommandAcknowledgementContract.LastActionIdKey] = "41",
                [TwinCommandAcknowledgementContract.LastActionSequenceKey] = "42",
                [TwinCommandAcknowledgementContract.LastActionLabelKey] = "Reset Breathing Calibration"
            });

        Assert.False(result.Acknowledged);
        Assert.Contains("different action", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_RejectsStalePreviousAcknowledgement()
    {
        var result = TwinCommandAcknowledgementContract.Evaluate(
            new TwinCommandAcknowledgementExpectation(
                ActionId: "14",
                DisplayName: "Start Breathing Calibration",
                PublishedSequence: 42,
                PreviousAcknowledgedSequence: 41,
                PreviousAcknowledgedTimestampRaw: "2026-06-24T10:00:00.0000000Z",
                IssuedAtUtc: DateTimeOffset.Parse("2026-06-24T10:00:01.0000000Z")),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TwinCommandAcknowledgementContract.LastActionIdKey] = "41",
                [TwinCommandAcknowledgementContract.LastActionSequenceKey] = "41",
                [TwinCommandAcknowledgementContract.LastActionTimeUtcKey] = "2026-06-24T10:00:00.0000000Z"
            });

        Assert.False(result.Acknowledged);
    }

    [Fact]
    public void Evaluate_FallbackRequiresFreshMatchingActionWhenSequenceIsUnavailable()
    {
        var issuedAtUtc = DateTimeOffset.Parse("2026-06-24T10:00:01.0000000Z");
        var staleResult = TwinCommandAcknowledgementContract.Evaluate(
            new TwinCommandAcknowledgementExpectation(
                ActionId: "14",
                DisplayName: "Start Breathing Calibration",
                PublishedSequence: null,
                PreviousAcknowledgedSequence: 41,
                PreviousAcknowledgedTimestampRaw: "2026-06-24T10:00:00.0000000Z",
                IssuedAtUtc: issuedAtUtc),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TwinCommandAcknowledgementContract.LastActionIdKey] = "14",
                [TwinCommandAcknowledgementContract.LastActionSequenceKey] = "41",
                [TwinCommandAcknowledgementContract.LastActionTimeUtcKey] = "2026-06-24T10:00:00.0000000Z"
            });
        var freshResult = TwinCommandAcknowledgementContract.Evaluate(
            new TwinCommandAcknowledgementExpectation(
                ActionId: "14",
                DisplayName: "Start Breathing Calibration",
                PublishedSequence: null,
                PreviousAcknowledgedSequence: 41,
                PreviousAcknowledgedTimestampRaw: "2026-06-24T10:00:00.0000000Z",
                IssuedAtUtc: issuedAtUtc),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TwinCommandAcknowledgementContract.LastActionIdKey] = "14",
                [TwinCommandAcknowledgementContract.LastActionSequenceKey] = "43",
                [TwinCommandAcknowledgementContract.LastActionTimeUtcKey] = "2026-06-24T10:00:02.0000000Z"
            });

        Assert.False(staleResult.Acknowledged);
        Assert.True(freshResult.Acknowledged);
    }

    [Fact]
    public void Evaluate_FallbackRejectsMatchingActionWithoutFreshnessEvidence()
    {
        var noFreshMarker = TwinCommandAcknowledgementContract.Evaluate(
            new TwinCommandAcknowledgementExpectation(
                ActionId: "14",
                DisplayName: "Start Breathing Calibration",
                PublishedSequence: null,
                PreviousAcknowledgedSequence: null,
                PreviousAcknowledgedTimestampRaw: null,
                IssuedAtUtc: null),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TwinCommandAcknowledgementContract.LastActionIdKey] = "14",
                [TwinCommandAcknowledgementContract.LastActionSequenceKey] = "41"
            });
        var olderThanIssueTime = TwinCommandAcknowledgementContract.Evaluate(
            new TwinCommandAcknowledgementExpectation(
                ActionId: "14",
                DisplayName: "Start Breathing Calibration",
                PublishedSequence: null,
                PreviousAcknowledgedSequence: null,
                PreviousAcknowledgedTimestampRaw: null,
                IssuedAtUtc: DateTimeOffset.Parse("2026-06-24T10:00:01.0000000Z")),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TwinCommandAcknowledgementContract.LastActionIdKey] = "14",
                [TwinCommandAcknowledgementContract.LastActionTimeUtcKey] = "2026-06-24T10:00:00.0000000Z"
            });

        Assert.False(noFreshMarker.Acknowledged);
        Assert.False(olderThanIssueTime.Acknowledged);
    }
}
