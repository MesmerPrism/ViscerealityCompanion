using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class PeripersonalCommandTransportTests
{
    [Fact]
    public async Task LslTransport_PublishesCommandJsonAndReturnsMatchingReceipt()
    {
        var outlet = new CapturingOutlet();
        var monitor = new QueuedMonitor();
        var command = CreateCommand("unity", "unity_quest_apk", "com.Viscereality.ViscerealityPeriPersonal", "prepare_session");
        monitor.EnqueueReceipt(PeripersonalCommandReceiptEnvelope.Create(
            command,
            accepted: true,
            executed: true,
            completed: true,
            transportReceived: "lsl",
            observedState: new JsonObject { ["session_ready"] = true }));

        var transport = new PeripersonalLslCommandTransport(outlet, monitor, TimeSpan.FromSeconds(1));
        var receipt = await transport.SendAsync(command);

        Assert.True(outlet.IsOpen);
        Assert.Single(outlet.Samples);
        Assert.Contains("\"commandId\":\"command-001\"", outlet.Samples[0][0], StringComparison.Ordinal);
        Assert.Equal(command.CommandId, receipt.CommandId);
        Assert.True(receipt.Accepted);
    }

    [Fact]
    public void ReceiptParser_AcceptsUnitySnakeCaseReceiptEnvelope()
    {
        var receipt = PeripersonalCommandReceiptEnvelope.ParseJson(
            "{" +
            "\"protocol_version\":\"viscereality.peripersonal.command_receipt.v1\"," +
            "\"command_id\":\"command-001\"," +
            "\"sequence\":12," +
            "\"session_id\":\"session-1\"," +
            "\"target_app\":\"unity\"," +
            "\"action\":\"start_recording\"," +
            "\"transport_received\":\"lsl\"," +
            "\"received_lsl_timestamp\":123.5," +
            "\"received_at_utc\":\"2026-06-20T12:00:00Z\"," +
            "\"accepted\":true," +
            "\"executed\":true," +
            "\"completed\":true," +
            "\"issue_code\":\"\"," +
            "\"message\":\"ok\"," +
            "\"state_revision\":7," +
            "\"observed_state\":{\"recording_active\":true}" +
            "}");

        Assert.Equal("command-001", receipt.CommandId);
        Assert.Equal(12, receipt.Sequence);
        Assert.Equal("unity", receipt.TargetApp);
        Assert.True(receipt.ObservedState?["recording_active"]?.GetValue<bool>());
    }

    [Fact]
    public void AndroidBroadcastTransport_ExtractsReceiptFromAmBroadcastResultData()
    {
        const string output =
            "Broadcasting: Intent { act=io.github.mesmerprism.questquestionnaire.panel.action.PERIPERSONAL_COMMAND }\n" +
            "Broadcast completed: result=-1, data=\"{\\\"protocol_version\\\":\\\"viscereality.peripersonal.command_receipt.v1\\\",\\\"command_id\\\":\\\"command-001\\\"}\"";

        var receiptJson = PeripersonalAndroidBroadcastCommandTransport.ExtractResultData(output);

        Assert.Contains("\"command_id\":\"command-001\"", receiptJson, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidBroadcastTransport_ExtractsPrettyReceiptFromQuestBroadcastOutput()
    {
        const string output =
            "Broadcast completed: result=-1, data=\"{\n" +
            "  \"protocol_version\": \"viscereality.peripersonal.command_receipt.v1\",\n" +
            "  \"command_id\": \"command-001\",\n" +
            "  \"observed_state\": {\n" +
            "    \"session_dir\": \"\\/data\\/user\\/0\\/io.github.mesmerprism.questquestionnaire.panel\\/files\\/peripersonal_sessions\\/P001_session-1_20260620-120000\"\n" +
            "  }\n" +
            "}\", extras: Bundle[mParcelledData.dataSize=2048]";

        var receiptJson = PeripersonalAndroidBroadcastCommandTransport.ExtractResultData(output);
        var receipt = PeripersonalCommandReceiptEnvelope.ParseJson(
            receiptJson.Replace(
                "\"observed_state\": {",
                "\"sequence\":1,\"session_id\":\"session-1\",\"target_app\":\"panel\",\"action\":\"prepare_session\",\"transport_received\":\"android_ordered_broadcast\",\"accepted\":true,\"executed\":true,\"completed\":true,\"issue_code\":\"\",\"message\":\"ok\",\"state_revision\":1,\"observed_state\": {",
                StringComparison.Ordinal));

        Assert.Equal("command-001", receipt.CommandId);
        Assert.Contains("/data/user/0/io.github.mesmerprism.questquestionnaire.panel", receiptJson, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidBroadcastTransport_BuildsShellQuotedBroadcastCommand()
    {
        const string commandJson = "{\"commandId\":\"command-001\",\"payload\":{\"participantRef\":\"O'Brien\"}}";

        var shellCommand = PeripersonalAndroidBroadcastCommandTransport.BuildBroadcastShellCommand(
            "io.github.mesmerprism.questquestionnaire.panel.action.PERIPERSONAL_COMMAND",
            "io.github.mesmerprism.questquestionnaire.panel/.PeripersonalPanelCommandReceiver",
            "io.github.mesmerprism.questquestionnaire.panel.extra.COMMAND_JSON",
            commandJson);

        Assert.StartsWith("am broadcast -a 'io.github.mesmerprism.questquestionnaire.panel.action.PERIPERSONAL_COMMAND'", shellCommand, StringComparison.Ordinal);
        Assert.Contains("--es 'io.github.mesmerprism.questquestionnaire.panel.extra.COMMAND_JSON'", shellCommand, StringComparison.Ordinal);
        Assert.Contains("'\\''", shellCommand, StringComparison.Ordinal);
        Assert.Contains("\"participantRef\":\"O'\\''Brien\"", shellCommand, StringComparison.Ordinal);
        Assert.EndsWith("'", shellCommand, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdbQuestHttpForwarder_UsesDeviceScopedUnityHttpBridgeForward()
    {
        var runner = new FakeAdbProcessRunner(
            new PeripersonalAdbProcessResult(0, string.Empty, string.Empty),
            new PeripersonalAdbProcessResult(0, "3487 tcp:8787 tcp:8787", string.Empty));
        var forwarder = new PeripersonalAdbQuestHttpForwarder(
            "adb.exe",
            "3487",
            PeripersonalAdbQuestHttpForwarder.DefaultUnityHttpBridgePort,
            PeripersonalAdbQuestHttpForwarder.DefaultUnityHttpBridgePort,
            TimeSpan.FromSeconds(1),
            runner);

        var result = await forwarder.EnsureUnityHttpBridgeForwardAsync();

        Assert.Equal(OperationOutcomeKind.Success, result.Kind);
        Assert.Equal("127.0.0.1:8787", result.Endpoint);
        Assert.Equal(["-s", "3487", "forward", "tcp:8787", "tcp:8787"], runner.Calls[0]);
        Assert.Equal(["-s", "3487", "forward", "--list"], runner.Calls[1]);
    }

    [Fact]
    public async Task AdbQuestHttpForwarder_FailsWhenForwardCommandFails()
    {
        var runner = new FakeAdbProcessRunner(
            new PeripersonalAdbProcessResult(1, string.Empty, "device offline"));
        var forwarder = new PeripersonalAdbQuestHttpForwarder(
            "adb.exe",
            "3487",
            8787,
            8787,
            TimeSpan.FromSeconds(1),
            runner);

        var result = await forwarder.EnsureUnityHttpBridgeForwardAsync();

        Assert.Equal(OperationOutcomeKind.Failure, result.Kind);
        Assert.Contains("device offline", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task AndroidBroadcastTransport_LiveQuestPanelPrepareReceipt_WhenEnvironmentIsSet()
    {
        var adbPath = Environment.GetEnvironmentVariable("PERIPERSONAL_PANEL_TRANSPORT_ADB");
        var selector = Environment.GetEnvironmentVariable("PERIPERSONAL_PANEL_TRANSPORT_SERIAL");
        if (string.IsNullOrWhiteSpace(adbPath) || string.IsNullOrWhiteSpace(selector))
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var sessionFolderName = $"PANELTRANSPORT_session-001_{timestamp}";
        var command = PeripersonalCommandEnvelope.Create(
            sequence: 42,
            sessionId: "session-001",
            participantRef: "PANELTRANSPORT",
            targetApp: "panel",
            targetRuntimeKind: "quest_questionnaire_panel_apk",
            targetPackage: "io.github.mesmerprism.questquestionnaire.panel",
            action: "prepare_session",
            transport: "adb_ordered_broadcast",
            requiresObservedState: true,
            payload: new JsonObject
            {
                ["study_id"] = "peripersonal-space",
                ["session_id"] = "session-001",
                ["participant_ref"] = "PANELTRANSPORT",
                ["handedness"] = "right-handed",
                ["breath_tracking_controller_side"] = "left",
                ["session_folder_name"] = sessionFolderName,
                ["session_folder_convention"] = PeripersonalOperatorWorkflowService.SessionFolderConvention,
                ["recording_lifecycle"] = PeripersonalOperatorWorkflowService.RecordingLifecycle,
                ["runtime_state_lsl_stream_name"] = "peripersonal_runtime_state_PANELTRANSPORT",
                ["particle_trigger_lsl_stream_name"] = "peripersonal_particle_triggers_PANELTRANSPORT"
            },
            commandId: $"panel-transport-{timestamp}",
            sentAtUtc: DateTimeOffset.UtcNow);

        var transport = new PeripersonalAndroidBroadcastCommandTransport(
            adbPath,
            selector,
            timeout: TimeSpan.FromSeconds(20));
        var receipt = await transport.SendAsync(command);

        Assert.True(receipt.Accepted);
        Assert.True(receipt.Executed);
        Assert.True(receipt.Completed);
        Assert.Equal(command.CommandId, receipt.CommandId);
        Assert.Equal("prepare_session", receipt.Action);
        Assert.True(receipt.ObservedState?["session_ready"]?.GetValue<bool>());
        Assert.Equal(sessionFolderName, receipt.ObservedState?["session_folder_name"]?.GetValue<string>());
        Assert.Equal("left", receipt.ObservedState?["breath_tracking_controller_side"]?.GetValue<string>());
    }

    private static PeripersonalCommandEnvelope CreateCommand(
        string targetApp,
        string targetRuntimeKind,
        string targetPackage,
        string action)
        => PeripersonalCommandEnvelope.Create(
            sequence: 1,
            sessionId: "session-1",
            participantRef: "P001",
            targetApp,
            targetRuntimeKind,
            targetPackage,
            action,
            transport: "lsl",
            requiresObservedState: true,
            payload: new JsonObject
            {
                ["session_folder_name"] = "P001_session-1_20260620-120000"
            },
            commandId: "command-001",
            sentAtUtc: new DateTimeOffset(2026, 06, 20, 12, 0, 0, TimeSpan.Zero));

    private sealed class CapturingOutlet : ILslOutletService
    {
        public LslRuntimeState RuntimeState { get; } = new(true, "fake");
        public bool IsOpen { get; private set; }
        public List<string[]> Samples { get; } = [];

        public OperationOutcome Open(string streamName, string streamType, int channelCount)
        {
            IsOpen = true;
            return new OperationOutcome(OperationOutcomeKind.Success, "opened", $"{streamName}/{streamType}/{channelCount}");
        }

        public void Close() => IsOpen = false;
        public void PushSample(string[] values) => Samples.Add(values);
        public OperationOutcome PublishConfigSnapshot(IReadOnlyList<RuntimeConfigEntry> entries) => throw new NotSupportedException();
        public OperationOutcome PublishCommand(TwinModeCommand command, int sequence) => throw new NotSupportedException();
        public void Dispose() => Close();
    }

    private sealed class QueuedMonitor : ILslMonitorService
    {
        private readonly Queue<string> _receipts = new();
        public LslRuntimeState RuntimeState { get; } = new(true, "fake");

        public void EnqueueReceipt(PeripersonalCommandReceiptEnvelope receipt)
            => _receipts.Enqueue("{" +
                $"\"protocol_version\":\"{receipt.ProtocolVersion}\"," +
                $"\"command_id\":\"{receipt.CommandId}\"," +
                $"\"sequence\":{receipt.Sequence}," +
                $"\"session_id\":\"{receipt.SessionId}\"," +
                $"\"target_app\":\"{receipt.TargetApp}\"," +
                $"\"action\":\"{receipt.Action}\"," +
                $"\"transport_received\":\"{receipt.TransportReceived}\"," +
                "\"accepted\":true," +
                "\"executed\":true," +
                "\"completed\":true," +
                "\"issue_code\":\"\"," +
                $"\"message\":\"{receipt.Message}\"," +
                "\"state_revision\":1," +
                "\"observed_state\":{\"session_ready\":true}" +
                "}");

        public async IAsyncEnumerable<LslMonitorReading> MonitorAsync(
            LslMonitorSubscription subscription,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (_receipts.TryDequeue(out var receipt))
            {
                yield return new LslMonitorReading(
                    "Streaming LSL sample.",
                    "fake",
                    null,
                    0,
                    DateTimeOffset.UtcNow,
                    TextValue: receipt,
                    SampleValues: [receipt],
                    ChannelFormat: LslChannelFormat.String);
            }

            await Task.CompletedTask;
        }
    }

    private sealed class FakeAdbProcessRunner : IPeripersonalAdbProcessRunner
    {
        private readonly Queue<PeripersonalAdbProcessResult> _results;

        public FakeAdbProcessRunner(params PeripersonalAdbProcessResult[] results)
        {
            _results = new Queue<PeripersonalAdbProcessResult>(results);
        }

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<PeripersonalAdbProcessResult> RunTextAsync(
            string adbPath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls.Add(arguments.ToArray());
            return Task.FromResult(_results.Dequeue());
        }

        public Task<PeripersonalAdbProcessResult> RunStdoutToFileAsync(
            string adbPath,
            IReadOnlyList<string> arguments,
            string outputPath,
            TimeSpan timeout,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
