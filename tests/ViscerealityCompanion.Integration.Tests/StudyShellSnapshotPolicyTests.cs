using ViscerealityCompanion.App;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class StudyShellSnapshotPolicyTests
{
    [Fact]
    public void NormalizeStartupSessionState_ClearsPersistedRegularAdbSnapshots()
    {
        var sessionState = new AppSessionState(
            ActiveEndpoint: "192.168.2.56:5555",
            LastUsbSerial: "usb",
            LastProximitySelector: "192.168.2.56:5555",
            LastProximityExpectedEnabled: false,
            LastProximityDisableUntilUtc: DateTimeOffset.UtcNow,
            LastProximityUpdatedAtUtc: DateTimeOffset.UtcNow,
            RegularAdbSnapshotEnabled: true);

        var normalized = StudyShellViewModel.NormalizeStartupSessionState(
            sessionState,
            out var clearedPersistedRegularSnapshots);

        Assert.True(clearedPersistedRegularSnapshots);
        Assert.False(normalized.RegularAdbSnapshotEnabled);
        Assert.Equal(sessionState.ActiveEndpoint, normalized.ActiveEndpoint);
    }

    [Fact]
    public void ShouldRefreshInstalledAppStatusForSnapshot_RefreshesWhenForced()
    {
        var currentStatus = new InstalledAppStatus(
            "com.Viscereality.SussexExperiment",
            IsInstalled: true,
            VersionName: "0.1.0",
            VersionCode: "1",
            InstalledSha256: "hash",
            InstalledPath: "/data/app/base.apk",
            Summary: "Installed.",
            Detail: "detail");

        var shouldRefresh = StudyShellViewModel.ShouldRefreshInstalledAppStatusForSnapshot(
            forceRefresh: true,
            currentStatus,
            currentStagedApkPath: @"C:\staged\SussexExperiment.apk",
            lastQueriedStagedApkPath: @"C:\staged\SussexExperiment.apk");

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshInstalledAppStatusForSnapshot_SkipsAutomaticRefreshWhenStatusAndStagedApkAreUnchanged()
    {
        var currentStatus = new InstalledAppStatus(
            "com.Viscereality.SussexExperiment",
            IsInstalled: true,
            VersionName: "0.1.0",
            VersionCode: "1",
            InstalledSha256: "hash",
            InstalledPath: "/data/app/base.apk",
            Summary: "Installed.",
            Detail: "detail");

        var shouldRefresh = StudyShellViewModel.ShouldRefreshInstalledAppStatusForSnapshot(
            forceRefresh: false,
            currentStatus,
            currentStagedApkPath: @"C:\bundle\..\bundle\SussexExperiment.apk",
            lastQueriedStagedApkPath: @"C:\bundle\SussexExperiment.apk");

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshInstalledAppStatusForSnapshot_RefreshesWhenStagedApkChanges()
    {
        var currentStatus = new InstalledAppStatus(
            "com.Viscereality.SussexExperiment",
            IsInstalled: true,
            VersionName: "0.1.0",
            VersionCode: "1",
            InstalledSha256: "hash",
            InstalledPath: "/data/app/base.apk",
            Summary: "Installed.",
            Detail: "detail");

        var shouldRefresh = StudyShellViewModel.ShouldRefreshInstalledAppStatusForSnapshot(
            forceRefresh: false,
            currentStatus,
            currentStagedApkPath: @"C:\bundle\SussexExperiment-v2.apk",
            lastQueriedStagedApkPath: @"C:\bundle\SussexExperiment.apk");

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshInstalledAppStatusForSnapshot_RefreshesWhenNoPriorStatusExists()
    {
        var shouldRefresh = StudyShellViewModel.ShouldRefreshInstalledAppStatusForSnapshot(
            forceRefresh: false,
            currentStatus: null,
            currentStagedApkPath: @"C:\bundle\SussexExperiment.apk",
            lastQueriedStagedApkPath: @"C:\bundle\SussexExperiment.apk");

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void HasReportedStudyRuntimeConfigJson_AcceptsDirectAndHotloadKeys()
    {
        Assert.True(StudyShellViewModel.HasReportedStudyRuntimeConfigJson(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["showcase_active_runtime_config_json"] = "{\"UseSphereDeformation\":true}"
        }));

        Assert.True(StudyShellViewModel.HasReportedStudyRuntimeConfigJson(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hotload.showcase_active_runtime_config_json"] = "{\"UseSphereDeformation\":true}"
        }));

        Assert.False(StudyShellViewModel.HasReportedStudyRuntimeConfigJson(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["showcase_active_runtime_config_json"] = "   "
        }));
    }

    [Fact]
    public void HasFreshRuntimeConfigTwinBaseline_RequiresFreshSnapshotAndRuntimeConfigJson()
    {
        var commandIssuedAtUtc = DateTimeOffset.UtcNow;
        var reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["showcase_active_runtime_config_json"] = "{\"UseSphereDeformation\":true}"
        };

        Assert.False(StudyShellViewModel.HasFreshRuntimeConfigTwinBaseline(
            previousRevision: "10",
            previousCommittedAtUtc: commandIssuedAtUtc.AddSeconds(-2),
            commandIssuedAtUtc,
            currentRevision: "10",
            currentCommittedAtUtc: commandIssuedAtUtc.AddSeconds(-2),
            reportedTwinState));

        Assert.False(StudyShellViewModel.HasFreshRuntimeConfigTwinBaseline(
            previousRevision: "10",
            previousCommittedAtUtc: commandIssuedAtUtc.AddSeconds(-2),
            commandIssuedAtUtc,
            currentRevision: "11",
            currentCommittedAtUtc: commandIssuedAtUtc.AddSeconds(1),
            reportedTwinState: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

        Assert.True(StudyShellViewModel.HasFreshRuntimeConfigTwinBaseline(
            previousRevision: "10",
            previousCommittedAtUtc: commandIssuedAtUtc.AddSeconds(-2),
            commandIssuedAtUtc,
            currentRevision: "11",
            currentCommittedAtUtc: commandIssuedAtUtc.AddSeconds(1),
            reportedTwinState));
    }

    [Fact]
    public void HasReportedParticipantSessionRuntimeConfig_RequiresExpectedSessionIdAndDatasetHash()
    {
        var reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["showcase_active_runtime_config_json"] =
                "{\"study_session_id\":\"session-123\",\"study_session_dataset_hash\":\"hash-abc\",\"UseSphereDeformation\":true}"
        };

        Assert.True(StudyShellViewModel.HasReportedParticipantSessionRuntimeConfig(
            reportedTwinState,
            "session-123",
            "hash-abc"));

        Assert.False(StudyShellViewModel.HasReportedParticipantSessionRuntimeConfig(
            reportedTwinState,
            "session-999",
            "hash-abc"));

        Assert.False(StudyShellViewModel.HasReportedParticipantSessionRuntimeConfig(
            reportedTwinState,
            "session-123",
            "hash-other"));
    }

    [Fact]
    public void HasReportedParticipantSessionRuntimeConfig_AcceptsFreshFlatMirrorKeys()
    {
        var reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hotload.study_session_id"] = "session-123",
            ["hotload.study_session_dataset_hash"] = "hash-abc"
        };

        Assert.True(StudyShellViewModel.HasReportedParticipantSessionRuntimeConfig(
            reportedTwinState,
            "session-123",
            "hash-abc"));

        Assert.False(StudyShellViewModel.HasReportedParticipantSessionRuntimeConfig(
            reportedTwinState,
            "session-999",
            "hash-abc"));
    }

    [Fact]
    public void HasFreshParticipantSessionRuntimeConfigBaseline_RequiresFreshSnapshotAndExpectedSessionMetadata()
    {
        var commandIssuedAtUtc = DateTimeOffset.UtcNow;
        var reportedTwinState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hotload.showcase_active_runtime_config_json"] =
                "{\"study_session_id\":\"session-123\",\"study_session_dataset_hash\":\"hash-abc\"}"
        };

        Assert.True(StudyShellViewModel.HasFreshParticipantSessionRuntimeConfigBaseline(
            previousRevision: "10",
            previousCommittedAtUtc: commandIssuedAtUtc.AddSeconds(-2),
            commandIssuedAtUtc,
            currentRevision: "11",
            currentCommittedAtUtc: commandIssuedAtUtc.AddSeconds(1),
            reportedTwinState,
            expectedSessionId: "session-123",
            expectedDatasetHash: "hash-abc"));

        Assert.False(StudyShellViewModel.HasFreshParticipantSessionRuntimeConfigBaseline(
            previousRevision: "10",
            previousCommittedAtUtc: commandIssuedAtUtc.AddSeconds(-2),
            commandIssuedAtUtc,
            currentRevision: "11",
            currentCommittedAtUtc: commandIssuedAtUtc.AddSeconds(1),
            reportedTwinState,
            expectedSessionId: "session-123",
            expectedDatasetHash: "hash-other"));
    }
}
