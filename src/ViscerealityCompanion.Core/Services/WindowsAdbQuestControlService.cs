using System.Diagnostics;
using System.Text;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class WindowsAdbQuestControlService : IQuestControlService
{
    private readonly string _adbPath;
    private readonly Lock _sync = new();
    private string? _activeSelector;
    private string? _lastUsbSelector;
    private string? _lastTcpSelector;

    public WindowsAdbQuestControlService(string adbPath, string? initialSelector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adbPath);
        _adbPath = adbPath;
        _activeSelector = string.IsNullOrWhiteSpace(initialSelector) ? null : initialSelector;
        _lastTcpSelector = LooksLikeTcpSelector(_activeSelector) ? _activeSelector : null;
    }

    public async Task<OperationOutcome> ProbeUsbAsync(CancellationToken cancellationToken = default)
    {
        var devices = await ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        var usbDevices = devices.Where(device => !device.IsTcp && string.Equals(device.State, "device", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (usbDevices.Length == 0)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "No USB Quest device found.",
                "Connect the headset over USB, accept the debugging prompt in-headset, and run the probe again.",
                Items: devices.Select(device => device.RawLine).ToArray());
        }

        lock (_sync)
        {
            _lastUsbSelector = usbDevices[0].Serial;
            _activeSelector ??= _lastUsbSelector;
        }

        return new OperationOutcome(
            OperationOutcomeKind.Success,
            $"USB ADB ready on {usbDevices[0].Serial}.",
            $"Detected {usbDevices.Length} USB ADB device(s).",
            Endpoint: usbDevices[0].Serial,
            Items: usbDevices.Select(device => device.RawLine).ToArray());
    }

    public async Task<OperationOutcome> DiscoverWifiAsync(CancellationToken cancellationToken = default)
    {
        var devices = await ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        var activeWifiDevices = devices
            .Where(device => device.IsTcp && string.Equals(device.State, "device", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (activeWifiDevices.Length > 0)
        {
            var selected = activeWifiDevices[0].Serial;
            lock (_sync)
            {
                _activeSelector = selected;
                _lastTcpSelector = selected;
            }

            return new OperationOutcome(
                OperationOutcomeKind.Success,
                $"Wi-Fi ADB ready on {selected}.",
                $"Detected {activeWifiDevices.Length} active Wi-Fi ADB endpoint(s).",
                Endpoint: selected,
                Items: activeWifiDevices.Select(device => device.RawLine).ToArray());
        }

        var reconnectCandidates = devices
            .Where(device => device.IsTcp)
            .Select(device => device.Serial)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rememberedEndpoint = GetRememberedTcpSelector();
        if (!string.IsNullOrWhiteSpace(rememberedEndpoint) &&
            !reconnectCandidates.Contains(rememberedEndpoint, StringComparer.OrdinalIgnoreCase))
        {
            reconnectCandidates.Insert(0, rememberedEndpoint);
        }

        foreach (var endpoint in reconnectCandidates)
        {
            var connectResult = await ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            if (connectResult.Kind == OperationOutcomeKind.Success)
            {
                return new OperationOutcome(
                    OperationOutcomeKind.Success,
                    $"Reconnected to Quest over Wi-Fi at {connectResult.Endpoint}.",
                    connectResult.Detail,
                    Endpoint: connectResult.Endpoint,
                    Items: reconnectCandidates.ToArray());
            }
        }

        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            "No Wi-Fi ADB Quest endpoint found.",
            "If the headset has rebooted, connect USB once and use Enable Wi-Fi ADB. Otherwise enter the known IP:port manually or reconnect from a previously saved endpoint.",
            Endpoint: rememberedEndpoint,
            Items: devices.Select(device => device.RawLine).ToArray());
    }

    public async Task<OperationOutcome> EnableWifiFromUsbAsync(CancellationToken cancellationToken = default)
    {
        var selector = await EnsureUsbSelectorAsync(cancellationToken).ConfigureAwait(false);
        var tcpip = await RunAdbAsync(["-s", selector, "tcpip", "5555"], cancellationToken).ConfigureAwait(false);
        if (tcpip.ExitCode != 0)
        {
            return Failure("Wi-Fi ADB bootstrap failed.", tcpip.CombinedOutput);
        }

        var ipAddress = await TryReadShellValueAsync(selector, "getprop dhcp.wlan0.ipaddress", cancellationToken).ConfigureAwait(false);
        var endpoint = string.IsNullOrWhiteSpace(ipAddress) ? string.Empty : $"{ipAddress}:5555";

        return new OperationOutcome(
            string.IsNullOrWhiteSpace(endpoint) ? OperationOutcomeKind.Warning : OperationOutcomeKind.Success,
            string.IsNullOrWhiteSpace(endpoint)
                ? "Quest switched to TCP/IP mode on port 5555."
                : $"Quest switched to TCP/IP mode at {endpoint}.",
            string.IsNullOrWhiteSpace(endpoint)
                ? "Enter the headset Wi-Fi IP manually and run Connect Quest."
                : AdbShellSupport.Collapse(tcpip.CombinedOutput),
            Endpoint: endpoint);
    }

    public async Task<OperationOutcome> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEndpoint(endpoint);
        var result = await RunAdbAsync(["connect", normalized], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return Failure($"ADB connect failed for {normalized}.", result.CombinedOutput, endpoint: normalized);
        }

        var model = await TryReadShellValueAsync(normalized, "getprop ro.product.model", cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _activeSelector = normalized;
            _lastTcpSelector = normalized;
        }

        return new OperationOutcome(
            OperationOutcomeKind.Success,
            $"Connected to Quest at {normalized}.",
            string.IsNullOrWhiteSpace(model)
                ? AdbShellSupport.Collapse(result.CombinedOutput)
                : $"Device model: {model}. {AdbShellSupport.Collapse(result.CombinedOutput)}",
            Endpoint: normalized);
    }

    public async Task<OperationOutcome> ApplyPerformanceLevelsAsync(int cpuLevel, int gpuLevel, CancellationToken cancellationToken = default)
    {
        cpuLevel = Math.Clamp(cpuLevel, 0, 5);
        gpuLevel = Math.Clamp(gpuLevel, 0, 5);

        var selector = GetRequiredSelector();
        var cpuResult = await RunShellAsync(selector, $"setprop debug.oculus.cpuLevel {cpuLevel}", cancellationToken).ConfigureAwait(false);
        if (cpuResult.ExitCode != 0)
        {
            return Failure("CPU level update failed.", cpuResult.CombinedOutput);
        }

        var gpuResult = await RunShellAsync(selector, $"setprop debug.oculus.gpuLevel {gpuLevel}", cancellationToken).ConfigureAwait(false);
        if (gpuResult.ExitCode != 0)
        {
            return Failure("GPU level update failed.", gpuResult.CombinedOutput);
        }

        var readCpu = await TryReadShellValueAsync(selector, "getprop debug.oculus.cpuLevel", cancellationToken).ConfigureAwait(false);
        var readGpu = await TryReadShellValueAsync(selector, "getprop debug.oculus.gpuLevel", cancellationToken).ConfigureAwait(false);

        if (!string.Equals(readCpu, cpuLevel.ToString(), StringComparison.Ordinal) ||
            !string.Equals(readGpu, gpuLevel.ToString(), StringComparison.Ordinal))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                $"Quest performance request sent: CPU {cpuLevel}, GPU {gpuLevel}.",
                $"Read back CPU `{readCpu}` and GPU `{readGpu}`.");
        }

        return Success(
            $"Applied Quest performance levels: CPU {cpuLevel}, GPU {gpuLevel}.",
            $"Quest reported CPU {readCpu} and GPU {readGpu}.");
    }

    public async Task<OperationOutcome> InstallAppAsync(QuestAppTarget target, CancellationToken cancellationToken = default)
    {
        var selector = GetRequiredSelector();
        var apkPath = ResolveExistingPath(target.ApkFile);
        if (apkPath is null)
        {
            return Failure($"APK not found for {target.Label}.", $"Expected file path: {target.ApkFile}", packageId: target.PackageId);
        }

        var install = await RunAdbAsync(["-s", selector, "install", "-r", "-d", "-g", apkPath], cancellationToken).ConfigureAwait(false);
        var verify = await RunShellAsync(selector, $"pm path {AdbShellSupport.Quote(target.PackageId)}", cancellationToken).ConfigureAwait(false);

        if (install.ExitCode == 0 && verify.StdOut.Contains("package:", StringComparison.OrdinalIgnoreCase))
        {
            return Success(
                $"Installed {target.Label}.",
                AdbShellSupport.Collapse(install.CombinedOutput),
                packageId: target.PackageId);
        }

        return Failure(
            $"Install failed for {target.Label}.",
            $"{AdbShellSupport.Collapse(install.CombinedOutput)} {AdbShellSupport.Collapse(verify.CombinedOutput)}",
            packageId: target.PackageId);
    }

    public async Task<OperationOutcome> InstallBundleAsync(
        QuestBundle bundle,
        IReadOnlyList<QuestAppTarget> targets,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<string>();
        var installed = new List<string>();

        foreach (var target in targets)
        {
            var outcome = await InstallAppAsync(target, cancellationToken).ConfigureAwait(false);
            outcomes.Add($"{target.Label}: {outcome.Summary}");
            if (outcome.Kind is OperationOutcomeKind.Success or OperationOutcomeKind.Preview)
            {
                installed.Add(target.PackageId);
            }
            else if (outcome.Kind is OperationOutcomeKind.Failure)
            {
                return new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    $"Bundle install failed for {bundle.Label}.",
                    string.Join(" ", outcomes),
                    Items: installed);
            }
        }

        return new OperationOutcome(
            OperationOutcomeKind.Success,
            $"Installed bundle {bundle.Label}.",
            string.Join(" ", outcomes),
            Items: installed);
    }

    public async Task<OperationOutcome> ApplyHotloadProfileAsync(
        HotloadProfile profile,
        QuestAppTarget target,
        CancellationToken cancellationToken = default)
    {
        var selector = GetRequiredSelector();

        // Resolve the CSV file — profile.File may be an absolute path or relative name
        var csvPath = File.Exists(profile.File) ? profile.File : null;
        if (csvPath is null)
        {
            return Failure(
                $"Hotload CSV not found: {profile.File}.",
                "Provide the full path to the CSV or ensure the catalog root is correct.");
        }

        var deviceDir = $"/sdcard/Android/data/{AdbShellSupport.Quote(target.PackageId)}/files/runtime_hotload";
        var deviceFile = $"{deviceDir}/runtime_overrides.csv";

        // Create target directory on device
        var mkdir = await RunShellAsync(selector, $"mkdir -p {deviceDir}", cancellationToken).ConfigureAwait(false);
        if (mkdir.ExitCode != 0)
        {
            return Failure($"Failed to create hotload directory on device.", mkdir.CombinedOutput);
        }

        // Push CSV
        var push = await RunAdbAsync(["-s", selector, "push", csvPath, deviceFile], cancellationToken).ConfigureAwait(false);
        if (push.ExitCode != 0)
        {
            return Failure($"ADB push failed for {profile.Label}.", push.CombinedOutput);
        }

        // Verify file exists on device
        var verify = await RunShellAsync(selector, $"ls -l {deviceFile}", cancellationToken).ConfigureAwait(false);
        if (verify.ExitCode != 0 || string.IsNullOrWhiteSpace(verify.StdOut))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                $"Push sent for {profile.Label} but verification failed.",
                AdbShellSupport.Collapse(verify.CombinedOutput),
                PackageId: target.PackageId);
        }

        return Success(
            $"Pushed hotload profile {profile.Label} to {target.PackageId}.",
            $"Device path: {deviceFile}. {AdbShellSupport.Collapse(push.CombinedOutput)}",
            packageId: target.PackageId);
    }

    public async Task<OperationOutcome> ApplyDeviceProfileAsync(DeviceProfile profile, CancellationToken cancellationToken = default)
    {
        var selector = GetRequiredSelector();
        var applied = new List<string>();

        foreach (var pair in profile.Properties)
        {
            var set = await RunShellAsync(selector, $"setprop {AdbShellSupport.Quote(pair.Key)} {AdbShellSupport.Quote(pair.Value)}", cancellationToken).ConfigureAwait(false);
            if (set.ExitCode != 0)
            {
                return Failure($"Device profile failed at {pair.Key}.", set.CombinedOutput);
            }

            var readBack = await TryReadShellValueAsync(selector, $"getprop {AdbShellSupport.Quote(pair.Key)}", cancellationToken).ConfigureAwait(false);
            if (!string.Equals(readBack, pair.Value, StringComparison.Ordinal))
            {
                return new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    $"Device profile partially applied: {profile.Label}.",
                    $"Expected {pair.Key}={pair.Value} but Quest reported `{readBack}`.");
            }

            applied.Add($"{pair.Key}={readBack}");
        }

        return new OperationOutcome(
            OperationOutcomeKind.Success,
            $"Applied device profile {profile.Label}.",
            string.Join("; ", applied),
            Items: applied);
    }

    public async Task<OperationOutcome> LaunchAppAsync(QuestAppTarget target, CancellationToken cancellationToken = default)
    {
        var selector = GetRequiredSelector();
        await RunShellAsync(selector, AdbShellSupport.BuildForceStopCommand(target.PackageId), cancellationToken).ConfigureAwait(false);

        AdbCommandResult launch;
        if (!string.IsNullOrWhiteSpace(target.LaunchComponent))
        {
            launch = await RunShellAsync(selector, AdbShellSupport.BuildExplicitLaunchCommand(target.LaunchComponent), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            launch = await RunShellAsync(selector, AdbShellSupport.BuildMonkeyLaunchCommand(target.PackageId), cancellationToken).ConfigureAwait(false);
        }

        if (launch.ExitCode == 0 && !launch.CombinedOutput.Contains("Error:", StringComparison.OrdinalIgnoreCase))
        {
            return Success(
                $"Launch command sent for {target.Label}.",
                AdbShellSupport.Collapse(launch.CombinedOutput),
                packageId: target.PackageId);
        }

        return Failure($"Launch failed for {target.Label}.", launch.CombinedOutput, packageId: target.PackageId);
    }

    public async Task<OperationOutcome> OpenBrowserAsync(
        string url,
        QuestAppTarget browserTarget,
        CancellationToken cancellationToken = default)
    {
        var selector = GetRequiredSelector();
        var open = await RunShellAsync(selector, AdbShellSupport.BuildOpenUrlCommand(url, browserTarget.PackageId), cancellationToken).ConfigureAwait(false);
        return open.ExitCode == 0
            ? Success($"Browser open sent for {url}.", AdbShellSupport.Collapse(open.CombinedOutput), packageId: browserTarget.PackageId)
            : Failure("Browser open failed.", open.CombinedOutput, packageId: browserTarget.PackageId);
    }

    public async Task<OperationOutcome> QueryForegroundAsync(CancellationToken cancellationToken = default)
    {
        var selector = GetRequiredSelector();
        var activityOutput = await RunShellAsync(selector, "dumpsys activity activities", cancellationToken).ConfigureAwait(false);
        var snapshot = activityOutput.ExitCode == 0
            ? AdbShellSupport.ParseForegroundSnapshot(activityOutput.StdOut)
            : null;

        AdbCommandResult? windowOutput = null;
        if (snapshot is null)
        {
            windowOutput = await RunShellAsync(selector, "dumpsys window windows", cancellationToken).ConfigureAwait(false);
            snapshot = windowOutput.ExitCode == 0
                ? AdbShellSupport.ParseForegroundSnapshot(windowOutput.StdOut)
                : null;
        }

        if (snapshot is null && activityOutput.ExitCode != 0 && (windowOutput is null || windowOutput.ExitCode != 0))
        {
            return Failure("Foreground query failed.", activityOutput.CombinedOutput);
        }

        var rawOutput = snapshot is not null
            ? activityOutput.StdOut
            : windowOutput?.StdOut ?? activityOutput.StdOut;

        return snapshot is null
            ? new OperationOutcome(OperationOutcomeKind.Warning, "Foreground package could not be parsed.", AdbShellSupport.Collapse(rawOutput))
            : new OperationOutcome(OperationOutcomeKind.Success, $"Foreground package is {snapshot.PackageId}.", AdbShellSupport.Collapse(snapshot.PrimaryComponent), PackageId: snapshot.PackageId, Items: snapshot.VisibleComponents);
    }

    public async Task<HeadsetAppStatus> QueryHeadsetStatusAsync(
        QuestAppTarget? target,
        bool remoteOnlyControlEnabled,
        CancellationToken cancellationToken = default)
    {
        var selector = GetActiveSelector();
        if (string.IsNullOrWhiteSpace(selector))
        {
            return new HeadsetAppStatus(
                false,
                "No active ADB session.",
                "Unknown",
                null,
                null,
                null,
                string.Empty,
                false,
                false,
                false,
                remoteOnlyControlEnabled,
                DateTimeOffset.UtcNow,
                "Headset not connected.",
                "Probe USB or connect to a Quest Wi-Fi endpoint first.");
        }

        try
        {
            var model = await TryReadShellValueAsync(selector, "getprop ro.product.model", cancellationToken).ConfigureAwait(false);
            var batteryOutput = await RunShellAsync(selector, "dumpsys battery", cancellationToken).ConfigureAwait(false);
            var batteryLevel = batteryOutput.ExitCode == 0 ? AdbShellSupport.ParseBatteryLevel(batteryOutput.StdOut) : null;
            var cpuLevel = ParseOptionalInt(await TryReadShellValueAsync(selector, "getprop debug.oculus.cpuLevel", cancellationToken).ConfigureAwait(false));
            var gpuLevel = ParseOptionalInt(await TryReadShellValueAsync(selector, "getprop debug.oculus.gpuLevel", cancellationToken).ConfigureAwait(false));
            var activityOutput = await RunShellAsync(selector, "dumpsys activity activities", cancellationToken).ConfigureAwait(false);
            var foregroundSnapshot = activityOutput.ExitCode == 0
                ? AdbShellSupport.ParseForegroundSnapshot(activityOutput.StdOut)
                : null;

            if (foregroundSnapshot is null)
            {
                var windowOutput = await RunShellAsync(selector, "dumpsys window windows", cancellationToken).ConfigureAwait(false);
                foregroundSnapshot = windowOutput.ExitCode == 0
                    ? AdbShellSupport.ParseForegroundSnapshot(windowOutput.StdOut)
                    : null;
            }

            var foregroundPackage = foregroundSnapshot?.PackageId ?? string.Empty;
            var foregroundComponent = foregroundSnapshot?.PrimaryComponent ?? string.Empty;
            var visibleActivityComponents = foregroundSnapshot?.VisibleComponents ?? Array.Empty<string>();

            var targetInstalled = false;
            var targetRunning = false;
            var targetForeground = false;
            if (target is not null)
            {
                var installOutput = await RunShellAsync(selector, $"pm path {AdbShellSupport.Quote(target.PackageId)}", cancellationToken).ConfigureAwait(false);
                targetInstalled = installOutput.StdOut.Contains("package:", StringComparison.OrdinalIgnoreCase);

                var pid = await TryReadShellValueAsync(selector, $"pidof {AdbShellSupport.Quote(target.PackageId)}", cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pid))
                {
                    var ps = await RunShellAsync(selector, $"ps -A | grep {AdbShellSupport.Quote(target.PackageId)}", cancellationToken).ConfigureAwait(false);
                    targetRunning = ps.ExitCode == 0 && ps.StdOut.Contains(target.PackageId, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    targetRunning = true;
                }

                targetForeground = string.Equals(foregroundPackage, target.PackageId, StringComparison.OrdinalIgnoreCase);
            }

            var summary = target is null
                ? $"Quest connected on {selector}."
                : targetForeground
                    ? $"{target.Label} is in the foreground."
                    : targetRunning
                        ? $"{target.Label} is running in the background."
                        : targetInstalled
                            ? $"{target.Label} is installed but not running."
                            : $"{target.Label} is not installed on the headset.";

            var detail = $"Model {model}; battery {(batteryLevel is null ? "n/a" : $"{batteryLevel}%")}; CPU {(cpuLevel?.ToString() ?? "n/a")}; GPU {(gpuLevel?.ToString() ?? "n/a")}; foreground {(string.IsNullOrWhiteSpace(foregroundComponent) ? (string.IsNullOrWhiteSpace(foregroundPackage) ? "n/a" : foregroundPackage) : foregroundComponent)}.";

            return new HeadsetAppStatus(
                true,
                selector,
                string.IsNullOrWhiteSpace(model) ? "Quest" : model,
                batteryLevel,
                cpuLevel,
                gpuLevel,
                foregroundPackage,
                targetInstalled,
                targetRunning,
                targetForeground,
                remoteOnlyControlEnabled,
                DateTimeOffset.UtcNow,
                summary,
                detail,
                ForegroundComponent: foregroundComponent,
                VisibleActivityComponents: visibleActivityComponents);
        }
        catch (Exception ex)
        {
            return new HeadsetAppStatus(
                false,
                selector,
                "Unknown",
                null,
                null,
                null,
                string.Empty,
                false,
                false,
                false,
                remoteOnlyControlEnabled,
                DateTimeOffset.UtcNow,
                "Headset status query failed.",
                ex.Message);
        }
    }

    public async Task<OperationOutcome> RunUtilityAsync(QuestUtilityAction action, CancellationToken cancellationToken = default)
    {
        var selector = GetRequiredSelector();
        return action switch
        {
            QuestUtilityAction.Home => await RunUtilityShellAsync("Home command sent.", "input keyevent 3", cancellationToken).ConfigureAwait(false),
            QuestUtilityAction.Back => await RunUtilityShellAsync("Back command sent.", "input keyevent 4", cancellationToken).ConfigureAwait(false),
            QuestUtilityAction.Wake => await RunUtilityShellAsync("Wake command sent.", "input keyevent 224", cancellationToken).ConfigureAwait(false),
            QuestUtilityAction.Reboot => await RunRebootAsync(cancellationToken).ConfigureAwait(false),
            QuestUtilityAction.ListInstalledPackages => await ListInstalledPackagesAsync(selector, cancellationToken).ConfigureAwait(false),
            _ => Failure("Unknown utility action.", action.ToString())
        };
    }

    private async Task<OperationOutcome> RunUtilityShellAsync(string summary, string command, CancellationToken cancellationToken)
    {
        var selector = GetRequiredSelector();
        var output = await RunShellAsync(selector, command, cancellationToken).ConfigureAwait(false);
        return output.ExitCode == 0
            ? Success(summary, AdbShellSupport.Collapse(output.CombinedOutput))
            : Failure(summary, output.CombinedOutput);
    }

    private async Task<OperationOutcome> RunRebootAsync(CancellationToken cancellationToken)
    {
        var selector = GetRequiredSelector();
        var reboot = await RunAdbAsync(["-s", selector, "reboot"], cancellationToken).ConfigureAwait(false);
        return reboot.ExitCode == 0
            ? Success("Reboot command sent.", AdbShellSupport.Collapse(reboot.CombinedOutput))
            : Failure("Reboot command failed.", reboot.CombinedOutput);
    }

    private async Task<OperationOutcome> ListInstalledPackagesAsync(string selector, CancellationToken cancellationToken)
    {
        var output = await RunShellAsync(selector, "pm list packages", cancellationToken).ConfigureAwait(false);
        if (output.ExitCode != 0)
        {
            return Failure("Installed package query failed.", output.CombinedOutput);
        }

        var packages = AdbShellSupport.ParseInstalledPackages(output.StdOut);
        return new OperationOutcome(
            OperationOutcomeKind.Success,
            $"Listed {packages.Count} installed package(s).",
            AdbShellSupport.Collapse(output.StdOut),
            Items: packages);
    }

    private async Task<IReadOnlyList<AdbDeviceRecord>> ListDevicesAsync(CancellationToken cancellationToken)
    {
        var result = await RunAdbAsync(["devices", "-l"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return Array.Empty<AdbDeviceRecord>();
        }

        return result.StdOut
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseDeviceLine)
            .Where(record => record is not null)
            .Cast<AdbDeviceRecord>()
            .ToArray();
    }

    private static AdbDeviceRecord? ParseDeviceLine(string line)
    {
        var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        return new AdbDeviceRecord(parts[0], parts[1], line, parts[0].Contains(':'));
    }

    private async Task<string> EnsureUsbSelectorAsync(CancellationToken cancellationToken)
    {
        string? selector;
        lock (_sync)
        {
            selector = _lastUsbSelector;
        }

        if (!string.IsNullOrWhiteSpace(selector))
        {
            return selector;
        }

        var probe = await ProbeUsbAsync(cancellationToken).ConfigureAwait(false);
        if (probe.Kind == OperationOutcomeKind.Failure || string.IsNullOrWhiteSpace(probe.Endpoint))
        {
            throw new InvalidOperationException("No USB ADB device is available.");
        }

        return probe.Endpoint!;
    }

    private string GetRequiredSelector()
    {
        var selector = GetActiveSelector();
        if (string.IsNullOrWhiteSpace(selector))
        {
            throw new InvalidOperationException("No active Quest ADB session is available.");
        }

        return selector;
    }

    private string? GetActiveSelector()
    {
        lock (_sync)
        {
            return _activeSelector ?? _lastUsbSelector;
        }
    }

    private string? GetRememberedTcpSelector()
    {
        lock (_sync)
        {
            if (LooksLikeTcpSelector(_activeSelector))
            {
                return _activeSelector;
            }

            return _lastTcpSelector;
        }
    }

    private async Task<string> TryReadShellValueAsync(string selector, string command, CancellationToken cancellationToken)
    {
        var result = await RunShellAsync(selector, command, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.StdOut.Trim() : string.Empty;
    }

    private async Task<AdbCommandResult> RunShellAsync(string selector, string command, CancellationToken cancellationToken)
        => await RunAdbAsync(["-s", selector, "shell", command], cancellationToken).ConfigureAwait(false);

    private async Task<AdbCommandResult> RunAdbAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _adbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new AdbCommandResult(process.ExitCode, stdout, stderr);
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Quest endpoint is required.", nameof(endpoint));
        }

        return trimmed.Contains(':', StringComparison.Ordinal) ? trimmed : $"{trimmed}:5555";
    }

    private static bool LooksLikeTcpSelector(string? selector)
        => !string.IsNullOrWhiteSpace(selector) && selector.Contains(':', StringComparison.Ordinal);

    private static string? ResolveExistingPath(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(candidate);
        return File.Exists(fullPath) ? fullPath : null;
    }

    private static int? ParseOptionalInt(string value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    private static OperationOutcome Success(
        string summary,
        string detail,
        string? endpoint = null,
        string? packageId = null,
        IReadOnlyList<string>? items = null)
        => new(OperationOutcomeKind.Success, summary, detail, endpoint, packageId, items);

    private static OperationOutcome Failure(
        string summary,
        string detail,
        string? endpoint = null,
        string? packageId = null,
        IReadOnlyList<string>? items = null)
        => new(OperationOutcomeKind.Failure, summary, detail, endpoint, packageId, items);

    private sealed record AdbDeviceRecord(string Serial, string State, string RawLine, bool IsTcp);

    private sealed record AdbCommandResult(int ExitCode, string StdOut, string StdErr)
    {
        public string CombinedOutput
        {
            get
            {
                var builder = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(StdOut))
                {
                    builder.Append(StdOut.Trim());
                }

                if (!string.IsNullOrWhiteSpace(StdErr))
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.Append(StdErr.Trim());
                }

                return builder.ToString();
            }
        }
    }
}
