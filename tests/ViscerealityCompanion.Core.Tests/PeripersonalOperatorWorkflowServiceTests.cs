using System.Text.Json.Nodes;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class PeripersonalOperatorWorkflowServiceTests
{
    [Fact]
    public async Task PrepareSession_CreatesWindowsFolderAndSendsUnityThenPanelPrepare()
    {
        var root = CreateTempRoot();
        try
        {
            var transport = new FakePeripersonalCommandTransport();
            var workflow = CreateWorkflow(root, transport);

            var result = await workflow.PrepareSessionAsync(new PeripersonalSessionSetupRequest(
                ParticipantRef: "P001",
                SessionId: "session-1",
                Handedness: "right-handed"));

            Assert.True(result.Succeeded);
            Assert.Equal(PeripersonalOperatorWorkflowState.Prepared, workflow.State);
            Assert.NotNull(workflow.CurrentSession);
            var session = workflow.CurrentSession!;
            Assert.Equal("P001_session-1_20260620-123456", session.SessionFolderName);
            Assert.Equal("left", session.BreathTrackingControllerSide);
            Assert.True(File.Exists(Path.Combine(session.WindowsSessionDirectory, "windows_session_metadata.json")));
            Assert.True(File.Exists(session.LedgerPath));

            Assert.Equal(["unity:prepare_session", "panel:prepare_session"], transport.CommandLog);
            Assert.Equal("peripersonal_runtime_state_P001", transport.SentCommands[0].Payload?["runtime_state_lsl_stream_name"]?.GetValue<string>());
            Assert.Equal("peripersonal_particle_triggers_P001", transport.SentCommands[0].Payload?["particle_trigger_lsl_stream_name"]?.GetValue<string>());
            Assert.Equal("quest_questionnaire_panel_apk", transport.SentCommands[1].TargetRuntimeKind);
            Assert.Equal("io.github.mesmerprism.questquestionnaire.panel", transport.SentCommands[1].TargetPackage);

            var ledgerLines = File.ReadAllLines(session.LedgerPath);
            Assert.Equal(6, ledgerLines.Length);
            Assert.Equal(2, ledgerLines.Count(line => line.Contains("\"event\":\"sent\"", StringComparison.Ordinal)));
            Assert.Equal(2, ledgerLines.Count(line => line.Contains("\"event\":\"receipt\"", StringComparison.Ordinal)));
            Assert.Equal(2, ledgerLines.Count(line => line.Contains("\"event\":\"observed\"", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StartRecording_IsBlockedUntilQuestionnaireBlockOneThenUsesOneGlobalStartAndFinalStop()
    {
        var root = CreateTempRoot();
        try
        {
            var transport = new FakePeripersonalCommandTransport();
            var workflow = CreateWorkflow(root, transport);
            await workflow.PrepareSessionAsync(new PeripersonalSessionSetupRequest(
                ParticipantRef: "P001",
                SessionId: "session-1",
                Handedness: "right-handed"));

            var blockedStart = await workflow.StartRecordingAsync();
            Assert.False(blockedStart.Succeeded);
            Assert.Equal(OperationOutcomeKind.Warning, blockedStart.Outcome.Kind);
            Assert.DoesNotContain(transport.CommandLog, command => command.EndsWith(":start_recording", StringComparison.Ordinal));

            workflow.RecordQuestionnaireSubmitted("block-1");
            var started = await workflow.StartRecordingAsync();
            Assert.True(started.Succeeded);
            Assert.Equal(PeripersonalOperatorWorkflowState.Recording, workflow.State);

            var blockEnd = await workflow.MarkXrBlockEndAsync("xr-block-1", "left-visible");
            Assert.True(blockEnd.Succeeded);
            var stopped = await workflow.StopRecordingAndCloseAppsAsync();
            Assert.True(stopped.Succeeded);
            Assert.Equal(PeripersonalOperatorWorkflowState.Stopped, workflow.State);

            Assert.Equal(1, transport.CommandLog.Count(command => command == "unity:start_recording"));
            Assert.Equal(1, transport.CommandLog.Count(command => command == "unity:stop_recording_and_close_apps"));
            Assert.DoesNotContain("unity:stop_session", transport.CommandLog);
            Assert.Contains("unity:mark_block_end", transport.CommandLog);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ParticleVisibilityCommandsCarryDistinctTriggerLabels()
    {
        var root = CreateTempRoot();
        try
        {
            var transport = new FakePeripersonalCommandTransport();
            var workflow = CreatePreparedRecordingWorkflow(root, transport);

            var on = await workflow.SetParticlesVisibleAsync(true);
            var off = await workflow.SetParticlesVisibleAsync(false);

            Assert.True(on.Succeeded);
            Assert.True(off.Succeeded);
            var particleCommands = transport.SentCommands
                .Where(command => command.Action is "set_particles_visible" or "set_particles_hidden")
                .ToArray();
            Assert.Equal(2, particleCommands.Length);
            Assert.Equal("Particles-ON", particleCommands[0].Payload?["particle_trigger_label"]?.GetValue<string>());
            Assert.Equal("Particles-OFF", particleCommands[1].Payload?["particle_trigger_label"]?.GetValue<string>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("right-handed", "left")]
    [InlineData("right", "left")]
    [InlineData("left-handed", "right")]
    [InlineData("left", "right")]
    public void BreathTrackingControllerFromHandedness_UsesOppositeController(string handedness, string expectedController)
    {
        Assert.Equal(expectedController, PeripersonalOperatorWorkflowService.BreathTrackingControllerFromHandedness(handedness));
    }

    private static PeripersonalOperatorWorkflowService CreatePreparedRecordingWorkflow(
        string root,
        FakePeripersonalCommandTransport transport)
    {
        var workflow = CreateWorkflow(root, transport);
        workflow.PrepareSessionAsync(new PeripersonalSessionSetupRequest(
            ParticipantRef: "P001",
            SessionId: "session-1",
            Handedness: "right-handed")).GetAwaiter().GetResult();
        workflow.RecordQuestionnaireSubmitted("block-1");
        workflow.StartRecordingAsync().GetAwaiter().GetResult();
        return workflow;
    }

    private static PeripersonalOperatorWorkflowService CreateWorkflow(
        string root,
        FakePeripersonalCommandTransport transport)
        => new(
            CreateStudy(),
            transport,
            root,
            utcNow: () => new DateTimeOffset(2026, 06, 20, 12, 34, 56, TimeSpan.Zero));

    private static StudyShellDefinition CreateStudy()
        => new(
            "peripersonal-space",
            "Peripersonal Space",
            "Peripersonal study team",
            "Peripersonal workflow test.",
            new StudyPinnedApp(
                "Peripersonal Unity Runtime APK",
                "com.Viscereality.ViscerealityPeriPersonal",
                "PeripersonalRuntime.apk",
                "com.Viscereality.ViscerealityPeriPersonal/com.unity3d.player.UnityPlayerGameActivity",
                "SHA",
                "test",
                string.Empty,
                AllowManualSelection: true,
                LaunchInKioskMode: false),
            new StudyPinnedDeviceProfile(
                "peripersonal-study-profile",
                "Peripersonal Study Device Profile",
                string.Empty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["viscereality.panel.package"] = "io.github.mesmerprism.questquestionnaire.panel",
                    ["viscereality.panel.targetRuntimeKind"] = "quest_questionnaire_panel_apk"
                }),
            new StudyMonitoringProfile(
                ExpectedBreathingLabel: string.Empty,
                ExpectedHeartbeatLabel: string.Empty,
                ExpectedCoherenceLabel: string.Empty,
                ExpectedLslStreamName: "peripersonal_runtime_state_{participantRef}",
                ExpectedLslStreamType: "peripersonal.runtime.state",
                RecenterDistanceThresholdUnits: 0.2d,
                LslConnectivityKeys: [],
                LslStreamNameKeys: [],
                LslStreamTypeKeys: [],
                LslValueKeys: [],
                ControllerValueKeys: [],
                ControllerStateKeys: [],
                ControllerTrackingKeys: [],
                AutomaticBreathingValueKeys: [],
                HeartbeatValueKeys: [],
                HeartbeatStateKeys: [],
                CoherenceValueKeys: [],
                CoherenceStateKeys: [],
                PerformanceFpsKeys: [],
                PerformanceFrameTimeKeys: [],
                PerformanceTargetFpsKeys: [],
                PerformanceRefreshRateKeys: [],
                RecenterDistanceKeys: [],
                ParticleVisibilityKeys: []),
            new StudyControlProfile(
                RecenterCommandActionId: "mark_timing_event",
                ParticleVisibleOnActionId: "set_particles_visible",
                ParticleVisibleOffActionId: "set_particles_hidden",
                StartExperimentActionId: "start_recording",
                EndExperimentActionId: "stop_recording_and_close_apps"));

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakePeripersonalCommandTransport : IPeripersonalCommandTransport
    {
        public List<PeripersonalCommandEnvelope> SentCommands { get; } = [];
        public List<string> CommandLog { get; } = [];

        public Task<PeripersonalCommandReceiptEnvelope> SendAsync(
            PeripersonalCommandEnvelope command,
            CancellationToken cancellationToken = default)
        {
            SentCommands.Add(command);
            CommandLog.Add($"{command.TargetApp}:{command.Action}");

            var observedState = new JsonObject
            {
                ["session_ready"] = command.Action == "prepare_session" || command.Action != "prepare_session",
                ["recording_active"] = command.Action switch
                {
                    "start_recording" => true,
                    "stop_recording_and_close_apps" => false,
                    _ => false
                },
                ["quest_apps_close_requested"] = command.Action == "stop_recording_and_close_apps",
                ["session_folder_name"] = command.Payload?["session_folder_name"]?.GetValue<string>() ?? string.Empty
            };
            if (command.Payload?["particles_visible"] is JsonNode particlesNode)
            {
                observedState["particles_visible"] = particlesNode.GetValue<bool>();
            }

            return Task.FromResult(PeripersonalCommandReceiptEnvelope.Create(
                command,
                accepted: true,
                executed: true,
                completed: true,
                transportReceived: command.Transport,
                message: $"{command.Action} accepted.",
                observedState: observedState,
                stateRevision: command.Sequence));
        }
    }
}
