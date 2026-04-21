using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class QuestWifiAdbDiagnosticPreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_SkipsBootstrap_WhenQuestIsAlreadyOnWifiAdb()
    {
        var headset = CreateWifiHeadset();
        var service = new QuestWifiAdbDiagnosticPreparationService(
            new StubQuestControlService([headset]));

        var result = await service.PrepareAsync(target: null, initialHeadset: headset);

        Assert.False(result.Attempted);
        Assert.True(result.Succeeded);
        Assert.Same(headset, result.EffectiveHeadset);
        Assert.Equal(string.Empty, result.Guidance);
    }

    [Fact]
    public async Task PrepareAsync_ReportsWifiBlocker_WhenBootstrapAndReconnectDoNotReachWifiAdb()
    {
        var initial = CreateUsbHeadset();
        var stillUsb = initial with
        {
            Timestamp = initial.Timestamp.AddSeconds(1),
            Summary = "Still connected over USB.",
            Detail = "USB ADB stayed active."
        };
        var service = new QuestWifiAdbDiagnosticPreparationService(
            new StubQuestControlService(
                [stillUsb, stillUsb],
                bootstrapOutcome: new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Wi-Fi ADB bootstrap started, but the Quest did not stay on the TCP endpoint.",
                    "Quest reported 192.168.0.55:5555 briefly, but ADB stayed on USB.",
                    Endpoint: "192.168.0.55:5555"),
                reconnectOutcome: new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Connect Quest failed.",
                    "TCP port 5555 did not answer.",
                    Endpoint: "192.168.0.55:5555")));

        var result = await service.PrepareAsync(target: null, initialHeadset: initial);
        var applied = result.ApplyTo(new QuestWifiTransportDiagnosticsResult(
            OperationOutcomeKind.Warning,
            "Quest is not on Wi-Fi ADB yet.",
            "The headset is connected, but the active study transport is not a Wi-Fi ADB endpoint yet.",
            Selector: "Selector 1WMHH000000000 over USB ADB.",
            HeadsetWifi: "Headset Wi-Fi SussexLab (192.168.0.55).",
            HostWifi: "Host Wi-Fi SussexLab via Wi-Fi (192.168.0.22).",
            Topology: "Topology not checked because Wi-Fi ADB is not active.",
            Ping: "Ping not attempted.",
            Tcp: "TCP probe not attempted.",
            TcpReachable: false,
            PingReachable: null,
            SelectorMatchesHeadsetIp: null,
            SameSubnet: null,
            CheckedAtUtc: DateTimeOffset.UtcNow));

        Assert.True(result.Attempted);
        Assert.False(result.Succeeded);
        Assert.Contains("cannot turn green", result.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Connect Quest with 192.168.0.55:5555", result.Guidance, StringComparison.Ordinal);
        Assert.True(applied.BootstrapAttempted);
        Assert.False(applied.BootstrapSucceeded);
        Assert.Contains("Automatic Wi-Fi ADB switch attempt", applied.Detail, StringComparison.Ordinal);
        Assert.Equal("Quest is still not on Wi-Fi ADB, so this diagnostic cannot turn green yet.", applied.Summary);
    }

    [Fact]
    public async Task PrepareAsync_ReportsRecovery_WhenBootstrapReconnectsQuestOntoWifiAdb()
    {
        var initial = CreateUsbHeadset();
        var switched = CreateWifiHeadset();
        var service = new QuestWifiAdbDiagnosticPreparationService(
            new StubQuestControlService(
                [initial, switched],
                bootstrapOutcome: new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Wi-Fi ADB enabled.",
                    "Quest is now listening on 192.168.0.55:5555.",
                    Endpoint: "192.168.0.55:5555")));

        var result = await service.PrepareAsync(target: null, initialHeadset: initial);
        var applied = result.ApplyTo(new QuestWifiTransportDiagnosticsResult(
            OperationOutcomeKind.Success,
            "Quest Wi-Fi path is reachable from Windows.",
            "TCP port 5555 is reachable.",
            Selector: "Selector 192.168.0.55:5555 over Wi-Fi ADB.",
            HeadsetWifi: "Headset Wi-Fi SussexLab (192.168.0.55).",
            HostWifi: "Host Wi-Fi SussexLab via Wi-Fi (192.168.0.22).",
            Topology: "SSIDs match.",
            Ping: "ICMP ping succeeded.",
            Tcp: "TCP port 5555 succeeded.",
            TcpReachable: true,
            PingReachable: true,
            SelectorMatchesHeadsetIp: true,
            SameSubnet: true,
            CheckedAtUtc: DateTimeOffset.UtcNow));

        Assert.True(result.Attempted);
        Assert.True(result.Succeeded);
        Assert.Contains("recovered onto Wi-Fi ADB", result.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.True(applied.BootstrapAttempted);
        Assert.True(applied.BootstrapSucceeded);
        Assert.Contains("Automatic Wi-Fi ADB switch attempt", applied.Detail, StringComparison.Ordinal);
        Assert.Equal("Quest Wi-Fi path is reachable from Windows.", applied.Summary);
    }

    private static HeadsetAppStatus CreateUsbHeadset()
        => new(
            IsConnected: true,
            ConnectionLabel: "1WMHH000000000",
            DeviceModel: "Quest 3",
            BatteryLevel: 92,
            CpuLevel: 4,
            GpuLevel: 4,
            ForegroundPackageId: "com.Viscereality.SussexExperiment",
            IsTargetInstalled: true,
            IsTargetRunning: true,
            IsTargetForeground: true,
            RemoteOnlyControlEnabled: false,
            Timestamp: DateTimeOffset.UtcNow,
            Summary: "Connected over USB.",
            Detail: "USB ADB is active.",
            IsWifiAdbTransport: false,
            HeadsetWifiSsid: "SussexLab",
            HeadsetWifiIpAddress: "192.168.0.55",
            HostWifiSsid: "SussexLab",
            WifiSsidMatchesHost: true);

    private static HeadsetAppStatus CreateWifiHeadset()
        => CreateUsbHeadset() with
        {
            ConnectionLabel = "192.168.0.55:5555",
            Detail = "Connected over Wi-Fi ADB.",
            IsWifiAdbTransport = true,
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(1)
        };

    private sealed class StubQuestControlService : IQuestControlService
    {
        private readonly Queue<HeadsetAppStatus> _headsetStatuses;
        private readonly OperationOutcome _bootstrapOutcome;
        private readonly OperationOutcome _reconnectOutcome;
        private HeadsetAppStatus _lastHeadset;

        public StubQuestControlService(
            IEnumerable<HeadsetAppStatus> headsetStatuses,
            OperationOutcome? bootstrapOutcome = null,
            OperationOutcome? reconnectOutcome = null)
        {
            _headsetStatuses = new Queue<HeadsetAppStatus>(headsetStatuses);
            _lastHeadset = _headsetStatuses.Count > 0 ? _headsetStatuses.Last() : CreateUsbHeadset();
            _bootstrapOutcome = bootstrapOutcome ?? new OperationOutcome(
                OperationOutcomeKind.Success,
                "Wi-Fi ADB enabled.",
                "Quest is now listening on TCP 5555.",
                Endpoint: "192.168.0.55:5555");
            _reconnectOutcome = reconnectOutcome ?? new OperationOutcome(
                OperationOutcomeKind.Success,
                "Connect Quest succeeded.",
                "Connected to 192.168.0.55:5555.",
                Endpoint: "192.168.0.55:5555");
        }

        public Task<OperationOutcome> ProbeUsbAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> DiscoverWifiAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> EnableWifiFromUsbAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_bootstrapOutcome);

        public Task<OperationOutcome> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
            => Task.FromResult(_reconnectOutcome);

        public Task<OperationOutcome> ApplyPerformanceLevelsAsync(int cpuLevel, int gpuLevel, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> InstallAppAsync(QuestAppTarget target, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> InstallBundleAsync(QuestBundle bundle, IReadOnlyList<QuestAppTarget> targets, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> ApplyHotloadProfileAsync(HotloadProfile profile, QuestAppTarget target, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> ClearHotloadOverrideAsync(QuestAppTarget target, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> ApplyDeviceProfileAsync(DeviceProfile profile, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> LaunchAppAsync(QuestAppTarget target, bool kioskMode = false, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> StopAppAsync(QuestAppTarget target, bool exitKioskMode = false, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> OpenBrowserAsync(string url, QuestAppTarget browserTarget, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> QueryForegroundAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<InstalledAppStatus> QueryInstalledAppAsync(QuestAppTarget target, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DeviceProfileStatus> QueryDeviceProfileStatusAsync(DeviceProfile profile, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HeadsetAppStatus> QueryHeadsetStatusAsync(
            QuestAppTarget? target,
            bool remoteOnlyControlEnabled,
            bool includeHostWifiStatus = true,
            CancellationToken cancellationToken = default)
        {
            if (_headsetStatuses.Count > 0)
            {
                _lastHeadset = _headsetStatuses.Dequeue();
            }

            return Task.FromResult(_lastHeadset);
        }

        public Task<OperationOutcome> RunUtilityAsync(
            QuestUtilityAction action,
            bool allowWakeResumeTarget = true,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
