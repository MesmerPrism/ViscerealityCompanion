using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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

                var visualOblatenessGroup = FindDescendantByAutomationId<TextBlock>(view, "visual-group-oblateness_by_radius_max")
                    ?? throw new InvalidOperationException("Could not resolve the visual oblateness group text.");
                var visualOblatenessLabel = FindDescendantByAutomationId<TextBlock>(view, "visual-label-oblateness_by_radius_max")
                    ?? throw new InvalidOperationException("Could not resolve the visual oblateness label text.");
                var visualEditor = FindDescendantByAutomationId<TextBox>(view, "visual-current-oblateness_by_radius_max")
                    ?? throw new InvalidOperationException("Could not resolve the visual oblateness editor textbox.");
                var visualStartup = FindDescendantByAutomationId<TextBlock>(view, "visual-startup-oblateness_by_radius_max")
                    ?? throw new InvalidOperationException("Could not resolve the visual oblateness startup textblock.");
                var tracerGroup = FindDescendantByAutomationId<TextBlock>(view, "visual-group-tracers_per_oscillator")
                    ?? throw new InvalidOperationException("Could not resolve the tracer group text.");
                var tracerLabel = FindDescendantByAutomationId<TextBlock>(view, "visual-label-tracers_per_oscillator")
                    ?? throw new InvalidOperationException("Could not resolve the tracer label text.");
                var tracerEditor = FindDescendantByAutomationId<TextBox>(view, "visual-current-tracers_per_oscillator")
                    ?? throw new InvalidOperationException("Could not resolve the tracer editor textbox.");
                var tracerStartup = FindDescendantByAutomationId<TextBlock>(view, "visual-startup-tracers_per_oscillator")
                    ?? throw new InvalidOperationException("Could not resolve the tracer startup textblock.");
                var sphereRadiusGroup = FindDescendantByAutomationId<TextBlock>(view, "visual-group-sphere_radius_max")
                    ?? throw new InvalidOperationException("Could not resolve the sphere radius group text.");
                var sphereRadiusLabel = FindDescendantByAutomationId<TextBlock>(view, "visual-label-sphere_radius_max")
                    ?? throw new InvalidOperationException("Could not resolve the sphere radius label text.");
                var sphereRadiusEditor = FindDescendantByAutomationId<TextBox>(view, "visual-current-sphere_radius_max")
                    ?? throw new InvalidOperationException("Could not resolve the sphere radius editor textbox.");
                var sphereRadiusStartup = FindDescendantByAutomationId<TextBlock>(view, "visual-startup-sphere_radius_max")
                    ?? throw new InvalidOperationException("Could not resolve the sphere radius startup textblock.");
                var sizeModeGroup = FindDescendantByAutomationId<TextBlock>(view, "visual-group-particle_size_relative_to_radius")
                    ?? throw new InvalidOperationException("Could not resolve the size mode group text.");
                var sizeModeLabel = FindDescendantByAutomationId<TextBlock>(view, "visual-label-particle_size_relative_to_radius")
                    ?? throw new InvalidOperationException("Could not resolve the size mode label text.");
                var sizeModeToggle = FindDescendantByAutomationId<CheckBox>(view, "visual-current-particle_size_relative_to_radius")
                    ?? throw new InvalidOperationException("Could not resolve the size mode checkbox.");
                var sizeModeStartup = FindDescendantByAutomationId<TextBlock>(view, "visual-startup-particle_size_relative_to_radius")
                    ?? throw new InvalidOperationException("Could not resolve the size mode startup textblock.");
                var visualSaveSelectedButton = (Button)view.FindName("VisualSaveSelectedProfileButton")!;
                var visualSaveButton = (Button)view.FindName("VisualSaveStartupSnapshotButton")!;
                var visualApplyButton = (Button)view.FindName("VisualApplyCurrentSessionButton")!;

                Assert.Equal("Shape", visualOblatenessGroup.Text);
                Assert.Equal("Oblateness Maximum", visualOblatenessLabel.Text);
                Assert.Equal("0.5", visualStartup.Text);
                Assert.Equal("Tracers", tracerGroup.Text);
                Assert.Equal("Tracers Per Oscillator", tracerLabel.Text);
                Assert.Equal("7", tracerStartup.Text);
                Assert.Equal("Size", sphereRadiusGroup.Text);
                Assert.Equal("Sphere Radius Maximum", sphereRadiusLabel.Text);
                Assert.Equal("2", sphereRadiusStartup.Text);
                Assert.Equal("Size", sizeModeGroup.Text);
                Assert.Equal("Particle Size Relative To Radius", sizeModeLabel.Text);
                Assert.Equal("On", sizeModeStartup.Text);
                Assert.Equal(
                    "0 .. 1",
                    visualWorkspace.ComparisonRows.First(row => string.Equals(row.Field.Id, "oblateness_by_radius_max", StringComparison.OrdinalIgnoreCase)).Range);
                Assert.Equal(
                    "1 .. 16",
                    visualWorkspace.ComparisonRows.First(row => string.Equals(row.Field.Id, "tracers_per_oscillator", StringComparison.OrdinalIgnoreCase)).Range);
                Assert.Equal(
                    "0.5 .. 3",
                    visualWorkspace.ComparisonRows.First(row => string.Equals(row.Field.Id, "sphere_radius_max", StringComparison.OrdinalIgnoreCase)).Range);
                Assert.Equal(
                    "Off / On",
                    visualWorkspace.ComparisonRows.First(row => string.Equals(row.Field.Id, "particle_size_relative_to_radius", StringComparison.OrdinalIgnoreCase)).Range);

                visualEditor.Focus();
                visualEditor.Text = 0.74d.ToString("0.###", CultureInfo.CurrentCulture);
                CommitTextBoxEdit(visualEditor);
                tracerEditor.Focus();
                tracerEditor.Text = "10";
                CommitTextBoxEdit(tracerEditor);
                sphereRadiusEditor.Focus();
                sphereRadiusEditor.Text = 2.4d.ToString("0.###", CultureInfo.CurrentCulture);
                CommitTextBoxEdit(sphereRadiusEditor);
                var sizeModePeer = new CheckBoxAutomationPeer(sizeModeToggle);
                var sizeModeProvider = (IToggleProvider?)sizeModePeer.GetPattern(PatternInterface.Toggle)
                    ?? throw new InvalidOperationException("Could not resolve the size mode toggle provider.");
                sizeModeProvider.Toggle();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                view.UpdateLayout();

                await WaitForConditionAsync(
                    () => visualSaveSelectedButton.IsEnabled,
                    TimeSpan.FromSeconds(10));

                visualSaveSelectedButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, visualSaveSelectedButton));

                await WaitForConditionAsync(
                    () => visualWorkspace.SelectedProfile?.Document.ControlValues.TryGetValue("oblateness_by_radius_max", out var persistedVisualMax) == true &&
                          Math.Abs(persistedVisualMax - 0.74d) < 0.000001d,
                    TimeSpan.FromSeconds(10));
                Assert.False(visualWorkspace.HasUnsavedDraftChanges);

                await WaitForConditionAsync(
                    () => visualSaveButton.IsEnabled,
                    TimeSpan.FromSeconds(10));
                visualSaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, visualSaveButton));

                await WaitForConditionAsync(
                    () =>
                    {
                        var startup = new SussexVisualProfileStartupStateStore(studyId).Load();
                        return startup?.ControlValues is not null &&
                               Math.Abs(startup.ControlValues["oblateness_by_radius_max"] - 0.74d) < 0.000001d &&
                               Math.Abs(startup.ControlValues["tracers_per_oscillator"] - 10d) < 0.000001d &&
                               Math.Abs(startup.ControlValues["sphere_radius_max"] - 2.4d) < 0.000001d &&
                               Math.Abs(startup.ControlValues["particle_size_relative_to_radius"] - 0d) < 0.000001d;
                    },
                    TimeSpan.FromSeconds(10));

                await WaitForConditionAsync(
                    () =>
                    {
                        var visualRow = visualWorkspace.ComparisonRows.FirstOrDefault(row => string.Equals(row.Field.Id, "oblateness_by_radius_max", StringComparison.OrdinalIgnoreCase));
                        var tracerRow = visualWorkspace.ComparisonRows.FirstOrDefault(row => string.Equals(row.Field.Id, "tracers_per_oscillator", StringComparison.OrdinalIgnoreCase));
                        var sphereRadiusRow = visualWorkspace.ComparisonRows.FirstOrDefault(row => string.Equals(row.Field.Id, "sphere_radius_max", StringComparison.OrdinalIgnoreCase));
                        var sizeModeRow = visualWorkspace.ComparisonRows.FirstOrDefault(row => string.Equals(row.Field.Id, "particle_size_relative_to_radius", StringComparison.OrdinalIgnoreCase));
                        return string.Equals(visualRow?.Startup, "0.74", StringComparison.Ordinal) &&
                               string.Equals(tracerRow?.Startup, "10", StringComparison.Ordinal) &&
                               string.Equals(sphereRadiusRow?.Startup, "2.4", StringComparison.Ordinal) &&
                               string.Equals(sizeModeRow?.Startup, "Off", StringComparison.Ordinal);
                    },
                    TimeSpan.FromSeconds(10));

                visualStartup = FindDescendantByAutomationId<TextBlock>(view, "visual-startup-oblateness_by_radius_max")
                    ?? throw new InvalidOperationException("Could not resolve the refreshed visual oblateness startup textblock.");
                tracerStartup = FindDescendantByAutomationId<TextBlock>(view, "visual-startup-tracers_per_oscillator")
                    ?? throw new InvalidOperationException("Could not resolve the refreshed tracer startup textblock.");
                sphereRadiusStartup = FindDescendantByAutomationId<TextBlock>(view, "visual-startup-sphere_radius_max")
                    ?? throw new InvalidOperationException("Could not resolve the refreshed sphere radius startup textblock.");
                sizeModeStartup = FindDescendantByAutomationId<TextBlock>(view, "visual-startup-particle_size_relative_to_radius")
                    ?? throw new InvalidOperationException("Could not resolve the refreshed size mode startup textblock.");

                await WaitForConditionAsync(
                    () => string.Equals(visualStartup.Text, "0.74", StringComparison.Ordinal) &&
                          string.Equals(tracerStartup.Text, "10", StringComparison.Ordinal) &&
                          string.Equals(sphereRadiusStartup.Text, "2.4", StringComparison.Ordinal) &&
                          string.Equals(sizeModeStartup.Text, "Off", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(10));

                var visualStartupState = new SussexVisualProfileStartupStateStore(studyId).Load();
                Assert.NotNull(visualStartupState);
                Assert.NotNull(visualStartupState!.ControlValues);
                Assert.Equal(0.74d, visualStartupState.ControlValues!["oblateness_by_radius_max"], 6);
                Assert.Equal(10d, visualStartupState.ControlValues!["tracers_per_oscillator"], 6);
                Assert.Equal(2.4d, visualStartupState.ControlValues!["sphere_radius_max"], 6);
                Assert.Equal(0d, visualStartupState.ControlValues!["particle_size_relative_to_radius"], 6);

                visualEditor.Focus();
                visualEditor.Text = 0.81d.ToString("0.###", CultureInfo.CurrentCulture);
                CommitTextBoxEdit(visualEditor);
                view.UpdateLayout();
                await WaitForConditionAsync(
                    () => visualApplyButton.IsEnabled,
                    TimeSpan.FromSeconds(10));
                visualApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, visualApplyButton));

                await WaitForConditionAsync(
                    () => string.Equals(tracerStartup.Text, "10", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(10));
                visualStartupState = new SussexVisualProfileStartupStateStore(studyId).Load();
                Assert.NotNull(visualStartupState);
                Assert.Equal(0.74d, visualStartupState!.ControlValues!["oblateness_by_radius_max"], 6);
                Assert.True(
                    visualWorkspace.SelectedProfile?.Document.ControlValues.TryGetValue("oblateness_by_radius_max", out var persistedSavedValue) == true &&
                    Math.Abs(persistedSavedValue - 0.74d) < 0.000001d);

                tabs.SelectedIndex = 4;
                view.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var controllerStartup = FindDescendantByAutomationId<TextBlock>(view, "controller-startup-median_window")
                    ?? throw new InvalidOperationException("Could not resolve the controller startup textblock.");
                var controllerSaveButton = (Button)view.FindName("ControllerSaveStartupSnapshotButton")!;
                var controllerApplyButton = (Button)view.FindName("ControllerApplyCurrentSessionButton")!;
                var controllerField = controllerWorkspace.Groups
                    .SelectMany(group => group.Fields)
                    .FirstOrDefault(field => string.Equals(field.Id, "median_window", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Could not resolve the controller median-window field.");

                controllerField.Value = 7d;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                view.UpdateLayout();
                await WaitForConditionAsync(
                    () => controllerSaveButton.IsEnabled,
                    TimeSpan.FromSeconds(10));
                controllerSaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, controllerSaveButton));

                await WaitForConditionAsync(
                    () => string.Equals(controllerStartup.Text, "7", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(10));

                var controllerStartupState = new SussexControllerBreathingProfileStartupStateStore(studyId).Load();
                Assert.NotNull(controllerStartupState);
                Assert.NotNull(controllerStartupState!.ControlValues);
                Assert.Equal(7d, controllerStartupState.ControlValues!["median_window"], 6);

                controllerField.Value = 11d;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                view.UpdateLayout();
                await WaitForConditionAsync(
                    () => controllerApplyButton.IsEnabled,
                    TimeSpan.FromSeconds(10));
                controllerApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, controllerApplyButton));

                await WaitForConditionAsync(
                    () => string.Equals(controllerStartup.Text, "7", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(10));
                controllerStartupState = new SussexControllerBreathingProfileStartupStateStore(studyId).Load();
                Assert.NotNull(controllerStartupState);
                Assert.Equal(7d, controllerStartupState!.ControlValues!["median_window"], 6);

                tabs.SelectedIndex = 3;
                view.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var visualTable = (DataGrid)view.FindName("VisualProfilesTable")!;
                var visualGroup = FindDescendantByAutomationId<TextBlock>(view, "visual-group-sphere_deformation_enabled")
                    ?? throw new InvalidOperationException("Could not resolve the visual group text.");
                var visualLabel = FindDescendantByAutomationId<TextBlock>(view, "visual-label-sphere_deformation_enabled")
                    ?? throw new InvalidOperationException("Could not resolve the visual label text.");
                var visualStartupLabel = FindDescendantByAutomationId<TextBlock>(view, "visual-startup-sphere_deformation_enabled")
                    ?? throw new InvalidOperationException("Could not resolve the visual startup text.");
                var visualToggle = FindDescendantByAutomationId<CheckBox>(view, "visual-current-sphere_deformation_enabled")
                    ?? throw new InvalidOperationException("Could not resolve the visual boolean checkbox.");
                var savedLaunchHeader = FindColumnHeader(visualTable, "Saved Launch Override")
                    ?? throw new InvalidOperationException("Could not resolve the Saved Launch Override header.");
                var expectedHeaderBackground = Assert.IsType<SolidColorBrush>(view.FindResource("SurfaceTintBrush"));
                var expectedHeaderForeground = Assert.IsType<SolidColorBrush>(view.FindResource("InkBrush"));

                Assert.Equal(HorizontalAlignment.Center, savedLaunchHeader.HorizontalContentAlignment);
                Assert.Equal(TextAlignment.Center, visualGroup.TextAlignment);
                Assert.Equal(HorizontalAlignment.Stretch, visualGroup.HorizontalAlignment);
                Assert.Equal(TextAlignment.Center, visualLabel.TextAlignment);
                Assert.Equal(HorizontalAlignment.Stretch, visualLabel.HorizontalAlignment);
                Assert.Equal(TextAlignment.Center, visualStartupLabel.TextAlignment);
                Assert.Equal(expectedHeaderBackground.Color, Assert.IsType<SolidColorBrush>(savedLaunchHeader.Background).Color);
                Assert.Equal(expectedHeaderForeground.Color, Assert.IsType<SolidColorBrush>(savedLaunchHeader.Foreground).Color);

                var togglePeer = new CheckBoxAutomationPeer(visualToggle);
                var toggleProvider = (IToggleProvider?)togglePeer.GetPattern(PatternInterface.Toggle)
                    ?? throw new InvalidOperationException("Could not resolve the checkbox toggle provider.");
                toggleProvider.Toggle();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                visualWorkspace.RefreshReportedTwinState(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

                await WaitForConditionAsync(
                    () =>
                    {
                        var row = visualWorkspace.ComparisonRows.FirstOrDefault(row =>
                            string.Equals(row.Field.Id, "sphere_deformation_enabled", StringComparison.OrdinalIgnoreCase));
                        return row is not null &&
                               row.CurrentBoolValue is false &&
                               visualToggle.IsChecked is false;
                    },
                    TimeSpan.FromSeconds(5));

                Assert.True(
                    visualWorkspace.SelectedProfile?.Document.ControlValues.TryGetValue("sphere_deformation_enabled", out var persistedValue) == true &&
                    persistedValue >= 0.5d);

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
                        if (Application.Current is Application currentApplication &&
                            currentApplication.Dispatcher == Dispatcher.CurrentDispatcher)
                        {
                            currentApplication.Shutdown();
                        }

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

    private static DataGridColumnHeader? FindColumnHeader(DataGrid grid, string headerText)
        => FindDescendants<DataGridColumnHeader>(grid)
            .FirstOrDefault(header => string.Equals(header.Content?.ToString(), headerText, StringComparison.Ordinal));

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T typedRoot)
        {
            yield return typedRoot;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            foreach (var descendant in FindDescendants<T>(VisualTreeHelper.GetChild(root, index)))
            {
                yield return descendant;
            }
        }
    }

    private static void DeleteFileIfPresent(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void CommitTextBoxEdit(TextBox textBox)
        => BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateSource();

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
