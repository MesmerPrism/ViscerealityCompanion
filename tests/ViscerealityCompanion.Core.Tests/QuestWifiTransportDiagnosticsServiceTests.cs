using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class QuestWifiTransportDiagnosticsServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_ReturnsSuccess_WhenQuestWifiPathIsReachable()
    {
        var service = new QuestWifiTransportDiagnosticsService(
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
                    Gateways: ["192.168.0.1"],
                    IPv4Cidrs: ["192.168.0.22/24"])
            ],
            pingProbe: (_, _, _, _) => Task.FromResult(new QuestWifiTransportDiagnosticsService.PingProbeResult(
                Attempted: true,
                Reachable: true,
                Attempts: 2,
                SuccessfulReplies: 2,
                AverageLatencyMs: 11.5,
                Error: string.Empty)),
            tcpProbe: (_, _, _, _) => Task.FromResult(new QuestWifiTransportDiagnosticsService.TcpProbeResult(
                Attempted: true,
                Reachable: true,
                ConnectLatencyMs: 24.3,
                Error: string.Empty)),
            utcNow: () => new DateTimeOffset(2026, 04, 16, 12, 0, 0, TimeSpan.Zero));

        var result = await service.AnalyzeAsync(CreateHeadsetStatus());

        Assert.Equal(OperationOutcomeKind.Success, result.Level);
        Assert.Equal("Quest Wi-Fi path is reachable from Windows.", result.Summary);
        Assert.True(result.TcpReachable);
        Assert.True(result.PingReachable);
        Assert.True(result.SelectorMatchesHeadsetIp);
        Assert.True(result.SameSubnet);
        Assert.Contains("SSIDs match", result.Topology, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TCP port 5555", result.Tcp, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_Fails_WhenSameSsidButTcpPortIsBlocked()
    {
        var service = new QuestWifiTransportDiagnosticsService(
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
                    Gateways: ["192.168.0.1"],
                    IPv4Cidrs: ["192.168.0.22/24"])
            ],
            pingProbe: (_, _, _, _) => Task.FromResult(new QuestWifiTransportDiagnosticsService.PingProbeResult(
                Attempted: true,
                Reachable: false,
                Attempts: 2,
                SuccessfulReplies: 0,
                AverageLatencyMs: null,
                Error: string.Empty)),
            tcpProbe: (_, _, _, _) => Task.FromResult(new QuestWifiTransportDiagnosticsService.TcpProbeResult(
                Attempted: true,
                Reachable: false,
                ConnectLatencyMs: null,
                Error: "timed out after 1500 ms")));

        var result = await service.AnalyzeAsync(CreateHeadsetStatus());

        Assert.Equal(OperationOutcomeKind.Failure, result.Level);
        Assert.Contains("cannot reach", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client isolation", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.TcpReachable);
    }

    private static HeadsetAppStatus CreateHeadsetStatus()
        => new(
            IsConnected: true,
            ConnectionLabel: "192.168.0.130:5555",
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
            Summary: "Connected.",
            Detail: "Connected over Wi-Fi ADB.",
            IsWifiAdbTransport: true,
            HeadsetWifiSsid: "SussexLab",
            HeadsetWifiIpAddress: "192.168.0.130",
            HostWifiSsid: "SussexLab",
            WifiSsidMatchesHost: true,
            HostWifiInterfaceName: "Wi-Fi");
}
