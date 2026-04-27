using System.Text;
using System.Text.Json;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class SussexPdfRendererTests
{
    [Fact]
    public void DiagnosticsRenderer_CreatesPdf()
    {
        var root = CreateTempRoot();
        try
        {
            var outputPath = Path.Combine(root, "sussex_lsl_twin_diagnostics.pdf");

            SussexDiagnosticsPdfRenderer.Render(CreateDiagnosticsReport(root), outputPath);

            AssertPdfCreated(outputPath);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void DiagnosticsLatex_IncludesExpectedStreamMissingLinksAndFocusNext()
    {
        var latex = SussexDiagnosticsReportService.RenderLatexReport(CreateDiagnosticsReport(@"C:\temp\diagnostics"));

        Assert.Contains("windows expected stream", latex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing links", latex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("focus next", latex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("companion TEST sender", latex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sussex runtime state and twin telemetry", latex, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationRenderer_CreatesPdf()
    {
        var root = CreateTempRoot();
        try
        {
            var sessionFolder = Path.Combine(root, "session-20260420T100000Z");
            Directory.CreateDirectory(sessionFolder);
            WriteValidationSessionFixture(sessionFolder);

            var outputPath = Path.Combine(sessionFolder, "validation_capture_preview.pdf");
            SussexValidationPdfRenderer.Render(sessionFolder, outputPath);

            AssertPdfCreated(outputPath);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ValidationRenderer_CreatesPdf_WhenSeriesAreSparse()
    {
        var root = CreateTempRoot();
        try
        {
            var sessionFolder = Path.Combine(root, "session-20260420T100000Z");
            Directory.CreateDirectory(sessionFolder);
            WriteSparseValidationSessionFixture(sessionFolder);

            var outputPath = Path.Combine(sessionFolder, "validation_capture_preview.pdf");
            SussexValidationPdfRenderer.Render(sessionFolder, outputPath);

            AssertPdfCreated(outputPath);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ValidationRenderer_LoadsPacketTimingMatches_ForLargeLslTimestamps()
    {
        var root = CreateTempRoot();
        try
        {
            var sessionFolder = Path.Combine(root, "session-20260421T151428Z");
            Directory.CreateDirectory(sessionFolder);
            WriteValidationSessionFixture(sessionFolder);
            WriteLines(
                Path.Combine(sessionFolder, "clock_alignment_roundtrip.csv"),
                "participant_id,session_id,dataset_id,window_kind,probe_sequence,probe_sent_at_utc,probe_sent_lsl_seconds,echo_received_at_utc,echo_received_lsl_seconds,echo_sample_lsl_seconds,quest_received_at_utc,quest_received_lsl_seconds,quest_echo_lsl_seconds,quest_minus_windows_clock_seconds,roundtrip_seconds",
                "participant-0001,session-20260421T151428Z,dataset,StartBurst,1,2026-04-21T15:14:28Z,502840.0000000,2026-04-21T15:14:28.0200000Z,502840.0200000,502840.0100000,2026-04-21T15:14:28.0050000Z,450.1716539,450.1716639,-502390.0300000,0.0200000",
                "participant-0001,session-20260421T151428Z,dataset,StartBurst,2,2026-04-21T15:14:29Z,502841.0000000,2026-04-21T15:14:29.0200000Z,502841.0200000,502841.0100000,2026-04-21T15:14:29.0050000Z,451.1716539,451.1716639,-502390.0300000,0.0200000");
            WriteLines(
                Path.Combine(sessionFolder, "upstream_lsl_monitor.csv"),
                "participant_id,session_id,dataset_id,recorded_at_utc,observed_local_clock_seconds,stream_sample_timestamp_seconds,stream_name,stream_type,channel_index,channel_format,value_numeric,value_text,sequence,status,detail,source_id",
                "participant-0001,session-20260421T151428Z,dataset,2026-04-21T15:14:40.6628024Z,502841.2028441,502841.2026836,HRV_Biofeedback,HRV,0,Float32,0.51406,0.51405966,3,Streaming LSL sample.,Numeric sample.,source-a",
                "participant-0001,session-20260421T151428Z,dataset,2026-04-21T15:14:40.9885594Z,502842.1707159,502842.1705584,HRV_Biofeedback,HRV,0,Float32,0.576215,0.5762153,4,Streaming LSL sample.,Numeric sample.,source-a");
            WriteLines(
                Path.Combine(sessionFolder, "device-session-pull", "timing_markers.csv"),
                "participant_id,session_id,dataset_id,recorded_at_utc,marker_name,marker_detail,sample_sequence,source_lsl_timestamp_seconds,quest_local_clock_seconds,value01,aux_value",
                "participant-0001,session-20260421T151428Z,dataset,2026-04-21T15:14:40.6650000Z,heartbeat_packet_receive,Quest heartbeat consumer received an upstream LSL packet.,3,502841.2026836,451.1730000,0.51406,",
                "participant-0001,session-20260421T151428Z,dataset,2026-04-21T15:14:40.6660000Z,coherence_value_publish,Quest coherence module published the latest coherence value into the signal registry.,3,502841.2026836,451.1740000,0.51406,",
                "participant-0001,session-20260421T151428Z,dataset,2026-04-21T15:14:40.8400000Z,orbit_radius_peak,Representative orbit-distance multiplier reached its near-maximum visual region.,3,502841.2026836,451.3470000,0.95000,",
                "participant-0001,session-20260421T151428Z,dataset,2026-04-21T15:14:41.9900000Z,heartbeat_packet_receive,Quest heartbeat consumer received an upstream LSL packet.,4,502842.1705584,452.1410000,0.576215,",
                "participant-0001,session-20260421T151428Z,dataset,2026-04-21T15:14:41.9910000Z,coherence_value_publish,Quest coherence module published the latest coherence value into the signal registry.,4,502842.1705584,452.1420000,0.576215,",
                "participant-0001,session-20260421T151428Z,dataset,2026-04-21T15:14:42.1600000Z,orbit_radius_peak,Representative orbit-distance multiplier reached its near-maximum visual region.,4,502842.1705584,452.3110000,0.97000,");

            var rendererType = typeof(SussexValidationPdfRenderer);
            var dataType = rendererType.GetNestedType("ValidationSessionData", System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(dataType);

            var loadMethod = dataType!.GetMethod("Load", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.NotNull(loadMethod);

            var validationData = loadMethod!.Invoke(null, [sessionFolder]);
            Assert.NotNull(validationData);

            var packetTiming = dataType.GetProperty("PacketTiming")!.GetValue(validationData);
            Assert.NotNull(packetTiming);

            var matches = packetTiming!.GetType().GetProperty("Matches")!.GetValue(packetTiming) as System.Collections.IEnumerable;
            Assert.NotNull(matches);
            Assert.Equal(2, matches!.Cast<object>().Count());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ValidationRenderer_PrefersWindowsTimingMarkers_WhenPresent()
    {
        var root = CreateTempRoot();
        try
        {
            var sessionFolder = Path.Combine(root, "session-20260421T170000Z");
            Directory.CreateDirectory(sessionFolder);
            WriteValidationSessionFixture(sessionFolder);
            WriteLines(
                Path.Combine(sessionFolder, "timing_markers.csv"),
                "participant_id,session_id,dataset_id,recorded_at_utc,marker_name,marker_detail,sample_sequence,source_lsl_timestamp_seconds,quest_local_clock_seconds,value01,aux_value,windows_received_at_utc",
                "participant-0001,session-20260420T100000Z,dataset,2026-04-20T10:00:01Z,heartbeat_packet_receive,Quest heartbeat consumer received an upstream LSL packet.,1,1001.000,1001.001,0.49,,2026-04-20T10:00:01.002Z",
                "participant-0001,session-20260420T100000Z,dataset,2026-04-20T10:00:01Z,coherence_value_publish,Quest coherence module published the latest coherence value into the signal registry.,1,1001.000,1001.003,0.40,,2026-04-20T10:00:01.004Z",
                "participant-0001,session-20260420T100000Z,dataset,2026-04-20T10:00:01Z,orbit_radius_peak,Representative orbit-distance multiplier reached its near-maximum visual region.,1,1001.000,1001.150,0.92,,2026-04-20T10:00:01.151Z");

            var rendererType = typeof(SussexValidationPdfRenderer);
            var dataType = rendererType.GetNestedType("ValidationSessionData", System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(dataType);

            var loadMethod = dataType!.GetMethod("Load", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.NotNull(loadMethod);

            var validationData = loadMethod!.Invoke(null, [sessionFolder]);
            Assert.NotNull(validationData);

            var packetTiming = dataType.GetProperty("PacketTiming")!.GetValue(validationData);
            Assert.NotNull(packetTiming);

            var matches = packetTiming!.GetType().GetProperty("Matches")!.GetValue(packetTiming) as System.Collections.IEnumerable;
            Assert.NotNull(matches);
            Assert.Single(matches!.Cast<object>());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ValidationRenderer_ParsesUnquotedJsonEventDetail_IntoTuningActivitySummary()
    {
        var root = CreateTempRoot();
        try
        {
            var sessionFolder = Path.Combine(root, "session-20260421T160000Z");
            Directory.CreateDirectory(sessionFolder);
            WriteValidationSessionFixture(sessionFolder);
            WriteLines(
                Path.Combine(sessionFolder, "session_events.csv"),
                "recorded_at_utc,event_name,event_detail",
                "2026-04-21T10:00:00Z,experiment.start_command,Participant started",
                "2026-04-21T10:00:02Z,recording.device_confirmation,Quest recorder confirmed",
                "2026-04-21T10:00:05Z,tuning.visual.applied,{\"ProfileName\":\"Operator Visual Profile\",\"Surface\":\"visual\",\"Kind\":\"applied\",\"Changes\":[{\"Label\":\"Particle Size Min\",\"CurrentLabel\":\"0.45\"},{\"Label\":\"Tracers Enabled\",\"CurrentLabel\":\"On\"}]}",
                "2026-04-21T10:00:18Z,experiment.end_command,Participant ended",
                "2026-04-21T10:00:19Z,recording.device_stop_confirmation,Quest recorder stopped");

            var rendererType = typeof(SussexValidationPdfRenderer);
            var dataType = rendererType.GetNestedType("ValidationSessionData", System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(dataType);

            var loadMethod = dataType!.GetMethod("Load", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.NotNull(loadMethod);

            var validationData = loadMethod!.Invoke(null, [sessionFolder]);
            Assert.NotNull(validationData);

            var windowsEvents = dataType.GetProperty("WindowsEvents")!.GetValue(validationData);
            Assert.NotNull(windowsEvents);

            var buildActivityRows = rendererType.GetMethod(
                "BuildSessionParameterActivityRows",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(buildActivityRows);

            var activityRows = buildActivityRows!.Invoke(null, [windowsEvents]);
            Assert.NotNull(activityRows);

            var activityRow = ((System.Collections.IEnumerable)activityRows!)
                .Cast<object>()
                .Single();
            var summary = activityRow.GetType().GetProperty("Summary")!.GetValue(activityRow)?.ToString();
            var changeCount = (int)(activityRow.GetType().GetProperty("ChangeCount")!.GetValue(activityRow) ?? -1);
            var profileName = activityRow.GetType().GetProperty("ProfileName")!.GetValue(activityRow)?.ToString();

            Assert.Equal(2, changeCount);
            Assert.Equal("Operator Visual Profile", profileName);
            Assert.Contains("Particle Size Min=0.45", summary, StringComparison.Ordinal);
            Assert.Contains("Tracers Enabled=On", summary, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void AssertPdfCreated(string outputPath)
    {
        Assert.True(File.Exists(outputPath));

        var bytes = File.ReadAllBytes(outputPath);
        Assert.True(bytes.Length > 1024, "Expected a non-trivial PDF file.");
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, Math.Min(5, bytes.Length)), StringComparison.Ordinal);
    }

    private static SussexDiagnosticsReport CreateDiagnosticsReport(string root)
    {
        var now = new DateTimeOffset(2026, 4, 20, 10, 30, 0, TimeSpan.Zero);
        return new SussexDiagnosticsReport(
            SchemaVersion: "test-schema",
            GeneratedAtUtc: now,
            OperatorDataRoot: root,
            ReportDirectory: root,
            StudyId: "sussex-university",
            StudyLabel: "Sussex University",
            PackageId: "com.Viscereality.SussexExperiment",
            ExpectedLslStreamName: "HRV_Biofeedback",
            ExpectedLslStreamType: "HRV",
            QuestSetup: new SussexQuestSetupSnapshot(
                Headset: new HeadsetAppStatus(
                    IsConnected: true,
                    ConnectionLabel: "192.168.0.10:5555",
                    DeviceModel: "Quest 3",
                    BatteryLevel: 88,
                    CpuLevel: 4,
                    GpuLevel: 4,
                    ForegroundPackageId: "com.Viscereality.SussexExperiment",
                    IsTargetInstalled: true,
                    IsTargetRunning: true,
                    IsTargetForeground: true,
                    RemoteOnlyControlEnabled: false,
                    Timestamp: now,
                    Summary: "Connected and foregrounded.",
                    Detail: "Quest runtime is active."),
                InstalledApp: new InstalledAppStatus(
                    PackageId: "com.Viscereality.SussexExperiment",
                    IsInstalled: true,
                    VersionName: "1.0.0",
                    VersionCode: "100",
                    InstalledSha256: "abcdef1234567890",
                    InstalledPath: "/data/app/com.Viscereality.SussexExperiment/base.apk",
                    Summary: "Pinned APK installed.",
                    Detail: "Installed package hash matches the pinned build."),
                DeviceProfile: new DeviceProfileStatus(
                    ProfileId: "sussex-university",
                    Label: "Sussex University",
                    IsActive: true,
                    Summary: "Profile active.",
                    Detail: "Pinned device profile matches expectations.",
                    Properties: Array.Empty<DevicePropertyStatus>()),
                Selector: "192.168.0.10:5555",
                ForegroundAndSnapshot: "Foreground confirmed from shell status.",
                PinnedBuild: "Pinned public Sussex build matches.",
                DeviceProfileSummary: "Pinned device profile active."),
            WindowsEnvironment: new WindowsEnvironmentAnalysisResult(
                Level: OperationOutcomeKind.Success,
                Summary: "Windows environment looks healthy.",
                Detail: "LSL runtime and expected sender are visible.",
                Checks:
                [
                    new WindowsEnvironmentCheckResult(
                        Id: "lsl.dll",
                        Label: "LSL runtime",
                        Level: OperationOutcomeKind.Success,
                        Summary: "Resolved",
                        Detail: "Official liblsl runtime resolved successfully."),
                    new WindowsEnvironmentCheckResult(
                        Id: "upstream-stream",
                        Label: "Expected upstream stream",
                        Level: OperationOutcomeKind.Success,
                        Summary: "Visible",
                        Detail: "HRV_Biofeedback / HRV was discovered on Windows.")
                ],
                CompletedAtUtc: now),
            MachineLslState: new SussexMachineLslStateResult(
                Level: OperationOutcomeKind.Success,
                Summary: "Machine LSL state looks healthy.",
                Detail: "No duplicate companion-owned outlets were found.",
                Checks:
                [
                    new SussexMachineLslCheckResult(
                        Label: "TEST sender",
                        Level: OperationOutcomeKind.Success,
                        Summary: "Available",
                        Detail: "Built-in sender can publish packets."),
                    new SussexMachineLslCheckResult(
                        Label: "Twin outlet inventory",
                        Level: OperationOutcomeKind.Success,
                        Summary: "Expected outlet visible",
                        Detail: "quest_twin_state publisher matches the pinned source id.")
                ],
                CompletedAtUtc: now),
            QuestWifiTransport: new QuestWifiTransportDiagnosticsResult(
                Level: OperationOutcomeKind.Success,
                Summary: "Quest Wi-Fi transport looks healthy.",
                Detail: "Selector, ping, and TCP checks agree.",
                Selector: "192.168.0.10:5555",
                HeadsetWifi: "LabWifi / 192.168.0.10",
                HostWifi: "LabWifi / 192.168.0.5",
                Topology: "Same SSID and same subnet.",
                Ping: "Ping reachable.",
                Tcp: "TCP 5555 reachable.",
                TcpReachable: true,
                PingReachable: true,
                SelectorMatchesHeadsetIp: true,
                SameSubnet: true,
                CheckedAtUtc: now),
            TwinStatePublisherInventory: new QuestTwinStatePublisherInventory(
                Level: OperationOutcomeKind.Success,
                Summary: "Expected quest_twin_state publisher visible.",
                Detail: "The pinned source id is visible on Windows.",
                AnyPublisherVisible: true,
                ExpectedPublisherVisible: true,
                ExpectedSourceId: "sussex-public-build",
                ExpectedSourceIdPrefix: "sussex-public",
                VisiblePublishers:
                [
                    new LslVisibleStreamInfo(
                        Name: "quest_twin_state",
                        Type: "quest.twin.state",
                        SourceId: "sussex-public-build",
                        ChannelCount: 1,
                        SampleRateHz: 0,
                        CreatedAtSeconds: 12.5)
                ]),
            TwinConnection: new SussexTwinConnectionProbeResult(
                Level: OperationOutcomeKind.Success,
                Summary: "Twin return path is healthy.",
                Detail: "Fresh quest_twin_state frames returned to Windows.",
                InletReady: true,
                ReturnPathReady: true,
                PinnedBuildReady: true,
                DeviceProfileReady: true,
                ExpectedInlet: "quest_twin_state / quest.twin.state",
                WindowsExpectedStreamVisible: true,
                WindowsExpectedStreamViaCompanionTestSender: true,
                WindowsExpectedStream: "HRV_Biofeedback / HRV is visible on Windows via the companion TEST sender.",
                MissingLinks: Array.Empty<string>(),
                FocusNext: "Windows already sees the expected upstream stream, so focus next on Sussex runtime state and twin telemetry.",
                RuntimeTarget: "com.Viscereality.SussexExperiment",
                ConnectedInlet: "quest_twin_state / quest.twin.state",
                Counts: "state=1, command=1, config=1",
                QuestStatus: "Foregrounded and publishing.",
                QuestEcho: "Recent state frame received.",
                ReturnPath: "Fresh frames visible in the bridge.",
                CommandChannel: "quest_twin_commands",
                HotloadChannel: "quest_hotload_config",
                TransportDetail: "Windows and Quest transport path looks healthy.",
                CheckedAtUtc: now),
            CommandAcceptance: new SussexCommandAcceptanceResult(
                Level: OperationOutcomeKind.Success,
                Summary: "Safe command acknowledgement succeeded.",
                Detail: "Quest echoed the test particle-off action.",
                Attempted: true,
                Sent: true,
                Accepted: true,
                ActionId: "particle.off.test",
                Sequence: 7,
                LastReportedActionId: "particle.off.test",
                LastReportedActionSequence: "7",
                LastReportedParticleSequence: "18",
                CheckedAtUtc: now),
            TwinTelemetry:
            [
                new SussexDiagnosticsKeyValue("coherence.value01", "0.62"),
                new SussexDiagnosticsKeyValue("heartbeat.packet_value01", "0.55")
            ],
            Artifacts:
            [
                new SussexDiagnosticsKeyValue("json", Path.Combine(root, "sussex_lsl_twin_diagnostics.json")),
                new SussexDiagnosticsKeyValue("tex", Path.Combine(root, "sussex_lsl_twin_diagnostics.tex")),
                new SussexDiagnosticsKeyValue("pdf", Path.Combine(root, "sussex_lsl_twin_diagnostics.pdf"))
            ],
            Level: OperationOutcomeKind.Success,
            Summary: "Diagnostics look healthy.",
            Detail: "All core transport and twin checks passed.");
    }

    private static void WriteValidationSessionFixture(string sessionFolder)
    {
        var devicePullFolder = Path.Combine(sessionFolder, "device-session-pull");
        Directory.CreateDirectory(devicePullFolder);

        var settings = new Dictionary<string, object?>
        {
            ["ParticipantId"] = "participant-0001",
            ["SessionId"] = "session-20260420T100000Z",
            ["PackageId"] = "com.Viscereality.SussexExperiment",
            ["AppVersionName"] = "1.0.0",
            ["ApkSha256"] = "abcdef1234567890abcdef1234567890",
            ["HeadsetBuildId"] = "BUILD-12345",
            ["HeadsetDisplayId"] = "DISPLAY-12345",
            ["QuestSelector"] = "192.168.0.10:5555",
            ["LslStreamName"] = "HRV_Biofeedback",
            ["LslStreamType"] = "HRV",
            ["WindowsMachineName"] = "WORKSTATION-1",
            ["SessionStartedAtUtc"] = "2026-04-20T10:00:00Z",
            ["SessionEndedAtUtc"] = "2026-04-20T10:00:20Z",
            ["InitialSessionParameterStateHash"] = "initial-parameter-state-hash",
            ["LatestSessionParameterStateHash"] = "latest-parameter-state-hash",
            ["SessionParameterStateUpdatedAtUtc"] = "2026-04-20T10:00:12Z",
            ["SessionParameterChangeCount"] = 2,
            ["InitialSessionParameterState"] = CreateSessionParameterStateFixture(0.50, 5, false),
            ["LatestSessionParameterState"] = CreateSessionParameterStateFixture(0.45, 7, true)
        };

        var settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(sessionFolder, "session_settings.json"), settingsJson);
        File.WriteAllText(Path.Combine(devicePullFolder, "session_settings.json"), settingsJson);

        WriteLines(
            Path.Combine(sessionFolder, "session_events.csv"),
            "recorded_at_utc,event_name,event_detail",
            "2026-04-20T10:00:00Z,experiment.start_command,Participant started",
            "2026-04-20T10:00:02Z,recording.device_confirmation,Quest recorder confirmed",
            "2026-04-20T10:00:04Z,clock_alignment.result,Median offset recorded",
            "2026-04-20T10:00:06Z,tuning.visual.applied,Applied tuned visual profile during the session",
            "2026-04-20T10:00:18Z,experiment.end_command,Participant ended",
            "2026-04-20T10:00:19Z,recording.device_stop_confirmation,Quest recorder stopped");
        WriteLines(
            Path.Combine(devicePullFolder, "session_events.csv"),
            "recorded_at_utc,event_name,event_detail",
            "2026-04-20T10:00:01Z,recording.device_confirmation,Quest recorder confirmed",
            "2026-04-20T10:00:18Z,recording.device_stop_confirmation,Quest recorder stopped");

        WriteLines(
            Path.Combine(sessionFolder, "signals_long.csv"),
            "recorded_at_utc,signal_name,value_numeric,unit",
            "2026-04-20T10:00:01Z,controller.position.x,0.10,meters",
            "2026-04-20T10:00:02Z,controller.position.x,0.12,meters",
            "2026-04-20T10:00:01Z,performance.fps,72,hz",
            "2026-04-20T10:00:02Z,performance.fps,71,hz",
            "2026-04-20T10:00:01Z,session.run_index,1,count",
            "2026-04-20T10:00:02Z,session.run_index,1,count",
            "2026-04-20T10:00:01Z,breathing.value01,0.22,unit01",
            "2026-04-20T10:00:02Z,breathing.value01,0.28,unit01",
            "2026-04-20T10:00:01Z,coherence.value01,0.42,unit01",
            "2026-04-20T10:00:02Z,coherence.value01,0.47,unit01",
            "2026-04-20T10:00:01Z,heartbeat.value01,0.61,unit01",
            "2026-04-20T10:00:02Z,heartbeat.value01,0.66,unit01",
            "2026-04-20T10:00:01Z,heartbeat.packet_value01,0.51,unit01",
            "2026-04-20T10:00:02Z,heartbeat.packet_value01,0.56,unit01",
            "2026-04-20T10:00:01Z,heartbeat.real_beat_value01,0.11,unit01",
            "2026-04-20T10:00:02Z,heartbeat.real_beat_value01,0.84,unit01",
            "2026-04-20T10:00:01Z,lsl.latest_timestamp_seconds,1234.10,seconds",
            "2026-04-20T10:00:02Z,lsl.latest_timestamp_seconds,1234.35,seconds",
            "2026-04-20T10:00:01Z,lsl.sample_count,41,count",
            "2026-04-20T10:00:02Z,lsl.sample_count,42,count",
            "2026-04-20T10:00:01Z,orbit.radius_envelope_weight01,0.44,unit01",
            "2026-04-20T10:00:02Z,orbit.radius_envelope_weight01,0.49,unit01",
            "2026-04-20T10:00:01Z,orbit.radius_peak_active,true,bool",
            "2026-04-20T10:00:02Z,orbit.radius_peak_active,false,bool",
            "2026-04-20T10:00:01Z,orbit.radius_phase01,0.24,unit01",
            "2026-04-20T10:00:02Z,orbit.radius_phase01,0.30,unit01",
            "2026-04-20T10:00:01Z,orbit.radius_visual01,0.61,unit01",
            "2026-04-20T10:00:02Z,orbit.radius_visual01,0.64,unit01",
            "2026-04-20T10:00:01Z,sphere_radius.progress01,0.33,unit01",
            "2026-04-20T10:00:02Z,sphere_radius.progress01,0.39,unit01",
            "2026-04-20T10:00:01Z,sphere_radius.raw,1.44,units",
            "2026-04-20T10:00:02Z,sphere_radius.raw,1.52,units");
        WriteLines(
            Path.Combine(devicePullFolder, "signals_long.csv"),
            "recorded_at_utc,signal_name,value_numeric,unit",
            "2026-04-20T10:00:01Z,controller.position.x,0.08,meters",
            "2026-04-20T10:00:02Z,controller.position.x,0.11,meters",
            "2026-04-20T10:00:01Z,performance.fps,72,hz",
            "2026-04-20T10:00:02Z,performance.fps,72,hz",
            "2026-04-20T10:00:01Z,session.run_index,1,count",
            "2026-04-20T10:00:02Z,session.run_index,1,count",
            "2026-04-20T10:00:01Z,breathing.value01,0.20,unit01",
            "2026-04-20T10:00:02Z,breathing.value01,0.26,unit01",
            "2026-04-20T10:00:01Z,coherence.value01,0.40,unit01",
            "2026-04-20T10:00:02Z,coherence.value01,0.45,unit01",
            "2026-04-20T10:00:01Z,heartbeat.value01,0.58,unit01",
            "2026-04-20T10:00:02Z,heartbeat.value01,0.63,unit01",
            "2026-04-20T10:00:01Z,heartbeat.packet_value01,0.49,unit01",
            "2026-04-20T10:00:02Z,heartbeat.packet_value01,0.54,unit01",
            "2026-04-20T10:00:01Z,heartbeat.real_beat_value01,0.10,unit01",
            "2026-04-20T10:00:02Z,heartbeat.real_beat_value01,0.79,unit01",
            "2026-04-20T10:00:01Z,lsl.latest_timestamp_seconds,1234.08,seconds",
            "2026-04-20T10:00:02Z,lsl.latest_timestamp_seconds,1234.30,seconds",
            "2026-04-20T10:00:01Z,lsl.sample_count,39,count",
            "2026-04-20T10:00:02Z,lsl.sample_count,40,count",
            "2026-04-20T10:00:01Z,orbit.radius_envelope_weight01,0.41,unit01",
            "2026-04-20T10:00:02Z,orbit.radius_envelope_weight01,0.46,unit01",
            "2026-04-20T10:00:01Z,orbit.radius_peak_active,true,bool",
            "2026-04-20T10:00:02Z,orbit.radius_peak_active,false,bool",
            "2026-04-20T10:00:01Z,orbit.radius_phase01,0.21,unit01",
            "2026-04-20T10:00:02Z,orbit.radius_phase01,0.28,unit01",
            "2026-04-20T10:00:01Z,orbit.radius_visual01,0.58,unit01",
            "2026-04-20T10:00:02Z,orbit.radius_visual01,0.62,unit01",
            "2026-04-20T10:00:01Z,sphere_radius.progress01,0.30,unit01",
            "2026-04-20T10:00:02Z,sphere_radius.progress01,0.35,unit01",
            "2026-04-20T10:00:01Z,sphere_radius.raw,1.40,units",
            "2026-04-20T10:00:02Z,sphere_radius.raw,1.48,units");

        WriteLines(
            Path.Combine(sessionFolder, "breathing_trace.csv"),
            "recorded_at_utc,breath_volume01,sphere_radius_progress01,sphere_radius_raw,controller_calibrated",
            "2026-04-20T10:00:01Z,0.22,0.33,1.44,true",
            "2026-04-20T10:00:02Z,0.28,0.39,1.52,true");
        WriteLines(
            Path.Combine(devicePullFolder, "breathing_trace.csv"),
            "recorded_at_utc,breath_volume01,sphere_radius_progress01,sphere_radius_raw,controller_calibrated",
            "2026-04-20T10:00:01Z,0.20,0.30,1.40,true",
            "2026-04-20T10:00:02Z,0.26,0.35,1.48,true");

        WriteLines(
            Path.Combine(sessionFolder, "clock_alignment_roundtrip.csv"),
            "probe_sequence,roundtrip_seconds,quest_minus_windows_clock_seconds",
            "1,0.012,0.0010",
            "2,0.011,0.0018",
            "3,0.013,0.0013");

        WriteLines(
            Path.Combine(sessionFolder, "upstream_lsl_monitor.csv"),
            "participant_id,session_id,dataset_id,recorded_at_utc,observed_local_clock_seconds,stream_sample_timestamp_seconds,stream_name,stream_type,channel_index,channel_format,value_numeric,value_text,sequence,status,detail,source_id",
            "participant-0001,session-20260420T100000Z,dataset,2026-04-20T10:00:01Z,1001.010,1001.000,HRV_Biofeedback,HRV,0,Float32,0.51,0.51,1,Streaming LSL sample.,Numeric sample.,source-a",
            "participant-0001,session-20260420T100000Z,dataset,2026-04-20T10:00:02Z,1002.013,1002.000,HRV_Biofeedback,HRV,0,Float32,0.56,0.56,2,Streaming LSL sample.,Numeric sample.,source-a");
        WriteLines(
            Path.Combine(devicePullFolder, "clock_alignment_samples.csv"),
            "probe_sequence,quest_timestamp_utc",
            "1,2026-04-20T10:00:01Z");
        WriteLines(
            Path.Combine(devicePullFolder, "timing_markers.csv"),
            "participant_id,session_id,dataset_id,recorded_at_utc,marker_name,marker_detail,sample_sequence,source_lsl_timestamp_seconds,quest_local_clock_seconds,value01,aux_value",
            "participant-0001,session-20260420T100000Z,dataset,2026-04-20T10:00:01Z,heartbeat_packet_receive,Quest heartbeat consumer received an upstream LSL packet.,1,1001.000,1001.001,0.49,",
            "participant-0001,session-20260420T100000Z,dataset,2026-04-20T10:00:01Z,coherence_value_publish,Quest coherence module published the latest coherence value into the signal registry.,1,1001.000,1001.003,0.40,",
            "participant-0001,session-20260420T100000Z,dataset,2026-04-20T10:00:01Z,orbit_radius_peak,Representative orbit-distance multiplier reached its near-maximum visual region.,1,1001.000,1001.150,0.92,",
            "participant-0001,session-20260420T100000Z,dataset,2026-04-20T10:00:02Z,heartbeat_packet_receive,Quest heartbeat consumer received an upstream LSL packet.,2,1002.000,1002.001,0.54,",
            "participant-0001,session-20260420T100000Z,dataset,2026-04-20T10:00:02Z,coherence_value_publish,Quest coherence module published the latest coherence value into the signal registry.,2,1002.000,1002.004,0.45,",
            "participant-0001,session-20260420T100000Z,dataset,2026-04-20T10:00:02Z,orbit_radius_peak,Representative orbit-distance multiplier reached its near-maximum visual region.,2,1002.000,1002.165,0.95,");
    }

    private static void WriteSparseValidationSessionFixture(string sessionFolder)
    {
        var devicePullFolder = Path.Combine(sessionFolder, "device-session-pull");
        Directory.CreateDirectory(devicePullFolder);

        var settings = new Dictionary<string, object?>
        {
            ["ParticipantId"] = "participant-0002",
            ["SessionId"] = "session-20260420T110000Z",
            ["PackageId"] = "com.Viscereality.SussexExperiment",
            ["AppVersionName"] = "0.1.2",
            ["ApkSha256"] = "abcdef1234567890abcdef1234567890",
            ["HeadsetBuildId"] = "BUILD-12345",
            ["HeadsetDisplayId"] = "DISPLAY-12345",
            ["QuestSelector"] = "192.168.0.10:5555",
            ["LslStreamName"] = "HRV_Biofeedback",
            ["LslStreamType"] = "HRV",
            ["WindowsMachineName"] = "WORKSTATION-1",
            ["SessionStartedAtUtc"] = "2026-04-20T11:00:00Z",
            ["SessionEndedAtUtc"] = "2026-04-20T11:00:05Z",
            ["InitialSessionParameterStateHash"] = "sparse-initial-parameter-state-hash",
            ["LatestSessionParameterStateHash"] = "sparse-latest-parameter-state-hash",
            ["SessionParameterStateUpdatedAtUtc"] = "2026-04-20T11:00:04Z",
            ["SessionParameterChangeCount"] = 1,
            ["InitialSessionParameterState"] = CreateSessionParameterStateFixture(0.50, 5, false),
            ["LatestSessionParameterState"] = CreateSessionParameterStateFixture(0.48, 5, false)
        };

        var settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(sessionFolder, "session_settings.json"), settingsJson);
        File.WriteAllText(Path.Combine(devicePullFolder, "session_settings.json"), settingsJson);

        WriteLines(
            Path.Combine(sessionFolder, "session_events.csv"),
            "recorded_at_utc,event_name,event_detail",
            "2026-04-20T11:00:00Z,experiment.start_command,Participant started",
            "2026-04-20T11:00:02Z,tuning.controller_breathing.applied,Applied controller-breathing profile during the session",
            "2026-04-20T11:00:05Z,experiment.end_command,Participant ended");
        WriteLines(
            Path.Combine(devicePullFolder, "session_events.csv"),
            "recorded_at_utc,event_name,event_detail",
            "2026-04-20T11:00:01Z,recording.device_confirmation,Quest recorder confirmed");

        WriteLines(
            Path.Combine(sessionFolder, "signals_long.csv"),
            "recorded_at_utc,signal_name,value_numeric,unit",
            "2026-04-20T11:00:01Z,performance.fps,72,hz",
            "2026-04-20T11:00:01Z,breathing.value01,0.22,unit01",
            "2026-04-20T11:00:01Z,coherence.value01,0.42,unit01",
            "2026-04-20T11:00:01Z,heartbeat.value01,0.61,unit01",
            "2026-04-20T11:00:01Z,heartbeat.packet_value01,0.51,unit01",
            "2026-04-20T11:00:01Z,heartbeat.real_beat_value01,0.11,unit01",
            "2026-04-20T11:00:01Z,lsl.latest_timestamp_seconds,1234.10,seconds",
            "2026-04-20T11:00:01Z,lsl.sample_count,41,count",
            "2026-04-20T11:00:01Z,orbit.radius_envelope_weight01,0.44,unit01",
            "2026-04-20T11:00:01Z,orbit.radius_peak_active,true,bool",
            "2026-04-20T11:00:01Z,orbit.radius_phase01,0.24,unit01",
            "2026-04-20T11:00:01Z,orbit.radius_visual01,0.61,unit01",
            "2026-04-20T11:00:01Z,sphere_radius.progress01,0.33,unit01",
            "2026-04-20T11:00:01Z,sphere_radius.raw,1.44,units");
        WriteLines(
            Path.Combine(devicePullFolder, "signals_long.csv"),
            "recorded_at_utc,signal_name,value_numeric,unit",
            "2026-04-20T11:00:01Z,performance.fps,72,hz",
            "2026-04-20T11:00:01Z,breathing.value01,0.20,unit01",
            "2026-04-20T11:00:01Z,coherence.value01,0.40,unit01",
            "2026-04-20T11:00:01Z,heartbeat.value01,0.58,unit01",
            "2026-04-20T11:00:01Z,heartbeat.packet_value01,0.49,unit01",
            "2026-04-20T11:00:01Z,heartbeat.real_beat_value01,0.10,unit01",
            "2026-04-20T11:00:01Z,lsl.latest_timestamp_seconds,1234.08,seconds",
            "2026-04-20T11:00:01Z,lsl.sample_count,39,count",
            "2026-04-20T11:00:01Z,orbit.radius_envelope_weight01,0.41,unit01",
            "2026-04-20T11:00:01Z,orbit.radius_peak_active,true,bool",
            "2026-04-20T11:00:01Z,orbit.radius_phase01,0.21,unit01",
            "2026-04-20T11:00:01Z,orbit.radius_visual01,0.58,unit01",
            "2026-04-20T11:00:01Z,sphere_radius.progress01,0.30,unit01",
            "2026-04-20T11:00:01Z,sphere_radius.raw,1.40,units");

        WriteLines(
            Path.Combine(sessionFolder, "breathing_trace.csv"),
            "recorded_at_utc,breath_volume01,sphere_radius_progress01,sphere_radius_raw,controller_calibrated",
            "2026-04-20T11:00:01Z,0.22,0.33,1.44,true");
        WriteLines(
            Path.Combine(devicePullFolder, "breathing_trace.csv"),
            "recorded_at_utc,breath_volume01,sphere_radius_progress01,sphere_radius_raw,controller_calibrated",
            "2026-04-20T11:00:01Z,0.20,0.30,1.40,true");

        WriteLines(
            Path.Combine(sessionFolder, "clock_alignment_roundtrip.csv"),
            "probe_sequence,roundtrip_seconds,quest_minus_windows_clock_seconds",
            "1,0.012,0.0010");

        WriteLines(
            Path.Combine(sessionFolder, "upstream_lsl_monitor.csv"),
            "participant_id,session_id,dataset_id,recorded_at_utc,observed_local_clock_seconds,stream_sample_timestamp_seconds,stream_name,stream_type,channel_index,channel_format,value_numeric,value_text,sequence,status,detail,source_id",
            "participant-0002,session-20260420T110000Z,dataset,2026-04-20T11:00:01Z,2001.010,2001.000,HRV_Biofeedback,HRV,0,Float32,0.49,0.49,1,Streaming LSL sample.,Numeric sample.,source-a");
        WriteLines(
            Path.Combine(devicePullFolder, "clock_alignment_samples.csv"),
            "probe_sequence,quest_timestamp_utc",
            "1,2026-04-20T11:00:01Z");
        WriteLines(
            Path.Combine(devicePullFolder, "timing_markers.csv"),
            "participant_id,session_id,dataset_id,recorded_at_utc,marker_name,marker_detail,sample_sequence,source_lsl_timestamp_seconds,quest_local_clock_seconds,value01,aux_value",
            "participant-0002,session-20260420T110000Z,dataset,2026-04-20T11:00:01Z,heartbeat_packet_receive,Quest heartbeat consumer received an upstream LSL packet.,1,2001.000,2001.001,0.49,",
            "participant-0002,session-20260420T110000Z,dataset,2026-04-20T11:00:01Z,coherence_value_publish,Quest coherence module published the latest coherence value into the signal registry.,1,2001.000,2001.003,0.40,",
            "participant-0002,session-20260420T110000Z,dataset,2026-04-20T11:00:01Z,orbit_radius_peak,Representative orbit-distance multiplier reached its near-maximum visual region.,1,2001.000,2001.120,0.88,");
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "vc-pdf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static object CreateSessionParameterStateFixture(
        double visualParticleSizeMin,
        int controllerMedianWindow,
        bool hasUnappliedEdits)
        => new
        {
            SchemaVersion = "sussex-session-parameter-state-v1",
            CapturedAtUtc = "2026-04-20T10:00:12Z",
            RuntimeHotloadProfile = new
            {
                Id = "viscereality_lsltwin_scene",
                Version = "2026.04.20",
                Channel = "public",
                RuntimeConfigHash = "runtime-config-hash"
            },
            VisualTuning = new
            {
                ApplySummary = "Visual profile applied.",
                ApplyDetail = "The visual profile was uploaded through the Sussex hotload path.",
                CurrentProfile = new
                {
                    Id = "visual-profile",
                    Document = new
                    {
                        Profile = new
                        {
                            Name = "Validation Visual Profile"
                        },
                        Controls = new object[]
                        {
                            new
                            {
                                Id = "particle_size_min",
                                Label = "Particle Size Min",
                                Value = visualParticleSizeMin,
                                BaselineValue = 0.5,
                                Type = "float",
                                Units = "units",
                                SafeMinimum = 0.1,
                                SafeMaximum = 1.0
                            },
                            new
                            {
                                Id = "tracers_enabled",
                                Label = "Tracers Enabled",
                                Value = 1.0,
                                BaselineValue = 0.0,
                                Type = "bool",
                                Units = "bool",
                                SafeMinimum = 0.0,
                                SafeMaximum = 1.0
                            }
                        }
                    }
                },
                EffectiveProfile = new
                {
                    Id = "visual-profile",
                    Document = new
                    {
                        Profile = new
                        {
                            Name = "Validation Visual Profile"
                        }
                    }
                },
                StartupProfile = new
                {
                    ProfileName = "Validation Visual Profile"
                },
                LastApplyRecord = new
                {
                    ProfileName = "Validation Visual Profile",
                    RequestedValues = new Dictionary<string, double>
                    {
                        ["particle_size_min"] = visualParticleSizeMin,
                        ["tracers_enabled"] = 1.0
                    }
                },
                ReportedValues = new Dictionary<string, double?>
                {
                    ["particle_size_min"] = visualParticleSizeMin,
                    ["tracers_enabled"] = 1.0
                },
                SelectedMatchesLastApplied = true,
                HasUnappliedEdits = hasUnappliedEdits
            },
            ControllerBreathingTuning = new
            {
                ApplySummary = "Controller-breathing profile applied.",
                ApplyDetail = "The controller-breathing profile was uploaded through the Sussex hotload path.",
                CurrentProfile = new
                {
                    Id = "controller-profile",
                    Document = new
                    {
                        Profile = new
                        {
                            Name = "Validation Controller Profile"
                        },
                        Controls = new object[]
                        {
                            new
                            {
                                Id = "median_window",
                                Group = "Smoothing",
                                Label = "Median Window",
                                Value = controllerMedianWindow,
                                BaselineValue = 5,
                                Type = "int",
                                Units = "samples",
                                SafeMinimum = 1,
                                SafeMaximum = 15
                            },
                            new
                            {
                                Id = "use_principal_axis_calibration",
                                Group = "Calibration",
                                Label = "Use Principal Axis Calibration",
                                Value = 1.0,
                                BaselineValue = 1.0,
                                Type = "bool",
                                Units = "bool",
                                SafeMinimum = 0.0,
                                SafeMaximum = 1.0
                            }
                        }
                    }
                },
                EffectiveProfile = new
                {
                    Id = "controller-profile",
                    Document = new
                    {
                        Profile = new
                        {
                            Name = "Validation Controller Profile"
                        }
                    }
                },
                StartupProfile = new
                {
                    ProfileName = "Validation Controller Profile"
                },
                LastApplyRecord = new
                {
                    ProfileName = "Validation Controller Profile",
                    RequestedValues = new Dictionary<string, double>
                    {
                        ["median_window"] = controllerMedianWindow,
                        ["use_principal_axis_calibration"] = 1.0
                    }
                },
                ReportedValues = new Dictionary<string, double?>
                {
                    ["median_window"] = controllerMedianWindow,
                    ["use_principal_axis_calibration"] = 1.0
                },
                SelectedMatchesLastApplied = true,
                HasUnappliedEdits = false
            }
        };

    private static void WriteLines(string path, params string[] lines)
        => File.WriteAllLines(path, lines);

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
