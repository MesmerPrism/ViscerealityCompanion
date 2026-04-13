using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

[Collection("WpfUi")]
public sealed class StudyShellMachineLslStatePanelTests
{
    private readonly WpfUiFixture fixture;

    public StudyShellMachineLslStatePanelTests(WpfUiFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Study_shell_pre_session_tab_exposes_machine_lsl_state_panel()
    {
        var studyId = $"study-shell-lsl-panel-{Guid.NewGuid():N}";

        await fixture.InvokeAsync(async () =>
        {
            var app = fixture.Application;
            if (app.Dispatcher.CheckAccess())
            {
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            using var visualWorkspace = new SussexVisualProfilesWorkspaceViewModel(CreateStudy(studyId), new PreviewQuestControlService());
            using var controllerWorkspace = new SussexControllerBreathingProfilesWorkspaceViewModel(CreateStudy(studyId), new PreviewQuestControlService());
            await visualWorkspace.InitializeAsync();
            await controllerWorkspace.InitializeAsync();

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

            try
            {
                window.Show();
                await WaitForConditionAsync(() => view.IsLoaded && window.IsVisible, TimeSpan.FromSeconds(5));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var tabs = (TabControl)view.FindName("StudyPhaseTabs")!;
                tabs.SelectedIndex = 1;
                view.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var refreshButton = (Button?)view.FindName("RefreshMachineLslStateButton");
                Assert.NotNull(refreshButton);
                Assert.Equal("Refresh Machine LSL State", refreshButton!.Content);
            }
            finally
            {
                window.Close();
            }
        });
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

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(25);
        }

        throw new TimeoutException("Timed out waiting for the WPF view to load.");
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
