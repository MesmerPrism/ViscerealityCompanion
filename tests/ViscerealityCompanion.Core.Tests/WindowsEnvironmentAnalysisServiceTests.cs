using System.Runtime.CompilerServices;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class WindowsEnvironmentAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_ReturnsSuccess_WhenToolingAndExpectedStreamAreReady()
    {
        var monitor = new FakeMonitorService(new LslRuntimeState(true, "Fake monitor runtime ready."));
        monitor.Enqueue(new LslMonitorReading(
            "Streaming LSL sample.",
            "Received a live HRV packet from the expected stream.",
            0.63f,
            10f,
            DateTimeOffset.UtcNow));
        var discovery = new FakeLslStreamDiscoveryService(
            new LslRuntimeState(true, "Fake discovery runtime ready."),
            [
                new LslVisibleStreamInfo("HRV_Biofeedback", "HRV", "external.sender.primary", 1, 10f, 100d)
            ]);

        using var clockAlignment = new FakeClockAlignmentService(new LslRuntimeState(true, "Clock alignment ready."));
        using var testSender = new FakeTestLslSignalService(new LslRuntimeState(true, "TEST sender ready."));
        var bridge = new FakeTwinModeBridge(new TwinBridgeStatus(true, false, "Twin bridge ready.", "Fake twin bridge is publishing."));
        var service = new WindowsEnvironmentAnalysisService(
            monitor,
            discovery,
            clockAlignment,
            testSender,
            bridge,
            toolingStatusProvider: () => CreateToolingStatus(isReady: true),
            adbLocator: () => @"C:\tooling\platform-tools\adb.exe",
            hzdbLocator: () => @"C:\tooling\hzdb\hzdb.exe",
            bundledLslLocator: () => @"C:\tooling\bundled\lsl.dll",
            agentWorkspacePresent: () => false,
            utcNow: () => new DateTimeOffset(2026, 04, 10, 14, 0, 0, TimeSpan.Zero));

        var result = await service.AnalyzeAsync(new WindowsEnvironmentAnalysisRequest("HRV_Biofeedback", "HRV"));

        Assert.Equal(OperationOutcomeKind.Success, result.Level);
        Assert.Equal("Windows environment analysis passed.", result.Summary);
        Assert.Equal("Checks ok/warn/fail: 10/0/0.", result.Detail);
        Assert.Equal(new DateTimeOffset(2026, 04, 10, 14, 0, 0, TimeSpan.Zero), result.CompletedAtUtc);

        var streamCheck = Assert.Single(result.Checks, check => check.Id == "expected-stream");
        Assert.Equal(OperationOutcomeKind.Success, streamCheck.Level);
        Assert.Contains("visible on Windows", streamCheck.Summary, StringComparison.OrdinalIgnoreCase);

        var bundledLslCheck = Assert.Single(result.Checks, check => check.Id == "bundled-liblsl");
        Assert.Equal(OperationOutcomeKind.Success, bundledLslCheck.Level);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsFailuresAndRemediation_WhenWindowsRequirementsAreMissing()
    {
        var monitor = new FakeMonitorService(new LslRuntimeState(true, "Fake monitor runtime ready."));
        monitor.Enqueue(new LslMonitorReading(
            "LSL stream not found.",
            "No matching stream was resolved during the probe window.",
            null,
            0f,
            DateTimeOffset.UtcNow));
        var discovery = new FakeLslStreamDiscoveryService(
            new LslRuntimeState(true, "Fake discovery runtime ready."),
            []);

        using var clockAlignment = new FakeClockAlignmentService(new LslRuntimeState(false, "Clock alignment runtime missing."));
        using var testSender = new FakeTestLslSignalService(new LslRuntimeState(false, "TEST sender runtime missing."));
        var bridge = new FakeTwinModeBridge(new TwinBridgeStatus(false, false, "Twin bridge unavailable.", "quest_twin_state is not being bridged."));
        var service = new WindowsEnvironmentAnalysisService(
            monitor,
            discovery,
            clockAlignment,
            testSender,
            bridge,
            toolingStatusProvider: () => CreateToolingStatus(isReady: false),
            adbLocator: () => null,
            hzdbLocator: () => null,
            bundledLslLocator: () => null,
            agentWorkspacePresent: () => false,
            utcNow: () => new DateTimeOffset(2026, 04, 10, 14, 30, 0, TimeSpan.Zero));

        var result = await service.AnalyzeAsync(new WindowsEnvironmentAnalysisRequest("HRV_Biofeedback", "HRV"));

        Assert.Equal(OperationOutcomeKind.Failure, result.Level);
        Assert.Equal("Windows environment analysis found blocking issues.", result.Summary);
        Assert.Equal("Checks ok/warn/fail: 2/4/4.", result.Detail);

        var adbCheck = Assert.Single(result.Checks, check => check.Id == "adb");
        Assert.Equal(OperationOutcomeKind.Failure, adbCheck.Level);
        Assert.Contains("install-official", adbCheck.Detail, StringComparison.OrdinalIgnoreCase);

        var hzdbCheck = Assert.Single(result.Checks, check => check.Id == "hzdb");
        Assert.Equal(OperationOutcomeKind.Failure, hzdbCheck.Level);
        Assert.Contains("Quest screenshot", hzdbCheck.Detail, StringComparison.Ordinal);

        var cacheCheck = Assert.Single(result.Checks, check => check.Id == "official-tool-cache");
        Assert.Equal(OperationOutcomeKind.Warning, cacheCheck.Level);

        var streamCheck = Assert.Single(result.Checks, check => check.Id == "expected-stream");
        Assert.Equal(OperationOutcomeKind.Warning, streamCheck.Level);
        Assert.Contains("start the upstream sender", streamCheck.Detail, StringComparison.OrdinalIgnoreCase);

        var bridgeCheck = Assert.Single(result.Checks, check => check.Id == "twin-bridge");
        Assert.Equal(OperationOutcomeKind.Failure, bridgeCheck.Level);

        var bundledLslCheck = Assert.Single(result.Checks, check => check.Id == "bundled-liblsl");
        Assert.Equal(OperationOutcomeKind.Warning, bundledLslCheck.Level);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsWarning_WhenMultipleExpectedStreamsAreVisible()
    {
        var monitor = new FakeMonitorService(new LslRuntimeState(true, "Fake monitor runtime ready."));
        var discovery = new FakeLslStreamDiscoveryService(
            new LslRuntimeState(true, "Fake discovery runtime ready."),
            [
                new LslVisibleStreamInfo("HRV_Biofeedback", "HRV", "external.sender.primary", 1, 10f, 100d),
                new LslVisibleStreamInfo("HRV_Biofeedback", "HRV", "viscereality.companion.study-shell.test.sussex", 1, 10f, 101d)
            ]);

        using var clockAlignment = new FakeClockAlignmentService(new LslRuntimeState(true, "Clock alignment ready."));
        using var testSender = new FakeTestLslSignalService(new LslRuntimeState(true, "TEST sender ready."));
        var bridge = new FakeTwinModeBridge(new TwinBridgeStatus(true, false, "Twin bridge ready.", "Fake twin bridge is publishing."));
        var service = new WindowsEnvironmentAnalysisService(
            monitor,
            discovery,
            clockAlignment,
            testSender,
            bridge,
            toolingStatusProvider: () => CreateToolingStatus(isReady: true),
            adbLocator: () => @"C:\tooling\platform-tools\adb.exe",
            hzdbLocator: () => @"C:\tooling\hzdb\hzdb.exe",
            bundledLslLocator: () => @"C:\tooling\bundled\lsl.dll",
            agentWorkspacePresent: () => false,
            utcNow: () => new DateTimeOffset(2026, 04, 10, 14, 45, 0, TimeSpan.Zero));

        var result = await service.AnalyzeAsync(new WindowsEnvironmentAnalysisRequest("HRV_Biofeedback", "HRV"));

        var streamCheck = Assert.Single(result.Checks, check => check.Id == "expected-stream");
        Assert.Equal(OperationOutcomeKind.Warning, streamCheck.Level);
        Assert.Contains("Multiple HRV_Biofeedback / HRV sources are visible", streamCheck.Summary, StringComparison.Ordinal);
        Assert.Contains("sender switching unreliable", streamCheck.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static OfficialQuestToolingStatus CreateToolingStatus(bool isReady)
        => new(
            new OfficialQuestToolStatus(
                "hzdb",
                "Meta hzdb",
                isReady,
                isReady ? "1.0.1" : null,
                "1.0.1",
                !isReady,
                OfficialQuestToolingLayout.HzdbExecutablePath,
                "https://example.invalid/hzdb",
                "Test license",
                "https://example.invalid/license"),
            new OfficialQuestToolStatus(
                "platform-tools",
                "Android platform-tools",
                isReady,
                isReady ? "37.0.0" : null,
                "37.0.0",
                !isReady,
                OfficialQuestToolingLayout.PlatformToolsDirectoryPath,
                "https://example.invalid/platform-tools",
                "Test license",
                "https://example.invalid/license"));

    private sealed class FakeMonitorService(LslRuntimeState runtimeState) : ILslMonitorService
    {
        private readonly Queue<LslMonitorReading> _readings = new();

        public LslRuntimeState RuntimeState { get; } = runtimeState;

        public void Enqueue(LslMonitorReading reading) => _readings.Enqueue(reading);

        public async IAsyncEnumerable<LslMonitorReading> MonitorAsync(
            LslMonitorSubscription subscription,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (_readings.TryDequeue(out var reading))
            {
                yield return reading;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class FakeClockAlignmentService(LslRuntimeState runtimeState) : IStudyClockAlignmentService
    {
        public LslRuntimeState RuntimeState { get; } = runtimeState;

        public Task<OperationOutcome> StartWarmSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Success, "Warm session started.", "test"));

        public Task<OperationOutcome> StopWarmSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Success, "Warm session stopped.", "test"));

        public Task<StudyClockAlignmentRunResult> RunAsync(
            StudyClockAlignmentRunRequest request,
            IProgress<StudyClockAlignmentProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class FakeLslStreamDiscoveryService(
        LslRuntimeState runtimeState,
        IReadOnlyList<LslVisibleStreamInfo> visibleStreams) : ILslStreamDiscoveryService
    {
        public LslRuntimeState RuntimeState { get; } = runtimeState;

        public IReadOnlyList<LslVisibleStreamInfo> Discover(LslStreamDiscoveryRequest request)
        {
            var matches = visibleStreams
                .Where(stream =>
                    (string.IsNullOrWhiteSpace(request.StreamName) || string.Equals(stream.Name, request.StreamName, StringComparison.Ordinal)) &&
                    (string.IsNullOrWhiteSpace(request.StreamType) || string.Equals(stream.Type, request.StreamType, StringComparison.Ordinal)) &&
                    (string.IsNullOrWhiteSpace(request.ExactSourceId) || string.Equals(stream.SourceId, request.ExactSourceId, StringComparison.Ordinal)) &&
                    (string.IsNullOrWhiteSpace(request.SourceIdPrefix) || stream.SourceId.StartsWith(request.SourceIdPrefix, StringComparison.Ordinal)))
                .ToArray();

            return request.PreferNewestFirst
                ? matches.OrderByDescending(stream => stream.CreatedAtSeconds).ToArray()
                : matches.OrderBy(stream => stream.SourceId, StringComparer.Ordinal).ToArray();
        }
    }

    private sealed class FakeTestLslSignalService(LslRuntimeState runtimeState) : ITestLslSignalService
    {
        public event EventHandler? StateChanged
        {
            add { }
            remove { }
        }

        public LslRuntimeState RuntimeState { get; } = runtimeState;
        public bool IsRunning => false;
        public float LastValue => 0f;
        public DateTimeOffset? LastSentAtUtc => null;
        public string LastFaultDetail => string.Empty;

        public OperationOutcome Start(string streamName, string streamType, string sourceId)
            => new(OperationOutcomeKind.Success, "Started.", "test");

        public OperationOutcome Stop()
            => new(OperationOutcomeKind.Success, "Stopped.", "test");

        public void Dispose()
        {
        }
    }

    private sealed class FakeTwinModeBridge(TwinBridgeStatus status) : ITwinModeBridge
    {
        public TwinBridgeStatus Status { get; } = status;

        public Task<OperationOutcome> SendCommandAsync(TwinModeCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Success, "Sent.", "test"));

        public Task<OperationOutcome> ApplyConfigAsync(
            HotloadProfile profile,
            QuestAppTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Success, "Applied.", "test"));

        public Task<OperationOutcome> PublishRuntimeConfigAsync(
            RuntimeConfigProfile profile,
            QuestAppTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationOutcome(OperationOutcomeKind.Success, "Published.", "test"));
    }
}
