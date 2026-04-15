using System.Globalization;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Cli;

internal static class DiagnosticsCliSupport
{
    private static readonly TimeSpan TwinStateIdleThreshold = TimeSpan.FromSeconds(5);

    internal sealed record StudyConnectionProbeResult(
        OperationOutcomeKind Level,
        string Summary,
        string Detail,
        bool InletReady,
        bool ReturnPathReady,
        bool PinnedBuildReady,
        bool DeviceProfileReady,
        string Selector,
        string ForegroundAndSnapshot,
        string PinnedBuild,
        string DeviceProfile,
        string ExpectedInlet,
        string RuntimeTarget,
        string ConnectedInlet,
        string Counts,
        string QuestStatus,
        string QuestEcho,
        string ReturnPath,
        string TwinStatePublisher,
        string CommandChannel,
        string HotloadChannel,
        string TransportDetail,
        DateTimeOffset CheckedAtUtc);

    internal static void PrintWindowsEnvironmentAnalysis(WindowsEnvironmentAnalysisResult result)
    {
        Console.WriteLine($"[{RenderLevel(result.Level)}] {result.Summary}");
        Console.WriteLine(result.Detail);
        Console.WriteLine($"Completed: {result.CompletedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
        Console.WriteLine();

        foreach (var check in result.Checks)
        {
            Console.WriteLine($"[{RenderLevel(check.Level)}] {check.Label}: {check.Summary}");
            if (!string.IsNullOrWhiteSpace(check.Detail))
            {
                Console.WriteLine($"      {check.Detail}");
            }
        }
    }

    internal static async Task<StudyConnectionProbeResult> ProbeStudyConnectionAsync(
        StudyShellDefinition definition,
        IQuestControlService questService,
        string? device,
        TimeSpan waitDuration,
        CancellationToken cancellationToken = default)
    {
        var target = StudyShellOperatorBindings.CreateQuestTarget(definition);
        var profile = StudyShellOperatorBindings.CreateDeviceProfile(definition);
        var headset = await questService
            .QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var installed = await questService
            .QueryInstalledAppAsync(target, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var profileStatus = await questService
            .QueryDeviceProfileStatusAsync(profile, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var checkedAtUtc = DateTimeOffset.UtcNow;
        var bridge = TwinModeBridgeFactory.CreateDefault();
        try
        {
            if (bridge is not LslTwinModeBridge lslBridge)
            {
                var previewTwinStatePublisher = QuestTwinStatePublisherInventoryService.Inspect(
                    LslStreamDiscoveryServiceFactory.CreateDefault(),
                    definition.App.PackageId);
                return new StudyConnectionProbeResult(
                    OperationOutcomeKind.Preview,
                    "Twin bridge transport is unavailable in this environment.",
                    bridge.Status.Detail,
                    InletReady: false,
                    ReturnPathReady: false,
                    PinnedBuildReady: IsPinnedBuildReady(definition, installed),
                    DeviceProfileReady: profileStatus.IsActive,
                    Selector: BuildSelectorLabel(device, headset),
                    ForegroundAndSnapshot: BuildForegroundAndSnapshotLabel(headset),
                    PinnedBuild: BuildPinnedBuildLabel(definition, installed),
                    DeviceProfile: BuildDeviceProfileLabel(profileStatus),
                    ExpectedInlet: $"{definition.Monitoring.ExpectedLslStreamName} / {definition.Monitoring.ExpectedLslStreamType}",
                    RuntimeTarget: "Runtime target n/a",
                    ConnectedInlet: "Connected stream n/a",
                    Counts: "Connection counts n/a",
                    QuestStatus: "Twin bridge unavailable.",
                    QuestEcho: "No inlet value reported yet.",
                    ReturnPath: "No quest_twin_state / quest.twin.state frame has reached Windows yet.",
                    TwinStatePublisher: QuestTwinStatePublisherInventoryService.RenderForOperator(previewTwinStatePublisher),
                    CommandChannel: "quest_twin_commands / quest.twin.command",
                    HotloadChannel: "quest_hotload_config / quest.config",
                    TransportDetail: bridge.Status.Detail,
                    CheckedAtUtc: checkedAtUtc);
            }

            lslBridge.ConfigureExpectedQuestStateSource(definition.App.PackageId);
            var openOutcome = lslBridge.Open();
            await WaitForTwinStateAsync(lslBridge, waitDuration, cancellationToken).ConfigureAwait(false);
            var twinStatePublisher = QuestTwinStatePublisherInventoryService.Inspect(
                LslStreamDiscoveryServiceFactory.CreateDefault(),
                definition.App.PackageId);

            var state = BuildStudyConnectionProbeState(definition, headset, installed, profileStatus, device, lslBridge, twinStatePublisher, checkedAtUtc);
            return openOutcome.Kind == OperationOutcomeKind.Failure
                ? state with
                {
                    Level = OperationOutcomeKind.Failure,
                    Summary = "Twin bridge could not open its local channels.",
                    Detail = $"{openOutcome.Detail}{Environment.NewLine}{Environment.NewLine}{state.Detail}".Trim()
                }
                : state;
        }
        finally
        {
            (bridge as IDisposable)?.Dispose();
        }
    }

    internal static void PrintStudyConnectionProbe(StudyConnectionProbeResult result)
    {
        Console.WriteLine($"[{RenderLevel(result.Level)}] {result.Summary}");
        Console.WriteLine(result.Detail);
        Console.WriteLine();
        Console.WriteLine($"Selector:               {result.Selector}");
        Console.WriteLine($"Foreground + snapshot:  {result.ForegroundAndSnapshot}");
        Console.WriteLine($"Pinned build:           {result.PinnedBuild}");
        Console.WriteLine($"Device profile:         {result.DeviceProfile}");
        Console.WriteLine($"Expected inlet:         {result.ExpectedInlet}");
        Console.WriteLine($"Runtime target:         {result.RuntimeTarget}");
        Console.WriteLine($"Connected inlet:        {result.ConnectedInlet}");
        Console.WriteLine($"Counts:                 {result.Counts}");
        Console.WriteLine($"Quest status:           {result.QuestStatus}");
        Console.WriteLine($"Quest echo:             {result.QuestEcho}");
        Console.WriteLine($"Return path:            {result.ReturnPath}");
        Console.WriteLine($"Twin-state outlet:      {result.TwinStatePublisher}");
        Console.WriteLine($"Command channel:        {result.CommandChannel}");
        Console.WriteLine($"Hotload channel:        {result.HotloadChannel}");
        Console.WriteLine($"Transport detail:       {result.TransportDetail}");
        Console.WriteLine($"Checked at:             {result.CheckedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
    }

    private static StudyConnectionProbeResult BuildStudyConnectionProbeState(
        StudyShellDefinition definition,
        HeadsetAppStatus headset,
        InstalledAppStatus installed,
        DeviceProfileStatus profileStatus,
        string? device,
        LslTwinModeBridge bridge,
        QuestTwinStatePublisherInventory twinStatePublisher,
        DateTimeOffset checkedAtUtc)
    {
        var expectedName = definition.Monitoring.ExpectedLslStreamName;
        var expectedType = definition.Monitoring.ExpectedLslStreamType;
        var settings = bridge.ReportedSettings;

        string? GetFirstValue(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (settings.TryGetValue(key, out var value) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        string? GetFirstValueFromCollection(IEnumerable<string> keys)
            => GetFirstValue(keys.ToArray());

        var streamName = GetFirstValue("study.lsl.filter_name") ?? GetFirstValueFromCollection(definition.Monitoring.LslStreamNameKeys);
        var streamType = GetFirstValue("study.lsl.filter_type") ?? GetFirstValueFromCollection(definition.Monitoring.LslStreamTypeKeys);
        var connectedName = GetFirstValue("study.lsl.connected_name");
        var connectedType = GetFirstValue("study.lsl.connected_type");
        var statusLine = GetFirstValue("study.lsl.status");
        var connectedFlag = ParseBool(GetFirstValue("study.lsl.connected"));
        var connectedCount = ParseInt(GetFirstValue("connection.lsl.connected_count"));
        var connectingCount = ParseInt(GetFirstValue("connection.lsl.connecting_count"));
        var totalCount = ParseInt(GetFirstValue("connection.lsl.total_count"));
        var hasInputValue = TryGetObservedLslValue(settings, definition, out var inputValue, out var inputValueKey);
        var hasConnectedInput = connectedFlag == true
            || connectedCount.GetValueOrDefault() > 0
            || !string.IsNullOrWhiteSpace(connectedName);

        var runtimeTarget = $"{(string.IsNullOrWhiteSpace(streamName) ? "n/a" : streamName)} / {(string.IsNullOrWhiteSpace(streamType) ? "n/a" : streamType)}";
        var connectedInlet = $"{(string.IsNullOrWhiteSpace(connectedName) ? "n/a" : connectedName)} / {(string.IsNullOrWhiteSpace(connectedType) ? "n/a" : connectedType)}";
        var counts = $"Connected {connectedCount?.ToString() ?? (connectedFlag.HasValue ? (connectedFlag.Value ? "1" : "0") : "n/a")}, connecting {connectingCount?.ToString() ?? "n/a"}, total {totalCount?.ToString() ?? "n/a"}";
        var questStatus = string.IsNullOrWhiteSpace(statusLine)
            ? "Runtime did not publish an extra LSL status line."
            : statusLine;

        var lastStateReceivedAt = bridge.LastStateReceivedAt;
        var hasTwinState = settings.Count > 0 || lastStateReceivedAt.HasValue;
        var returnPathReady = hasTwinState
            && (!lastStateReceivedAt.HasValue || checkedAtUtc - lastStateReceivedAt.Value <= TwinStateIdleThreshold);
        var returnPath = !hasTwinState
            ? "No quest_twin_state / quest.twin.state frame has reached Windows yet."
            : returnPathReady
                ? lastStateReceivedAt.HasValue
                    ? $"Latest quest_twin_state / quest.twin.state frame {lastStateReceivedAt.Value.ToLocalTime():HH:mm:ss}."
                    : "quest_twin_state / quest.twin.state is active."
                : $"Latest quest_twin_state / quest.twin.state frame {lastStateReceivedAt!.Value.ToLocalTime():HH:mm:ss}; the return path is stale.";

        var questEcho = hasInputValue
            ? lastStateReceivedAt.HasValue
                ? $"Current Quest echo {inputValue:0.000} via {inputValueKey} in frame {lastStateReceivedAt.Value.ToLocalTime():HH:mm:ss}."
                : $"Current Quest echo {inputValue:0.000} via {inputValueKey}."
            : hasConnectedInput
                ? "Connected, but this public build does not echo the routed inlet value yet."
                : "No inlet value reported yet.";

        var streamMatches = (string.IsNullOrWhiteSpace(expectedName) || string.Equals(streamName, expectedName, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(expectedType) || string.Equals(streamType, expectedType, StringComparison.OrdinalIgnoreCase));
        var inletReady = hasConnectedInput && streamMatches;
        var headsetForeground = string.Equals(headset.ForegroundPackageId, definition.App.PackageId, StringComparison.OrdinalIgnoreCase);
        var pinnedBuildReady = IsPinnedBuildReady(definition, installed);
        var deviceProfileReady = profileStatus.IsActive;

        var connectionSummary = inletReady && returnPathReady
            ? "Quest inlet is connected and the Windows return path is live."
            : inletReady
                ? "Quest inlet is connected, but Windows is not receiving a fresh return path yet."
                : returnPathReady
                    ? "Windows is receiving quest_twin_state, but Sussex has not confirmed an LSL inlet yet."
                    : headsetForeground
                    ? "Sussex is in front, but neither the LSL inlet nor the Windows return path is confirmed."
                    : "Quest is reachable, but neither the LSL inlet nor the Windows return path is confirmed.";

        var connectionLevel = inletReady && returnPathReady
            ? OperationOutcomeKind.Success
            : inletReady || returnPathReady
                ? OperationOutcomeKind.Warning
                : headsetForeground
                    ? OperationOutcomeKind.Failure
                    : OperationOutcomeKind.Warning;

        var summary = !pinnedBuildReady
            ? "Pinned Sussex APK is not installed or does not match the study shell baseline."
            : !deviceProfileReady && connectionLevel == OperationOutcomeKind.Success
                ? "Quest path is live, but the required Sussex device profile still needs attention."
                : connectionSummary;
        var level = !pinnedBuildReady
            ? OperationOutcomeKind.Failure
            : !deviceProfileReady && connectionLevel == OperationOutcomeKind.Success
                ? OperationOutcomeKind.Warning
                : connectionLevel;

        var detailParts = new List<string>();
        if (!pinnedBuildReady)
        {
            detailParts.Add($"Install the pinned Sussex APK before debugging LSL; expected {ShortHash(definition.App.Sha256)}, found {ShortHash(installed.InstalledSha256)}.");
        }

        if (!deviceProfileReady)
        {
            detailParts.Add("Apply the Sussex study device profile before participant validation so Wi-Fi ADB, profile guards, and startup assumptions match the release baseline.");
        }

        if (!inletReady && returnPathReady)
        {
            detailParts.Add("The headset is publishing twin state back to Windows, but it has not reported an active Sussex inlet connection yet.");
        }
        else if (inletReady && !returnPathReady)
        {
            detailParts.Add("Sussex reported an inlet connection, but the quest_twin_state return path is missing or stale on Windows.");
        }
        else if (!inletReady && !returnPathReady)
        {
            detailParts.Add("Windows-side transport can be healthy while this still fails; this state usually means the headset runtime has not published Sussex LSL or twin-state telemetry yet.");
        }

        if (!returnPathReady)
        {
            detailParts.Add(QuestTwinStatePublisherInventoryService.RenderForOperator(twinStatePublisher));
        }

        if (headsetForeground)
        {
            detailParts.Add("The Sussex package is foregrounded per ADB, so this is no longer just a launch-selection problem.");
        }
        else
        {
            detailParts.Add("The pinned Sussex package is not currently confirmed in the foreground. Re-check the visible headset scene before blaming LSL.");
        }

        detailParts.Add("If the expected upstream LSL stream is already visible on Windows, focus next on headset-side scene state, Wi-Fi/client-isolation issues, or whether the Quest build is actually publishing quest_twin_state.");

        return new StudyConnectionProbeResult(
            level,
            summary,
            string.Join(" ", detailParts),
            inletReady,
            returnPathReady,
            pinnedBuildReady,
            deviceProfileReady,
            Selector: BuildSelectorLabel(device, headset),
            ForegroundAndSnapshot: BuildForegroundAndSnapshotLabel(headset),
            PinnedBuild: BuildPinnedBuildLabel(definition, installed),
            DeviceProfile: BuildDeviceProfileLabel(profileStatus),
            ExpectedInlet: $"{expectedName} / {expectedType}",
            RuntimeTarget: runtimeTarget,
            ConnectedInlet: connectedInlet,
            Counts: counts,
            QuestStatus: questStatus,
            QuestEcho: questEcho,
            ReturnPath: returnPath,
            TwinStatePublisher: QuestTwinStatePublisherInventoryService.RenderForOperator(twinStatePublisher),
            CommandChannel: "quest_twin_commands / quest.twin.command",
            HotloadChannel: "quest_hotload_config / quest.config",
            TransportDetail: bridge.Status.Detail,
            CheckedAtUtc: checkedAtUtc);
    }

    private static string RenderLevel(OperationOutcomeKind level)
        => level switch
        {
            OperationOutcomeKind.Success => "OK",
            OperationOutcomeKind.Warning => "WARN",
            OperationOutcomeKind.Failure => "FAIL",
            OperationOutcomeKind.Preview => "PREVIEW",
            _ => "INFO"
        };

    private static string BuildSelectorLabel(string? device, HeadsetAppStatus headset)
    {
        var state = CliSessionState.Load();
        var selector = !string.IsNullOrWhiteSpace(device)
            ? device
            : state.ActiveEndpoint ?? state.LastUsbSerial ?? headset.ConnectionLabel;

        if (string.IsNullOrWhiteSpace(selector))
        {
            return "Selector n/a.";
        }

        var transport = selector.Contains(":", StringComparison.Ordinal)
            ? "Wi-Fi ADB"
            : "USB ADB";
        return $"Selector {selector} over {transport}.";
    }

    private static string BuildForegroundAndSnapshotLabel(HeadsetAppStatus headset)
    {
        var foreground = string.IsNullOrWhiteSpace(headset.ForegroundPackageId) ? "Foreground n/a" : headset.ForegroundPackageId;
        var parts = new List<string>
        {
            foreground,
            $"snapshot {headset.Timestamp.ToLocalTime():HH:mm:ss}"
        };

        if (!string.IsNullOrWhiteSpace(headset.HeadsetWifiSsid) || !string.IsNullOrWhiteSpace(headset.HeadsetWifiIpAddress))
        {
            parts.Add($"headset Wi-Fi {JoinNetworkLabel(headset.HeadsetWifiSsid, headset.HeadsetWifiIpAddress)}");
        }

        if (!string.IsNullOrWhiteSpace(headset.HostWifiSsid))
        {
            parts.Add($"host Wi-Fi {headset.HostWifiSsid}");
        }

        if (headset.WifiSsidMatchesHost.HasValue)
        {
            parts.Add($"Wi-Fi SSID match {headset.WifiSsidMatchesHost.Value}");
        }

        return string.Join(" | ", parts);
    }

    private static string BuildPinnedBuildLabel(StudyShellDefinition definition, InstalledAppStatus installed)
    {
        var expected = ShortHash(definition.App.Sha256);
        if (!installed.IsInstalled)
        {
            return $"Not installed; pinned {expected}.";
        }

        var actual = ShortHash(installed.InstalledSha256);
        var version = string.IsNullOrWhiteSpace(installed.VersionName)
            ? "version n/a"
            : $"version {installed.VersionName}";
        return $"Installed match {IsPinnedBuildReady(definition, installed)}; pinned {expected}, installed {actual}, {version}.";
    }

    private static string BuildDeviceProfileLabel(DeviceProfileStatus profileStatus)
    {
        var blockingMismatches = profileStatus.Properties.Count(property => property.BlocksActivation && !property.Matches);
        return blockingMismatches > 0
            ? $"{profileStatus.Summary} Active {profileStatus.IsActive}; {blockingMismatches} blocking mismatch(es)."
            : $"{profileStatus.Summary} Active {profileStatus.IsActive}.";
    }

    private static string JoinNetworkLabel(string ssid, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ssid))
        {
            return string.IsNullOrWhiteSpace(ipAddress) ? "n/a" : ipAddress;
        }

        return string.IsNullOrWhiteSpace(ipAddress)
            ? ssid
            : $"{ssid} / {ipAddress}";
    }

    private static bool IsPinnedBuildReady(StudyShellDefinition definition, InstalledAppStatus installed)
        => installed.IsInstalled && MatchesHash(installed.InstalledSha256, definition.App.Sha256);

    private static bool MatchesHash(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string ShortHash(string? hash)
        => string.IsNullOrWhiteSpace(hash)
            ? "n/a"
            : hash.Length <= 12
                ? hash
                : hash[..12];

    private static async Task WaitForTwinStateAsync(LslTwinModeBridge bridge, TimeSpan waitDuration, CancellationToken cancellationToken)
    {
        if (bridge.LastStateReceivedAt.HasValue || waitDuration <= TimeSpan.Zero)
        {
            return;
        }

        var timeoutAtUtc = DateTimeOffset.UtcNow + waitDuration;
        while (!bridge.LastStateReceivedAt.HasValue && DateTimeOffset.UtcNow < timeoutAtUtc)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TryGetObservedLslValue(
        IReadOnlyDictionary<string, string> reportedSettings,
        StudyShellDefinition definition,
        out double value,
        out string sourceKey)
    {
        if (TryGetConfiguredUnitIntervalValue(reportedSettings, ["study.lsl.latest_default_value", "study.lsl.latest_ch0_value"], out value, out sourceKey))
        {
            return true;
        }

        if (TryGetConfiguredUnitIntervalValue(reportedSettings, definition.Monitoring.LslValueKeys, out value, out sourceKey))
        {
            return true;
        }

        if (TryGetSignalMirrorValue(reportedSettings, "coherence_lsl", out value, out sourceKey))
        {
            return true;
        }

        if (TryGetSignalMirrorValue(reportedSettings, "breathing_lsl", out value, out sourceKey))
        {
            return true;
        }

        value = 0d;
        sourceKey = string.Empty;
        return false;
    }

    private static bool TryGetConfiguredUnitIntervalValue(
        IReadOnlyDictionary<string, string> reportedSettings,
        IEnumerable<string> keys,
        out double value,
        out string sourceKey)
    {
        foreach (var key in keys)
        {
            if (reportedSettings.TryGetValue(key, out var rawValue) &&
                ParseUnitInterval(rawValue) is double parsedValue)
            {
                value = parsedValue;
                sourceKey = key;
                return true;
            }
        }

        value = 0d;
        sourceKey = string.Empty;
        return false;
    }

    private static bool TryGetSignalMirrorValue(
        IReadOnlyDictionary<string, string> reportedSettings,
        string signalName,
        out double value,
        out string sourceKey)
    {
        const string NameSuffix = ".name";
        const string ValueSuffix = ".value01";

        foreach (var entry in reportedSettings)
        {
            if (!entry.Key.StartsWith("driver.stream.", StringComparison.OrdinalIgnoreCase) ||
                !entry.Key.EndsWith(NameSuffix, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.Value, signalName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var keyBase = entry.Key[..^NameSuffix.Length];
            var valueKey = keyBase + ValueSuffix;
            if (reportedSettings.TryGetValue(valueKey, out var rawValue) &&
                ParseUnitInterval(rawValue) is double parsedValue)
            {
                value = parsedValue;
                sourceKey = valueKey;
                return true;
            }
        }

        value = 0d;
        sourceKey = string.Empty;
        return false;
    }

    private static bool? ParseBool(string? rawValue)
        => bool.TryParse(rawValue, out var parsed)
            ? parsed
            : null;

    private static int? ParseInt(string? rawValue)
        => int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static double? ParseUnitInterval(string? rawValue)
    {
        if (!double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0d || parsed > 1d)
        {
            return null;
        }

        return parsed;
    }
}
