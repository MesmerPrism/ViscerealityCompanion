using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class StudyShellSnapshotPolicyTests
{
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
}
