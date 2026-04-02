using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
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

    private static MethodInfo GetPrivateMethod(string name)
        => typeof(StudyShellViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new InvalidOperationException($"Could not find {name} on {nameof(StudyShellViewModel)}.");

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"Could not find field {fieldName}.");
        field.SetValue(target, value);
    }

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
}
