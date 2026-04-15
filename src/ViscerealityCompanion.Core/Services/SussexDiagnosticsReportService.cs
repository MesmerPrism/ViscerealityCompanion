using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed record SussexDiagnosticsReportRequest(
    StudyShellDefinition Study,
    string? DeviceSelector = null,
    string? OutputDirectory = null,
    TimeSpan? ProbeWaitDuration = null,
    bool RunCommandAcceptanceCheck = true);

public sealed record SussexDiagnosticsReportResult(
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    string ReportDirectory,
    string JsonPath,
    string TexPath,
    string PdfPath,
    DateTimeOffset CompletedAtUtc,
    SussexDiagnosticsReport Report);

public sealed record SussexDiagnosticsReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string OperatorDataRoot,
    string ReportDirectory,
    string StudyId,
    string StudyLabel,
    string PackageId,
    string ExpectedLslStreamName,
    string ExpectedLslStreamType,
    SussexQuestSetupSnapshot QuestSetup,
    WindowsEnvironmentAnalysisResult WindowsEnvironment,
    SussexMachineLslStateResult MachineLslState,
    QuestTwinStatePublisherInventory TwinStatePublisherInventory,
    SussexTwinConnectionProbeResult TwinConnection,
    SussexCommandAcceptanceResult CommandAcceptance,
    IReadOnlyList<SussexDiagnosticsKeyValue> TwinTelemetry,
    IReadOnlyList<SussexDiagnosticsKeyValue> Artifacts,
    OperationOutcomeKind Level,
    string Summary,
    string Detail);

public sealed record SussexQuestSetupSnapshot(
    HeadsetAppStatus Headset,
    InstalledAppStatus InstalledApp,
    DeviceProfileStatus DeviceProfile,
    string Selector,
    string ForegroundAndSnapshot,
    string PinnedBuild,
    string DeviceProfileSummary);

public sealed record SussexMachineLslCheckResult(
    string Label,
    OperationOutcomeKind Level,
    string Summary,
    string Detail);

public sealed record SussexMachineLslStateResult(
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    IReadOnlyList<SussexMachineLslCheckResult> Checks,
    DateTimeOffset CompletedAtUtc);

public sealed record SussexTwinConnectionProbeResult(
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    bool InletReady,
    bool ReturnPathReady,
    bool PinnedBuildReady,
    bool DeviceProfileReady,
    string ExpectedInlet,
    string RuntimeTarget,
    string ConnectedInlet,
    string Counts,
    string QuestStatus,
    string QuestEcho,
    string ReturnPath,
    string CommandChannel,
    string HotloadChannel,
    string TransportDetail,
    DateTimeOffset CheckedAtUtc);

public sealed record SussexCommandAcceptanceResult(
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    bool Attempted,
    bool Sent,
    bool Accepted,
    string ActionId,
    int? Sequence,
    string LastReportedActionId,
    string LastReportedActionSequence,
    string LastReportedParticleSequence,
    DateTimeOffset CheckedAtUtc);

public sealed record SussexDiagnosticsKeyValue(string Key, string Value);

public sealed class SussexDiagnosticsReportService
{
    public const string CommandStreamName = "quest_twin_commands";
    public const string CommandStreamType = "quest.twin.command";
    public const string ConfigStreamName = "quest_hotload_config";
    public const string ConfigStreamType = "quest.config";

    private const string ReportSchemaVersion = "2026-04-15.sussex-lsl-twin-diagnostics.v1";
    private static readonly TimeSpan DefaultProbeWaitDuration = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan TwinStateIdleThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CommandAcceptanceWaitDuration = TimeSpan.FromSeconds(6);
    private static readonly JsonSerializerOptions ReportJsonOptions = CreateJsonOptions();

    private readonly IQuestControlService _questService;
    private readonly WindowsEnvironmentAnalysisService _windowsEnvironmentAnalysisService;
    private readonly ILslStreamDiscoveryService _streamDiscoveryService;
    private readonly ITestLslSignalService _testSignalService;
    private readonly ITwinModeBridge _twinBridge;

    public SussexDiagnosticsReportService(
        IQuestControlService questService,
        WindowsEnvironmentAnalysisService windowsEnvironmentAnalysisService,
        ILslStreamDiscoveryService streamDiscoveryService,
        ITestLslSignalService testSignalService,
        ITwinModeBridge twinBridge)
    {
        _questService = questService ?? throw new ArgumentNullException(nameof(questService));
        _windowsEnvironmentAnalysisService = windowsEnvironmentAnalysisService ?? throw new ArgumentNullException(nameof(windowsEnvironmentAnalysisService));
        _streamDiscoveryService = streamDiscoveryService ?? throw new ArgumentNullException(nameof(streamDiscoveryService));
        _testSignalService = testSignalService ?? throw new ArgumentNullException(nameof(testSignalService));
        _twinBridge = twinBridge ?? throw new ArgumentNullException(nameof(twinBridge));
    }

    public async Task<SussexDiagnosticsReportResult> GenerateAsync(
        SussexDiagnosticsReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Study);

        var study = request.Study;
        var reportDirectory = ResolveReportDirectory(study, request.OutputDirectory);
        Directory.CreateDirectory(reportDirectory);

        var jsonPath = Path.Combine(reportDirectory, "sussex_lsl_twin_diagnostics.json");
        var texPath = Path.Combine(reportDirectory, "sussex_lsl_twin_diagnostics.tex");
        var pdfPath = Path.Combine(reportDirectory, "sussex_lsl_twin_diagnostics.pdf");

        var target = StudyShellOperatorBindings.CreateQuestTarget(study);
        var profile = StudyShellOperatorBindings.CreateDeviceProfile(study);

        var headset = await _questService
            .QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var installed = await _questService
            .QueryInstalledAppAsync(target, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var profileStatus = await _questService
            .QueryDeviceProfileStatusAsync(profile, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (_twinBridge is LslTwinModeBridge lslBridge)
        {
            lslBridge.ConfigureExpectedQuestStateSource(study.App.PackageId);
            lslBridge.Open();
        }

        await WaitForTwinStateAsync(
                _twinBridge as LslTwinModeBridge,
                request.ProbeWaitDuration ?? DefaultProbeWaitDuration,
                cancellationToken)
            .ConfigureAwait(false);

        var expectedName = string.IsNullOrWhiteSpace(study.Monitoring.ExpectedLslStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : study.Monitoring.ExpectedLslStreamName;
        var expectedType = string.IsNullOrWhiteSpace(study.Monitoring.ExpectedLslStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : study.Monitoring.ExpectedLslStreamType;

        var windowsEnvironment = await _windowsEnvironmentAnalysisService
            .AnalyzeAsync(new WindowsEnvironmentAnalysisRequest(expectedName, expectedType), cancellationToken)
            .ConfigureAwait(false);
        var machineLslState = await BuildMachineLslStateResultAsync(study, cancellationToken).ConfigureAwait(false);
        var twinStatePublisher = QuestTwinStatePublisherInventoryService.Inspect(_streamDiscoveryService, study.App.PackageId);
        var questSetup = BuildQuestSetupSnapshot(study, request.DeviceSelector, headset, installed, profileStatus);
        var twinConnection = BuildTwinConnectionProbe(study, headset, installed, profileStatus, _twinBridge, twinStatePublisher);
        var commandAcceptance = request.RunCommandAcceptanceCheck
            ? await ProbeCommandAcceptanceAsync(study, _twinBridge as LslTwinModeBridge, cancellationToken).ConfigureAwait(false)
            : BuildSkippedCommandAcceptanceResult("Command acceptance check skipped by request.");

        var twinTelemetry = BuildTwinTelemetry(_twinBridge as LslTwinModeBridge);
        var artifacts = new[]
        {
            new SussexDiagnosticsKeyValue("json", jsonPath),
            new SussexDiagnosticsKeyValue("tex", texPath),
            new SussexDiagnosticsKeyValue("pdf", pdfPath)
        };

        var level = CombineLevels([
            windowsEnvironment.Level,
            machineLslState.Level,
            twinStatePublisher.Level,
            twinConnection.Level,
            commandAcceptance.Level
        ]);
        var summary = level switch
        {
            OperationOutcomeKind.Failure => "Sussex LSL/twin diagnostics found blocking issues.",
            OperationOutcomeKind.Warning => "Sussex LSL/twin diagnostics found items needing attention.",
            OperationOutcomeKind.Preview => "Sussex LSL/twin diagnostics could only run in preview mode.",
            _ => "Sussex LSL/twin diagnostics passed."
        };
        var detail = BuildReportSummaryDetail(windowsEnvironment, machineLslState, twinStatePublisher, twinConnection, commandAcceptance);
        var completedAtUtc = DateTimeOffset.UtcNow;

        var report = new SussexDiagnosticsReport(
            ReportSchemaVersion,
            completedAtUtc,
            CompanionOperatorDataLayout.RootPath,
            reportDirectory,
            study.Id,
            study.Label,
            study.App.PackageId,
            expectedName,
            expectedType,
            questSetup,
            windowsEnvironment,
            machineLslState,
            twinStatePublisher,
            twinConnection,
            commandAcceptance,
            twinTelemetry,
            artifacts,
            level,
            summary,
            detail);

        await File.WriteAllTextAsync(
                jsonPath,
                JsonSerializer.Serialize(report, ReportJsonOptions),
                Encoding.UTF8,
                cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(texPath, RenderLatexReport(report), Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);

        return new SussexDiagnosticsReportResult(
            level,
            summary,
            detail,
            reportDirectory,
            jsonPath,
            texPath,
            pdfPath,
            completedAtUtc,
            report);
    }

    public static string RenderLatexReport(SussexDiagnosticsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine(@"\documentclass[10pt]{article}");
        builder.AppendLine(@"\usepackage[a4paper,margin=18mm]{geometry}");
        builder.AppendLine(@"\usepackage[T1]{fontenc}");
        builder.AppendLine(@"\usepackage[utf8]{inputenc}");
        builder.AppendLine(@"\usepackage{longtable}");
        builder.AppendLine(@"\usepackage{xcolor}");
        builder.AppendLine(@"\usepackage{hyperref}");
        builder.AppendLine(@"\hypersetup{colorlinks=true,linkcolor=black,urlcolor=blue}");
        builder.AppendLine(@"\setlength{\parindent}{0pt}");
        builder.AppendLine(@"\setlength{\parskip}{6pt}");
        builder.AppendLine(@"\begin{document}");
        builder.AppendLine(@"\section*{" + EscapeLatex($"{report.StudyLabel} LSL/Twin Diagnostics") + "}");
        builder.AppendLine($@"\textbf{{Generated:}} {EscapeLatex(report.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture))}\\");
        builder.AppendLine($@"\textbf{{Overall:}} {RenderLevel(report.Level)} - {EscapeLatex(report.Summary)}\\");
        builder.AppendLine(EscapeLatex(report.Detail));

        AppendKeyValueTable(builder, "Study Setup", [
            new SussexDiagnosticsKeyValue("study", report.StudyId),
            new SussexDiagnosticsKeyValue("package", report.PackageId),
            new SussexDiagnosticsKeyValue("expected upstream", $"{report.ExpectedLslStreamName} / {report.ExpectedLslStreamType}"),
            new SussexDiagnosticsKeyValue("selector", report.QuestSetup.Selector),
            new SussexDiagnosticsKeyValue("foreground", report.QuestSetup.ForegroundAndSnapshot),
            new SussexDiagnosticsKeyValue("pinned build", report.QuestSetup.PinnedBuild),
            new SussexDiagnosticsKeyValue("device profile", report.QuestSetup.DeviceProfileSummary)
        ]);

        AppendCheckTable(builder, "Windows Environment", report.WindowsEnvironment.Checks.Select(check =>
            new SussexMachineLslCheckResult(check.Label, check.Level, check.Summary, check.Detail)));
        AppendCheckTable(builder, "Machine LSL State", report.MachineLslState.Checks);
        AppendKeyValueTable(builder, "Quest Twin Return Path", [
            new SussexDiagnosticsKeyValue("twin-state outlet", QuestTwinStatePublisherInventoryService.RenderForOperator(report.TwinStatePublisherInventory)),
            new SussexDiagnosticsKeyValue("expected inlet", report.TwinConnection.ExpectedInlet),
            new SussexDiagnosticsKeyValue("runtime target", report.TwinConnection.RuntimeTarget),
            new SussexDiagnosticsKeyValue("connected inlet", report.TwinConnection.ConnectedInlet),
            new SussexDiagnosticsKeyValue("counts", report.TwinConnection.Counts),
            new SussexDiagnosticsKeyValue("quest status", report.TwinConnection.QuestStatus),
            new SussexDiagnosticsKeyValue("quest echo", report.TwinConnection.QuestEcho),
            new SussexDiagnosticsKeyValue("return path", report.TwinConnection.ReturnPath),
            new SussexDiagnosticsKeyValue("transport", report.TwinConnection.TransportDetail)
        ]);

        AppendKeyValueTable(builder, "Command Acceptance Probe", [
            new SussexDiagnosticsKeyValue("level", RenderLevel(report.CommandAcceptance.Level)),
            new SussexDiagnosticsKeyValue("summary", report.CommandAcceptance.Summary),
            new SussexDiagnosticsKeyValue("action id", report.CommandAcceptance.ActionId),
            new SussexDiagnosticsKeyValue("sequence", report.CommandAcceptance.Sequence?.ToString(CultureInfo.InvariantCulture) ?? "n/a"),
            new SussexDiagnosticsKeyValue("accepted", report.CommandAcceptance.Accepted ? "yes" : "no"),
            new SussexDiagnosticsKeyValue("detail", report.CommandAcceptance.Detail)
        ]);

        if (report.TwinTelemetry.Count > 0)
        {
            AppendKeyValueTable(builder, "Key Twin Telemetry", report.TwinTelemetry);
        }

        AppendKeyValueTable(builder, "Artifacts", report.Artifacts);
        builder.AppendLine(@"\end{document}");
        return builder.ToString();
    }

    private async Task<SussexMachineLslStateResult> BuildMachineLslStateResultAsync(
        StudyShellDefinition study,
        CancellationToken cancellationToken)
    {
        var expectedStreamName = string.IsNullOrWhiteSpace(study.Monitoring.ExpectedLslStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : study.Monitoring.ExpectedLslStreamName;
        var expectedStreamType = string.IsNullOrWhiteSpace(study.Monitoring.ExpectedLslStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : study.Monitoring.ExpectedLslStreamType;
        var testSenderSourceId = BuildTestSenderSourceId(study);
        var localTwinOutletsActive = (_twinBridge as LslTwinModeBridge)?.IsCommandOutletOpen == true;
        var runtimeState = _streamDiscoveryService.RuntimeState;

        IReadOnlyList<LslVisibleStreamInfo> expectedStreams = [];
        IReadOnlyList<LslVisibleStreamInfo> twinCommandStreams = [];
        IReadOnlyList<LslVisibleStreamInfo> twinConfigStreams = [];
        IReadOnlyList<LslVisibleStreamInfo> clockProbeStreams = [];

        if (runtimeState.Available)
        {
            expectedStreams = await DiscoverSafeAsync(new LslStreamDiscoveryRequest(expectedStreamName, expectedStreamType), cancellationToken).ConfigureAwait(false);
            twinCommandStreams = await DiscoverSafeAsync(new LslStreamDiscoveryRequest(CommandStreamName, CommandStreamType), cancellationToken).ConfigureAwait(false);
            twinConfigStreams = await DiscoverSafeAsync(new LslStreamDiscoveryRequest(ConfigStreamName, ConfigStreamType), cancellationToken).ConfigureAwait(false);
            clockProbeStreams = await DiscoverSafeAsync(new LslStreamDiscoveryRequest(SussexClockAlignmentStreamContract.ProbeStreamName, SussexClockAlignmentStreamContract.ProbeStreamType), cancellationToken).ConfigureAwait(false);
        }

        var companionExpectedStreams = expectedStreams
            .Where(stream => string.Equals(stream.SourceId, testSenderSourceId, StringComparison.Ordinal))
            .ToArray();

        var checks = new[]
        {
            BuildMachineLslRuntimeCheck(runtimeState),
            BuildExpectedUpstreamInventoryCheck(expectedStreamName, expectedStreamType, expectedStreams, testSenderSourceId),
            BuildTestSenderServiceCheck(study, expectedStreamName, expectedStreamType, companionExpectedStreams),
            BuildTwinOutletInventoryCheck(localTwinOutletsActive, twinCommandStreams, twinConfigStreams, _twinBridge.Status),
            BuildClockAlignmentTransportCheck(clockProbeStreams),
            BuildPassiveUpstreamMonitorCheck()
        };

        var level = CombineLevels(checks.Select(check => check.Level));
        var summary = level switch
        {
            OperationOutcomeKind.Failure => "Machine LSL state found blocking issues.",
            OperationOutcomeKind.Warning => "Machine LSL state needs attention.",
            OperationOutcomeKind.Preview => "Machine LSL state could only run in preview mode.",
            _ => "Machine LSL state looks clean."
        };

        var warningCount = checks.Count(check => check.Level == OperationOutcomeKind.Warning);
        var failureCount = checks.Count(check => check.Level == OperationOutcomeKind.Failure);
        return new SussexMachineLslStateResult(
            level,
            summary,
            $"Checks warn/fail: {warningCount.ToString(CultureInfo.InvariantCulture)}/{failureCount.ToString(CultureInfo.InvariantCulture)}.",
            checks,
            DateTimeOffset.UtcNow);
    }

    private async Task<IReadOnlyList<LslVisibleStreamInfo>> DiscoverSafeAsync(
        LslStreamDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => _streamDiscoveryService.Discover(request), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    private static SussexQuestSetupSnapshot BuildQuestSetupSnapshot(
        StudyShellDefinition study,
        string? deviceSelector,
        HeadsetAppStatus headset,
        InstalledAppStatus installed,
        DeviceProfileStatus profileStatus)
        => new(
            headset,
            installed,
            profileStatus,
            BuildSelectorLabel(deviceSelector, headset),
            BuildForegroundAndSnapshotLabel(headset),
            BuildPinnedBuildLabel(study, installed),
            BuildDeviceProfileLabel(profileStatus));

    private static SussexTwinConnectionProbeResult BuildTwinConnectionProbe(
        StudyShellDefinition study,
        HeadsetAppStatus headset,
        InstalledAppStatus installed,
        DeviceProfileStatus profileStatus,
        ITwinModeBridge bridge,
        QuestTwinStatePublisherInventory twinStatePublisher)
    {
        var checkedAtUtc = DateTimeOffset.UtcNow;
        if (bridge is not LslTwinModeBridge lslBridge)
        {
            return new SussexTwinConnectionProbeResult(
                OperationOutcomeKind.Preview,
                "Twin bridge transport is unavailable in this environment.",
                bridge.Status.Detail,
                InletReady: false,
                ReturnPathReady: false,
                PinnedBuildReady: IsPinnedBuildReady(study, installed),
                DeviceProfileReady: profileStatus.IsActive,
                ExpectedInlet: $"{study.Monitoring.ExpectedLslStreamName} / {study.Monitoring.ExpectedLslStreamType}",
                RuntimeTarget: "Runtime target n/a",
                ConnectedInlet: "Connected stream n/a",
                Counts: "Connection counts n/a",
                QuestStatus: "Twin bridge unavailable.",
                QuestEcho: "No inlet value reported yet.",
                ReturnPath: "No quest_twin_state / quest.twin.state frame has reached Windows yet.",
                CommandChannel: $"{CommandStreamName} / {CommandStreamType}",
                HotloadChannel: $"{ConfigStreamName} / {ConfigStreamType}",
                TransportDetail: bridge.Status.Detail,
                CheckedAtUtc: checkedAtUtc);
        }

        return BuildTwinConnectionProbeState(study, headset, installed, profileStatus, lslBridge, twinStatePublisher, checkedAtUtc);
    }

    private static SussexTwinConnectionProbeResult BuildTwinConnectionProbeState(
        StudyShellDefinition study,
        HeadsetAppStatus headset,
        InstalledAppStatus installed,
        DeviceProfileStatus profileStatus,
        LslTwinModeBridge bridge,
        QuestTwinStatePublisherInventory twinStatePublisher,
        DateTimeOffset checkedAtUtc)
    {
        var expectedName = string.IsNullOrWhiteSpace(study.Monitoring.ExpectedLslStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : study.Monitoring.ExpectedLslStreamName;
        var expectedType = string.IsNullOrWhiteSpace(study.Monitoring.ExpectedLslStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : study.Monitoring.ExpectedLslStreamType;
        var settings = bridge.ReportedSettings;

        string? GetFirstValue(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        string? GetFirstValueFromCollection(IEnumerable<string> keys)
            => GetFirstValue(keys.ToArray());

        var streamName = GetFirstValue("study.lsl.filter_name") ?? GetFirstValueFromCollection(study.Monitoring.LslStreamNameKeys);
        var streamType = GetFirstValue("study.lsl.filter_type") ?? GetFirstValueFromCollection(study.Monitoring.LslStreamTypeKeys);
        var connectedName = GetFirstValue("study.lsl.connected_name");
        var connectedType = GetFirstValue("study.lsl.connected_type");
        var statusLine = GetFirstValue("study.lsl.status");
        var connectedFlag = ParseBool(GetFirstValue("study.lsl.connected"));
        var connectedCount = ParseInt(GetFirstValue("connection.lsl.connected_count"));
        var connectingCount = ParseInt(GetFirstValue("connection.lsl.connecting_count"));
        var totalCount = ParseInt(GetFirstValue("connection.lsl.total_count"));
        var hasInputValue = TryGetObservedLslValue(settings, study, out var inputValue, out var inputValueKey);
        var hasConnectedInput = connectedFlag == true || connectedCount.GetValueOrDefault() > 0 || !string.IsNullOrWhiteSpace(connectedName);

        var runtimeTarget = $"{(string.IsNullOrWhiteSpace(streamName) ? "n/a" : streamName)} / {(string.IsNullOrWhiteSpace(streamType) ? "n/a" : streamType)}";
        var connectedInlet = $"{(string.IsNullOrWhiteSpace(connectedName) ? "n/a" : connectedName)} / {(string.IsNullOrWhiteSpace(connectedType) ? "n/a" : connectedType)}";
        var counts = $"Connected {connectedCount?.ToString(CultureInfo.InvariantCulture) ?? (connectedFlag.HasValue ? (connectedFlag.Value ? "1" : "0") : "n/a")}, connecting {connectingCount?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}, total {totalCount?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}";
        var questStatus = string.IsNullOrWhiteSpace(statusLine)
            ? "Runtime did not publish an extra LSL status line."
            : statusLine;

        var lastStateReceivedAt = bridge.LastStateReceivedAt;
        var hasTwinState = settings.Count > 0 || lastStateReceivedAt.HasValue;
        var returnPathReady = hasTwinState && (!lastStateReceivedAt.HasValue || checkedAtUtc - lastStateReceivedAt.Value <= TwinStateIdleThreshold);
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
        var headsetForeground = string.Equals(headset.ForegroundPackageId, study.App.PackageId, StringComparison.OrdinalIgnoreCase);
        var pinnedBuildReady = IsPinnedBuildReady(study, installed);
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
            detailParts.Add($"Install the pinned Sussex APK before debugging LSL; expected {ShortHash(study.App.Sha256)}, found {ShortHash(installed.InstalledSha256)}.");
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

        detailParts.Add(headsetForeground
            ? "The Sussex package is foregrounded per ADB, so this is no longer just a launch-selection problem."
            : "The pinned Sussex package is not currently confirmed in the foreground. Re-check the visible headset scene before blaming LSL.");

        return new SussexTwinConnectionProbeResult(
            level,
            summary,
            string.Join(" ", detailParts),
            inletReady,
            returnPathReady,
            pinnedBuildReady,
            deviceProfileReady,
            ExpectedInlet: $"{expectedName} / {expectedType}",
            RuntimeTarget: runtimeTarget,
            ConnectedInlet: connectedInlet,
            Counts: counts,
            QuestStatus: questStatus,
            QuestEcho: questEcho,
            ReturnPath: returnPath,
            CommandChannel: $"{CommandStreamName} / {CommandStreamType}",
            HotloadChannel: $"{ConfigStreamName} / {ConfigStreamType}",
            TransportDetail: bridge.Status.Detail,
            CheckedAtUtc: checkedAtUtc);
    }

    private static async Task<SussexCommandAcceptanceResult> ProbeCommandAcceptanceAsync(
        StudyShellDefinition study,
        LslTwinModeBridge? bridge,
        CancellationToken cancellationToken)
    {
        var checkedAtUtc = DateTimeOffset.UtcNow;
        if (bridge is null)
        {
            return BuildSkippedCommandAcceptanceResult("Command acceptance check requires the public LSL twin bridge.");
        }

        var actionId = study.Controls.ParticleVisibleOffActionId;
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return BuildSkippedCommandAcceptanceResult("No safe particle-off command action id is configured for this study shell.");
        }

        bridge.Open();
        var command = new TwinModeCommand(actionId, "Diagnostics particle visibility off");
        var sendOutcome = await bridge.SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
        var sequence = bridge.LastPublishedCommandSequence > 0 ? bridge.LastPublishedCommandSequence : (int?)null;

        await WaitForCommandEchoAsync(bridge, actionId, sequence, CommandAcceptanceWaitDuration, cancellationToken).ConfigureAwait(false);
        var settings = bridge.ReportedSettings;
        var lastActionId = GetSetting(settings, "study.command.last_action_id");
        var lastActionSequence = GetSetting(settings, "study.command.last_action_sequence");
        var lastParticleSequence = GetSetting(settings, "study.particles.last_command_sequence");
        var accepted = sendOutcome.Kind != OperationOutcomeKind.Failure &&
            (string.Equals(lastActionId, actionId, StringComparison.OrdinalIgnoreCase) ||
             SequenceMatches(lastActionSequence, sequence) ||
             SequenceMatches(lastParticleSequence, sequence));

        if (sendOutcome.Kind == OperationOutcomeKind.Failure)
        {
            return new SussexCommandAcceptanceResult(
                OperationOutcomeKind.Failure,
                "Companion could not publish the diagnostic twin command.",
                sendOutcome.Detail,
                Attempted: true,
                Sent: false,
                Accepted: false,
                ActionId: actionId,
                Sequence: sequence,
                LastReportedActionId: lastActionId,
                LastReportedActionSequence: lastActionSequence,
                LastReportedParticleSequence: lastParticleSequence,
                CheckedAtUtc: checkedAtUtc);
        }

        return accepted
            ? new SussexCommandAcceptanceResult(
                OperationOutcomeKind.Success,
                "Quest accepted the diagnostic twin command.",
                "The companion published a safe particle-off command and the returned quest_twin_state reported the matching action id or sequence.",
                Attempted: true,
                Sent: true,
                Accepted: true,
                ActionId: actionId,
                Sequence: sequence,
                LastReportedActionId: lastActionId,
                LastReportedActionSequence: lastActionSequence,
                LastReportedParticleSequence: lastParticleSequence,
                CheckedAtUtc: checkedAtUtc)
            : new SussexCommandAcceptanceResult(
                OperationOutcomeKind.Warning,
                "Diagnostic twin command was published, but no matching Quest acknowledgement returned.",
                "The command outlet sent the safe particle-off command. The report did not observe the action id or sequence on quest_twin_state before the timeout; this points at return telemetry, command handling, or a stale scene state.",
                Attempted: true,
                Sent: true,
                Accepted: false,
                ActionId: actionId,
                Sequence: sequence,
                LastReportedActionId: lastActionId,
                LastReportedActionSequence: lastActionSequence,
                LastReportedParticleSequence: lastParticleSequence,
                CheckedAtUtc: checkedAtUtc);
    }

    private static async Task WaitForTwinStateAsync(
        LslTwinModeBridge? bridge,
        TimeSpan waitDuration,
        CancellationToken cancellationToken)
    {
        if (bridge is null || bridge.LastStateReceivedAt.HasValue || waitDuration <= TimeSpan.Zero)
        {
            return;
        }

        var timeoutAtUtc = DateTimeOffset.UtcNow + waitDuration;
        while (!bridge.LastStateReceivedAt.HasValue && DateTimeOffset.UtcNow < timeoutAtUtc)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitForCommandEchoAsync(
        LslTwinModeBridge bridge,
        string actionId,
        int? sequence,
        TimeSpan waitDuration,
        CancellationToken cancellationToken)
    {
        var timeoutAtUtc = DateTimeOffset.UtcNow + waitDuration;
        while (DateTimeOffset.UtcNow < timeoutAtUtc)
        {
            var settings = bridge.ReportedSettings;
            if (string.Equals(GetSetting(settings, "study.command.last_action_id"), actionId, StringComparison.OrdinalIgnoreCase) ||
                SequenceMatches(GetSetting(settings, "study.command.last_action_sequence"), sequence) ||
                SequenceMatches(GetSetting(settings, "study.particles.last_command_sequence"), sequence))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }

    private static SussexCommandAcceptanceResult BuildSkippedCommandAcceptanceResult(string detail)
        => new(
            OperationOutcomeKind.Preview,
            "Command acceptance check was not attempted.",
            detail,
            Attempted: false,
            Sent: false,
            Accepted: false,
            ActionId: "n/a",
            Sequence: null,
            LastReportedActionId: "n/a",
            LastReportedActionSequence: "n/a",
            LastReportedParticleSequence: "n/a",
            CheckedAtUtc: DateTimeOffset.UtcNow);

    private static IReadOnlyList<SussexDiagnosticsKeyValue> BuildTwinTelemetry(LslTwinModeBridge? bridge)
    {
        if (bridge is null)
        {
            return [];
        }

        var prefixes = new[]
        {
            "study.lsl.",
            "connection.lsl.",
            "study.command.",
            "study.particles.",
            "tracker.breathing.controller.",
            "signal01.",
            "driver.stream."
        };

        return bridge.ReportedSettings
            .Where(entry => prefixes.Any(prefix => entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .Select(entry => new SussexDiagnosticsKeyValue(entry.Key, entry.Value))
            .ToArray();
    }

    private static SussexMachineLslCheckResult BuildMachineLslRuntimeCheck(LslRuntimeState runtimeState)
        => runtimeState.Available
            ? new SussexMachineLslCheckResult(
                "Windows liblsl discovery runtime",
                OperationOutcomeKind.Success,
                "Windows liblsl discovery runtime is ready.",
                runtimeState.Detail)
            : new SussexMachineLslCheckResult(
                "Windows liblsl discovery runtime",
                OperationOutcomeKind.Failure,
                "Windows liblsl discovery runtime is unavailable.",
                runtimeState.Detail);

    private static SussexMachineLslCheckResult BuildExpectedUpstreamInventoryCheck(
        string expectedStreamName,
        string expectedStreamType,
        IReadOnlyList<LslVisibleStreamInfo> expectedStreams,
        string companionTestSourceId)
    {
        if (expectedStreams.Count == 0)
        {
            return new SussexMachineLslCheckResult(
                "Expected upstream sources",
                OperationOutcomeKind.Warning,
                $"No {expectedStreamName} / {expectedStreamType} sources are visible on Windows.",
                $"Start the intended upstream sender or inspect the external publisher. The companion currently cannot see any source advertising {expectedStreamName} / {expectedStreamType} on this PC.");
        }

        var detail = $"Visible matches:{Environment.NewLine}{FormatVisibleStreamInventory(expectedStreams)}";
        if (expectedStreams.Count == 1)
        {
            return new SussexMachineLslCheckResult(
                "Expected upstream sources",
                OperationOutcomeKind.Success,
                $"{expectedStreamName} / {expectedStreamType} is visible on Windows.",
                detail);
        }

        var includesCompanionTestSender = expectedStreams.Any(stream => string.Equals(stream.SourceId, companionTestSourceId, StringComparison.Ordinal));
        var summary = includesCompanionTestSender
            ? $"Multiple {expectedStreamName} / {expectedStreamType} sources are visible, including the companion TEST sender."
            : $"Multiple {expectedStreamName} / {expectedStreamType} sources are visible on Windows.";
        var warningDetail = includesCompanionTestSender
            ? $"{detail}{Environment.NewLine}This can make switching between the companion TEST sender and external sources unreliable because more than one source is advertising the same expected stream contract."
            : $"{detail}{Environment.NewLine}More than one upstream source is advertising the expected stream contract on Windows.";
        return new SussexMachineLslCheckResult(
            "Expected upstream sources",
            OperationOutcomeKind.Warning,
            summary,
            warningDetail);
    }

    private SussexMachineLslCheckResult BuildTestSenderServiceCheck(
        StudyShellDefinition study,
        string expectedStreamName,
        string expectedStreamType,
        IReadOnlyList<LslVisibleStreamInfo> companionExpectedStreams)
    {
        if (!_testSignalService.RuntimeState.Available)
        {
            return new SussexMachineLslCheckResult(
                "Companion TEST sender",
                OperationOutcomeKind.Preview,
                "Companion TEST sender unavailable.",
                _testSignalService.RuntimeState.Detail);
        }

        if (!string.IsNullOrWhiteSpace(_testSignalService.LastFaultDetail))
        {
            return new SussexMachineLslCheckResult(
                "Companion TEST sender",
                OperationOutcomeKind.Failure,
                "Companion TEST sender stopped after a local fault.",
                _testSignalService.LastFaultDetail);
        }

        if (_testSignalService.IsRunning)
        {
            return companionExpectedStreams.Count switch
            {
                0 => new SussexMachineLslCheckResult(
                    "Companion TEST sender",
                    OperationOutcomeKind.Warning,
                    "Companion TEST sender says it is running, but its source is not visible yet.",
                    $"The local sender is active, but no visible source matched `{BuildTestSenderSourceId(study)}` for {expectedStreamName} / {expectedStreamType}. If this does not clear after a refresh, the local sender may not be advertising cleanly."),
                1 => new SussexMachineLslCheckResult(
                    "Companion TEST sender",
                    OperationOutcomeKind.Success,
                    "Companion TEST sender is running and visible.",
                    FormatVisibleStreamInventory(companionExpectedStreams)),
                _ => new SussexMachineLslCheckResult(
                    "Companion TEST sender",
                    OperationOutcomeKind.Warning,
                    "Companion TEST sender source appears more than once.",
                    FormatVisibleStreamInventory(companionExpectedStreams))
            };
        }

        if (companionExpectedStreams.Count > 0)
        {
            return new SussexMachineLslCheckResult(
                "Companion TEST sender",
                OperationOutcomeKind.Warning,
                "Companion TEST sender is off, but its source is still visible on Windows.",
                $"{FormatVisibleStreamInventory(companionExpectedStreams)}{Environment.NewLine}If this persists after another refresh, the sender may not have shut down cleanly or another companion instance is still advertising the same source id.");
        }

        return new SussexMachineLslCheckResult(
            "Companion TEST sender",
            OperationOutcomeKind.Preview,
            "Companion TEST sender idle.",
            $"The companion is not currently publishing {expectedStreamName} / {expectedStreamType}.");
    }

    private static SussexMachineLslCheckResult BuildTwinOutletInventoryCheck(
        bool localTwinOutletsActive,
        IReadOnlyList<LslVisibleStreamInfo> twinCommandStreams,
        IReadOnlyList<LslVisibleStreamInfo> twinConfigStreams,
        TwinBridgeStatus twinBridgeStatus)
    {
        var detail =
            $"Local twin bridge: {twinBridgeStatus.Summary}{Environment.NewLine}" +
            $"Command stream matches ({CommandStreamName} / {CommandStreamType}): {twinCommandStreams.Count}{Environment.NewLine}" +
            $"{FormatVisibleStreamInventory(twinCommandStreams)}{Environment.NewLine}" +
            $"Config stream matches ({ConfigStreamName} / {ConfigStreamType}): {twinConfigStreams.Count}{Environment.NewLine}" +
            $"{FormatVisibleStreamInventory(twinConfigStreams)}";

        if (localTwinOutletsActive)
        {
            if (twinCommandStreams.Count == 1 && twinConfigStreams.Count == 1)
            {
                return new SussexMachineLslCheckResult(
                    "Companion twin outlets",
                    OperationOutcomeKind.Success,
                    "Companion twin outlets are active and visible.",
                    detail);
            }

            return new SussexMachineLslCheckResult(
                "Companion twin outlets",
                OperationOutcomeKind.Warning,
                "Companion twin outlets are active, but the visible Windows inventory is unexpected.",
                detail);
        }

        if (twinCommandStreams.Count > 0 || twinConfigStreams.Count > 0)
        {
            return new SussexMachineLslCheckResult(
                "Companion twin outlets",
                OperationOutcomeKind.Warning,
                "Twin outlet streams are still visible while the local bridge is idle.",
                detail);
        }

        return new SussexMachineLslCheckResult(
            "Companion twin outlets",
            OperationOutcomeKind.Preview,
            "Companion twin outlets idle.",
            twinBridgeStatus.Detail);
    }

    private static SussexMachineLslCheckResult BuildClockAlignmentTransportCheck(
        IReadOnlyList<LslVisibleStreamInfo> clockProbeStreams)
    {
        var stateDetail =
            $"Probe stream matches ({SussexClockAlignmentStreamContract.ProbeStreamName} / {SussexClockAlignmentStreamContract.ProbeStreamType}): {clockProbeStreams.Count}{Environment.NewLine}" +
            FormatVisibleStreamInventory(clockProbeStreams);

        return clockProbeStreams.Count switch
        {
            0 => new SussexMachineLslCheckResult(
                "Clock-alignment transport",
                OperationOutcomeKind.Preview,
                "Clock-alignment transport idle.",
                stateDetail),
            1 => new SussexMachineLslCheckResult(
                "Clock-alignment transport",
                OperationOutcomeKind.Success,
                "Clock-alignment probe transport is visible.",
                stateDetail),
            _ => new SussexMachineLslCheckResult(
                "Clock-alignment transport",
                OperationOutcomeKind.Warning,
                "More than one clock-alignment probe stream is visible.",
                stateDetail)
        };
    }

    private static SussexMachineLslCheckResult BuildPassiveUpstreamMonitorCheck()
        => new(
            "Passive upstream monitor",
            OperationOutcomeKind.Preview,
            "Passive upstream monitor state is GUI/session-owned.",
            $"The report inspects visible {HrvBiofeedbackStreamContract.StreamName} / {HrvBiofeedbackStreamContract.StreamType} streams directly. Participant-session passive monitor state is only meaningful while the Experiment Session recorder is active.");

    private static string ResolveReportDirectory(StudyShellDefinition study, string? outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Path.GetFullPath(outputDirectory);
        }

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        return Path.Combine(CompanionOperatorDataLayout.DiagnosticsRootPath, study.Id, $"diagnostics-{stamp}");
    }

    private static string BuildReportSummaryDetail(
        WindowsEnvironmentAnalysisResult windowsEnvironment,
        SussexMachineLslStateResult machineLslState,
        QuestTwinStatePublisherInventory twinStatePublisher,
        SussexTwinConnectionProbeResult twinConnection,
        SussexCommandAcceptanceResult commandAcceptance)
    {
        var attention = new[]
        {
            ("Windows Environment", windowsEnvironment.Level, windowsEnvironment.Summary),
            ("Machine LSL State", machineLslState.Level, machineLslState.Summary),
            ("Quest twin-state outlet", twinStatePublisher.Level, twinStatePublisher.Summary),
            ("Twin connection", twinConnection.Level, twinConnection.Summary),
            ("Command acceptance", commandAcceptance.Level, commandAcceptance.Summary)
        }
        .Where(item => item.Level is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
        .Select(item => $"{item.Item1}: {item.Summary}")
        .ToArray();

        return attention.Length == 0
            ? "Windows LSL, machine inventory, Quest twin-state return path, and command acceptance all reported cleanly."
            : string.Join(" ", attention);
    }

    private static OperationOutcomeKind CombineLevels(IEnumerable<OperationOutcomeKind> levels)
    {
        var materialized = levels.ToArray();
        if (materialized.Any(level => level == OperationOutcomeKind.Failure))
        {
            return OperationOutcomeKind.Failure;
        }

        if (materialized.Any(level => level == OperationOutcomeKind.Warning))
        {
            return OperationOutcomeKind.Warning;
        }

        return materialized.Any(level => level == OperationOutcomeKind.Success)
            ? OperationOutcomeKind.Success
            : OperationOutcomeKind.Preview;
    }

    private static string BuildSelectorLabel(string? device, HeadsetAppStatus headset)
    {
        var selector = !string.IsNullOrWhiteSpace(device) ? device : headset.ConnectionLabel;
        if (string.IsNullOrWhiteSpace(selector))
        {
            return "Selector n/a.";
        }

        var transport = selector.Contains(":", StringComparison.Ordinal) ? "Wi-Fi ADB" : "USB ADB";
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

    private static string BuildPinnedBuildLabel(StudyShellDefinition study, InstalledAppStatus installed)
    {
        var expected = ShortHash(study.App.Sha256);
        if (!installed.IsInstalled)
        {
            return $"Not installed; pinned {expected}.";
        }

        var actual = ShortHash(installed.InstalledSha256);
        var version = string.IsNullOrWhiteSpace(installed.VersionName) ? "version n/a" : $"version {installed.VersionName}";
        return $"Installed match {IsPinnedBuildReady(study, installed)}; pinned {expected}, installed {actual}, {version}.";
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

        return string.IsNullOrWhiteSpace(ipAddress) ? ssid : $"{ssid} / {ipAddress}";
    }

    private static bool IsPinnedBuildReady(StudyShellDefinition study, InstalledAppStatus installed)
        => installed.IsInstalled && MatchesHash(installed.InstalledSha256, study.App.Sha256);

    private static bool MatchesHash(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string ShortHash(string? hash)
        => string.IsNullOrWhiteSpace(hash)
            ? "n/a"
            : hash.Length <= 12
                ? hash
                : hash[..12];

    private static string FormatVisibleStreamInventory(IReadOnlyList<LslVisibleStreamInfo> streams)
        => streams.Count == 0
            ? "No matching visible streams."
            : string.Join(
                Environment.NewLine,
                streams.Select(static stream =>
                    $"{stream.Name} / {stream.Type} | source_id `{(string.IsNullOrWhiteSpace(stream.SourceId) ? "n/a" : stream.SourceId)}` | channels {stream.ChannelCount.ToString(CultureInfo.InvariantCulture)} | nominal {stream.SampleRateHz.ToString("0.###", CultureInfo.InvariantCulture)} Hz"));

    private static string BuildTestSenderSourceId(StudyShellDefinition study)
        => $"viscereality.companion.study-shell.test.{study.Id}";

    private static bool TryGetObservedLslValue(
        IReadOnlyDictionary<string, string> reportedSettings,
        StudyShellDefinition study,
        out double value,
        out string sourceKey)
    {
        if (TryGetConfiguredUnitIntervalValue(reportedSettings, ["study.lsl.latest_default_value", "study.lsl.latest_ch0_value"], out value, out sourceKey))
        {
            return true;
        }

        if (TryGetConfiguredUnitIntervalValue(reportedSettings, study.Monitoring.LslValueKeys, out value, out sourceKey))
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
        => bool.TryParse(rawValue, out var parsed) ? parsed : null;

    private static int? ParseInt(string? rawValue)
        => int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static double? ParseUnitInterval(string? rawValue)
    {
        if (!double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        return double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0d || parsed > 1d
            ? null
            : parsed;
    }

    private static string GetSetting(IReadOnlyDictionary<string, string> settings, string key)
        => settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : "n/a";

    private static bool SequenceMatches(string? rawValue, int? sequence)
        => sequence.HasValue &&
           int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
           parsed == sequence.Value;

    private static void AppendKeyValueTable(
        StringBuilder builder,
        string title,
        IEnumerable<SussexDiagnosticsKeyValue> rows)
    {
        builder.AppendLine(@"\section*{" + EscapeLatex(title) + "}");
        builder.AppendLine(@"\begin{longtable}{p{0.27\linewidth}p{0.67\linewidth}}");
        foreach (var row in rows)
        {
            builder.AppendLine($@"\textbf{{{EscapeLatex(row.Key)}}} & {EscapeLatex(row.Value)} \\");
        }

        builder.AppendLine(@"\end{longtable}");
    }

    private static void AppendCheckTable(
        StringBuilder builder,
        string title,
        IEnumerable<SussexMachineLslCheckResult> checks)
    {
        builder.AppendLine(@"\section*{" + EscapeLatex(title) + "}");
        builder.AppendLine(@"\begin{longtable}{p{0.18\linewidth}p{0.24\linewidth}p{0.50\linewidth}}");
        builder.AppendLine(@"\textbf{Level} & \textbf{Check} & \textbf{Result} \\");
        foreach (var check in checks)
        {
            builder.AppendLine($@"{EscapeLatex(RenderLevel(check.Level))} & {EscapeLatex(check.Label)} & {EscapeLatex($"{check.Summary} {check.Detail}".Trim())} \\");
        }

        builder.AppendLine(@"\end{longtable}");
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

    private static string EscapeLatex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "n/a";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                '\\' => @"\textbackslash{}",
                '&' => @"\&",
                '%' => @"\%",
                '$' => @"\$",
                '#' => @"\#",
                '_' => @"\_",
                '{' => @"\{",
                '}' => @"\}",
                '~' => @"\textasciitilde{}",
                '^' => @"\textasciicircum{}",
                '\r' => string.Empty,
                '\n' => @"\newline{} ",
                _ => ch.ToString()
            });
        }

        return builder.ToString();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
