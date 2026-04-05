using System.Text.Json;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class StudyDataRecorderServiceTests
{
    [Fact]
    public void GetParticipantStatus_FindsExistingParticipantSessions()
    {
        var root = CreateTempRoot();
        try
        {
            var participantRoot = Path.Combine(root, "sussex-university", "participant-P001");
            Directory.CreateDirectory(Path.Combine(participantRoot, "session-20260329T100000Z"));
            Directory.CreateDirectory(Path.Combine(participantRoot, "session-20260329T110000Z"));

            var service = new StudyDataRecorderService(root);
            var status = service.GetParticipantStatus("sussex-university", "P001");

            Assert.True(status.HasExistingSessions);
            Assert.Equal("P001", status.ParticipantId);
            Assert.Equal("participant-P001", status.ParticipantFolderName);
            Assert.Equal(
                ["session-20260329T100000Z", "session-20260329T110000Z"],
                status.ExistingSessionIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StartSession_WritesSettingsEventsSignalsAndBreathingFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new StudyDataRecorderService(root);
            var startedAtUtc = new DateTimeOffset(2026, 03, 29, 12, 34, 56, TimeSpan.Zero);
            using var session = service.StartSession(CreateRequest("P007", "session-20260329T123456Z", startedAtUtc));

            session.RecordEvent(
                "recording.started",
                "Recorder created.",
                null,
                "success",
                startedAtUtc);

            var twinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["study.pose.headset.local_pos_x"] = "0.11",
                ["study.pose.headset.local_pos_y"] = "1.22",
                ["study.pose.headset.local_pos_z"] = "-0.33",
                ["study.pose.headset.local_rot_x"] = "0.1",
                ["study.pose.headset.local_rot_y"] = "0.2",
                ["study.pose.headset.local_rot_z"] = "0.3",
                ["study.pose.headset.local_rot_w"] = "0.4",
                ["study.pose.controller.local_pos_x"] = "0.44",
                ["study.pose.controller.local_pos_y"] = "0.55",
                ["study.pose.controller.local_pos_z"] = "0.66",
                ["study.pose.controller.local_rot_x"] = "0.7",
                ["study.pose.controller.local_rot_y"] = "0.8",
                ["study.pose.controller.local_rot_z"] = "0.9",
                ["study.pose.controller.local_rot_w"] = "1.0",
                ["study.heartbeat.value01"] = "0.71",
                ["study.heartbeat.real_beat_value01"] = "1",
                ["study.coherence.value01"] = "0.63",
                ["study.breathing.value01"] = "0.58",
                ["study.radius.sphere.progress01"] = "0.34",
                ["study.radius.sphere.raw"] = "1.75",
                ["study.lsl.received_sample_count"] = "42",
                ["study.lsl.latest_timestamp"] = "1234.56",
                ["study.lsl.latest_default_value"] = "0.63",
                ["study.session.state_label"] = "Running",
                ["tracker.breathing.controller.calibrated"] = "true",
                ["study.session.run_index"] = "3"
            };

            session.RecordTwinState(twinState, startedAtUtc.AddSeconds(2), "192.168.1.8:5555");
            session.RecordTwinState(twinState, startedAtUtc.AddSeconds(3), "192.168.1.8:5555");
            session.RecordClockAlignmentSample(new StudyClockAlignmentSample(
                StudyClockAlignmentWindowKind.StartBurst,
                7,
                startedAtUtc.AddSeconds(1),
                100.25,
                startedAtUtc.AddSeconds(4),
                103.75,
                100.25,
                "2026-03-29T12:34:57.0000000Z",
                101.10,
                101.35,
                0.42,
                0.315));
            session.UpdateClockAlignmentSummary(StudyClockAlignmentWindowKind.StartBurst, new StudyClockAlignmentSummary(
                8,
                1,
                0.42,
                0.42,
                0.42,
                0.315,
                0.315,
                0.315));
            session.RecordUpstreamLslObservation(new StudyUpstreamLslObservation(
                startedAtUtc.AddSeconds(1.5),
                100.5,
                99.75,
                "HRV_Biofeedback",
                "HRV",
                0,
                LslChannelFormat.Float32,
                0.63f,
                null,
                12,
                "Streaming LSL sample.",
                "Numeric sample from `HRV_Biofeedback` / `HRV`."));

            using (var preCompleteSettingsDocument = JsonDocument.Parse(File.ReadAllText(session.SettingsJsonPath)))
            {
                Assert.Equal(0, preCompleteSettingsDocument.RootElement.GetProperty("UpstreamLslMonitorSampleCount").GetInt32());
            }

            session.Complete(startedAtUtc.AddMinutes(8));

            var eventLines = File.ReadAllLines(session.EventsCsvPath);
            Assert.Equal(2, eventLines.Length);
            Assert.Contains("recording.started", eventLines[1], StringComparison.Ordinal);
            Assert.Contains("sussex-university__P007__session-20260329T123456Z", eventLines[1], StringComparison.Ordinal);

            var signalLines = File.ReadAllLines(session.SignalsCsvPath);
            Assert.True(signalLines.Length > 5);
            Assert.Equal(signalLines.Distinct(StringComparer.Ordinal).Count(), signalLines.Length);
            Assert.Contains("headset.position.x", string.Join(Environment.NewLine, signalLines), StringComparison.Ordinal);
            Assert.Contains("breathing.value01", string.Join(Environment.NewLine, signalLines), StringComparison.Ordinal);
            Assert.DoesNotContain("pacer_radius.progress01", string.Join(Environment.NewLine, signalLines), StringComparison.Ordinal);

            var breathingLines = File.ReadAllLines(session.BreathingCsvPath);
            Assert.Equal(2, breathingLines.Length);
            Assert.Equal(breathingLines.Distinct(StringComparer.Ordinal).Count(), breathingLines.Length);
            Assert.Contains("controller_calibrated", breathingLines[0], StringComparison.Ordinal);
            Assert.DoesNotContain("controller_active", breathingLines[0], StringComparison.Ordinal);
            Assert.Contains("0.58", breathingLines[1], StringComparison.Ordinal);
            Assert.Contains("1.75", breathingLines[1], StringComparison.Ordinal);
            Assert.EndsWith(",true", breathingLines[1], StringComparison.OrdinalIgnoreCase);

            var clockAlignmentLines = File.ReadAllLines(session.ClockAlignmentCsvPath);
            Assert.Equal(2, clockAlignmentLines.Length);
            Assert.Contains("window_kind", clockAlignmentLines[0], StringComparison.Ordinal);
            Assert.Contains("probe_sequence", clockAlignmentLines[0], StringComparison.Ordinal);
            Assert.Contains("StartBurst", clockAlignmentLines[1], StringComparison.Ordinal);
            Assert.Contains(",7,", clockAlignmentLines[1], StringComparison.Ordinal);
            Assert.Contains("0.315", clockAlignmentLines[1], StringComparison.Ordinal);

            var upstreamLines = File.ReadAllLines(session.UpstreamLslMonitorCsvPath);
            Assert.Equal(2, upstreamLines.Length);
            Assert.Contains("stream_name", upstreamLines[0], StringComparison.Ordinal);
            Assert.Contains("HRV_Biofeedback", upstreamLines[1], StringComparison.Ordinal);
            Assert.Contains("0.63", upstreamLines[1], StringComparison.Ordinal);

            using var settingsDocument = JsonDocument.Parse(File.ReadAllText(session.SettingsJsonPath));
            var rootElement = settingsDocument.RootElement;
            Assert.Equal("P007", rootElement.GetProperty("ParticipantId").GetString());
            Assert.Equal("session-20260329T123456Z", rootElement.GetProperty("SessionId").GetString());
            Assert.Equal("sussex-university__P007__session-20260329T123456Z", rootElement.GetProperty("DatasetId").GetString());
            Assert.Equal("DATASET_HASH", rootElement.GetProperty("DatasetHash").GetString());
            Assert.Equal("SETTINGS_HASH", rootElement.GetProperty("SettingsHash").GetString());
            Assert.Equal("ENV_HASH", rootElement.GetProperty("EnvironmentHash").GetString());
            Assert.Equal("com.Viscereality.SussexExperiment", rootElement.GetProperty("PackageId").GetString());
            Assert.Equal("CONFIG_HASH", rootElement.GetProperty("RuntimeConfigJsonHash").GetString());
            Assert.Equal("viscereality_lsltwin_scene", rootElement.GetProperty("RuntimeHotloadProfileId").GetString());
            Assert.Equal("Quest-Selector-01", rootElement.GetProperty("QuestSelector").GetString());
            Assert.Equal(session.ClockAlignmentCsvPath, rootElement.GetProperty("ClockAlignmentFile").GetString());
            Assert.Equal(10, rootElement.GetProperty("ClockAlignmentDurationSeconds").GetInt32());
            Assert.Equal(250, rootElement.GetProperty("ClockAlignmentProbeIntervalMilliseconds").GetInt32());
            Assert.Equal(SussexClockAlignmentStreamContract.DefaultBackgroundProbeIntervalSeconds, rootElement.GetProperty("ClockAlignmentBackgroundProbeIntervalSeconds").GetInt32());
            Assert.Equal(0.42, rootElement.GetProperty("ClockAlignmentRecommendedQuestMinusWindowsClockSeconds").GetDouble(), 3);
            Assert.Equal(0.315, rootElement.GetProperty("ClockAlignmentMeanRoundTripSeconds").GetDouble(), 3);
            Assert.Equal(1, rootElement.GetProperty("ClockAlignmentSampleCount").GetInt32());
            Assert.Equal(0.42, rootElement.GetProperty("ClockAlignmentStartRecommendedQuestMinusWindowsClockSeconds").GetDouble(), 3);
            Assert.Equal(0.315, rootElement.GetProperty("ClockAlignmentStartMeanRoundTripSeconds").GetDouble(), 3);
            Assert.Equal(1, rootElement.GetProperty("ClockAlignmentStartSampleCount").GetInt32());
            Assert.Equal(0, rootElement.GetProperty("ClockAlignmentBackgroundSampleCount").GetInt32());
            Assert.Equal(session.UpstreamLslMonitorCsvPath, rootElement.GetProperty("UpstreamLslMonitorFile").GetString());
            Assert.Equal(1, rootElement.GetProperty("UpstreamLslMonitorSampleCount").GetInt32());
            Assert.Equal("2026-03-29T12:42:56+00:00", rootElement.GetProperty("SessionEndedAtUtc").GetString());

            using var sessionSnapshotDocument = JsonDocument.Parse(File.ReadAllText(session.SessionSnapshotJsonPath));
            var snapshotRoot = sessionSnapshotDocument.RootElement;
            Assert.Equal("sussex-session-snapshot-v1", snapshotRoot.GetProperty("SchemaVersion").GetString());
            Assert.Equal("P007", snapshotRoot.GetProperty("Settings").GetProperty("ParticipantId").GetString());
            Assert.Equal(
                "sussex-session-conditions-v1",
                snapshotRoot.GetProperty("Conditions").GetProperty("SchemaVersion").GetString());
            Assert.Equal(
                "participant-locked",
                snapshotRoot.GetProperty("Conditions").GetProperty("Runtime").GetProperty("Mode").GetString());
            Assert.Equal(
                "0.11",
                snapshotRoot.GetProperty("Conditions").GetProperty("ReportedTwinState").GetProperty("Values").GetProperty("study.pose.headset.local_pos_x").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RecordUpstreamLslObservation_PersistsSettingsCountInBatchesAndOnComplete()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new StudyDataRecorderService(root);
            var startedAtUtc = new DateTimeOffset(2026, 03, 29, 12, 34, 56, TimeSpan.Zero);
            using var session = service.StartSession(CreateRequest("P008", "session-20260329T223456Z", startedAtUtc));

            for (var index = 1; index <= 25; index++)
            {
                session.RecordUpstreamLslObservation(new StudyUpstreamLslObservation(
                    startedAtUtc.AddSeconds(index),
                    100d + index,
                    99d + index,
                    "HRV_Biofeedback",
                    "HRV",
                    0,
                    LslChannelFormat.Float32,
                    0.5f,
                    null,
                    index,
                    "Streaming LSL sample.",
                    "Regression batch persistence sample."));
            }

            using (var batchedSettingsDocument = JsonDocument.Parse(File.ReadAllText(session.SettingsJsonPath)))
            {
                Assert.Equal(25, batchedSettingsDocument.RootElement.GetProperty("UpstreamLslMonitorSampleCount").GetInt32());
            }

            session.RecordUpstreamLslObservation(new StudyUpstreamLslObservation(
                startedAtUtc.AddSeconds(26),
                126d,
                125d,
                "HRV_Biofeedback",
                "HRV",
                0,
                LslChannelFormat.Float32,
                0.5f,
                null,
                26,
                "Streaming LSL sample.",
                "Regression completion sample."));

            using (var preCompleteSettingsDocument = JsonDocument.Parse(File.ReadAllText(session.SettingsJsonPath)))
            {
                Assert.Equal(25, preCompleteSettingsDocument.RootElement.GetProperty("UpstreamLslMonitorSampleCount").GetInt32());
            }

            session.Complete(startedAtUtc.AddMinutes(1));

            using var completedSettingsDocument = JsonDocument.Parse(File.ReadAllText(session.SettingsJsonPath));
            Assert.Equal(26, completedSettingsDocument.RootElement.GetProperty("UpstreamLslMonitorSampleCount").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static StudyDataRecordingStartRequest CreateRequest(
        string participantId,
        string sessionId,
        DateTimeOffset startedAtUtc)
        => new(
            "sussex-university",
            "Sussex University",
            participantId,
            sessionId,
            "sussex-university__P007__session-20260329T123456Z",
            "DATASET_HASH",
            "SETTINGS_HASH",
            "ENV_HASH",
            startedAtUtc,
            "com.Viscereality.SussexExperiment",
            "ABC123",
            "0.2.0",
            "com.Viscereality.SussexExperiment/.MainActivity",
            "14",
            "2921110053000610",
            "UP1A.231005.007.A1",
            "sussex-study-profile",
            "Sussex Study Device Profile",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["debug.oculus.cpuLevel"] = "2",
                ["debug.oculus.gpuLevel"] = "5"
            },
            "HRV_Biofeedback",
            "HRV",
            0.2d,
            "CONFIG_HASH",
            "viscereality_lsltwin_scene",
            "2026.03.29",
            "public",
            "study-rig",
            "Quest-Selector-01",
            """
            {
              "SchemaVersion": "sussex-session-conditions-v1",
              "Runtime": {
                "Mode": "participant-locked"
              },
              "ReportedTwinState": {
                "Values": {
                  "study.pose.headset.local_pos_x": "0.11"
                }
              }
            }
            """);

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
