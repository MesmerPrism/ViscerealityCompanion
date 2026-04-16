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
            loopbackOutletFactory: CreateLoopbackOutletFactory(),
            networkAdapterSnapshotProvider: () => SinglePhysicalAdapter(),
            utcNow: () => new DateTimeOffset(2026, 04, 10, 14, 0, 0, TimeSpan.Zero));

        var result = await service.AnalyzeAsync(new WindowsEnvironmentAnalysisRequest("HRV_Biofeedback", "HRV"));

        Assert.Equal(OperationOutcomeKind.Success, result.Level);
        Assert.Equal("Windows environment analysis passed.", result.Summary);
        Assert.Equal("Checks ok/warn/fail: 13/0/0.", result.Detail);
        Assert.Equal(new DateTimeOffset(2026, 04, 10, 14, 0, 0, TimeSpan.Zero), result.CompletedAtUtc);

        var streamCheck = Assert.Single(result.Checks, check => check.Id == "expected-stream");
        Assert.Equal(OperationOutcomeKind.Success, streamCheck.Level);
        Assert.Contains("visible on Windows", streamCheck.Summary, StringComparison.OrdinalIgnoreCase);

        var bundledLslCheck = Assert.Single(result.Checks, check => check.Id == "bundled-liblsl");
        Assert.Equal(OperationOutcomeKind.Success, bundledLslCheck.Level);

        var adapterCheck = Assert.Single(result.Checks, check => check.Id == "network-adapters");
        Assert.Equal(OperationOutcomeKind.Success, adapterCheck.Level);

        var discoveryHealthCheck = Assert.Single(result.Checks, check => check.Id == "lsl-discovery-health");
        Assert.Equal(OperationOutcomeKind.Success, discoveryHealthCheck.Level);

        var loopbackCheck = Assert.Single(result.Checks, check => check.Id == "lsl-loopback-outlet");
        Assert.Equal(OperationOutcomeKind.Success, loopbackCheck.Level);
        Assert.Contains("rediscover", loopbackCheck.Summary, StringComparison.OrdinalIgnoreCase);
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
            loopbackOutletFactory: CreateLoopbackOutletFactory(),
            networkAdapterSnapshotProvider: () => SinglePhysicalAdapter(),
            utcNow: () => new DateTimeOffset(2026, 04, 10, 14, 30, 0, TimeSpan.Zero));

        var result = await service.AnalyzeAsync(new WindowsEnvironmentAnalysisRequest("HRV_Biofeedback", "HRV"));

        Assert.Equal(OperationOutcomeKind.Failure, result.Level);
        Assert.Equal("Windows environment analysis found blocking issues.", result.Summary);
        Assert.Equal("Checks ok/warn/fail: 5/4/4.", result.Detail);

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
            loopbackOutletFactory: CreateLoopbackOutletFactory(),
            networkAdapterSnapshotProvider: () => SinglePhysicalAdapter(),
            utcNow: () => new DateTimeOffset(2026, 04, 10, 14, 45, 0, TimeSpan.Zero));

        var result = await service.AnalyzeAsync(new WindowsEnvironmentAnalysisRequest("HRV_Biofeedback", "HRV"));

        var streamCheck = Assert.Single(result.Checks, check => check.Id == "expected-stream");
        Assert.Equal(OperationOutcomeKind.Warning, streamCheck.Level);
        Assert.Contains("Multiple HRV_Biofeedback / HRV sources are visible", streamCheck.Summary, StringComparison.Ordinal);
        Assert.Contains("sender switching unreliable", streamCheck.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_Warns_WhenNetworkAdaptersCanDestabilizeLslDiscovery()
    {
        var monitor = new FakeMonitorService(new LslRuntimeState(true, "Fake monitor runtime ready."));
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
            loopbackOutletFactory: CreateLoopbackOutletFactory(),
            networkAdapterSnapshotProvider: () =>
            [
                new WindowsNetworkAdapterSnapshot(
                    "Wi-Fi",
                    "Intel Wi-Fi",
                    "Wireless80211",
                    IsUp: true,
                    IsLoopback: false,
                    IsTunnel: false,
                    SupportsMulticast: true,
                    IPv4Addresses: ["192.168.0.22"],
                    Gateways: ["192.168.0.1"]),
                new WindowsNetworkAdapterSnapshot(
                    "Tailscale",
                    "Tailscale Tunnel",
                    "Tunnel",
                    IsUp: true,
                    IsLoopback: false,
                    IsTunnel: true,
                    SupportsMulticast: false,
                    IPv4Addresses: ["100.101.102.103"],
                    Gateways: [])
            ]);

        var result = await service.AnalyzeAsync(new WindowsEnvironmentAnalysisRequest("HRV_Biofeedback", "HRV"));

        Assert.Equal(OperationOutcomeKind.Warning, result.Level);
        var adapterCheck = Assert.Single(result.Checks, check => check.Id == "network-adapters");
        Assert.Equal(OperationOutcomeKind.Warning, adapterCheck.Level);
        Assert.Contains("VPN or virtual adapters", adapterCheck.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("multiple active IPv4 adapters", adapterCheck.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("multicast", adapterCheck.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_IncludesQuestWifiTransportCheck_WhenHeadsetContextIsProvided()
    {
        var monitor = new FakeMonitorService(new LslRuntimeState(true, "Fake monitor runtime ready."));
        var discovery = new FakeLslStreamDiscoveryService(
            new LslRuntimeState(true, "Fake discovery runtime ready."),
            [
                new LslVisibleStreamInfo("HRV_Biofeedback", "HRV", "external.sender.primary", 1, 10f, 100d)
            ]);

        using var clockAlignment = new FakeClockAlignmentService(new LslRuntimeState(true, "Clock alignment ready."));
        using var testSender = new FakeTestLslSignalService(new LslRuntimeState(true, "TEST sender ready."));
        var bridge = new FakeTwinModeBridge(new TwinBridgeStatus(true, false, "Twin bridge ready.", "Fake twin bridge is publishing."));
        var wifiDiagnostics = new QuestWifiTransportDiagnosticsService(
            networkAdapterSnapshotProvider: () => SinglePhysicalAdapter(),
            pingProbe: (_, _, _, _) => Task.FromResult(new QuestWifiTransportDiagnosticsService.PingProbeResult(true, true, 2, 2, 8.5, string.Empty)),
            tcpProbe: (_, _, _, _) => Task.FromResult(new QuestWifiTransportDiagnosticsService.TcpProbeResult(true, true, 21.2, string.Empty)),
            utcNow: () => new DateTimeOffset(2026, 04, 10, 15, 0, 0, TimeSpan.Zero));
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
            loopbackOutletFactory: CreateLoopbackOutletFactory(),
            questWifiTransportDiagnosticsService: wifiDiagnostics,
            networkAdapterSnapshotProvider: () => SinglePhysicalAdapter(),
            utcNow: () => new DateTimeOffset(2026, 04, 10, 15, 0, 0, TimeSpan.Zero));

        var result = await service.AnalyzeAsync(new WindowsEnvironmentAnalysisRequest(
            "HRV_Biofeedback",
            "HRV",
            QuestWifiTransport: new QuestWifiTransportDiagnosticsContext(
                CreateConnectedHeadsetStatus(),
                "192.168.0.55:5555")));

        var wifiCheck = Assert.Single(result.Checks, check => check.Id == "quest-wifi-transport");
        Assert.Equal(OperationOutcomeKind.Success, wifiCheck.Level);
        Assert.Contains("reachable from Windows", wifiCheck.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TCP port 5555", wifiCheck.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_FailsDiscoverySelfCheck_WhenLiblslResolverThrowsSocketError()
    {
        var monitor = new FakeMonitorService(new LslRuntimeState(true, "Fake monitor runtime ready."));
        var discovery = new FakeLslStreamDiscoveryService(
            new LslRuntimeState(true, "Fake discovery runtime ready."),
            [],
            new InvalidOperationException("Could not resolve visible LSL streams (internal error): set_option: The requested address is not valid in its context."));

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
            loopbackOutletFactory: CreateLoopbackOutletFactory(),
            networkAdapterSnapshotProvider: () => SinglePhysicalAdapter());

        var result = await service.AnalyzeAsync(new WindowsEnvironmentAnalysisRequest("HRV_Biofeedback", "HRV"));

        Assert.Equal(OperationOutcomeKind.Failure, result.Level);
        var discoveryHealthCheck = Assert.Single(result.Checks, check => check.Id == "lsl-discovery-health");
        Assert.Equal(OperationOutcomeKind.Failure, discoveryHealthCheck.Level);
        Assert.Contains("Windows socket/adapter", discoveryHealthCheck.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VPN/virtual adapters", discoveryHealthCheck.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_FailsLoopback_WhenTemporaryOutletCannotBeRediscovered()
    {
        var monitor = new FakeMonitorService(new LslRuntimeState(true, "Fake monitor runtime ready."));
        var discovery = new FakeLslStreamDiscoveryService(
            new LslRuntimeState(true, "Fake discovery runtime ready."),
            [
                new LslVisibleStreamInfo("HRV_Biofeedback", "HRV", "external.sender.primary", 1, 10f, 100d)
            ],
            includeLoopback: false);

        using var clockAlignment = new FakeClockAlignmentService(new LslRuntimeState(true, "Clock alignment ready."));
        using var testSender = new FakeTestLslSignalService(new LslRuntimeState(true, "TEST sender ready."), isRunning: true);
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
            loopbackOutletFactory: CreateLoopbackOutletFactory(),
            networkAdapterSnapshotProvider: () => SinglePhysicalAdapter());

        var result = await service.AnalyzeAsync(new WindowsEnvironmentAnalysisRequest("HRV_Biofeedback", "HRV"));

        Assert.Equal(OperationOutcomeKind.Failure, result.Level);
        var loopbackCheck = Assert.Single(result.Checks, check => check.Id == "lsl-loopback-outlet");
        Assert.Equal(OperationOutcomeKind.Failure, loopbackCheck.Level);
        Assert.Contains("temporary LSL outlet", loopbackCheck.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TEST sender currently reports active", loopbackCheck.Detail, StringComparison.Ordinal);
        Assert.Contains("Windows LSL advertisement/discovery problem", loopbackCheck.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_ExplainsActiveTestSender_WhenExpectedStreamIsMissing()
    {
        var monitor = new FakeMonitorService(new LslRuntimeState(true, "Fake monitor runtime ready."));
        var discovery = new FakeLslStreamDiscoveryService(
            new LslRuntimeState(true, "Fake discovery runtime ready."),
            []);

        using var clockAlignment = new FakeClockAlignmentService(new LslRuntimeState(true, "Clock alignment ready."));
        using var testSender = new FakeTestLslSignalService(new LslRuntimeState(true, "TEST sender ready."), isRunning: true);
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
            loopbackOutletFactory: CreateLoopbackOutletFactory(),
            networkAdapterSnapshotProvider: () => SinglePhysicalAdapter());

        var result = await service.AnalyzeAsync(new WindowsEnvironmentAnalysisRequest("HRV_Biofeedback", "HRV"));

        Assert.Equal(OperationOutcomeKind.Warning, result.Level);
        var loopbackCheck = Assert.Single(result.Checks, check => check.Id == "lsl-loopback-outlet");
        Assert.Equal(OperationOutcomeKind.Success, loopbackCheck.Level);

        var streamCheck = Assert.Single(result.Checks, check => check.Id == "expected-stream");
        Assert.Equal(OperationOutcomeKind.Warning, streamCheck.Level);
        Assert.Contains("TEST sender reports active", streamCheck.Detail, StringComparison.Ordinal);
        Assert.Contains("loopback outlet self-check", streamCheck.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adapter/firewall", streamCheck.Detail, StringComparison.OrdinalIgnoreCase);
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

    private static IReadOnlyList<WindowsNetworkAdapterSnapshot> SinglePhysicalAdapter()
        =>
        [
            new WindowsNetworkAdapterSnapshot(
                "Wi-Fi",
                "Intel Wi-Fi",
                "Wireless80211",
                IsUp: true,
                IsLoopback: false,
                IsTunnel: false,
                SupportsMulticast: true,
                IPv4Addresses: ["192.168.0.22"],
                Gateways: ["192.168.0.1"])
        ];

    private static HeadsetAppStatus CreateConnectedHeadsetStatus()
        => new(
            IsConnected: true,
            ConnectionLabel: "192.168.0.55:5555",
            DeviceModel: "Quest 3",
            BatteryLevel: 90,
            CpuLevel: 4,
            GpuLevel: 4,
            ForegroundPackageId: "com.Viscereality.SussexExperiment",
            IsTargetInstalled: true,
            IsTargetRunning: true,
            IsTargetForeground: true,
            RemoteOnlyControlEnabled: false,
            Timestamp: DateTimeOffset.UtcNow,
            Summary: "Connected.",
            Detail: "Connected over Wi-Fi ADB.",
            IsWifiAdbTransport: true,
            HeadsetWifiSsid: "SussexLab",
            HeadsetWifiIpAddress: "192.168.0.55",
            HostWifiSsid: "SussexLab",
            WifiSsidMatchesHost: true,
            HostWifiInterfaceName: "Wi-Fi");

    private static Func<ILslOutletService> CreateLoopbackOutletFactory()
        => () => new FakeLslOutletService(new LslRuntimeState(true, "Fake outlet runtime ready."));

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
        IReadOnlyList<LslVisibleStreamInfo> visibleStreams,
        Exception? exception = null,
        bool includeLoopback = true) : ILslStreamDiscoveryService
    {
        public LslRuntimeState RuntimeState { get; } = runtimeState;

        public IReadOnlyList<LslVisibleStreamInfo> Discover(LslStreamDiscoveryRequest request)
        {
            if (exception is not null)
            {
                throw exception;
            }

            if (includeLoopback &&
                request.StreamName?.StartsWith("viscereality_lsl_loopback_self_check_", StringComparison.Ordinal) == true)
            {
                return
                [
                    new LslVisibleStreamInfo(
                        request.StreamName,
                        request.StreamType ?? "viscereality.loopback",
                        string.IsNullOrWhiteSpace(request.ExactSourceId) ? "viscereality.companion.test.loopback" : request.ExactSourceId,
                        1,
                        0f,
                        200d)
                ];
            }

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

    private sealed class FakeLslOutletService(LslRuntimeState runtimeState) : ILslOutletService
    {
        public LslRuntimeState RuntimeState { get; } = runtimeState;
        public bool IsOpen { get; private set; }

        public OperationOutcome Open(string streamName, string streamType, int channelCount)
        {
            if (!RuntimeState.Available)
            {
                return new OperationOutcome(OperationOutcomeKind.Failure, "Outlet unavailable.", RuntimeState.Detail);
            }

            IsOpen = true;
            return new OperationOutcome(OperationOutcomeKind.Success, "Outlet opened.", "test");
        }

        public void Close() => IsOpen = false;

        public void PushSample(string[] values)
        {
        }

        public OperationOutcome PublishConfigSnapshot(IReadOnlyList<RuntimeConfigEntry> entries)
            => new(OperationOutcomeKind.Success, "Published config.", "test");

        public OperationOutcome PublishCommand(TwinModeCommand command, int sequence)
            => new(OperationOutcomeKind.Success, "Published command.", "test");

        public void Dispose() => Close();
    }

    private sealed class FakeTestLslSignalService(
        LslRuntimeState runtimeState,
        bool isRunning = false,
        string lastFaultDetail = "") : ITestLslSignalService
    {
        public event EventHandler? StateChanged
        {
            add { }
            remove { }
        }

        public LslRuntimeState RuntimeState { get; } = runtimeState;
        public bool IsRunning { get; } = isRunning;
        public float LastValue => 0f;
        public DateTimeOffset? LastSentAtUtc => null;
        public string LastFaultDetail { get; } = lastFaultDetail;

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
