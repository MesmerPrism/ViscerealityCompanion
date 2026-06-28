namespace ViscerealityCompanion.Core.Models;

public static class StudyClockAlignmentStreamContract
{
    public const string ProbeStreamName = "PeripersonalClockProbe";
    public const string ProbeStreamType = "peripersonal.clock.probe";
    public const string EchoStreamName = "PeripersonalClockEcho";
    public const string EchoStreamType = "peripersonal.clock.echo";
    public const string ProbeSourceId = "viscereality.companion.peripersonal.clockprobe";
    public const string SussexProbeStreamName = "SussexClockProbe";
    public const string SussexProbeStreamType = "sussex.clock.probe";
    public const string SussexEchoStreamName = "SussexClockEcho";
    public const string SussexEchoStreamType = "sussex.clock.echo";
    public const string SussexProbeSourceId = "viscereality.companion.sussex.clockprobe";
    public const string EchoChannelLabel = "clock_alignment_echo";
    public const int DefaultDurationSeconds = 10;
    public const int DefaultProbeIntervalMilliseconds = 250;
    public const int DefaultEchoGraceMilliseconds = 1500;
    public const int DefaultBackgroundProbeIntervalSeconds = 5;
}

public enum StudyClockAlignmentWindowKind
{
    StartBurst = 0,
    BackgroundSparse = 1,
    EndBurst = 2
}

public sealed record StudyClockAlignmentRunRequest(
    string SessionId,
    string DatasetHash,
    StudyClockAlignmentWindowKind WindowKind,
    TimeSpan Duration,
    TimeSpan ProbeInterval,
    TimeSpan EchoGracePeriod,
    int FirstProbeSequence = 1,
    string ProbeStreamName = StudyClockAlignmentStreamContract.ProbeStreamName,
    string ProbeStreamType = StudyClockAlignmentStreamContract.ProbeStreamType,
    string EchoStreamName = StudyClockAlignmentStreamContract.EchoStreamName,
    string EchoStreamType = StudyClockAlignmentStreamContract.EchoStreamType,
    string ProbeSourceId = StudyClockAlignmentStreamContract.ProbeSourceId,
    bool RequireSessionMatch = true);

public sealed record StudyClockAlignmentSample(
    StudyClockAlignmentWindowKind WindowKind,
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
