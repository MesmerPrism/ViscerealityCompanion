using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class ValidationCaptureRegressionTests
{
    [Fact]
    public async Task StartUpstreamMonitor_DoesNotBlockCaller_WhenMonitorHasSynchronousPrefix()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"viscereality-validation-regression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var recorderService = new StudyDataRecorderService(tempRoot);
            using var session = recorderService.StartSession(new StudyDataRecordingStartRequest(
                "sussex-university",
                "Sussex University",
                "regression",
                "session-regression",
                "dataset",
                "dataset-hash",
                "settings-hash",
                "environment-hash",
                DateTimeOffset.UtcNow,
                "com.Viscereality.SussexExperiment",
                "apk-hash",
                "0.0.0",
                "component",
                "14",
                "build",
                "display",
                "profile",
                "Profile",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                "HRV_Biofeedback",
                "HRV",
                0.2d,
                "runtime-hash",
                "profile-id",
                "1",
                "channel",
                Environment.MachineName,
                "selector",
                "{\"SchemaVersion\":\"sussex-session-conditions-v1\"}"));

            var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
            SetPrivateField(viewModel, "_upstreamLslMonitorService", new BlockingStartupMonitorService());

            var startMethod = GetPrivateMethod("StartUpstreamLslMonitor");
            var stopMethod = GetPrivateMethod("StopUpstreamLslMonitorAsync");

            var stopwatch = Stopwatch.StartNew();
            startMethod.Invoke(viewModel, [session]);
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(250),
                $"Starting the passive upstream LSL monitor blocked the caller for {stopwatch.Elapsed.TotalMilliseconds:0} ms.");

            var stopTask = (Task<OperationOutcome>)stopMethod.Invoke(viewModel, [])!;
            var stopOutcome = await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(OperationOutcomeKind.Failure, stopOutcome.Kind);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void AutomaticBreathingReadback_UsesQuestTwinStateConfirmation()
    {
        var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
        SetPrivateField(
            viewModel,
            "_reportedTwinState",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["routing.breathing.mode"] = "6",
                ["routing.breathing.label"] = "Automatic Cycle",
                ["routing.automatic_breathing.running"] = "true",
                ["signal01.mock_pacer_breathing"] = "0.625"
            });
        SetPrivateField(viewModel, "_lastTwinStateTimestampLabel", "14:22:31");

        Assert.Equal("Automatic breathing driver confirmed.", viewModel.AutomaticBreathingSummary);
        Assert.Contains("quest_twin_state", viewModel.AutomaticBreathingDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Automatic Cycle", viewModel.AutomaticBreathingDetail, StringComparison.Ordinal);
        Assert.Contains("mode 6", viewModel.AutomaticBreathingDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("running", viewModel.AutomaticBreathingDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("14:22:31", viewModel.AutomaticBreathingDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticBreathingReadback_ShowsPendingRequestUntilHeadsetStateMatches()
    {
        var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
        SetPrivateField(
            viewModel,
            "_reportedTwinState",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["routing.breathing.mode"] = "6",
                ["routing.breathing.label"] = "Automatic Cycle",
                ["routing.automatic_breathing.running"] = "true"
            });
        SetPrivateField(viewModel, "_lastTwinStateTimestampLabel", "14:22:31");
        SetPrivateField(
            viewModel,
            "_lastAutomaticBreathingRequest",
            CreateAutomaticBreathingRequest("Pause Automatic", automaticModeSelected: true, automaticRunning: false));

        Assert.Contains("Waiting for headset confirmation", viewModel.AutomaticBreathingSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pause Automatic", viewModel.AutomaticBreathingDetail, StringComparison.Ordinal);
        Assert.Contains("Waiting for the requested breathing-driver state", viewModel.AutomaticBreathingDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoherenceCard_UsesQuestReportedLslLatestValue_WhenDirectLslSignalMirrorIsMissing()
    {
        using var viewModel = new StudyShellViewModel(CreateLslDirectStudyDefinition());
        SetPrivateField(
            viewModel,
            "_reportedTwinState",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["routing.coherence.label"] = "LSL Direct",
                ["routing.coherence.mode"] = "4",
                ["study.lsl.connected"] = "true",
                ["study.lsl.connected_name"] = "HRV_Biofeedback",
                ["study.lsl.connected_type"] = "HRV",
                ["study.lsl.latest_ch0_value"] = "0.712"
            });

        GetPrivateMethod("UpdateCoherenceCard").Invoke(viewModel, []);

        Assert.Equal("0.712", viewModel.CoherenceValueLabel);
        Assert.Equal(OperationOutcomeKind.Success, GetPropertyValue<OperationOutcomeKind>(viewModel, "CoherenceLevel"));
        Assert.Contains("Coherence live value: 0.712.", viewModel.CoherenceSummary, StringComparison.Ordinal);
        Assert.Contains("study.lsl.latest_ch0_value", viewModel.CoherenceDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void ControllerCalibrationQuality_UsesAcceptedStatusWhenCalibrationCompletedWithHealthyLowMotionMargin()
    {
        var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
        var method = GetPrivateMethod("BuildControllerCalibrationQualityStatus");

        var status = method.Invoke(
            viewModel,
            [
                false,
                true,
                1d,
                "controller_volume",
                150,
                120,
                30,
                2,
                28,
                0.80d,
                "movement too small"
            ]) ?? throw new InvalidOperationException("Calibration quality status was null.");

        Assert.Equal(OperationOutcomeKind.Success, GetPropertyValue<OperationOutcomeKind>(status, "Level"));
        Assert.True(GetPropertyValue<bool>(status, "Accepted"));
        Assert.Equal("Accepted", GetPropertyValue<string>(status, "Badge"));
        Assert.Contains("should work", GetPropertyValue<string>(status, "Summary"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("less robust response", GetPropertyValue<string>(status, "Expectation"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("low-motion", GetPropertyValue<string>(status, "Cause"), StringComparison.OrdinalIgnoreCase);

        var metrics = GetPropertyValue<string>(status, "Metrics");
        Assert.Contains("Observed 150", metrics, StringComparison.Ordinal);
        Assert.Contains("Rejected 30", metrics, StringComparison.Ordinal);
        Assert.Contains("Target 120", metrics, StringComparison.Ordinal);
        Assert.Contains("Acceptance 80%", metrics, StringComparison.Ordinal);
    }

    [Fact]
    public void ControllerCalibrationQuality_SaysNotAcceptedWhenLowMotionPreventsAcceptance()
    {
        var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
        var method = GetPrivateMethod("BuildControllerCalibrationQualityStatus");

        var status = method.Invoke(
            viewModel,
            [
                false,
                false,
                0d,
                "controller_volume",
                191,
                120,
                71,
                0,
                71,
                0.63d,
                "movement too small"
            ]) ?? throw new InvalidOperationException("Calibration quality status was null.");

        Assert.Equal(OperationOutcomeKind.Warning, GetPropertyValue<OperationOutcomeKind>(status, "Level"));
        Assert.False(GetPropertyValue<bool>(status, "Accepted"));
        Assert.Equal("Not accepted", GetPropertyValue<string>(status, "Badge"));
        Assert.Contains("not been accepted yet", GetPropertyValue<string>(status, "Summary"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not rely on controller breath tracking yet", GetPropertyValue<string>(status, "Expectation"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("controller motion stayed too small", GetPropertyValue<string>(status, "Cause"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControllerCard_SaysCalibrationNotAcceptedWhenTelemetryShowsLowMotionOnly()
    {
        var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
        SetPrivateField(viewModel, "_study", CreateMinimalStudyDefinition());
        SetPrivateField(
            viewModel,
            "_reportedTwinState",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tracker.breathing.controller.active"] = "true",
                ["tracker.breathing.controller.state"] = "running",
                ["tracker.breathing.controller.calibrated"] = "false",
                ["tracker.breathing.controller.validation_progress01"] = "0.0",
                ["tracker.breathing.controller.validation_axis_mode"] = "controller_volume",
                ["tracker.breathing.controller.validation_frames_observed"] = "191",
                ["tracker.breathing.controller.validation_frames_accepted"] = "120",
                ["tracker.breathing.controller.validation_frames_rejected"] = "71",
                ["tracker.breathing.controller.validation_frames_rejected_bad_tracking"] = "0",
                ["tracker.breathing.controller.validation_frames_rejected_low_motion"] = "71",
                ["tracker.breathing.controller.validation_acceptance01"] = "0.63",
                ["tracker.breathing.controller.failure_reason"] = "movement too small"
            });

        GetPrivateMethod("UpdateControllerCard").Invoke(viewModel, []);

        Assert.Equal("Calibration not accepted yet", viewModel.ControllerCalibrationLabel);
        Assert.Contains("not accepted yet", viewModel.ControllerSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not rely on breath tracking", viewModel.ControllerSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bench-only", viewModel.ControllerDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Not accepted", viewModel.ControllerCalibrationQualityBadge);
    }

    [Fact]
    public void ControllerCalibrationModeReadback_UsesHotloadFallbackKey()
    {
        var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
        SetPrivateField(
            viewModel,
            "_reportedTwinState",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hotload.study_controller_breathing_use_principal_axis_calibration"] = "false"
            });

        var method = GetPrivateMethod("TryGetCurrentControllerCalibrationModeSelection");
        var actual = (bool?)method.Invoke(viewModel, []);

        Assert.False(actual);
    }

    [Fact]
    public void ValidationCaptureActionSummary_ExplainsWhyTheInlineRunButtonIsDisabledWithoutSubjectId()
    {
        var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));

        Assert.Contains("Enter a temporary subject id first", viewModel.ValidationCaptureActionSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationCaptureBackgroundDriftDetail_UsesFiveSecondCadence()
    {
        var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
        var method = GetPrivateMethod("MarkValidationClockAlignmentBackgroundArmed");

        method.Invoke(viewModel, []);

        var detail = GetFieldValue<string>(viewModel, "_validationClockAlignmentBackgroundDetail");
        Assert.Contains("every 5 seconds", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sparse drift probe", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationCaptureBackgroundDriftStatus_PreservesCompletedProbeWhenWrapUpStopsLaterAttempt()
    {
        var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
        var method = GetPrivateMethod("UpdateValidationClockAlignmentStage");

        method.Invoke(
            viewModel,
            [StudyClockAlignmentWindowKind.BackgroundSparse, OperationOutcomeKind.Success, "Background drift completed.", "Quest echoed background probes."]);
        method.Invoke(
            viewModel,
            [StudyClockAlignmentWindowKind.BackgroundSparse, OperationOutcomeKind.Preview, "Background drift stopped for wrap-up.", "The current sparse probe was interrupted."]);

        Assert.Equal("Background drift completed.", GetFieldValue<string>(viewModel, "_validationClockAlignmentBackgroundSummary"));
        Assert.Equal("Quest echoed background probes.", GetFieldValue<string>(viewModel, "_validationClockAlignmentBackgroundDetail"));
    }

    [Fact]
    public void QuestBackupButtons_RequirePulledArtifactsInsteadOfAnEmptyFolder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"viscereality-empty-pull-{Guid.NewGuid():N}");
        var emptyPullFolder = Path.Combine(tempRoot, "device-session-pull");
        Directory.CreateDirectory(emptyPullFolder);

        try
        {
            var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
            SetPrivateField(viewModel, "_recordingDevicePullFolderPath", emptyPullFolder);
            SetPrivateField(viewModel, "_validationCaptureDevicePullFolderPath", emptyPullFolder);

            Assert.False(viewModel.CanOpenRecordingSessionDevicePullFolder);
            Assert.False(viewModel.CanOpenValidationCaptureDevicePullFolder);

            File.WriteAllText(Path.Combine(emptyPullFolder, "session_events.csv"), "header");

            Assert.True(viewModel.CanOpenRecordingSessionDevicePullFolder);
            Assert.True(viewModel.CanOpenValidationCaptureDevicePullFolder);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task StopParticipantRecording_CompletesLocalSession_WhenQuestPullbackIsCanceled()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"viscereality-stop-pullback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var recorderService = new StudyDataRecorderService(tempRoot);
            using var viewModel = new StudyShellViewModel(CreateMinimalStudyDefinition());
            using var session = recorderService.StartSession(CreateRecordingStartRequest("regression"));
            SetPrivateField(viewModel, "_hzdbService", new CancelingPullHzdbService());
            SetPrivateField(viewModel, "_appSessionState", new AppSessionState("test-device", "test-device"));
            SetPrivateField(viewModel, "_activeRecordingSession", session);
            SetPrivateField(viewModel, "_participantRunStopping", true);

            var method = GetPrivateMethod("FinishParticipantRecordingAsync");
            var task = (Task<OperationOutcome?>)method.Invoke(
                viewModel,
                [
                    session,
                    DateTimeOffset.UtcNow,
                    "recording.stopped",
                    "Participant recording closed by regression test.",
                    "warning",
                    true,
                    true,
                    "/sdcard/Android/data/com.Viscereality.SussexExperiment/files/session-regression",
                    false
                ])!;

            var pullbackOutcome = await task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(pullbackOutcome);
            Assert.Equal(OperationOutcomeKind.Warning, pullbackOutcome!.Kind);
            Assert.Contains("Windows data saved", pullbackOutcome.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("session_events.csv", pullbackOutcome.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Quest backup", pullbackOutcome.Detail, StringComparison.OrdinalIgnoreCase);

            var events = File.ReadAllText(session.EventsCsvPath);
            Assert.Contains("recording.device_pullback", events, StringComparison.Ordinal);
            Assert.Contains("recording.stopped", events, StringComparison.Ordinal);
            Assert.Contains("Windows data saved", events, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, GetFieldValue<string>(viewModel, "_recorderFaultDetail"));

            var pullFolder = Path.Combine(session.SessionFolderPath, "device-session-pull");
            Assert.False(Directory.Exists(pullFolder) && Directory.EnumerateFileSystemEntries(pullFolder).Any());
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void ReserveClockAlignmentProbeSequenceRange_UsesNonOverlappingSessionRanges()
    {
        var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
        var method = GetPrivateMethod("ReserveClockAlignmentProbeSequenceRange");

        var startBurstFirst = (int)(method.Invoke(viewModel, [TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250)]) ?? 0);
        var backgroundFirst = (int)(method.Invoke(viewModel, [TimeSpan.FromSeconds(2.5), TimeSpan.FromMilliseconds(250)]) ?? 0);
        var endBurstFirst = (int)(method.Invoke(viewModel, [TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250)]) ?? 0);

        Assert.Equal(1, startBurstFirst);
        Assert.Equal(41, backgroundFirst);
        Assert.Equal(51, endBurstFirst);
    }

    [Fact]
    public void RecenterCommandObservation_RecordsEffectOnlyOncePerRequest()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"viscereality-recenter-regression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var recorderService = new StudyDataRecorderService(tempRoot);
            using var session = recorderService.StartSession(new StudyDataRecordingStartRequest(
                "sussex-university",
                "Sussex University",
                "regression",
                "session-recenter",
                "dataset",
                "dataset-hash",
                "settings-hash",
                "environment-hash",
                DateTimeOffset.UtcNow,
                "com.Viscereality.SussexExperiment",
                "apk-hash",
                "0.0.0",
                "component",
                "14",
                "build",
                "display",
                "profile",
                "Profile",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                "HRV_Biofeedback",
                "HRV",
                0.2d,
                "runtime-hash",
                "profile-id",
                "1",
                "channel",
                Environment.MachineName,
                "selector",
                "{\"SchemaVersion\":\"sussex-session-conditions-v1\"}"));

            var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
            SetPrivateField(viewModel, "_study", CreateMinimalStudyDefinition());
            SetPrivateField(
                viewModel,
                "_reportedTwinState",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["study.recenter.last_anchor_recorded_at_utc"] = "2026-04-09T11:00:00.0000000Z",
                    ["study.recenter.distance_units"] = "0.100"
                });
            SetPrivateField(
                viewModel,
                "_lastRecenterCommandRequest",
                CreateStudyTwinCommandRequest(
                    "recenter",
                    "Recenter",
                    sentAtUtc: new DateTimeOffset(2026, 4, 9, 10, 59, 58, TimeSpan.Zero),
                    previousAnchorTimestampRaw: "2026-04-09T10:59:00.0000000Z",
                    previousDistance: 1.0d));

            var method = GetPrivateMethod("TryRecordRecenterCommandObservation");
            method.Invoke(viewModel, [session, new DateTimeOffset(2026, 4, 9, 11, 0, 1, TimeSpan.Zero)]);

            var twinState = GetFieldValue<IReadOnlyDictionary<string, string>>(viewModel, "_reportedTwinState");
            ((Dictionary<string, string>)twinState)["study.recenter.distance_units"] = "0.050";
            method.Invoke(viewModel, [session, new DateTimeOffset(2026, 4, 9, 11, 0, 2, TimeSpan.Zero)]);

            session.Complete(DateTimeOffset.UtcNow);

            var effectEvents = File.ReadAllLines(session.EventsCsvPath)
                .Count(line => line.Contains("command.recenter.effect_observed", StringComparison.Ordinal));
            Assert.Equal(1, effectEvents);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task MonitorUpstreamLslAsync_PersistsStatusRowsEvenWithoutStreamingSamples()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"viscereality-upstream-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var recorderService = new StudyDataRecorderService(tempRoot);
            using var session = recorderService.StartSession(new StudyDataRecordingStartRequest(
                "sussex-university",
                "Sussex University",
                "regression",
                "session-upstream",
                "dataset",
                "dataset-hash",
                "settings-hash",
                "environment-hash",
                DateTimeOffset.UtcNow,
                "com.Viscereality.SussexExperiment",
                "apk-hash",
                "0.0.0",
                "component",
                "14",
                "build",
                "display",
                "profile",
                "Profile",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                "HRV_Biofeedback",
                "HRV",
                0.2d,
                "runtime-hash",
                "profile-id",
                "1",
                "channel",
                Environment.MachineName,
                "selector",
                "{\"SchemaVersion\":\"sussex-session-conditions-v1\"}"));

            var monitor = new FakeMonitorService();
            monitor.EnqueueReading(new LslMonitorReading(
                "LSL stream not found.",
                "No visible stream matched HRV_Biofeedback / HRV.",
                null,
                0f,
                DateTimeOffset.UtcNow));

            var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
            SetPrivateField(viewModel, "_upstreamLslMonitorService", monitor);
            var method = GetPrivateMethod("MonitorUpstreamLslAsync");
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            var task = (Task)method.Invoke(viewModel, [session, cts.Token])!;
            await task.WaitAsync(TimeSpan.FromSeconds(5));

            session.Complete(DateTimeOffset.UtcNow);

            var lines = File.ReadAllLines(session.UpstreamLslMonitorCsvPath);
            Assert.Equal(2, lines.Length);
            Assert.Contains("LSL stream not found.", lines[1], StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task MonitorUpstreamLslAsync_PersistsResolvedSourceIdForDuplicateDiagnostics()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"viscereality-upstream-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var recorderService = new StudyDataRecorderService(tempRoot);
            using var session = recorderService.StartSession(new StudyDataRecordingStartRequest(
                "sussex-university",
                "Sussex University",
                "regression",
                "session-upstream-source",
                "dataset",
                "dataset-hash",
                "settings-hash",
                "environment-hash",
                DateTimeOffset.UtcNow,
                "com.Viscereality.SussexExperiment",
                "apk-hash",
                "0.0.0",
                "component",
                "14",
                "build",
                "display",
                "profile",
                "Profile",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                "HRV_Biofeedback",
                "HRV",
                0.2d,
                "runtime-hash",
                "profile-id",
                "1",
                "channel",
                Environment.MachineName,
                "selector",
                "{\"SchemaVersion\":\"sussex-session-conditions-v1\"}"));

            var monitor = new FakeMonitorService();
            monitor.EnqueueReading(new LslMonitorReading(
                "Streaming LSL sample.",
                "Numeric sample from `HRV_Biofeedback` / `HRV`.",
                0.55f,
                10f,
                DateTimeOffset.UtcNow,
                SampleTimestampSeconds: 100.25d,
                ObservedLocalClockSeconds: 100.30d,
                ResolvedSourceId: "external.sender.primary"));

            var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
            SetPrivateField(viewModel, "_upstreamLslMonitorService", monitor);
            var method = GetPrivateMethod("MonitorUpstreamLslAsync");
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            var task = (Task)method.Invoke(viewModel, [session, cts.Token])!;
            await task.WaitAsync(TimeSpan.FromSeconds(5));

            session.Complete(DateTimeOffset.UtcNow);

            var lines = File.ReadAllLines(session.UpstreamLslMonitorCsvPath);
            Assert.Equal(2, lines.Length);
            Assert.Contains("source_id", lines[0], StringComparison.Ordinal);
            Assert.Contains("external.sender.primary", lines[1], StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static MethodInfo GetPrivateMethod(string name)
        => typeof(StudyShellViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new InvalidOperationException($"Could not find {name} on {nameof(StudyShellViewModel)}.");

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"Could not find field {fieldName}.");
        field.SetValue(target, value);
    }

    private static T GetPropertyValue<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?? throw new InvalidOperationException($"Could not find property {propertyName}.");
        return (T)(property.GetValue(target) ?? throw new InvalidOperationException($"Property {propertyName} was null."));
    }

    private static T GetFieldValue<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"Could not find field {fieldName}.");
        return (T)(field.GetValue(target) ?? throw new InvalidOperationException($"Field {fieldName} was null."));
    }

    private static object CreateAutomaticBreathingRequest(string requestedLabel, bool automaticModeSelected, bool automaticRunning)
    {
        var type = typeof(StudyShellViewModel).Assembly.GetType("ViscerealityCompanion.App.ViewModels.AutomaticBreathingRequest")
                   ?? throw new InvalidOperationException("Could not resolve AutomaticBreathingRequest.");
        return Activator.CreateInstance(
                   type,
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null,
                   args: [automaticModeSelected, automaticRunning, requestedLabel, DateTimeOffset.UtcNow],
                   culture: null)
               ?? throw new InvalidOperationException("Could not create AutomaticBreathingRequest.");
    }

    private static object CreateStudyTwinCommandRequest(
        string actionId,
        string label,
        DateTimeOffset sentAtUtc,
        string previousAnchorTimestampRaw,
        double previousDistance)
    {
        var type = typeof(StudyShellViewModel).Assembly.GetType("ViscerealityCompanion.App.ViewModels.StudyTwinCommandRequest")
                   ?? throw new InvalidOperationException("Could not resolve StudyTwinCommandRequest.");
        return Activator.CreateInstance(
                   type,
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null,
                   args:
                   [
                       actionId,
                       label,
                       null,
                       7,
                       sentAtUtc,
                       null,
                       null,
                       previousAnchorTimestampRaw,
                       previousDistance,
                       null
                   ],
                   culture: null)
               ?? throw new InvalidOperationException("Could not create StudyTwinCommandRequest.");
    }

    private static StudyDataRecordingStartRequest CreateRecordingStartRequest(string participantId)
        => new(
            "sussex-university",
            "Sussex University",
            participantId,
            $"session-{Guid.NewGuid():N}",
            "dataset",
            "dataset-hash",
            "settings-hash",
            "environment-hash",
            DateTimeOffset.UtcNow,
            "com.Viscereality.SussexExperiment",
            "apk-hash",
            "0.0.0",
            "component",
            "14",
            "build",
            "display",
            "profile",
            "Profile",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            "HRV_Biofeedback",
            "HRV",
            0.2d,
            "runtime-hash",
            "profile-id",
            "1",
            "channel",
            Environment.MachineName,
            "test-device",
            "{\"SchemaVersion\":\"sussex-session-conditions-v1\"}");

    private static StudyShellDefinition CreateMinimalStudyDefinition()
        => new(
            "sussex-university",
            "Sussex University",
            "University of Sussex",
            "Regression test shell",
            new StudyPinnedApp(
                "Sussex Experiment APK",
                "com.Viscereality.SussexExperiment",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                true),
            new StudyPinnedDeviceProfile(
                "profile",
                "Profile",
                "Profile",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new StudyMonitoringProfile(
                string.Empty,
                string.Empty,
                string.Empty,
                "HRV_Biofeedback",
                "HRV",
                0.2d,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                ["study.recenter.distance_units"],
                Array.Empty<string>()),
            new StudyControlProfile(
                "recenter",
                "particles.on",
                "particles.off"));

    private static StudyShellDefinition CreateLslDirectStudyDefinition()
        => new(
            "sussex-university",
            "Sussex University",
            "University of Sussex",
            "Regression test shell",
            new StudyPinnedApp(
                "Sussex Experiment APK",
                "com.Viscereality.SussexExperiment",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                true),
            new StudyPinnedDeviceProfile(
                "profile",
                "Profile",
                "Profile",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new StudyMonitoringProfile(
                string.Empty,
                "LSL Heartbeat",
                "LSL Direct",
                "HRV_Biofeedback",
                "HRV",
                0.2d,
                ["study.lsl.connected"],
                ["study.lsl.filter_name"],
                ["study.lsl.filter_type"],
                ["signal01.coherence_lsl"],
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                ["signal01.coherence_lsl", "signal01.coherence_heartbeat", "signal01.mock_coherence"],
                ["routing.coherence.mode", "routing.coherence.label", "routing.coherence.uses_heartbeat_source"],
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>()),
            new StudyControlProfile(
                "recenter",
                "particles.on",
                "particles.off"));

    private sealed class BlockingStartupMonitorService : ILslMonitorService
    {
        public LslRuntimeState RuntimeState { get; } = new(true, "Blocking regression-test monitor.");

        public async IAsyncEnumerable<LslMonitorReading> MonitorAsync(
            LslMonitorSubscription subscription,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Thread.Sleep(750);

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }

            yield break;
        }
    }

    private sealed class CancelingPullHzdbService : IHzdbService
    {
        public bool IsAvailable => true;

        public Task<OperationOutcome> CaptureScreenshotAsync(string deviceSerial, string outputPath, string? method = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Failure, "Not implemented.", "Test fake."));

        public Task<OperationOutcome> CapturePerfTraceAsync(string deviceSerial, int durationMs = 5000, CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Failure, "Not implemented.", "Test fake."));

        public Task<OperationOutcome> SetProximityAsync(string deviceSerial, bool enabled, int? durationMs = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Failure, "Not implemented.", "Test fake."));

        public Task<QuestProximityStatus> GetProximityStatusAsync(string deviceSerial, CancellationToken cancellationToken = default)
            => Task.FromResult(new QuestProximityStatus(
                Available: false,
                HoldActive: false,
                VirtualState: string.Empty,
                IsAutosleepDisabled: false,
                HeadsetState: string.Empty,
                AutoSleepTimeMs: null,
                RetrievedAtUtc: DateTimeOffset.UtcNow,
                HoldUntilUtc: null,
                StatusDetail: "Test fake."));

        public Task<OperationOutcome> WakeDeviceAsync(string deviceSerial, CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Failure, "Not implemented.", "Test fake."));

        public Task<OperationOutcome> GetDeviceInfoAsync(string deviceSerial, CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Failure, "Not implemented.", "Test fake."));

        public Task<OperationOutcome> ListFilesAsync(string deviceSerial, string remotePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(
                OperationOutcomeKind.Success,
                "Listed remote session.",
                remotePath,
                Items: ["session_events.csv"]));

        public Task<OperationOutcome> PushFileAsync(string deviceSerial, string localPath, string remotePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Failure, "Not implemented.", "Test fake."));

        public Task<OperationOutcome> PullFileAsync(string deviceSerial, string remotePath, string localPath, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException("Simulated hzdb pull cancellation.");
    }
}
