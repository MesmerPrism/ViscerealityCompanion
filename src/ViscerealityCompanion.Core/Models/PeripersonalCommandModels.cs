using System.Globalization;
using System.Text.Json;
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

    public static PeripersonalCommandReceiptEnvelope ParseJson(string receiptJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptJson);

        var node = JsonNode.Parse(receiptJson) as JsonObject
            ?? throw new InvalidDataException("Peripersonal receipt JSON must be an object.");

        var protocolVersion = ReadString(node, "protocol_version", "protocolVersion");
        var commandId = ReadString(node, "command_id", "commandId");
        var sequence = ReadLong(node, "sequence");
        var sessionId = ReadString(node, "session_id", "sessionId");
        var targetApp = ReadString(node, "target_app", "targetApp");
        var action = ReadString(node, "action");
        var transportReceived = ReadString(node, "transport_received", "transportReceived");
        var receivedLslTimestamp = ReadNullableDouble(node, "received_lsl_timestamp", "receivedLslTimestamp");
        var receivedAtUtc = ReadNullableDateTimeOffset(node, "received_at_utc", "receivedAtUtc");
        var accepted = ReadBool(node, "accepted");
        var executed = ReadBool(node, "executed");
        var completed = ReadBool(node, "completed");
        var issueCode = ReadString(node, "issue_code", "issueCode");
        var message = ReadString(node, "message");
        var stateRevision = ReadNullableLong(node, "state_revision", "stateRevision");
        var observedState = ReadObject(node, "observed_state", "observedState");

        if (protocolVersion != PeripersonalCommandProtocol.ReceiptProtocolVersion)
        {
            throw new InvalidDataException($"Unsupported peripersonal receipt protocol `{protocolVersion}`.");
        }

        return new PeripersonalCommandReceiptEnvelope(
            protocolVersion,
            commandId,
            sequence,
            sessionId,
            targetApp,
            action,
            transportReceived,
            receivedLslTimestamp,
            receivedAtUtc,
            accepted,
            executed,
            completed,
            issueCode,
            message,
            stateRevision,
            observedState);
    }

    private static string ReadString(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetPropertyValue(name, out var node) && node is not null)
            {
                return node.GetValueKind() == JsonValueKind.String
                    ? node.GetValue<string>()
                    : node.ToJsonString();
            }
        }

        return string.Empty;
    }

    private static bool ReadBool(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetPropertyValue(name, out var node) || node is null)
            {
                continue;
            }

            return node.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(node.GetValue<string>(), out var value) && value,
                JsonValueKind.Number => node.GetValue<int>() != 0,
                _ => false
            };
        }

        return false;
    }

    private static long ReadLong(JsonObject obj, params string[] names)
        => ReadNullableLong(obj, names) ?? 0L;

    private static long? ReadNullableLong(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetPropertyValue(name, out var node) || node is null)
            {
                continue;
            }

            return node.GetValueKind() switch
            {
                JsonValueKind.Number => node.GetValue<long>(),
                JsonValueKind.String when long.TryParse(node.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
                _ => null
            };
        }

        return null;
    }

    private static double? ReadNullableDouble(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetPropertyValue(name, out var node) || node is null)
            {
                continue;
            }

            return node.GetValueKind() switch
            {
                JsonValueKind.Number => node.GetValue<double>(),
                JsonValueKind.String when double.TryParse(node.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
                _ => null
            };
        }

        return null;
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(JsonObject obj, params string[] names)
    {
        var raw = ReadString(obj, names);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }

    private static JsonObject? ReadObject(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetPropertyValue(name, out var node) && node is JsonObject jsonObject)
            {
                return JsonNode.Parse(jsonObject.ToJsonString()) as JsonObject;
            }
        }

        return null;
    }
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
