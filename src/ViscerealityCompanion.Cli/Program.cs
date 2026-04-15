using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Cli;

public static class Program
{
    private static readonly Option<string?> DeviceOption = new(
        ["--device", "-d"],
        "ADB device selector (serial or IP:port). Overrides persisted session.");

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("ViscerealityCompanion — Quest operator station CLI");
        rootCommand.AddGlobalOption(DeviceOption);

        rootCommand.AddCommand(BuildProbeCommand());
        rootCommand.AddCommand(BuildWifiCommand());
        rootCommand.AddCommand(BuildConnectCommand());
        rootCommand.AddCommand(BuildStatusCommand());
        rootCommand.AddCommand(BuildInstallCommand());
        rootCommand.AddCommand(BuildLaunchCommand());
        rootCommand.AddCommand(BuildStopCommand());
        rootCommand.AddCommand(BuildPerfCommand());
        rootCommand.AddCommand(BuildHotloadCommand());
        rootCommand.AddCommand(BuildMonitorCommand());
        rootCommand.AddCommand(BuildTwinCommand());
        rootCommand.AddCommand(BuildCatalogCommand());
        rootCommand.AddCommand(BuildStudyCommand());
        rootCommand.AddCommand(BuildSussexCommand());
        rootCommand.AddCommand(BuildHzdbCommand());
        rootCommand.AddCommand(BuildToolingCommand());
        rootCommand.AddCommand(BuildWindowsEnvironmentCommand());
        rootCommand.AddCommand(BuildUtilityCommand());

        return await rootCommand.InvokeAsync(args);
    }

    private static IQuestControlService CreateQuestService(string? device = null)
    {
        var selector = device;
        if (string.IsNullOrWhiteSpace(selector))
        {
            var state = CliSessionState.Load();
            selector = state.ActiveEndpoint;
        }

        return QuestControlServiceFactory.CreateDefault(selector);
    }

    private static ILslMonitorService CreateMonitorService() => LslMonitorServiceFactory.CreateDefault();

    private static void SaveEndpoint(OperationOutcome result)
    {
        if (result.Kind is not (OperationOutcomeKind.Success or OperationOutcomeKind.Warning))
            return;
        if (string.IsNullOrWhiteSpace(result.Endpoint))
            return;

        var state = CliSessionState.Load().WithEndpoint(result.Endpoint);
        state.Save();
    }

    private static void SaveUsbSerial(OperationOutcome result)
    {
        if (result.Kind is not (OperationOutcomeKind.Success or OperationOutcomeKind.Warning))
            return;
        if (string.IsNullOrWhiteSpace(result.Endpoint))
            return;

        var state = CliSessionState.Load()
            .WithUsbSerial(result.Endpoint)
            .WithEndpoint(result.Endpoint);
        state.Save();
    }

    private static Command BuildProbeCommand()
    {
        var command = new Command("probe", "Detect Quest devices connected via USB");
        command.Handler = CommandHandler.Create(async (string? device) =>
        {
            var service = CreateQuestService(device);
            var result = await service.ProbeUsbAsync();
            SaveUsbSerial(result);
            PrintOutcome(result);
        });
        return command;
    }

    private static Command BuildWifiCommand()
    {
        var command = new Command("wifi", "Enable Wi-Fi ADB on the USB-connected Quest");
        command.Handler = CommandHandler.Create(async (string? device) =>
        {
            var service = CreateQuestService(device);
            var result = await service.EnableWifiFromUsbAsync();
            SaveEndpoint(result);
            PrintOutcome(result);
        });
        return command;
    }

    private static Command BuildConnectCommand()
    {
        var endpointArg = new Argument<string>("endpoint", description: "Quest IP address or endpoint (e.g. 192.168.43.1:5555)");
        var command = new Command("connect", "Connect to a Quest device over Wi-Fi") { endpointArg };
        command.Handler = CommandHandler.Create(async (string endpoint, string? device) =>
        {
            var service = CreateQuestService(device);
            var result = await service.ConnectAsync(endpoint);
            SaveEndpoint(result);
            PrintOutcome(result);
        });
        return command;
    }

    private static Command BuildStatusCommand()
    {
        var command = new Command("status", "Query headset status (connection, battery, foreground app)");
        command.Handler = CommandHandler.Create(async (string? device) =>
        {
            var service = CreateQuestService(device);
            var status = await service.QueryHeadsetStatusAsync(null, false);
            Console.WriteLine($"Connected:   {status.IsConnected}");
            Console.WriteLine($"Model:       {status.DeviceModel}");
            Console.WriteLine($"Battery:     {(status.BatteryLevel.HasValue ? $"{status.BatteryLevel}%" : "n/a")}");
            Console.WriteLine($"Software:    {(string.IsNullOrWhiteSpace(status.SoftwareVersion) ? "n/a" : status.SoftwareVersion)}");
            Console.WriteLine($"CPU Level:   {(status.CpuLevel.HasValue ? status.CpuLevel.ToString() : "n/a")}");
            Console.WriteLine($"GPU Level:   {(status.GpuLevel.HasValue ? status.GpuLevel.ToString() : "n/a")}");
            Console.WriteLine($"Foreground:  {status.ForegroundPackageId}");
            Console.WriteLine($"Component:   {status.ForegroundComponent ?? string.Empty}");
            Console.WriteLine($"Visible:     {string.Join(" | ", status.VisibleActivityComponents ?? Array.Empty<string>())}");
            Console.WriteLine($"Summary:     {status.Summary}");
        });
        return command;
    }

    private static Command BuildInstallCommand()
    {
        var apkArg = new Argument<string>("apk", description: "Path to an APK file");
        var command = new Command("install", "Install an APK file on the connected Quest") { apkArg };
        command.Handler = CommandHandler.Create(async (string apk, string? device) =>
        {
            var service = CreateQuestService(device);
            var target = new QuestAppTarget(
                Id: Path.GetFileNameWithoutExtension(apk),
                Label: Path.GetFileNameWithoutExtension(apk),
                PackageId: string.Empty,
                ApkFile: apk,
                LaunchComponent: string.Empty,
                BrowserPackageId: string.Empty,
                Description: $"CLI install: {apk}",
                Tags: []);
            var result = await service.InstallAppAsync(target);
            PrintOutcome(result);
        });
        return command;
    }

    private static Command BuildLaunchCommand()
    {
        var kioskOption = new Option<bool>("--kiosk", "Enter kiosk mode after launching the app.");
        var componentOption = new Option<string?>("--component", "Explicit launch component to use for the app.");
        var packageArg = new Argument<string>("package", description: "Package ID to launch");
        var command = new Command("launch", "Launch an app on the connected Quest. Wake the headset first; the launcher now blocks while the headset reports asleep.") { packageArg, kioskOption, componentOption };
        command.Handler = CommandHandler.Create(async (string package, bool kiosk, string? component, string? device) =>
        {
            var service = CreateQuestService(device);
            var target = new QuestAppTarget(
                Id: package,
                Label: package,
                PackageId: package,
                ApkFile: string.Empty,
                LaunchComponent: component ?? string.Empty,
                BrowserPackageId: string.Empty,
                Description: $"CLI launch: {package}",
                Tags: []);
            var result = await service.LaunchAppAsync(target, kioskMode: kiosk);
            PrintOutcome(result);
        });
        return command;
    }

    private static Command BuildStopCommand()
    {
        var exitKioskOption = new Option<bool>("--exit-kiosk", "Exit kiosk mode before stopping the app.");
        var packageArg = new Argument<string>("package", description: "Package ID to stop");
        var command = new Command("stop", "Stop an app on the connected Quest") { packageArg, exitKioskOption };
        command.Handler = CommandHandler.Create(async (string package, bool exitKiosk, string? device) =>
        {
            var service = CreateQuestService(device);
            var target = new QuestAppTarget(
                Id: package,
                Label: package,
                PackageId: package,
                ApkFile: string.Empty,
                LaunchComponent: string.Empty,
                BrowserPackageId: string.Empty,
                Description: $"CLI stop: {package}",
                Tags: []);
            var result = await service.StopAppAsync(target, exitKioskMode: exitKiosk);
            PrintOutcome(result);
        });
        return command;
    }

    private static Command BuildPerfCommand()
    {
        var cpuArg = new Argument<int>("cpu", description: "CPU performance level (0-5)");
        var gpuArg = new Argument<int>("gpu", description: "GPU performance level (0-5)");
        var command = new Command("perf", "Set Quest CPU and GPU performance levels") { cpuArg, gpuArg };
        command.Handler = CommandHandler.Create(async (int cpu, int gpu, string? device) =>
        {
            var service = CreateQuestService(device);
            var result = await service.ApplyPerformanceLevelsAsync(cpu, gpu);
            PrintOutcome(result);
        });
        return command;
    }

    private static Command BuildHotloadCommand()
    {
        var hotloadCommand = new Command("hotload", "Push a runtime hotload profile to the Quest");

        var profileArg = new Argument<string>("profile", description: "Hotload profile ID or CSV file path");
        var packageOption = new Option<string?>("--package", "Target package ID (default: first from profile)");
        var rootOption = new Option<string?>("--root", "Catalog root directory path");

        var pushCommand = new Command("push", "Push a hotload profile CSV to the device") { profileArg, packageOption, rootOption };
        pushCommand.Handler = CommandHandler.Create(async (string profile, string? package, string? root, string? device) =>
        {
            var service = CreateQuestService(device);
            var catalogRoot = root ?? ResolveCatalogRoot();

            // Resolve profile: either a file path or a profile ID from the catalog
            string csvPath;
            string packageId;

            if (File.Exists(profile))
            {
                csvPath = Path.GetFullPath(profile);
                packageId = package ?? "com.Viscereality.KarateBio";
            }
            else
            {
                var loader = new QuestSessionKitCatalogLoader();
                var catalog = await loader.LoadAsync(catalogRoot);
                var match = catalog.HotloadProfiles.FirstOrDefault(p =>
                    string.Equals(p.Id, profile, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    Console.Error.WriteLine($"Profile '{profile}' not found in catalog and is not a file path.");
                    return;
                }

                var hotloadDir = Path.Combine(catalogRoot, "HotloadProfiles");
                csvPath = Path.Combine(hotloadDir, match.File);
                if (!File.Exists(csvPath))
                {
                    Console.Error.WriteLine($"CSV file not found: {csvPath}");
                    return;
                }

                packageId = package ?? match.PackageIds.FirstOrDefault() ?? "com.Viscereality.KarateBio";
            }

            var target = new QuestAppTarget(
                Id: packageId, Label: packageId, PackageId: packageId,
                ApkFile: string.Empty, LaunchComponent: string.Empty,
                BrowserPackageId: string.Empty, Description: "Hotload target", Tags: []);

            var result = await service.ApplyHotloadProfileAsync(
                new HotloadProfile(
                    Path.GetFileNameWithoutExtension(csvPath),
                    Path.GetFileNameWithoutExtension(csvPath),
                    csvPath,
                    string.Empty, string.Empty, false, string.Empty, [packageId]),
                target);
            PrintOutcome(result);
        });

        var listCommand = new Command("list", "List available hotload profiles") { rootOption };
        listCommand.Handler = CommandHandler.Create(async (string? root) =>
        {
            var catalogRoot = root ?? ResolveCatalogRoot();
            var loader = new QuestSessionKitCatalogLoader();
            var catalog = await loader.LoadAsync(catalogRoot);

            Console.WriteLine("Hotload Profiles:");
            foreach (var profile in catalog.HotloadProfiles)
            {
                var lockLabel = profile.StudyLock ? " [LOCKED]" : "";
                Console.WriteLine($"  {profile.Id,-35} {profile.Channel,-12} {profile.File}{lockLabel}");
            }
        });

        hotloadCommand.AddCommand(pushCommand);
        hotloadCommand.AddCommand(listCommand);
        return hotloadCommand;
    }

    private static Command BuildMonitorCommand()
    {
        var nameOption = new Option<string>("--stream", () => "quest_monitor", "LSL stream name to monitor");
        var typeOption = new Option<string>("--type", () => "quest.telemetry", "LSL stream type to monitor");
        var channelOption = new Option<int>("--channel", () => 0, "Channel index to monitor");

        var command = new Command("monitor", "Monitor an LSL stream (continuous output)") { nameOption, typeOption, channelOption };
        command.Handler = CommandHandler.Create(async (string stream, string type, int channel) =>
        {
            var monitor = CreateMonitorService();
            var subscription = new LslMonitorSubscription(stream, type, channel);

            Console.WriteLine($"Monitoring {stream} / {type} channel {channel}...");
            Console.WriteLine("Press Ctrl+C to stop.");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                await foreach (var reading in monitor.MonitorAsync(subscription, cts.Token))
                {
                    var valueStr = reading.Value.HasValue ? $"{reading.Value:F4}" : "---";
                    var payload = reading.SampleValues is { Count: > 0 }
                        ? string.Join(" | ", reading.SampleValues)
                        : reading.TextValue ?? string.Empty;
                    var detail = string.IsNullOrWhiteSpace(payload)
                        ? reading.Detail
                        : $"{reading.Detail} Payload: {payload}";

                    Console.WriteLine($"[{reading.Timestamp:HH:mm:ss.fff}] {reading.Status,-30} {valueStr,10}  {reading.SampleRateHz:F1} Hz  {detail}");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Monitor stopped.");
            }
        });
        return command;
    }

    private static Command BuildTwinCommand()
    {
        var twinCommand = new Command("twin", "Twin mode operations");

        var actionArg = new Argument<string>("action", description: "Twin command action ID (e.g. twin-start, twin-pause)");
        var settleMsOption = new Option<int>(
            "--settle-ms",
            () => 1500,
            "Milliseconds to advertise the LSL command outlet before publishing the command.");
        var holdMsOption = new Option<int>(
            "--hold-ms",
            () => 3000,
            "Milliseconds to keep the LSL command outlet alive after publishing the command.");
        var sendCommand = new Command("send", "Send a twin command to the Quest")
        {
            actionArg,
            settleMsOption,
            holdMsOption
        };
        sendCommand.Handler = CommandHandler.Create(async (string action, int settleMs, int holdMs) =>
        {
            var bridge = TwinModeBridgeFactory.CreateDefault();
            try
            {
                if (bridge is LslTwinModeBridge lslBridge)
                {
                    var openResult = lslBridge.Open();
                    if (openResult.Kind == OperationOutcomeKind.Failure)
                    {
                        PrintOutcome(openResult);
                        return;
                    }

                    if (settleMs > 0)
                    {
                        await Task.Delay(Math.Min(settleMs, 30000));
                    }
                }

                var twinCmd = new TwinModeCommand(action, action);
                var result = await bridge.SendCommandAsync(twinCmd);
                PrintOutcome(result);

                if (holdMs > 0)
                {
                    await Task.Delay(Math.Min(holdMs, 30000));
                }
            }
            finally
            {
                if (bridge is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        });

        var statusCommand = new Command("status", "Show twin bridge status and settings comparison");
        statusCommand.Handler = CommandHandler.Create(() =>
        {
            var bridge = TwinModeBridgeFactory.CreateDefault();
            var status = bridge.Status;
            Console.WriteLine($"Available:   {status.IsAvailable}");
            Console.WriteLine($"Summary:     {status.Summary}");
            Console.WriteLine($"Detail:      {status.Detail}");
        });

        twinCommand.AddCommand(sendCommand);
        twinCommand.AddCommand(statusCommand);
        return twinCommand;
    }

    private static Command BuildCatalogCommand()
    {
        var catalogCommand = new Command("catalog", "Quest Session Kit catalog operations");

        var rootOption = new Option<string?>("--root", "Catalog root directory path");
        var listCommand = new Command("list", "List available apps, bundles, and profiles") { rootOption };
        listCommand.Handler = CommandHandler.Create(async (string? root) =>
        {
            var loader = new QuestSessionKitCatalogLoader();
            var catalogRoot = root ?? ResolveCatalogRoot();
            var catalog = await loader.LoadAsync(catalogRoot);

            Console.WriteLine($"Source: {catalog.Source.Label} ({catalog.Source.RootPath})");
            Console.WriteLine();

            Console.WriteLine("Apps:");
            foreach (var app in catalog.Apps)
            {
                Console.WriteLine($"  {app.Id,-35} {app.PackageId,-50} {app.Description}");
            }

            Console.WriteLine();
            Console.WriteLine("Bundles:");
            foreach (var bundle in catalog.Bundles)
            {
                Console.WriteLine($"  {bundle.Id,-35} [{string.Join(", ", bundle.AppIds)}]");
            }

            Console.WriteLine();
            Console.WriteLine("Hotload Profiles:");
            foreach (var profile in catalog.HotloadProfiles)
            {
                var lockLabel = profile.StudyLock ? " [LOCKED]" : "";
                Console.WriteLine($"  {profile.Id,-35} {profile.Channel,-12} {profile.File}{lockLabel}");
            }

            Console.WriteLine();
            Console.WriteLine("Device Profiles:");
            foreach (var profile in catalog.DeviceProfiles)
            {
                Console.WriteLine($"  {profile.Id,-35} {profile.Properties.Count} properties");
            }
        });

        catalogCommand.AddCommand(listCommand);
        return catalogCommand;
    }

    private static Command BuildStudyCommand()
    {
        var studyCommand = new Command("study", "Pinned study-shell operations that mirror the GUI workflow");
        var rootOption = new Option<string?>("--root", "Study shell catalog root directory path");
        var studyArg = new Argument<string>("study", description: "Study shell ID (for example: sussex-university)");

        var listCommand = new Command("list", "List available study shells") { rootOption };
        listCommand.Handler = CommandHandler.Create(async (string? root) =>
        {
            var catalog = await LoadStudyShellCatalogAsync(root);

            Console.WriteLine($"Source: {catalog.Source.Label} ({catalog.Source.RootPath})");
            Console.WriteLine();

            foreach (var study in catalog.Studies)
            {
                var kioskLabel = study.App.LaunchInKioskMode ? "kiosk" : "standard";
                Console.WriteLine($"  {study.Id,-24} {study.App.PackageId,-40} {kioskLabel,-8} {study.Label}");
            }
        });

        var installCommand = new Command("install", "Install the pinned study APK") { studyArg, rootOption };
        installCommand.Handler = CommandHandler.Create(async (string study, string? root, string? device) =>
        {
            var definition = await ResolveStudyShellAsync(study, root);
            var service = CreateQuestService(device);
            var result = await service.InstallAppAsync(StudyShellOperatorBindings.CreateQuestTarget(definition));
            PrintOutcome(result);
        });

        var profileCommand = new Command("apply-profile", "Apply the pinned study device profile") { studyArg, rootOption };
        profileCommand.Handler = CommandHandler.Create(async (string study, string? root, string? device) =>
        {
            var definition = await ResolveStudyShellAsync(study, root);
            var service = CreateQuestService(device);
            var result = await service.ApplyDeviceProfileAsync(StudyShellOperatorBindings.CreateDeviceProfile(definition));
            PrintOutcome(result);
        });

        var launchCommand = new Command("launch", "Launch the pinned study runtime using the study kiosk policy. Wake the headset first; the launcher now blocks while the headset reports asleep.") { studyArg, rootOption };
        launchCommand.Handler = CommandHandler.Create(async (string study, string? root, string? device) =>
        {
            var definition = await ResolveStudyShellAsync(study, root);
            var service = CreateQuestService(device);
            var startupSync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(
                definition,
                service,
                definition.Id,
                root,
                forceWhenStudyNotForeground: true);
            if (!string.IsNullOrWhiteSpace(startupSync.Summary))
            {
                Console.WriteLine($"[STARTUP] {startupSync.Summary}");
                if (!string.IsNullOrWhiteSpace(startupSync.Detail))
                {
                    Console.WriteLine($"          {startupSync.Detail}");
                }
            }

            var target = StudyShellOperatorBindings.CreateQuestTarget(definition);
            var result = await service.LaunchAppAsync(target, kioskMode: definition.App.LaunchInKioskMode);
            PrintOutcome(result);
        });

        var stopCommand = new Command("stop", "Stop the pinned study runtime using the study kiosk-exit policy") { studyArg, rootOption };
        stopCommand.Handler = CommandHandler.Create(async (string study, string? root, string? device) =>
        {
            var definition = await ResolveStudyShellAsync(study, root);
            var service = CreateQuestService(device);
            var target = StudyShellOperatorBindings.CreateQuestTarget(definition);
            var result = await service.StopAppAsync(target, exitKioskMode: definition.App.LaunchInKioskMode);
            PrintOutcome(result);
            if (result.Kind != OperationOutcomeKind.Failure)
            {
                var startupSync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(
                    definition,
                    service,
                    definition.Id,
                    root,
                    forceWhenStudyNotForeground: true);
                if (!string.IsNullOrWhiteSpace(startupSync.Summary))
                {
                    Console.WriteLine($"[STARTUP] {startupSync.Summary}");
                    if (!string.IsNullOrWhiteSpace(startupSync.Detail))
                    {
                        Console.WriteLine($"          {startupSync.Detail}");
                    }
                }
            }
        });

        var statusCommand = new Command("status", "Show current headset state against the pinned study verification baseline") { studyArg, rootOption };
        statusCommand.Handler = CommandHandler.Create(async (string study, string? root, string? device) =>
        {
            var definition = await ResolveStudyShellAsync(study, root);
            var service = CreateQuestService(device);
            var target = StudyShellOperatorBindings.CreateQuestTarget(definition);
            var profile = StudyShellOperatorBindings.CreateDeviceProfile(definition);
            var headset = await service.QueryHeadsetStatusAsync(target, false);
            var installed = await service.QueryInstalledAppAsync(target);
            var profileStatus = await service.QueryDeviceProfileStatusAsync(profile);
            var baseline = definition.App.VerificationBaseline;
            var installedMatchesPinned = MatchesHash(installed.InstalledSha256, definition.App.Sha256);
            var baselineMatches =
                baseline is not null &&
                headset.IsConnected &&
                installed.IsInstalled &&
                installedMatchesPinned &&
                profileStatus.IsActive &&
                !string.IsNullOrWhiteSpace(headset.SoftwareReleaseOrCodename) &&
                !string.IsNullOrWhiteSpace(headset.SoftwareBuildId) &&
                StudyVerificationFingerprint.Matches(
                    baseline,
                    definition.App.PackageId,
                    installed.InstalledSha256,
                    headset.SoftwareReleaseOrCodename,
                    headset.SoftwareBuildId,
                    definition.DeviceProfile.Id,
                    headset.SoftwareDisplayId);

            Console.WriteLine($"Study:             {definition.Label}");
            Console.WriteLine($"Package:           {definition.App.PackageId}");
            Console.WriteLine($"Kiosk policy:      {definition.App.LaunchInKioskMode}");
            Console.WriteLine($"Pinned SHA256:     {definition.App.Sha256}");
            Console.WriteLine($"Installed SHA256:  {(string.IsNullOrWhiteSpace(installed.InstalledSha256) ? "n/a" : installed.InstalledSha256)}");
            Console.WriteLine($"Installed match:   {installedMatchesPinned}");
            Console.WriteLine($"Software:          {(string.IsNullOrWhiteSpace(headset.SoftwareVersion) ? "n/a" : headset.SoftwareVersion)}");
            Console.WriteLine($"Device profile:    {profileStatus.Summary}");
            Console.WriteLine($"Device profile ok: {profileStatus.IsActive}");
            Console.WriteLine($"Headset summary:   {headset.Summary}");

            if (baseline is null)
            {
                Console.WriteLine("Verified baseline: none recorded");
            }
            else
            {
                Console.WriteLine($"Verified baseline: {baseline.SoftwareVersion} | build {baseline.BuildId}");
                Console.WriteLine($"Verified at:       {(baseline.VerifiedAtUtc.HasValue ? baseline.VerifiedAtUtc.Value.ToString("O") : "n/a")}");
                Console.WriteLine($"Environment hash:  {baseline.EnvironmentHash}");
                Console.WriteLine($"Environment match: {baselineMatches}");
            }
        });

        var probeJsonOption = new Option<bool>("--json", "Write machine-readable JSON output.");
        var probeWaitOption = new Option<int>("--wait-seconds", () => 4, "How long to wait for fresh quest_twin_state after opening the local bridge.");
        var probeConnectionCommand = new Command("probe-connection", "Probe the Sussex LSL inlet and quest_twin_state return path, mirroring the Step 9 guide check") { studyArg, rootOption, probeJsonOption, probeWaitOption };
        probeConnectionCommand.Handler = CommandHandler.Create(async (string study, string? root, bool json, int waitSeconds, string? device) =>
        {
            var definition = await ResolveStudyShellAsync(study, root);
            var service = CreateQuestService(device);
            var waitDuration = TimeSpan.FromSeconds(Math.Max(0, waitSeconds));
            var result = await DiagnosticsCliSupport.ProbeStudyConnectionAsync(definition, service, device, waitDuration).ConfigureAwait(false);

            if (json)
            {
                SussexCliSupport.WriteJson(result);
                return;
            }

            DiagnosticsCliSupport.PrintStudyConnectionProbe(result);
        });

        var reportJsonOption = new Option<bool>("--json", "Write machine-readable JSON output.");
        var reportWaitOption = new Option<int>("--wait-seconds", () => 12, "How long to wait for fresh quest_twin_state before classifying the return path.");
        var reportOutputOption = new Option<string?>("--output-dir", "Directory for the generated JSON, LaTeX source, and PDF report. Defaults to the operator diagnostics folder.");
        var reportSkipCommandOption = new Option<bool>("--skip-command-check", "Skip the safe particle-off twin command acknowledgement probe.");
        var reportNoPdfOption = new Option<bool>("--no-pdf", "Write JSON and LaTeX only; skip PDF generation.");
        var diagnosticsReportCommand = new Command("diagnostics-report", "Generate a shareable Sussex LSL/twin diagnostics report as JSON, LaTeX source, and PDF")
        {
            studyArg,
            rootOption,
            reportJsonOption,
            reportWaitOption,
            reportOutputOption,
            reportSkipCommandOption,
            reportNoPdfOption
        };
        diagnosticsReportCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            bool json,
            int waitSeconds,
            string? outputDir,
            bool skipCommandCheck,
            bool noPdf,
            string? device) =>
        {
            var definition = await ResolveStudyShellAsync(study, root);
            var questService = CreateQuestService(device);
            using var clockAlignment = StudyClockAlignmentServiceFactory.CreateDefault();
            using var testSender = TestLslSignalServiceFactory.CreateDefault();
            var streamDiscovery = LslStreamDiscoveryServiceFactory.CreateDefault();
            var bridge = TwinModeBridgeFactory.CreateDefault();
            try
            {
                var windowsEnvironment = new WindowsEnvironmentAnalysisService(
                    CreateMonitorService(),
                    streamDiscovery,
                    clockAlignment,
                    testSender,
                    bridge);
                var reportService = new SussexDiagnosticsReportService(
                    questService,
                    windowsEnvironment,
                    streamDiscovery,
                    testSender,
                    bridge);
                var result = await reportService.GenerateAsync(
                        new SussexDiagnosticsReportRequest(
                            definition,
                            DeviceSelector: device,
                            OutputDirectory: outputDir,
                            ProbeWaitDuration: TimeSpan.FromSeconds(Math.Max(0, waitSeconds)),
                            RunCommandAcceptanceCheck: !skipCommandCheck))
                    .ConfigureAwait(false);

                OperationOutcome? pdfOutcome = null;
                if (!noPdf)
                {
                    pdfOutcome = await GenerateSussexDiagnosticsPdfAsync(result.JsonPath, result.PdfPath).ConfigureAwait(false);
                }

                if (json)
                {
                    SussexCliSupport.WriteJson(new
                    {
                        result.Level,
                        result.Summary,
                        result.Detail,
                        result.ReportDirectory,
                        result.JsonPath,
                        result.TexPath,
                        result.PdfPath,
                        PdfOutcome = pdfOutcome,
                        result.Report
                    });
                    return;
                }

                DiagnosticsCliSupport.PrintSussexDiagnosticsReport(result, pdfOutcome);
            }
            finally
            {
                (bridge as IDisposable)?.Dispose();
            }
        });

        studyCommand.AddCommand(listCommand);
        studyCommand.AddCommand(installCommand);
        studyCommand.AddCommand(profileCommand);
        studyCommand.AddCommand(launchCommand);
        studyCommand.AddCommand(stopCommand);
        studyCommand.AddCommand(statusCommand);
        studyCommand.AddCommand(probeConnectionCommand);
        studyCommand.AddCommand(diagnosticsReportCommand);
        return studyCommand;
    }

    private static Command BuildSussexCommand()
    {
        var sussexCommand = new Command("sussex", "Sussex profile automation commands that mirror the GUI profile tabs");
        var studyOption = new Option<string>("--study", () => SussexCliSupport.DefaultStudyId, "Study shell ID used for local startup/apply state.");
        var rootOption = new Option<string?>("--root", "Study shell catalog root directory path");
        var jsonOption = new Option<bool>("--json", "Emit JSON output.");
        sussexCommand.AddGlobalOption(studyOption);
        sussexCommand.AddGlobalOption(rootOption);
        sussexCommand.AddGlobalOption(jsonOption);

        static Option<string[]> CreateRepeatedAssignmentOption(string name, string description)
        {
            var option = new Option<string[]>(name, description)
            {
                Arity = ArgumentArity.ZeroOrMore
            };
            option.AllowMultipleArgumentsPerToken = true;
            return option;
        }

        sussexCommand.AddCommand(BuildSussexVisualCommand(studyOption, rootOption, jsonOption, CreateRepeatedAssignmentOption));
        sussexCommand.AddCommand(BuildSussexControllerCommand(studyOption, rootOption, jsonOption, CreateRepeatedAssignmentOption));
        return sussexCommand;
    }

    private static Command BuildSussexVisualCommand(
        Option<string> studyOption,
        Option<string?> rootOption,
        Option<bool> jsonOption,
        Func<string, string, Option<string[]>> createRepeatedAssignmentOption)
    {
        var visual = new Command("visual", "Sussex visual-profile commands");

        var list = new Command("list", "List bundled and local Sussex visual profiles");
        list.Handler = CommandHandler.Create(async (string study, string? root, bool json) =>
        {
            var profiles = await SussexCliSupport.LoadVisualProfilesAsync(study, root);
            if (json)
            {
                SussexCliSupport.WriteJson(profiles.Select(profile => new
                {
                    id = profile.Record.Id,
                    name = profile.Record.Document.Profile.Name,
                    kind = profile.IsBundledBaseline ? "bundled-baseline" : profile.IsBundledProfile ? "bundled-profile" : "local-profile",
                    path = string.IsNullOrWhiteSpace(profile.Record.FilePath) ? null : profile.Record.FilePath
                }));
            }
            else
            {
                SussexCliSupport.PrintVisualProfiles(profiles, study);
            }
        });

        var fields = new Command("fields", "List all Sussex visual field ids, ranges, and tooltip metadata");
        fields.Handler = CommandHandler.Create((bool json) =>
        {
            var specs = SussexCliSupport.BuildVisualFieldSpecs();
            if (json)
            {
                SussexCliSupport.WriteJson(specs);
            }
            else
            {
                SussexCliSupport.PrintFieldSpecs("Sussex Visual Fields:", specs);
            }
        });

        var profileArg = new Argument<string>("profile", "Visual profile id or name.");
        var show = new Command("show", "Show one Sussex visual profile, including its field metadata") { profileArg };
        show.Handler = CommandHandler.Create(async (string profile, string study, string? root) =>
        {
            var profiles = await SussexCliSupport.LoadVisualProfilesAsync(study, root);
            var resolved = SussexCliSupport.ResolveVisualProfile(profiles, profile);
            SussexCliSupport.WriteJson(SussexCliSupport.BuildVisualProfileView(resolved, study));
        });

        var fromOption = new Option<string>("--from", () => "bundled-baseline", "Source profile id/name, or bundled-baseline.");
        var nameOption = new Option<string>("--name", "New profile name.") { IsRequired = true };
        var notesOption = new Option<string?>("--notes", "Profile notes. Pass an empty string to clear notes.");
        var setOption = createRepeatedAssignmentOption("--set", "Set one or more field values as id=value.");
        var scaleOption = createRepeatedAssignmentOption("--scale", "Scale one or more numeric fields as id=factor.");
        var setStartupOption = new Option<bool>("--set-startup", "Also save the resulting profile as the next-launch default.");

        var create = new Command("create", "Create a new Sussex visual profile from baseline or another profile");
        create.AddOption(fromOption);
        create.AddOption(nameOption);
        create.AddOption(notesOption);
        create.AddOption(setOption);
        create.AddOption(scaleOption);
        create.AddOption(setStartupOption);
        create.Handler = CommandHandler.Create(async (string from, string name, string? notes, string[] set, string[] scale, bool setStartup, string study, string? root, bool json, string? device) =>
        {
            var compiler = SussexCliSupport.CreateVisualCompiler();
            var store = SussexCliSupport.CreateVisualStore(compiler);
            var profiles = await SussexCliSupport.LoadVisualProfilesAsync(study, root);
            var source = SussexCliSupport.ResolveVisualProfile(profiles, from);
            var document = SussexCliSupport.BuildVisualDocument(compiler, source.Record.Document, name, notes, set, scale);
            var saved = await SussexCliSupport.SaveVisualAsNewAsync(store, document);

            SussexCliSupport.StartupSyncResult? sync = null;
            if (setStartup)
            {
                SussexCliSupport.SaveVisualStartupState(study, saved);
                var definition = await ResolveStudyShellAsync(study, root);
                sync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(definition, CreateQuestService(device), study, root, forceWhenStudyNotForeground: false);
            }

            if (json)
            {
                SussexCliSupport.WriteJson(new
                {
                    created = SussexCliSupport.BuildVisualProfileView(new SussexCliSupport.VisualResolvedProfile(saved, false, false), study),
                    startup_sync = sync
                });
            }
            else
            {
                Console.WriteLine($"Created visual profile: {saved.Document.Profile.Name} ({saved.Id})");
                Console.WriteLine(saved.FilePath);
                if (sync is not null)
                {
                    Console.WriteLine(sync.Summary);
                    Console.WriteLine(sync.Detail);
                }
            }
        });

        var update = new Command("update", "Update an existing local Sussex visual profile") { profileArg };
        update.AddOption(nameOption);
        update.AddOption(notesOption);
        update.AddOption(setOption);
        update.AddOption(scaleOption);
        update.AddOption(setStartupOption);
        update.Handler = CommandHandler.Create(async (string profile, string? name, string? notes, string[] set, string[] scale, bool setStartup, string study, string? root, bool json, string? device) =>
        {
            var compiler = SussexCliSupport.CreateVisualCompiler();
            var store = SussexCliSupport.CreateVisualStore(compiler);
            var profiles = await SussexCliSupport.LoadVisualProfilesAsync(study, root);
            var target = SussexCliSupport.ResolveVisualProfile(profiles, profile);
            var startupBefore = new SussexVisualProfileStartupStateStore(study).Load();
            var document = SussexCliSupport.BuildVisualDocument(compiler, target.Record.Document, name, notes, set, scale);
            var saved = await SussexCliSupport.SaveVisualExistingAsync(store, target, document);
            var shouldSyncStartup = setStartup || string.Equals(startupBefore?.ProfileId, target.Record.Id, StringComparison.OrdinalIgnoreCase);
            SussexCliSupport.RefreshVisualStateAfterSave(study, target.Record.Id, saved, forceSetStartup: setStartup);

            SussexCliSupport.StartupSyncResult? sync = null;
            if (shouldSyncStartup)
            {
                var definition = await ResolveStudyShellAsync(study, root);
                sync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(definition, CreateQuestService(device), study, root, forceWhenStudyNotForeground: false);
            }

            if (json)
            {
                SussexCliSupport.WriteJson(new
                {
                    updated = SussexCliSupport.BuildVisualProfileView(new SussexCliSupport.VisualResolvedProfile(saved, false, false), study),
                    startup_sync = sync
                });
            }
            else
            {
                Console.WriteLine($"Updated visual profile: {saved.Document.Profile.Name} ({saved.Id})");
                Console.WriteLine(saved.FilePath);
                if (sync is not null)
                {
                    Console.WriteLine(sync.Summary);
                    Console.WriteLine(sync.Detail);
                }
            }
        });

        var delete = new Command("delete", "Delete one local Sussex visual profile") { profileArg };
        delete.Handler = CommandHandler.Create(async (string profile, string study, string? root, bool json, string? device) =>
        {
            var compiler = SussexCliSupport.CreateVisualCompiler();
            var store = SussexCliSupport.CreateVisualStore(compiler);
            var profiles = await SussexCliSupport.LoadVisualProfilesAsync(study, root);
            var target = SussexCliSupport.ResolveVisualProfile(profiles, profile);
            if (!target.IsWritableLocalProfile)
            {
                throw new InvalidOperationException("Only local Sussex visual profiles can be deleted.");
            }

            var startupBefore = new SussexVisualProfileStartupStateStore(study).Load();
            var shouldSyncStartup = string.Equals(startupBefore?.ProfileId, target.Record.Id, StringComparison.OrdinalIgnoreCase);
            SussexCliSupport.ClearVisualStateForDeletedProfile(study, target.Record.Id);
            await store.DeleteAsync(target.Record.FilePath);

            SussexCliSupport.StartupSyncResult? sync = null;
            if (shouldSyncStartup)
            {
                var definition = await ResolveStudyShellAsync(study, root);
                sync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(definition, CreateQuestService(device), study, root, forceWhenStudyNotForeground: false);
            }

            if (json)
            {
                SussexCliSupport.WriteJson(new { deleted = target.Record.Id, startup_sync = sync });
            }
            else
            {
                Console.WriteLine($"Deleted visual profile: {target.Record.Id}");
                if (sync is not null)
                {
                    Console.WriteLine(sync.Summary);
                    Console.WriteLine(sync.Detail);
                }
            }
        });

        var importArg = new Argument<string>("path", "Path to a Sussex visual profile JSON file.");
        var import = new Command("import", "Import a Sussex visual profile JSON file") { importArg };
        import.Handler = CommandHandler.Create(async (string path, string study, bool json) =>
        {
            var store = SussexCliSupport.CreateVisualStore(SussexCliSupport.CreateVisualCompiler());
            var imported = await store.ImportAsync(path);
            if (json)
            {
                SussexCliSupport.WriteJson(new { imported = SussexCliSupport.BuildVisualProfileView(new SussexCliSupport.VisualResolvedProfile(imported, false, false), study) });
            }
            else
            {
                Console.WriteLine($"Imported visual profile: {imported.Document.Profile.Name} ({imported.Id})");
                Console.WriteLine(imported.FilePath);
            }
        });

        var exportArg = new Argument<string>("path", "Destination JSON path.");
        var export = new Command("export", "Export one Sussex visual profile as JSON") { profileArg, exportArg };
        export.Handler = CommandHandler.Create(async (string profile, string path, string study, string? root, bool json) =>
        {
            var store = SussexCliSupport.CreateVisualStore(SussexCliSupport.CreateVisualCompiler());
            var profiles = await SussexCliSupport.LoadVisualProfilesAsync(study, root);
            var target = SussexCliSupport.ResolveVisualProfile(profiles, profile);
            await store.ExportAsync(target.Record.Document, path);
            if (json)
            {
                SussexCliSupport.WriteJson(new { exported = target.Record.Id, path = Path.GetFullPath(path) });
            }
            else
            {
                Console.WriteLine($"Exported visual profile {target.Record.Id} -> {Path.GetFullPath(path)}");
            }
        });

        var setStartup = new Command("set-startup", "Set the next-launch Sussex visual profile") { profileArg };
        setStartup.Handler = CommandHandler.Create(async (string profile, string study, string? root, bool json, string? device) =>
        {
            var profiles = await SussexCliSupport.LoadVisualProfilesAsync(study, root);
            var target = SussexCliSupport.ResolveVisualProfile(profiles, profile);
            SussexCliSupport.SaveVisualStartupState(study, target.IsBundledBaseline ? null : target.Record);
            var definition = await ResolveStudyShellAsync(study, root);
            var sync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(definition, CreateQuestService(device), study, root, forceWhenStudyNotForeground: false);
            if (json)
            {
                SussexCliSupport.WriteJson(new { startup = target.IsBundledBaseline ? "bundled-baseline" : target.Record.Id, sync });
            }
            else
            {
                Console.WriteLine(target.IsBundledBaseline
                    ? "Visual startup profile reset to bundled baseline."
                    : $"Visual startup profile set to {target.Record.Document.Profile.Name} ({target.Record.Id}).");
                Console.WriteLine(sync.Summary);
                Console.WriteLine(sync.Detail);
            }
        });

        var clearStartup = new Command("clear-startup", "Reset the next-launch Sussex visual profile to the bundled baseline");
        clearStartup.Handler = CommandHandler.Create(async (string study, string? root, bool json, string? device) =>
        {
            SussexCliSupport.SaveVisualStartupState(study, null);
            var definition = await ResolveStudyShellAsync(study, root);
            var sync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(definition, CreateQuestService(device), study, root, forceWhenStudyNotForeground: false);
            if (json)
            {
                SussexCliSupport.WriteJson(new { startup = "bundled-baseline", sync });
            }
            else
            {
                Console.WriteLine("Visual startup profile reset to bundled baseline.");
                Console.WriteLine(sync.Summary);
                Console.WriteLine(sync.Detail);
            }
        });

        var applyLive = new Command("apply-live", "Apply one Sussex visual profile to the current running Sussex session") { profileArg };
        applyLive.Handler = CommandHandler.Create(async (string profile, string study, string? root, bool json, string? device) =>
        {
            var definition = await ResolveStudyShellAsync(study, root);
            var profiles = await SussexCliSupport.LoadVisualProfilesAsync(study, root);
            var target = SussexCliSupport.ResolveVisualProfile(profiles, profile);
            var result = await SussexCliSupport.ApplyVisualLiveAsync(definition, CreateQuestService(device), TwinModeBridgeFactory.CreateDefault(), study, target.Record);
            if (json)
            {
                SussexCliSupport.WriteJson(new { outcome = result.Outcome, csv_path = result.CsvPath });
            }
            else
            {
                PrintOutcome(result.Outcome);
            }
        });

        visual.AddCommand(list);
        visual.AddCommand(fields);
        visual.AddCommand(show);
        visual.AddCommand(create);
        visual.AddCommand(update);
        visual.AddCommand(delete);
        visual.AddCommand(import);
        visual.AddCommand(export);
        visual.AddCommand(setStartup);
        visual.AddCommand(clearStartup);
        visual.AddCommand(applyLive);
        return visual;
    }

    private static Command BuildSussexControllerCommand(
        Option<string> studyOption,
        Option<string?> rootOption,
        Option<bool> jsonOption,
        Func<string, string, Option<string[]>> createRepeatedAssignmentOption)
    {
        var controller = new Command("controller", "Sussex controller-breathing profile commands, including calibration mode, accepted-motion thresholds, and controller vibration.");

        var list = new Command("list", "List bundled and local Sussex controller-breathing profiles");
        list.Handler = CommandHandler.Create(async (string study, string? root, bool json) =>
        {
            var profiles = await SussexCliSupport.LoadControllerProfilesAsync(study, root);
            if (json)
            {
                SussexCliSupport.WriteJson(
                    new[]
                    {
                        new { id = "bundled-baseline", name = "Bundled Sussex controller-breathing baseline", kind = "bundled-baseline", path = (string?)null }
                    }
                    .Concat(profiles.Select(profile => new
                    {
                        id = profile.Record.Id,
                        name = profile.Record.Document.Profile.Name,
                        kind = profile.IsBundledProfile ? "bundled-profile" : "local-profile",
                        path = (string?)profile.Record.FilePath
                    })));
            }
            else
            {
                SussexCliSupport.PrintControllerProfiles(profiles, study);
            }
        });

        var fields = new Command("fields", "List all Sussex controller-breathing field ids, including calibration mode, accepted-motion, and vibration controls.");
        fields.Handler = CommandHandler.Create((bool json) =>
        {
            var specs = SussexCliSupport.BuildControllerFieldSpecs();
            if (json)
            {
                SussexCliSupport.WriteJson(specs);
            }
            else
            {
                SussexCliSupport.PrintFieldSpecs("Sussex Controller-Breathing Fields:", specs);
            }
        });

        var profileArg = new Argument<string>("profile", "Controller-breathing profile id or name.");
        var show = new Command("show", "Show one Sussex controller-breathing profile, including its field metadata") { profileArg };
        show.Handler = CommandHandler.Create(async (string profile, string study, string? root) =>
        {
            var profiles = await SussexCliSupport.LoadControllerProfilesAsync(study, root);
            var resolved = SussexCliSupport.ResolveControllerProfile(profiles, profile, allowBaselineTemplate: true);
            SussexCliSupport.WriteJson(SussexCliSupport.BuildControllerProfileView(resolved, study));
        });

        var fromOption = new Option<string>("--from", () => "bundled-baseline", "Source profile id/name, or bundled-baseline.");
        var nameOption = new Option<string>("--name", "New profile name.") { IsRequired = true };
        var notesOption = new Option<string?>("--notes", "Profile notes. Pass an empty string to clear notes.");
        var setOption = createRepeatedAssignmentOption("--set", "Set one or more field values as id=value. Examples: use_principal_axis_calibration=off, min_accepted_delta=0.0004, min_acceptable_travel=0.01.");
        var scaleOption = createRepeatedAssignmentOption("--scale", "Scale one or more numeric fields as id=factor. Use the fields command first for valid ids and safe ranges.");
        var setStartupOption = new Option<bool>("--set-startup", "Also save the resulting profile as the next-launch default.");

        var create = new Command("create", "Create a new Sussex controller-breathing profile from baseline or another profile, including calibration mode, accepted-motion thresholds, and vibration.");
        create.AddOption(fromOption);
        create.AddOption(nameOption);
        create.AddOption(notesOption);
        create.AddOption(setOption);
        create.AddOption(scaleOption);
        create.AddOption(setStartupOption);
        create.Handler = CommandHandler.Create(async (string from, string name, string? notes, string[] set, string[] scale, bool setStartup, string study, string? root, bool json, string? device) =>
        {
            var compiler = SussexCliSupport.CreateControllerCompiler();
            var store = SussexCliSupport.CreateControllerStore(compiler);
            var profiles = await SussexCliSupport.LoadControllerProfilesAsync(study, root);
            var source = SussexCliSupport.ResolveControllerProfile(profiles, from, allowBaselineTemplate: true);
            var document = SussexCliSupport.BuildControllerDocument(compiler, source.Record.Document, name, notes, set, scale);
            var saved = await SussexCliSupport.SaveControllerAsNewAsync(store, document);

            SussexCliSupport.StartupSyncResult? sync = null;
            if (setStartup)
            {
                SussexCliSupport.SaveControllerStartupState(study, saved);
                var definition = await ResolveStudyShellAsync(study, root);
                sync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(definition, CreateQuestService(device), study, root, forceWhenStudyNotForeground: false);
            }

            if (json)
            {
                SussexCliSupport.WriteJson(new
                {
                    created = SussexCliSupport.BuildControllerProfileView(new SussexCliSupport.ControllerResolvedProfile(saved, false), study),
                    startup_sync = sync
                });
            }
            else
            {
                Console.WriteLine($"Created controller-breathing profile: {saved.Document.Profile.Name} ({saved.Id})");
                Console.WriteLine(saved.FilePath);
                if (sync is not null)
                {
                    Console.WriteLine(sync.Summary);
                    Console.WriteLine(sync.Detail);
                }
            }
        });

        var update = new Command("update", "Update an existing local Sussex controller-breathing profile, including calibration mode, accepted-motion thresholds, and vibration.") { profileArg };
        update.AddOption(nameOption);
        update.AddOption(notesOption);
        update.AddOption(setOption);
        update.AddOption(scaleOption);
        update.AddOption(setStartupOption);
        update.Handler = CommandHandler.Create(async (string profile, string? name, string? notes, string[] set, string[] scale, bool setStartup, string study, string? root, bool json, string? device) =>
        {
            var compiler = SussexCliSupport.CreateControllerCompiler();
            var store = SussexCliSupport.CreateControllerStore(compiler);
            var profiles = await SussexCliSupport.LoadControllerProfilesAsync(study, root);
            var target = SussexCliSupport.ResolveControllerProfile(profiles, profile, allowBaselineTemplate: false);
            var startupBefore = new SussexControllerBreathingProfileStartupStateStore(study).Load();
            var document = SussexCliSupport.BuildControllerDocument(compiler, target.Record.Document, name, notes, set, scale);
            var saved = await SussexCliSupport.SaveControllerExistingAsync(store, target, document);
            var shouldSyncStartup = setStartup || string.Equals(startupBefore?.ProfileId, target.Record.Id, StringComparison.OrdinalIgnoreCase);
            SussexCliSupport.RefreshControllerStateAfterSave(study, target.Record.Id, saved, forceSetStartup: setStartup);

            SussexCliSupport.StartupSyncResult? sync = null;
            if (shouldSyncStartup)
            {
                var definition = await ResolveStudyShellAsync(study, root);
                sync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(definition, CreateQuestService(device), study, root, forceWhenStudyNotForeground: false);
            }

            if (json)
            {
                SussexCliSupport.WriteJson(new
                {
                    updated = SussexCliSupport.BuildControllerProfileView(new SussexCliSupport.ControllerResolvedProfile(saved, false), study),
                    startup_sync = sync
                });
            }
            else
            {
                Console.WriteLine($"Updated controller-breathing profile: {saved.Document.Profile.Name} ({saved.Id})");
                Console.WriteLine(saved.FilePath);
                if (sync is not null)
                {
                    Console.WriteLine(sync.Summary);
                    Console.WriteLine(sync.Detail);
                }
            }
        });

        var delete = new Command("delete", "Delete one local Sussex controller-breathing profile") { profileArg };
        delete.Handler = CommandHandler.Create(async (string profile, string study, string? root, bool json, string? device) =>
        {
            var compiler = SussexCliSupport.CreateControllerCompiler();
            var store = SussexCliSupport.CreateControllerStore(compiler);
            var profiles = await SussexCliSupport.LoadControllerProfilesAsync(study, root);
            var target = SussexCliSupport.ResolveControllerProfile(profiles, profile, allowBaselineTemplate: false);
            var startupBefore = new SussexControllerBreathingProfileStartupStateStore(study).Load();
            var shouldSyncStartup = string.Equals(startupBefore?.ProfileId, target.Record.Id, StringComparison.OrdinalIgnoreCase);
            SussexCliSupport.ClearControllerStateForDeletedProfile(study, target.Record.Id);
            await store.DeleteAsync(target.Record.FilePath);

            SussexCliSupport.StartupSyncResult? sync = null;
            if (shouldSyncStartup)
            {
                var definition = await ResolveStudyShellAsync(study, root);
                sync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(definition, CreateQuestService(device), study, root, forceWhenStudyNotForeground: false);
            }

            if (json)
            {
                SussexCliSupport.WriteJson(new { deleted = target.Record.Id, startup_sync = sync });
            }
            else
            {
                Console.WriteLine($"Deleted controller-breathing profile: {target.Record.Id}");
                if (sync is not null)
                {
                    Console.WriteLine(sync.Summary);
                    Console.WriteLine(sync.Detail);
                }
            }
        });

        var importArg = new Argument<string>("path", "Path to a Sussex controller-breathing profile JSON file.");
        var import = new Command("import", "Import a Sussex controller-breathing profile JSON file") { importArg };
        import.Handler = CommandHandler.Create(async (string path, string study, bool json) =>
        {
            var store = SussexCliSupport.CreateControllerStore(SussexCliSupport.CreateControllerCompiler());
            var imported = await store.ImportAsync(path);
            if (json)
            {
                SussexCliSupport.WriteJson(new { imported = SussexCliSupport.BuildControllerProfileView(new SussexCliSupport.ControllerResolvedProfile(imported, false), study) });
            }
            else
            {
                Console.WriteLine($"Imported controller-breathing profile: {imported.Document.Profile.Name} ({imported.Id})");
                Console.WriteLine(imported.FilePath);
            }
        });

        var exportArg = new Argument<string>("path", "Destination JSON path.");
        var export = new Command("export", "Export one Sussex controller-breathing profile as JSON") { profileArg, exportArg };
        export.Handler = CommandHandler.Create(async (string profile, string path, string study, string? root, bool json) =>
        {
            var store = SussexCliSupport.CreateControllerStore(SussexCliSupport.CreateControllerCompiler());
            var profiles = await SussexCliSupport.LoadControllerProfilesAsync(study, root);
            var target = SussexCliSupport.ResolveControllerProfile(profiles, profile, allowBaselineTemplate: true);
            await store.ExportAsync(target.Record.Document, path);
            if (json)
            {
                SussexCliSupport.WriteJson(new { exported = target.Record.Id, path = Path.GetFullPath(path) });
            }
            else
            {
                Console.WriteLine($"Exported controller-breathing profile {target.Record.Id} -> {Path.GetFullPath(path)}");
            }
        });

        var setStartup = new Command("set-startup", "Set the next-launch Sussex controller-breathing profile") { profileArg };
        setStartup.Handler = CommandHandler.Create(async (string profile, string study, string? root, bool json, string? device) =>
        {
            var profiles = await SussexCliSupport.LoadControllerProfilesAsync(study, root);
            var target = SussexCliSupport.ResolveControllerProfile(profiles, profile, allowBaselineTemplate: true);
            SussexCliSupport.SaveControllerStartupState(study, target.IsBaselineTemplate ? null : target.Record);
            var definition = await ResolveStudyShellAsync(study, root);
            var sync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(definition, CreateQuestService(device), study, root, forceWhenStudyNotForeground: false);
            if (json)
            {
                SussexCliSupport.WriteJson(new { startup = target.IsBaselineTemplate ? "bundled-baseline" : target.Record.Id, sync });
            }
            else
            {
                Console.WriteLine(target.IsBaselineTemplate
                    ? "Controller-breathing startup profile reset to bundled baseline."
                    : $"Controller-breathing startup profile set to {target.Record.Document.Profile.Name} ({target.Record.Id}).");
                Console.WriteLine(sync.Summary);
                Console.WriteLine(sync.Detail);
            }
        });

        var clearStartup = new Command("clear-startup", "Reset the next-launch Sussex controller-breathing profile to the bundled baseline");
        clearStartup.Handler = CommandHandler.Create(async (string study, string? root, bool json, string? device) =>
        {
            SussexCliSupport.SaveControllerStartupState(study, null);
            var definition = await ResolveStudyShellAsync(study, root);
            var sync = await SussexCliSupport.SyncPinnedStartupProfilesAsync(definition, CreateQuestService(device), study, root, forceWhenStudyNotForeground: false);
            if (json)
            {
                SussexCliSupport.WriteJson(new { startup = "bundled-baseline", sync });
            }
            else
            {
                Console.WriteLine("Controller-breathing startup profile reset to bundled baseline.");
                Console.WriteLine(sync.Summary);
                Console.WriteLine(sync.Detail);
            }
        });

        var applyLive = new Command("apply-live", "Apply one Sussex controller-breathing profile to the current running Sussex session") { profileArg };
        applyLive.Handler = CommandHandler.Create(async (string profile, string study, string? root, bool json, string? device) =>
        {
            var definition = await ResolveStudyShellAsync(study, root);
            var profiles = await SussexCliSupport.LoadControllerProfilesAsync(study, root);
            var target = SussexCliSupport.ResolveControllerProfile(profiles, profile, allowBaselineTemplate: true);
            var result = await SussexCliSupport.ApplyControllerLiveAsync(definition, CreateQuestService(device), TwinModeBridgeFactory.CreateDefault(), study, target.Record);
            if (json)
            {
                SussexCliSupport.WriteJson(new { outcome = result.Outcome, csv_path = result.CsvPath });
            }
            else
            {
                PrintOutcome(result.Outcome);
            }
        });

        controller.AddCommand(list);
        controller.AddCommand(fields);
        controller.AddCommand(show);
        controller.AddCommand(create);
        controller.AddCommand(update);
        controller.AddCommand(delete);
        controller.AddCommand(import);
        controller.AddCommand(export);
        controller.AddCommand(setStartup);
        controller.AddCommand(clearStartup);
        controller.AddCommand(applyLive);
        return controller;
    }

    private static Command BuildUtilityCommand()
    {
        var actionArg = new Argument<string>("action", description: "Utility action: home, back, wake, list, reboot");
        var command = new Command("utility", "Run a Quest utility action") { actionArg };
        command.Handler = CommandHandler.Create(async (string action, string? device) =>
        {
            var parsed = action.ToLowerInvariant() switch
            {
                "home" => QuestUtilityAction.Home,
                "back" => QuestUtilityAction.Back,
                "wake" => QuestUtilityAction.Wake,
                "list" => QuestUtilityAction.ListInstalledPackages,
                "reboot" => QuestUtilityAction.Reboot,
                _ => (QuestUtilityAction?)null
            };

            if (parsed is null)
            {
                Console.Error.WriteLine($"Unknown utility action: {action}. Use: home, back, wake, list, reboot");
                return;
            }

            var service = CreateQuestService(device);
            var result = await service.RunUtilityAsync(parsed.Value);
            PrintOutcome(result);
        });
        return command;
    }

    private static Command BuildHzdbCommand()
    {
        var hzdbCommand = new Command("hzdb", "Meta Horizon Debug Bridge (hzdb) operations");

        var screenshotCommand = new Command("screenshot", "Capture a screenshot from the Quest");
        var outputOption = new Option<string?>("--output", "Output file path (default: session directory)");
        screenshotCommand.Add(outputOption);
        screenshotCommand.Handler = CommandHandler.Create(async (string? output, string? device) =>
        {
            var hzdb = HzdbServiceFactory.CreateDefault();
            var serial = ResolveDeviceSerial(device);
            var outputPath = output ?? Path.Combine(
                CompanionOperatorDataLayout.ScreenshotsRootPath,
                $"screenshot_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}");
            var result = await hzdb.CaptureScreenshotAsync(serial, outputPath);
            PrintOutcome(result);
        });

        var perfTraceCommand = new Command("perf", "Capture a Perfetto performance trace");
        var durationOption = new Option<int>("--duration", () => 5000, "Trace duration in milliseconds");
        perfTraceCommand.Add(durationOption);
        perfTraceCommand.Handler = CommandHandler.Create(async (int duration, string? device) =>
        {
            var hzdb = HzdbServiceFactory.CreateDefault();
            var serial = ResolveDeviceSerial(device);
            var result = await hzdb.CapturePerfTraceAsync(serial, duration);
            PrintOutcome(result);
        });

        var proximityCommand = new Command("proximity", "Enable or disable proximity sensor");
        var enableArg = new Argument<string>("state", description: "enable or disable");
        proximityCommand.Add(enableArg);
        proximityCommand.Handler = CommandHandler.Create(async (string state, string? device) =>
        {
            var hzdb = HzdbServiceFactory.CreateDefault();
            var serial = ResolveDeviceSerial(device);
            var enabled = string.Equals(state, "enable", StringComparison.OrdinalIgnoreCase);
            var result = await hzdb.SetProximityAsync(serial, enabled);
            PrintOutcome(result);
        });

        var wakeCommand = new Command("wake", "Wake the Quest device");
        wakeCommand.Handler = CommandHandler.Create(async (string? device) =>
        {
            var hzdb = HzdbServiceFactory.CreateDefault();
            var serial = ResolveDeviceSerial(device);
            var result = await hzdb.WakeDeviceAsync(serial);
            PrintOutcome(result);
        });

        var infoCommand = new Command("info", "Get detailed device info via hzdb");
        infoCommand.Handler = CommandHandler.Create(async (string? device) =>
        {
            var hzdb = HzdbServiceFactory.CreateDefault();
            var serial = ResolveDeviceSerial(device);
            var result = await hzdb.GetDeviceInfoAsync(serial);
            PrintOutcome(result);
        });

        var lsCommand = new Command("ls", "List files on device");
        var pathArg = new Argument<string>("path", description: "Remote path to list");
        lsCommand.Add(pathArg);
        lsCommand.Handler = CommandHandler.Create(async (string path, string? device) =>
        {
            var hzdb = HzdbServiceFactory.CreateDefault();
            var serial = ResolveDeviceSerial(device);
            var result = await hzdb.ListFilesAsync(serial, path);
            PrintOutcome(result);
        });

        var pullCommand = new Command("pull", "Pull one file from device storage");
        var remotePathArg = new Argument<string>("remote-path", description: "Remote file path to pull");
        var localPathArg = new Argument<string>("local-path", description: "Local destination path");
        pullCommand.Add(remotePathArg);
        pullCommand.Add(localPathArg);
        pullCommand.Handler = CommandHandler.Create(async (string remotePath, string localPath, string? device) =>
        {
            var hzdb = HzdbServiceFactory.CreateDefault();
            var serial = ResolveDeviceSerial(device);
            var result = await hzdb.PullFileAsync(serial, remotePath, localPath);
            PrintOutcome(result);
        });

        hzdbCommand.AddCommand(screenshotCommand);
        hzdbCommand.AddCommand(perfTraceCommand);
        hzdbCommand.AddCommand(proximityCommand);
        hzdbCommand.AddCommand(wakeCommand);
        hzdbCommand.AddCommand(infoCommand);
        hzdbCommand.AddCommand(lsCommand);
        hzdbCommand.AddCommand(pullCommand);
        return hzdbCommand;
    }

    private static Command BuildToolingCommand()
    {
        var toolingCommand = new Command("tooling", "Manage the official Quest developer tooling used by the companion");
        var jsonOption = new Option<bool>("--json", "Write machine-readable JSON output.");

        var statusCommand = new Command("status", "Show the managed official Quest tooling state for hzdb plus Android platform-tools");
        var checkUpstreamOption = new Option<bool>("--check-upstream", "Check the latest published upstream versions as well as the local managed installs.");
        statusCommand.Add(jsonOption);
        statusCommand.Add(checkUpstreamOption);
        statusCommand.Handler = CommandHandler.Create(async (bool json, bool checkUpstream) =>
        {
            using var tooling = new OfficialQuestToolingService();
            var status = checkUpstream
                ? await tooling.GetStatusAsync().ConfigureAwait(false)
                : tooling.GetLocalStatus();

            if (json)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(status, SussexCliSupport.JsonOptions));
                return;
            }

            PrintToolingStatus(status, includeUpstream: checkUpstream);
        });

        var installCommand = new Command("install-official", "Install or update Meta hzdb plus Android platform-tools into the managed LocalAppData tool cache");
        installCommand.Add(jsonOption);
        installCommand.Handler = CommandHandler.Create(async (bool json) =>
        {
            using var tooling = new OfficialQuestToolingService();
            var result = await tooling.InstallOrUpdateAsync().ConfigureAwait(false);

            if (json)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, SussexCliSupport.JsonOptions));
                return;
            }

            Console.WriteLine(result.Summary);
            Console.WriteLine(result.Detail);
            Console.WriteLine();
            PrintToolingStatus(result.Status, includeUpstream: true);
        });

        toolingCommand.AddCommand(statusCommand);
        toolingCommand.AddCommand(installCommand);
        return toolingCommand;
    }

    private static Command BuildWindowsEnvironmentCommand()
    {
        var windowsEnvCommand = new Command("windows-env", "CLI mirror of Analyze Windows Environment for liblsl, twin transport, and expected upstream stream checks");
        var jsonOption = new Option<bool>("--json", "Write machine-readable JSON output.");
        var streamOption = new Option<string>("--expected-stream", () => HrvBiofeedbackStreamContract.StreamName, "Expected upstream LSL stream name to probe.");
        var typeOption = new Option<string>("--expected-type", () => HrvBiofeedbackStreamContract.StreamType, "Expected upstream LSL stream type to probe.");
        var skipStreamProbeOption = new Option<bool>("--skip-stream-probe", "Skip probing the expected upstream LSL stream.");

        var analyzeCommand = new Command("analyze", "Mirror the GUI Analyze Windows Environment check for Windows tooling, liblsl runtimes, the twin bridge, and expected upstream stream visibility")
        {
            jsonOption,
            streamOption,
            typeOption,
            skipStreamProbeOption
        };

        analyzeCommand.Handler = CommandHandler.Create(async (bool json, string expectedStream, string expectedType, bool skipStreamProbe) =>
        {
            using var clockAlignment = StudyClockAlignmentServiceFactory.CreateDefault();
            using var testSender = TestLslSignalServiceFactory.CreateDefault();
            var streamDiscovery = LslStreamDiscoveryServiceFactory.CreateDefault();
            var bridge = TwinModeBridgeFactory.CreateDefault();
            try
            {
                var service = new WindowsEnvironmentAnalysisService(
                    CreateMonitorService(),
                    streamDiscovery,
                    clockAlignment,
                    testSender,
                    bridge);
                var result = await service.AnalyzeAsync(
                        new WindowsEnvironmentAnalysisRequest(
                            expectedStream,
                            expectedType,
                            ProbeExpectedLslStream: !skipStreamProbe))
                    .ConfigureAwait(false);

                if (json)
                {
                    SussexCliSupport.WriteJson(result);
                    return;
                }

                DiagnosticsCliSupport.PrintWindowsEnvironmentAnalysis(result);
            }
            finally
            {
                (bridge as IDisposable)?.Dispose();
            }
        });

        windowsEnvCommand.AddCommand(analyzeCommand);
        return windowsEnvCommand;
    }

    private static async Task<OperationOutcome> GenerateSussexDiagnosticsPdfAsync(
        string inputJsonPath,
        string outputPdfPath)
    {
        var scriptPath = TryResolveSussexDiagnosticsPdfScriptPath();
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Diagnostics PDF generator was not found.",
                "JSON and LaTeX reports were written, but the bundled PDF script could not be resolved.");
        }

        var attempts = new[]
        {
            ("py", new[] { "-3", scriptPath }),
            ("python", new[] { scriptPath })
        };

        foreach (var (exe, prefixArgs) in attempts)
        {
            try
            {
                var startInfo = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                foreach (var arg in prefixArgs)
                {
                    startInfo.ArgumentList.Add(arg);
                }

                startInfo.ArgumentList.Add("--input-json");
                startInfo.ArgumentList.Add(inputJsonPath);
                startInfo.ArgumentList.Add("--output-pdf");
                startInfo.ArgumentList.Add(outputPdfPath);

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    continue;
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var completed = await Task.Run(() => process.WaitForExit(45000)).ConfigureAwait(false);
                if (!completed)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    return new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Diagnostics PDF generation timed out.",
                        $"The LaTeX source and JSON report are still available. Timed out running {exe}.");
                }

                var stdout = await outputTask.ConfigureAwait(false);
                var stderr = await errorTask.ConfigureAwait(false);
                if (process.ExitCode == 0 && File.Exists(outputPdfPath))
                {
                    return new OperationOutcome(
                        OperationOutcomeKind.Success,
                        "Diagnostics PDF generated.",
                        string.IsNullOrWhiteSpace(stdout) ? outputPdfPath : stdout.Trim(),
                        Items: [outputPdfPath]);
                }

                if (exe == "python")
                {
                    return new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Diagnostics PDF generation failed.",
                        string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text))).Trim());
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                if (exe == "python")
                {
                    return new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Python was not available for diagnostics PDF generation.",
                        "JSON and LaTeX reports were written. Install Python with matplotlib to generate the PDF locally, or share the JSON/TEX artifacts.");
                }
            }
        }

        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            "Diagnostics PDF was not generated.",
            "JSON and LaTeX reports were written, but no Python runtime completed the bundled PDF script.");
    }

    private static string? TryResolveSussexDiagnosticsPdfScriptPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "reports", "generate_sussex_diagnostics_pdf.py"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tools", "reports", "generate_sussex_diagnostics_pdf.py")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "reports", "generate_sussex_diagnostics_pdf.py")),
            Path.Combine(Directory.GetCurrentDirectory(), "tools", "reports", "generate_sussex_diagnostics_pdf.py"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "ViscerealityCompanion",
                "tools",
                "reports",
                "generate_sussex_diagnostics_pdf.py")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ResolveDeviceSerial(string? device)
    {
        if (!string.IsNullOrWhiteSpace(device))
            return device;
        var state = CliSessionState.Load();
        return state.LastUsbSerial ?? state.ActiveEndpoint
            ?? throw new InvalidOperationException("No device available. Use --device or run probe/connect first.");
    }

    private static bool MatchesHash(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static async Task<StudyShellCatalog> LoadStudyShellCatalogAsync(string? root)
    {
        var loader = new StudyShellCatalogLoader();
        var catalogRoot = root ?? ResolveStudyShellRoot();
        return await loader.LoadAsync(catalogRoot);
    }

    private static async Task<StudyShellDefinition> ResolveStudyShellAsync(string studyId, string? root)
    {
        var catalog = await LoadStudyShellCatalogAsync(root);
        return catalog.Studies.FirstOrDefault(study =>
                   string.Equals(study.Id, studyId, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Study shell '{studyId}' was not found in {catalog.Source.RootPath}.");
    }

    private static void PrintOutcome(OperationOutcome outcome)
    {
        var prefix = outcome.Kind switch
        {
            OperationOutcomeKind.Success => "OK",
            OperationOutcomeKind.Warning => "WARN",
            OperationOutcomeKind.Failure => "FAIL",
            OperationOutcomeKind.Preview => "PREVIEW",
            _ => "INFO"
        };

        Console.WriteLine($"[{prefix}] {outcome.Summary}");
        if (!string.IsNullOrWhiteSpace(outcome.Detail))
        {
            Console.WriteLine($"       {outcome.Detail}");
        }

        if (outcome.SafeItems.Count > 0)
        {
            foreach (var item in outcome.SafeItems)
            {
                Console.WriteLine($"       - {item}");
            }
        }
    }

    private static void PrintToolingStatus(OfficialQuestToolingStatus status, bool includeUpstream)
    {
        Console.WriteLine($"Managed tooling root: {OfficialQuestToolingLayout.RootPath}");
        Console.WriteLine($"Ready: {status.IsReady}");
        Console.WriteLine();

        PrintToolingComponent(status.Hzdb, includeUpstream);
        Console.WriteLine();
        PrintToolingComponent(status.PlatformTools, includeUpstream);
        Console.WriteLine();
        Console.WriteLine("LSL note: liblsl is bundled with packaged installs and exported agent workspaces, but it is not part of the managed official Quest tool cache.");
        Console.WriteLine("Use `viscereality windows-env analyze` to inspect the active liblsl runtime path and expected stream visibility.");
    }

    private static void PrintToolingComponent(OfficialQuestToolStatus component, bool includeUpstream)
    {
        Console.WriteLine(component.DisplayName);
        Console.WriteLine($"  Installed:         {component.IsInstalled}");
        Console.WriteLine($"  Installed version: {component.InstalledVersion ?? "n/a"}");
        if (includeUpstream)
        {
            Console.WriteLine($"  Available version: {component.AvailableVersion ?? "n/a"}");
            Console.WriteLine($"  Update available:  {component.UpdateAvailable}");
        }

        Console.WriteLine($"  Path:              {component.InstallPath}");
        Console.WriteLine($"  Source:            {component.SourceUri}");
        Console.WriteLine($"  License:           {component.LicenseSummary}");
        Console.WriteLine($"  License URL:       {component.LicenseUri}");
    }

    private static string ResolveCatalogRoot()
    {
        return CliAssetLocator.TryResolveQuestSessionKitRoot()
            ?? throw new InvalidOperationException(
                "No Quest Session Kit catalog root found. Set VISCEREALITY_QUEST_SESSION_KIT_ROOT or use --root.");
    }

    private static string ResolveStudyShellRoot()
    {
        return CliAssetLocator.TryResolveStudyShellRoot()
            ?? throw new InvalidOperationException(
                "No study shell catalog root found. Set VISCEREALITY_STUDY_SHELL_ROOT or use --root.");
    }
}
