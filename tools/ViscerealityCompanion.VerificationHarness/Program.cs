using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
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
        HarnessScenarioRunner.RunOnceFromCurrentDirectory();
    }
}

public static class HarnessScenarioRunner
{
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

        using var outlet = new FloatLslTestOutlet();
        outlet.Open("quest_biofeedback_in", "quest.biofeedback", "viscereality.sussex.harness");

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
                await ExecuteScenarioAsync(window, outputRoot, outlet);
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(Path.Combine(outputRoot, "sussex-study-mode-error.txt"), ex.ToString());
            }
            finally
            {
                window.Close();
                app.Shutdown();
            }
        };

        app.Run(window);
    }

    private static async Task ExecuteScenarioAsync(MainWindow window, string outputRoot, FloatLslTestOutlet outlet)
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

        await studyViewModel.RefreshStatusAsync();
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
        var latencyResults = await MeasureLslRoundTripAsync(outlet, studyViewModel);

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

        CaptureWindow(window, Path.Combine(outputRoot, "sussex-main-window-live.png"));

        await File.WriteAllTextAsync(
            Path.Combine(outputRoot, "sussex-study-mode-report.txt"),
            BuildReport(mainViewModel, studyViewModel, latencyResults, recenterResult, particlesOffResult, particlesHidden, particlesOnResult, particlesVisible));
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

            await Task.Delay(TimeSpan.FromMilliseconds(180));
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
            await Task.Delay(TimeSpan.FromMilliseconds(300));
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

    private static string BuildReport(
        MainWindowViewModel mainViewModel,
        StudyShellViewModel studyViewModel,
        IReadOnlyList<LatencyResult> latencyResults,
        ObservationResult recenterResult,
        ObservationResult particlesOffResult,
        ObservationResult particlesHidden,
        ObservationResult particlesOnResult,
        ObservationResult particlesVisible)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Mode: {mainViewModel.CurrentModeLabel}");
        builder.AppendLine($"Mode detail: {mainViewModel.CurrentModeDetail}");
        builder.AppendLine($"Quest: {studyViewModel.QuestStatusSummary}");
        builder.AppendLine($"Pinned build: {studyViewModel.PinnedBuildSummary}");
        builder.AppendLine($"Installed build: {studyViewModel.InstalledApkSummary}");
        builder.AppendLine($"Device profile: {studyViewModel.DeviceProfileSummary}");
        builder.AppendLine($"Live runtime: {studyViewModel.LiveRuntimeSummary}");
        builder.AppendLine($"LSL: {studyViewModel.LslSummary}");
        builder.AppendLine($"LSL detail: {studyViewModel.LslDetail}");
        builder.AppendLine($"Controller: {studyViewModel.ControllerSummary}");
        builder.AppendLine($"Coherence: {studyViewModel.CoherenceSummary}");
        builder.AppendLine($"Recenter: {studyViewModel.RecenterSummary}");
        builder.AppendLine($"Recenter detail: {studyViewModel.RecenterDetail}");
        builder.AppendLine($"Particles: {studyViewModel.ParticlesSummary}");
        builder.AppendLine($"Particles detail: {studyViewModel.ParticlesDetail}");
        builder.AppendLine($"Last action: {studyViewModel.LastActionLabel}");
        builder.AppendLine();
        builder.AppendLine("Command observations:");
        builder.AppendLine($"- {recenterResult.Label}: {recenterResult.Detail}");
        builder.AppendLine($"- {particlesOffResult.Label}: {particlesOffResult.Detail}");
        builder.AppendLine($"- {particlesHidden.Label}: {particlesHidden.Detail}");
        builder.AppendLine($"- {particlesOnResult.Label}: {particlesOnResult.Detail}");
        builder.AppendLine($"- {particlesVisible.Label}: {particlesVisible.Detail}");
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
            builder.AppendLine("The harness publishes normalized 0..1 bench inlet samples on quest_biofeedback_in / quest.biofeedback from this Windows machine.");
            builder.AppendLine("The current public Sussex telemetry confirmed study.lsl.* inlet connectivity on this pass, but it did not echo that routed inlet value back over quest_twin_state.");
            builder.AppendLine("Value-level round-trip latency therefore still needs a public inlet-value echo or inlet sample timestamp in the runtime state frame.");
        }

        builder.AppendLine();
        builder.AppendLine("Relevant live keys:");
        foreach (var entry in GetRelevantLiveKeys(studyViewModel))
        {
            builder.AppendLine($"- {entry.Key}: {entry.Value}");
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

    private static IReadOnlyList<KeyValuePair<string, string>> GetRelevantLiveKeys(StudyShellViewModel studyViewModel)
    {
        var relevant = new List<KeyValuePair<string, string>>();
        var snapshot = studyViewModel.ReportedTwinStateSnapshot;
        var breathingMirrorBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in snapshot)
        {
            if (entry.Key.StartsWith("driver.stream.", StringComparison.OrdinalIgnoreCase) &&
                entry.Key.EndsWith(".name", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Value, "breathing_lsl", StringComparison.OrdinalIgnoreCase))
            {
                breathingMirrorBases.Add(entry.Key[..^".name".Length]);
            }
        }

        foreach (var entry in snapshot.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (entry.Key.StartsWith("study.lsl", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("study.recenter", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("study.particles", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("connection.lsl", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("signal01.breathing_lsl", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Contains("breathing_lsl", StringComparison.OrdinalIgnoreCase) ||
                breathingMirrorBases.Any(baseKey => entry.Key.StartsWith(baseKey, StringComparison.OrdinalIgnoreCase)))
            {
                relevant.Add(entry);
            }
        }

        return relevant;
    }

    private sealed record LatencyResult(float Value, DateTimeOffset SentAt, DateTimeOffset? ObservedAt, string SourceKey, double? LatencyMs);
    private sealed record ObservedValueResult(DateTimeOffset? Timestamp, string SourceKey);
    private sealed record ObservationResult(string Label, bool Success, string Detail);
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

        _outlet = LslNative.CreateOutlet(_streamInfo, 0, 120);
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
                $"Could not locate lsl.dll for the verification harness. Searched: {string.Join("; ", CandidateLibraryPaths)}");
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
            AddCandidate(candidates, Path.Combine(userProfile, "source", "repos", "AstralKarateDojo", "Assets", "Plugins", "LSL", "Windows", "x64", "lsl.dll"));
            AddCandidate(candidates, Path.Combine(userProfile, "source", "repos", "AstralKarateDojo-phone-monitor-shell", "Assets", "Plugins", "LSL", "Windows", "x64", "lsl.dll"));
            AddCandidate(candidates, Path.Combine(userProfile, "source", "repos", "UnitySixthSense", "Assets", "Plugins", "LSL", "Windows", "x64", "lsl.dll"));

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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

        [DllImport("lsl", EntryPoint = "lsl_local_clock", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_local_clock();

        [DllImport("lsl", EntryPoint = "lsl_push_sample_ftp", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lsl_push_sample_ftp(nint outlet, float[] data, double timestamp, int pushThrough);
    }
}
