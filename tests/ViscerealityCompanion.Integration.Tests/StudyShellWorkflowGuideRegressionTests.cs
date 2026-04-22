using System.Reflection;
using System.Runtime.CompilerServices;
using ViscerealityCompanion.Core.Services;
using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class StudyShellWorkflowGuideRegressionTests
{
    [Fact]
    public void PinnedBuildStatus_HidesVerifiedBaselineStateWhenPinnedApkMatches()
    {
        var viewModel = CreateViewModel(CreateStudy(CreateBaseline()));
        SetPrivateField(
            viewModel,
            "_installedAppStatus",
            new InstalledAppStatus(
                "com.Viscereality.SussexExperiment",
                true,
                "1.0.0",
                "100",
                "apk-hash",
                "/data/app/sussex.apk",
                "Installed Sussex APK matches the pinned hash.",
                "Installed Sussex APK hash is current."));
        SetPrivateField(
            viewModel,
            "_headsetStatus",
            new HeadsetAppStatus(
                IsConnected: true,
                ConnectionLabel: "192.168.0.55:5555",
                DeviceModel: "Quest 3",
                BatteryLevel: 92,
                CpuLevel: 4,
                GpuLevel: 4,
                ForegroundPackageId: "com.Viscereality.SussexExperiment",
                IsTargetInstalled: true,
                IsTargetRunning: false,
                IsTargetForeground: false,
                RemoteOnlyControlEnabled: true,
                Timestamp: DateTimeOffset.UtcNow,
                Summary: "Connected.",
                Detail: "Connected over Wi-Fi ADB.",
                IsWifiAdbTransport: true,
                HeadsetWifiSsid: "SussexLab",
                HeadsetWifiIpAddress: "192.168.0.55",
                HostWifiSsid: "SussexLab",
                WifiSsidMatchesHost: true,
                SoftwareReleaseOrCodename: "v63",
                SoftwareBuildId: "new-build",
                SoftwareDisplayId: "new-display"));
        SetPrivateField(
            viewModel,
            "_deviceProfileStatus",
            new DeviceProfileStatus(
                "sussex-profile",
                "Sussex Device Profile",
                true,
                "Pinned Quest device profile is active.",
                "Pinned Quest properties match.",
                []));

        InvokePrivateVoid(viewModel, "UpdatePinnedBuildStatus");

        Assert.Equal(OperationOutcomeKind.Success, viewModel.PinnedBuildLevel);
        Assert.Equal("Sussex APK matches the pinned hash.", viewModel.PinnedBuildSummary);
        Assert.Contains("Headset software: v63 | build new-build | display new-display", viewModel.PinnedBuildDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("verified baseline", viewModel.PinnedBuildDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recorded Sussex build", viewModel.PinnedBuildSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SoftwareIdentityGate_StaysAdvisoryWhenBaselineSurfacingIsDisabled()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_pinnedBuildLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_pinnedBuildSummary", "Sussex APK matches the pinned hash.");
        SetPrivateField(viewModel, "_pinnedBuildDetail", "Study package: com.Viscereality.SussexExperiment");

        var gate = InvokePrivateMethod<WorkflowGuideGateState>(viewModel, "EvaluateSoftwareIdentityGateState");

        Assert.True(gate.Ready);
        Assert.Equal(OperationOutcomeKind.Success, gate.Level);
        Assert.Equal("Sussex APK matches the pinned hash.", gate.Summary);
        Assert.Contains("Study package: com.Viscereality.SussexExperiment", gate.Detail, StringComparison.Ordinal);
        Assert.Contains("intentionally hidden in the operator shell", gate.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no verified Sussex software baseline", gate.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbContinuityGate_IsReadyWhenWifiAdbIsActiveEvenIfUsbIsStillVisible()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(
            viewModel,
            "_headsetStatus",
            new HeadsetAppStatus(
                IsConnected: true,
                ConnectionLabel: "192.168.0.55:5555",
                DeviceModel: "Quest 3",
                BatteryLevel: 90,
                CpuLevel: 4,
                GpuLevel: 4,
                ForegroundPackageId: "com.Viscereality.SussexExperiment",
                IsTargetInstalled: true,
                IsTargetRunning: false,
                IsTargetForeground: false,
                RemoteOnlyControlEnabled: true,
                Timestamp: DateTimeOffset.UtcNow,
                Summary: "Connected.",
                Detail: "Connected over Wi-Fi ADB.",
                IsWifiAdbTransport: true,
                IsUsbAdbVisible: true,
                VisibleUsbSerial: "1WMHH000000000",
                HeadsetWifiSsid: "SussexLab",
                HeadsetWifiIpAddress: "192.168.0.55",
                HostWifiSsid: "SussexLab",
                WifiSsidMatchesHost: true));

        var gate = InvokePrivateMethod<WorkflowGuideGateState>(viewModel, "BuildUsbDisconnectWorkflowGuideGateState");

        Assert.True(gate.Ready);
        Assert.Equal(OperationOutcomeKind.Success, gate.Level);
        Assert.Equal("Wi-Fi ADB continuity confirmed.", gate.Summary);
        Assert.Contains("advisory only", gate.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbContinuityChecks_KeepWifiTransportSuccessfulWhileUsbVisibilityStaysAdvisory()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_connectionTransportSummary", "ADB over Wi-Fi: on.");
        SetPrivateField(viewModel, "_connectionTransportDetail", "Remote control is currently using the Wi-Fi endpoint.");
        SetPrivateField(
            viewModel,
            "_headsetStatus",
            new HeadsetAppStatus(
                IsConnected: true,
                ConnectionLabel: "192.168.0.55:5555",
                DeviceModel: "Quest 3",
                BatteryLevel: 90,
                CpuLevel: 4,
                GpuLevel: 4,
                ForegroundPackageId: "com.Viscereality.SussexExperiment",
                IsTargetInstalled: true,
                IsTargetRunning: false,
                IsTargetForeground: false,
                RemoteOnlyControlEnabled: true,
                Timestamp: DateTimeOffset.UtcNow,
                Summary: "Connected.",
                Detail: "Connected over Wi-Fi ADB.",
                IsWifiAdbTransport: true,
                IsUsbAdbVisible: true,
                VisibleUsbSerial: "1WMHH000000000"));

        var checks = InvokePrivateMethod<IReadOnlyList<WorkflowGuideCheckItem>>(viewModel, "BuildWorkflowGuideCheckItems", 3);

        Assert.Collection(
            checks,
            transport =>
            {
                Assert.Equal("Current transport", transport.Label);
                Assert.Equal(OperationOutcomeKind.Success, transport.Level);
            },
            usb =>
            {
                Assert.Equal("USB visibility", usb.Label);
                Assert.Equal(OperationOutcomeKind.Warning, usb.Level);
                Assert.Contains("awareness", usb.Detail, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Wi-Fi endpoint", usb.Detail, StringComparison.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public void ProfileReviewChecks_NoLongerIncludeSoftwareIdentityRow()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_deviceProfileLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_deviceProfileSummary", "Pinned Quest device profile is active.");
        SetPrivateField(viewModel, "_deviceProfileDetail", "Pinned Quest properties match.");

        var checks = InvokePrivateMethod<IReadOnlyList<WorkflowGuideCheckItem>>(viewModel, "BuildWorkflowGuideCheckItems", 5);

        Assert.Equal(3, checks.Count);
        Assert.DoesNotContain(checks, item => string.Equals(item.Label, "Software identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LslGuideChecks_ExposeWindowsReturnPathAndExactChannels()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment"));
        SetPrivateField(
            viewModel,
            "_questTwinStatePublisherInventory",
            new QuestTwinStatePublisherInventory(
                OperationOutcomeKind.Success,
                "Expected Quest twin-state outlet is visible on Windows.",
                "Windows can see quest_twin_state / quest.twin.state from the expected Sussex source_id.",
                AnyPublisherVisible: true,
                ExpectedPublisherVisible: true,
                ExpectedSourceId: "viscereality.quest.com-viscereality-sussexexperiment.quest-twin-state.quest-twin-state",
                ExpectedSourceIdPrefix: "viscereality.quest.",
                VisiblePublishers: []));
        SetPrivateField(viewModel, "_questTwinStatePublisherInventoryDetail", "Expected Quest twin-state outlet is visible on Windows.");
        SetPrivateField(viewModel, "_reportedTwinState", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["study.lsl.connected"] = "true",
            ["study.lsl.connected_name"] = "HRV_Biofeedback",
            ["study.lsl.connected_type"] = "HRV"
        });
        SetPrivateField(viewModel, "_lslLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_lslSummary", "LSL input live: HRV_Biofeedback.");
        SetPrivateField(viewModel, "_lslDetail", "The inlet is healthy and matches the study stream target.");
        SetPrivateField(viewModel, "_lslExpectedStreamLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslRuntimeTargetLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslConnectedStreamLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslConnectionStateLabel", "Connected 1, connecting 0, total 1");
        SetPrivateField(viewModel, "_lslStatusLineLabel", "Runtime matched expected inlet.");
        SetPrivateField(viewModel, "_lslEchoStateLabel", "Connected, but this public build does not echo the routed inlet value yet.");
        SetPrivateField(viewModel, "_lslBenchStateLabel", "Windows TEST sender active. Latest local send 0.336 at 13:07:28.");
        SetPrivateField(viewModel, "_pinnedBuildLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_pinnedBuildSummary", "Pinned Sussex APK matches the recorded Sussex build.");
        SetPrivateField(viewModel, "_deviceProfileLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_deviceProfileSummary", "Pinned Quest device profile is active.");
        SetPrivateField(viewModel, "_deviceProfileDetail", "Pinned Quest properties match.");
        SetPrivateField(viewModel, "_liveProximitySelector", "192.168.0.55:5555");
        SetPrivateField(
            viewModel,
            "_lslExpectedUpstreamProbeState",
            new SussexExpectedUpstreamState(
                ExpectedStreamName: "HRV_Biofeedback",
                ExpectedStreamType: "HRV",
                DiscoveryAvailable: true,
                ProbeFailed: false,
                VisibleOnWindows: true,
                VisibleViaCompanionTestSender: true,
                VisibleMatchCount: 1,
                VisibleMatches:
                [
                    new LslVisibleStreamInfo(
                        "HRV_Biofeedback",
                        "HRV",
                        "viscereality.companion.study-shell.test.sussex-test",
                        ChannelCount: 1,
                        SampleRateHz: 0f,
                        CreatedAtSeconds: 0d)
                ],
                Summary: "HRV_Biofeedback / HRV is visible on Windows via the companion TEST sender.",
                Detail: "HRV_Biofeedback / HRV | source_id `viscereality.companion.study-shell.test.sussex-test` | channels 1 | nominal 0 Hz"));
        SetPrivateField(
            viewModel,
            "_liveProximityStatus",
            new QuestProximityStatus(
                Available: true,
                HoldActive: true,
                VirtualState: "CLOSE",
                IsAutosleepDisabled: true,
                HeadsetState: "HEADSET_MOUNTED",
                AutoSleepTimeMs: 0,
                RetrievedAtUtc: DateTimeOffset.UtcNow,
                HoldUntilUtc: null,
                StatusDetail: "keep-awake active"));

        var checks = InvokePrivateMethod<IReadOnlyList<WorkflowGuideCheckItem>>(viewModel, "BuildWorkflowGuideCheckItems", 8);

        Assert.Equal(6, checks.Count);
        Assert.Equal("Headset wake + proximity", checks[0].Label);
        Assert.Equal(OperationOutcomeKind.Success, checks[0].Level);
        Assert.Contains("Keep-awake proximity", checks[0].Detail, StringComparison.Ordinal);
        Assert.Contains("Expected inlet:", checks[1].Detail, StringComparison.Ordinal);
        Assert.Contains("Bench sender:", checks[1].Detail, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine, checks[1].Detail, StringComparison.Ordinal);
        Assert.Contains("Runtime target:", checks[2].Detail, StringComparison.Ordinal);
        Assert.Contains("Counts:", checks[2].Detail, StringComparison.Ordinal);
        Assert.Equal("Windows upstream inventory", checks[3].Label);
        Assert.Equal(OperationOutcomeKind.Success, checks[3].Level);
        Assert.Contains("source_id `viscereality.companion.study-shell.test.sussex-test`", checks[3].Detail, StringComparison.Ordinal);
        Assert.Equal("Windows return path", checks[4].Label);
        Assert.Equal(OperationOutcomeKind.Success, checks[4].Level);
        Assert.Contains("Selector:", checks[4].Detail, StringComparison.Ordinal);
        Assert.Contains("Pinned build:", checks[4].Detail, StringComparison.Ordinal);
        Assert.Contains("Device profile:", checks[4].Detail, StringComparison.Ordinal);
        Assert.Contains("Return path:", checks[4].Detail, StringComparison.Ordinal);
        Assert.Contains("Twin-state outlet:", checks[4].Detail, StringComparison.Ordinal);
        Assert.Contains("quest_twin_state / quest.twin.state", checks[4].Detail, StringComparison.Ordinal);
        Assert.Contains("quest_twin_commands / quest.twin.command", checks[4].Detail, StringComparison.Ordinal);
        Assert.Contains("quest_hotload_config / quest.config", checks[4].Detail, StringComparison.Ordinal);
        Assert.Equal("Potential hazards", checks[5].Label);
        Assert.Equal(OperationOutcomeKind.Success, checks[5].Level);
    }

    [Fact]
    public void LslGuideChecks_SurfaceDuplicateWindowsUpstreamPublishers()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_lslExpectedUpstreamProbeState", new SussexExpectedUpstreamState(
            ExpectedStreamName: "HRV_Biofeedback",
            ExpectedStreamType: "HRV",
            DiscoveryAvailable: true,
            ProbeFailed: false,
            VisibleOnWindows: true,
            VisibleViaCompanionTestSender: true,
            VisibleMatchCount: 2,
            VisibleMatches:
            [
                new LslVisibleStreamInfo(
                    "HRV_Biofeedback",
                    "HRV",
                    "viscereality.companion.study-shell.test.sussex-test",
                    ChannelCount: 1,
                    SampleRateHz: 0f,
                    CreatedAtSeconds: 0d),
                new LslVisibleStreamInfo(
                    "HRV_Biofeedback",
                    "HRV",
                    "python.sender.external",
                    ChannelCount: 1,
                    SampleRateHz: 30f,
                    CreatedAtSeconds: 0d)
            ],
            Summary: "Multiple HRV_Biofeedback / HRV sources are visible on Windows, including the companion TEST sender.",
            Detail: "Visible matches on Windows."));

        var check = InvokePrivateMethod<WorkflowGuideCheckItem>(viewModel, "BuildLslUpstreamInventoryWorkflowGuideCheckItem");

        Assert.Equal(OperationOutcomeKind.Warning, check.Level);
        Assert.Contains("including the companion TEST sender", check.Summary, StringComparison.Ordinal);
        Assert.Contains("python.sender.external", check.Detail, StringComparison.Ordinal);
        Assert.Contains("switching between the companion TEST sender and external sources unreliable", check.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LslGuideChecks_CallOutQuestInletTimeoutInsteadOfStillResolving()
    {
        var study = CreateStudy();
        study = study with
        {
            Monitoring = study.Monitoring with
            {
                ExpectedLslStreamName = "HRV_Biofeedback",
                ExpectedLslStreamType = "HRV"
            }
        };

        var viewModel = CreateViewModel(study);
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment"));
        SetPrivateField(viewModel, "_reportedTwinState", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["study.lsl.filter_name"] = "HRV_Biofeedback",
            ["study.lsl.filter_type"] = "HRV",
            ["study.lsl.status"] = "LSL: connect failed (TimeoutException: The operation failed due to a timeout.)",
            ["connection.lsl.connected_count"] = "0",
            ["connection.lsl.connecting_count"] = "1",
            ["connection.lsl.total_count"] = "2",
            ["signal01.coherence_lsl"] = "0.5"
        });

        InvokePrivateVoid(viewModel, "UpdateLslCard");

        var checks = InvokePrivateMethod<IReadOnlyList<WorkflowGuideCheckItem>>(viewModel, "BuildWorkflowGuideCheckItems", 8);
        var lslReceipt = Assert.Single(checks, item => string.Equals(item.Label, "LSL receipt", StringComparison.Ordinal));
        var streamCheck = Assert.Single(checks, item => string.Equals(item.Label, "Expected vs connected stream", StringComparison.Ordinal));

        Assert.Equal(OperationOutcomeKind.Failure, lslReceipt.Level);
        Assert.Contains("Quest inlet connect failed:", lslReceipt.Summary, StringComparison.Ordinal);
        Assert.Contains("timeout", lslReceipt.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("still resolving", lslReceipt.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Assessment: Quest inlet connect failed:", lslReceipt.Detail, StringComparison.Ordinal);
        Assert.Contains("Connected inlet: n/a / n/a", streamCheck.Detail, StringComparison.Ordinal);
        Assert.Contains("Quest status: LSL: connect failed (TimeoutException: The operation failed due to a timeout.)", streamCheck.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void LslGuideGate_UsesExplicitQuestInletTimeoutSummary()
    {
        var study = CreateStudy();
        study = study with
        {
            Monitoring = study.Monitoring with
            {
                ExpectedLslStreamName = "HRV_Biofeedback",
                ExpectedLslStreamType = "HRV"
            }
        };

        var viewModel = CreateViewModel(study);
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment"));
        SetPrivateField(viewModel, "_reportedTwinState", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["study.lsl.filter_name"] = "HRV_Biofeedback",
            ["study.lsl.filter_type"] = "HRV",
            ["study.lsl.status"] = "LSL: connect failed (TimeoutException: The operation failed due to a timeout.)",
            ["connection.lsl.connected_count"] = "0",
            ["connection.lsl.connecting_count"] = "1",
            ["connection.lsl.total_count"] = "2"
        });

        InvokePrivateVoid(viewModel, "UpdateLslCard");

        var gate = InvokePrivateMethod<WorkflowGuideGateState>(viewModel, "BuildLslWorkflowGuideGateState");
        var returnPath = InvokePrivateMethod<WorkflowGuideCheckItem>(viewModel, "BuildLslReturnPathWorkflowGuideCheckItem");

        Assert.False(gate.Ready);
        Assert.Equal(OperationOutcomeKind.Failure, gate.Level);
        Assert.Contains("failed to subscribe", gate.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Quest inlet connect failed:", gate.Detail, StringComparison.Ordinal);
        Assert.Equal(OperationOutcomeKind.Failure, returnPath.Level);
        Assert.Contains("Quest inlet connect failed:", returnPath.Summary, StringComparison.Ordinal);
        Assert.Contains("quest_twin_state", returnPath.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LslGuideGate_RequiresFreshQuestTwinStateReturnPath()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment"));
        SetPrivateField(viewModel, "_lslLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_lslSummary", "LSL input live: HRV_Biofeedback.");
        SetPrivateField(viewModel, "_lslDetail", "The inlet is healthy and matches the study stream target.");
        SetPrivateField(viewModel, "_lslExpectedStreamLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslRuntimeTargetLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslConnectedStreamLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslConnectionStateLabel", "Connected 1, connecting 0, total 1");
        SetPrivateField(viewModel, "_lslStatusLineLabel", "Runtime matched expected inlet.");
        SetPrivateField(viewModel, "_lslEchoStateLabel", "Connected, but this public build does not echo the routed inlet value yet.");

        var gate = InvokePrivateMethod<WorkflowGuideGateState>(viewModel, "BuildLslWorkflowGuideGateState");

        Assert.False(gate.Ready);
        Assert.Equal(OperationOutcomeKind.Warning, gate.Level);
        Assert.Contains("Selector:", gate.Detail, StringComparison.Ordinal);
        Assert.Contains("Return path:", gate.Detail, StringComparison.Ordinal);
        Assert.Contains("Twin-state outlet:", gate.Detail, StringComparison.Ordinal);
        Assert.Contains("quest_twin_state / quest.twin.state", gate.Detail, StringComparison.Ordinal);
        Assert.Contains("must turn green", gate.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LslGuideGate_StaysBlockedWhenProbeFallsBackToUsbAfterWifiBootstrapAttempt()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(
            viewModel,
            "_headsetStatus",
            CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment") with
            {
                ConnectionLabel = "1WMHH000000000",
                Detail = "Connected over USB ADB.",
                IsWifiAdbTransport = false
            });
        SetPrivateField(viewModel, "_lslLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_lslSummary", "LSL input live: HRV_Biofeedback.");
        SetPrivateField(viewModel, "_lslDetail", "The inlet is healthy and matches the study stream target.");
        SetPrivateField(viewModel, "_lslExpectedStreamLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslRuntimeTargetLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslConnectedStreamLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslConnectionStateLabel", "Connected 1, connecting 0, total 1");
        SetPrivateField(viewModel, "_lslStatusLineLabel", "Runtime matched expected inlet.");
        SetPrivateField(viewModel, "_lslEchoStateLabel", "Connected, but this public build does not echo the routed inlet value yet.");
        SetPrivateField(viewModel, "_pinnedBuildLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_pinnedBuildSummary", "Pinned Sussex APK matches the recorded Sussex build.");
        SetPrivateField(viewModel, "_deviceProfileLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_deviceProfileSummary", "Pinned Quest device profile is active.");
        SetPrivateField(viewModel, "_deviceProfileDetail", "Pinned Quest properties match.");
        SetPrivateField(
            viewModel,
            "_questWifiTransportDiagnostics",
            new QuestWifiTransportDiagnosticsResult(
                OperationOutcomeKind.Warning,
                "Quest is still not on Wi-Fi ADB, so this diagnostic cannot turn green yet.",
                "Automatic Wi-Fi ADB switch attempt: [WARN] Wi-Fi ADB bootstrap started, but the Quest did not stay on the TCP endpoint. Automatic Connect Quest retry: [FAIL] Connect Quest failed. This keeps the diagnostic from turning green until the active Quest transport is Wi-Fi ADB. Keep USB attached, accept any in-headset debugging prompt, and if needed use Connect Quest with 192.168.0.55:5555.",
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
                CheckedAtUtc: DateTimeOffset.UtcNow,
                BootstrapAttempted: true,
                BootstrapSucceeded: false,
                Bootstrap: "Automatic Wi-Fi ADB switch attempt: [WARN] Wi-Fi ADB bootstrap started, but the Quest did not stay on the TCP endpoint."));

        var gate = InvokePrivateMethod<WorkflowGuideGateState>(viewModel, "BuildLslWorkflowGuideGateState");

        Assert.False(gate.Ready);
        Assert.Equal(OperationOutcomeKind.Warning, gate.Level);
        Assert.Contains("Wi-Fi ADB", gate.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot turn green", gate.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Connect Quest with 192.168.0.55:5555", gate.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconnectTargetCheck_WarnsWhenSavedEndpointDiffersFromLiveQuestIp()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment"));
        SetPrivateField(viewModel, "_appSessionState", new AppSessionState("192.168.0.17:5555", null));
        viewModel.EndpointDraft = "192.168.0.17:5555";

        var checks = InvokePrivateMethod<IReadOnlyList<WorkflowGuideCheckItem>>(viewModel, "BuildWorkflowGuideCheckItems", 1);

        Assert.Equal("Reconnect target", checks[1].Label);
        Assert.Equal(OperationOutcomeKind.Warning, checks[1].Level);
        Assert.Contains("stale", checks[1].Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("192.168.0.17:5555", checks[1].Detail, StringComparison.Ordinal);
        Assert.Contains("192.168.0.55:5555", checks[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void WifiMatchChecks_ExposeRouterPathWarning_WhenQuestWifiTransportLooksBlocked()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment"));
        SetPrivateField(
            viewModel,
            "_questWifiTransportDiagnostics",
            new QuestWifiTransportDiagnosticsResult(
                OperationOutcomeKind.Failure,
                "Windows cannot reach the Quest ADB TCP endpoint over the current Wi-Fi path.",
                "Selector 192.168.0.55:5555. Headset Wi-Fi SussexLab (192.168.0.55). Host Wi-Fi SussexLab via Wi-Fi (192.168.0.22). The PC and Quest report the same SSID, but Windows could not open TCP port 5555 on the Quest IP. On the failing router this usually means guest Wi-Fi/client isolation, AP isolation, WLAN peer blocking, or a stale endpoint surviving an ADB restart.",
                Selector: "Selector 192.168.0.55:5555.",
                HeadsetWifi: "Headset Wi-Fi SussexLab (192.168.0.55).",
                HostWifi: "Host Wi-Fi SussexLab via Wi-Fi (192.168.0.22).",
                Topology: "SSIDs match. Selector IP matches the headset Wi-Fi IP. Quest and host appear to share the same IPv4 subnet.",
                Ping: "ICMP ping to the Quest IP timed out across 2 attempt(s).",
                Tcp: "TCP port 5555 on the Quest did not accept a connection from Windows: timed out after 1500 ms",
                TcpReachable: false,
                PingReachable: false,
                SelectorMatchesHeadsetIp: true,
                SameSubnet: true,
                CheckedAtUtc: DateTimeOffset.UtcNow));

        var checks = InvokePrivateMethod<IReadOnlyList<WorkflowGuideCheckItem>>(viewModel, "BuildWorkflowGuideCheckItems", 2);

        Assert.Equal(3, checks.Count);
        Assert.Equal("Wi-Fi router path", checks[2].Label);
        Assert.Equal(OperationOutcomeKind.Failure, checks[2].Level);
        Assert.Contains("client isolation", checks[2].Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WifiMatchGate_Fails_WhenWifiTransportDiagnosticsDetectRouterIsolation()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment"));
        SetPrivateField(
            viewModel,
            "_questWifiTransportDiagnostics",
            new QuestWifiTransportDiagnosticsResult(
                OperationOutcomeKind.Failure,
                "Windows cannot reach the Quest ADB TCP endpoint over the current Wi-Fi path.",
                "The PC and Quest report the same SSID, but TCP port 5555 is blocked.",
                Selector: "Selector 192.168.0.55:5555.",
                HeadsetWifi: "Headset Wi-Fi SussexLab (192.168.0.55).",
                HostWifi: "Host Wi-Fi SussexLab via Wi-Fi (192.168.0.22).",
                Topology: "SSIDs match.",
                Ping: "ICMP ping timed out.",
                Tcp: "TCP port 5555 blocked.",
                TcpReachable: false,
                PingReachable: false,
                SelectorMatchesHeadsetIp: true,
                SameSubnet: true,
                CheckedAtUtc: DateTimeOffset.UtcNow));

        var gate = InvokePrivateMethod<WorkflowGuideGateState>(viewModel, "BuildWifiMatchWorkflowGuideGateState");

        Assert.False(gate.Ready);
        Assert.Equal(OperationOutcomeKind.Failure, gate.Level);
        Assert.Contains("cannot reach", gate.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TCP port 5555", gate.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WifiMatchGate_Passes_WhenQuestIsReachableOverEthernetRoutedHostPath()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment") with
        {
            HostWifiSsid = "OfficeWifi",
            WifiSsidMatchesHost = false
        });
        SetPrivateField(viewModel, "_headsetWifiSummary", "Headset Wi-Fi: SussexLab");
        SetPrivateField(viewModel, "_hostWifiSummary", "This PC Wi-Fi: OfficeWifi");
        SetPrivateField(
            viewModel,
            "_questWifiTransportDiagnostics",
            new QuestWifiTransportDiagnosticsResult(
                OperationOutcomeKind.Success,
                "Quest Wi-Fi path is reachable from Windows over the current wired/router path.",
                "The Quest is on Wi-Fi, but Windows can still reach TCP port 5555 through the current routed host adapter. Matching PC Wi-Fi SSIDs are not required when the companion is on the same router over Ethernet or another valid routed link.",
                Selector: "Selector 192.168.0.55:5555.",
                HeadsetWifi: "Headset Wi-Fi SussexLab (192.168.0.55).",
                HostWifi: "Host routed link via Ethernet (192.168.0.22); PC Wi-Fi SSID OfficeWifi.",
                Topology: "SSIDs do not match. Selector IP matches the headset Wi-Fi IP. Host adapter Ethernet (Ethernet) IPv4 192.168.0.22, gateway 192.168.0.1. Quest and host appear to share the same IPv4 subnet.",
                Ping: "ICMP ping reached the Quest.",
                Tcp: "TCP port 5555 on the Quest is reachable from Windows.",
                TcpReachable: true,
                PingReachable: true,
                SelectorMatchesHeadsetIp: true,
                SameSubnet: true,
                CheckedAtUtc: DateTimeOffset.UtcNow,
                RoutedTopologyAccepted: true));

        var gate = InvokePrivateMethod<WorkflowGuideGateState>(viewModel, "BuildWifiMatchWorkflowGuideGateState");

        Assert.True(gate.Ready);
        Assert.Equal(OperationOutcomeKind.Success, gate.Level);
        Assert.Contains("router path", gate.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not required", gate.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("change the headset Wi-Fi manually", gate.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WifiMatchGate_Passes_WhenQuestIsReachableOverEthernetRoutedHostPathAndWifiNamesAreUnavailable()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment") with
        {
            HostWifiSsid = string.Empty,
            WifiSsidMatchesHost = null,
            HostWifiInterfaceName = "Ethernet"
        });
        SetPrivateField(viewModel, "_headsetWifiSummary", "Headset Wi-Fi: TP-Link_COAC");
        SetPrivateField(viewModel, "_hostWifiSummary", "This PC Wi-Fi: n/a");
        SetPrivateField(
            viewModel,
            "_questWifiTransportDiagnostics",
            new QuestWifiTransportDiagnosticsResult(
                OperationOutcomeKind.Success,
                "Quest Wi-Fi path is reachable from Windows over the current wired/router path.",
                "Selector 192.168.0.130:5555. Headset Wi-Fi TP-Link_COAC (192.168.0.130). Host routed link via Ethernet (192.168.0.153); PC Wi-Fi SSID n/a. Host is using a routed non-Wi-Fi adapter, so direct PC Wi-Fi SSID matching is not required. Selector IP matches the headset Wi-Fi IP. Host adapter Ethernet (Ethernet) IPv4 192.168.0.153, gateway 192.168.0.1. Quest and host appear to share the same IPv4 subnet. ICMP ping reached the Quest across 2/2 reply/replies (12.5 ms avg). TCP port 5555 on the Quest is reachable from Windows (6.8 ms). The Quest is on Wi-Fi, but Windows can still reach TCP port 5555 through the current routed host adapter. Matching PC Wi-Fi SSIDs are not required when the companion is on the same router over Ethernet or another valid routed link.",
                Selector: "Selector 192.168.0.130:5555.",
                HeadsetWifi: "Headset Wi-Fi TP-Link_COAC (192.168.0.130).",
                HostWifi: "Host routed link via Ethernet (192.168.0.153); PC Wi-Fi SSID n/a.",
                Topology: "Host is using a routed non-Wi-Fi adapter, so direct PC Wi-Fi SSID matching is not required. Selector IP matches the headset Wi-Fi IP. Host adapter Ethernet (Ethernet) IPv4 192.168.0.153, gateway 192.168.0.1. Quest and host appear to share the same IPv4 subnet.",
                Ping: "ICMP ping reached the Quest across 2/2 reply/replies (12.5 ms avg).",
                Tcp: "TCP port 5555 on the Quest is reachable from Windows (6.8 ms).",
                TcpReachable: true,
                PingReachable: true,
                SelectorMatchesHeadsetIp: true,
                SameSubnet: true,
                CheckedAtUtc: DateTimeOffset.UtcNow,
                RoutedTopologyAccepted: true));

        var gate = InvokePrivateMethod<WorkflowGuideGateState>(viewModel, "BuildWifiMatchWorkflowGuideGateState");

        Assert.True(gate.Ready);
        Assert.Equal(OperationOutcomeKind.Success, gate.Level);
        Assert.Equal("Quest router path is valid.", gate.Summary);
        Assert.Contains("not required", gate.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Refresh the headset snapshot until both the headset Wi-Fi and the PC Wi-Fi names are visible", gate.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LslReturnPathCheck_DistinguishesStalledTwinStatePublisherFromGenericConnectionFailure()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment"));
        SetPrivateField(viewModel, "_reportedTwinState", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["study.lsl.connected"] = "true",
            ["study.lsl.connected_name"] = "HRV_Biofeedback",
            ["study.lsl.connected_type"] = "HRV"
        });
        SetPrivateField(viewModel, "_lslLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_lslExpectedStreamLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslRuntimeTargetLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslConnectedStreamLabel", "HRV_Biofeedback / HRV");
        SetPrivateField(viewModel, "_lslConnectionStateLabel", "Connected 1, connecting 0, total 1");
        SetPrivateField(viewModel, "_lslStatusLineLabel", "LSL: connected HRV_Biofeedback (HRV), ch=1");
        SetPrivateField(viewModel, "_lslEchoStateLabel", "Current Quest echo 0.466 via study.lsl.latest_default_value in frame 11:09:24.");
        SetPrivateField(viewModel, "_pinnedBuildLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_pinnedBuildSummary", "Pinned Sussex APK matches the recorded Sussex build.");
        SetPrivateField(viewModel, "_deviceProfileLevel", OperationOutcomeKind.Success);
        SetPrivateField(viewModel, "_deviceProfileSummary", "Pinned Quest device profile is active.");
        SetPrivateField(viewModel, "_deviceProfileDetail", "Pinned Quest properties match.");
        var bridge = new LslTwinModeBridge(new PreviewLslOutletService(), new PreviewLslOutletService(), new PreviewLslMonitorService());
        SetPrivateField(bridge, "_lastStateReceivedAt", DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));
        SetPrivateField(viewModel, "_twinBridge", bridge);
        SetPrivateField(
            viewModel,
            "_questTwinStatePublisherInventory",
            new QuestTwinStatePublisherInventory(
                OperationOutcomeKind.Warning,
                "No Quest twin-state outlet is visible on Windows.",
                "quest_twin_state / quest.twin.state did not appear in Windows LSL discovery.",
                AnyPublisherVisible: false,
                ExpectedPublisherVisible: false,
                ExpectedSourceId: "viscereality.quest.com-viscereality-sussexexperiment.quest-twin-state.quest-twin-state",
                ExpectedSourceIdPrefix: "viscereality.quest.",
                VisiblePublishers: []));
        SetPrivateField(viewModel, "_questTwinStatePublisherInventoryDetail", "No Quest twin-state outlet is visible on Windows.");

        var check = InvokePrivateMethod<WorkflowGuideCheckItem>(viewModel, "BuildLslReturnPathWorkflowGuideCheckItem");

        Assert.Equal(OperationOutcomeKind.Warning, check.Level);
        Assert.Contains("publisher stalled or became undiscoverable", check.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("separate failure mode", check.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LslGuideActions_IncludeWindowsEnvironmentAnalysisButton()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_testLslSignalService", new PreviewTestLslSignalService());
        SetPrivateField(viewModel, "_testLslSenderActionLabel", "Start TEST Sender");
        SetPrivateAutoProperty(viewModel, nameof(StudyShellViewModel.ToggleTestLslSenderCommand), new AsyncRelayCommand(() => Task.CompletedTask));
        SetPrivateAutoProperty(viewModel, nameof(StudyShellViewModel.ProbeLslConnectionCommand), new AsyncRelayCommand(() => Task.CompletedTask));
        SetPrivateAutoProperty(viewModel, nameof(StudyShellViewModel.AnalyzeWindowsEnvironmentCommand), new AsyncRelayCommand(() => Task.CompletedTask));

        var actions = InvokePrivateMethod<IReadOnlyList<WorkflowGuideActionItem>>(viewModel, "BuildWorkflowGuideActionItems", 8);

        Assert.Equal(3, actions.Count);
        Assert.Contains(actions, action => string.Equals(action.Label, "Analyze Windows Environment", StringComparison.Ordinal));
    }

    [Fact]
    public void StudyRuntimeLaunch_IsBlockedAndRelabeled_WhenHeadsetIsAsleep()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(
            viewModel,
            "_headsetStatus",
            CreateConnectedHeadsetStatus("com.oculus.vrshell") with
            {
                IsTargetForeground = false,
                IsTargetRunning = false,
                IsAwake = false,
                Wakefulness = "Asleep",
                DisplayPowerState = "OFF"
            });

        Assert.True(viewModel.IsLaunchBlockedBySleepingHeadset);
        Assert.False(viewModel.CanLaunchStudyRuntime);
        Assert.False(viewModel.CanToggleStudyRuntime);
        Assert.Equal("Wake Headset To Enable Launching", viewModel.StudyRuntimeActionLabel);
    }

    [Fact]
    public void WorkflowGuideLaunchAction_UsesWakePromptAndDisablesLaunch_WhenHeadsetIsAsleep()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(
            viewModel,
            "_headsetStatus",
            CreateConnectedHeadsetStatus("com.oculus.vrshell") with
            {
                IsTargetForeground = false,
                IsTargetRunning = false,
                IsAwake = false,
                Wakefulness = "Asleep",
                DisplayPowerState = "OFF"
            });
        SetPrivateAutoProperty(viewModel, nameof(StudyShellViewModel.LaunchStudyAppCommand), new AsyncRelayCommand(() => Task.CompletedTask));
        SetPrivateAutoProperty(viewModel, nameof(StudyShellViewModel.RefreshDeviceSnapshotCommand), new AsyncRelayCommand(() => Task.CompletedTask));
        SetPrivateAutoProperty(viewModel, nameof(StudyShellViewModel.ToggleProximityCommand), new AsyncRelayCommand(() => Task.CompletedTask));

        var actions = InvokePrivateMethod<IReadOnlyList<WorkflowGuideActionItem>>(viewModel, "BuildWorkflowGuideActionItems", 7);

        Assert.Equal(3, actions.Count);
        Assert.Equal("Disable Proximity", actions[0].Label);
        Assert.True(actions[0].IsEnabled);
        Assert.Equal("Wake Headset To Enable Launching", actions[1].Label);
        Assert.False(actions[1].IsEnabled);
        Assert.True(actions[2].IsEnabled);
    }

    [Fact]
    public void StudyRuntimeLaunch_IsBlockedAndRelabeled_WhenMetaVisualBlockerIsActive()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(
            viewModel,
            "_headsetStatus",
            CreateConnectedHeadsetStatus("com.oculus.guardian") with
            {
                IsTargetForeground = false,
                IsTargetRunning = false,
                IsAwake = true,
                IsInWakeLimbo = true,
                ForegroundComponent = "com.oculus.guardian/com.oculus.vrguardianservice.guardiandialog.GuardianDialogActivity"
            });

        Assert.True(viewModel.IsLaunchBlockedByHeadsetVisualBlocker);
        Assert.False(viewModel.CanLaunchStudyRuntime);
        Assert.False(viewModel.CanToggleStudyRuntime);
        Assert.Equal("Clear Guardian Blocker Before Launching", viewModel.StudyRuntimeActionLabel);
    }

    [Fact]
    public void StudyRuntimeLaunch_IsBlockedAndRelabeled_WhenLockScreenIsForeground()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(
            viewModel,
            "_headsetStatus",
            CreateConnectedHeadsetStatus("com.oculus.os.vrlockscreen") with
            {
                IsTargetForeground = false,
                IsTargetRunning = false,
                IsAwake = false,
                IsInWakeLimbo = true,
                ForegroundComponent = "com.oculus.os.vrlockscreen/.SensorLockActivity"
            });

        Assert.True(viewModel.IsLaunchBlockedByHeadsetLockScreen);
        Assert.False(viewModel.IsLaunchBlockedByHeadsetVisualBlocker);
        Assert.False(viewModel.CanLaunchStudyRuntime);
        Assert.False(viewModel.CanToggleStudyRuntime);
        Assert.Equal("Clear Lock Screen Before Launching", viewModel.StudyRuntimeActionLabel);
    }

    [Fact]
    public void UpdateProximityCard_ReportsDirectProxCloseAsActiveOverride()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_hzdbService", new StubHzdbService(isAvailable: true));
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment"));
        SetPrivateField(viewModel, "_liveProximitySelector", "192.168.0.55:5555");
        SetPrivateField(
            viewModel,
            "_liveProximityStatus",
            new QuestProximityStatus(
                Available: true,
                HoldActive: true,
                VirtualState: "CLOSE",
                IsAutosleepDisabled: false,
                HeadsetState: "HEADSET_MOUNTED",
                AutoSleepTimeMs: 15000,
                RetrievedAtUtc: DateTimeOffset.UtcNow,
                HoldUntilUtc: null,
                StatusDetail: "Read from adb shell dumpsys vrpowermanager.",
                LastBroadcastAction: "prox_close",
                LastBroadcastDurationMs: 0,
                LastBroadcastAgeSeconds: 2));

        InvokePrivateVoid(viewModel, "UpdateProximityCard");

        Assert.Equal(OperationOutcomeKind.Success, viewModel.ProximityLevel);
        Assert.Equal("Enable Proximity", viewModel.ProximityActionLabel);
        Assert.Equal("Keep-awake proximity override is active.", viewModel.ProximitySummary);
        Assert.Contains("direct prox_close override", viewModel.ProximityDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateProximityCard_ReportsAutomationDisableAsNormalSensorBehavior()
    {
        var viewModel = CreateViewModel(CreateStudy());
        SetPrivateField(viewModel, "_hzdbService", new StubHzdbService(isAvailable: true));
        SetPrivateField(viewModel, "_headsetStatus", CreateConnectedHeadsetStatus("com.Viscereality.SussexExperiment"));
        SetPrivateField(viewModel, "_liveProximitySelector", "192.168.0.55:5555");
        SetPrivateField(
            viewModel,
            "_liveProximityStatus",
            new QuestProximityStatus(
                Available: true,
                HoldActive: false,
                VirtualState: "DISABLED",
                IsAutosleepDisabled: false,
                HeadsetState: "HEADSET_MOUNTED",
                AutoSleepTimeMs: 15000,
                RetrievedAtUtc: DateTimeOffset.UtcNow,
                HoldUntilUtc: null,
                StatusDetail: "Read from adb shell dumpsys vrpowermanager.",
                LastBroadcastAction: "automation_disable",
                LastBroadcastDurationMs: 0,
                LastBroadcastAgeSeconds: 2));

        InvokePrivateVoid(viewModel, "UpdateProximityCard");

        Assert.Equal(OperationOutcomeKind.Success, viewModel.ProximityLevel);
        Assert.Equal("Disable Proximity", viewModel.ProximityActionLabel);
        Assert.Equal("Normal proximity sensor behavior is active.", viewModel.ProximitySummary);
        Assert.Contains("physical wear sensor is in charge again", viewModel.ProximityDetail, StringComparison.Ordinal);
    }

    private static StudyShellViewModel CreateViewModel(StudyShellDefinition study)
    {
        var viewModel = (StudyShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(StudyShellViewModel));
        SetPrivateField(viewModel, "_study", study);
        SetPrivateField(viewModel, "_appSessionState", new AppSessionState(null, null));
        SetPrivateField(viewModel, "_reportedTwinState", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        return viewModel;
    }

    private static StudyShellDefinition CreateStudy(StudyVerificationBaseline? baseline = null)
        => new(
            "sussex-test",
            "Sussex Test",
            "Sussex",
            "Study shell regression-test definition.",
            new StudyPinnedApp(
                "Sussex",
                "com.Viscereality.SussexExperiment",
                string.Empty,
                string.Empty,
                "apk-hash",
                "1.0.0",
                string.Empty,
                AllowManualSelection: true,
                LaunchInKioskMode: true,
                VerificationBaseline: baseline),
            new StudyPinnedDeviceProfile(
                "sussex-profile",
                "Sussex Device Profile",
                string.Empty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new StudyMonitoringProfile(
                ExpectedBreathingLabel: string.Empty,
                ExpectedHeartbeatLabel: string.Empty,
                ExpectedCoherenceLabel: string.Empty,
                ExpectedLslStreamName: string.Empty,
                ExpectedLslStreamType: string.Empty,
                RecenterDistanceThresholdUnits: 0d,
                LslConnectivityKeys: [],
                LslStreamNameKeys: [],
                LslStreamTypeKeys: [],
                LslValueKeys: [],
                ControllerValueKeys: [],
                ControllerStateKeys: [],
                ControllerTrackingKeys: [],
                AutomaticBreathingValueKeys: [],
                HeartbeatValueKeys: [],
                HeartbeatStateKeys: [],
                CoherenceValueKeys: [],
                CoherenceStateKeys: [],
                PerformanceFpsKeys: [],
                PerformanceFrameTimeKeys: [],
                PerformanceTargetFpsKeys: [],
                PerformanceRefreshRateKeys: [],
                RecenterDistanceKeys: [],
                ParticleVisibilityKeys: []),
            new StudyControlProfile(
                RecenterCommandActionId: "recenter",
                ParticleVisibleOnActionId: "particles.on",
                ParticleVisibleOffActionId: "particles.off"));

    private static StudyVerificationBaseline CreateBaseline()
        => new(
            ApkSha256: "apk-hash",
            SoftwareVersion: "v62",
            BuildId: "old-build",
            DisplayId: "old-display",
            DeviceProfileId: "sussex-profile",
            EnvironmentHash: "env-hash",
            VerifiedAtUtc: DateTimeOffset.UtcNow.AddDays(-1),
            VerifiedBy: "regression-test");

    private static HeadsetAppStatus CreateConnectedHeadsetStatus(string foregroundPackageId)
        => new(
            IsConnected: true,
            ConnectionLabel: "192.168.0.55:5555",
            DeviceModel: "Quest 3",
            BatteryLevel: 90,
            CpuLevel: 4,
            GpuLevel: 4,
            ForegroundPackageId: foregroundPackageId,
            IsTargetInstalled: true,
            IsTargetRunning: true,
            IsTargetForeground: true,
            RemoteOnlyControlEnabled: true,
            Timestamp: DateTimeOffset.UtcNow,
            Summary: "Connected.",
            Detail: "Connected over Wi-Fi ADB.",
            IsWifiAdbTransport: true,
            HeadsetWifiSsid: "SussexLab",
            HeadsetWifiIpAddress: "192.168.0.55",
            HostWifiSsid: "SussexLab",
            WifiSsidMatchesHost: true);

    private static T InvokePrivateMethod<T>(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException($"Could not find method {methodName}.");
        return (T)(method.Invoke(target, args) ?? throw new InvalidOperationException($"Method {methodName} returned null."));
    }

    private static void InvokePrivateVoid(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException($"Could not find method {methodName}.");
        method.Invoke(target, args);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"Could not find field {fieldName}.");
        field.SetValue(target, value);
    }

    private static void SetPrivateAutoProperty(object target, string propertyName, object value)
    {
        var field = target.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"Could not find auto-property backing field for {propertyName}.");
        field.SetValue(target, value);
    }

    private sealed class StubHzdbService(bool isAvailable) : IHzdbService
    {
        public bool IsAvailable => isAvailable;

        public Task<OperationOutcome> CaptureScreenshotAsync(string deviceSerial, string outputPath, string? method = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> CapturePerfTraceAsync(string deviceSerial, int durationMs = 5000, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> SetProximityAsync(string deviceSerial, bool enabled, int? durationMs = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QuestProximityStatus> GetProximityStatusAsync(string deviceSerial, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> WakeDeviceAsync(string deviceSerial, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> GetDeviceInfoAsync(string deviceSerial, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> ListFilesAsync(string deviceSerial, string remotePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> PushFileAsync(string deviceSerial, string localPath, string remotePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperationOutcome> PullFileAsync(string deviceSerial, string remotePath, string localPath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
