using System.Net.NetworkInformation;
using System.Net.Sockets;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed record WindowsEnvironmentCheckResult(
    string Id,
    string Label,
    OperationOutcomeKind Level,
    string Summary,
    string Detail);

public sealed record WindowsEnvironmentAnalysisRequest(
    string ExpectedLslStreamName,
    string ExpectedLslStreamType,
    bool ProbeExpectedLslStream = true,
    QuestWifiTransportDiagnosticsContext? QuestWifiTransport = null);

public sealed record QuestWifiTransportDiagnosticsContext(
    HeadsetAppStatus Headset,
    string? RequestedSelector = null);

public sealed record WindowsEnvironmentAnalysisResult(
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    IReadOnlyList<WindowsEnvironmentCheckResult> Checks,
    DateTimeOffset CompletedAtUtc);

public sealed record WindowsNetworkAdapterSnapshot(
    string Name,
    string Description,
    string InterfaceType,
    bool IsUp,
    bool IsLoopback,
    bool IsTunnel,
    bool SupportsMulticast,
    IReadOnlyList<string> IPv4Addresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string>? IPv4Cidrs = null)
{
    public IReadOnlyList<string> SafeIPv4Cidrs => IPv4Cidrs ?? Array.Empty<string>();
}

public sealed class WindowsEnvironmentAnalysisService
{
    private const string LslDiscoveryProbeName = "viscereality_lsl_discovery_self_check";
    private const string LslDiscoveryProbeType = "viscereality.diagnostics";
    private const string LslLoopbackProbeNamePrefix = "viscereality_lsl_loopback_self_check_";
    private const string LslLoopbackProbeType = "viscereality.loopback";
    private const int LslLoopbackProbeAttempts = 3;
    private static readonly TimeSpan LslLoopbackProbeRetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly ILslMonitorService _monitorService;
    private readonly ILslStreamDiscoveryService _streamDiscoveryService;
    private readonly IStudyClockAlignmentService _clockAlignmentService;
    private readonly ITestLslSignalService _testSignalService;
    private readonly ITwinModeBridge _twinBridge;
    private readonly Func<ILslOutletService> _loopbackOutletFactory;
    private readonly Func<OfficialQuestToolingStatus> _toolingStatusProvider;
    private readonly Func<string?> _adbLocator;
    private readonly Func<string?> _hzdbLocator;
    private readonly Func<string?> _bundledLslLocator;
    private readonly Func<string?> _agentWorkspaceLslLocator;
    private readonly Func<bool> _agentWorkspacePresent;
    private readonly Func<IReadOnlyList<WindowsNetworkAdapterSnapshot>> _networkAdapterSnapshotProvider;
    private readonly QuestWifiTransportDiagnosticsService _questWifiTransportDiagnosticsService;
    private readonly Func<DateTimeOffset> _utcNow;

    public WindowsEnvironmentAnalysisService(
        ILslMonitorService monitorService,
        ILslStreamDiscoveryService streamDiscoveryService,
        IStudyClockAlignmentService clockAlignmentService,
        ITestLslSignalService testSignalService,
        ITwinModeBridge twinBridge,
        Func<OfficialQuestToolingStatus>? toolingStatusProvider = null,
        Func<string?>? adbLocator = null,
        Func<string?>? hzdbLocator = null,
        Func<string?>? bundledLslLocator = null,
        Func<string?>? agentWorkspaceLslLocator = null,
        Func<bool>? agentWorkspacePresent = null,
        Func<ILslOutletService>? loopbackOutletFactory = null,
        QuestWifiTransportDiagnosticsService? questWifiTransportDiagnosticsService = null,
        Func<IReadOnlyList<WindowsNetworkAdapterSnapshot>>? networkAdapterSnapshotProvider = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _streamDiscoveryService = streamDiscoveryService ?? throw new ArgumentNullException(nameof(streamDiscoveryService));
        _clockAlignmentService = clockAlignmentService ?? throw new ArgumentNullException(nameof(clockAlignmentService));
        _testSignalService = testSignalService ?? throw new ArgumentNullException(nameof(testSignalService));
        _twinBridge = twinBridge ?? throw new ArgumentNullException(nameof(twinBridge));
        _loopbackOutletFactory = loopbackOutletFactory ?? LslOutletServiceFactory.CreateDefault;
        _toolingStatusProvider = toolingStatusProvider ?? GetLocalToolingStatus;
        _adbLocator = adbLocator ?? AdbExecutableLocator.TryLocate;
        _hzdbLocator = hzdbLocator ?? WindowsHzdbService.ResolveHzdbCommandPath;
        _bundledLslLocator = bundledLslLocator ?? ResolveBundledLslPath;
        _agentWorkspaceLslLocator = agentWorkspaceLslLocator ?? ResolveAgentWorkspaceLslPath;
        _agentWorkspacePresent = agentWorkspacePresent ?? IsAgentWorkspacePresent;
        _networkAdapterSnapshotProvider = networkAdapterSnapshotProvider ?? SnapshotNetworkAdapters;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _questWifiTransportDiagnosticsService = questWifiTransportDiagnosticsService ?? new QuestWifiTransportDiagnosticsService(
            _networkAdapterSnapshotProvider,
            utcNow: _utcNow);
    }

    public async Task<WindowsEnvironmentAnalysisResult> AnalyzeAsync(
        WindowsEnvironmentAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var checks = new List<WindowsEnvironmentCheckResult>
        {
            BuildWindowsPlatformCheck(),
            BuildNetworkAdapterHazardCheck(_networkAdapterSnapshotProvider()),
            BuildManagedToolCacheCheck(_toolingStatusProvider()),
            BuildAdbCheck(),
            BuildHzdbCheck(),
            BuildBundledLslCheck(_bundledLslLocator(), _monitorService.RuntimeState),
            BuildRuntimeCheck(
                id: "lsl-monitor-runtime",
                label: "Windows liblsl monitor runtime",
                runtimeState: _monitorService.RuntimeState,
                required: true,
                fixHint: "Make sure an official liblsl runtime is available to the app, either from the packaged copy or %VISCEREALITY_LSL_DLL%."),
            BuildAgentWorkspaceLslCheck(_agentWorkspacePresent(), _agentWorkspaceLslLocator()),
            BuildRuntimeCheck(
                id: "clock-alignment-runtime",
                label: "Windows clock-alignment runtime",
                runtimeState: _clockAlignmentService.RuntimeState,
                required: true,
                fixHint: "Clock alignment needs the same working liblsl runtime as the Windows monitor path."),
            BuildRuntimeCheck(
                id: "test-sender-runtime",
                label: "Windows TEST sender runtime",
                runtimeState: _testSignalService.RuntimeState,
                required: false,
                fixHint: "The Sussex bench sender is optional, but if you use it the same liblsl runtime must be available locally."),
            BuildTwinBridgeCheck()
        };

        if (request.QuestWifiTransport is not null)
        {
            checks.Add(await BuildQuestWifiTransportCheckAsync(request.QuestWifiTransport, cancellationToken).ConfigureAwait(false));
        }

        checks.Add(await ProbeLslDiscoveryHealthAsync(cancellationToken).ConfigureAwait(false));
        checks.Add(await ProbeLslLoopbackAdvertisementAsync(cancellationToken).ConfigureAwait(false));

        if (request.ProbeExpectedLslStream)
        {
            checks.Add(await ProbeExpectedStreamAsync(request, cancellationToken).ConfigureAwait(false));
        }

        var failureCount = checks.Count(check => check.Level == OperationOutcomeKind.Failure);
        var warningCount = checks.Count(check => check.Level == OperationOutcomeKind.Warning);
        var successCount = checks.Count(check => check.Level == OperationOutcomeKind.Success);

        var level = failureCount > 0
            ? OperationOutcomeKind.Failure
            : warningCount > 0
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success;

        var summary = level switch
        {
            OperationOutcomeKind.Failure => "Windows environment analysis found blocking issues.",
            OperationOutcomeKind.Warning => "Windows environment analysis found advisories.",
            _ => "Windows environment analysis passed."
        };

        var detail = $"Checks ok/warn/fail: {successCount}/{warningCount}/{failureCount}.";
        return new WindowsEnvironmentAnalysisResult(level, summary, detail, checks, _utcNow());
    }

    private static WindowsEnvironmentCheckResult BuildWindowsPlatformCheck()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsEnvironmentCheckResult(
                "windows-platform",
                "Windows platform",
                OperationOutcomeKind.Success,
                "Windows runtime detected.",
                $"Running on {Environment.OSVersion.VersionString}. The public Sussex desktop flow expects Windows.");
        }

        return new WindowsEnvironmentCheckResult(
            "windows-platform",
            "Windows platform",
            OperationOutcomeKind.Failure,
            "Windows runtime not detected.",
            "The Sussex desktop operator shell only supports the live public workflow on Windows.");
    }

    private static OfficialQuestToolingStatus GetLocalToolingStatus()
    {
        using var toolingService = new OfficialQuestToolingService();
        return toolingService.GetLocalStatus();
    }

    private static string? ResolveBundledLslPath()
        => LslRuntimeLayout.TryResolveExistingLocalPath(AppContext.BaseDirectory);

    private static string? ResolveAgentWorkspaceLslPath()
        => LslRuntimeLayout.TryResolveExistingLocalPath(LocalAgentWorkspaceLayout.BundledCliRootPath);

    private static bool IsAgentWorkspacePresent()
        => Directory.Exists(LocalAgentWorkspaceLayout.RootPath);

    internal static IReadOnlyList<WindowsNetworkAdapterSnapshot> SnapshotNetworkAdapters()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var snapshots = new List<WindowsNetworkAdapterSnapshot>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var properties = adapter.GetIPProperties();
                var ipv4 = properties.UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address.ToString())
                    .Where(static address => !string.IsNullOrWhiteSpace(address))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var ipv4Cidrs = properties.UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address =>
                    {
                        var prefixLength = address.PrefixLength;
                        return prefixLength is >= 0 and <= 32
                            ? $"{address.Address}/{prefixLength.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                            : string.Empty;
                    })
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var gateways = properties.GatewayAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address.ToString())
                    .Where(static address => !string.IsNullOrWhiteSpace(address) && address != "0.0.0.0")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                snapshots.Add(new WindowsNetworkAdapterSnapshot(
                    adapter.Name,
                    adapter.Description,
                    adapter.NetworkInterfaceType.ToString(),
                    adapter.OperationalStatus == OperationalStatus.Up,
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback,
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel,
                    adapter.SupportsMulticast,
                    ipv4,
                    gateways,
                    ipv4Cidrs));
            }
            catch
            {
                snapshots.Add(new WindowsNetworkAdapterSnapshot(
                    adapter.Name,
                    adapter.Description,
                    adapter.NetworkInterfaceType.ToString(),
                    adapter.OperationalStatus == OperationalStatus.Up,
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback,
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel,
                    adapter.SupportsMulticast,
                    [],
                    [],
                    []));
            }
        }

        return snapshots;
    }

    private static WindowsEnvironmentCheckResult BuildNetworkAdapterHazardCheck(
        IReadOnlyList<WindowsNetworkAdapterSnapshot> adapters)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WindowsEnvironmentCheckResult(
                "network-adapters",
                "Windows network adapter hazards",
                OperationOutcomeKind.Preview,
                "Network adapter hazard scan is Windows-only.",
                "The public Sussex operator flow is Windows-first; this check is only active on Windows.");
        }

        var activeIpv4 = adapters
            .Where(static adapter => adapter.IsUp && !adapter.IsLoopback && adapter.IPv4Addresses.Count > 0)
            .OrderBy(static adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (activeIpv4.Length == 0)
        {
            return new WindowsEnvironmentCheckResult(
                "network-adapters",
                "Windows network adapter hazards",
                OperationOutcomeKind.Failure,
                "No active non-loopback IPv4 adapter is visible.",
                "LSL discovery and Wi-Fi ADB need a usable Windows network adapter on the same network as the Quest. Connect Wi-Fi or Ethernet before running the Sussex checks.");
        }

        var virtualOrVpn = activeIpv4.Where(LooksLikeVirtualOrVpnAdapter).ToArray();
        var multicastDisabled = activeIpv4.Where(static adapter => !adapter.SupportsMulticast).ToArray();
        var gatewayAdapters = activeIpv4.Where(static adapter => adapter.Gateways.Count > 0).ToArray();

        var hazards = new List<string>();
        if (activeIpv4.Length > 1)
        {
            hazards.Add("multiple active IPv4 adapters are up");
        }

        if (virtualOrVpn.Length > 0)
        {
            hazards.Add("VPN or virtual adapters are active");
        }

        if (multicastDisabled.Length > 0)
        {
            hazards.Add("at least one active adapter does not report multicast support");
        }

        if (gatewayAdapters.Length == 0)
        {
            hazards.Add("no active IPv4 adapter reports a default gateway");
        }
        else if (gatewayAdapters.Length > 1)
        {
            hazards.Add("multiple active adapters report default gateways");
        }

        var adapterLines = activeIpv4
            .Take(12)
            .Select(adapter =>
                $"- {adapter.Name} ({adapter.InterfaceType}) IPv4 {JoinOrNone(adapter.IPv4Addresses)} gateway {JoinOrNone(adapter.Gateways)} multicast {(adapter.SupportsMulticast ? "yes" : "no")}{(LooksLikeVirtualOrVpnAdapter(adapter) ? " virtual/vpn-like" : string.Empty)}")
            .ToArray();
        var detail =
            $"Active IPv4 adapters:{Environment.NewLine}{string.Join(Environment.NewLine, adapterLines)}";

        if (activeIpv4.Length > adapterLines.Length)
        {
            detail += $"{Environment.NewLine}- ... {activeIpv4.Length - adapterLines.Length} more active adapter(s)";
        }

        if (hazards.Count == 0)
        {
            return new WindowsEnvironmentCheckResult(
                "network-adapters",
                "Windows network adapter hazards",
                OperationOutcomeKind.Success,
                "Network adapter shape looks simple.",
                $"{detail}{Environment.NewLine}This does not prove multicast is perfect, but the common multi-adapter and VPN/virtual-adapter hazards are not visible.");
        }

        return new WindowsEnvironmentCheckResult(
            "network-adapters",
            "Windows network adapter hazards",
            OperationOutcomeKind.Warning,
            "Network adapter shape can make LSL discovery inconsistent.",
            $"{detail}{Environment.NewLine}Hazards: {string.Join("; ", hazards)}. If Sussex works intermittently, disable unused VPN, Hyper-V, Docker, WSL, TAP/Wintun, VirtualBox, VMware, or other virtual adapters during the run, keep the Quest and PC on the same Wi-Fi, and use a Private Windows network profile.");
    }

    private static bool LooksLikeVirtualOrVpnAdapter(WindowsNetworkAdapterSnapshot adapter)
    {
        if (adapter.IsTunnel)
        {
            return true;
        }

        var text = $"{adapter.Name} {adapter.Description} {adapter.InterfaceType}".ToLowerInvariant();
        string[] markers =
        [
            "vpn",
            "tailscale",
            "zerotier",
            "wireguard",
            "wintun",
            "tap",
            "tun ",
            "hyper-v",
            "vethernet",
            "docker",
            "wsl",
            "virtualbox",
            "vmware",
            "npcap",
            "loopback",
            "teredo",
            "isatap"
        ];

        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string JoinOrNone(IReadOnlyList<string> values)
        => values.Count == 0 ? "none" : string.Join(", ", values);

    private static WindowsEnvironmentCheckResult BuildManagedToolCacheCheck(OfficialQuestToolingStatus localStatus)
    {
        if (localStatus.IsReady)
        {
            return new WindowsEnvironmentCheckResult(
                "official-tool-cache",
                "Managed official tool cache",
                OperationOutcomeKind.Success,
                "Managed Quest tooling cache is ready.",
                $"hzdb {localStatus.Hzdb.InstalledVersion ?? "n/a"} at {localStatus.Hzdb.InstallPath}. Android platform-tools {localStatus.PlatformTools.InstalledVersion ?? "n/a"} at {localStatus.PlatformTools.InstallPath}.");
        }

        return new WindowsEnvironmentCheckResult(
            "official-tool-cache",
            "Managed official tool cache",
            OperationOutcomeKind.Warning,
            "Managed Quest tooling cache is incomplete.",
            $"Run guided setup or `viscereality tooling install-official` so the app can rely on the LocalAppData-managed official Meta hzdb and Google platform-tools copies under {OfficialQuestToolingLayout.RootPath}.");
    }

    private static WindowsEnvironmentCheckResult BuildBundledLslCheck(string? bundledLslPath, LslRuntimeState runtimeState)
    {
        if (!string.IsNullOrWhiteSpace(bundledLslPath))
        {
            return new WindowsEnvironmentCheckResult(
                "bundled-liblsl",
                "Bundled liblsl copy",
                OperationOutcomeKind.Success,
                "Bundled liblsl copy is present.",
                $"Resolved process-local liblsl at {bundledLslPath}.");
        }

        var expectedPaths = string.Join(" or ", LslRuntimeLayout.GetLocalCandidatePaths(AppContext.BaseDirectory));
        var detail = runtimeState.Available
            ? $"The current process can still load liblsl, but not from a bundled local copy. {runtimeState.Detail} Expected local paths: {expectedPaths}."
            : $"No bundled local liblsl copy was found for the current process. Expected local paths: {expectedPaths}.";
        return new WindowsEnvironmentCheckResult(
            "bundled-liblsl",
            "Bundled liblsl copy",
            OperationOutcomeKind.Warning,
            "Bundled liblsl copy is not present in this process layout.",
            detail);
    }

    private static WindowsEnvironmentCheckResult BuildAgentWorkspaceLslCheck(bool agentWorkspacePresent, string? agentWorkspaceLslPath)
    {
        if (!agentWorkspacePresent)
        {
            return new WindowsEnvironmentCheckResult(
                "agent-workspace-liblsl",
                "Local agent workspace liblsl export",
                OperationOutcomeKind.Preview,
                "Local agent workspace not created yet.",
                $"Once the workspace exists under {LocalAgentWorkspaceLayout.RootPath}, it should export liblsl alongside the bundled CLI for installed-app agent use.");
        }

        if (!string.IsNullOrWhiteSpace(agentWorkspaceLslPath))
        {
            return new WindowsEnvironmentCheckResult(
                "agent-workspace-liblsl",
                "Local agent workspace liblsl export",
                OperationOutcomeKind.Success,
                "Local agent workspace exports liblsl.",
                $"Agent workspace CLI can resolve liblsl from {agentWorkspaceLslPath}.");
        }

        return new WindowsEnvironmentCheckResult(
            "agent-workspace-liblsl",
            "Local agent workspace liblsl export",
            OperationOutcomeKind.Warning,
            "Local agent workspace is missing its bundled liblsl export.",
            $"Expected {LocalAgentWorkspaceLayout.BundledCliLslDllPath} or {LocalAgentWorkspaceLayout.BundledCliRuntimeLslDllPath}. Reopen the workspace from the packaged app or refresh the installed build so the exported CLI mirrors the bundled liblsl runtime.");
    }

    private WindowsEnvironmentCheckResult BuildAdbCheck()
    {
        var adbPath = _adbLocator();
        if (!string.IsNullOrWhiteSpace(adbPath))
        {
            return new WindowsEnvironmentCheckResult(
                "adb",
                "ADB transport",
                OperationOutcomeKind.Success,
                "adb.exe is available.",
                $"Resolved adb at {adbPath}.");
        }

        return new WindowsEnvironmentCheckResult(
            "adb",
            "ADB transport",
            OperationOutcomeKind.Failure,
            "adb.exe is missing.",
            $"Quest control cannot run until adb.exe is available. Preferred fix: run guided setup or `viscereality tooling install-official`. Expected managed path: {OfficialQuestToolingLayout.AdbExecutablePath}.");
    }

    private WindowsEnvironmentCheckResult BuildHzdbCheck()
    {
        var hzdbPath = _hzdbLocator();
        if (!string.IsNullOrWhiteSpace(hzdbPath))
        {
            var level = string.Equals(Path.GetExtension(hzdbPath), ".cmd", StringComparison.OrdinalIgnoreCase)
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success;
            var summary = level == OperationOutcomeKind.Success
                ? "hzdb is available."
                : "hzdb is available through npm/npx fallback.";
            var detail = level == OperationOutcomeKind.Success
                ? $"Resolved hzdb command at {hzdbPath}."
                : $"Resolved hzdb through {hzdbPath}. This works, but the managed Meta binary cache is the more stable public path.";
            return new WindowsEnvironmentCheckResult("hzdb", "hzdb command", level, summary, detail);
        }

        return new WindowsEnvironmentCheckResult(
            "hzdb",
            "hzdb command",
            OperationOutcomeKind.Failure,
            "hzdb is missing.",
            $"Quest screenshot, file pullback, and proximity control depend on hzdb. Preferred fix: run guided setup or `viscereality tooling install-official`. Expected managed path: {OfficialQuestToolingLayout.HzdbExecutablePath}.");
    }

    private static WindowsEnvironmentCheckResult BuildRuntimeCheck(
        string id,
        string label,
        LslRuntimeState runtimeState,
        bool required,
        string fixHint)
    {
        if (runtimeState.Available)
        {
            return new WindowsEnvironmentCheckResult(id, label, OperationOutcomeKind.Success, $"{label} is ready.", runtimeState.Detail);
        }

        return new WindowsEnvironmentCheckResult(
            id,
            label,
            required ? OperationOutcomeKind.Failure : OperationOutcomeKind.Warning,
            $"{label} is unavailable.",
            $"{runtimeState.Detail} Fix: {fixHint}");
    }

    private WindowsEnvironmentCheckResult BuildTwinBridgeCheck()
    {
        var status = _twinBridge.Status;
        return new WindowsEnvironmentCheckResult(
            "twin-bridge",
            "Twin bridge transport",
            status.IsAvailable ? OperationOutcomeKind.Success : OperationOutcomeKind.Failure,
            status.Summary,
            status.Detail);
    }

    private async Task<WindowsEnvironmentCheckResult> BuildQuestWifiTransportCheckAsync(
        QuestWifiTransportDiagnosticsContext context,
        CancellationToken cancellationToken)
    {
        var result = await _questWifiTransportDiagnosticsService
            .AnalyzeAsync(context.Headset, context.RequestedSelector, cancellationToken)
            .ConfigureAwait(false);

        return new WindowsEnvironmentCheckResult(
            "quest-wifi-transport",
            "Quest Wi-Fi transport path",
            result.Level,
            result.Summary,
            result.Detail);
    }

    private async Task<WindowsEnvironmentCheckResult> ProbeLslDiscoveryHealthAsync(
        CancellationToken cancellationToken)
    {
        if (!_streamDiscoveryService.RuntimeState.Available)
        {
            return new WindowsEnvironmentCheckResult(
                "lsl-discovery-health",
                "Windows liblsl discovery self-check",
                OperationOutcomeKind.Warning,
                "Windows liblsl discovery self-check could not run.",
                $"The discovery runtime is unavailable. {_streamDiscoveryService.RuntimeState.Detail}");
        }

        try
        {
            _ = await Task.Run(
                    () => _streamDiscoveryService.Discover(new LslStreamDiscoveryRequest(
                        LslDiscoveryProbeName,
                        LslDiscoveryProbeType,
                        Limit: 1)),
                    cancellationToken)
                .ConfigureAwait(false);

            return new WindowsEnvironmentCheckResult(
                "lsl-discovery-health",
                "Windows liblsl discovery self-check",
                OperationOutcomeKind.Success,
                "Windows liblsl discovery API returned normally.",
                "A deliberately unique diagnostic stream lookup completed without a liblsl resolver/socket error. This only proves local discovery did not fail immediately; it does not prove the expected HRV stream is currently visible.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var hazardHint = IsLikelySocketAdapterFailure(ex.Message)
                ? " The error text matches a Windows socket/adapter context failure, which often points to VPN, virtual adapter, multicast, firewall, or network-profile state."
                : string.Empty;
            return new WindowsEnvironmentCheckResult(
                "lsl-discovery-health",
                "Windows liblsl discovery self-check",
                OperationOutcomeKind.Failure,
                "Windows liblsl discovery failed before stream matching.",
                $"{ex.Message}{hazardHint} Try disabling unused VPN/virtual adapters, confirm the active Wi-Fi network is Private, and rerun Analyze Windows Environment before blaming the Quest inlet.");
        }
    }

    private async Task<WindowsEnvironmentCheckResult> ProbeLslLoopbackAdvertisementAsync(
        CancellationToken cancellationToken)
    {
        if (!_streamDiscoveryService.RuntimeState.Available)
        {
            return new WindowsEnvironmentCheckResult(
                "lsl-loopback-outlet",
                "Windows LSL loopback outlet self-check",
                OperationOutcomeKind.Warning,
                "Windows LSL loopback outlet self-check could not run.",
                $"The discovery runtime is unavailable. {_streamDiscoveryService.RuntimeState.Detail}");
        }

        try
        {
            using var outlet = _loopbackOutletFactory();
            if (!outlet.RuntimeState.Available)
            {
                return new WindowsEnvironmentCheckResult(
                    "lsl-loopback-outlet",
                    "Windows LSL loopback outlet self-check",
                    OperationOutcomeKind.Failure,
                    "Windows could not open a temporary local LSL outlet.",
                    $"The outlet runtime is unavailable. {outlet.RuntimeState.Detail}");
            }

            var streamName = $"{LslLoopbackProbeNamePrefix}{Guid.NewGuid():N}";
            var sourceId = TwinLslSourceId.BuildCompanionSourceId(streamName, LslLoopbackProbeType, Environment.MachineName);
            var open = outlet.Open(streamName, LslLoopbackProbeType, 1);
            if (open.Kind == OperationOutcomeKind.Failure)
            {
                return new WindowsEnvironmentCheckResult(
                    "lsl-loopback-outlet",
                    "Windows LSL loopback outlet self-check",
                    OperationOutcomeKind.Failure,
                    "Windows could not open a temporary local LSL outlet.",
                    $"{open.Summary} {open.Detail}");
            }

            if (open.Kind != OperationOutcomeKind.Success)
            {
                return new WindowsEnvironmentCheckResult(
                    "lsl-loopback-outlet",
                    "Windows LSL loopback outlet self-check",
                    OperationOutcomeKind.Warning,
                    "Windows LSL loopback outlet self-check ran in preview mode.",
                    $"{open.Summary} {open.Detail}");
            }

            outlet.PushSample(["ping"]);

            for (var attempt = 1; attempt <= LslLoopbackProbeAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    await Task.Delay(LslLoopbackProbeRetryDelay, cancellationToken).ConfigureAwait(false);
                }

                var matches = await Task.Run(
                        () => _streamDiscoveryService.Discover(new LslStreamDiscoveryRequest(
                            streamName,
                            LslLoopbackProbeType,
                            ExactSourceId: sourceId,
                            Limit: 4)),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (matches.Count > 0)
                {
                    return new WindowsEnvironmentCheckResult(
                        "lsl-loopback-outlet",
                        "Windows LSL loopback outlet self-check",
                        OperationOutcomeKind.Success,
                        "Windows can advertise and rediscover a local LSL outlet.",
                        $"Temporary outlet {streamName} / {LslLoopbackProbeType} was rediscovered with source_id `{sourceId}` on attempt {attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)}. This proves the companion's local LSL outlet advertisement path is discoverable on this PC.");
                }
            }

            var activeSenderHint = _testSignalService.IsRunning
                ? " The companion TEST sender currently reports active, so a missing HRV stream alongside this failure points to a Windows LSL advertisement/discovery problem rather than a Quest inlet problem."
                : string.Empty;
            return new WindowsEnvironmentCheckResult(
                "lsl-loopback-outlet",
                "Windows LSL loopback outlet self-check",
                OperationOutcomeKind.Failure,
                "Windows could not rediscover its own temporary LSL outlet.",
                $"The analyzer opened a temporary local LSL outlet but liblsl discovery did not find it after {LslLoopbackProbeAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture)} attempts.{activeSenderHint} Fix this before debugging the headset: disable unused VPN/virtual adapters, confirm the active Wi-Fi network is Private, allow the app through Windows Firewall on Private networks, and rerun Analyze Windows Environment.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var hazardHint = IsLikelySocketAdapterFailure(ex.Message)
                ? " The error text matches a Windows socket/adapter context failure, which often points to VPN, virtual adapter, multicast, firewall, or network-profile state."
                : string.Empty;
            return new WindowsEnvironmentCheckResult(
                "lsl-loopback-outlet",
                "Windows LSL loopback outlet self-check",
                OperationOutcomeKind.Failure,
                "Windows LSL loopback outlet self-check failed.",
                $"{ex.Message}{hazardHint} If the TEST sender reports active but HRV_Biofeedback / HRV is still missing, debug this Windows-side LSL advertisement path before debugging the Quest.");
        }
    }

    private static bool IsLikelySocketAdapterFailure(string? message)
        => !string.IsNullOrWhiteSpace(message) &&
           (message.Contains("set_option", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("requested address is not valid in its context", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("socket", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("multicast", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("adapter", StringComparison.OrdinalIgnoreCase));

    private async Task<WindowsEnvironmentCheckResult> ProbeExpectedStreamAsync(
        WindowsEnvironmentAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var streamName = string.IsNullOrWhiteSpace(request.ExpectedLslStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : request.ExpectedLslStreamName.Trim();
        var streamType = string.IsNullOrWhiteSpace(request.ExpectedLslStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : request.ExpectedLslStreamType.Trim();

        if (!_streamDiscoveryService.RuntimeState.Available)
        {
            return new WindowsEnvironmentCheckResult(
                "expected-stream",
                "Expected Sussex LSL stream",
                OperationOutcomeKind.Warning,
                "Expected Sussex LSL stream could not be probed.",
                $"The Windows LSL discovery runtime is unavailable, so the app could not probe {streamName} / {streamType} on this PC.");
        }

        try
        {
            var matches = await Task.Run(
                    () => _streamDiscoveryService.Discover(new LslStreamDiscoveryRequest(streamName, streamType)),
                    cancellationToken)
                .ConfigureAwait(false);

            if (matches.Count == 0)
            {
                var activeSenderHint = _testSignalService.IsRunning
                    ? " The companion TEST sender reports active, so if this remains missing, check the Windows LSL loopback outlet self-check and local adapter/firewall state before assuming the headset failed."
                    : string.Empty;
                return new WindowsEnvironmentCheckResult(
                    "expected-stream",
                    "Expected Sussex LSL stream",
                    OperationOutcomeKind.Warning,
                    $"{streamName} / {streamType} is not currently visible on Windows.",
                    $"No visible source matched {streamName} / {streamType}. Fix: start the upstream sender on this PC, or verify that the external sender is publishing the expected name/type contract.{activeSenderHint}");
            }

            var detail = string.Join(
                Environment.NewLine,
                matches.Select(static stream =>
                    $"{stream.Name} / {stream.Type} | source_id `{(string.IsNullOrWhiteSpace(stream.SourceId) ? "n/a" : stream.SourceId)}` | channels {stream.ChannelCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} | nominal {stream.SampleRateHz.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} Hz"));

            return matches.Count == 1
                ? new WindowsEnvironmentCheckResult(
                    "expected-stream",
                    "Expected Sussex LSL stream",
                    OperationOutcomeKind.Success,
                    $"{streamName} / {streamType} is visible on Windows.",
                    detail)
                : new WindowsEnvironmentCheckResult(
                    "expected-stream",
                    "Expected Sussex LSL stream",
                    OperationOutcomeKind.Warning,
                    $"Multiple {streamName} / {streamType} sources are visible on Windows.",
                    $"More than one matching source is advertising the expected upstream contract. This can make sender switching unreliable. Visible matches:{Environment.NewLine}{detail}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WindowsEnvironmentCheckResult(
                "expected-stream",
                "Expected Sussex LSL stream",
                OperationOutcomeKind.Failure,
                $"{streamName} / {streamType} could not be probed.",
                ex.Message);
        }
    }
}
