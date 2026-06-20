using System.Text.Json.Nodes;

namespace ViscerealityCompanion.Core.Models;

public static class PeripersonalCommandProtocol
{
    public const string CommandProtocolVersion = "viscereality.peripersonal.command.v1";
    public const string ReceiptProtocolVersion = "viscereality.peripersonal.command_receipt.v1";
}

public sealed record PeripersonalCommandEnvelope(
    string ProtocolVersion,
    string CommandId,
    long Sequence,
    string SessionId,
    string ParticipantRef,
    string TargetApp,
    string TargetRuntimeKind,
    string TargetPackage,
    string Action,
    JsonObject? Payload,
    DateTimeOffset SentAtUtc,
    string Transport,
    bool RequiresObservedState)
{
    public static PeripersonalCommandEnvelope Create(
        long sequence,
        string sessionId,
        string participantRef,
        string targetApp,
        string targetRuntimeKind,
        string targetPackage,
        string action,
        string transport,
        bool requiresObservedState,
        JsonObject? payload = null,
        string? commandId = null,
        DateTimeOffset? sentAtUtc = null)
        => new(
            PeripersonalCommandProtocol.CommandProtocolVersion,
            string.IsNullOrWhiteSpace(commandId) ? Guid.NewGuid().ToString("D") : commandId.Trim(),
            sequence,
            sessionId?.Trim() ?? string.Empty,
            participantRef?.Trim() ?? string.Empty,
            targetApp?.Trim() ?? string.Empty,
            targetRuntimeKind?.Trim() ?? string.Empty,
            targetPackage?.Trim() ?? string.Empty,
            action?.Trim() ?? string.Empty,
            payload,
            sentAtUtc ?? DateTimeOffset.UtcNow,
            transport?.Trim() ?? string.Empty,
            requiresObservedState);
}

public sealed record PeripersonalCommandReceiptEnvelope(
    string ProtocolVersion,
    string CommandId,
    long Sequence,
    string SessionId,
    string TargetApp,
    string Action,
    string TransportReceived,
    double? ReceivedLslTimestamp,
    DateTimeOffset? ReceivedAtUtc,
    bool Accepted,
    bool Executed,
    bool Completed,
    string IssueCode,
    string Message,
    long? StateRevision,
    JsonObject? ObservedState)
{
    public static PeripersonalCommandReceiptEnvelope Create(
        PeripersonalCommandEnvelope command,
        bool accepted,
        bool executed,
        bool completed,
        string transportReceived,
        string issueCode = "",
        string message = "",
        JsonObject? observedState = null,
        long? stateRevision = null,
        double? receivedLslTimestamp = null,
        DateTimeOffset? receivedAtUtc = null)
        => new(
            PeripersonalCommandProtocol.ReceiptProtocolVersion,
            command.CommandId,
            command.Sequence,
            command.SessionId,
            command.TargetApp,
            command.Action,
            transportReceived?.Trim() ?? string.Empty,
            receivedLslTimestamp,
            receivedAtUtc ?? DateTimeOffset.UtcNow,
            accepted,
            executed,
            completed,
            issueCode?.Trim() ?? string.Empty,
            message?.Trim() ?? string.Empty,
            stateRevision,
            observedState);
}

public enum PeripersonalCommandLifecycleState
{
    Sent,
    Accepted,
    Executed,
    Observed,
    Rejected,
    TimedOut
}

public sealed record PeripersonalCommandLedgerSnapshot(
    string CommandId,
    long Sequence,
    string SessionId,
    string Action,
    PeripersonalCommandLifecycleState Status,
    bool Accepted,
    bool Executed,
    bool Completed,
    bool Observed,
    bool RequiresObservedState,
    string IssueCode,
    string Message,
    DateTimeOffset SentAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public bool WorkflowAdvanceAllowed =>
        Status == PeripersonalCommandLifecycleState.Observed ||
        (Status == PeripersonalCommandLifecycleState.Executed && !RequiresObservedState);
}

