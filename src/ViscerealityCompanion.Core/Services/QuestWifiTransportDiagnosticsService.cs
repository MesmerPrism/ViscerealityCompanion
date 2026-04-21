using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed record QuestWifiTransportDiagnosticsResult(
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    string Selector,
    string HeadsetWifi,
    string HostWifi,
    string Topology,
    string Ping,
    string Tcp,
    bool TcpReachable,
    bool? PingReachable,
    bool? SelectorMatchesHeadsetIp,
    bool? SameSubnet,
    DateTimeOffset CheckedAtUtc,
    bool RoutedTopologyAccepted = false,
    bool BootstrapAttempted = false,
    bool BootstrapSucceeded = false,
    string Bootstrap = "");

public sealed class QuestWifiTransportDiagnosticsService
{
    private const int DefaultPingAttempts = 2;
    private const int DefaultPingTimeoutMs = 1200;
    private const int DefaultTcpTimeoutMs = 1500;

    private readonly Func<IReadOnlyList<WindowsNetworkAdapterSnapshot>> _networkAdapterSnapshotProvider;
    private readonly Func<string, int, int, CancellationToken, Task<PingProbeResult>> _pingProbe;
    private readonly Func<string, int, int, CancellationToken, Task<TcpProbeResult>> _tcpProbe;
    private readonly Func<DateTimeOffset> _utcNow;

    public QuestWifiTransportDiagnosticsService(
        Func<IReadOnlyList<WindowsNetworkAdapterSnapshot>>? networkAdapterSnapshotProvider = null,
        Func<string, int, int, CancellationToken, Task<PingProbeResult>>? pingProbe = null,
        Func<string, int, int, CancellationToken, Task<TcpProbeResult>>? tcpProbe = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _networkAdapterSnapshotProvider = networkAdapterSnapshotProvider ?? WindowsEnvironmentAnalysisService.SnapshotNetworkAdapters;
        _pingProbe = pingProbe ?? ProbePingAsync;
        _tcpProbe = tcpProbe ?? ProbeTcpAsync;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<QuestWifiTransportDiagnosticsResult> AnalyzeAsync(
        HeadsetAppStatus headset,
        string? requestedSelector = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(headset);

        var checkedAtUtc = _utcNow();
        var selector = string.IsNullOrWhiteSpace(requestedSelector)
            ? headset.ConnectionLabel
            : requestedSelector.Trim();

        if (!OperatingSystem.IsWindows())
        {
            return new QuestWifiTransportDiagnosticsResult(
                OperationOutcomeKind.Preview,
                "Quest Wi-Fi transport path is only diagnosed on Windows.",
                "The current Sussex operator transport diagnostics only ship on Windows.",
                Selector: RenderSelector(selector),
                HeadsetWifi: RenderHeadsetWifi(headset),
                HostWifi: RenderHostWifi(headset, null),
                Topology: "Topology n/a.",
                Ping: "Ping not attempted.",
                Tcp: "TCP probe not attempted.",
                TcpReachable: false,
                PingReachable: null,
                SelectorMatchesHeadsetIp: null,
                SameSubnet: null,
                CheckedAtUtc: checkedAtUtc);
        }

        if (!headset.IsConnected)
        {
            return new QuestWifiTransportDiagnosticsResult(
                OperationOutcomeKind.Preview,
                "Quest Wi-Fi transport path has not been checked yet.",
                "Connect the headset first. Once the study path is on Wi-Fi ADB, this check will probe whether Windows can actually reach the Quest endpoint over the current router path.",
                Selector: RenderSelector(selector),
                HeadsetWifi: RenderHeadsetWifi(headset),
                HostWifi: RenderHostWifi(headset, null),
                Topology: "Topology n/a.",
                Ping: "Ping not attempted.",
                Tcp: "TCP probe not attempted.",
                TcpReachable: false,
                PingReachable: null,
                SelectorMatchesHeadsetIp: null,
                SameSubnet: null,
                CheckedAtUtc: checkedAtUtc);
        }

        if (!headset.IsWifiAdbTransport)
        {
            return new QuestWifiTransportDiagnosticsResult(
                OperationOutcomeKind.Warning,
                "Quest is not on Wi-Fi ADB yet.",
                "The headset is connected, but the active study transport is not a Wi-Fi ADB endpoint yet. Switch to Wi-Fi ADB before using router/client-isolation diagnostics.",
                Selector: RenderSelector(selector),
                HeadsetWifi: RenderHeadsetWifi(headset),
                HostWifi: RenderHostWifi(headset, null),
                Topology: "Topology not checked because Wi-Fi ADB is not active.",
                Ping: "Ping not attempted.",
                Tcp: "TCP probe not attempted.",
                TcpReachable: false,
                PingReachable: null,
                SelectorMatchesHeadsetIp: null,
                SameSubnet: null,
                CheckedAtUtc: checkedAtUtc);
        }

        var headsetIp = !string.IsNullOrWhiteSpace(headset.HeadsetWifiIpAddress)
            ? headset.HeadsetWifiIpAddress.Trim()
            : ExtractIpAddressFromSelector(selector);
        if (!IPAddress.TryParse(headsetIp, out var headsetIpAddress))
        {
            return new QuestWifiTransportDiagnosticsResult(
                OperationOutcomeKind.Warning,
                "Quest Wi-Fi IP is unknown.",
                "The headset is on Wi-Fi ADB, but the current snapshot did not yield a usable Quest Wi-Fi IPv4 address. Refresh the headset snapshot, then rerun the transport diagnostics.",
                Selector: RenderSelector(selector),
                HeadsetWifi: RenderHeadsetWifi(headset),
                HostWifi: RenderHostWifi(headset, null),
                Topology: "Topology n/a because the Quest IP is unavailable.",
                Ping: "Ping not attempted.",
                Tcp: "TCP probe not attempted.",
                TcpReachable: false,
                PingReachable: null,
                SelectorMatchesHeadsetIp: null,
                SameSubnet: null,
                CheckedAtUtc: checkedAtUtc);
        }

        var adapters = _networkAdapterSnapshotProvider();
        var hostAdapter = SelectHostAdapter(adapters, headset.HostWifiInterfaceName, headsetIpAddress);
        var selectorIp = ExtractIpAddressFromSelector(selector);
        bool? selectorMatchesHeadsetIp = !string.IsNullOrWhiteSpace(selectorIp)
            ? string.Equals(selectorIp, headsetIp, StringComparison.OrdinalIgnoreCase)
            : null;
        var sameSubnet = hostAdapter is not null
            ? DetermineSameSubnet(headsetIpAddress, hostAdapter)
            : null;

        PingProbeResult pingProbe = await _pingProbe(headsetIp, DefaultPingAttempts, DefaultPingTimeoutMs, cancellationToken).ConfigureAwait(false);
        TcpProbeResult tcpProbe = await _tcpProbe(headsetIp, 5555, DefaultTcpTimeoutMs, cancellationToken).ConfigureAwait(false);

        var topology = BuildTopologySummary(headset, hostAdapter, selectorMatchesHeadsetIp, sameSubnet);
        var ping = BuildPingSummary(pingProbe);
        var tcp = BuildTcpSummary(tcpProbe);

        var detailParts = new List<string>
        {
            RenderSelector(selector),
            RenderHeadsetWifi(headset),
            RenderHostWifi(headset, hostAdapter),
            topology,
            ping,
            tcp
        };

        var level = OperationOutcomeKind.Success;
        var summary = "Quest Wi-Fi path is reachable from Windows.";
        var routedTopologyAccepted = headset.WifiSsidMatchesHost == false
            && tcpProbe.Reachable
            && (sameSubnet == true || IsNonWirelessHostAdapter(hostAdapter));

        if (!tcpProbe.Reachable)
        {
            level = OperationOutcomeKind.Failure;
            summary = "Windows cannot reach the Quest ADB TCP endpoint over the current Wi-Fi path.";
            detailParts.Add(headset.WifiSsidMatchesHost == true
                ? "The PC and Quest report the same SSID, but Windows could not open TCP port 5555 on the Quest IP. On the failing router this usually means guest Wi-Fi/client isolation, AP isolation, WLAN peer blocking, or a stale endpoint surviving an ADB restart."
                : "Without a working TCP path to port 5555, Wi-Fi ADB recovery, probe actions, and Sussex relaunches will be unreliable.");
        }
        else if (routedTopologyAccepted)
        {
            level = sameSubnet == true ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning;
            summary = IsNonWirelessHostAdapter(hostAdapter)
                ? "Quest Wi-Fi path is reachable from Windows over the current wired/router path."
                : "Quest Wi-Fi path is reachable from Windows even though the SSID names differ.";
            detailParts.Add(IsNonWirelessHostAdapter(hostAdapter)
                ? "The Quest is on Wi-Fi, but Windows can still reach TCP port 5555 through the current routed host adapter. Matching PC Wi-Fi SSIDs are not required when the companion is on the same router over Ethernet or another valid routed link."
                : "The SSID names differ, but Windows can still reach TCP port 5555 on the Quest IP over the current routed path. Treat the SSID mismatch as advisory on this topology.");
        }
        else if (headset.WifiSsidMatchesHost == false)
        {
            level = OperationOutcomeKind.Failure;
            summary = "Quest and Windows are on different network paths.";
            detailParts.Add("The headset and PC report different SSIDs, and the current routed path does not yet prove that Windows can reach the Quest endpoint through a same-router Ethernet or equivalent routed setup. Move both devices onto the same reachable network path before continuing.");
        }
        else if (selectorMatchesHeadsetIp == false)
        {
            level = OperationOutcomeKind.Warning;
            summary = "Wi-Fi path is reachable, but the companion is still pointing at a stale Quest endpoint.";
            detailParts.Add($"The current action selector {selector} does not match the headset-reported Wi-Fi IP {headsetIp}. Update the saved reconnect target before relying on recovery actions.");
        }
        else if (sameSubnet == false)
        {
            level = OperationOutcomeKind.Warning;
            summary = "Wi-Fi path is reachable, but the PC and Quest are not on the same IPv4 subnet.";
            detailParts.Add("The router path still allowed TCP 5555, but the devices are not on the same detected IPv4 subnet. Keep this as a hazard because consumer guest networks and managed VLANs can behave inconsistently across steps.");
        }
        else if (pingProbe.Reachable == false)
        {
            level = OperationOutcomeKind.Warning;
            summary = "Quest ADB TCP is reachable, but ICMP ping is blocked or timing out.";
            detailParts.Add("TCP 5555 is working, so Wi-Fi ADB can still function. The missing ping replies usually point to router/firewall ICMP policy rather than a broken Sussex path.");
        }
        else if (hostAdapter is null)
        {
            level = OperationOutcomeKind.Warning;
            summary = "Quest Wi-Fi path is reachable, but the host Wi-Fi adapter could not be identified cleanly.";
            detailParts.Add("The transport works, but the PC-side Wi-Fi adapter metadata is incomplete. That makes subnet and gateway diagnostics less certain on this machine.");
        }
        else if (headset.WifiSsidMatchesHost is null)
        {
            level = OperationOutcomeKind.Warning;
            summary = "Quest Wi-Fi path is reachable, but the SSID match is still unknown.";
            detailParts.Add("The router path looks healthy, but the current snapshot did not resolve both SSID names. Refresh the headset snapshot if you need operator-facing proof of the exact Wi-Fi names.");
        }

        return new QuestWifiTransportDiagnosticsResult(
            level,
            summary,
            string.Join(" ", detailParts.Where(part => !string.IsNullOrWhiteSpace(part))),
            Selector: RenderSelector(selector),
            HeadsetWifi: RenderHeadsetWifi(headset),
            HostWifi: RenderHostWifi(headset, hostAdapter),
            Topology: topology,
            Ping: ping,
            Tcp: tcp,
            TcpReachable: tcpProbe.Reachable,
            PingReachable: pingProbe.Reachable,
            SelectorMatchesHeadsetIp: selectorMatchesHeadsetIp,
            SameSubnet: sameSubnet,
            CheckedAtUtc: checkedAtUtc,
            RoutedTopologyAccepted: routedTopologyAccepted);
    }

    private static WindowsNetworkAdapterSnapshot? SelectHostAdapter(
        IReadOnlyList<WindowsNetworkAdapterSnapshot> adapters,
        string? hostInterfaceName,
        IPAddress headsetIpAddress)
    {
        var eligibleAdapters = adapters
            .Where(adapter => adapter.IsUp && !adapter.IsLoopback && adapter.IPv4Addresses.Count > 0)
            .ToArray();

        var sameSubnetAdapters = eligibleAdapters
            .Where(adapter => DetermineSameSubnet(headsetIpAddress, adapter) == true)
            .ToArray();
        if (sameSubnetAdapters.Length > 0)
        {
            return sameSubnetAdapters
                .OrderByDescending(adapter => adapter.Gateways.Count > 0)
                .ThenBy(adapter => IsWirelessAdapter(adapter) ? 1 : 0)
                .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(hostInterfaceName))
        {
            var match = eligibleAdapters.FirstOrDefault(adapter =>
                string.Equals(adapter.Name, hostInterfaceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(adapter.Description, hostInterfaceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return eligibleAdapters
            .OrderByDescending(adapter => adapter.Gateways.Count > 0)
            .ThenBy(adapter => IsWirelessAdapter(adapter) ? 1 : 0)
            .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool IsWirelessAdapter(WindowsNetworkAdapterSnapshot adapter)
        => string.Equals(adapter.InterfaceType, "Wireless80211", StringComparison.OrdinalIgnoreCase);

    private static bool IsNonWirelessHostAdapter(WindowsNetworkAdapterSnapshot? adapter)
        => adapter is not null && !IsWirelessAdapter(adapter);

    private static bool? DetermineSameSubnet(IPAddress headsetIpAddress, WindowsNetworkAdapterSnapshot adapter)
    {
        if (adapter.SafeIPv4Cidrs.Count > 0)
        {
            foreach (var cidr in adapter.SafeIPv4Cidrs)
            {
                if (TryParseCidr(cidr, out var networkAddress, out var prefixLength) &&
                    IsAddressInSubnet(headsetIpAddress, networkAddress, prefixLength))
                {
                    return true;
                }
            }

            return false;
        }

        var adapterIp = adapter.IPv4Addresses
            .Select(ParseIpv4)
            .FirstOrDefault(ip => ip is not null);
        if (adapterIp is null)
        {
            return null;
        }

        if (adapterIp.GetAddressBytes().Length != 4 || headsetIpAddress.GetAddressBytes().Length != 4)
        {
            return null;
        }

        var left = adapterIp.GetAddressBytes();
        var right = headsetIpAddress.GetAddressBytes();
        return left[0] == right[0] && left[1] == right[1] && left[2] == right[2];
    }

    private static IPAddress? ParseIpv4(string? value)
        => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork
            ? address
            : null;

    private static bool TryParseCidr(string cidr, out IPAddress networkAddress, out int prefixLength)
    {
        networkAddress = IPAddress.None;
        prefixLength = 0;
        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        var separatorIndex = cidr.LastIndexOf('/');
        if (separatorIndex <= 0)
        {
            return false;
        }

        if (!IPAddress.TryParse(cidr[..separatorIndex], out var parsedNetworkAddress) ||
            parsedNetworkAddress.AddressFamily != AddressFamily.InterNetwork ||
            !int.TryParse(cidr[(separatorIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out prefixLength) ||
            prefixLength is < 0 or > 32)
        {
            networkAddress = IPAddress.None;
            prefixLength = 0;
            return false;
        }

        networkAddress = parsedNetworkAddress;
        return true;
    }

    private static bool IsAddressInSubnet(IPAddress address, IPAddress networkAddress, int prefixLength)
    {
        var addressBytes = address.GetAddressBytes();
        var networkBytes = networkAddress.GetAddressBytes();
        if (addressBytes.Length != 4 || networkBytes.Length != 4)
        {
            return false;
        }

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        for (var index = 0; index < fullBytes; index++)
        {
            if (addressBytes[index] != networkBytes[index])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }

    private static string BuildTopologySummary(
        HeadsetAppStatus headset,
        WindowsNetworkAdapterSnapshot? hostAdapter,
        bool? selectorMatchesHeadsetIp,
        bool? sameSubnet)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(headset.HostWifiSsid) || !string.IsNullOrWhiteSpace(headset.HeadsetWifiSsid))
        {
            parts.Add(headset.WifiSsidMatchesHost switch
            {
                true => "SSIDs match.",
                false => "SSIDs do not match.",
                _ => "SSID match unknown."
            });
        }

        parts.Add(selectorMatchesHeadsetIp switch
        {
            true => "Selector IP matches the headset Wi-Fi IP.",
            false => "Selector IP does not match the headset Wi-Fi IP.",
            _ => "Selector IP match unknown."
        });

        if (hostAdapter is not null)
        {
            var adapterIp = hostAdapter.IPv4Addresses.Count > 0 ? hostAdapter.IPv4Addresses[0] : "n/a";
            var gateway = hostAdapter.Gateways.Count > 0 ? hostAdapter.Gateways[0] : "n/a";
            parts.Add($"Host adapter {hostAdapter.Name} ({hostAdapter.InterfaceType}) IPv4 {adapterIp}, gateway {gateway}.");
        }

        parts.Add(sameSubnet switch
        {
            true => "Quest and host appear to share the same IPv4 subnet.",
            false => "Quest and host do not appear to share the same IPv4 subnet.",
            _ => "Same-subnet check unavailable."
        });

        return string.Join(" ", parts);
    }

    private static string BuildPingSummary(PingProbeResult result)
    {
        if (!result.Attempted)
        {
            return "Ping not attempted.";
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return $"Ping probe failed: {result.Error}";
        }

        if (!result.Reachable)
        {
            return $"ICMP ping to the Quest IP timed out across {result.Attempts.ToString(CultureInfo.InvariantCulture)} attempt(s).";
        }

        var latency = result.AverageLatencyMs.HasValue
            ? $"{result.AverageLatencyMs.Value:0.#} ms avg"
            : "latency n/a";
        return $"ICMP ping reached the Quest across {result.SuccessfulReplies.ToString(CultureInfo.InvariantCulture)}/{result.Attempts.ToString(CultureInfo.InvariantCulture)} reply/replies ({latency}).";
    }

    private static string BuildTcpSummary(TcpProbeResult result)
    {
        if (!result.Attempted)
        {
            return "TCP probe not attempted.";
        }

        if (result.Reachable)
        {
            var latency = result.ConnectLatencyMs.HasValue
                ? $"{result.ConnectLatencyMs.Value:0.#} ms"
                : "latency n/a";
            return $"TCP port 5555 on the Quest is reachable from Windows ({latency}).";
        }

        return string.IsNullOrWhiteSpace(result.Error)
            ? "TCP port 5555 on the Quest did not accept a connection from Windows."
            : $"TCP port 5555 on the Quest did not accept a connection from Windows: {result.Error}";
    }

    private static string RenderSelector(string? selector)
        => string.IsNullOrWhiteSpace(selector)
            ? "Selector n/a."
            : $"Selector {selector}.";

    private static string RenderHeadsetWifi(HeadsetAppStatus headset)
    {
        var ssid = string.IsNullOrWhiteSpace(headset.HeadsetWifiSsid) ? "n/a" : headset.HeadsetWifiSsid.Trim();
        var ip = string.IsNullOrWhiteSpace(headset.HeadsetWifiIpAddress) ? "n/a" : headset.HeadsetWifiIpAddress.Trim();
        return $"Headset Wi-Fi {ssid} ({ip}).";
    }

    private static string RenderHostWifi(HeadsetAppStatus headset, WindowsNetworkAdapterSnapshot? hostAdapter)
    {
        var ssid = string.IsNullOrWhiteSpace(headset.HostWifiSsid) ? "n/a" : headset.HostWifiSsid.Trim();
        if (hostAdapter is null)
        {
            var interfaceName = string.IsNullOrWhiteSpace(headset.HostWifiInterfaceName) ? "n/a" : headset.HostWifiInterfaceName.Trim();
            return $"Host Wi-Fi {ssid} via {interfaceName}.";
        }

        var adapterIp = hostAdapter.IPv4Addresses.Count > 0 ? hostAdapter.IPv4Addresses[0] : "n/a";
        if (!IsWirelessAdapter(hostAdapter))
        {
            return $"Host routed link via {hostAdapter.Name} ({adapterIp}); PC Wi-Fi SSID {ssid}.";
        }

        return $"Host Wi-Fi {ssid} via {hostAdapter.Name} ({adapterIp}).";
    }

    private static async Task<PingProbeResult> ProbePingAsync(
        string host,
        int attempts,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var replies = new List<long>();
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reply = await ping.SendPingAsync(host, timeoutMs).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    replies.Add(reply.RoundtripTime);
                }
            }

            return new PingProbeResult(
                Attempted: true,
                Reachable: replies.Count > 0,
                Attempts: attempts,
                SuccessfulReplies: replies.Count,
                AverageLatencyMs: replies.Count > 0 ? replies.Average() : null,
                Error: string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PingProbeResult(
                Attempted: true,
                Reachable: false,
                Attempts: attempts,
                SuccessfulReplies: 0,
                AverageLatencyMs: null,
                Error: ex.Message);
        }
    }

    private static async Task<TcpProbeResult> ProbeTcpAsync(
        string host,
        int port,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(host, port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, cancellationToken)).ConfigureAwait(false);
            if (completedTask != connectTask)
            {
                return new TcpProbeResult(
                    Attempted: true,
                    Reachable: false,
                    ConnectLatencyMs: null,
                    Error: $"timed out after {timeoutMs.ToString(CultureInfo.InvariantCulture)} ms");
            }

            await connectTask.ConfigureAwait(false);
            stopwatch.Stop();
            return new TcpProbeResult(
                Attempted: true,
                Reachable: true,
                ConnectLatencyMs: stopwatch.Elapsed.TotalMilliseconds,
                Error: string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new TcpProbeResult(
                Attempted: true,
                Reachable: false,
                ConnectLatencyMs: null,
                Error: ex.Message);
        }
    }

    private static string ExtractIpAddressFromSelector(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector) || !selector.Contains(':', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var trimmed = selector.Trim();
        var separatorIndex = trimmed.LastIndexOf(':');
        return separatorIndex > 0 ? trimmed[..separatorIndex] : string.Empty;
    }

    public sealed record PingProbeResult(
        bool Attempted,
        bool Reachable,
        int Attempts,
        int SuccessfulReplies,
        double? AverageLatencyMs,
        string Error);

    public sealed record TcpProbeResult(
        bool Attempted,
        bool Reachable,
        double? ConnectLatencyMs,
        string Error);
}
