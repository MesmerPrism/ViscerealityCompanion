using System.Runtime.CompilerServices;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public interface IQuestControlService
{
    Task<OperationOutcome> ProbeUsbAsync(CancellationToken cancellationToken = default);
    Task<OperationOutcome> DiscoverWifiAsync(CancellationToken cancellationToken = default);
    Task<OperationOutcome> EnableWifiFromUsbAsync(CancellationToken cancellationToken = default);
    Task<OperationOutcome> ConnectAsync(string endpoint, CancellationToken cancellationToken = default);
    Task<OperationOutcome> ApplyPerformanceLevelsAsync(int cpuLevel, int gpuLevel, CancellationToken cancellationToken = default);
    Task<OperationOutcome> InstallAppAsync(QuestAppTarget target, CancellationToken cancellationToken = default);
    Task<OperationOutcome> InstallBundleAsync(
        QuestBundle bundle,
        IReadOnlyList<QuestAppTarget> targets,
        CancellationToken cancellationToken = default);
    Task<OperationOutcome> ApplyHotloadProfileAsync(
        HotloadProfile profile,
        QuestAppTarget target,
        CancellationToken cancellationToken = default);
    Task<OperationOutcome> ApplyDeviceProfileAsync(DeviceProfile profile, CancellationToken cancellationToken = default);
    Task<OperationOutcome> LaunchAppAsync(QuestAppTarget target, CancellationToken cancellationToken = default);
    Task<OperationOutcome> OpenBrowserAsync(
        string url,
        QuestAppTarget browserTarget,
        CancellationToken cancellationToken = default);
    Task<OperationOutcome> QueryForegroundAsync(CancellationToken cancellationToken = default);
    Task<InstalledAppStatus> QueryInstalledAppAsync(
        QuestAppTarget target,
        CancellationToken cancellationToken = default);
    Task<DeviceProfileStatus> QueryDeviceProfileStatusAsync(
        DeviceProfile profile,
        CancellationToken cancellationToken = default);
    Task<HeadsetAppStatus> QueryHeadsetStatusAsync(
        QuestAppTarget? target,
        bool remoteOnlyControlEnabled,
        CancellationToken cancellationToken = default);
    Task<OperationOutcome> RunUtilityAsync(QuestUtilityAction action, CancellationToken cancellationToken = default);
}

public interface ILslMonitorService
{
    IAsyncEnumerable<LslMonitorReading> MonitorAsync(
        LslMonitorSubscription subscription,
        CancellationToken cancellationToken = default);
}

public interface ITwinModeBridge
{
    TwinBridgeStatus Status { get; }

    Task<OperationOutcome> SendCommandAsync(
        TwinModeCommand command,
        CancellationToken cancellationToken = default);

    Task<OperationOutcome> ApplyConfigAsync(
        HotloadProfile profile,
        QuestAppTarget target,
        CancellationToken cancellationToken = default);

    Task<OperationOutcome> PublishRuntimeConfigAsync(
        RuntimeConfigProfile profile,
        QuestAppTarget target,
        CancellationToken cancellationToken = default);
}

public sealed class PreviewQuestControlService : IQuestControlService
{
    public Task<OperationOutcome> ProbeUsbAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            "USB ADB probe prepared.",
            "This scaffold is wired for the Quest Session Kit control flow, but the public repo ships a preview transport only."));

    public Task<OperationOutcome> DiscoverWifiAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            "Wi-Fi ADB discovery prepared.",
            "The public shell can expose discovery and reconnect workflow here once the desktop ADB backend is attached."));

    public Task<OperationOutcome> EnableWifiFromUsbAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            "Wi-Fi ADB bootstrap prepared.",
            "Wire the real ADB bootstrap implementation here once the desktop transport is ready."));

    public Task<OperationOutcome> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            $"Quest endpoint recorded: {endpoint}.",
            "The public shell stores the endpoint and session metadata, but does not ship the live Quest transport.",
            Endpoint: endpoint));

    public Task<OperationOutcome> ApplyPerformanceLevelsAsync(int cpuLevel, int gpuLevel, CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            $"Performance levels prepared: CPU {cpuLevel}, GPU {gpuLevel}.",
            "Replace the preview control service with the Windows ADB implementation to write Quest performance properties."));

    public Task<OperationOutcome> InstallAppAsync(QuestAppTarget target, CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            $"Install queued for {target.Label}.",
            $"Expected APK payload: {target.ApkFile}",
            PackageId: target.PackageId));

    public Task<OperationOutcome> InstallBundleAsync(
        QuestBundle bundle,
        IReadOnlyList<QuestAppTarget> targets,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            $"Bundle queued: {bundle.Label}.",
            $"Ordered install list: {string.Join(", ", targets.Select(target => target.Label))}",
            Items: targets.Select(target => target.PackageId).ToArray()));

    public Task<OperationOutcome> ApplyHotloadProfileAsync(
        HotloadProfile profile,
        QuestAppTarget target,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            $"Runtime preset prepared: {profile.Label}.",
            $"CSV payload {profile.File} would be pushed to {target.PackageId}.",
            PackageId: target.PackageId));

    public Task<OperationOutcome> ApplyDeviceProfileAsync(DeviceProfile profile, CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            $"Device profile prepared: {profile.Label}.",
            $"The preview transport would write {profile.Properties.Count} debug/system properties over ADB.",
            Items: profile.Properties.Select(pair => $"{pair.Key}={pair.Value}").ToArray()));

    public Task<OperationOutcome> LaunchAppAsync(QuestAppTarget target, CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            $"Launch queued for {target.Label}.",
            string.IsNullOrWhiteSpace(target.LaunchComponent)
                ? "The target does not define an explicit component; the transport would launch by package id."
                : $"Launch component: {target.LaunchComponent}",
            PackageId: target.PackageId));

    public Task<OperationOutcome> OpenBrowserAsync(
        string url,
        QuestAppTarget browserTarget,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            $"Browser open prepared for {browserTarget.Label}.",
            $"Target URL: {url}",
            PackageId: browserTarget.PackageId));

    public Task<OperationOutcome> QueryForegroundAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            "Foreground package query prepared.",
            "The preview transport returns no live package id until a desktop ADB backend is attached."));

    public Task<InstalledAppStatus> QueryInstalledAppAsync(
        QuestAppTarget target,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new InstalledAppStatus(
            target.PackageId,
            IsInstalled: true,
            VersionName: "preview",
            VersionCode: "0",
            InstalledSha256: target.ApkSha256 ?? string.Empty,
            InstalledPath: string.Empty,
            Summary: $"Preview install state available for {target.Label}.",
            Detail: "Attach the Windows ADB backend to verify the installed package path and hash on the headset."));

    public Task<DeviceProfileStatus> QueryDeviceProfileStatusAsync(
        DeviceProfile profile,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new DeviceProfileStatus(
            profile.Id,
            profile.Label,
            IsActive: false,
            Summary: $"Preview device-profile check for {profile.Label}.",
            Detail: "Attach the Windows ADB backend to read the current Quest properties and compare them against the pinned study profile.",
            Properties: profile.Properties
                .Select(pair => new DevicePropertyStatus(pair.Key, pair.Value, string.Empty, Matches: false))
                .ToArray()));

    public Task<HeadsetAppStatus> QueryHeadsetStatusAsync(
        QuestAppTarget? target,
        bool remoteOnlyControlEnabled,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new HeadsetAppStatus(
            IsConnected: false,
            ConnectionLabel: "Preview transport only.",
            DeviceModel: "Quest preview",
            BatteryLevel: 76,
            CpuLevel: 2,
            GpuLevel: 2,
            ForegroundPackageId: target?.PackageId ?? "com.Viscereality.LslTwin",
            IsTargetInstalled: target is not null,
            IsTargetRunning: target is not null,
            IsTargetForeground: target is not null,
            RemoteOnlyControlEnabled: remoteOnlyControlEnabled,
            Timestamp: DateTimeOffset.UtcNow,
            Summary: "Preview status available.",
            Detail: "Attach the Windows ADB backend to read live foreground, install state, battery, and CPU/GPU levels from the headset."));

    public Task<OperationOutcome> RunUtilityAsync(QuestUtilityAction action, CancellationToken cancellationToken = default)
        => Task.FromResult(Preview(
            $"{action} command queued.",
            "The public shell keeps the operator workflow and command contract visible without bundling live Quest control code."));

    private static OperationOutcome Preview(
        string summary,
        string detail,
        string? Endpoint = null,
        string? PackageId = null,
        IReadOnlyList<string>? Items = null)
        => new(OperationOutcomeKind.Preview, summary, detail, Endpoint, PackageId, Items);
}

public sealed class PreviewLslMonitorService : ILslMonitorService
{
    public async IAsyncEnumerable<LslMonitorReading> MonitorAsync(
        LslMonitorSubscription subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new LslMonitorReading(
            "Resolving LSL stream...",
            $"Searching for `{subscription.StreamName}` / `{subscription.StreamType}` channel {subscription.ChannelIndex} in preview mode.",
            null,
            0f,
            DateTimeOffset.UtcNow);

        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);

        yield return new LslMonitorReading(
            "LSL stream connected.",
            "Preview bridge attached. Replace PreviewLslMonitorService with a real liblsl-backed monitor when the desktop runtime is ready.",
            0.42f,
            30f,
            DateTimeOffset.UtcNow);

        var startedAt = DateTime.UtcNow;
        while (!cancellationToken.IsCancellationRequested)
        {
            var elapsedSeconds = (DateTime.UtcNow - startedAt).TotalSeconds;
            var value = 0.52f + (float)(Math.Sin(elapsedSeconds * 1.3d) * 0.26d);

            yield return new LslMonitorReading(
                "Streaming LSL sample.",
                $"Preview sample for `{subscription.StreamName}` / `{subscription.StreamType}`.",
                Math.Clamp(value, 0f, 1f),
                30f,
                DateTimeOffset.UtcNow);

            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class UnavailablePrivateTwinModeBridge : ITwinModeBridge
{
    public TwinBridgeStatus Status { get; } = new(
        IsAvailable: false,
        UsesPrivateImplementation: false,
        Summary: "Private twin bridge not installed.",
        Detail: "The public repo keeps the Astral twin command/state contract, hotload keyspace, and runtime-config tracking surface visible. Only the live coupling dynamics runtime and any private transport overlay stay out of the public repo.");

    public Task<OperationOutcome> SendCommandAsync(
        TwinModeCommand command,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new OperationOutcome(
            OperationOutcomeKind.Preview,
            $"Twin command prepared: {command.DisplayName}.",
            "No private twin implementation is currently loaded. The action contract is public; the live backend stays private."));

    public Task<OperationOutcome> ApplyConfigAsync(
        HotloadProfile profile,
        QuestAppTarget target,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new OperationOutcome(
            OperationOutcomeKind.Preview,
            $"Twin preset prepared: {profile.Label}.",
            $"The public repo can select `{profile.File}` for {target.PackageId}, but the private broadcast path is intentionally excluded.",
            PackageId: target.PackageId));

    public Task<OperationOutcome> PublishRuntimeConfigAsync(
        RuntimeConfigProfile profile,
        QuestAppTarget target,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new OperationOutcome(
            OperationOutcomeKind.Preview,
            $"Runtime config prepared: {profile.Label}.",
            $"The public repo can edit and stage the Astral runtime hotload contract for {target.PackageId}. The private overlay is limited to attaching the live coupling runtime handoff.",
            PackageId: target.PackageId,
            Items:
            [
                $"profile={profile.Id}",
                $"entries={profile.Entries.Count}"
            ]));
}
