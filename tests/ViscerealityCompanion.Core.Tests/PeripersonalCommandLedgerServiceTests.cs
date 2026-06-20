using System.Text.Json;
using System.Text.Json.Nodes;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class PeripersonalCommandLedgerServiceTests
{
    [Fact]
    public void CommandLifecycle_RequiresReceiptAndObservedStateBeforeWorkflowAdvance()
    {
        var root = CreateTempRoot();
        try
        {
            var ledger = new PeripersonalCommandLedgerService(Path.Combine(root, "command_ledger.jsonl"));
            var command = CreateCommand(ledger.AllocateSequence(), requiresObservedState: true);

            var sent = ledger.RecordSent(command);

            Assert.Equal(PeripersonalCommandLifecycleState.Sent, sent.Status);
            Assert.False(sent.WorkflowAdvanceAllowed);

            var receipt = PeripersonalCommandReceiptEnvelope.Create(
                command,
                accepted: true,
                executed: true,
                completed: true,
                transportReceived: "lsl",
                observedState: new JsonObject
                {
                    ["recording_active"] = true
                });
            var executed = ledger.RecordReceipt(receipt);

            Assert.Equal(PeripersonalCommandLifecycleState.Executed, executed.Status);
            Assert.True(executed.Accepted);
            Assert.True(executed.Executed);
            Assert.False(executed.WorkflowAdvanceAllowed);

            var observed = ledger.RecordObservedState(
                command.CommandId,
                new JsonObject
                {
                    ["recording_active"] = true
                },
                predicateSatisfied: true,
                "recording_active=true observed in twin state");

            Assert.Equal(PeripersonalCommandLifecycleState.Observed, observed.Status);
            Assert.True(observed.Observed);
            Assert.True(observed.WorkflowAdvanceAllowed);

            var lines = File.ReadAllLines(Path.Combine(root, "command_ledger.jsonl"));
            Assert.Equal(3, lines.Length);
            Assert.Contains("\"event\":\"sent\"", lines[0], StringComparison.Ordinal);
            Assert.Contains("\"event\":\"receipt\"", lines[1], StringComparison.Ordinal);
            Assert.Contains("\"event\":\"observed\"", lines[2], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RecordReceipt_RejectedCommandBlocksWorkflowAdvance()
    {
        var root = CreateTempRoot();
        try
        {
            var ledger = new PeripersonalCommandLedgerService(Path.Combine(root, "command_ledger.jsonl"));
            var command = CreateCommand(ledger.AllocateSequence(), requiresObservedState: false);
            ledger.RecordSent(command);

            var rejected = ledger.RecordReceipt(PeripersonalCommandReceiptEnvelope.Create(
                command,
                accepted: false,
                executed: false,
                completed: true,
                transportReceived: "http",
                issueCode: "wrong_session",
                message: "Unity rejected the stale session id."));

            Assert.Equal(PeripersonalCommandLifecycleState.Rejected, rejected.Status);
            Assert.Equal("wrong_session", rejected.IssueCode);
            Assert.False(rejected.WorkflowAdvanceAllowed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MarkTimedOut_WritesTimeoutLedgerEntry()
    {
        var root = CreateTempRoot();
        try
        {
            var ledger = new PeripersonalCommandLedgerService(Path.Combine(root, "command_ledger.jsonl"));
            var command = CreateCommand(ledger.AllocateSequence(), requiresObservedState: true);
            ledger.RecordSent(command);

            var timedOut = ledger.MarkTimedOut(
                command.CommandId,
                "receipt_timeout",
                "No matching command receipt arrived before the deadline.");

            Assert.Equal(PeripersonalCommandLifecycleState.TimedOut, timedOut.Status);
            Assert.Equal("receipt_timeout", timedOut.IssueCode);
            Assert.False(timedOut.WorkflowAdvanceAllowed);
            Assert.Contains("\"event\":\"timeout\"", File.ReadAllText(Path.Combine(root, "command_ledger.jsonl")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RecordReceipt_RejectsMismatchedSequence()
    {
        var root = CreateTempRoot();
        try
        {
            var ledger = new PeripersonalCommandLedgerService(Path.Combine(root, "command_ledger.jsonl"));
            var command = CreateCommand(ledger.AllocateSequence(), requiresObservedState: false);
            ledger.RecordSent(command);

            var mismatched = PeripersonalCommandReceiptEnvelope.Create(
                command,
                accepted: true,
                executed: true,
                completed: true,
                transportReceived: "lsl") with
            {
                Sequence = command.Sequence + 1
            };

            var ex = Assert.Throws<InvalidOperationException>(() => ledger.RecordReceipt(mismatched));
            Assert.Contains("does not match", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AllocateSequence_PersistsAcrossServiceInstances()
    {
        var root = CreateTempRoot();
        try
        {
            var ledgerPath = Path.Combine(root, "command_ledger.jsonl");
            var first = new PeripersonalCommandLedgerService(ledgerPath);

            Assert.Equal(1, first.AllocateSequence());
            Assert.Equal(2, first.AllocateSequence());

            var second = new PeripersonalCommandLedgerService(ledgerPath);
            Assert.Equal(3, second.AllocateSequence());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PeripersonalCommandEnvelope CreateCommand(long sequence, bool requiresObservedState)
        => PeripersonalCommandEnvelope.Create(
            sequence,
            sessionId: "session-20260620T120000Z",
            participantRef: "P001",
            targetApp: "unity",
            targetRuntimeKind: "unity_quest_apk",
            targetPackage: "com.Viscereality.ViscerealityPeriPersonal",
            action: "start_recording",
            transport: "lsl",
            requiresObservedState,
            payload: new JsonObject
            {
                ["block_id"] = "xr-block-1",
                ["condition_id"] = "left-visible"
            },
            commandId: "command-001",
            sentAtUtc: new DateTimeOffset(2026, 06, 20, 12, 0, 0, TimeSpan.Zero));

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

