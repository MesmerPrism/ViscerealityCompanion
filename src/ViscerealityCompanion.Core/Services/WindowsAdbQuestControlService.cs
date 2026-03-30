using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class WindowsAdbQuestControlService : IQuestControlService
{
    private const string ProfileBrightnessPercentKey = "viscereality.screen_brightness_percent";
    private const string ProfileMediaVolumeKey = "viscereality.media_volume_music";
    private const string ProfileHeadsetBatteryMinimumKey = "viscereality.minimum_headset_battery_percent";
    private const string ProfileRightControllerBatteryMinimumKey = "viscereality.minimum_right_controller_battery_percent";
    private const string QuestSensorLockPackage = "com.oculus.os.vrlockscreen";
    private const string QuestSensorLockActivity = "SensorLockActivity";
    private const string QuestClearActivityPackage = "com.oculus.os.clearactivity";
    private const string QuestClearActivity = "ClearActivity";
    private const string QuestGuardianPackage = "com.oculus.guardian";
    private const string QuestGuardianDialogActivity = "GuardianDialogActivity";
    private const string QuestHomePackage = "com.oculus.vrshell";
    private const string QuestHomeActivity = "HomeActivity";
    private const string QuestMainActivity = "MainActivity";
    private const string QuestFocusPlaceholderActivity = "FocusPlaceholderActivity";
    private const string QuestSystemUxPackage = "com.oculus.systemux";
    private const string QuestVirtualObjectsActivity = "VirtualObjectsActivity";
    private const string QuestQuickSettingsPackage = "com.oculus.panelapp.settings";
    private const string QuestQuickSettingsActivity = "QuickSettingsActivity";
    private const string QuestVrPowerManagerAutomationDisableAction = "com.oculus.vrpowermanager.automation_disable";
    private const string QuestVrPowerManagerProxCloseAction = "com.oculus.vrpowermanager.prox_close";
    private static readonly TimeSpan WakeGuardianRecoveryStepDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan WakePowerRecoveryPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan KioskVerificationPollInterval = TimeSpan.FromMilliseconds(500);
    private const int WakePowerRecoveryPollCount = 6;
    private const int KioskVerificationPollCount = 6;

    private readonly string _adbPath;
    private readonly Lock _sync = new();
    private string? _activeSelector;
    private string? _lastUsbSelector;
    private string? _lastTcpSelector;
    private QuestWakeResumeTarget? _lastWakeResumeTarget;
    private HostWifiStatus _lastKnownHostWifiStatus = new(string.Empty, string.Empty);

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
            OperationOutcomeKind.Success,
            "Quest switched to TCP/IP mode on port 5555.",
            string.IsNullOrWhiteSpace(endpoint)
                ? "Wi-Fi ADB bootstrap completed. If the session remains on USB, reconnect from the companion before removing the cable."
                : "Wi-Fi ADB bootstrap completed.",
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

        var selector = await EnsureSelectorAsync(cancellationToken).ConfigureAwait(false);
        var wakeOutcome = await WakeSelectorBeforeActionAsync(selector, "performance update", cancellationToken).ConfigureAwait(false);
        if (IsWakeFailure(wakeOutcome))
        {
            return wakeOutcome!;
        }

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
            return MergeWakeWarning(
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    $"Quest performance request sent: CPU {cpuLevel}, GPU {gpuLevel}.",
                    $"Read back CPU `{readCpu}` and GPU `{readGpu}`."),
                wakeOutcome);
        }

        return MergeWakeWarning(
            Success(
                $"Applied Quest performance levels: CPU {cpuLevel}, GPU {gpuLevel}.",
                $"Quest reported CPU {readCpu} and GPU {readGpu}."),
            wakeOutcome);
    }

    public async Task<OperationOutcome> InstallAppAsync(QuestAppTarget target, CancellationToken cancellationToken = default)
    {
        var selector = await EnsureSelectorAsync(cancellationToken).ConfigureAwait(false);
        var wakeOutcome = await WakeSelectorBeforeActionAsync(selector, $"installing {target.Label}", cancellationToken).ConfigureAwait(false);
        if (IsWakeFailure(wakeOutcome))
        {
            return wakeOutcome!;
        }

        var apkPath = ResolveExistingPath(target.ApkFile);
        if (apkPath is null)
        {
            return Failure($"APK not found for {target.Label}.", $"Expected file path: {target.ApkFile}", packageId: target.PackageId);
        }

        var install = await RunAdbAsync(["-s", selector, "install", "-r", "-d", "-g", apkPath], cancellationToken).ConfigureAwait(false);
        var verify = await RunShellAsync(selector, $"pm path {AdbShellSupport.Quote(target.PackageId)}", cancellationToken).ConfigureAwait(false);
        var installSucceeded = install.CombinedOutput.Contains("Success", StringComparison.OrdinalIgnoreCase);
        var packagePresent = verify.StdOut.Contains("package:", StringComparison.OrdinalIgnoreCase);

        if (packagePresent || (install.ExitCode == 0 && installSucceeded))
        {
            var detail = packagePresent
                ? AdbShellSupport.Collapse($"{install.CombinedOutput} {verify.CombinedOutput}")
                : AdbShellSupport.Collapse(install.CombinedOutput);

            return MergeWakeWarning(
                Success(
                    $"Installed {target.Label}.",
                    detail,
                    packageId: target.PackageId),
                wakeOutcome);
        }

        return MergeWakeWarning(
            Failure(
                $"Install failed for {target.Label}.",
                $"{AdbShellSupport.Collapse(install.CombinedOutput)} {AdbShellSupport.Collapse(verify.CombinedOutput)}",
                packageId: target.PackageId),
            wakeOutcome);
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
        var selector = await EnsureSelectorAsync(cancellationToken).ConfigureAwait(false);
        var wakeOutcome = await WakeSelectorBeforeActionAsync(selector, $"pushing {profile.Label}", cancellationToken).ConfigureAwait(false);
        if (IsWakeFailure(wakeOutcome))
        {
            return wakeOutcome!;
        }

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
            return MergeWakeWarning(
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    $"Push sent for {profile.Label} but verification failed.",
                    AdbShellSupport.Collapse(verify.CombinedOutput),
                    PackageId: target.PackageId),
                wakeOutcome);
        }

        return MergeWakeWarning(
            Success(
                $"Pushed hotload profile {profile.Label} to {target.PackageId}.",
                $"Device path: {deviceFile}. {AdbShellSupport.Collapse(push.CombinedOutput)}",
                packageId: target.PackageId),
            wakeOutcome);
    }

    public async Task<OperationOutcome> ApplyDeviceProfileAsync(DeviceProfile profile, CancellationToken cancellationToken = default)
    {
        var selector = await EnsureSelectorAsync(cancellationToken).ConfigureAwait(false);
        var wakeOutcome = await WakeSelectorBeforeActionAsync(selector, $"applying {profile.Label}", cancellationToken).ConfigureAwait(false);
        if (IsWakeFailure(wakeOutcome))
        {
            return wakeOutcome!;
        }

        var applied = new List<string>();

        foreach (var pair in profile.Properties)
        {
            var applyOutcome = await ApplyDeviceProfilePropertyAsync(selector, pair.Key, pair.Value, cancellationToken).ConfigureAwait(false);
            if (applyOutcome.Kind == OperationOutcomeKind.Failure)
            {
                return Failure($"Device profile failed at {pair.Key}.", applyOutcome.Detail);
            }

            var status = await QueryDeviceProfilePropertyStatusAsync(selector, pair.Key, pair.Value, cancellationToken).ConfigureAwait(false);
            if (!status.Matches)
            {
                return MergeWakeWarning(
                    new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        $"Device profile partially applied: {profile.Label}.",
                        $"Expected {pair.Key}={pair.Value} but Quest reported `{status.ReportedValue}`."),
                    wakeOutcome);
            }

            applied.Add($"{FormatDeviceProfilePropertyLabel(pair.Key)}={status.ReportedValue}");
        }

        return MergeWakeWarning(
            new OperationOutcome(
                OperationOutcomeKind.Success,
                $"Applied device profile {profile.Label}.",
                string.Join("; ", applied),
                Items: applied),
            wakeOutcome);
    }

    public async Task<OperationOutcome> LaunchAppAsync(QuestAppTarget target, bool kioskMode = false, CancellationToken cancellationToken = default)
    {
        var selector = await EnsureSelectorAsync(cancellationToken).ConfigureAwait(false);
        var wakeOutcome = await WakeSelectorBeforeActionAsync(selector, $"launching {target.Label}", cancellationToken).ConfigureAwait(false);
        if (IsWakeFailure(wakeOutcome))
        {
            return wakeOutcome!;
        }

        var launchOutcome = await LaunchAppCoreAsync(selector, target, cancellationToken).ConfigureAwait(false);
        if (launchOutcome.Kind == OperationOutcomeKind.Failure)
        {
            return MergeWakeWarning(launchOutcome, wakeOutcome);
        }

        if (!kioskMode)
        {
            return MergeWakeWarning(launchOutcome, wakeOutcome);
        }

        var kioskOutcome = await EnterKioskModeAsync(selector, target, launchOutcome, cancellationToken).ConfigureAwait(false);
        return MergeWakeWarning(kioskOutcome, wakeOutcome);
    }

    public async Task<OperationOutcome> StopAppAsync(QuestAppTarget target, bool exitKioskMode = false, CancellationToken cancellationToken = default)
    {
        var selector = await EnsureSelectorAsync(cancellationToken).ConfigureAwait(false);
        if (exitKioskMode)
        {
            return await ExitKioskModeAsync(selector, target, cancellationToken).ConfigureAwait(false);
        }

        var wakeOutcome = await WakeSelectorBeforeActionAsync(selector, $"stopping {target.Label}", cancellationToken).ConfigureAwait(false);
        if (IsWakeFailure(wakeOutcome))
        {
            return wakeOutcome!;
        }

        var stop = await RunShellAsync(selector, AdbShellSupport.BuildForceStopCommand(target.PackageId), cancellationToken).ConfigureAwait(false);
        var outcome = stop.ExitCode == 0
            ? Success(
                $"Stop command sent for {target.Label}.",
                AdbShellSupport.Collapse(stop.CombinedOutput),
                packageId: target.PackageId)
            : Failure($"Stop failed for {target.Label}.", stop.CombinedOutput, packageId: target.PackageId);
        return MergeWakeWarning(outcome, wakeOutcome);
    }

    private async Task<OperationOutcome> LaunchAppCoreAsync(
        string selector,
        QuestAppTarget target,
        CancellationToken cancellationToken)
    {
        var launchTrace = new List<string>();
        var (readiness, _, _) = await QueryWakeReadinessSnapshotAsync(selector, cancellationToken).ConfigureAwait(false);
        if (IsGuardianAutomationRecoveryCandidate(readiness))
        {
            launchTrace.Add($"Pre-launch: {readiness.Detail}");
            readiness = await TryRecoverWakeGuardianTrackingLossAsync(selector, readiness, launchTrace, cancellationToken).ConfigureAwait(false);

            if (readiness.IsInWakeLimbo && IsVisibleWakeBlockingOverlayComponent(readiness.WakeLimboComponent))
            {
                var stopGuardian = await RunShellAsync(
                    selector,
                    AdbShellSupport.BuildForceStopCommand(QuestGuardianPackage),
                    cancellationToken).ConfigureAwait(false);
                launchTrace.Add($"Force-stopped {QuestGuardianPackage}. {AdbShellSupport.Collapse(stopGuardian.CombinedOutput)}".Trim());
                await Task.Delay(WakeGuardianRecoveryStepDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        var launchTraceDetail = launchTrace.Count == 0 ? string.Empty : string.Join(" ", launchTrace);
        await RunShellAsync(selector, AdbShellSupport.BuildForceStopCommand(target.PackageId), cancellationToken).ConfigureAwait(false);

        var monkeyLaunch = await RunShellAsync(selector, AdbShellSupport.BuildMonkeyLaunchCommand(target.PackageId), cancellationToken).ConfigureAwait(false);
        if (LaunchSucceeded(monkeyLaunch))
        {
            return Success(
                $"Launch command sent for {target.Label}.",
                $"{launchTraceDetail} {AdbShellSupport.Collapse(monkeyLaunch.CombinedOutput)}".Trim(),
                packageId: target.PackageId);
        }

        if (!string.IsNullOrWhiteSpace(target.LaunchComponent))
        {
            var explicitLaunch = await RunShellAsync(selector, AdbShellSupport.BuildExplicitLaunchCommand(target.LaunchComponent), cancellationToken).ConfigureAwait(false);
            if (LaunchSucceeded(explicitLaunch))
            {
                return Success(
                    $"Launch command sent for {target.Label}.",
                    $"{launchTraceDetail} {AdbShellSupport.Collapse(explicitLaunch.CombinedOutput)}".Trim(),
                    packageId: target.PackageId);
            }

            return Failure(
                $"Launch failed for {target.Label}.",
                $"{launchTraceDetail} {AdbShellSupport.Collapse(monkeyLaunch.CombinedOutput)} {AdbShellSupport.Collapse(explicitLaunch.CombinedOutput)}".Trim(),
                packageId: target.PackageId);
        }

        return Failure(
            $"Launch failed for {target.Label}.",
            $"{launchTraceDetail} {monkeyLaunch.CombinedOutput}".Trim(),
            packageId: target.PackageId);
    }

    private async Task<OperationOutcome> EnterKioskModeAsync(
        string selector,
        QuestAppTarget target,
        OperationOutcome launchOutcome,
        CancellationToken cancellationToken)
    {
        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(launchOutcome.Detail))
        {
            detailParts.Add(launchOutcome.Detail);
        }

        await Task.Delay(KioskVerificationPollInterval, cancellationToken).ConfigureAwait(false);
        var taskId = await ResolveRecentTaskIdAsync(selector, target.PackageId, cancellationToken).ConfigureAwait(false);
        if (!taskId.HasValue)
        {
            detailParts.Add("Quest did not expose a live task id in recents after launch, so kiosk pinning was not verified.");
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                $"Launch completed for {target.Label}, but kiosk mode was not confirmed.",
                string.Join(" ", detailParts),
                PackageId: target.PackageId);
        }

        var lockTask = await RunShellAsync(selector, AdbShellSupport.BuildTaskLockCommand(taskId.Value), cancellationToken).ConfigureAwait(false);
        var verification = await VerifyPinnedForegroundAsync(selector, target.PackageId, cancellationToken).ConfigureAwait(false);
        if (verification.Succeeded)
        {
            detailParts.Add($"Pinned task {taskId.Value} in front for kiosk mode.");
            if (!string.IsNullOrWhiteSpace(verification.Detail))
            {
                detailParts.Add(verification.Detail);
            }

            return new OperationOutcome(
                OperationOutcomeKind.Success,
                $"Launched {target.Label} in kiosk mode.",
                string.Join(" ", detailParts),
                PackageId: target.PackageId,
                Items: [$"taskId={taskId.Value}"]);
        }

        detailParts.Add($"Sent task-lock pin for task {taskId.Value}. {AdbShellSupport.Collapse(lockTask.CombinedOutput)}".Trim());
        detailParts.Add(verification.Detail);
        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            $"Launch completed for {target.Label}, but kiosk mode was not fully confirmed.",
            string.Join(" ", detailParts),
            PackageId: target.PackageId,
            Items: [$"taskId={taskId.Value}"]);
    }

    private async Task<OperationOutcome> ExitKioskModeAsync(
        string selector,
        QuestAppTarget target,
        CancellationToken cancellationToken)
    {
        var detailParts = new List<string>();

        var automationDisable = await RunShellAsync(
            selector,
            AdbShellSupport.BuildBroadcastActionCommand(QuestVrPowerManagerAutomationDisableAction),
            cancellationToken).ConfigureAwait(false);
        detailParts.Add($"Sent {QuestVrPowerManagerAutomationDisableAction}. {AdbShellSupport.Collapse(automationDisable.CombinedOutput)}".Trim());

        var stopLock = await RunShellAsync(selector, AdbShellSupport.BuildTaskLockStopCommand(), cancellationToken).ConfigureAwait(false);
        detailParts.Add($"Stopped lock-task mode. {AdbShellSupport.Collapse(stopLock.CombinedOutput)}".Trim());

        var home = await RunShellAsync(
            selector,
            AdbShellSupport.BuildHomeLaunchCommand($"{QuestHomePackage}/.{QuestHomeActivity}"),
            cancellationToken).ConfigureAwait(false);
        detailParts.Add($"Brought {QuestHomePackage}/.{QuestHomeActivity} to the front. {AdbShellSupport.Collapse(home.CombinedOutput)}".Trim());

        var stop = await RunShellAsync(selector, AdbShellSupport.BuildForceStopCommand(target.PackageId), cancellationToken).ConfigureAwait(false);
        detailParts.Add($"Stopped {target.PackageId}. {AdbShellSupport.Collapse(stop.CombinedOutput)}".Trim());

        var verification = await VerifyHomeForegroundAsync(selector, target.PackageId, cancellationToken).ConfigureAwait(false);
        detailParts.Add(verification.Detail);
        return new OperationOutcome(
            verification.Succeeded ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning,
            verification.Succeeded
                ? $"Exited kiosk mode for {target.Label}."
                : $"Sent kiosk-exit sequence for {target.Label}, but Home was not fully confirmed.",
            string.Join(" ", detailParts),
            PackageId: target.PackageId);
    }

    public async Task<OperationOutcome> OpenBrowserAsync(
        string url,
        QuestAppTarget browserTarget,
        CancellationToken cancellationToken = default)
    {
        var selector = await EnsureSelectorAsync(cancellationToken).ConfigureAwait(false);
        var wakeOutcome = await WakeSelectorBeforeActionAsync(selector, "opening the browser", cancellationToken).ConfigureAwait(false);
        if (IsWakeFailure(wakeOutcome))
        {
            return wakeOutcome!;
        }

        var open = await RunShellAsync(selector, AdbShellSupport.BuildOpenUrlCommand(url, browserTarget.PackageId), cancellationToken).ConfigureAwait(false);
        var outcome = open.ExitCode == 0
            ? Success($"Browser open sent for {url}.", AdbShellSupport.Collapse(open.CombinedOutput), packageId: browserTarget.PackageId)
            : Failure("Browser open failed.", open.CombinedOutput, packageId: browserTarget.PackageId);
        return MergeWakeWarning(outcome, wakeOutcome);
    }

    public async Task<OperationOutcome> QueryForegroundAsync(CancellationToken cancellationToken = default)
    {
        var selector = await EnsureSelectorAsync(cancellationToken).ConfigureAwait(false);
        var (windowOutput, snapshot) = await QueryForegroundSnapshotAsync(selector, cancellationToken).ConfigureAwait(false);
        if (snapshot is null && windowOutput.ExitCode != 0)
        {
            return Failure("Foreground query failed.", windowOutput.CombinedOutput);
        }

        return snapshot is null
            ? new OperationOutcome(OperationOutcomeKind.Warning, "Foreground package could not be parsed.", AdbShellSupport.Collapse(windowOutput.StdOut))
            : new OperationOutcome(OperationOutcomeKind.Success, $"Foreground package is {snapshot.PackageId}.", AdbShellSupport.Collapse(snapshot.PrimaryComponent), PackageId: snapshot.PackageId, Items: snapshot.VisibleComponents);
    }

    public async Task<InstalledAppStatus> QueryInstalledAppAsync(
        QuestAppTarget target,
        CancellationToken cancellationToken = default)
    {
        var selector = await ResolveResponsiveSelectorAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(selector))
        {
            return new InstalledAppStatus(
                target.PackageId,
                IsInstalled: false,
                VersionName: string.Empty,
                VersionCode: string.Empty,
                InstalledSha256: string.Empty,
                InstalledPath: string.Empty,
                Summary: "No active ADB session.",
                Detail: "Connect to a Quest before checking the installed study build.");
        }

        try
        {
            var installOutput = await RunShellAsync(selector, $"pm path {AdbShellSupport.Quote(target.PackageId)}", cancellationToken).ConfigureAwait(false);
            if (installOutput.ExitCode != 0)
            {
                return new InstalledAppStatus(
                    target.PackageId,
                    IsInstalled: false,
                    VersionName: string.Empty,
                    VersionCode: string.Empty,
                    InstalledSha256: string.Empty,
                    InstalledPath: string.Empty,
                    Summary: $"Installed-build check failed for {target.Label}.",
                    Detail: AdbShellSupport.Collapse(installOutput.CombinedOutput));
            }

            if (!installOutput.StdOut.Contains("package:", StringComparison.OrdinalIgnoreCase))
            {
                return new InstalledAppStatus(
                    target.PackageId,
                    IsInstalled: false,
                    VersionName: string.Empty,
                    VersionCode: string.Empty,
                    InstalledSha256: string.Empty,
                    InstalledPath: string.Empty,
                    Summary: $"{target.Label} is not installed.",
                    Detail: AdbShellSupport.Collapse(installOutput.CombinedOutput));
            }

            var packagePath = installOutput.StdOut
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.StartsWith("package:", StringComparison.OrdinalIgnoreCase) ? line["package:".Length..].Trim() : string.Empty)
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
                ?? string.Empty;

            var packageDump = await RunShellAsync(selector, $"dumpsys package {AdbShellSupport.Quote(target.PackageId)}", cancellationToken).ConfigureAwait(false);
            var versionName = ParseDumpsysValue(packageDump.StdOut, "versionName");
            var versionCode = ParseVersionCode(packageDump.StdOut);
            var installedSha256 = string.Empty;
            var detail = new List<string>();

            if (!string.IsNullOrWhiteSpace(packagePath))
            {
                var tempPath = Path.Combine(
                    Path.GetTempPath(),
                    "ViscerealityCompanion",
                    "package-hash",
                    $"{SanitizeFileToken(target.PackageId)}.apk");
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

                try
                {
                    var pull = await RunAdbAsync(["-s", selector, "pull", packagePath, tempPath], cancellationToken).ConfigureAwait(false);
                    if (pull.ExitCode == 0 && File.Exists(tempPath))
                    {
                        await using var stream = File.OpenRead(tempPath);
                        using var sha256 = SHA256.Create();
                        var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
                        installedSha256 = Convert.ToHexString(hash);
                    }
                    else
                    {
                        detail.Add($"Could not pull {packagePath} to compute a build hash.");
                    }
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(installedSha256))
            {
                detail.Add("Installed package hash unavailable.");
            }

            if (!string.IsNullOrWhiteSpace(versionName))
            {
                detail.Add($"versionName={versionName}");
            }

            if (!string.IsNullOrWhiteSpace(versionCode))
            {
                detail.Add($"versionCode={versionCode}");
            }

            if (!string.IsNullOrWhiteSpace(packagePath))
            {
                detail.Add($"path={packagePath}");
            }

            return new InstalledAppStatus(
                target.PackageId,
                IsInstalled: true,
                VersionName: versionName,
                VersionCode: versionCode,
                InstalledSha256: installedSha256,
                InstalledPath: packagePath,
                Summary: $"{target.Label} is installed on the headset.",
                Detail: string.Join("; ", detail));
        }
        catch (Exception ex)
        {
            return new InstalledAppStatus(
                target.PackageId,
                IsInstalled: false,
                VersionName: string.Empty,
                VersionCode: string.Empty,
                InstalledSha256: string.Empty,
                InstalledPath: string.Empty,
                Summary: $"Installed-build check failed for {target.Label}.",
                Detail: ex.Message);
        }
    }

    public async Task<DeviceProfileStatus> QueryDeviceProfileStatusAsync(
        DeviceProfile profile,
        CancellationToken cancellationToken = default)
    {
        var selector = await ResolveResponsiveSelectorAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(selector))
        {
            return new DeviceProfileStatus(
                profile.Id,
                profile.Label,
                IsActive: false,
                Summary: "No active ADB session.",
                Detail: "Connect to a Quest before checking the pinned device profile.",
                Properties: profile.Properties
                    .Select(pair => new DevicePropertyStatus(pair.Key, pair.Value, string.Empty, Matches: false))
                    .ToArray());
        }

        try
        {
            var checks = new List<DevicePropertyStatus>(profile.Properties.Count);
            foreach (var pair in profile.Properties)
            {
                checks.Add(await QueryDeviceProfilePropertyStatusAsync(selector, pair.Key, pair.Value, cancellationToken).ConfigureAwait(false));
            }

            var isActive = checks.Count > 0 && checks.All(check => check.Matches);
            var matchedCount = checks.Count(check => check.Matches);
            return new DeviceProfileStatus(
                profile.Id,
                profile.Label,
                isActive,
                isActive
                    ? $"{profile.Label} is active on the headset."
                    : $"{profile.Label} is not fully active on the headset.",
                $"Matched {matchedCount}/{checks.Count} pinned device properties.",
                checks);
        }
        catch (Exception ex)
        {
            return new DeviceProfileStatus(
                profile.Id,
                profile.Label,
                IsActive: false,
                Summary: $"Device-profile check failed for {profile.Label}.",
                Detail: ex.Message,
                Properties: profile.Properties
                    .Select(pair => new DevicePropertyStatus(pair.Key, pair.Value, string.Empty, Matches: false))
                    .ToArray());
        }
    }

    public async Task<HeadsetAppStatus> QueryHeadsetStatusAsync(
        QuestAppTarget? target,
        bool remoteOnlyControlEnabled,
        bool includeHostWifiStatus = true,
        CancellationToken cancellationToken = default)
    {
        var selector = await ResolveResponsiveSelectorAsync(cancellationToken).ConfigureAwait(false);
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
            var visibleDevices = await ListDevicesAsync(cancellationToken).ConfigureAwait(false);
            var visibleUsbSerial = visibleDevices
                .Where(device => !device.IsTcp && string.Equals(device.State, "device", StringComparison.OrdinalIgnoreCase))
                .Select(device => device.Serial)
                .FirstOrDefault() ?? string.Empty;
            var isUsbAdbVisible = !string.IsNullOrWhiteSpace(visibleUsbSerial);
            var isWifiAdbTransport = LooksLikeTcpSelector(selector);
            var modelOutput = await RunShellAsync(selector, "getprop ro.product.model", cancellationToken).ConfigureAwait(false);
            var model = modelOutput.ExitCode == 0 ? modelOutput.StdOut.Trim() : string.Empty;
            var batteryOutput = await RunShellAsync(selector, "dumpsys battery", cancellationToken).ConfigureAwait(false);
            var batteryLevel = batteryOutput.ExitCode == 0 ? AdbShellSupport.ParseBatteryLevel(batteryOutput.StdOut) : null;
            var trackingOutput = await RunShellAsync(selector, "dumpsys tracking", cancellationToken).ConfigureAwait(false);
            var controllerStatuses = trackingOutput.ExitCode == 0
                ? ParseControllerStatuses(trackingOutput.StdOut)
                : Array.Empty<QuestControllerStatus>();
            var softwareReleaseOrCodename = await TryReadShellValueAsync(
                selector,
                "getprop ro.build.version.release_or_codename",
                cancellationToken).ConfigureAwait(false);
            var softwareBuildId = await TryReadShellValueAsync(
                selector,
                "getprop ro.build.version.incremental",
                cancellationToken).ConfigureAwait(false);
            var softwareDisplayId = await TryReadShellValueAsync(
                selector,
                "getprop ro.build.display.id",
                cancellationToken).ConfigureAwait(false);
            var softwareVersion = FormatSoftwareVersion(
                softwareReleaseOrCodename,
                softwareBuildId,
                softwareDisplayId);
            var brightnessStatus = await QueryScreenBrightnessStatusAsync(selector, cancellationToken).ConfigureAwait(false);
            var mediaVolumeStatus = await QueryMediaVolumeStatusAsync(selector, cancellationToken).ConfigureAwait(false);
            var cpuLevel = ParseOptionalInt(await TryReadShellValueAsync(selector, "getprop debug.oculus.cpuLevel", cancellationToken).ConfigureAwait(false));
            var gpuLevel = ParseOptionalInt(await TryReadShellValueAsync(selector, "getprop debug.oculus.gpuLevel", cancellationToken).ConfigureAwait(false));
            var powerOutput = await RunShellAsync(selector, "dumpsys power", cancellationToken).ConfigureAwait(false);
            var powerStatus = powerOutput.ExitCode == 0
                ? ParseQuestPowerStatus(powerOutput.StdOut)
                : new QuestPowerStatus(string.Empty, null, string.Empty, null, "Quest power-state readback unavailable.");
            var (_, foregroundSnapshot) = await QueryForegroundSnapshotAsync(selector, cancellationToken).ConfigureAwait(false);
            var wakeReadiness = EvaluateWakeReadiness(powerStatus, foregroundSnapshot);
            var questWifiStatus = await QueryQuestWifiStatusAsync(selector, cancellationToken).ConfigureAwait(false);
            var hostWifiStatus = ResolveHostWifiStatus(includeHostWifiStatus);

            if (modelOutput.ExitCode != 0 && batteryOutput.ExitCode != 0 && powerOutput.ExitCode != 0)
            {
                var failureDetail = string.Join(
                    " ",
                    new[]
                    {
                        AdbShellSupport.Collapse(modelOutput.CombinedOutput),
                        AdbShellSupport.Collapse(batteryOutput.CombinedOutput),
                        AdbShellSupport.Collapse(powerOutput.CombinedOutput)
                    }.Where(part => !string.IsNullOrWhiteSpace(part)));

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
                    string.IsNullOrWhiteSpace(failureDetail)
                        ? "ADB shell queries did not return a readable headset state."
                        : failureDetail);
            }

            var foregroundPackage = foregroundSnapshot?.PackageId ?? string.Empty;
            var foregroundComponent = foregroundSnapshot?.PrimaryComponent ?? string.Empty;
            var visibleActivityComponents = foregroundSnapshot?.VisibleComponents ?? Array.Empty<string>();
            RememberWakeResumeTarget(ResolveWakeResumeTarget(foregroundSnapshot));
            var headsetWifiIpAddress = string.IsNullOrWhiteSpace(questWifiStatus.IpAddress)
                ? ExtractIpAddressFromSelector(selector)
                : questWifiStatus.IpAddress;
            bool? wifiSsidMatchesHost =
                !string.IsNullOrWhiteSpace(questWifiStatus.Ssid) && !string.IsNullOrWhiteSpace(hostWifiStatus.Ssid)
                    ? string.Equals(questWifiStatus.Ssid, hostWifiStatus.Ssid, StringComparison.Ordinal)
                    : null;

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
                    ? $"{target.Label} is active."
                    : targetRunning
                        ? $"{target.Label} is running in the background."
                        : targetInstalled
                            ? $"{target.Label} is installed but not running."
                            : $"{target.Label} is not installed on the headset.";

            var detailParts = new List<string>
            {
                $"Model {model}",
                $"battery {(batteryLevel is null ? "n/a" : $"{batteryLevel}%")}",
                $"CPU {(cpuLevel?.ToString() ?? "n/a")}",
                $"GPU {(gpuLevel?.ToString() ?? "n/a")}",
                $"active {(string.IsNullOrWhiteSpace(foregroundComponent) ? (string.IsNullOrWhiteSpace(foregroundPackage) ? "n/a" : foregroundPackage) : foregroundComponent)}"
            };

            if (brightnessStatus.Percent.HasValue)
            {
                detailParts.Add($"brightness {brightnessStatus.Percent.Value}%");
            }

            if (mediaVolumeStatus.Level.HasValue)
            {
                detailParts.Add(
                    mediaVolumeStatus.MaxLevel.HasValue
                        ? $"volume {mediaVolumeStatus.Level.Value}/{mediaVolumeStatus.MaxLevel.Value}"
                        : $"volume {mediaVolumeStatus.Level.Value}");
            }

            if (!string.IsNullOrWhiteSpace(wakeReadiness.Detail))
            {
                detailParts.Add($"power {wakeReadiness.Detail}");
            }

            if (controllerStatuses.Count > 0)
            {
                detailParts.Add($"controllers {string.Join(", ", controllerStatuses.Select(FormatControllerDetail))}");
            }

            if (!string.IsNullOrWhiteSpace(softwareVersion))
            {
                detailParts.Add($"OS {softwareVersion}");
            }

            if (!string.IsNullOrWhiteSpace(questWifiStatus.Ssid) || !string.IsNullOrWhiteSpace(headsetWifiIpAddress) || !string.IsNullOrWhiteSpace(hostWifiStatus.Ssid))
            {
                var headsetWifiLabel = string.IsNullOrWhiteSpace(questWifiStatus.Ssid)
                    ? "n/a"
                    : questWifiStatus.Ssid;
                if (!string.IsNullOrWhiteSpace(headsetWifiIpAddress))
                {
                    headsetWifiLabel = $"{headsetWifiLabel} ({headsetWifiIpAddress})";
                }

                var hostWifiLabel = string.IsNullOrWhiteSpace(hostWifiStatus.Ssid)
                    ? "n/a"
                    : hostWifiStatus.Ssid;
                var matchLabel = wifiSsidMatchesHost switch
                {
                    true => "match",
                    false => "do not match",
                    _ => "unknown"
                };

                detailParts.Add($"headset Wi-Fi {headsetWifiLabel}");
                detailParts.Add($"host Wi-Fi {hostWifiLabel}");
                detailParts.Add($"SSIDs {matchLabel}");
            }

            var detail = string.Join("; ", detailParts) + ".";

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
                VisibleActivityComponents: visibleActivityComponents,
                IsWifiAdbTransport: isWifiAdbTransport,
                HeadsetWifiSsid: questWifiStatus.Ssid,
                HeadsetWifiIpAddress: headsetWifiIpAddress,
                HostWifiSsid: hostWifiStatus.Ssid,
                WifiSsidMatchesHost: wifiSsidMatchesHost,
                IsAwake: wakeReadiness.IsAwake,
                IsInteractive: powerStatus.IsInteractive,
                Wakefulness: powerStatus.Wakefulness,
                DisplayPowerState: powerStatus.DisplayPowerState,
                PowerStatusDetail: wakeReadiness.Detail,
                IsInWakeLimbo: wakeReadiness.IsInWakeLimbo,
                Controllers: controllerStatuses,
                SoftwareVersion: softwareVersion,
                SoftwareReleaseOrCodename: softwareReleaseOrCodename,
                SoftwareBuildId: softwareBuildId,
                SoftwareDisplayId: softwareDisplayId,
                ScreenBrightnessPercent: brightnessStatus.Percent,
                MediaVolumeLevel: mediaVolumeStatus.Level,
                MediaVolumeMax: mediaVolumeStatus.MaxLevel,
                IsUsbAdbVisible: isUsbAdbVisible,
                VisibleUsbSerial: visibleUsbSerial);
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

    private async Task<OperationOutcome> ApplyDeviceProfilePropertyAsync(
        string selector,
        string key,
        string expectedValue,
        CancellationToken cancellationToken)
    {
        if (string.Equals(key, ProfileBrightnessPercentKey, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(expectedValue, out var brightnessPercent))
            {
                return Failure(
                    "Device profile brightness value is invalid.",
                    $"Expected an integer percentage for {ProfileBrightnessPercentKey}, but received `{expectedValue}`.");
            }

            brightnessPercent = Math.Clamp(brightnessPercent, 0, 100);
            var mode = await RunShellAsync(selector, "settings put system screen_brightness_mode 0", cancellationToken).ConfigureAwait(false);
            if (mode.ExitCode != 0)
            {
                return Failure("Brightness mode update failed.", mode.CombinedOutput);
            }

            var rawBrightness = ConvertBrightnessPercentToRaw(brightnessPercent);
            var set = await RunShellAsync(selector, $"settings put system screen_brightness {rawBrightness}", cancellationToken).ConfigureAwait(false);
            return set.ExitCode == 0
                ? Success(
                    "Screen brightness updated.",
                    $"Set Quest screen brightness to {brightnessPercent}% ({rawBrightness}/255).")
                : Failure("Brightness update failed.", set.CombinedOutput);
        }

        if (string.Equals(key, ProfileMediaVolumeKey, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(expectedValue, out var mediaVolume))
            {
                return Failure(
                    "Device profile media-volume value is invalid.",
                    $"Expected an integer volume index for {ProfileMediaVolumeKey}, but received `{expectedValue}`.");
            }

            mediaVolume = Math.Max(mediaVolume, 0);
            var set = await RunShellAsync(selector, $"cmd media_session volume --stream 3 --set {mediaVolume}", cancellationToken).ConfigureAwait(false);
            return set.ExitCode == 0
                ? Success(
                    "Media volume command sent.",
                    $"Sent Quest media volume stream 3 to index {mediaVolume}. Verify the readback afterward because some Quest builds ignore remote volume writes.")
                : Failure("Media volume update failed.", set.CombinedOutput);
        }

        if (IsDeviceProfileThresholdKey(key))
        {
            return Success(
                $"{FormatDeviceProfilePropertyLabel(key)} is verification only.",
                "No device write was sent; the current headset/controller state will be checked against the minimum threshold.");
        }

        var setprop = await RunShellAsync(
            selector,
            $"setprop {AdbShellSupport.Quote(key)} {AdbShellSupport.Quote(expectedValue)}",
            cancellationToken).ConfigureAwait(false);
        return setprop.ExitCode == 0
            ? Success(
                $"{FormatDeviceProfilePropertyLabel(key)} updated.",
                AdbShellSupport.Collapse(setprop.CombinedOutput))
            : Failure($"Device profile failed at {key}.", setprop.CombinedOutput);
    }

    private async Task<DevicePropertyStatus> QueryDeviceProfilePropertyStatusAsync(
        string selector,
        string key,
        string expectedValue,
        CancellationToken cancellationToken)
    {
        if (string.Equals(key, ProfileBrightnessPercentKey, StringComparison.OrdinalIgnoreCase))
        {
            var brightness = await QueryScreenBrightnessStatusAsync(selector, cancellationToken).ConfigureAwait(false);
            var expectedPercent = int.TryParse(expectedValue, out var parsedExpectedPercent)
                ? Math.Clamp(parsedExpectedPercent, 0, 100)
                : (int?)null;
            var reported = brightness.Percent.HasValue
                ? brightness.RawValue.HasValue
                    ? $"{brightness.Percent.Value}% ({brightness.RawValue.Value}/255{(brightness.IsManualMode ? ", manual" : ", auto")})"
                    : $"{brightness.Percent.Value}%"
                : string.Empty;
            return new DevicePropertyStatus(
                key,
                expectedValue,
                reported,
                expectedPercent.HasValue &&
                brightness.Percent == expectedPercent &&
                brightness.IsManualMode);
        }

        if (string.Equals(key, ProfileMediaVolumeKey, StringComparison.OrdinalIgnoreCase))
        {
            var mediaVolume = await QueryMediaVolumeStatusAsync(selector, cancellationToken).ConfigureAwait(false);
            var expectedLevel = int.TryParse(expectedValue, out var parsedExpectedLevel) ? Math.Max(parsedExpectedLevel, 0) : (int?)null;
            var reported = mediaVolume.Level.HasValue
                ? mediaVolume.MaxLevel.HasValue
                    ? $"{mediaVolume.Level.Value}/{mediaVolume.MaxLevel.Value}"
                    : mediaVolume.Level.Value.ToString()
                : string.Empty;
            return new DevicePropertyStatus(
                key,
                expectedValue,
                reported,
                expectedLevel.HasValue && mediaVolume.Level == expectedLevel);
        }

        if (string.Equals(key, ProfileHeadsetBatteryMinimumKey, StringComparison.OrdinalIgnoreCase))
        {
            var batteryOutput = await RunShellAsync(selector, "dumpsys battery", cancellationToken).ConfigureAwait(false);
            var batteryLevel = batteryOutput.ExitCode == 0 ? AdbShellSupport.ParseBatteryLevel(batteryOutput.StdOut) : null;
            var expectedMinimum = int.TryParse(expectedValue, out var parsedExpectedMinimum)
                ? Math.Clamp(parsedExpectedMinimum, 0, 100)
                : (int?)null;
            return new DevicePropertyStatus(
                key,
                expectedValue,
                batteryLevel.HasValue ? $"{batteryLevel.Value}%" : string.Empty,
                expectedMinimum.HasValue && batteryLevel.HasValue && batteryLevel.Value >= expectedMinimum.Value);
        }

        if (string.Equals(key, ProfileRightControllerBatteryMinimumKey, StringComparison.OrdinalIgnoreCase))
        {
            var trackingOutput = await RunShellAsync(selector, "dumpsys tracking", cancellationToken).ConfigureAwait(false);
            var controllerStatuses = trackingOutput.ExitCode == 0
                ? ParseControllerStatuses(trackingOutput.StdOut)
                : Array.Empty<QuestControllerStatus>();
            var rightController = controllerStatuses.FirstOrDefault(
                status => string.Equals(status.HandLabel, "Right", StringComparison.OrdinalIgnoreCase));
            var expectedMinimum = int.TryParse(expectedValue, out var parsedExpectedMinimum)
                ? Math.Clamp(parsedExpectedMinimum, 0, 100)
                : (int?)null;
            var reported = rightController is null
                ? string.Empty
                : rightController.BatteryLevel.HasValue
                    ? $"{rightController.BatteryLevel.Value}% ({rightController.ConnectionState})"
                    : rightController.ConnectionState;
            return new DevicePropertyStatus(
                key,
                expectedValue,
                reported,
                expectedMinimum.HasValue &&
                rightController?.BatteryLevel is int batteryLevel &&
                batteryLevel >= expectedMinimum.Value);
        }

        var reportedValue = await TryReadShellValueAsync(
            selector,
            $"getprop {AdbShellSupport.Quote(key)}",
            cancellationToken).ConfigureAwait(false);
        return new DevicePropertyStatus(
            key,
            expectedValue,
            reportedValue,
            string.Equals(expectedValue, reportedValue, StringComparison.Ordinal));
    }

    private async Task<QuestScreenBrightnessStatus> QueryScreenBrightnessStatusAsync(string selector, CancellationToken cancellationToken)
    {
        var brightnessRaw = ParseOptionalInt(
            await TryReadShellValueAsync(selector, "settings get system screen_brightness", cancellationToken).ConfigureAwait(false));
        var brightnessMode = ParseOptionalInt(
            await TryReadShellValueAsync(selector, "settings get system screen_brightness_mode", cancellationToken).ConfigureAwait(false));
        return new QuestScreenBrightnessStatus(
            brightnessRaw.HasValue ? ConvertBrightnessRawToPercent(brightnessRaw.Value) : null,
            brightnessRaw,
            brightnessMode == 0);
    }

    private async Task<QuestMediaVolumeStatus> QueryMediaVolumeStatusAsync(string selector, CancellationToken cancellationToken)
    {
        var output = await RunShellAsync(selector, "cmd media_session volume --stream 3 --get", cancellationToken).ConfigureAwait(false);
        if (output.ExitCode != 0 || !TryParseMediaVolumeStatus(output.StdOut, out var level, out var maxLevel))
        {
            return default;
        }

        return new QuestMediaVolumeStatus(level, maxLevel);
    }

    private static bool TryParseMediaVolumeStatus(string rawOutput, out int level, out int maxLevel)
    {
        level = 0;
        maxLevel = 0;
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return false;
        }

        const string volumeMarker = "volume is ";
        const string rangeMarker = " in range [0..";
        var volumeIndex = rawOutput.IndexOf(volumeMarker, StringComparison.OrdinalIgnoreCase);
        if (volumeIndex < 0)
        {
            return false;
        }

        var levelStart = volumeIndex + volumeMarker.Length;
        var rangeIndex = rawOutput.IndexOf(rangeMarker, levelStart, StringComparison.OrdinalIgnoreCase);
        if (rangeIndex < 0)
        {
            return false;
        }

        var levelText = rawOutput[levelStart..rangeIndex].Trim();
        if (!int.TryParse(levelText, out level))
        {
            return false;
        }

        var maxStart = rangeIndex + rangeMarker.Length;
        var maxEnd = rawOutput.IndexOf(']', maxStart);
        if (maxEnd < 0)
        {
            return false;
        }

        return int.TryParse(rawOutput[maxStart..maxEnd].Trim(), out maxLevel);
    }

    private static int ConvertBrightnessPercentToRaw(int percent)
        => (int)Math.Round(Math.Clamp(percent, 0, 100) * 255d / 100d, MidpointRounding.AwayFromZero);

    private static int ConvertBrightnessRawToPercent(int rawBrightness)
        => (int)Math.Round(Math.Clamp(rawBrightness, 0, 255) * 100d / 255d, MidpointRounding.AwayFromZero);

    private static bool IsDeviceProfileThresholdKey(string key)
        => string.Equals(key, ProfileHeadsetBatteryMinimumKey, StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, ProfileRightControllerBatteryMinimumKey, StringComparison.OrdinalIgnoreCase);

    private static string FormatDeviceProfilePropertyLabel(string key)
        => key.ToLowerInvariant() switch
        {
            ProfileBrightnessPercentKey => "Screen brightness",
            ProfileMediaVolumeKey => "Media volume",
            ProfileHeadsetBatteryMinimumKey => "Minimum headset battery",
            ProfileRightControllerBatteryMinimumKey => "Minimum right-controller battery",
            _ => key
        };

    private static string FormatSoftwareVersion(string? releaseOrCodename, string? incrementalBuild, string? displayId)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(releaseOrCodename))
        {
            parts.Add(releaseOrCodename.Trim());
        }

        var buildLabel = !string.IsNullOrWhiteSpace(incrementalBuild)
            ? incrementalBuild.Trim()
            : string.IsNullOrWhiteSpace(displayId)
                ? string.Empty
                : displayId.Trim();

        if (!string.IsNullOrWhiteSpace(buildLabel))
        {
            parts.Add($"build {buildLabel}");
        }

        return string.Join(" | ", parts);
    }

    public async Task<OperationOutcome> RunUtilityAsync(
        QuestUtilityAction action,
        bool allowWakeResumeTarget = true,
        CancellationToken cancellationToken = default)
    {
        var selector = await EnsureSelectorAsync(cancellationToken).ConfigureAwait(false);
        return action switch
        {
            QuestUtilityAction.Home => await RunUtilityShellAsync("Home command sent.", "input keyevent 3", cancellationToken, wakeFirst: true).ConfigureAwait(false),
            QuestUtilityAction.Back => await RunUtilityShellAsync("Back command sent.", "input keyevent 4", cancellationToken, wakeFirst: true).ConfigureAwait(false),
            QuestUtilityAction.Wake => await WakeHeadsetForVisualsAsync(selector, allowWakeResumeTarget, cancellationToken).ConfigureAwait(false),
            QuestUtilityAction.Sleep => await RunUtilityShellAsync("Sleep command sent.", "input keyevent 223", cancellationToken).ConfigureAwait(false),
            QuestUtilityAction.Reboot => await RunRebootAsync(cancellationToken).ConfigureAwait(false),
            QuestUtilityAction.ListInstalledPackages => await ListInstalledPackagesAsync(selector, cancellationToken).ConfigureAwait(false),
            _ => Failure("Unknown utility action.", action.ToString())
        };
    }

    private async Task<OperationOutcome> RunUtilityShellAsync(
        string summary,
        string command,
        CancellationToken cancellationToken,
        bool wakeFirst = false)
    {
        var selector = await EnsureSelectorAsync(cancellationToken).ConfigureAwait(false);
        OperationOutcome? wakeOutcome = null;
        if (wakeFirst)
        {
            wakeOutcome = await WakeSelectorBeforeActionAsync(selector, summary, cancellationToken).ConfigureAwait(false);
            if (IsWakeFailure(wakeOutcome))
            {
                return wakeOutcome!;
            }
        }

        var output = await RunShellAsync(selector, command, cancellationToken).ConfigureAwait(false);
        var outcome = output.ExitCode == 0
            ? Success(summary, AdbShellSupport.Collapse(output.CombinedOutput))
            : Failure(summary, output.CombinedOutput);
        return MergeWakeWarning(outcome, wakeOutcome);
    }

    private async Task<OperationOutcome?> WakeSelectorBeforeActionAsync(
        string selector,
        string actionLabel,
        CancellationToken cancellationToken)
    {
        var trace = new List<string>();
        var (readiness, powerStatus, _) = await QueryWakeReadinessSnapshotAsync(selector, cancellationToken).ConfigureAwait(false);
        trace.Add($"Initial: {readiness.Detail}");

        if (powerStatus.IsAwake != true)
        {
            var wake = await RunShellAsync(selector, "input keyevent 224", cancellationToken).ConfigureAwait(false);
            if (wake.ExitCode != 0)
            {
                return Failure(
                    $"Wake before {actionLabel} failed.",
                    $"{string.Join(" ", trace)} Wake command failed: {AdbShellSupport.Collapse(wake.CombinedOutput)}".Trim());
            }

            trace.Add("Sent KEYCODE_WAKEUP.");
            readiness = await RefreshWakeReadinessAsync(selector, cancellationToken).ConfigureAwait(false);
            trace.Add($"After wake: {readiness.Detail}");
        }

        // The Quest can report itself as awake while still sitting in SensorLockActivity
        // or another blocked shell state. Normalize that before continuing with the action.
        if (readiness.IsInWakeLimbo)
        {
            readiness = await TryNormalizeWakeHomeShellAsync(selector, readiness, trace, cancellationToken).ConfigureAwait(false);
        }

        if (IsGuardianAutomationRecoveryCandidate(readiness))
        {
            var beforeGuardianRecovery = readiness;
            readiness = await TryRecoverWakeGuardianTrackingLossAsync(selector, readiness, trace, cancellationToken).ConfigureAwait(false);
            var guardianRecoveryMadeProgress =
                !string.Equals(readiness.WakeLimboComponent, beforeGuardianRecovery.WakeLimboComponent, StringComparison.OrdinalIgnoreCase) ||
                readiness.IsInWakeLimbo != beforeGuardianRecovery.IsInWakeLimbo ||
                readiness.IsAwake != beforeGuardianRecovery.IsAwake;

            if (!guardianRecoveryMadeProgress && IsPowerKeyWakeRecoveryCandidate(readiness))
            {
                readiness = await TryRecoverWakePowerOverlayAsync(selector, readiness, trace, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (IsPowerKeyWakeRecoveryCandidate(readiness))
        {
            readiness = await TryRecoverWakePowerOverlayAsync(selector, readiness, trace, cancellationToken).ConfigureAwait(false);
        }

        if (readiness.IsAwake == true && !readiness.IsInWakeLimbo)
        {
            return null;
        }

        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            $"Wake before {actionLabel} left the headset blocked.",
            string.Join(" ", trace));
    }

    private async Task<OperationOutcome> WakeHeadsetForVisualsAsync(
        string selector,
        bool allowWakeResumeTarget,
        CancellationToken cancellationToken)
    {
        var trace = new List<string>();
        var (readiness, powerStatus, foregroundSnapshot) = await QueryWakeReadinessSnapshotAsync(selector, cancellationToken).ConfigureAwait(false);
        var wakeResumeTarget = allowWakeResumeTarget
            ? ResolveWakeResumeTarget(foregroundSnapshot) ?? GetRememberedWakeResumeTarget()
            : null;
        trace.Add($"Initial: {readiness.Detail}");

        if (powerStatus.IsAwake != true)
        {
            var wake = await RunShellAsync(selector, "input keyevent 224", cancellationToken).ConfigureAwait(false);
            if (wake.ExitCode != 0)
            {
                return Failure(
                    "Headset wake sequence failed.",
                    $"{string.Join(" ", trace)} Wake command failed: {AdbShellSupport.Collapse(wake.CombinedOutput)}".Trim());
            }

            trace.Add("Sent KEYCODE_WAKEUP.");
            readiness = await RefreshWakeReadinessAsync(selector, cancellationToken).ConfigureAwait(false);
            trace.Add($"After wake: {readiness.Detail}");
        }

        if (readiness.IsInWakeLimbo)
        {
            readiness = await TryNormalizeWakeHomeShellAsync(selector, readiness, trace, cancellationToken).ConfigureAwait(false);
        }

        if (IsGuardianAutomationRecoveryCandidate(readiness))
        {
            var beforeGuardianRecovery = readiness;
            readiness = await TryRecoverWakeGuardianTrackingLossAsync(selector, readiness, trace, cancellationToken).ConfigureAwait(false);
            var guardianRecoveryMadeProgress =
                !string.Equals(readiness.WakeLimboComponent, beforeGuardianRecovery.WakeLimboComponent, StringComparison.OrdinalIgnoreCase) ||
                readiness.IsInWakeLimbo != beforeGuardianRecovery.IsInWakeLimbo ||
                readiness.IsAwake != beforeGuardianRecovery.IsAwake;

            if (!guardianRecoveryMadeProgress && IsPowerKeyWakeRecoveryCandidate(readiness))
            {
                readiness = await TryRecoverWakePowerOverlayAsync(selector, readiness, trace, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (IsPowerKeyWakeRecoveryCandidate(readiness))
        {
            readiness = await TryRecoverWakePowerOverlayAsync(selector, readiness, trace, cancellationToken).ConfigureAwait(false);
        }

        if (readiness.IsInWakeLimbo && wakeResumeTarget is not null)
        {
            readiness = await TryRecoverWakeVisualsAsync(
                    selector,
                    readiness,
                    $"Relaunched {wakeResumeTarget.PackageId}.",
                    string.IsNullOrWhiteSpace(wakeResumeTarget.Component)
                        ? AdbShellSupport.BuildMonkeyLaunchCommand(wakeResumeTarget.PackageId)
                        : AdbShellSupport.BuildExplicitLaunchCommand(wakeResumeTarget.Component),
                    trace,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (readiness.IsInWakeLimbo && !allowWakeResumeTarget)
        {
            trace.Add("Skipped wake-resume relaunch because this wake path is restricted to Home-shell recovery only.");
        }

        return readiness.IsAwake == true && !readiness.IsInWakeLimbo
            ? Success(
                "Headset awake.",
                string.Join(" ", trace))
            : new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Headset woke into a blocked state.",
                string.Join(" ", trace));
    }

    private async Task<OperationOutcome> RunRebootAsync(CancellationToken cancellationToken)
    {
        var selector = await EnsureSelectorAsync(cancellationToken).ConfigureAwait(false);
        var reboot = await RunAdbAsync(["-s", selector, "reboot"], cancellationToken).ConfigureAwait(false);
        return reboot.ExitCode == 0
            ? Success("Reboot command sent.", AdbShellSupport.Collapse(reboot.CombinedOutput))
            : Failure("Reboot command failed.", reboot.CombinedOutput);
    }

    private async Task<QuestWakeReadiness> TryRecoverWakeVisualsAsync(
        string selector,
        QuestWakeReadiness readiness,
        string traceLabel,
        string command,
        ICollection<string> trace,
        CancellationToken cancellationToken)
    {
        if (!readiness.IsInWakeLimbo)
        {
            return readiness;
        }

        var result = await RunShellAsync(selector, command, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.CombinedOutput.Contains("Error:", StringComparison.OrdinalIgnoreCase))
        {
            trace.Add($"{traceLabel} Failed: {AdbShellSupport.Collapse(result.CombinedOutput)}");
            return readiness;
        }

        trace.Add(traceLabel);
        readiness = await RefreshWakeReadinessAsync(selector, cancellationToken).ConfigureAwait(false);
        trace.Add($"After recovery: {readiness.Detail}");
        return readiness;
    }

    private async Task<QuestWakeReadiness> TryNormalizeWakeHomeShellAsync(
        string selector,
        QuestWakeReadiness readiness,
        ICollection<string> trace,
        CancellationToken cancellationToken)
    {
        if (!readiness.IsInWakeLimbo)
        {
            return readiness;
        }

        var home = await RunShellAsync(selector, "input keyevent 3", cancellationToken).ConfigureAwait(false);
        if (home.ExitCode != 0)
        {
            trace.Add($"Sent KEYCODE_HOME to normalize the shell. Failed: {AdbShellSupport.Collapse(home.CombinedOutput)}");
        }
        else
        {
            trace.Add("Sent KEYCODE_HOME to normalize the shell.");
            readiness = await RefreshWakeReadinessAsync(selector, cancellationToken).ConfigureAwait(false);
            trace.Add($"After home: {readiness.Detail}");
        }

        if (!readiness.IsInWakeLimbo)
        {
            return readiness;
        }

        var explicitHome = await RunShellAsync(
            selector,
            AdbShellSupport.BuildExplicitLaunchCommand($"{QuestHomePackage}/.{QuestHomeActivity}"),
            cancellationToken).ConfigureAwait(false);
        if (explicitHome.ExitCode != 0 || explicitHome.CombinedOutput.Contains("Error:", StringComparison.OrdinalIgnoreCase))
        {
            trace.Add($"Started {QuestHomePackage}/.{QuestHomeActivity}. Failed: {AdbShellSupport.Collapse(explicitHome.CombinedOutput)}");
            return readiness;
        }

        trace.Add($"Started {QuestHomePackage}/.{QuestHomeActivity}.");
        readiness = await RefreshWakeReadinessAsync(selector, cancellationToken).ConfigureAwait(false);
        trace.Add($"After explicit home: {readiness.Detail}");
        return readiness;
    }

    private async Task<QuestWakeReadiness> TryRecoverWakePowerOverlayAsync(
        string selector,
        QuestWakeReadiness readiness,
        ICollection<string> trace,
        CancellationToken cancellationToken)
    {
        if (!IsPowerKeyWakeRecoveryCandidate(readiness))
        {
            return readiness;
        }

        var power = await RunShellAsync(selector, "input keyevent 26", cancellationToken).ConfigureAwait(false);
        if (power.ExitCode != 0)
        {
            trace.Add($"Sent KEYCODE_POWER to recover blocked tracking shell. Failed: {AdbShellSupport.Collapse(power.CombinedOutput)}");
            return readiness;
        }

        trace.Add("Sent KEYCODE_POWER to recover blocked tracking shell.");
        for (var poll = 1; poll <= WakePowerRecoveryPollCount; poll++)
        {
            await Task.Delay(WakePowerRecoveryPollInterval, cancellationToken).ConfigureAwait(false);
            readiness = await RefreshWakeReadinessAsync(selector, cancellationToken).ConfigureAwait(false);
            trace.Add($"After power poll {poll}: {readiness.Detail}");

            if (readiness.IsAwake == true && !readiness.IsInWakeLimbo)
            {
                break;
            }
        }

        // HorizonOS can move into Quick Settings as an intermediate stable state
        // before the original launch target is resumed.
        return readiness;
    }

    private async Task<QuestWakeReadiness> TryRecoverWakeGuardianTrackingLossAsync(
        string selector,
        QuestWakeReadiness readiness,
        ICollection<string> trace,
        CancellationToken cancellationToken)
    {
        if (!IsGuardianAutomationRecoveryCandidate(readiness))
        {
            return readiness;
        }

        var disableAutomation = await RunShellAsync(
            selector,
            $"am broadcast -a {QuestVrPowerManagerAutomationDisableAction}",
            cancellationToken).ConfigureAwait(false);
        if (disableAutomation.ExitCode != 0 || disableAutomation.CombinedOutput.Contains("Exception", StringComparison.OrdinalIgnoreCase))
        {
            trace.Add($"Broadcast {QuestVrPowerManagerAutomationDisableAction} to dismiss Guardian. Failed: {AdbShellSupport.Collapse(disableAutomation.CombinedOutput)}");
            return readiness;
        }

        trace.Add($"Broadcast {QuestVrPowerManagerAutomationDisableAction} to dismiss Guardian.");
        await Task.Delay(WakeGuardianRecoveryStepDelay, cancellationToken).ConfigureAwait(false);
        readiness = await RefreshWakeReadinessAsync(selector, cancellationToken).ConfigureAwait(false);
        trace.Add($"After automation_disable: {readiness.Detail}");

        var proxClose = await RunShellAsync(
            selector,
            $"am broadcast -a {QuestVrPowerManagerProxCloseAction}",
            cancellationToken).ConfigureAwait(false);
        if (proxClose.ExitCode != 0 || proxClose.CombinedOutput.Contains("Exception", StringComparison.OrdinalIgnoreCase))
        {
            trace.Add($"Broadcast {QuestVrPowerManagerProxCloseAction} to re-enter mounted wake. Failed: {AdbShellSupport.Collapse(proxClose.CombinedOutput)}");
            return readiness;
        }

        trace.Add($"Broadcast {QuestVrPowerManagerProxCloseAction} to re-enter mounted wake.");
        await Task.Delay(WakeGuardianRecoveryStepDelay, cancellationToken).ConfigureAwait(false);
        readiness = await RefreshWakeReadinessAsync(selector, cancellationToken).ConfigureAwait(false);
        trace.Add($"After prox_close: {readiness.Detail}");
        return readiness;
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

    private async Task<string> EnsureSelectorAsync(CancellationToken cancellationToken)
    {
        var selector = await ResolveResponsiveSelectorAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(selector))
        {
            return selector;
        }

        return await EnsureUsbSelectorAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task<string?> ResolveResponsiveSelectorAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<string>(3);
        lock (_sync)
        {
            AddSelectorCandidate(candidates, _lastTcpSelector);
            AddSelectorCandidate(candidates, _activeSelector);
            AddSelectorCandidate(candidates, _lastUsbSelector);
        }

        foreach (var candidate in candidates)
        {
            var responsive = await TryActivateSelectorAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(responsive))
            {
                return responsive;
            }
        }

        var devices = await ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        var activeWifiSelector = devices
            .Where(device => device.IsTcp && string.Equals(device.State, "device", StringComparison.OrdinalIgnoreCase))
            .Select(device => device.Serial)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(activeWifiSelector))
        {
            RememberSelector(activeWifiSelector);
            return activeWifiSelector;
        }

        var activeUsbSelector = devices
            .Where(device => !device.IsTcp && string.Equals(device.State, "device", StringComparison.OrdinalIgnoreCase))
            .Select(device => device.Serial)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(activeUsbSelector))
        {
            RememberSelector(activeUsbSelector);
            return activeUsbSelector;
        }

        return null;
    }

    private async Task<string?> TryActivateSelectorAsync(string? selector, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var normalizedSelector = LooksLikeTcpSelector(selector)
            ? NormalizeEndpoint(selector)
            : selector.Trim();

        if (await IsSelectorResponsiveAsync(normalizedSelector, cancellationToken).ConfigureAwait(false))
        {
            RememberSelector(normalizedSelector);
            return normalizedSelector;
        }

        if (!LooksLikeTcpSelector(normalizedSelector))
        {
            return null;
        }

        var reconnect = await RunAdbAsync(["connect", normalizedSelector], cancellationToken).ConfigureAwait(false);
        if (reconnect.ExitCode != 0 &&
            !reconnect.CombinedOutput.Contains("already connected", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (await IsSelectorResponsiveAsync(normalizedSelector, cancellationToken).ConfigureAwait(false))
        {
            RememberSelector(normalizedSelector);
            return normalizedSelector;
        }

        return null;
    }

    private async Task<bool> IsSelectorResponsiveAsync(string selector, CancellationToken cancellationToken)
    {
        var state = await RunAdbAsync(["-s", selector, "get-state"], cancellationToken).ConfigureAwait(false);
        return state.ExitCode == 0 &&
               state.StdOut.Contains("device", StringComparison.OrdinalIgnoreCase);
    }

    private void RememberSelector(string selector)
    {
        lock (_sync)
        {
            _activeSelector = selector;
            if (LooksLikeTcpSelector(selector))
            {
                _lastTcpSelector = selector;
                return;
            }

            _lastUsbSelector = selector;
        }
    }

    private static void AddSelectorCandidate(ICollection<string> candidates, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return;
        }

        if (candidates.Contains(selector, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        candidates.Add(selector);
    }

    private async Task<string> TryReadShellValueAsync(string selector, string command, CancellationToken cancellationToken)
    {
        var result = await RunShellAsync(selector, command, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.StdOut.Trim() : string.Empty;
    }

    private async Task<(AdbCommandResult Output, AdbShellSupport.ForegroundAppSnapshot? Snapshot)> QueryForegroundSnapshotAsync(
        string selector,
        CancellationToken cancellationToken)
    {
        var activityOutput = await RunShellAsync(selector, "dumpsys activity activities", cancellationToken).ConfigureAwait(false);
        var snapshot = activityOutput.ExitCode == 0
            ? AdbShellSupport.ParseForegroundSnapshot(activityOutput.StdOut)
            : null;

        return (activityOutput, snapshot);
    }

    private async Task<int?> ResolveRecentTaskIdAsync(
        string selector,
        string packageId,
        CancellationToken cancellationToken)
    {
        for (var poll = 0; poll < KioskVerificationPollCount; poll++)
        {
            var recentsOutput = await RunShellAsync(selector, "dumpsys activity recents", cancellationToken).ConfigureAwait(false);
            if (recentsOutput.ExitCode == 0)
            {
                var taskId = AdbShellSupport.ParseRecentTaskId(recentsOutput.StdOut, packageId);
                if (taskId.HasValue)
                {
                    return taskId;
                }
            }

            await Task.Delay(KioskVerificationPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<ForegroundVerification> VerifyPinnedForegroundAsync(
        string selector,
        string packageId,
        CancellationToken cancellationToken)
    {
        for (var poll = 0; poll < KioskVerificationPollCount; poll++)
        {
            var activitiesOutput = await RunShellAsync(selector, "dumpsys activity activities", cancellationToken).ConfigureAwait(false);
            if (activitiesOutput.ExitCode == 0)
            {
                var hasPinnedState = ContainsActivityMarker(activitiesOutput.StdOut, "mLockTaskModeState=", "PINNED");
                var hasResumedTarget = ContainsActivityMarker(activitiesOutput.StdOut, "ResumedActivity:", packageId);
                var hasFocusedTarget = ContainsActivityMarker(activitiesOutput.StdOut, "mCurrentFocus=", packageId);
                var hasOpaqueTarget = ContainsActivityMarker(activitiesOutput.StdOut, "mTopFullscreenOpaqueWindowState=", packageId);

                if (hasPinnedState && hasResumedTarget && hasFocusedTarget && hasOpaqueTarget)
                {
                    return new ForegroundVerification(
                        true,
                        $"Quest reported {packageId} as resumed, focused, top-opaque, and PINNED after task-lock poll {poll + 1}.");
                }
            }

            await Task.Delay(KioskVerificationPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return new ForegroundVerification(
            false,
            $"Quest did not report {packageId} as resumed, focused, top-opaque, and PINNED within {KioskVerificationPollCount} task-lock polls.");
    }

    private async Task<ForegroundVerification> VerifyHomeForegroundAsync(
        string selector,
        string targetPackageId,
        CancellationToken cancellationToken)
    {
        for (var poll = 0; poll < KioskVerificationPollCount; poll++)
        {
            var activitiesOutput = await RunShellAsync(selector, "dumpsys activity activities", cancellationToken).ConfigureAwait(false);
            if (activitiesOutput.ExitCode == 0)
            {
                var snapshot = AdbShellSupport.ParseForegroundSnapshot(activitiesOutput.StdOut);
                var lockTaskPinned = ContainsActivityMarker(activitiesOutput.StdOut, "mLockTaskModeState=", "PINNED");
                var hasHomeFocus = ContainsActivityMarker(activitiesOutput.StdOut, "mCurrentFocus=", QuestHomePackage);
                var hasHomeVisible = ContainsActivityMarker(activitiesOutput.StdOut, "HomeActivity", QuestHomePackage);
                var hasHomeTopOpaque =
                    ContainsActivityMarker(activitiesOutput.StdOut, "mTopFullscreenOpaqueWindowState=", QuestHomePackage) ||
                    ContainsActivityMarker(activitiesOutput.StdOut, "mTopFullscreenOpaqueWindowState=", QuestSystemUxPackage) ||
                    ContainsActivityMarker(activitiesOutput.StdOut, "ResumedActivity:", $"{QuestSystemUxPackage}/com.oculus.panelapp.virtualobjects.{QuestVirtualObjectsActivity}");
                var targetStillForeground = string.Equals(snapshot?.PackageId, targetPackageId, StringComparison.OrdinalIgnoreCase);

                if (!lockTaskPinned && hasHomeFocus && hasHomeVisible && hasHomeTopOpaque && !targetStillForeground)
                {
                    return new ForegroundVerification(
                        true,
                        $"Quest reported Home foreground ownership and no pinned {targetPackageId} task after kiosk-exit poll {poll + 1}.");
                }
            }

            await Task.Delay(KioskVerificationPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return new ForegroundVerification(
            false,
            $"Quest did not report a clean Home-side foreground after exiting kiosk mode within {KioskVerificationPollCount} polls.");
    }

    private static bool ContainsActivityMarker(string output, string marker, string expectedFragment)
        => output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line =>
                line.Contains(marker, StringComparison.OrdinalIgnoreCase) &&
                line.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));

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

    internal static QuestPowerStatus ParseQuestPowerStatus(string output)
    {
        var wakefulness = string.Empty;
        bool? isInteractive = null;
        var displayPowerState = string.Empty;

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith("mWakefulness=", StringComparison.OrdinalIgnoreCase))
            {
                wakefulness = rawLine["mWakefulness=".Length..].Trim();
                continue;
            }

            if (rawLine.StartsWith("mInteractive=", StringComparison.OrdinalIgnoreCase))
            {
                var interactiveRaw = rawLine["mInteractive=".Length..].Trim();
                if (bool.TryParse(interactiveRaw, out var parsedInteractive))
                {
                    isInteractive = parsedInteractive;
                }

                continue;
            }

            if (rawLine.StartsWith("Display Power:", StringComparison.OrdinalIgnoreCase))
            {
                var stateIndex = rawLine.IndexOf("state=", StringComparison.OrdinalIgnoreCase);
                if (stateIndex >= 0)
                {
                    displayPowerState = rawLine[(stateIndex + "state=".Length)..].Trim();
                }
            }
        }

        bool? isAwake = null;
        if (isInteractive.HasValue)
        {
            isAwake = isInteractive.Value;
        }
        else if (string.Equals(wakefulness, "Awake", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(wakefulness, "Dreaming", StringComparison.OrdinalIgnoreCase))
        {
            isAwake = true;
        }
        else if (string.Equals(wakefulness, "Asleep", StringComparison.OrdinalIgnoreCase))
        {
            isAwake = false;
        }
        else if (string.Equals(displayPowerState, "ON", StringComparison.OrdinalIgnoreCase))
        {
            isAwake = true;
        }
        else if (string.Equals(displayPowerState, "OFF", StringComparison.OrdinalIgnoreCase))
        {
            isAwake = false;
        }

        var detailParts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(wakefulness))
        {
            detailParts.Add($"wakefulness {wakefulness}");
        }

        if (isInteractive.HasValue)
        {
            detailParts.Add($"interactive {isInteractive.Value.ToString().ToLowerInvariant()}");
        }

        if (!string.IsNullOrWhiteSpace(displayPowerState))
        {
            detailParts.Add($"display {displayPowerState}");
        }

        var detail = detailParts.Count == 0
            ? "Quest power-state readback unavailable."
            : string.Join("; ", detailParts);

        return new QuestPowerStatus(wakefulness, isInteractive, displayPowerState, isAwake, detail);
    }

    internal static QuestWakeReadiness EvaluateWakeReadiness(
        QuestPowerStatus powerStatus,
        AdbShellSupport.ForegroundAppSnapshot? foregroundSnapshot)
    {
        var wakeLimboComponent = ResolveWakeLimboComponent(foregroundSnapshot);
        var detailParts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(powerStatus.Detail))
        {
            detailParts.Add(powerStatus.Detail);
        }

        if (!string.IsNullOrWhiteSpace(wakeLimboComponent))
        {
            detailParts.Add($"foreground {wakeLimboComponent}");
            detailParts.Add("Meta visual blocker active");
        }

        var detail = detailParts.Count == 0
            ? "Quest wake-state readback unavailable."
            : string.Join("; ", detailParts);

        return new QuestWakeReadiness(
            IsAwake: string.IsNullOrWhiteSpace(wakeLimboComponent) ? powerStatus.IsAwake : false,
            IsInWakeLimbo: !string.IsNullOrWhiteSpace(wakeLimboComponent),
            WakeLimboComponent: wakeLimboComponent,
            Detail: detail);
    }

    internal static QuestWifiStatus ParseQuestWifiStatus(string output)
    {
        var ssid = string.Empty;
        var ipAddress = string.Empty;

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith("Wifi is connected to ", StringComparison.OrdinalIgnoreCase))
            {
                ssid = ExtractQuotedValue(rawLine);
                continue;
            }

            if (!rawLine.StartsWith("WifiInfo:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var infoSegments = rawLine.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in infoSegments)
            {
                if (segment.StartsWith("WifiInfo: SSID:", StringComparison.OrdinalIgnoreCase))
                {
                    ssid = ExtractQuotedValue(segment);
                }
                else if (segment.StartsWith("IP:", StringComparison.OrdinalIgnoreCase))
                {
                    ipAddress = segment["IP:".Length..].Trim().TrimStart('/');
                }
            }
        }

        return new QuestWifiStatus(ssid, ipAddress);
    }

    internal static HostWifiStatus ParseHostWifiStatus(string output)
    {
        string name = string.Empty;
        string state = string.Empty;
        string ssid = string.Empty;

        static HostWifiStatus BuildStatus(string currentName, string currentState, string currentSsid)
            => string.Equals(currentState, "connected", StringComparison.OrdinalIgnoreCase)
                ? new HostWifiStatus(currentName, currentSsid)
                : new HostWifiStatus(string.Empty, string.Empty);

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase))
            {
                var existingStatus = BuildStatus(name, state, ssid);
                if (!string.IsNullOrWhiteSpace(existingStatus.Ssid))
                {
                    return existingStatus;
                }

                name = value;
                state = string.Empty;
                ssid = string.Empty;
                continue;
            }

            if (string.Equals(key, "State", StringComparison.OrdinalIgnoreCase))
            {
                state = value;
                continue;
            }

            if (string.Equals(key, "SSID", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "BSSID", StringComparison.OrdinalIgnoreCase))
            {
                ssid = value;
            }
        }

        return BuildStatus(name, state, ssid);
    }

    internal static IReadOnlyList<QuestControllerStatus> ParseControllerStatuses(string output)
    {
        var statuses = new Dictionary<string, QuestControllerStatus>(StringComparer.OrdinalIgnoreCase);
        string currentHand = string.Empty;

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (string.Equals(line, "Left", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Left --", StringComparison.OrdinalIgnoreCase))
            {
                currentHand = "Left";
                continue;
            }

            if (string.Equals(line, "Right", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Right --", StringComparison.OrdinalIgnoreCase))
            {
                currentHand = "Right";
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentHand))
            {
                continue;
            }

            var entryIndex = line.IndexOf("[id:", StringComparison.OrdinalIgnoreCase);
            if (entryIndex < 0)
            {
                continue;
            }

            var entry = line[entryIndex..];
            var deviceId = ExtractLabeledSegment(entry, "id:");
            var connectionState = ExtractLabeledSegment(entry, "conn:");
            var batteryText = ExtractLabeledSegment(entry, "battery:");
            if (string.IsNullOrWhiteSpace(deviceId) &&
                string.IsNullOrWhiteSpace(connectionState) &&
                string.IsNullOrWhiteSpace(batteryText))
            {
                continue;
            }

            statuses[currentHand] = new QuestControllerStatus(
                currentHand,
                int.TryParse(batteryText, out var batteryLevel) ? batteryLevel : null,
                connectionState,
                deviceId);
        }

        return statuses.Values
            .OrderBy(status => string.Equals(status.HandLabel, "Left", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(status => status.HandLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ParseDumpsysValue(string dumpsysOutput, string key)
    {
        if (string.IsNullOrWhiteSpace(dumpsysOutput) || string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        foreach (var rawLine in dumpsysOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = $"{key}=";
            var index = rawLine.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            return rawLine[(index + token.Length)..].Trim();
        }

        return string.Empty;
    }

    private static string ExtractLabeledSegment(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var index = value.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var start = index + label.Length;
        var end = value.IndexOfAny([',', ']'], start);
        var segment = end >= 0
            ? value[start..end]
            : value[start..];
        return segment.Trim();
    }

    private static string ParseVersionCode(string dumpsysOutput)
    {
        var raw = ParseDumpsysValue(dumpsysOutput, "versionCode");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var spaceIndex = raw.IndexOf(' ');
        return spaceIndex > 0 ? raw[..spaceIndex].Trim() : raw.Trim();
    }

    private static string SanitizeFileToken(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }

    private async Task<QuestWakeReadiness> QueryWakeReadinessAsync(
        string selector,
        CancellationToken cancellationToken)
        => (await QueryWakeReadinessSnapshotAsync(selector, cancellationToken).ConfigureAwait(false)).Readiness;

    private async Task<(QuestWakeReadiness Readiness, QuestPowerStatus PowerStatus, AdbShellSupport.ForegroundAppSnapshot? ForegroundSnapshot)> QueryWakeReadinessSnapshotAsync(
        string selector,
        CancellationToken cancellationToken)
    {
        var powerOutput = await RunShellAsync(selector, "dumpsys power", cancellationToken).ConfigureAwait(false);
        var powerStatus = powerOutput.ExitCode == 0
            ? ParseQuestPowerStatus(powerOutput.StdOut)
            : new QuestPowerStatus(string.Empty, null, string.Empty, null, "Quest power-state readback unavailable.");
        var (_, foregroundSnapshot) = await QueryForegroundSnapshotAsync(selector, cancellationToken).ConfigureAwait(false);
        return (EvaluateWakeReadiness(powerStatus, foregroundSnapshot), powerStatus, foregroundSnapshot);
    }

    private async Task<QuestWakeReadiness> RefreshWakeReadinessAsync(
        string selector,
        CancellationToken cancellationToken)
    {
        await Task.Delay(450, cancellationToken).ConfigureAwait(false);
        return await QueryWakeReadinessAsync(selector, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveWakeLimboComponent(AdbShellSupport.ForegroundAppSnapshot? foregroundSnapshot)
    {
        if (foregroundSnapshot is null)
        {
            return string.Empty;
        }

        if (IsPrimaryWakeBlockingComponent(foregroundSnapshot.PrimaryComponent))
        {
            return foregroundSnapshot.PrimaryComponent;
        }

        if (IsQuestHomeShellComponent(foregroundSnapshot.PrimaryComponent))
        {
            foreach (var component in foregroundSnapshot.VisibleComponents)
            {
                if (IsHomeShellWakeBlockingComponent(component))
                {
                    return component;
                }
            }

            return string.Empty;
        }

        var launchableAppOwnsForeground =
            IsLaunchableWakeResumePackage(foregroundSnapshot.PackageId) &&
            !IsQuestHomeShellComponent(foregroundSnapshot.PrimaryComponent);

        foreach (var component in foregroundSnapshot.VisibleComponents)
        {
            if (launchableAppOwnsForeground)
            {
                if (IsVisibleWakeBlockingOverlayComponent(component))
                {
                    return component;
                }

                continue;
            }

            if (IsVisibleWakeBlockingComponent(component))
            {
                return component;
            }
        }

        return string.Empty;
    }

    private QuestWakeResumeTarget? GetRememberedWakeResumeTarget()
    {
        lock (_sync)
        {
            return _lastWakeResumeTarget;
        }
    }

    private void RememberWakeResumeTarget(QuestWakeResumeTarget? target)
    {
        if (target is null)
        {
            return;
        }

        lock (_sync)
        {
            _lastWakeResumeTarget = target;
        }
    }

    private static QuestWakeResumeTarget? ResolveWakeResumeTarget(AdbShellSupport.ForegroundAppSnapshot? foregroundSnapshot)
    {
        if (foregroundSnapshot is null || !IsLaunchableWakeResumePackage(foregroundSnapshot.PackageId))
        {
            return null;
        }

        var component = string.IsNullOrWhiteSpace(foregroundSnapshot.PrimaryComponent)
            ? null
            : foregroundSnapshot.PrimaryComponent;
        return new QuestWakeResumeTarget(foregroundSnapshot.PackageId, component);
    }

    private static bool IsPrimaryWakeBlockingComponent(string? component)
        => !string.IsNullOrWhiteSpace(component) &&
           (component.Contains(QuestClearActivityPackage, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestClearActivity, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestGuardianPackage, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestGuardianDialogActivity, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestFocusPlaceholderActivity, StringComparison.OrdinalIgnoreCase));

    private static bool IsVisibleWakeBlockingComponent(string? component)
        => !string.IsNullOrWhiteSpace(component) &&
           (component.Contains(QuestClearActivityPackage, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestClearActivity, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestGuardianPackage, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestGuardianDialogActivity, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestFocusPlaceholderActivity, StringComparison.OrdinalIgnoreCase));

    private static bool IsVisibleWakeBlockingOverlayComponent(string? component)
        => !string.IsNullOrWhiteSpace(component) &&
           (component.Contains(QuestClearActivityPackage, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestClearActivity, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestGuardianPackage, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestGuardianDialogActivity, StringComparison.OrdinalIgnoreCase));

    private static bool IsPowerKeyWakeRecoveryCandidate(QuestWakeReadiness readiness)
        => !string.IsNullOrWhiteSpace(readiness.WakeLimboComponent) &&
           (readiness.WakeLimboComponent.Contains(QuestGuardianPackage, StringComparison.OrdinalIgnoreCase) ||
            readiness.WakeLimboComponent.Contains(QuestGuardianDialogActivity, StringComparison.OrdinalIgnoreCase) ||
            readiness.WakeLimboComponent.Contains(QuestClearActivityPackage, StringComparison.OrdinalIgnoreCase) ||
            readiness.WakeLimboComponent.Contains(QuestClearActivity, StringComparison.OrdinalIgnoreCase) ||
            readiness.WakeLimboComponent.Contains(QuestFocusPlaceholderActivity, StringComparison.OrdinalIgnoreCase));

    private static bool IsGuardianAutomationRecoveryCandidate(QuestWakeReadiness readiness)
        => !string.IsNullOrWhiteSpace(readiness.WakeLimboComponent) &&
           (readiness.WakeLimboComponent.Contains(QuestGuardianPackage, StringComparison.OrdinalIgnoreCase) ||
            readiness.WakeLimboComponent.Contains(QuestGuardianDialogActivity, StringComparison.OrdinalIgnoreCase) ||
            readiness.WakeLimboComponent.Contains(QuestClearActivityPackage, StringComparison.OrdinalIgnoreCase) ||
            readiness.WakeLimboComponent.Contains(QuestClearActivity, StringComparison.OrdinalIgnoreCase) ||
            readiness.WakeLimboComponent.Contains(QuestFocusPlaceholderActivity, StringComparison.OrdinalIgnoreCase));

    private static bool IsQuestHomeShellComponent(string? component)
        => !string.IsNullOrWhiteSpace(component) &&
           ((component.Contains(QuestHomePackage, StringComparison.OrdinalIgnoreCase) &&
             (component.Contains(QuestHomeActivity, StringComparison.OrdinalIgnoreCase) ||
              component.Contains(QuestFocusPlaceholderActivity, StringComparison.OrdinalIgnoreCase))) ||
            (component.Contains(QuestQuickSettingsPackage, StringComparison.OrdinalIgnoreCase) &&
             component.Contains(QuestQuickSettingsActivity, StringComparison.OrdinalIgnoreCase)));

    private static bool IsHomeShellWakeBlockingComponent(string? component)
        => !string.IsNullOrWhiteSpace(component) &&
           (component.Contains(QuestClearActivityPackage, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestClearActivity, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestGuardianPackage, StringComparison.OrdinalIgnoreCase) ||
            component.Contains(QuestGuardianDialogActivity, StringComparison.OrdinalIgnoreCase));

    private static bool IsLaunchableWakeResumePackage(string? packageId)
        => !string.IsNullOrWhiteSpace(packageId) &&
           !string.Equals(packageId, QuestHomePackage, StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(packageId, QuestSensorLockPackage, StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(packageId, QuestClearActivityPackage, StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(packageId, QuestGuardianPackage, StringComparison.OrdinalIgnoreCase) &&
           !packageId.Contains("com.oculus.systemux", StringComparison.OrdinalIgnoreCase);

    private static bool LaunchSucceeded(AdbCommandResult launch)
        => launch.ExitCode == 0 &&
           !launch.CombinedOutput.Contains("Error:", StringComparison.OrdinalIgnoreCase) &&
           !launch.CombinedOutput.Contains("monkey aborted", StringComparison.OrdinalIgnoreCase);

    internal static bool IsWakeFailure(OperationOutcome? wakeOutcome)
        => wakeOutcome?.Kind == OperationOutcomeKind.Failure;

    internal static OperationOutcome MergeWakeWarning(OperationOutcome outcome, OperationOutcome? wakeOutcome)
    {
        if (wakeOutcome is null || wakeOutcome.Kind != OperationOutcomeKind.Warning)
        {
            return outcome;
        }

        var detailParts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(wakeOutcome.Summary))
        {
            detailParts.Add(wakeOutcome.Summary);
        }

        if (!string.IsNullOrWhiteSpace(wakeOutcome.Detail))
        {
            detailParts.Add(wakeOutcome.Detail);
        }

        if (!string.IsNullOrWhiteSpace(outcome.Detail))
        {
            detailParts.Add(outcome.Detail);
        }

        return outcome with
        {
            Kind = outcome.Kind == OperationOutcomeKind.Success ? OperationOutcomeKind.Warning : outcome.Kind,
            Detail = string.Join(" ", detailParts)
        };
    }

    private static string FormatControllerDetail(QuestControllerStatus status)
    {
        var batteryLabel = status.BatteryLevel is null ? "n/a" : $"{status.BatteryLevel}%";
        var connectionLabel = string.IsNullOrWhiteSpace(status.ConnectionState)
            ? "unknown"
            : status.ConnectionState;
        return $"{status.HandLabel.ToLowerInvariant()} {batteryLabel} {connectionLabel}";
    }

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

    internal sealed record QuestWifiStatus(string Ssid, string IpAddress);

    internal sealed record HostWifiStatus(string InterfaceName, string Ssid);

    internal sealed record QuestPowerStatus(
        string Wakefulness,
        bool? IsInteractive,
        string DisplayPowerState,
        bool? IsAwake,
        string Detail);

    internal sealed record QuestWakeReadiness(
        bool? IsAwake,
        bool IsInWakeLimbo,
        string WakeLimboComponent,
        string Detail);

    private sealed record QuestWakeResumeTarget(string PackageId, string? Component);

    private sealed record ForegroundVerification(bool Succeeded, string Detail);

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

    private async Task<QuestWifiStatus> QueryQuestWifiStatusAsync(string selector, CancellationToken cancellationToken)
    {
        var result = await RunShellAsync(selector, "cmd wifi status", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? ParseQuestWifiStatus(result.StdOut)
            : new QuestWifiStatus(string.Empty, string.Empty);
    }

    private static HostWifiStatus QueryHostWifiStatus()
    {
        var netshPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "netsh.exe");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = File.Exists(netshPath) ? netshPath : "netsh",
                Arguments = "wlan show interfaces",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return new HostWifiStatus(string.Empty, string.Empty);
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode != 0)
            {
                return new HostWifiStatus(string.Empty, string.Empty);
            }

            var status = ParseHostWifiStatus(output);
            return !string.IsNullOrWhiteSpace(status.Ssid)
                ? status
                : new HostWifiStatus(string.Empty, string.Empty);
        }
        catch
        {
            return new HostWifiStatus(string.Empty, string.Empty);
        }
    }

    private HostWifiStatus ResolveHostWifiStatus(bool includeHostWifiStatus)
    {
        if (!includeHostWifiStatus)
        {
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(_lastKnownHostWifiStatus.Ssid))
                {
                    return _lastKnownHostWifiStatus;
                }
            }
        }

        var status = QueryHostWifiStatus();
        lock (_sync)
        {
            _lastKnownHostWifiStatus = status;
            return _lastKnownHostWifiStatus;
        }
    }

    private static string ExtractQuotedValue(string value)
    {
        var firstQuote = value.IndexOf('"');
        if (firstQuote < 0)
        {
            return string.Empty;
        }

        var secondQuote = value.IndexOf('"', firstQuote + 1);
        if (secondQuote <= firstQuote)
        {
            return string.Empty;
        }

        return value.Substring(firstQuote + 1, secondQuote - firstQuote - 1).Trim();
    }

    private static string ExtractIpAddressFromSelector(string selector)
    {
        if (!LooksLikeTcpSelector(selector))
        {
            return string.Empty;
        }

        var separatorIndex = selector.LastIndexOf(':');
        return separatorIndex > 0 ? selector[..separatorIndex] : string.Empty;
    }
}

internal readonly record struct QuestScreenBrightnessStatus(
    int? Percent,
    int? RawValue,
    bool IsManualMode);

internal readonly record struct QuestMediaVolumeStatus(
    int? Level,
    int? MaxLevel);
