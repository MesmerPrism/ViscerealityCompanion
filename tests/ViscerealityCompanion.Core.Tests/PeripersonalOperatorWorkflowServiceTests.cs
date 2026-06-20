using System.Diagnostics;
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
            Assert.Equal("peripersonal-space-P001-session-1", transport.SentCommands[0].Payload?["dataset_id"]?.GetValue<string>());
            Assert.Equal("peripersonal-space-P001-session-1", transport.SentCommands[0].Payload?["dataset_hash"]?.GetValue<string>());
            Assert.Equal("quest_questionnaire_panel_apk", transport.SentCommands[1].TargetRuntimeKind);
            Assert.Equal("io.github.mesmerprism.questquestionnaire.panel", transport.SentCommands[1].TargetPackage);
            var metadata = File.ReadAllText(Path.Combine(session.WindowsSessionDirectory, "windows_session_metadata.json"));
            Assert.Contains("\"dataset_id\": \"peripersonal-space-P001-session-1\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"dataset_hash\": \"peripersonal-space-P001-session-1\"", metadata, StringComparison.Ordinal);

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
            var appCloser = new FakeQuestAppCloser();
            var backupPuller = new FakeQuestBackupPuller();
            var workflow = CreateWorkflow(root, transport, appCloser, backupPuller);
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
            Assert.Equal(
                ["com.Viscereality.ViscerealityPeriPersonal", "io.github.mesmerprism.questquestionnaire.panel"],
                appCloser.ClosedPackages);
            var pulledSessionFolder = Assert.Single(backupPuller.PulledSessionFolders);
            Assert.Equal("P001_session-1_20260620-123456", pulledSessionFolder);
            Assert.Equal(Path.Combine(workflow.CurrentSession!.WindowsSessionDirectory, "device-session-pull"), stopped.Outcome.SafeItems.Single());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpenQuestionnaireBlock_UsesMaiaSpatialRendererContractForBlockOne()
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

            var opened = await workflow.OpenQuestionnaireBlockAsync(
                "request-1",
                "block-1",
                ["block-1"],
                conditionNumber: 1);

            Assert.True(opened.Succeeded);
            var command = transport.SentCommands.Single(item => item.Action == "open_questionnaire_block");
            Assert.Equal(PeripersonalOperatorWorkflowService.PanelQuestionnaireId, command.Payload?["schema_id"]?.GetValue<string>());
            Assert.Equal(PeripersonalOperatorWorkflowService.PanelQuestionnaireId, command.Payload?["questionnaire_id"]?.GetValue<string>());
            Assert.Equal(PeripersonalOperatorWorkflowService.PanelQuestionnaireBlock1LanguageStage, command.Payload?["open_stage"]?.GetValue<string>());
            Assert.Equal("block-1", command.Payload?["operator_questionnaire_block_id"]?.GetValue<string>());
            Assert.Equal("en", command.Payload?["language_code"]?.GetValue<string>());

            var sequence = command.Payload?["screen_sequence"]?.AsArray()
                .Select(item => item!.GetValue<string>())
                .ToArray();
            Assert.NotNull(sequence);
            Assert.Equal(
                [
                    PeripersonalOperatorWorkflowService.PanelQuestionnaireBlock1LanguageStage,
                    PeripersonalOperatorWorkflowService.PanelQuestionnaireBlock1DemographicsStage,
                    PeripersonalOperatorWorkflowService.PanelQuestionnaireBlock1Maia2Stage
                ],
                sequence);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpenQuestionnaireBlock_UsesMaiaSpatialRendererContractForBlocksTwoAndThree()
    {
        var root = CreateTempRoot();
        try
        {
            var transport = new FakePeripersonalCommandTransport();
            var workflow = CreateWorkflow(root, transport);
            await workflow.PrepareSessionAsync(new PeripersonalSessionSetupRequest(
                ParticipantRef: "P001",
                SessionId: "session-1",
                Handedness: "right-handed",
                LanguageCode: "de"));
            workflow.RecordQuestionnaireSubmitted("block-1");
            var started = await workflow.StartRecordingAsync();
            Assert.True(started.Succeeded);

            var block2 = await workflow.OpenQuestionnaireBlockAsync(
                "request-2",
                "block-2",
                ["block-2"],
                conditionNumber: 2);
            var block3 = await workflow.OpenQuestionnaireBlockAsync(
                "request-3",
                "block-3",
                ["block-3"],
                conditionNumber: 3);

            Assert.True(block2.Succeeded);
            Assert.True(block3.Succeeded);
            var questionnaireCommands = transport.SentCommands
                .Where(item => item.Action == "open_questionnaire_block")
                .ToArray();
            Assert.Equal(2, questionnaireCommands.Length);
            AssertMaiaQuestionnaireCommand(
                questionnaireCommands[0],
                "block-2",
                PeripersonalOperatorWorkflowService.PanelQuestionnaireBlock2SpatialFrameStage,
                [PeripersonalOperatorWorkflowService.PanelQuestionnaireBlock2SpatialFrameStage],
                expectedLanguage: "de");
            AssertMaiaQuestionnaireCommand(
                questionnaireCommands[1],
                "block-3",
                PeripersonalOperatorWorkflowService.PanelQuestionnaireBlock3SpatialFrameStage,
                [PeripersonalOperatorWorkflowService.PanelQuestionnaireBlock3SpatialFrameStage],
                expectedLanguage: "de");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpenQuestionnaireBlock_CanCarryParticipantAutomationScriptForEvidenceRuns()
    {
        var root = CreateTempRoot();
        try
        {
            var transport = new FakePeripersonalCommandTransport();
            var workflow = CreatePreparedRecordingWorkflow(root, transport);

            var opened = await workflow.OpenQuestionnaireBlockAsync(
                "request-automation",
                "block-2",
                ["block-2"],
                conditionNumber: 2,
                participantCommandScript: "submit;choice=D;submit",
                participantCommandIntervalMs: 1500);

            Assert.True(opened.Succeeded);
            var command = transport.SentCommands.Last(item => item.Action == "open_questionnaire_block");
            Assert.Equal("submit;choice=D;submit", command.Payload?["debug_command_script"]?.GetValue<string>());
            Assert.Equal(1500, command.Payload?["debug_command_interval_ms"]?.GetValue<int>());
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

    [Fact]
    public async Task RunClockProbe_UsesPeripersonalDatasetIdAndPersistsRoundTripCsv()
    {
        var root = CreateTempRoot();
        try
        {
            var transport = new FakePeripersonalCommandTransport();
            var workflow = CreatePreparedRecordingWorkflow(root, transport);
            using var clockAlignment = new FakeClockAlignmentService();

            var result = await workflow.RunClockProbeAsync(
                clockAlignment,
                duration: TimeSpan.FromSeconds(1),
                probeInterval: TimeSpan.FromMilliseconds(100));

            Assert.True(result.Succeeded, result.Outcome.Detail);
            Assert.Equal(StudyClockAlignmentWindowKind.BackgroundSparse, clockAlignment.LastRequest?.WindowKind);
            Assert.Equal("session-1", clockAlignment.LastRequest?.SessionId);
            Assert.Equal("peripersonal-space-P001-session-1", clockAlignment.LastRequest?.DatasetHash);
            Assert.True(File.Exists(result.WindowsRoundTripCsvPath), result.WindowsRoundTripCsvPath);
            var lines = File.ReadAllLines(result.WindowsRoundTripCsvPath);
            Assert.Equal(2, lines.Length);
            Assert.Contains("participant_id,session_id,dataset_id", lines[0], StringComparison.Ordinal);
            Assert.Contains("P001,session-1,peripersonal-space-P001-session-1,BackgroundSparse,1", lines[1], StringComparison.Ordinal);
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

    [Fact]
    public async Task LiveQuestWorkflow_ContinuousRecordingReceipts_WhenEnvironmentIsSet()
    {
        var adbPath = Environment.GetEnvironmentVariable("PERIPERSONAL_LIVE_WORKFLOW_ADB");
        var selector = Environment.GetEnvironmentVariable("PERIPERSONAL_LIVE_WORKFLOW_SERIAL");
        if (string.IsNullOrWhiteSpace(adbPath) || string.IsNullOrWhiteSpace(selector))
        {
            return;
        }

        var study = CreateStudy();
        var root = CreateTempRoot();
        try
        {
            await RunAdbAsync(adbPath, selector, ["shell", "am", "force-stop", study.App.PackageId]);
            await RunAdbAsync(adbPath, selector, ["shell", "am", "start", "-n", study.App.LaunchComponent]);
            await Task.Delay(TimeSpan.FromSeconds(12));

            using var commandOutlet = new WindowsLslOutletService();
            Assert.True(commandOutlet.RuntimeState.Available, commandOutlet.RuntimeState.Detail);
            var open = commandOutlet.Open(
                PeripersonalLslCommandTransport.CommandStreamName,
                PeripersonalLslCommandTransport.CommandStreamType,
                channelCount: 1);
            Assert.NotEqual(OperationOutcomeKind.Failure, open.Kind);
            await Task.Delay(TimeSpan.FromSeconds(4));

            var transport = new PeripersonalCompositeCommandTransport(
                new PeripersonalLslCommandTransport(
                    commandOutlet,
                    new WindowsLslMonitorService(),
                    TimeSpan.FromSeconds(25)),
                new PeripersonalAndroidBroadcastCommandTransport(
                    adbPath,
                    selector,
                    timeout: TimeSpan.FromSeconds(20)));
            var workflow = new PeripersonalOperatorWorkflowService(
                study,
                transport,
                root,
                questAppCloser: new PeripersonalAdbQuestAppCloser(
                    adbPath,
                    selector,
                    TimeSpan.FromSeconds(15)),
                questBackupPuller: new PeripersonalAdbQuestBackupPuller(
                    adbPath,
                    selector,
                    TimeSpan.FromSeconds(20)));

            var prepare = await workflow.PrepareSessionAsync(new PeripersonalSessionSetupRequest(
                ParticipantRef: "LIVEWF",
                SessionId: "session-001",
                Handedness: "right-handed"));
            Assert.True(prepare.Succeeded, prepare.Outcome.Detail);
            Assert.Equal("left", prepare.Session?.BreathTrackingControllerSide);
            Assert.StartsWith("LIVEWF_session-001_", prepare.Session?.SessionFolderName, StringComparison.Ordinal);

            workflow.RecordQuestionnaireSubmitted("block-1");
            var started = await workflow.StartRecordingAsync();
            Assert.True(started.Succeeded, started.Outcome.Detail);
            var particlesOn = await workflow.SetParticlesVisibleAsync(true);
            Assert.True(particlesOn.Succeeded, particlesOn.Outcome.Detail);
            var particlesOff = await workflow.SetParticlesVisibleAsync(false);
            Assert.True(particlesOff.Succeeded, particlesOff.Outcome.Detail);
            var marker = await workflow.MarkXrBlockEndAsync("xr-block-1", "left-anchor-only");
            Assert.True(marker.Succeeded, marker.Outcome.Detail);
            var stopped = await workflow.StopRecordingAndCloseAppsAsync();
            Assert.True(stopped.Succeeded, stopped.Outcome.Detail);
            Assert.Equal(PeripersonalOperatorWorkflowState.Stopped, workflow.State);
            var pulledQuestBackupFolder = stopped.Outcome.SafeItems.FirstOrDefault();
            Assert.False(string.IsNullOrWhiteSpace(pulledQuestBackupFolder));
            Assert.True(Directory.Exists(pulledQuestBackupFolder), pulledQuestBackupFolder);
            foreach (var requiredFile in new[]
                     {
                         "session_settings.json",
                         "session_snapshot.json",
                         "session_schema.json",
                         "legacy_outputs_manifest.json",
                         "session_events.csv",
                         "runtime_state_samples.csv",
                         "timing_markers.csv"
                     })
            {
                var requiredPath = Path.Combine(pulledQuestBackupFolder, requiredFile);
                Assert.True(File.Exists(requiredPath), requiredPath);
                Assert.True(new FileInfo(requiredPath).Length > 0, requiredPath);
            }

            var unityPid = await RunAdbAsync(adbPath, selector, ["shell", "pidof", study.App.PackageId], allowNonZeroExit: true);
            var panelPid = await RunAdbAsync(
                adbPath,
                selector,
                ["shell", "pidof", "io.github.mesmerprism.questquestionnaire.panel"],
                allowNonZeroExit: true);
            Assert.True(string.IsNullOrWhiteSpace(unityPid.StdOut), unityPid.StdOut);
            Assert.True(string.IsNullOrWhiteSpace(panelPid.StdOut), panelPid.StdOut);
            Assert.Contains(workflow.CommandSnapshots, snapshot => snapshot.Action == "start_recording" && snapshot.Observed);
            Assert.Contains(workflow.CommandSnapshots, snapshot => snapshot.Action == "stop_recording_and_close_apps" && snapshot.Observed);
        }
        finally
        {
            try
            {
                await RunAdbAsync(adbPath, selector, ["shell", "am", "force-stop", "io.github.mesmerprism.questquestionnaire.panel"], allowNonZeroExit: true);
                await RunAdbAsync(adbPath, selector, ["shell", "am", "force-stop", "com.Viscereality.ViscerealityPeriPersonal"], allowNonZeroExit: true);
            }
            catch
            {
            }

            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertMaiaQuestionnaireCommand(
        PeripersonalCommandEnvelope command,
        string expectedOperatorBlockId,
        string expectedOpenStage,
        IReadOnlyList<string> expectedSequence,
        string expectedLanguage)
    {
        Assert.Equal(PeripersonalOperatorWorkflowService.PanelQuestionnaireId, command.Payload?["schema_id"]?.GetValue<string>());
        Assert.Equal(PeripersonalOperatorWorkflowService.PanelQuestionnaireId, command.Payload?["questionnaire_id"]?.GetValue<string>());
        Assert.Equal(expectedOpenStage, command.Payload?["open_stage"]?.GetValue<string>());
        Assert.Equal(expectedOperatorBlockId, command.Payload?["operator_questionnaire_block_id"]?.GetValue<string>());
        Assert.Equal(expectedLanguage, command.Payload?["language_code"]?.GetValue<string>());
        var sequence = command.Payload?["screen_sequence"]?.AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();
        Assert.NotNull(sequence);
        Assert.Equal(expectedSequence, sequence);
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
        FakePeripersonalCommandTransport transport,
        IPeripersonalQuestAppCloser? questAppCloser = null,
        IPeripersonalQuestBackupPuller? questBackupPuller = null)
        => new(
            CreateStudy(),
            transport,
            root,
            utcNow: () => new DateTimeOffset(2026, 06, 20, 12, 34, 56, TimeSpan.Zero),
            questAppCloser: questAppCloser,
            questBackupPuller: questBackupPuller);

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

    private static async Task<AdbProcessResult> RunAdbAsync(
        string adbPath,
        string selector,
        IReadOnlyList<string> arguments,
        bool allowNonZeroExit = false)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = adbPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-s");
        process.StartInfo.ArgumentList.Add(selector);
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var result = new AdbProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
        if (!allowNonZeroExit && result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.CombinedOutput);
        }

        return result;
    }

    private sealed record AdbProcessResult(int ExitCode, string StdOut, string StdErr)
    {
        public string CombinedOutput => string.Join(Environment.NewLine, new[] { StdOut, StdErr }.Where(static value => !string.IsNullOrWhiteSpace(value)));
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

    private sealed class FakeQuestAppCloser : IPeripersonalQuestAppCloser
    {
        public List<string> ClosedPackages { get; } = [];

        public Task<PeripersonalQuestAppCloseResult> CloseQuestAppsAsync(
            string unityPackage,
            string panelPackage,
            CancellationToken cancellationToken = default)
        {
            ClosedPackages.Add(unityPackage);
            ClosedPackages.Add(panelPackage);
            return Task.FromResult(new PeripersonalQuestAppCloseResult(true, "closed"));
        }
    }

    private sealed class FakeQuestBackupPuller : IPeripersonalQuestBackupPuller
    {
        public List<string> PulledSessionFolders { get; } = [];

        public Task<PeripersonalQuestBackupPullResult> PullSessionBackupAsync(
            PeripersonalQuestBackupPullRequest request,
            CancellationToken cancellationToken = default)
        {
            PulledSessionFolders.Add(request.SessionFolderName);
            var pullFolder = Path.Combine(request.WindowsSessionDirectory, request.WindowsPullSubfolder);
            Directory.CreateDirectory(pullFolder);
            File.WriteAllText(Path.Combine(pullFolder, "session_events.csv"), "participant_id,session_id");
            return Task.FromResult(new PeripersonalQuestBackupPullResult(
                new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Quest backup files pulled.",
                    $"Pulled fake backup into {pullFolder}.",
                    Items: [pullFolder]),
                pullFolder,
                ["session_events.csv"],
                [],
                []));
        }
    }

    private sealed class FakeClockAlignmentService : IStudyClockAlignmentService
    {
        public LslRuntimeState RuntimeState { get; } = new(true, "Fake clock alignment ready.");
        public StudyClockAlignmentRunRequest? LastRequest { get; private set; }

        public Task<OperationOutcome> StartWarmSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Success, "Warm session started.", string.Empty));

        public Task<OperationOutcome> StopWarmSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Success, "Warm session stopped.", string.Empty));

        public Task<StudyClockAlignmentRunResult> RunAsync(
            StudyClockAlignmentRunRequest request,
            IProgress<StudyClockAlignmentProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            progress?.Report(new StudyClockAlignmentProgress(100d, 1, 1, "Fake clock probe complete.", "Fake Quest echo captured."));
            var sample = new StudyClockAlignmentSample(
                request.WindowKind,
                1,
                DateTimeOffset.Parse("2026-06-20T12:34:57Z"),
                100d,
                DateTimeOffset.Parse("2026-06-20T12:34:57.018Z"),
                100.018d,
                100.018d,
                "2026-06-20T12:34:57.009Z",
                100.013d,
                100.014d,
                0.004d,
                0.018d);
            return Task.FromResult(new StudyClockAlignmentRunResult(
                new OperationOutcome(OperationOutcomeKind.Success, "Clock probe completed.", "Fake Quest echo captured."),
                new StudyClockAlignmentSummary(1, 1, 0.004d, 0.004d, 0.004d, 0.018d, 0.018d, 0.018d),
                [sample]));
        }

        public void Dispose()
        {
        }
    }
}
