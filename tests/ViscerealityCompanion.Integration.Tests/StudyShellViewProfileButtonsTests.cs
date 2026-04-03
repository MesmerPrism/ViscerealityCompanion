using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class StudyShellViewProfileButtonsTests
{
    [Fact]
    public async Task Study_shell_profile_buttons_commit_grid_edits_and_update_saved_launch_override()
    {
        var visualTemplatePath = AppAssetLocator.TryResolveSussexVisualTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex visual tuning template.");
        var controllerTemplatePath = AppAssetLocator.TryResolveSussexControllerBreathingTuningTemplatePath()
            ?? throw new FileNotFoundException("Could not resolve the Sussex controller-breathing tuning template.");

        var visualCompiler = new SussexVisualTuningCompiler(File.ReadAllText(visualTemplatePath));
        var controllerCompiler = new SussexControllerBreathingTuningCompiler(File.ReadAllText(controllerTemplatePath));
        var visualStore = new SussexVisualProfileStore(visualCompiler);
        var controllerStore = new SussexControllerBreathingProfileStore(controllerCompiler);
        var visualProfileName = $"ZZZ GUI Visual {Guid.NewGuid():N}";
        var controllerProfileName = $"ZZZ GUI Controller {Guid.NewGuid():N}";
        var studyId = $"study-shell-gui-{Guid.NewGuid():N}";

        SussexVisualProfileRecord? visualRecord = null;
        SussexControllerBreathingProfileRecord? controllerRecord = null;
        try
        {
            visualRecord = await visualStore.CreateFromTemplateAsync(visualProfileName);
            controllerRecord = await controllerStore.CreateFromTemplateAsync(controllerProfileName);

            await RunOnStaAsync(async () =>
            {
                var app = EnsureApplication();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                using var visualWorkspace = new SussexVisualProfilesWorkspaceViewModel(CreateStudy(studyId), new PreviewQuestControlService());
                using var controllerWorkspace = new SussexControllerBreathingProfilesWorkspaceViewModel(CreateStudy(studyId), new PreviewQuestControlService());
                await visualWorkspace.InitializeAsync();
                await controllerWorkspace.InitializeAsync();

                visualWorkspace.SelectedProfile = visualWorkspace.Profiles.First(profile => string.Equals(profile.Id, visualRecord.Id, StringComparison.OrdinalIgnoreCase));
                controllerWorkspace.SelectedProfile = controllerWorkspace.Profiles.First(profile => string.Equals(profile.Id, controllerRecord.Id, StringComparison.OrdinalIgnoreCase));

                var host = new StudyShellViewHost(visualWorkspace, controllerWorkspace);
                var view = new StudyShellView
                {
                    DataContext = host
                };
                var window = new Window
                {
                    Content = view,
                    Width = 1800,
                    Height = 1200,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                window.Show();
                await WaitForConditionAsync(() => view.IsLoaded && window.IsVisible, TimeSpan.FromSeconds(5));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var tabs = (TabControl)view.FindName("StudyPhaseTabs")!;
                tabs.SelectedIndex = 3;
                view.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var visualEditor = FindDescendantByAutomationId<TextBox>(view, "visual-current-particle_size_max")
                    ?? throw new InvalidOperationException("Could not resolve the visual editor textbox.");
                var visualStartup = FindDescendantByAutomationId<TextBlock>(view, "visual-startup-particle_size_max")
                    ?? throw new InvalidOperationException("Could not resolve the visual startup textblock.");
                var visualSaveButton = (Button)view.FindName("VisualSaveStartupSnapshotButton")!;
                var visualApplyButton = (Button)view.FindName("VisualApplyCurrentSessionButton")!;

                visualEditor.Focus();
                visualEditor.Text = 0.24d.ToString("0.###", CultureInfo.CurrentCulture);
                visualSaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, visualSaveButton));

                await WaitForConditionAsync(
                    () => string.Equals(visualStartup.Text, "0.24", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5));

                var visualStartupState = new SussexVisualProfileStartupStateStore(studyId).Load();
                Assert.NotNull(visualStartupState);
                Assert.NotNull(visualStartupState!.ControlValues);
                Assert.Equal(0.24d, visualStartupState.ControlValues!["particle_size_max"], 6);

                visualEditor.Focus();
                visualEditor.Text = 0.31d.ToString("0.###", CultureInfo.CurrentCulture);
                visualApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, visualApplyButton));

                await WaitForConditionAsync(
                    () => string.Equals(visualStartup.Text, "0.24", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5));
                visualStartupState = new SussexVisualProfileStartupStateStore(studyId).Load();
                Assert.NotNull(visualStartupState);
                Assert.Equal(0.24d, visualStartupState!.ControlValues!["particle_size_max"], 6);

                tabs.SelectedIndex = 4;
                view.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var controllerEditor = FindDescendantByAutomationId<TextBox>(view, "controller-current-median_window")
                    ?? throw new InvalidOperationException("Could not resolve the controller editor textbox.");
                var controllerStartup = FindDescendantByAutomationId<TextBlock>(view, "controller-startup-median_window")
                    ?? throw new InvalidOperationException("Could not resolve the controller startup textblock.");
                var controllerSaveButton = (Button)view.FindName("ControllerSaveStartupSnapshotButton")!;
                var controllerApplyButton = (Button)view.FindName("ControllerApplyCurrentSessionButton")!;

                controllerEditor.Focus();
                controllerEditor.Text = "7";
                controllerSaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, controllerSaveButton));

                await WaitForConditionAsync(
                    () => string.Equals(controllerStartup.Text, "7", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5));

                var controllerStartupState = new SussexControllerBreathingProfileStartupStateStore(studyId).Load();
                Assert.NotNull(controllerStartupState);
                Assert.NotNull(controllerStartupState!.ControlValues);
                Assert.Equal(7d, controllerStartupState.ControlValues!["median_window"], 6);

                controllerEditor.Focus();
                controllerEditor.Text = "11";
                controllerApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, controllerApplyButton));

                await WaitForConditionAsync(
                    () => string.Equals(controllerStartup.Text, "7", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5));
                controllerStartupState = new SussexControllerBreathingProfileStartupStateStore(studyId).Load();
                Assert.NotNull(controllerStartupState);
                Assert.Equal(7d, controllerStartupState!.ControlValues!["median_window"], 6);

                window.Close();
            });
        }
        finally
        {
            DeleteFileIfPresent(visualRecord?.FilePath);
            DeleteFileIfPresent(controllerRecord?.FilePath);
            DeleteStudySessionArtifacts(
                studyId,
                "sussex-visual-startup",
                "sussex-visual-apply",
                "sussex-controller-breathing-startup",
                "sussex-controller-breathing-apply");
        }
    }

    private static ViscerealityCompanion.App.App EnsureApplication()
    {
        if (Application.Current is ViscerealityCompanion.App.App current)
        {
            return current;
        }

        var app = new ViscerealityCompanion.App.App();
        app.InitializeComponent();
        return app;
    }

    private static StudyShellDefinition CreateStudy(string studyId)
        => new(
            studyId,
            "Sussex Test",
            "Sussex",
            "Test study shell definition.",
            new StudyPinnedApp(
                "Sussex",
                "com.Viscereality.SussexExperiment",
                string.Empty,
                string.Empty,
                string.Empty,
                "preview",
                string.Empty,
                AllowManualSelection: true,
                LaunchInKioskMode: false),
            new StudyPinnedDeviceProfile(
                "sussex-test-device-profile",
                "Sussex Test Device Profile",
                string.Empty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new StudyMonitoringProfile(
                ExpectedBreathingLabel: string.Empty,
                ExpectedHeartbeatLabel: string.Empty,
                ExpectedCoherenceLabel: string.Empty,
                ExpectedLslStreamName: string.Empty,
                ExpectedLslStreamType: string.Empty,
                RecenterDistanceThresholdUnits: 0d,
                LslConnectivityKeys: [],
                LslStreamNameKeys: [],
                LslStreamTypeKeys: [],
                LslValueKeys: [],
                ControllerValueKeys: [],
                ControllerStateKeys: [],
                ControllerTrackingKeys: [],
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
                "recenter",
                "particles_on",
                "particles_off"));

    private static async Task RunOnStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                action().ContinueWith(task =>
                {
                    try
                    {
                        if (task.IsFaulted)
                        {
                            completion.SetException(task.Exception!.InnerExceptions);
                        }
                        else if (task.IsCanceled)
                        {
                            completion.SetCanceled();
                        }
                        else
                        {
                            completion.SetResult();
                        }
                    }
                    finally
                    {
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());

                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromMinutes(2));
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(condition(), "Timed out waiting for the expected WPF state.");
    }

    private static T? FindDescendantByAutomationId<T>(DependencyObject root, string automationId)
        where T : FrameworkElement
    {
        if (root is T typedRoot &&
            string.Equals(AutomationProperties.GetAutomationId(typedRoot), automationId, StringComparison.Ordinal))
        {
            return typedRoot;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var match = FindDescendantByAutomationId<T>(VisualTreeHelper.GetChild(root, index), automationId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static void DeleteFileIfPresent(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteStudySessionArtifacts(string studyId, params string[] prefixes)
    {
        var sessionRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ViscerealityCompanion",
            "session");
        var token = studyId
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        var sanitizedStudyId = new string(token).Trim('-');

        foreach (var prefix in prefixes)
        {
            var path = Path.Combine(sessionRoot, $"{prefix}-{sanitizedStudyId}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class StudyShellViewHost
    {
        public StudyShellViewHost(
            SussexVisualProfilesWorkspaceViewModel visualProfiles,
            SussexControllerBreathingProfilesWorkspaceViewModel controllerBreathingProfiles)
        {
            VisualProfiles = visualProfiles;
            ControllerBreathingProfiles = controllerBreathingProfiles;
        }

        public SussexVisualProfilesWorkspaceViewModel VisualProfiles { get; }

        public SussexControllerBreathingProfilesWorkspaceViewModel ControllerBreathingProfiles { get; }

        public int SelectedPhaseTabIndex { get; set; }
    }
}
