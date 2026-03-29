using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class StudyShellCatalogLoaderTests
{
    [Fact]
    public async Task LoadAsync_MapsStudyShellManifestAndDefinition()
    {
        var root = CreateStudyShellCatalog();

        try
        {
            var loader = new StudyShellCatalogLoader();
            var catalog = await loader.LoadAsync(root);

            Assert.Equal("Public study shells", catalog.Source.Label);
            Assert.Equal("sussex-university", catalog.LaunchOptions.StartupStudyId);
            Assert.True(catalog.LaunchOptions.LockToStartupStudy);
            var study = Assert.Single(catalog.Studies);
            Assert.Equal("sussex-university", study.Id);
            Assert.Equal("com.Viscereality.LslTwin", study.App.PackageId);
            Assert.False(study.App.AllowManualSelection);
            Assert.True(study.App.LaunchInKioskMode);
            Assert.NotNull(study.App.VerificationBaseline);
            Assert.Equal("14", study.App.VerificationBaseline!.SoftwareVersion);
            Assert.Equal("2921110053000610", study.App.VerificationBaseline.BuildId);
            Assert.Equal("2", study.Controls.RecenterCommandActionId);
            Assert.Contains("debug.oculus.gpuLevel", study.DeviceProfile.Properties.Keys);
            Assert.Contains("connection.lsl.connected_count", study.Monitoring.LslConnectivityKeys);
            Assert.Contains("signal01.coherence_lsl", study.Monitoring.LslValueKeys);
            Assert.Contains("study.performance.fps", study.Monitoring.PerformanceFpsKeys);
            Assert.Equal(0.2d, study.Monitoring.RecenterDistanceThresholdUnits);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ResolvesRelativeApkPathAgainstDefinitionDirectory()
    {
        var root = CreateStudyShellCatalog(withRelativeApkPath: true);

        try
        {
            var loader = new StudyShellCatalogLoader();
            var catalog = await loader.LoadAsync(root);
            var study = Assert.Single(catalog.Studies);

            Assert.True(Path.IsPathRooted(study.App.ApkPath));
            Assert.EndsWith(Path.Combine("payload", "Sussex.apk"), study.App.ApkPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateStudyShellCatalog(bool withRelativeApkPath = false)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var payloadDirectory = Path.Combine(root, "payload");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(payloadDirectory);

        File.WriteAllText(
            Path.Combine(root, "manifest.json"),
            """
            {
              "label": "Public study shells",
              "startupStudyId": "sussex-university",
              "lockToStartupStudy": true,
              "studies": [
                { "file": "sussex.json" }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(root, "sussex.json"),
            $$"""
            {
              "id": "sussex-university",
              "label": "Sussex University",
              "partner": "University of Sussex",
              "description": "Controller breathing study shell.",
              "app": {
                "label": "Sussex Controller Study APK",
                "packageId": "com.Viscereality.LslTwin",
                "apkPath": "{{(withRelativeApkPath ? "payload/Sussex.apk" : "")}}",
                "launchComponent": "",
                "sha256": "ABC123",
                "versionName": "0.1.0",
                "notes": "Bundled Sussex APK.",
                "allowManualSelection": false,
                "launchInKioskMode": true,
                "verification": {
                  "apkSha256": "ABC123",
                  "softwareVersion": "14",
                  "buildId": "2921110053000610",
                  "displayId": "UP1A.231005.007.A1",
                  "deviceProfileId": "sussex-study-profile",
                  "environmentHash": "CAFEBABE",
                  "verifiedAtUtc": "2026-03-29T10:15:00Z",
                  "verifiedBy": "tools/ViscerealityCompanion.VerificationHarness"
                }
              },
              "deviceProfile": {
                "id": "sussex-study-profile",
                "label": "Sussex Study Device Profile",
                "description": "Quest study settings.",
                "properties": {
                  "debug.oculus.cpuLevel": "2",
                  "debug.oculus.gpuLevel": "5"
                }
              },
              "monitoring": {
                "expectedBreathingLabel": "",
                "expectedHeartbeatLabel": "",
                "expectedCoherenceLabel": "",
                "expectedLslStreamName": "{{HrvBiofeedbackStreamContract.StreamName}}",
                "expectedLslStreamType": "{{HrvBiofeedbackStreamContract.StreamType}}",
                "recenterDistanceThresholdUnits": 0.2,
                "lslConnectivityKeys": ["connection.lsl.connected_count"],
                "lslStreamNameKeys": ["showcase_lsl_in_stream_name"],
                "lslStreamTypeKeys": ["showcase_lsl_in_stream_type"],
                "lslValueKeys": ["signal01.coherence_lsl"],
                "controllerValueKeys": ["tracker.breathing.controller.volume01"],
                "controllerStateKeys": ["tracker.breathing.controller.state"],
                "controllerTrackingKeys": ["tracker.breathing.controller.active"],
                "heartbeatValueKeys": ["signal01.heartbeat_lsl"],
                "heartbeatStateKeys": ["routing.heartbeat.mode"],
                "coherenceValueKeys": ["signal01.coherence_lsl"],
                "coherenceStateKeys": ["routing.coherence.mode"],
                "performanceFpsKeys": ["study.performance.fps"],
                "performanceFrameTimeKeys": ["study.performance.frame_ms"],
                "performanceTargetFpsKeys": ["study.performance.target_fps"],
                "performanceRefreshRateKeys": ["study.performance.refresh_hz"],
                "recenterDistanceKeys": [],
                "particleVisibilityKeys": []
              },
              "controls": {
                "recenterCommandActionId": "2",
                "particleVisibleOnActionId": "",
                "particleVisibleOffActionId": ""
              }
            }
            """);

        if (withRelativeApkPath)
        {
            File.WriteAllBytes(Path.Combine(payloadDirectory, "Sussex.apk"), [0x50, 0x4B, 0x03, 0x04]);
        }

        return root;
    }
}
