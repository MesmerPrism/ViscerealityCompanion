using System.Reflection;
using System.Runtime.CompilerServices;
using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class StudyShellWorkflowGuideRegressionTests
{
    [Fact]
    public void PinnedBuildStatus_TreatsSoftwareIdentityMismatchAsAdvisoryWhenPinnedApkMatches()
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
        Assert.Equal("Pinned Sussex APK matches the recorded Sussex build.", viewModel.PinnedBuildSummary);
        Assert.Contains("Current headset OS/build differs from the verified baseline.", viewModel.PinnedBuildDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("Headset OS does not match", viewModel.PinnedBuildSummary, StringComparison.OrdinalIgnoreCase);
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

        var checks = InvokePrivateMethod<IReadOnlyList<WorkflowGuideCheckItem>>(viewModel, "BuildWorkflowGuideCheckItems", 8);

        Assert.Equal(3, checks.Count);
        Assert.Contains("Expected inlet:", checks[0].Detail, StringComparison.Ordinal);
        Assert.Contains("Bench sender:", checks[0].Detail, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine, checks[0].Detail, StringComparison.Ordinal);
        Assert.Contains("Runtime target:", checks[1].Detail, StringComparison.Ordinal);
        Assert.Contains("Counts:", checks[1].Detail, StringComparison.Ordinal);
        Assert.Equal("Windows return path", checks[2].Label);
        Assert.Equal(OperationOutcomeKind.Success, checks[2].Level);
        Assert.Contains("Selector:", checks[2].Detail, StringComparison.Ordinal);
        Assert.Contains("Return path:", checks[2].Detail, StringComparison.Ordinal);
        Assert.Contains("quest_twin_state / quest.twin.state", checks[2].Detail, StringComparison.Ordinal);
        Assert.Contains("quest_twin_commands / quest.twin.command", checks[2].Detail, StringComparison.Ordinal);
        Assert.Contains("quest_hotload_config / quest.config", checks[2].Detail, StringComparison.Ordinal);
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
        Assert.Contains("quest_twin_state / quest.twin.state", gate.Detail, StringComparison.Ordinal);
        Assert.Contains("must turn green", gate.Detail, StringComparison.OrdinalIgnoreCase);
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
}
