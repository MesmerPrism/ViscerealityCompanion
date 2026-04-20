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
            ["SessionEndedAtUtc"] = "2026-04-20T10:00:20Z"
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
            "2026-04-20T10:00:01Z,coherence.value01,0.42,unit01",
            "2026-04-20T10:00:02Z,coherence.value01,0.47,unit01",
            "2026-04-20T10:00:01Z,heartbeat.packet_value01,0.51,unit01",
            "2026-04-20T10:00:02Z,heartbeat.packet_value01,0.56,unit01",
            "2026-04-20T10:00:01Z,orbit.radius_visual01,0.61,unit01",
            "2026-04-20T10:00:02Z,orbit.radius_visual01,0.64,unit01");
        WriteLines(
            Path.Combine(devicePullFolder, "signals_long.csv"),
            "recorded_at_utc,signal_name,value_numeric,unit",
            "2026-04-20T10:00:01Z,coherence.value01,0.40,unit01",
            "2026-04-20T10:00:02Z,coherence.value01,0.45,unit01",
            "2026-04-20T10:00:01Z,heartbeat.packet_value01,0.49,unit01",
            "2026-04-20T10:00:02Z,heartbeat.packet_value01,0.54,unit01",
            "2026-04-20T10:00:01Z,orbit.radius_visual01,0.58,unit01",
            "2026-04-20T10:00:02Z,orbit.radius_visual01,0.62,unit01");

        WriteLines(
            Path.Combine(sessionFolder, "breathing_trace.csv"),
            "recorded_at_utc,breath_volume01",
            "2026-04-20T10:00:01Z,0.22",
            "2026-04-20T10:00:02Z,0.28");
        WriteLines(
            Path.Combine(devicePullFolder, "breathing_trace.csv"),
            "recorded_at_utc,breath_volume01",
            "2026-04-20T10:00:01Z,0.20",
            "2026-04-20T10:00:02Z,0.26");

        WriteLines(
            Path.Combine(sessionFolder, "clock_alignment_roundtrip.csv"),
            "probe_sequence,roundtrip_seconds,quest_minus_windows_clock_seconds",
            "1,0.012,0.0010",
            "2,0.011,0.0018",
            "3,0.013,0.0013");

        WriteLines(
            Path.Combine(sessionFolder, "upstream_lsl_monitor.csv"),
            "recorded_at_utc,value_numeric",
            "2026-04-20T10:00:01Z,0.55");
        WriteLines(
            Path.Combine(devicePullFolder, "clock_alignment_samples.csv"),
            "probe_sequence,quest_timestamp_utc",
            "1,2026-04-20T10:00:01Z");
        WriteLines(
            Path.Combine(devicePullFolder, "timing_markers.csv"),
            "recorded_at_utc,marker",
            "2026-04-20T10:00:01Z,start");
        WriteLines(
            Path.Combine(devicePullFolder, "lsl_samples.csv"),
            "recorded_at_utc,value_numeric",
            "2026-04-20T10:00:01Z,0.55");
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
            ["SessionEndedAtUtc"] = "2026-04-20T11:00:05Z"
        };

        var settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(sessionFolder, "session_settings.json"), settingsJson);
        File.WriteAllText(Path.Combine(devicePullFolder, "session_settings.json"), settingsJson);

        WriteLines(
            Path.Combine(sessionFolder, "session_events.csv"),
            "recorded_at_utc,event_name,event_detail",
            "2026-04-20T11:00:00Z,experiment.start_command,Participant started",
            "2026-04-20T11:00:05Z,experiment.end_command,Participant ended");
        WriteLines(
            Path.Combine(devicePullFolder, "session_events.csv"),
            "recorded_at_utc,event_name,event_detail",
            "2026-04-20T11:00:01Z,recording.device_confirmation,Quest recorder confirmed");

        WriteLines(
            Path.Combine(sessionFolder, "signals_long.csv"),
            "recorded_at_utc,signal_name,value_numeric,unit",
            "2026-04-20T11:00:01Z,coherence.value01,0.42,unit01",
            "2026-04-20T11:00:01Z,heartbeat.packet_value01,0.51,unit01",
            "2026-04-20T11:00:01Z,orbit.radius_visual01,0.61,unit01");
        WriteLines(
            Path.Combine(devicePullFolder, "signals_long.csv"),
            "recorded_at_utc,signal_name,value_numeric,unit",
            "2026-04-20T11:00:01Z,coherence.value01,0.40,unit01",
            "2026-04-20T11:00:01Z,heartbeat.packet_value01,0.49,unit01",
            "2026-04-20T11:00:01Z,orbit.radius_visual01,0.58,unit01");

        WriteLines(
            Path.Combine(sessionFolder, "breathing_trace.csv"),
            "recorded_at_utc,breath_volume01",
            "2026-04-20T11:00:01Z,0.22");
        WriteLines(
            Path.Combine(devicePullFolder, "breathing_trace.csv"),
            "recorded_at_utc,breath_volume01",
            "2026-04-20T11:00:01Z,0.20");

        WriteLines(
            Path.Combine(sessionFolder, "clock_alignment_roundtrip.csv"),
            "probe_sequence,roundtrip_seconds,quest_minus_windows_clock_seconds",
            "1,0.012,0.0010");

        WriteLines(
            Path.Combine(sessionFolder, "upstream_lsl_monitor.csv"),
            "recorded_at_utc,value_numeric",
            "2026-04-20T11:00:01Z,0.55");
        WriteLines(
            Path.Combine(devicePullFolder, "clock_alignment_samples.csv"),
            "probe_sequence,quest_timestamp_utc",
            "1,2026-04-20T11:00:01Z");
        WriteLines(
            Path.Combine(devicePullFolder, "timing_markers.csv"),
            "recorded_at_utc,marker",
            "2026-04-20T11:00:01Z,start");
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "vc-pdf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

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
