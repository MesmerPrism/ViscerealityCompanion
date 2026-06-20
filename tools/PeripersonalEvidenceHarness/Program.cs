using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [STAThread]
    private static void Main(string[] args)
    {
        var options = EvidenceRunOptions.FromArgs(args);
        var outputRoot = !string.IsNullOrWhiteSpace(options.OutputRoot)
            ? Path.GetFullPath(args[0])
            : Path.Combine(
                Directory.GetCurrentDirectory(),
                "artifacts",
                "peripersonal-wpf-evidence-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(outputRoot);

        Environment.SetEnvironmentVariable(
            ViscerealityCompanion.App.App.SuppressStartupWindowEnvironmentVariable,
            "1");
        var app = new ViscerealityCompanion.App.App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        app.Startup += async (_, _) =>
        {
            try
            {
                await RunAsync(outputRoot, options).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(outputRoot, "wpf-evidence-error.txt"), ex.ToString());
                Environment.ExitCode = 1;
            }
            finally
            {
                app.Shutdown();
            }
        };

        app.Run();
    }

    private static async Task RunAsync(string outputRoot, EvidenceRunOptions options)
    {
        var study = CreateStudy();
        using var viewModel = new StudyShellViewModel(study)
        {
            ParticipantIdDraft = options.ParticipantRef,
            PeripersonalSessionIdDraft = options.SessionId,
            PeripersonalHandednessDraft = options.Handedness,
            PeripersonalLanguageCodeDraft = options.LanguageCode,
            PeripersonalXrBlockIdDraft = "xr-block-1"
        };
        var condition = viewModel.Conditions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, options.ConditionId, StringComparison.OrdinalIgnoreCase));
        if (condition is not null)
        {
            viewModel.SelectedCondition = condition;
        }
        else if (viewModel.Conditions.Count > 0)
        {
            viewModel.SelectedCondition = viewModel.Conditions[0];
        }

        var window = new StudyExperimentSessionWindow(viewModel)
        {
            Width = 1680,
            Height = 1320,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.Show();
        await WaitForWindowAsync(window).ConfigureAwait(true);

        var manifestPath = Path.Combine(outputRoot, "wpf-evidence-steps.jsonl");
        File.Delete(manifestPath);
        var screenshotsRoot = Path.Combine(outputRoot, "wpf");
        Directory.CreateDirectory(screenshotsRoot);

        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "01-initial",
            $"viscereality peripersonal prepare --participant {options.ParticipantRef} --session {options.SessionId} --handedness {options.Handedness} --language {options.LanguageCode}",
            "Prepare Session",
            "Initial Peripersonal setup panel before Prepare Session.",
            "PreparePeripersonalSessionButton").ConfigureAwait(true);

        var workflow = new PeripersonalOperatorWorkflowService(
            study,
            new FakePeripersonalCommandTransport(),
            Path.Combine(outputRoot, "windows-sessions"),
            utcNow: () => new DateTimeOffset(2026, 06, 20, 17, 30, 00, TimeSpan.Zero),
            questAppCloser: new FakeQuestAppCloser(),
            questBackupPuller: new FakeQuestBackupPuller());
        SetPrivateField(viewModel, "_peripersonalWorkflow", workflow);
        var prepare = await workflow.PrepareSessionAsync(new PeripersonalSessionSetupRequest(
                ParticipantRef: options.ParticipantRef,
                SessionId: options.SessionId,
                Handedness: options.Handedness,
                StudyId: study.Id,
                LanguageCode: options.LanguageCode,
                InitialConditionId: viewModel.SelectedCondition?.Id ?? string.Empty))
            .ConfigureAwait(true);
        await ApplyPeripersonalResultAsync(viewModel, "Prepare Session", prepare).ConfigureAwait(true);
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "02-prepared",
            "viscereality peripersonal open-questionnaire --block block-1 --condition-number 1",
            "Open Block 1",
            "Prepared session with MAIA language and session folder visible.",
            "OpenPeripersonalQuestionnaireBlock1Button").ConfigureAwait(true);

        await InvokePrivateTaskAsync(viewModel, "OpenPeripersonalQuestionnaireBlockAsync", "block-1", 1).ConfigureAwait(true);
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "03-block1-open",
            "viscereality peripersonal mark-block1-submitted",
            "Block 1 Submitted",
            "Block 1 MAIA launch command has been recorded; operator can mark the block submitted after participant completion.",
            "MarkPeripersonalBlock1SubmittedButton").ConfigureAwait(true);

        await InvokePrivateTaskAsync(viewModel, "MarkPeripersonalBlock1SubmittedAsync").ConfigureAwait(true);
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "04-block1-submitted",
            "viscereality peripersonal start-recording",
            "Start Recording",
            "Block 1 submitted; the global Start Recording action is available.",
            "RecordingToggleButton").ConfigureAwait(true);

        await InvokePrivateTaskAsync(viewModel, "StartPeripersonalRecordingAsync").ConfigureAwait(true);
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "05-recording",
            "viscereality peripersonal clock-probe --duration-seconds 10 --probe-interval-ms 250",
            "Run Clock Probe",
            "One global Peripersonal recording is active; the operator can run the clock-alignment probe inside the same recording.",
            "RunClockProbeButton").ConfigureAwait(true);

        ApplyFakeClockAlignmentResult(viewModel);
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "06-clock-probe-complete",
            "viscereality peripersonal particles particles-on",
            "Particles On",
            "Clock probe completed and the latest probe card shows echoed Quest samples and round-trip timing.",
            "ParticlesToggleButton").ConfigureAwait(true);

        await InvokePrivateTaskAsync(viewModel, "SetPeripersonalParticlesVisibleAsync", true).ConfigureAwait(true);
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "07-particles-on",
            "viscereality peripersonal particles particles-off",
            "Particles Off",
            "Particles visible command state after the Particles-ON marker.",
            "ParticlesToggleButton").ConfigureAwait(true);

        await InvokePrivateTaskAsync(viewModel, "SetPeripersonalParticlesVisibleAsync", false).ConfigureAwait(true);
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "08-particles-off",
            "viscereality peripersonal mark-xr-block-end --block xr-block-1 --condition media-evidence",
            "Mark XR Block End",
            "Particles hidden again before recording an XR block-end marker.",
            "MarkPeripersonalXrBlockEndButton").ConfigureAwait(true);

        await InvokePrivateTaskAsync(viewModel, "MarkPeripersonalXrBlockEndAsync").ConfigureAwait(true);
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "09-xr-block1-end",
            "viscereality peripersonal open-questionnaire --block block-2 --condition-number 2",
            "Open Block 2",
            "XR block 1 ended as a marker while the global recording remains active.",
            "OpenPeripersonalQuestionnaireBlock2Button").ConfigureAwait(true);

        await InvokePrivateTaskAsync(viewModel, "OpenPeripersonalQuestionnaireBlockAsync", "block-2", 2).ConfigureAwait(true);
        viewModel.PeripersonalXrBlockIdDraft = "xr-block-2";
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "10-block2-open",
            "viscereality peripersonal mark-xr-block-end --block xr-block-2 --condition media-evidence",
            "Mark XR Block End",
            "Block 2 MAIA spatial frame reference launch has been recorded.",
            "MarkPeripersonalXrBlockEndButton").ConfigureAwait(true);

        await InvokePrivateTaskAsync(viewModel, "MarkPeripersonalXrBlockEndAsync").ConfigureAwait(true);
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "11-xr-block2-end",
            "viscereality peripersonal open-questionnaire --block block-3 --condition-number 3",
            "Open Block 3",
            "XR block 2 ended as a marker while the global recording remains active.",
            "OpenPeripersonalQuestionnaireBlock3Button").ConfigureAwait(true);

        await InvokePrivateTaskAsync(viewModel, "OpenPeripersonalQuestionnaireBlockAsync", "block-3", 3).ConfigureAwait(true);
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "12-block3-open",
            "viscereality peripersonal stop-recording",
            "Stop Recording",
            "Block 3 MAIA spatial frame reference launch has been recorded before final stop.",
            "RecordingToggleButton").ConfigureAwait(true);

        await InvokePrivateTaskAsync(viewModel, "StopPeripersonalRecordingAsync").ConfigureAwait(true);
        await CaptureStepAsync(
            window,
            manifestPath,
            screenshotsRoot,
            "13-stopped",
            "viscereality peripersonal workflow-status",
            "Workflow Status",
            "Final Stop Recording completed and the WPF session surface shows the stopped state.",
            "RecordingToggleButton").ConfigureAwait(true);

        window.Close();
    }

    private static async Task WaitForWindowAsync(Window window)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (window.IsLoaded && window.IsVisible)
            {
                window.UpdateLayout();
                await Task.Delay(150).ConfigureAwait(true);
                return;
            }

            await Task.Delay(50).ConfigureAwait(true);
        }

        throw new InvalidOperationException("Timed out waiting for the WPF evidence window.");
    }

    private static async Task CaptureStepAsync(
        Window window,
        string manifestPath,
        string screenshotsRoot,
        string stepId,
        string cliCommand,
        string uiAction,
        string note,
        string? focusElementName = null)
    {
        await Task.Delay(150).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(focusElementName) &&
            window.FindName(focusElementName) is FrameworkElement focusElement)
        {
            focusElement.BringIntoView();
            ScrollAncestorToElement(focusElement, topPadding: 72);
            await Task.Delay(150).ConfigureAwait(true);
        }

        window.UpdateLayout();
        var screenshotPath = Path.Combine(screenshotsRoot, stepId + ".png");
        CaptureWindow(window, screenshotPath);
        var record = new
        {
            stepId,
            cliCommand,
            uiAction,
            note,
            screenshotPath,
            capturedAtUtc = DateTimeOffset.UtcNow
        };
        await File.AppendAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine)
            .ConfigureAwait(false);
    }

    private static void ScrollAncestorToElement(FrameworkElement element, double topPadding)
    {
        var scrollViewer = FindAncestor<ScrollViewer>(element);
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.UpdateLayout();
        var point = element.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + point.Y - topPadding);
        scrollViewer.UpdateLayout();
    }

    private static T? FindAncestor<T>(DependencyObject node)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(node);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static void CaptureWindow(Window window, string path)
    {
        if (window.Content is not FrameworkElement root)
        {
            throw new InvalidOperationException("Window content is not renderable.");
        }

        root.UpdateLayout();
        var size = new Size(root.ActualWidth, root.ActualHeight);
        if (size.Width < 1 || size.Height < 1)
        {
            throw new InvalidOperationException("Window content has no renderable size.");
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

    private static async Task ApplyPeripersonalResultAsync(
        StudyShellViewModel viewModel,
        string actionLabel,
        PeripersonalWorkflowOperationResult result)
    {
        await InvokePrivateTaskAsync(
                viewModel,
                "ApplyPeripersonalWorkflowResultAsync",
                actionLabel,
                result)
            .ConfigureAwait(true);
    }

    private static void ApplyFakeClockAlignmentResult(StudyShellViewModel viewModel)
    {
        var sentAt = new DateTimeOffset(2026, 06, 20, 17, 31, 00, TimeSpan.Zero);
        var result = new StudyClockAlignmentRunResult(
            new OperationOutcome(
                OperationOutcomeKind.Success,
                "Background clock-alignment probe completed.",
                "WPF evidence harness simulated a completed Quest echo result for the visual guide. The live CLI run provides the real clock CSV rows."),
            new StudyClockAlignmentSummary(
                ProbesSent: 2,
                EchoesReceived: 2,
                RecommendedQuestMinusWindowsClockSeconds: 0.0042d,
                MedianQuestMinusWindowsClockSeconds: 0.0041d,
                MeanQuestMinusWindowsClockSeconds: 0.0042d,
                MeanRoundTripSeconds: 0.018d,
                MinRoundTripSeconds: 0.017d,
                MaxRoundTripSeconds: 0.019d),
            [
                new StudyClockAlignmentSample(
                    StudyClockAlignmentWindowKind.BackgroundSparse,
                    1,
                    sentAt,
                    100.000d,
                    sentAt.AddMilliseconds(18),
                    100.018d,
                    100.018d,
                    sentAt.AddMilliseconds(9).ToString("O"),
                    100.013d,
                    100.014d,
                    0.004d,
                    0.018d)
            ]);
        InvokePrivateMethod(
            viewModel,
            "ApplyClockAlignmentOutcome",
            StudyClockAlignmentWindowKind.BackgroundSparse,
            result);
        InvokePrivateMethod(
            viewModel,
            "UpdateClockAlignmentConsistencyTelemetry",
            StudyClockAlignmentWindowKind.BackgroundSparse,
            result);
    }

    private static async Task InvokePrivateTaskAsync(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        var result = method.Invoke(target, args);
        if (result is Task task)
        {
            await task.ConfigureAwait(true);
        }
    }

    private static void InvokePrivateMethod(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        method.Invoke(target, args);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
        field.SetValue(target, value);
    }

    private static StudyShellDefinition CreateStudy()
        => new(
            "peripersonal-space",
            "Peripersonal Space",
            "Peripersonal study team",
            "MAIA spatial questionnaire evidence shell.",
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
                EndExperimentActionId: "stop_recording_and_close_apps"),
            [
                new StudyConditionDefinition(
                    "media-evidence",
                    "Media Evidence",
                    "Short evidence run condition.",
                    "peripersonal-visual-media-evidence",
                    "peripersonal-breath-controller"),
                new StudyConditionDefinition(
                    "anchor-only",
                    "Anchor Only",
                    "Anchor-only comparison condition.",
                    "peripersonal-visual-anchor-only",
                    "peripersonal-breath-controller")
            ]);

    private sealed class FakePeripersonalCommandTransport : IPeripersonalCommandTransport
    {
        public Task<PeripersonalCommandReceiptEnvelope> SendAsync(
            PeripersonalCommandEnvelope command,
            CancellationToken cancellationToken = default)
        {
            var observedState = new JsonObject
            {
                ["session_ready"] = true,
                ["session_folder_name"] = command.Payload?["session_folder_name"]?.GetValue<string>() ?? string.Empty,
                ["recording_active"] = command.Action switch
                {
                    "start_recording" => true,
                    "stop_recording_and_close_apps" => false,
                    _ => false
                },
                ["quest_apps_close_requested"] = command.Action == "stop_recording_and_close_apps"
            };
            if (command.Payload?["particles_visible"] is JsonNode particles)
            {
                observedState["particles_visible"] = particles.GetValue<bool>();
            }

            return Task.FromResult(PeripersonalCommandReceiptEnvelope.Create(
                command,
                accepted: true,
                executed: true,
                completed: true,
                transportReceived: command.Transport,
                message: command.Action + " accepted by WPF evidence harness.",
                observedState: observedState,
                stateRevision: command.Sequence));
        }
    }

    private sealed class FakeQuestAppCloser : IPeripersonalQuestAppCloser
    {
        public Task<PeripersonalQuestAppCloseResult> CloseQuestAppsAsync(
            string unityPackage,
            string panelPackage,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PeripersonalQuestAppCloseResult(true, "WPF evidence harness close."));
    }

    private sealed class FakeQuestBackupPuller : IPeripersonalQuestBackupPuller
    {
        public Task<PeripersonalQuestBackupPullResult> PullSessionBackupAsync(
            PeripersonalQuestBackupPullRequest request,
            CancellationToken cancellationToken = default)
        {
            var pullFolder = Path.Combine(request.WindowsSessionDirectory, request.WindowsPullSubfolder);
            Directory.CreateDirectory(pullFolder);
            File.WriteAllText(Path.Combine(pullFolder, "session_events.csv"), "event,status");
            return Task.FromResult(new PeripersonalQuestBackupPullResult(
                new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Quest backup files pulled.",
                    "WPF evidence harness created a backup placeholder.",
                    Items: [pullFolder]),
                pullFolder,
                ["session_events.csv"],
                [],
                []));
        }
    }

    private sealed record EvidenceRunOptions(
        string OutputRoot,
        string ParticipantRef,
        string SessionId,
        string LanguageCode,
        string Handedness,
        string ConditionId)
    {
        public static EvidenceRunOptions FromArgs(string[] args)
            => new(
                GetArg(args, 0, string.Empty),
                GetArg(args, 1, "P-MAIA-E2E"),
                GetArg(args, 2, "session-001"),
                GetArg(args, 3, "en"),
                GetArg(args, 4, "right-handed"),
                GetArg(args, 5, "media-evidence"));

        private static string GetArg(string[] args, int index, string fallback)
            => args.Length > index && !string.IsNullOrWhiteSpace(args[index])
                ? args[index].Trim()
                : fallback;
    }
}
