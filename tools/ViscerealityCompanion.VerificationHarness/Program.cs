using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            HarnessScenarioRunner.RunOnceFromCurrentDirectory();
        }
        catch (Exception ex)
        {
            HarnessScenarioRunner.WriteFatalErrorFromCurrentDirectory(ex);
            Environment.ExitCode = 1;
            throw;
        }
    }
}

public static class HarnessScenarioRunner
{
    private static readonly TimeSpan MockHeartbeatInterval = TimeSpan.FromMilliseconds(910);
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [STAThread]
    public static void RunOnceFromCurrentDirectory()
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var outputRoot = Path.Combine(repoRoot, "artifacts", "verify", "sussex-study-mode-live");
        Directory.CreateDirectory(outputRoot);
        DeleteIfPresent(Path.Combine(outputRoot, "sussex-study-mode-error.txt"));
        DeleteIfPresent(Path.Combine(outputRoot, "sussex-study-mode-report.txt"));
        DeleteIfPresent(Path.Combine(outputRoot, "sussex-main-window-initial.png"));
        DeleteIfPresent(Path.Combine(outputRoot, "sussex-main-window-live.png"));
        DeleteIfPresent(Path.Combine(outputRoot, "sussex-main-window-kiosk-proof.png"));
        DeleteIfPresent(Path.Combine(outputRoot, "sussex-main-window-home-proof.png"));
        DeleteIfPresent(Path.Combine(outputRoot, "quest-kiosk-proof.png"));
        DeleteIfPresent(Path.Combine(outputRoot, "quest-home-proof.png"));
        DeleteIfPresent(Path.Combine(outputRoot, "sussex-main-window-participant-ready-proof.png"));
        DeleteIfPresent(Path.Combine(outputRoot, "sussex-main-window-participant-running-proof.png"));
        DeleteIfPresent(Path.Combine(outputRoot, "sussex-main-window-participant-ended-proof.png"));
        DeleteIfPresent(Path.Combine(outputRoot, "quest-participant-ready-proof.png"));
        DeleteIfPresent(Path.Combine(outputRoot, "quest-participant-running-proof.png"));
        DeleteIfPresent(Path.Combine(outputRoot, "quest-participant-ended-proof.png"));
        DeleteDirectoryIfPresent(Path.Combine(outputRoot, "device-session-pull"));

        using var outlet = new FloatLslTestOutlet();
        outlet.Open(
            HrvBiofeedbackStreamContract.StreamName,
            HrvBiofeedbackStreamContract.StreamType,
            "viscereality.sussex.harness");

        var app = new ViscerealityCompanion.App.App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        window.Loaded += async (_, _) =>
        {
            try
            {
                window.Activate();
                window.Topmost = true;
                window.Topmost = false;
                await ExecuteScenarioAsync(window, repoRoot, outputRoot, outlet);
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(Path.Combine(outputRoot, "sussex-study-mode-error.txt"), ex.ToString());
                Environment.ExitCode = 1;
            }
            finally
            {
                window.Close();
                app.Shutdown();
            }
        };

        app.Run(window);
    }

    public static void WriteFatalErrorFromCurrentDirectory(Exception ex)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var outputRoot = Path.Combine(repoRoot, "artifacts", "verify", "sussex-study-mode-live");
        Directory.CreateDirectory(outputRoot);
        File.WriteAllText(Path.Combine(outputRoot, "sussex-study-mode-error.txt"), ex.ToString());
    }

    private static async Task ExecuteScenarioAsync(Window window, string repoRoot, string outputRoot, FloatLslTestOutlet outlet)
    {
        if (window.DataContext is not MainWindowViewModel mainViewModel)
        {
            throw new InvalidOperationException("Main window did not expose MainWindowViewModel as DataContext.");
        }

        await WaitForConditionAsync(
            () => mainViewModel.StudyShells.Count > 0,
            TimeSpan.FromSeconds(20),
            "Study shell catalog did not load.");

        var study = mainViewModel.StudyShells.FirstOrDefault(
                        item => string.Equals(item.Id, "sussex-university", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Sussex study shell was not found in the catalog.");

        await mainViewModel.ActivateStudyModeAsync(study);
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        CaptureWindow(window, Path.Combine(outputRoot, "sussex-main-window-initial.png"));

        var studyViewModel = mainViewModel.ActiveStudyShell
                             ?? throw new InvalidOperationException("Study mode did not create an active study shell.");

        await ExecuteCommandAsync(studyViewModel.ProbeUsbCommand, studyViewModel, "Probe USB");
        await ExecuteCommandAsync(studyViewModel.EnableWifiCommand, studyViewModel, "Enable Wi-Fi ADB");
        await ExecuteCommandAsync(studyViewModel.ConnectQuestCommand, studyViewModel, "Connect Quest");
        await ExecuteCommandAsync(studyViewModel.RefreshStatusCommand, studyViewModel, null);
        await EnsureHeadsetWakeReadyAsync(studyViewModel);
        await EnsureProximityHoldDisabledAsync(studyViewModel);

        await studyViewModel.InstallStudyAppAsync();
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await studyViewModel.ApplyPinnedDeviceProfileAsync();
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await studyViewModel.LaunchStudyAppAsync();

        await WaitForConditionAsync(
            () => studyViewModel.LiveRuntimeLevel is OperationOutcomeKind.Success or OperationOutcomeKind.Warning,
            TimeSpan.FromSeconds(30),
            "Quest twin state never became visible.");

        await WarmUpLslAsync(outlet, studyViewModel);
        await Task.Delay(TimeSpan.FromSeconds(4));
        var kioskScreenshotPath = await CaptureQuestScreenshotProofAsync(
            studyViewModel,
            window,
            outputRoot,
            "quest-kiosk-proof.png",
            "sussex-main-window-kiosk-proof.png");
        var latencyResults = await MeasureLslRoundTripAsync(outlet, studyViewModel);
        var senderRestartResult = await VerifyLslSenderRestartRecoveryAsync(outlet, studyViewModel);

        var recenterBaseline = CaptureTwinValues(
            studyViewModel,
            "study.recenter.last_command_sequence",
            "study.recenter.last_command_at_utc",
            "study.recenter.last_anchor_recorded_at_utc");
        await studyViewModel.RecenterAsync();
        var recenterResult = await WaitForTwinValueChangeAsync(
            studyViewModel,
            recenterBaseline,
            TimeSpan.FromSeconds(8),
            "Recenter confirmation");

        var particlesOffBaseline = CaptureTwinValues(
            studyViewModel,
            "study.particles.last_command_sequence",
            "study.particles.last_command_at_utc",
            "study.particles.requested_visible",
            "study.particles.visible",
            "study.particles.render_output_enabled");
        await studyViewModel.ParticlesOffAsync();
        var particlesOffResult = await WaitForTwinValueChangeAsync(
            studyViewModel,
            particlesOffBaseline,
            TimeSpan.FromSeconds(8),
            "Particles Off confirmation");
        var particlesHidden = await WaitForTwinBoolAsync(
            studyViewModel,
            "Particles hidden",
            false,
            TimeSpan.FromSeconds(6),
            "study.particles.visible",
            "study.particles.render_output_enabled");

        var particlesOnBaseline = CaptureTwinValues(
            studyViewModel,
            "study.particles.last_command_sequence",
            "study.particles.last_command_at_utc",
            "study.particles.requested_visible",
            "study.particles.visible",
            "study.particles.render_output_enabled");
        await studyViewModel.ParticlesOnAsync();
        var particlesOnResult = await WaitForTwinValueChangeAsync(
            studyViewModel,
            particlesOnBaseline,
            TimeSpan.FromSeconds(8),
            "Particles On confirmation");
        var particlesVisible = await WaitForTwinBoolAsync(
            studyViewModel,
            "Particles visible",
            true,
            TimeSpan.FromSeconds(6),
            "study.particles.visible",
            "study.particles.render_output_enabled");

        var participantRunResult = await RunParticipantRecordingPhaseAsync(
            mainViewModel,
            studyViewModel,
            window,
            outputRoot,
            outlet);

        var preExitRuntimeHealthy = studyViewModel.LiveRuntimeLevel is OperationOutcomeKind.Success or OperationOutcomeKind.Warning;
        var preExitLslHealthy = studyViewModel.LslLevel is OperationOutcomeKind.Success or OperationOutcomeKind.Warning;

        var stopVerification = await ValidateKioskExitAsync(
            studyViewModel,
            window,
            outputRoot,
            participantRunResult.EndedQuestScreenshotPath);
        var stopActionLevel = stopVerification.Level;
        var stopActionDetail = stopVerification.Detail;
        var homeScreenshotPath = stopVerification.HomeScreenshotPath;
        CaptureWindow(window, Path.Combine(outputRoot, "sussex-main-window-live.png"));
        var verificationPersistence = await PersistVerifiedEnvironmentBaselineAsync(
            repoRoot,
            studyViewModel,
            senderRestartResult,
            recenterResult,
            particlesOffResult,
            particlesHidden,
            particlesOnResult,
            particlesVisible,
            preExitRuntimeHealthy,
            preExitLslHealthy,
            stopActionLevel,
            stopActionDetail);

        await File.WriteAllTextAsync(
            Path.Combine(outputRoot, "sussex-study-mode-report.txt"),
            BuildReport(
                mainViewModel,
                studyViewModel,
                participantRunResult,
                latencyResults,
                senderRestartResult,
                recenterResult,
                particlesOffResult,
                particlesHidden,
                particlesOnResult,
                particlesVisible,
                stopActionLevel,
                stopActionDetail,
                kioskScreenshotPath,
                homeScreenshotPath,
                verificationPersistence));
    }

    private static async Task<StopAppVerificationResult> ValidateKioskExitAsync(
        StudyShellViewModel studyViewModel,
        Window window,
        string outputRoot,
        string fallbackScreenshotPath)
    {
        try
        {
            await studyViewModel.StopStudyAppAsync();
            await WaitForConditionAsync(
                () =>
                    !studyViewModel.HeadsetForegroundLabel.Contains(studyViewModel.PinnedPackageId, StringComparison.OrdinalIgnoreCase)
                    && !studyViewModel.QuestStatusSummary.Contains("is active", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(20),
                "The Sussex runtime stayed foregrounded after Exit Kiosk Runtime.");

            var homeScreenshotPath = await CaptureQuestScreenshotProofAsync(
                studyViewModel,
                window,
                outputRoot,
                "quest-home-proof.png",
                "sussex-main-window-home-proof.png");

            return new StopAppVerificationResult(
                OperationOutcomeKind.Success,
                $"Exit Kiosk Runtime returned the headset to a non-study foreground scene ({studyViewModel.HeadsetForegroundLabel}).",
                homeScreenshotPath);
        }
        catch (Exception exception)
        {
            return new StopAppVerificationResult(
                OperationOutcomeKind.Warning,
                $"Exit Kiosk Runtime could not be fully verified. {exception.Message}",
                fallbackScreenshotPath);
        }
    }

    private static async Task WarmUpLslAsync(FloatLslTestOutlet outlet, StudyShellViewModel studyViewModel)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            outlet.PushSample(0.25f);
            if (studyViewModel.LslLevel is OperationOutcomeKind.Success or OperationOutcomeKind.Warning)
            {
                return;
            }

            await Task.Delay(MockHeartbeatInterval);
        }
    }

    private static async Task<IReadOnlyList<LatencyResult>> MeasureLslRoundTripAsync(
        FloatLslTestOutlet outlet,
        StudyShellViewModel studyViewModel)
    {
        var results = new List<LatencyResult>();
        var stepValues = new[] { 0.14f, 0.63f, 0.29f, 0.82f, 0.47f };

        foreach (var step in stepValues)
        {
            await Task.Delay(MockHeartbeatInterval);
            var sentAt = DateTimeOffset.UtcNow;
            outlet.PushSample(step);

            var observation = await WaitForObservedValueAsync(studyViewModel, step, TimeSpan.FromSeconds(4));
            results.Add(new LatencyResult(
                step,
                sentAt,
                observation.Timestamp,
                observation.SourceKey,
                observation.Timestamp is null ? null : (observation.Timestamp.Value - sentAt).TotalMilliseconds));
        }

        return results;
    }

    private static async Task<ObservationResult> VerifyLslSenderRestartRecoveryAsync(
        FloatLslTestOutlet outlet,
        StudyShellViewModel studyViewModel)
    {
        const float expectedValue = 0.71f;
        outlet.Dispose();
        await Task.Delay(TimeSpan.FromSeconds(2));
        outlet.Open(
            HrvBiofeedbackStreamContract.StreamName,
            HrvBiofeedbackStreamContract.StreamType,
            "viscereality.sussex.harness.restart");

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            outlet.PushSample(expectedValue);
            if (studyViewModel.TryGetObservedLslValue(out var observedValue, out var sourceKey) &&
                Math.Abs(observedValue - expectedValue) <= 0.035f)
            {
                return new ObservationResult(
                    "Sender restart recovery",
                    true,
                    $"Quest resumed echoing {observedValue:0.000} via {sourceKey} after the Windows LSL sender was restarted without restarting the APK.");
            }

            await Task.Delay(MockHeartbeatInterval);
        }

        return new ObservationResult(
            "Sender restart recovery",
            false,
            "Quest did not resume echoing the restarted Windows LSL sender within the recovery window; this usually means the inlet stayed latched to a dead stream handle.");
    }

    private static async Task<ObservedValueResult> WaitForObservedValueAsync(
        StudyShellViewModel studyViewModel,
        float expectedValue,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (studyViewModel.TryGetObservedLslValue(out var observedValue, out var sourceKey) &&
                Math.Abs(observedValue - expectedValue) <= 0.035f)
            {
                return new ObservedValueResult(DateTimeOffset.UtcNow, sourceKey);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(120));
        }

        return new ObservedValueResult(null, string.Empty);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, string error)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150));
        }

        throw new TimeoutException(error);
    }

    private static async Task<string> CaptureQuestScreenshotProofAsync(
        StudyShellViewModel studyViewModel,
        Window window,
        string outputRoot,
        string questScreenshotFileName,
        string windowScreenshotFileName)
    {
        var previousPath = studyViewModel.QuestScreenshotPath;
        var outcome = await studyViewModel.CaptureQuestScreenshotForVerificationAsync(wakeBeforeCapture: false);
        if (outcome.Kind == OperationOutcomeKind.Failure)
        {
            throw new InvalidOperationException($"Quest screenshot proof capture failed: {outcome.Detail}");
        }

        await WaitForConditionAsync(
            () =>
            {
                var currentPath = studyViewModel.QuestScreenshotPath;
                return !string.IsNullOrWhiteSpace(currentPath)
                    && File.Exists(currentPath)
                    && !string.Equals(currentPath, previousPath, StringComparison.OrdinalIgnoreCase);
            },
            TimeSpan.FromSeconds(10),
            "Quest screenshot capture did not produce a new screenshot file.");

        var questScreenshotPath = studyViewModel.QuestScreenshotPath;
        var proofPath = Path.Combine(outputRoot, questScreenshotFileName);
        File.Copy(questScreenshotPath, proofPath, overwrite: true);
        CaptureWindow(window, Path.Combine(outputRoot, windowScreenshotFileName));
        return proofPath;
    }

    private static async Task<ParticipantRunResult> RunParticipantRecordingPhaseAsync(
        MainWindowViewModel mainViewModel,
        StudyShellViewModel studyViewModel,
        Window window,
        string outputRoot,
        FloatLslTestOutlet outlet)
    {
        var participantId = $"harness-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        await Application.Current.Dispatcher.InvokeAsync(() => studyViewModel.ParticipantIdDraft = participantId);
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        await EnsureStudyRuntimeReadyForParticipantAsync(studyViewModel);

        var readyQuestScreenshotPath = await CaptureQuestScreenshotProofAsync(
            studyViewModel,
            window,
            outputRoot,
            "quest-participant-ready-proof.png",
            "sussex-main-window-participant-ready-proof.png");

        var lslCountBefore = GetTwinLongValue(studyViewModel, "study.lsl.received_sample_count");

        await studyViewModel.StartExperimentAsync();
        await WaitForConditionAsync(
            () =>
            {
                var snapshot = studyViewModel.ReportedTwinStateSnapshot;
                snapshot.TryGetValue("study.session.id", out var sessionId);
                snapshot.TryGetValue("study.recording.device.active", out var deviceActiveRaw);
                return !string.IsNullOrWhiteSpace(studyViewModel.RecordingFolderPath)
                       && Directory.Exists(studyViewModel.RecordingFolderPath)
                       && !string.IsNullOrWhiteSpace(sessionId)
                       && ParseBool(deviceActiveRaw) == true;
            },
            TimeSpan.FromSeconds(15),
            "Participant run never reached the active local+Quest recording state.");

        var snapshotAfterStart = studyViewModel.ReportedTwinStateSnapshot;
        var sessionId = snapshotAfterStart.TryGetValue("study.session.id", out var startedSessionId)
            ? startedSessionId ?? string.Empty
            : string.Empty;
        var datasetId = snapshotAfterStart.TryGetValue("study.session.dataset_id", out var startedDatasetId)
            ? startedDatasetId ?? string.Empty
            : string.Empty;
        var datasetHash = snapshotAfterStart.TryGetValue("study.session.dataset_hash", out var startedDatasetHash)
            ? startedDatasetHash ?? string.Empty
            : string.Empty;
        var settingsHash = snapshotAfterStart.TryGetValue("study.session.settings_hash", out var startedSettingsHash)
            ? startedSettingsHash ?? string.Empty
            : string.Empty;
        var environmentHash = snapshotAfterStart.TryGetValue("study.session.environment_hash", out var startedEnvironmentHash)
            ? startedEnvironmentHash ?? string.Empty
            : string.Empty;
        var deviceSessionDirectory = snapshotAfterStart.TryGetValue("study.recording.device.session_dir", out var startedDeviceSessionDirectory)
            ? startedDeviceSessionDirectory ?? string.Empty
            : string.Empty;
        var localSessionFolderPath = studyViewModel.RecordingFolderPath;

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Participant run started, but the live twin state did not expose study.session.id.");
        }

        if (string.IsNullOrWhiteSpace(deviceSessionDirectory))
        {
            throw new InvalidOperationException("Participant run started, but the live twin state did not expose study.recording.device.session_dir.");
        }

        await WaitForSessionFilesAsync(
            localSessionFolderPath,
            ["session_events.csv", "signals_long.csv", "breathing_trace.csv", "clock_alignment_roundtrip.csv", "upstream_lsl_monitor.csv", "session_settings.json"],
            TimeSpan.FromSeconds(10));

        var runningQuestScreenshotPath = await CaptureQuestScreenshotProofAsync(
            studyViewModel,
            window,
            outputRoot,
            "quest-participant-running-proof.png",
            "sussex-main-window-participant-running-proof.png");

        var lslFlowResult = await DriveParticipantRunLslAsync(outlet, studyViewModel, lslCountBefore);

        var clockAlignmentPath = Path.Combine(localSessionFolderPath, "clock_alignment_roundtrip.csv");
        try
        {
            await WaitForConditionAsync(
                () => File.Exists(clockAlignmentPath)
                      && ContainsTextWithSharedRead(clockAlignmentPath, "BackgroundSparse"),
                TimeSpan.FromSeconds(12),
                "The participant run never captured a sparse background clock-alignment probe.");
        }
        catch (TimeoutException)
        {
            // The sparse probe path is informative for long runs, but it should not block
            // the main Sussex verification harness from completing a clean on-head pass.
        }

        await WaitForConditionAsync(
            () => HasMinimumCsvDataRows(Path.Combine(localSessionFolderPath, "signals_long.csv"), 8)
                  && HasMinimumCsvDataRows(Path.Combine(localSessionFolderPath, "session_events.csv"), 4),
            TimeSpan.FromSeconds(12),
            "The Windows participant recorder did not flush enough data rows for inspection.");

        await studyViewModel.EndExperimentAsync();

        await WaitForConditionAsync(
            () => File.Exists(Path.Combine(localSessionFolderPath, "session_settings.json"))
                  && File.Exists(Path.Combine(localSessionFolderPath, "session_events.csv"))
                  && ContainsTextWithSharedRead(Path.Combine(localSessionFolderPath, "session_events.csv"), "recording.stopped"),
            TimeSpan.FromSeconds(10),
            "The Windows participant recorder did not finish flushing the completed session.");

        var endedQuestScreenshotPath = await CaptureQuestScreenshotProofAsync(
            studyViewModel,
            window,
            outputRoot,
            "quest-participant-ended-proof.png",
            "sussex-main-window-participant-ended-proof.png");

        var localFiles = InspectSessionFiles(
            localSessionFolderPath,
            "session_settings.json",
            "session_events.csv",
            "signals_long.csv",
            "breathing_trace.csv",
            "clock_alignment_roundtrip.csv",
            "upstream_lsl_monitor.csv");
        var localMetadata = ReadSessionMetadata(Path.Combine(localSessionFolderPath, "session_settings.json"));

        var deviceSelector = ResolveHzdbSelector(mainViewModel, studyViewModel);
        if (string.IsNullOrWhiteSpace(deviceSelector))
        {
            throw new InvalidOperationException("Could not resolve a headset selector for hzdb device file pull.");
        }

        var devicePullDirectory = Path.Combine(outputRoot, "device-session-pull");
        var deviceFiles = await PullAndInspectDeviceSessionFilesAsync(
            deviceSelector,
            deviceSessionDirectory,
            devicePullDirectory,
            "session_settings.json",
            "session_events.csv",
            "signals_long.csv",
            "breathing_trace.csv",
            "clock_alignment_samples.csv",
            "timing_markers.csv",
            "lsl_samples.csv");
        var deviceMetadata = ReadSessionMetadata(Path.Combine(devicePullDirectory, "session_settings.json"));

        return new ParticipantRunResult(
            participantId,
            sessionId,
            datasetId,
            datasetHash,
            settingsHash,
            environmentHash,
            localSessionFolderPath,
            deviceSelector,
            deviceSessionDirectory,
            readyQuestScreenshotPath,
            runningQuestScreenshotPath,
            endedQuestScreenshotPath,
            lslFlowResult,
            localMetadata,
            deviceMetadata,
            localFiles,
            deviceFiles);
    }

    private static async Task<ObservationResult> DriveParticipantRunLslAsync(
        FloatLslTestOutlet outlet,
        StudyShellViewModel studyViewModel,
        long? initialCount)
    {
        var runValues = new[] { 0.19f, 0.48f, 0.77f, 0.31f, 0.66f, 0.28f };
        foreach (var value in runValues)
        {
            outlet.PushSample(value);
            await Task.Delay(MockHeartbeatInterval);
        }

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var currentCount = GetTwinLongValue(studyViewModel, "study.lsl.received_sample_count");
            if (currentCount.HasValue && (!initialCount.HasValue || currentCount.Value > initialCount.Value))
            {
                return new ObservationResult(
                    "Participant-run LSL capture",
                    true,
                    initialCount.HasValue
                        ? $"Quest LSL sample counter advanced from {initialCount.Value} to {currentCount.Value} during the participant recording window."
                        : $"Quest LSL sample counter reported {currentCount.Value} during the participant recording window.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150));
        }

        return new ObservationResult(
            "Participant-run LSL capture",
            false,
            "Quest LSL sample counter did not advance during the participant recording window.");
    }

    private static async Task EnsureStudyRuntimeReadyForParticipantAsync(StudyShellViewModel studyViewModel)
    {
        await ExecuteCommandAsync(studyViewModel.RefreshStatusCommand, studyViewModel, null, TimeSpan.FromSeconds(15));

        var runtimeVisible = studyViewModel.LiveRuntimeLevel is OperationOutcomeKind.Success or OperationOutcomeKind.Warning;
        var appActive = !studyViewModel.InstalledApkSummary.Contains("not active", StringComparison.OrdinalIgnoreCase);
        if (!runtimeVisible || !appActive)
        {
            await EnsureHeadsetWakeReadyAsync(studyViewModel);
            await studyViewModel.LaunchStudyAppAsync();
            await WaitForConditionAsync(
                () => studyViewModel.LiveRuntimeLevel is OperationOutcomeKind.Success or OperationOutcomeKind.Warning,
                TimeSpan.FromSeconds(30),
                "Quest twin state did not recover before the participant run.");
        }

        await studyViewModel.ParticlesOnAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await studyViewModel.RecenterAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(500));
    }

    private static async Task WaitForSessionFilesAsync(string sessionFolderPath, IReadOnlyList<string> fileNames, TimeSpan timeout)
    {
        await WaitForConditionAsync(
            () => Directory.Exists(sessionFolderPath)
                  && fileNames.All(fileName => File.Exists(Path.Combine(sessionFolderPath, fileName))),
            timeout,
            $"The participant session folder `{sessionFolderPath}` never contained the expected files.");
    }

    private static bool HasMinimumCsvDataRows(string path, int minimumDataRows)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lineCount = 0;
            while (lineCount < minimumDataRows + 1 && reader.ReadLine() is not null)
            {
                lineCount++;
            }

            return lineCount >= minimumDataRows + 1;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static IReadOnlyList<FileInspectionResult> InspectSessionFiles(string sessionFolderPath, params string[] fileNames)
        => fileNames
            .Select(fileName => InspectFile(Path.Combine(sessionFolderPath, fileName)))
            .ToArray();

    private static async Task<IReadOnlyList<FileInspectionResult>> PullAndInspectDeviceSessionFilesAsync(
        string deviceSelector,
        string deviceSessionDirectory,
        string outputDirectory,
        params string[] fileNames)
    {
        var hzdb = HzdbServiceFactory.CreateDefault();
        if (!hzdb.IsAvailable)
        {
            throw new InvalidOperationException("hzdb was not available for Quest session file pull.");
        }

        Directory.CreateDirectory(outputDirectory);

        var listingOutcome = await hzdb.ListFilesAsync(deviceSelector, deviceSessionDirectory);
        if (listingOutcome.Kind == OperationOutcomeKind.Failure)
        {
            throw new InvalidOperationException($"Could not list Quest session directory {deviceSessionDirectory}: {listingOutcome.Detail}");
        }

        var inspections = new List<FileInspectionResult>(fileNames.Length + 1)
        {
            new(deviceSessionDirectory, true, 0, listingOutcome.Detail)
        };

        foreach (var fileName in fileNames)
        {
            var remotePath = $"{deviceSessionDirectory.TrimEnd('/', '\\')}/{fileName}";
            var localPath = Path.Combine(outputDirectory, fileName);
            var pullOutcome = await hzdb.PullFileAsync(deviceSelector, remotePath, localPath);
            if (pullOutcome.Kind == OperationOutcomeKind.Failure)
            {
                throw new InvalidOperationException($"Could not pull Quest session file {remotePath}: {pullOutcome.Detail}");
            }

            inspections.Add(InspectFile(localPath));
        }

        return inspections;
    }

    private static FileInspectionResult InspectFile(string path)
    {
        if (!File.Exists(path))
        {
            return new FileInspectionResult(path, false, 0, "missing");
        }

        var fileInfo = new FileInfo(path);
        string preview;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            var lines = new List<string>(5);
            for (var index = 0; index < 5 && !reader.EndOfStream; index++)
            {
                var line = reader.ReadLine() ?? string.Empty;
                lines.Add(line.Length > 220 ? $"{line[..220]}..." : line);
            }

            preview = string.Join(Environment.NewLine, lines);
        }

        return new FileInspectionResult(path, true, fileInfo.Length, preview);
    }

    private static SessionMetadataResult ReadSessionMetadata(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return new SessionMetadataResult(settingsPath, false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "missing");
        }

        JsonObject? root;
        using (var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            root = JsonNode.Parse(reader.ReadToEnd()) as JsonObject;
        }

        if (root is null)
        {
            return new SessionMetadataResult(settingsPath, false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "invalid json");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["StudyId"] = ReadJsonString(root, "StudyId", "studyId"),
            ["StudyLabel"] = ReadJsonString(root, "StudyLabel", "studyLabel"),
            ["ParticipantId"] = ReadJsonString(root, "ParticipantId", "participantId"),
            ["SessionId"] = ReadJsonString(root, "SessionId", "sessionId"),
            ["DatasetId"] = ReadJsonString(root, "DatasetId", "datasetId"),
            ["DatasetHash"] = ReadJsonString(root, "DatasetHash", "datasetHash"),
            ["SettingsHash"] = ReadJsonString(root, "SettingsHash", "settingsHash"),
            ["EnvironmentHash"] = ReadJsonString(root, "EnvironmentHash", "environmentHash"),
            ["SessionStartedAtUtc"] = ReadJsonString(root, "SessionStartedAtUtc", "sessionStartedAtUtc"),
            ["PackageId"] = ReadJsonString(root, "PackageId", "packageId"),
            ["ApkSha256"] = ReadJsonString(root, "ApkSha256", "apkSha256"),
            ["WindowsMachineName"] = ReadJsonString(root, "WindowsMachineName", "windowsMachineName"),
            ["DeviceProfileId"] = ReadJsonString(root, "DeviceProfileId", "deviceProfileId"),
            ["SessionFolderPath"] = ReadJsonString(root, "SessionFolderPath", "sessionFolderPath")
        };

        var summary = string.Join(
            ", ",
            new[]
            {
                $"participant={values["ParticipantId"]}",
                $"session={values["SessionId"]}",
                $"datasetId={values["DatasetId"]}",
                $"datasetHash={values["DatasetHash"]}",
                $"settingsHash={values["SettingsHash"]}"
            });

        return new SessionMetadataResult(settingsPath, true, values, summary);
    }

    private static string ReadJsonString(JsonObject root, params string[] candidateKeys)
    {
        foreach (var key in candidateKeys)
        {
            if (root.TryGetPropertyValue(key, out var node) && node is not null)
            {
                return node.GetValue<string?>() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string ResolveHzdbSelector(MainWindowViewModel mainViewModel, StudyShellViewModel studyViewModel)
    {
        var method = typeof(StudyShellViewModel).GetMethod("ResolveHzdbSelector", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method?.Invoke(studyViewModel, null) is string selector && !string.IsNullOrWhiteSpace(selector))
        {
            return selector;
        }

        return string.IsNullOrWhiteSpace(mainViewModel.EndpointDraft)
            ? string.Empty
            : mainViewModel.EndpointDraft.Trim();
    }

    private static long? GetTwinLongValue(StudyShellViewModel studyViewModel, string key)
    {
        if (!studyViewModel.ReportedTwinStateSnapshot.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool ContainsTextWithSharedRead(string path, string needle)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Contains(needle, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string BuildReport(
        MainWindowViewModel mainViewModel,
        StudyShellViewModel studyViewModel,
        ParticipantRunResult participantRunResult,
        IReadOnlyList<LatencyResult> latencyResults,
        ObservationResult senderRestartResult,
        ObservationResult recenterResult,
        ObservationResult particlesOffResult,
        ObservationResult particlesHidden,
        ObservationResult particlesOnResult,
        ObservationResult particlesVisible,
        OperationOutcomeKind stopActionLevel,
        string stopActionDetail,
        string kioskScreenshotPath,
        string homeScreenshotPath,
        VerificationPersistenceResult verificationPersistence)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Mode: {mainViewModel.CurrentModeLabel}");
        builder.AppendLine($"Mode detail: {mainViewModel.CurrentModeDetail}");
        builder.AppendLine($"Quest: {studyViewModel.QuestStatusSummary}");
        builder.AppendLine($"Connection: {studyViewModel.ConnectionSummary}");
        builder.AppendLine($"Connection transport: {studyViewModel.ConnectionTransportSummary}");
        builder.AppendLine($"Proximity: {studyViewModel.ProximitySummary}");
        builder.AppendLine($"Proximity detail: {studyViewModel.ProximityDetail}");
        builder.AppendLine($"Pinned build: {studyViewModel.PinnedBuildSummary}");
        builder.AppendLine($"Installed build: {studyViewModel.InstalledApkSummary}");
        builder.AppendLine($"Device profile: {studyViewModel.DeviceProfileSummary}");
        builder.AppendLine($"Verified environment: {verificationPersistence.Summary}");
        builder.AppendLine($"Verified environment detail: {verificationPersistence.Detail}");
        builder.AppendLine($"Live runtime: {studyViewModel.LiveRuntimeSummary}");
        builder.AppendLine($"LSL: {studyViewModel.LslSummary}");
        builder.AppendLine($"LSL detail: {studyViewModel.LslDetail}");
        builder.AppendLine($"Controller: {studyViewModel.ControllerSummary}");
        builder.AppendLine($"Coherence: {studyViewModel.CoherenceSummary}");
        builder.AppendLine($"Coherence route: {studyViewModel.CoherenceRouteSummary}");
        builder.AppendLine($"Recenter: {studyViewModel.RecenterSummary}");
        builder.AppendLine($"Recenter detail: {studyViewModel.RecenterDetail}");
        builder.AppendLine($"Particles: {studyViewModel.ParticlesSummary}");
        builder.AppendLine($"Particles detail: {studyViewModel.ParticlesDetail}");
        builder.AppendLine($"Kiosk exit outcome: {stopActionLevel}");
        builder.AppendLine($"Kiosk exit detail: {stopActionDetail}");
        builder.AppendLine($"Last action: {studyViewModel.LastActionLabel}");
        builder.AppendLine($"Quest kiosk screenshot: {kioskScreenshotPath}");
        builder.AppendLine($"Quest final screenshot: {homeScreenshotPath}");
        builder.AppendLine();
        builder.AppendLine("Command observations:");
        builder.AppendLine($"- {senderRestartResult.Label}: {senderRestartResult.Detail}");
        builder.AppendLine($"- {recenterResult.Label}: {recenterResult.Detail}");
        builder.AppendLine($"- {particlesOffResult.Label}: {particlesOffResult.Detail}");
        builder.AppendLine($"- {particlesHidden.Label}: {particlesHidden.Detail}");
        builder.AppendLine($"- {particlesOnResult.Label}: {particlesOnResult.Detail}");
        builder.AppendLine($"- {particlesVisible.Label}: {particlesVisible.Detail}");
        builder.AppendLine($"- {participantRunResult.LslFlowResult.Label}: {participantRunResult.LslFlowResult.Detail}");
        builder.AppendLine();
        builder.AppendLine("Participant run:");
        builder.AppendLine($"- Participant id: {participantRunResult.ParticipantId}");
        builder.AppendLine($"- Session id: {participantRunResult.SessionId}");
        builder.AppendLine($"- Dataset id: {participantRunResult.DatasetId}");
        builder.AppendLine($"- Dataset hash: {participantRunResult.DatasetHash}");
        builder.AppendLine($"- Settings hash: {participantRunResult.SettingsHash}");
        builder.AppendLine($"- Environment hash: {participantRunResult.EnvironmentHash}");
        builder.AppendLine($"- Local session folder: {participantRunResult.LocalSessionFolderPath}");
        builder.AppendLine($"- Quest device selector: {participantRunResult.DeviceSelector}");
        builder.AppendLine($"- Quest session directory: {participantRunResult.DeviceSessionDirectory}");
        builder.AppendLine($"- Quest ready screenshot: {participantRunResult.ReadyQuestScreenshotPath}");
        builder.AppendLine($"- Quest running screenshot: {participantRunResult.RunningQuestScreenshotPath}");
        builder.AppendLine($"- Quest ended screenshot: {participantRunResult.EndedQuestScreenshotPath}");
        builder.AppendLine($"- Local settings metadata: {participantRunResult.LocalMetadata.Summary}");
        builder.AppendLine($"- Quest settings metadata: {participantRunResult.DeviceMetadata.Summary}");
        builder.AppendLine($"- Metadata match: {BuildMetadataMatchSummary(participantRunResult.LocalMetadata, participantRunResult.DeviceMetadata)}");
        builder.AppendLine();
        builder.AppendLine("Observed LSL loop latency:");

        foreach (var result in latencyResults)
        {
            builder.AppendLine(
                $"- value {result.Value:0.000}: sent {result.SentAt:HH:mm:ss.fff}, observed {(result.ObservedAt is null ? "timeout" : result.ObservedAt.Value.ToString("HH:mm:ss.fff"))}, latency {(result.LatencyMs is null ? "n/a" : result.LatencyMs.Value.ToString("0.0", CultureInfo.InvariantCulture) + " ms")}, key {(string.IsNullOrWhiteSpace(result.SourceKey) ? "n/a" : result.SourceKey)}");
        }

        if (latencyResults.Any(result => result.LatencyMs.HasValue))
        {
            var latencies = latencyResults.Where(result => result.LatencyMs.HasValue).Select(result => result.LatencyMs!.Value).ToArray();
            builder.AppendLine($"Mean latency: {latencies.Average():0.0} ms");
            builder.AppendLine($"Max latency: {latencies.Max():0.0} ms");
        }
        else
        {
            builder.AppendLine("No round-trip latency samples were observed.");
            builder.AppendLine($"The harness publishes normalized smoothed-HRV samples on {HrvBiofeedbackStreamContract.StreamName} / {HrvBiofeedbackStreamContract.StreamType} from this Windows machine using irregular heartbeat-timed spacing.");
            builder.AppendLine("The current public Sussex telemetry confirmed study.lsl.* inlet connectivity on this pass, but it did not echo that routed biofeedback value back over quest_twin_state.");
            builder.AppendLine("Value-level round-trip latency therefore still needs a public routed-biofeedback echo or inlet sample timestamp in the runtime state frame.");
        }

        builder.AppendLine();
        builder.AppendLine("Relevant live keys:");
        foreach (var entry in GetRelevantLiveKeys(studyViewModel))
        {
            builder.AppendLine($"- {entry.Key}: {entry.Value}");
        }

        builder.AppendLine();
        builder.AppendLine("Local session files:");
        foreach (var file in participantRunResult.LocalFiles)
        {
            builder.AppendLine($"- {file.Path} ({(file.Exists ? $"{file.LengthBytes} bytes" : "missing")})");
            builder.AppendLine(file.Preview);
        }

        builder.AppendLine();
        builder.AppendLine("Quest session files:");
        foreach (var file in participantRunResult.DeviceFiles)
        {
            builder.AppendLine($"- {file.Path} ({(file.Exists ? $"{file.LengthBytes} bytes" : "missing")})");
            builder.AppendLine(file.Preview);
        }

        builder.AppendLine();
        builder.AppendLine("Focused rows:");
        foreach (var row in studyViewModel.FocusRows)
        {
            builder.AppendLine($"- {row.Label}: {row.Value} (expected {row.Expected}) [{row.Key}]");
        }

        builder.AppendLine();
        builder.AppendLine("Operator log:");
        foreach (var entry in studyViewModel.Logs.Take(12))
        {
            builder.AppendLine($"- {entry.Timestamp:O} {entry.Level}: {entry.Message} :: {entry.Detail}");
        }

        return builder.ToString();
    }

    private static IReadOnlyDictionary<string, string?> CaptureTwinValues(StudyShellViewModel studyViewModel, params string[] keys)
    {
        var snapshot = studyViewModel.ReportedTwinStateSnapshot;
        return keys.ToDictionary(
            key => key,
            key => snapshot.TryGetValue(key, out var value) ? value : null,
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<ObservationResult> WaitForTwinValueChangeAsync(
        StudyShellViewModel studyViewModel,
        IReadOnlyDictionary<string, string?> baseline,
        TimeSpan timeout,
        string label)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = studyViewModel.ReportedTwinStateSnapshot;
            foreach (var entry in baseline)
            {
                snapshot.TryGetValue(entry.Key, out var currentValue);
                if (!string.Equals(currentValue, entry.Value, StringComparison.Ordinal))
                {
                    return new ObservationResult(
                        label,
                        true,
                        $"changed {entry.Key} from `{entry.Value ?? "n/a"}` to `{currentValue ?? "n/a"}`");
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(120));
        }

        return new ObservationResult(label, false, "timed out waiting for the relevant twin-state keys to change");
    }

    private static async Task<ObservationResult> WaitForTwinBoolAsync(
        StudyShellViewModel studyViewModel,
        string label,
        bool expectedValue,
        TimeSpan timeout,
        params string[] keys)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = studyViewModel.ReportedTwinStateSnapshot;
            foreach (var key in keys)
            {
                if (!snapshot.TryGetValue(key, out var rawValue))
                {
                    continue;
                }

                var parsedValue = ParseBool(rawValue);
                if (parsedValue == expectedValue)
                {
                    return new ObservationResult(label, true, $"{key} reported `{rawValue}`");
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(120));
        }

        return new ObservationResult(label, false, $"timed out waiting for `{expectedValue}` on {string.Join(", ", keys)}");
    }

    private static async Task ExecuteCommandAsync(
        AsyncRelayCommand command,
        StudyShellViewModel studyViewModel,
        string? expectedActionLabel,
        TimeSpan? timeout = null)
    {
        if (!command.CanExecute(null))
        {
            throw new InvalidOperationException($"Command '{expectedActionLabel ?? "anonymous"}' was not available for the harness.");
        }

        await Application.Current.Dispatcher.InvokeAsync(() => command.Execute(null));

        await WaitForConditionAsync(
            () => command.CanExecute(null)
                  && (string.IsNullOrWhiteSpace(expectedActionLabel)
                      || string.Equals(studyViewModel.LastActionLabel, expectedActionLabel, StringComparison.Ordinal)),
            timeout ?? TimeSpan.FromSeconds(20),
            $"GUI command '{expectedActionLabel ?? "anonymous"}' did not complete.");
    }

    private static async Task EnsureProximityHoldDisabledAsync(StudyShellViewModel studyViewModel)
    {
        // The Sussex operator workflow expects normal wear-sensor behavior unless
        // wake recovery explicitly needs a temporary automation assist. Do not
        // arm the 8h proximity hold here; clear it if a previous run left it on.
        if (!string.Equals(studyViewModel.ProximityActionLabel, "Enable Proximity", StringComparison.Ordinal))
        {
            return;
        }

        await ExecuteCommandAsync(
            studyViewModel.ToggleProximityCommand,
            studyViewModel,
            "Enable Proximity",
            TimeSpan.FromSeconds(25));

        await WaitForConditionAsync(
            () => studyViewModel.ProximitySummary.Contains("Normal proximity sensor behavior is active.", StringComparison.OrdinalIgnoreCase)
                  || studyViewModel.ProximityActionLabel.StartsWith("Disable", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            "Proximity hold did not clear through the study shell.");
    }

    private static async Task EnsureHeadsetWakeReadyAsync(StudyShellViewModel studyViewModel)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var summary = studyViewModel.HeadsetAwakeSummary;
            if (string.Equals(summary, "Headset awake.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await ExecuteCommandAsync(
                studyViewModel.ToggleHeadsetPowerCommand,
                studyViewModel,
                "Wake Headset",
                TimeSpan.FromSeconds(25));

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private static bool? ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => null
        };
    }

    private static void CaptureWindow(Window window, string path)
    {
        if (window.Content is not FrameworkElement root)
        {
            return;
        }

        root.UpdateLayout();
        var size = new Size(root.ActualWidth, root.ActualHeight);
        if (size.Width < 1 || size.Height < 1)
        {
            return;
        }

        root.Measure(size);
        root.Arrange(new Rect(size));
        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX),
            (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            96d * dpi.DpiScaleX,
            96d * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static async Task<VerificationPersistenceResult> PersistVerifiedEnvironmentBaselineAsync(
        string repoRoot,
        StudyShellViewModel studyViewModel,
        ObservationResult senderRestartResult,
        ObservationResult recenterResult,
        ObservationResult particlesOffResult,
        ObservationResult particlesHidden,
        ObservationResult particlesOnResult,
        ObservationResult particlesVisible,
        bool preExitRuntimeHealthy,
        bool preExitLslHealthy,
        OperationOutcomeKind stopActionLevel,
        string stopActionDetail)
    {
        if (!ShouldPersistVerifiedEnvironmentBaseline(
                studyViewModel,
                senderRestartResult,
                recenterResult,
                particlesOffResult,
                particlesHidden,
                particlesOnResult,
                particlesVisible,
                preExitRuntimeHealthy,
                preExitLslHealthy,
                stopActionLevel))
        {
            return new VerificationPersistenceResult(
                Persisted: false,
                Summary: "Verification baseline not updated.",
                Detail: $"The Sussex run completed, but one or more critical pass conditions failed or the headset identity could not be read back cleanly. Kiosk exit detail: {stopActionDetail}",
                Baseline: null);
        }

        var baseline = new StudyVerificationBaseline(
            ApkSha256: studyViewModel.InstalledApkHash,
            SoftwareVersion: studyViewModel.HeadsetSoftwareRelease,
            BuildId: studyViewModel.HeadsetSoftwareBuildId,
            DisplayId: studyViewModel.HeadsetSoftwareDisplayId,
            DeviceProfileId: studyViewModel.PinnedDeviceProfileId,
            EnvironmentHash: StudyVerificationFingerprint.Compute(
                studyViewModel.PinnedPackageId,
                studyViewModel.InstalledApkHash,
                studyViewModel.HeadsetSoftwareRelease,
                studyViewModel.HeadsetSoftwareBuildId,
                studyViewModel.PinnedDeviceProfileId,
                studyViewModel.HeadsetSoftwareDisplayId),
            VerifiedAtUtc: DateTimeOffset.UtcNow,
            VerifiedBy: "tools/ViscerealityCompanion.VerificationHarness");

        var studyShellPath = Path.Combine(repoRoot, "samples", "study-shells", $"{studyViewModel.StudyId}.json");
        var compatibilityPath = Path.Combine(repoRoot, "samples", "quest-session-kit", "APKs", "compatibility.json");
        await UpdateStudyShellVerificationAsync(studyShellPath, baseline);
        await UpdateCompatibilityVerificationAsync(compatibilityPath, baseline);

        return new VerificationPersistenceResult(
            Persisted: true,
            Summary: "Verification baseline updated.",
            Detail: $"Recorded OS {baseline.SoftwareVersion} build {baseline.BuildId} against APK {baseline.ApkSha256} with environment hash {baseline.EnvironmentHash}.",
            Baseline: baseline);
    }

    private static bool ShouldPersistVerifiedEnvironmentBaseline(
        StudyShellViewModel studyViewModel,
        ObservationResult senderRestartResult,
        ObservationResult recenterResult,
        ObservationResult particlesOffResult,
        ObservationResult particlesHidden,
        ObservationResult particlesOnResult,
        ObservationResult particlesVisible,
        bool preExitRuntimeHealthy,
        bool preExitLslHealthy,
        OperationOutcomeKind stopActionLevel)
        => MatchesHash(studyViewModel.InstalledApkHash, studyViewModel.PinnedBuildHash)
           && !string.IsNullOrWhiteSpace(studyViewModel.HeadsetSoftwareRelease)
           && !string.IsNullOrWhiteSpace(studyViewModel.HeadsetSoftwareBuildId)
           && studyViewModel.DeviceProfileLevel == OperationOutcomeKind.Success
           && preExitRuntimeHealthy
           && preExitLslHealthy
           && stopActionLevel == OperationOutcomeKind.Success
           && senderRestartResult.Success
           && recenterResult.Success
           && particlesOffResult.Success
           && particlesHidden.Success
           && particlesOnResult.Success
           && particlesVisible.Success;

    private static async Task UpdateStudyShellVerificationAsync(string path, StudyVerificationBaseline baseline)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject
            ?? throw new InvalidDataException($"Could not parse study shell definition at {path}.");
        var app = root["app"] as JsonObject
            ?? throw new InvalidDataException($"Study shell definition at {path} did not contain an app object.");

        app["sha256"] = baseline.ApkSha256;
        app["verification"] = JsonSerializer.SerializeToNode(baseline, ManifestJsonOptions);
        await File.WriteAllTextAsync(path, root.ToJsonString(ManifestJsonOptions));
    }

    private static async Task UpdateCompatibilityVerificationAsync(string path, StudyVerificationBaseline baseline)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject
            ?? throw new InvalidDataException($"Could not parse compatibility manifest at {path}.");
        var apps = root["apps"] as JsonArray
            ?? throw new InvalidDataException($"Compatibility manifest at {path} did not contain an apps array.");

        var entry = apps
            .OfType<JsonObject>()
            .FirstOrDefault(app => string.Equals(app["sha256"]?.GetValue<string>(), baseline.ApkSha256, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Compatibility manifest at {path} did not contain an entry for APK {baseline.ApkSha256}.");

        entry["sha256"] = baseline.ApkSha256;
        entry["verification"] = JsonSerializer.SerializeToNode(baseline, ManifestJsonOptions);
        await File.WriteAllTextAsync(path, root.ToJsonString(ManifestJsonOptions));
    }

    private static bool MatchesHash(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string BuildMetadataMatchSummary(SessionMetadataResult local, SessionMetadataResult device)
    {
        if (!local.IsAvailable || !device.IsAvailable)
        {
            return "n/a";
        }

        var keys = new[]
        {
            "StudyId",
            "ParticipantId",
            "SessionId",
            "DatasetId",
            "DatasetHash",
            "SettingsHash",
            "EnvironmentHash",
            "ApkSha256",
            "DeviceProfileId"
        };

        var checks = keys.Select(key =>
        {
            local.Values.TryGetValue(key, out var localValue);
            device.Values.TryGetValue(key, out var deviceValue);
            var matches = string.Equals(localValue, deviceValue, StringComparison.OrdinalIgnoreCase);
            return $"{key}={(matches ? "match" : $"mismatch ({localValue} vs {deviceValue})")}";
        });

        return string.Join(", ", checks);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> GetRelevantLiveKeys(StudyShellViewModel studyViewModel)
    {
        var relevant = new List<KeyValuePair<string, string>>();
        var snapshot = studyViewModel.ReportedTwinStateSnapshot;
        var coherenceMirrorBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in snapshot)
        {
            if (entry.Key.StartsWith("driver.stream.", StringComparison.OrdinalIgnoreCase) &&
                entry.Key.EndsWith(".name", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Value, "coherence_lsl", StringComparison.OrdinalIgnoreCase))
            {
                coherenceMirrorBases.Add(entry.Key[..^".name".Length]);
            }
        }

        foreach (var entry in snapshot.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (entry.Key.StartsWith("study.lsl", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("study.recenter", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("study.particles", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("study.session", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("study.recording.device", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("study.performance", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("connection.lsl", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("signal01.coherence_lsl", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Contains("coherence_lsl", StringComparison.OrdinalIgnoreCase) ||
                coherenceMirrorBases.Any(baseKey => entry.Key.StartsWith(baseKey, StringComparison.OrdinalIgnoreCase)))
            {
                relevant.Add(entry);
            }
        }

        return relevant;
    }

    private sealed record LatencyResult(float Value, DateTimeOffset SentAt, DateTimeOffset? ObservedAt, string SourceKey, double? LatencyMs);
    private sealed record ObservedValueResult(DateTimeOffset? Timestamp, string SourceKey);
    private sealed record ObservationResult(string Label, bool Success, string Detail);
    private sealed record StopAppVerificationResult(OperationOutcomeKind Level, string Detail, string HomeScreenshotPath);
    private sealed record VerificationPersistenceResult(bool Persisted, string Summary, string Detail, StudyVerificationBaseline? Baseline);
    private sealed record FileInspectionResult(string Path, bool Exists, long LengthBytes, string Preview);
    private sealed record SessionMetadataResult(string Path, bool IsAvailable, IReadOnlyDictionary<string, string> Values, string Summary);
    private sealed record ParticipantRunResult(
        string ParticipantId,
        string SessionId,
        string DatasetId,
        string DatasetHash,
        string SettingsHash,
        string EnvironmentHash,
        string LocalSessionFolderPath,
        string DeviceSelector,
        string DeviceSessionDirectory,
        string ReadyQuestScreenshotPath,
        string RunningQuestScreenshotPath,
        string EndedQuestScreenshotPath,
        ObservationResult LslFlowResult,
        SessionMetadataResult LocalMetadata,
        SessionMetadataResult DeviceMetadata,
        IReadOnlyList<FileInspectionResult> LocalFiles,
        IReadOnlyList<FileInspectionResult> DeviceFiles);
}

internal sealed class FloatLslTestOutlet : IDisposable
{
    private nint _streamInfo;
    private nint _outlet;

    public void Open(string streamName, string streamType, string sourceId)
    {
        LslNative.EnsureResolverInstalled();
        _streamInfo = LslNative.CreateStreamInfo(streamName, streamType, 1, 0d, 1, sourceId);
        if (_streamInfo == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create LSL stream info for the harness outlet.");
        }

        LslNative.AppendSingleChannelMetadata(
            _streamInfo,
            HrvBiofeedbackStreamContract.ChannelLabel,
            HrvBiofeedbackStreamContract.ChannelUnit);

        _outlet = LslNative.CreateOutlet(_streamInfo, 1, 1);
        if (_outlet == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create the LSL harness outlet.");
        }
    }

    public void PushSample(float value)
    {
        if (_outlet == IntPtr.Zero)
        {
            throw new InvalidOperationException("The LSL harness outlet is not open.");
        }

        var timestamp = LslNative.LocalClock();
        LslNative.PushFloatSample(_outlet, [value], timestamp);
    }

    public void Dispose()
    {
        if (_outlet != IntPtr.Zero)
        {
            LslNative.DestroyOutlet(_outlet);
            _outlet = IntPtr.Zero;
        }

        if (_streamInfo != IntPtr.Zero)
        {
            LslNative.DestroyStreamInfo(_streamInfo);
            _streamInfo = IntPtr.Zero;
        }
    }

    private static class LslNative
    {
        private static readonly object Sync = new();
        private static readonly string[] CandidateLibraryPaths = BuildCandidateLibraryPaths();
        private static bool _resolverInstalled;
        private static nint _libraryHandle;

        public static void EnsureResolverInstalled()
        {
            lock (Sync)
            {
                if (_resolverInstalled)
                {
                    return;
                }

                NativeLibrary.SetDllImportResolver(typeof(LslNative).Assembly, ResolveLibrary);
                _resolverInstalled = true;
            }
        }

        public static nint CreateStreamInfo(string name, string type, int channelCount, double nominalRate, int channelFormat, string sourceId)
            => lsl_create_streaminfo(name, type, channelCount, nominalRate, channelFormat, sourceId);

        public static void DestroyStreamInfo(nint streamInfo) => lsl_destroy_streaminfo(streamInfo);
        public static nint CreateOutlet(nint streamInfo, int chunkSize, int maxBuffered) => lsl_create_outlet(streamInfo, chunkSize, maxBuffered);
        public static void DestroyOutlet(nint outlet) => lsl_destroy_outlet(outlet);
        public static void AppendSingleChannelMetadata(nint streamInfo, string label, string unit)
        {
            var desc = lsl_get_desc(streamInfo);
            if (desc == IntPtr.Zero)
            {
                return;
            }

            var channels = lsl_append_child(desc, "channels");
            if (channels == IntPtr.Zero)
            {
                return;
            }

            var channel = lsl_append_child(channels, "channel");
            if (channel == IntPtr.Zero)
            {
                return;
            }

            lsl_append_child_value(channel, "label", label);
            lsl_append_child_value(channel, "unit", unit);
        }

        public static double LocalClock() => lsl_local_clock();
        public static int PushFloatSample(nint outlet, float[] values, double timestamp) => lsl_push_sample_ftp(outlet, values, timestamp, 1);

        private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, "lsl", StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            lock (Sync)
            {
                if (_libraryHandle != IntPtr.Zero)
                {
                    return _libraryHandle;
                }

                foreach (var candidate in CandidateLibraryPaths)
                {
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    if (NativeLibrary.TryLoad(candidate, out _libraryHandle))
                    {
                        return _libraryHandle;
                    }
                }
            }

            throw new DllNotFoundException(
                $"Could not locate or load lsl.dll for the verification harness. Searched: {string.Join("; ", CandidateLibraryPaths)}");
        }

        private static string[] BuildCandidateLibraryPaths()
        {
            var candidates = new List<string>();

            static void AddCandidate(ICollection<string> list, string? path)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    list.Add(Path.GetFullPath(path));
                }
            }

            AddCandidate(candidates, Environment.GetEnvironmentVariable("VISCEREALITY_LSL_DLL"));
            AddCandidate(candidates, Path.Combine(AppContext.BaseDirectory, "lsl.dll"));
            AddCandidate(candidates, Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "lsl.dll"));

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AddUserToolsLiblslCandidates(candidates, userProfile);
            AddCandidate(candidates, Path.Combine(userProfile, "source", "repos", "AstralKarateDojo", "Assets", "Plugins", "LSL", "Windows", "x64", "lsl.dll"));
            AddCandidate(candidates, Path.Combine(userProfile, "source", "repos", "AstralKarateDojo-phone-monitor-shell", "Assets", "Plugins", "LSL", "Windows", "x64", "lsl.dll"));
            AddCandidate(candidates, Path.Combine(userProfile, "source", "repos", "UnitySixthSense", "Assets", "Plugins", "LSL", "Windows", "x64", "lsl.dll"));
            AddCandidate(candidates, Path.Combine(userProfile, "source", "repos", "Viscereality", "Viscereality", "Packages", "com.labstreaminglayer.lsl4unity", "Plugins", "LSL", "Windows", "x64", "lsl.dll"));

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void AddUserToolsLiblslCandidates(ICollection<string> candidates, string userProfile)
        {
            var userToolsRoot = Path.Combine(userProfile, "Tools", "liblsl");
            if (!Directory.Exists(userToolsRoot))
            {
                return;
            }

            foreach (var versionDirectory in Directory.EnumerateDirectories(userToolsRoot)
                         .OrderByDescending(path =>
                         {
                             var versionName = Path.GetFileName(path);
                             return Version.TryParse(versionName, out var version)
                                 ? version
                                 : new Version(0, 0);
                         }))
            {
                var candidate = Path.Combine(versionDirectory, "bin", "lsl.dll");
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    candidates.Add(Path.GetFullPath(candidate));
                }
            }
        }

        [DllImport("lsl", EntryPoint = "lsl_create_streaminfo", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern nint lsl_create_streaminfo(
            string name,
            string type,
            int channelCount,
            double nominalRate,
            int channelFormat,
            string sourceId);

        [DllImport("lsl", EntryPoint = "lsl_destroy_streaminfo", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_streaminfo(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_create_outlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint lsl_create_outlet(nint streamInfo, int chunkSize, int maxBuffered);

        [DllImport("lsl", EntryPoint = "lsl_destroy_outlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_outlet(nint outlet);

        [DllImport("lsl", EntryPoint = "lsl_get_desc", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint lsl_get_desc(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_append_child", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern nint lsl_append_child(nint element, string name);

        [DllImport("lsl", EntryPoint = "lsl_append_child_value", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern nint lsl_append_child_value(nint element, string name, string value);

        [DllImport("lsl", EntryPoint = "lsl_local_clock", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_local_clock();

        [DllImport("lsl", EntryPoint = "lsl_push_sample_ftp", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lsl_push_sample_ftp(nint outlet, float[] data, double timestamp, int pushThrough);
    }
}
