namespace ViscerealityCompanion.Core.Models;

public static class SussexClockAlignmentStreamContract
{
    public const string ProbeStreamName = "SussexClockProbe";
    public const string ProbeStreamType = "sussex.clock.probe";
    public const string EchoStreamName = "SussexClockEcho";
    public const string EchoStreamType = "sussex.clock.echo";
    public const string EchoChannelLabel = "clock_alignment_echo";
    public const int DefaultDurationSeconds = 10;
    public const int DefaultProbeIntervalMilliseconds = 250;
    public const int DefaultEchoGraceMilliseconds = 1500;
}

public sealed record StudyClockAlignmentRunRequest(
    string SessionId,
    string DatasetHash,
    TimeSpan Duration,
    TimeSpan ProbeInterval,
    TimeSpan EchoGracePeriod);

public sealed record StudyClockAlignmentSample(
    int ProbeSequence,
    DateTimeOffset ProbeSentAtUtc,
    double ProbeSentLocalClockSeconds,
    DateTimeOffset EchoReceivedAtUtc,
    double EchoReceivedLocalClockSeconds,
    double? EchoSampleTimestampSeconds,
    string QuestReceivedAtUtc,
    double QuestReceivedLocalClockSeconds,
    double QuestEchoLocalClockSeconds,
    double QuestMinusWindowsClockSeconds,
    double RoundTripSeconds);

public sealed record StudyClockAlignmentProgress(
    double PercentComplete,
    int ProbesSent,
    int EchoesReceived,
    string Summary,
    string Detail);

public sealed record StudyClockAlignmentSummary(
    int ProbesSent,
    int EchoesReceived,
    double? RecommendedQuestMinusWindowsClockSeconds,
    double? MedianQuestMinusWindowsClockSeconds,
    double? MeanQuestMinusWindowsClockSeconds,
    double? MeanRoundTripSeconds,
    double? MinRoundTripSeconds,
    double? MaxRoundTripSeconds);

public sealed record StudyClockAlignmentRunResult(
    OperationOutcome Outcome,
    StudyClockAlignmentSummary Summary,
    IReadOnlyList<StudyClockAlignmentSample> Samples);
