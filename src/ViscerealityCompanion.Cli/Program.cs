using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
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
        rootCommand.AddCommand(BuildPerfCommand());
        rootCommand.AddCommand(BuildHotloadCommand());
        rootCommand.AddCommand(BuildMonitorCommand());
        rootCommand.AddCommand(BuildTwinCommand());
        rootCommand.AddCommand(BuildCatalogCommand());
        rootCommand.AddCommand(BuildHzdbCommand());
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
        var apkArg = new Argument<string>("apk", description: "Path to APK file or app ID from catalog");
        var command = new Command("install", "Install an APK on the connected Quest") { apkArg };
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
        var packageArg = new Argument<string>("package", description: "Package ID to launch");
        var command = new Command("launch", "Launch an app on the connected Quest") { packageArg };
        command.Handler = CommandHandler.Create(async (string package, string? device) =>
        {
            var service = CreateQuestService(device);
            var target = new QuestAppTarget(
                Id: package,
                Label: package,
                PackageId: package,
                ApkFile: string.Empty,
                LaunchComponent: string.Empty,
                BrowserPackageId: string.Empty,
                Description: $"CLI launch: {package}",
                Tags: []);
            var result = await service.LaunchAppAsync(target);
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
        var sendCommand = new Command("send", "Send a twin command to the Quest") { actionArg };
        sendCommand.Handler = CommandHandler.Create(async (string action) =>
        {
            var bridge = TwinModeBridgeFactory.CreateDefault();
            var twinCmd = new TwinModeCommand(action, action);
            var result = await bridge.SendCommandAsync(twinCmd);
            PrintOutcome(result);
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
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ViscerealityCompanion", "screenshots",
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

        hzdbCommand.AddCommand(screenshotCommand);
        hzdbCommand.AddCommand(perfTraceCommand);
        hzdbCommand.AddCommand(proximityCommand);
        hzdbCommand.AddCommand(wakeCommand);
        hzdbCommand.AddCommand(infoCommand);
        hzdbCommand.AddCommand(lsCommand);
        return hzdbCommand;
    }

    private static string ResolveDeviceSerial(string? device)
    {
        if (!string.IsNullOrWhiteSpace(device))
            return device;
        var state = CliSessionState.Load();
        return state.LastUsbSerial ?? state.ActiveEndpoint
            ?? throw new InvalidOperationException("No device available. Use --device or run probe/connect first.");
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

    private static string ResolveCatalogRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("VISCEREALITY_QUEST_SESSION_KIT_ROOT"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source", "repos", "AstralKarateDojo", "QuestSessionKit"),
            Path.Combine(AppContext.BaseDirectory, "samples", "quest-session-kit"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "quest-session-kit"))
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path!))
            ?? throw new InvalidOperationException(
                "No Quest Session Kit catalog root found. Set VISCEREALITY_QUEST_SESSION_KIT_ROOT or use --root.");
    }
}
