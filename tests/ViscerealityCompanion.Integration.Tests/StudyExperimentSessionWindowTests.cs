using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Integration.Tests;

[Collection("WpfUi")]
public sealed class StudyExperimentSessionWindowTests
{
    private readonly WpfUiFixture fixture;

    public StudyExperimentSessionWindowTests(WpfUiFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ExperimentSessionWindow_ExposesCalibrationButtons_AndPanelDetailExpanders()
    {
        await fixture.InvokeAsync(async () =>
        {
            var app = fixture.Application;
            if (app.Dispatcher.CheckAccess())
            {
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            using var viewModel = new StudyShellViewModel(CreateStudy());
            var window = new StudyExperimentSessionWindow(viewModel)
            {
                Width = 1600,
                Height = 1000,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            window.Show();
            await WaitForConditionAsync(() => window.IsLoaded && window.IsVisible, TimeSpan.FromSeconds(5));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var dynamicButton = Assert.IsType<Button>(window.FindName("StartDynamicAxisCalibrationButton"));
            var fixedButton = Assert.IsType<Button>(window.FindName("StartFixedAxisCalibrationButton"));
            var resetButton = Assert.IsType<Button>(window.FindName("ResetCalibrationButton"));
            Assert.Equal("Start Dynamic-Axis Calibration", dynamicButton.Content);
            Assert.Equal("Start Fixed-Axis Calibration", fixedButton.Content);
            Assert.Equal("Reset Calibration", resetButton.Content);

            Assert.IsType<Expander>(window.FindName("SessionControlDetailExpander"));
            Assert.IsType<Expander>(window.FindName("ClockNetworkDetailExpander"));
            Assert.IsType<Expander>(window.FindName("LatestCommandDetailExpander"));
            Assert.IsType<Expander>(window.FindName("ControllerDetailExpander"));
            Assert.IsType<Expander>(window.FindName("CoherenceDetailExpander"));
            Assert.IsType<Expander>(window.FindName("ParticlesDetailExpander"));
            Assert.IsType<Expander>(window.FindName("RecenterDetailExpander"));
            Assert.IsType<Expander>(window.FindName("QuestScreenshotDetailExpander"));
            Assert.IsType<Expander>(window.FindName("ClockProbeDetailExpander"));

            window.Close();
        });
    }

    private static StudyShellDefinition CreateStudy()
        => new(
            "sussex-experiment-session-test",
            "Sussex Test",
            "Sussex",
            "Experiment session window regression test.",
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
                ExpectedLslStreamName: "HRV_Biofeedback",
                ExpectedLslStreamType: "HRV",
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
                RecenterCommandActionId: "recenter",
                ParticleVisibleOnActionId: "particles.on",
                ParticleVisibleOffActionId: "particles.off",
                StartBreathingCalibrationActionId: "calibration.start",
                ResetBreathingCalibrationActionId: "calibration.reset",
                StartExperimentActionId: "experiment.start",
                EndExperimentActionId: "experiment.end"));

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
}
