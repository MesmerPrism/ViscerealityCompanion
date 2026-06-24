using System.Globalization;

namespace ViscerealityCompanion.Core.Services;

public sealed record TwinCommandAcknowledgementExpectation(
    string ActionId,
    string DisplayName,
    int? PublishedSequence,
    int? PreviousAcknowledgedSequence,
    string? PreviousAcknowledgedTimestampRaw,
    DateTimeOffset? IssuedAtUtc);

public sealed record TwinCommandAcknowledgementResult(
    bool Acknowledged,
    string Summary,
    string Detail,
    string? ReportedActionId,
    string? ReportedActionSequence,
    string? ReportedActionLabel,
    string? ReportedActionSource,
    string? ReportedActionTimeUtc);

public static class TwinCommandAcknowledgementContract
{
    public const string LastActionIdKey = "study.command.last_action_id";
    public const string LastActionSequenceKey = "study.command.last_action_sequence";
    public const string LastActionLabelKey = "study.command.last_action_label";
    public const string LastActionSourceKey = "study.command.last_action_source";
    public const string LastActionTimeUtcKey = "study.command.last_action_at_utc";

    public static TwinCommandAcknowledgementResult Evaluate(
        TwinCommandAcknowledgementExpectation expectation,
        IReadOnlyDictionary<string, string>? reportedTwinState)
    {
        ArgumentNullException.ThrowIfNull(expectation);

        var settings = reportedTwinState ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.TryGetValue(LastActionIdKey, out var reportedActionId);
        settings.TryGetValue(LastActionSequenceKey, out var reportedSequence);
        settings.TryGetValue(LastActionLabelKey, out var reportedLabel);
        settings.TryGetValue(LastActionSourceKey, out var reportedSource);
        settings.TryGetValue(LastActionTimeUtcKey, out var reportedTimeUtc);

        var hasSignal = !string.IsNullOrWhiteSpace(reportedActionId) ||
                        !string.IsNullOrWhiteSpace(reportedSequence) ||
                        !string.IsNullOrWhiteSpace(reportedLabel) ||
                        !string.IsNullOrWhiteSpace(reportedSource) ||
                        !string.IsNullOrWhiteSpace(reportedTimeUtc);
        if (!hasSignal)
        {
            return BuildResult(
                acknowledged: false,
                "No headset command acknowledgement reported yet.",
                BuildDetail(expectation, reportedActionId, reportedSequence, reportedLabel, reportedSource, reportedTimeUtc),
                reportedActionId,
                reportedSequence,
                reportedLabel,
                reportedSource,
                reportedTimeUtc);
        }

        var expectedSequence = expectation.PublishedSequence.GetValueOrDefault();
        var hasExpectedSequence = expectedSequence > 0;
        var hasReportedSequence = int.TryParse(reportedSequence, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedReportedSequence);
        var sequenceMatches = hasExpectedSequence && hasReportedSequence && parsedReportedSequence == expectedSequence;
        var actionCompatible = IsActionCompatible(expectation.ActionId, reportedActionId);

        if (sequenceMatches && actionCompatible)
        {
            return BuildResult(
                acknowledged: true,
                "Headset acknowledged the expected command sequence.",
                BuildDetail(expectation, reportedActionId, reportedSequence, reportedLabel, reportedSource, reportedTimeUtc),
                reportedActionId,
                reportedSequence,
                reportedLabel,
                reportedSource,
                reportedTimeUtc);
        }

        if (sequenceMatches && !actionCompatible)
        {
            return BuildResult(
                acknowledged: false,
                "Headset reported the expected sequence with a different action id.",
                BuildDetail(expectation, reportedActionId, reportedSequence, reportedLabel, reportedSource, reportedTimeUtc),
                reportedActionId,
                reportedSequence,
                reportedLabel,
                reportedSource,
                reportedTimeUtc);
        }

        var actionMatches = ActionIdMatches(expectation.ActionId, reportedActionId);
        if (!hasExpectedSequence && actionMatches && HasFreshFallbackConfirmation(expectation, parsedReportedSequence, hasReportedSequence, reportedTimeUtc))
        {
            return BuildResult(
                acknowledged: true,
                "Headset acknowledged the expected command action.",
                BuildDetail(expectation, reportedActionId, reportedSequence, reportedLabel, reportedSource, reportedTimeUtc),
                reportedActionId,
                reportedSequence,
                reportedLabel,
                reportedSource,
                reportedTimeUtc);
        }

        var summary = actionMatches
            ? "Headset has not reported a fresh acknowledgement for the expected command yet."
            : "Headset last acknowledged a different command.";
        return BuildResult(
            acknowledged: false,
            summary,
            BuildDetail(expectation, reportedActionId, reportedSequence, reportedLabel, reportedSource, reportedTimeUtc),
            reportedActionId,
            reportedSequence,
            reportedLabel,
            reportedSource,
            reportedTimeUtc);
    }

    private static bool IsActionCompatible(string expectedActionId, string? reportedActionId)
        => string.IsNullOrWhiteSpace(expectedActionId) ||
           string.IsNullOrWhiteSpace(reportedActionId) ||
           string.Equals(reportedActionId.Trim(), expectedActionId.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ActionIdMatches(string expectedActionId, string? reportedActionId)
        => string.IsNullOrWhiteSpace(expectedActionId) ||
           !string.IsNullOrWhiteSpace(reportedActionId) &&
           string.Equals(reportedActionId.Trim(), expectedActionId.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool HasFreshFallbackConfirmation(
        TwinCommandAcknowledgementExpectation expectation,
        int parsedReportedSequence,
        bool hasReportedSequence,
        string? reportedTimeUtc)
    {
        if (hasReportedSequence &&
            expectation.PreviousAcknowledgedSequence.HasValue &&
            parsedReportedSequence != expectation.PreviousAcknowledgedSequence.Value)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(reportedTimeUtc) &&
            !string.Equals(reportedTimeUtc, expectation.PreviousAcknowledgedTimestampRaw, StringComparison.Ordinal))
        {
            if (expectation.IssuedAtUtc is null)
            {
                return !string.IsNullOrWhiteSpace(expectation.PreviousAcknowledgedTimestampRaw);
            }

            return DateTimeOffset.TryParse(
                       reportedTimeUtc,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.RoundtripKind,
                       out var parsedReportedTime) &&
                   parsedReportedTime >= expectation.IssuedAtUtc.Value;
        }

        return false;
    }

    private static TwinCommandAcknowledgementResult BuildResult(
        bool acknowledged,
        string summary,
        string detail,
        string? reportedActionId,
        string? reportedSequence,
        string? reportedLabel,
        string? reportedSource,
        string? reportedTimeUtc)
        => new(
            acknowledged,
            summary,
            detail,
            reportedActionId,
            reportedSequence,
            reportedLabel,
            reportedSource,
            reportedTimeUtc);

    private static string BuildDetail(
        TwinCommandAcknowledgementExpectation expectation,
        string? reportedActionId,
        string? reportedSequence,
        string? reportedLabel,
        string? reportedSource,
        string? reportedTimeUtc)
    {
        var expectedSequenceValue = expectation.PublishedSequence.GetValueOrDefault();
        var expectedSequence = expectedSequenceValue > 0
            ? expectedSequenceValue.ToString(CultureInfo.InvariantCulture)
            : "n/a";
        return
            $"Expected action {FormatValue(expectation.ActionId)} seq {expectedSequence}; " +
            $"headset last action {FormatValue(reportedActionId)} seq {FormatValue(reportedSequence)} " +
            $"label {FormatValue(reportedLabel)} via {FormatValue(reportedSource)} at {FormatValue(reportedTimeUtc)}.";
    }

    private static string FormatValue(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "n/a"
            : value.Trim();
}
