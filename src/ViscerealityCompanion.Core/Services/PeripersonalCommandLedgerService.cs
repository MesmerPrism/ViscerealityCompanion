using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class PeripersonalCommandLedgerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly object _sync = new();
    private readonly Dictionary<string, CommandState> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _sequencePath;
    private long _lastSequence;

    public PeripersonalCommandLedgerService(string ledgerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerPath);

        LedgerPath = Path.GetFullPath(ledgerPath);
        _sequencePath = Path.ChangeExtension(LedgerPath, ".sequence");
        LoadExistingState();
    }

    public string LedgerPath { get; }

    public long AllocateSequence()
    {
        lock (_sync)
        {
            _lastSequence++;
            Directory.CreateDirectory(Path.GetDirectoryName(_sequencePath) ?? ".");
            File.WriteAllText(_sequencePath, _lastSequence.ToString(System.Globalization.CultureInfo.InvariantCulture), Utf8NoBom);
            return _lastSequence;
        }
    }

    public PeripersonalCommandLedgerSnapshot RecordSent(PeripersonalCommandEnvelope command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command);

        lock (_sync)
        {
            if (_commands.ContainsKey(command.CommandId))
            {
                throw new InvalidOperationException($"Command `{command.CommandId}` is already present in the ledger.");
            }

            var state = CommandState.FromCommand(command);
            _commands.Add(command.CommandId, state);
            AppendRecord(new LedgerRecord(
                "sent",
                DateTimeOffset.UtcNow,
                command.CommandId,
                command.Sequence,
                command.SessionId,
                command.Action,
                command.Transport,
                PeripersonalCommandLifecycleState.Sent,
                command.RequiresObservedState,
                false,
                false,
                false,
                false,
                string.Empty,
                "Command sent.",
                command.Payload,
                null));
            return state.ToSnapshot();
        }
    }

    public PeripersonalCommandLedgerSnapshot RecordReceipt(PeripersonalCommandReceiptEnvelope receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        lock (_sync)
        {
            if (!_commands.TryGetValue(receipt.CommandId, out var state))
            {
                throw new InvalidOperationException($"Command `{receipt.CommandId}` is not present in the ledger.");
            }

            ValidateReceiptMatchesCommand(state.Command, receipt);

            state.Accepted = receipt.Accepted;
            state.Executed = receipt.Executed;
            state.Completed = receipt.Completed;
            state.IssueCode = receipt.IssueCode;
            state.Message = receipt.Message;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            state.Status = receipt.Accepted switch
            {
                false => PeripersonalCommandLifecycleState.Rejected,
                true when receipt.Executed || receipt.Completed => PeripersonalCommandLifecycleState.Executed,
                _ => PeripersonalCommandLifecycleState.Accepted
            };

            AppendRecord(new LedgerRecord(
                "receipt",
                state.UpdatedAtUtc,
                receipt.CommandId,
                receipt.Sequence,
                receipt.SessionId,
                receipt.Action,
                receipt.TransportReceived,
                state.Status,
                state.Command.RequiresObservedState,
                state.Accepted,
                state.Executed,
                state.Completed,
                state.Observed,
                receipt.IssueCode,
                receipt.Message,
                null,
                receipt.ObservedState));
            return state.ToSnapshot();
        }
    }

    public PeripersonalCommandLedgerSnapshot RecordObservedState(
        string commandId,
        JsonObject? observedState,
        bool predicateSatisfied,
        string message = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        lock (_sync)
        {
            if (!_commands.TryGetValue(commandId, out var state))
            {
                throw new InvalidOperationException($"Command `{commandId}` is not present in the ledger.");
            }

            if (state.Status is PeripersonalCommandLifecycleState.Rejected or PeripersonalCommandLifecycleState.TimedOut)
            {
                throw new InvalidOperationException($"Command `{commandId}` cannot be observed after `{state.Status}`.");
            }

            state.Observed = predicateSatisfied;
            state.Message = string.IsNullOrWhiteSpace(message) ? state.Message : message;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            if (predicateSatisfied)
            {
                state.Status = PeripersonalCommandLifecycleState.Observed;
            }

            AppendRecord(new LedgerRecord(
                predicateSatisfied ? "observed" : "observed_mismatch",
                state.UpdatedAtUtc,
                state.Command.CommandId,
                state.Command.Sequence,
                state.Command.SessionId,
                state.Command.Action,
                "state",
                state.Status,
                state.Command.RequiresObservedState,
                state.Accepted,
                state.Executed,
                state.Completed,
                state.Observed,
                predicateSatisfied ? string.Empty : "observed_state_mismatch",
                state.Message,
                null,
                observedState));
            return state.ToSnapshot();
        }
    }

    public PeripersonalCommandLedgerSnapshot MarkTimedOut(
        string commandId,
        string issueCode,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        lock (_sync)
        {
            if (!_commands.TryGetValue(commandId, out var state))
            {
                throw new InvalidOperationException($"Command `{commandId}` is not present in the ledger.");
            }

            state.Status = PeripersonalCommandLifecycleState.TimedOut;
            state.IssueCode = string.IsNullOrWhiteSpace(issueCode) ? "receipt_timeout" : issueCode.Trim();
            state.Message = message?.Trim() ?? string.Empty;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;

            AppendRecord(new LedgerRecord(
                "timeout",
                state.UpdatedAtUtc,
                state.Command.CommandId,
                state.Command.Sequence,
                state.Command.SessionId,
                state.Command.Action,
                "ledger",
                state.Status,
                state.Command.RequiresObservedState,
                state.Accepted,
                state.Executed,
                state.Completed,
                state.Observed,
                state.IssueCode,
                state.Message,
                null,
                null));
            return state.ToSnapshot();
        }
    }

    public PeripersonalCommandLedgerSnapshot? TryGetSnapshot(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        lock (_sync)
        {
            return _commands.TryGetValue(commandId, out var state)
                ? state.ToSnapshot()
                : null;
        }
    }

    private void LoadExistingState()
    {
        if (File.Exists(_sequencePath) &&
            long.TryParse(File.ReadAllText(_sequencePath), out var sequence))
        {
            _lastSequence = Math.Max(0, sequence);
        }

        if (!File.Exists(LedgerPath))
        {
            return;
        }

        foreach (var line in File.ReadLines(LedgerPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var record = JsonSerializer.Deserialize<LedgerRecord>(line, JsonOptions);
            if (record is null)
            {
                continue;
            }

            _lastSequence = Math.Max(_lastSequence, record.Sequence);
        }
    }

    private void AppendRecord(LedgerRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath) ?? ".");
        File.AppendAllText(
            LedgerPath,
            JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine,
            Utf8NoBom);
    }

    private static void ValidateCommand(PeripersonalCommandEnvelope command)
    {
        if (command.ProtocolVersion != PeripersonalCommandProtocol.CommandProtocolVersion)
        {
            throw new InvalidDataException($"Unsupported command protocol `{command.ProtocolVersion}`.");
        }

        Require(command.CommandId, "command_id");
        Require(command.SessionId, "session_id");
        Require(command.TargetApp, "target_app");
        Require(command.TargetRuntimeKind, "target_runtime_kind");
        Require(command.TargetPackage, "target_package");
        Require(command.Action, "action");
        Require(command.Transport, "transport");
        if (command.Sequence <= 0)
        {
            throw new InvalidDataException("Command sequence must be positive.");
        }
    }

    private static void ValidateReceiptMatchesCommand(
        PeripersonalCommandEnvelope command,
        PeripersonalCommandReceiptEnvelope receipt)
    {
        if (receipt.ProtocolVersion != PeripersonalCommandProtocol.ReceiptProtocolVersion)
        {
            throw new InvalidDataException($"Unsupported receipt protocol `{receipt.ProtocolVersion}`.");
        }

        if (receipt.Sequence != command.Sequence ||
            !string.Equals(receipt.SessionId, command.SessionId, StringComparison.Ordinal) ||
            !string.Equals(receipt.TargetApp, command.TargetApp, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.Action, command.Action, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Receipt for command `{receipt.CommandId}` does not match the sent command envelope.");
        }
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Command field `{name}` is required.");
        }
    }

    private sealed class CommandState
    {
        private CommandState(PeripersonalCommandEnvelope command)
        {
            Command = command;
            Status = PeripersonalCommandLifecycleState.Sent;
            UpdatedAtUtc = command.SentAtUtc;
        }

        public PeripersonalCommandEnvelope Command { get; }
        public PeripersonalCommandLifecycleState Status { get; set; }
        public bool Accepted { get; set; }
        public bool Executed { get; set; }
        public bool Completed { get; set; }
        public bool Observed { get; set; }
        public string IssueCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; set; }

        public static CommandState FromCommand(PeripersonalCommandEnvelope command) => new(command);

        public PeripersonalCommandLedgerSnapshot ToSnapshot()
            => new(
                Command.CommandId,
                Command.Sequence,
                Command.SessionId,
                Command.Action,
                Status,
                Accepted,
                Executed,
                Completed,
                Observed,
                Command.RequiresObservedState,
                IssueCode,
                Message,
                Command.SentAtUtc,
                UpdatedAtUtc);
    }

    private sealed record LedgerRecord(
        string Event,
        DateTimeOffset RecordedAtUtc,
        string CommandId,
        long Sequence,
        string SessionId,
        string Action,
        string Transport,
        PeripersonalCommandLifecycleState Status,
        bool RequiresObservedState,
        bool Accepted,
        bool Executed,
        bool Completed,
        bool Observed,
        string IssueCode,
        string Message,
        JsonObject? Payload,
        JsonObject? ObservedState);
}

