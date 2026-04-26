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
            Assert.Equal("com.Viscereality.SussexExperiment", study.App.PackageId);
            Assert.False(study.App.AllowManualSelection);
            Assert.True(study.App.LaunchInKioskMode);
            Assert.NotNull(study.App.VerificationBaseline);
            Assert.Equal("14", study.App.VerificationBaseline!.SoftwareVersion);
            Assert.Equal("2921110053000610", study.App.VerificationBaseline.BuildId);
            Assert.Equal("2", study.Controls.RecenterCommandActionId);
            Assert.Equal("14", study.Controls.StartBreathingCalibrationActionId);
            Assert.Equal("20", study.Controls.SetBreathingModeControllerVolumeActionId);
            Assert.Equal("46", study.Controls.SetBreathingModeAutomaticCycleActionId);
            Assert.Equal("47", study.Controls.StartAutomaticBreathingActionId);
            Assert.Equal("48", study.Controls.PauseAutomaticBreathingActionId);
            Assert.Contains("signal01.mock_pacer_breathing", study.Monitoring.AutomaticBreathingValueKeys);
            Assert.Contains("debug.oculus.gpuLevel", study.DeviceProfile.Properties.Keys);
            Assert.Contains("connection.lsl.connected_count", study.Monitoring.LslConnectivityKeys);
            Assert.Contains("signal01.coherence_lsl", study.Monitoring.LslValueKeys);
            Assert.Contains("study.performance.fps", study.Monitoring.PerformanceFpsKeys);
            Assert.Equal(0.2d, study.Monitoring.RecenterDistanceThresholdUnits);
            Assert.Equal(2, study.Conditions.Count);
            Assert.True(Assert.Single(study.Conditions, condition => condition.Id == "current").IsActive);
            var fixedRadiusCondition = Assert.Single(study.Conditions, condition => condition.Id == "fixed-radius-no-orbit");
            Assert.Equal("Fixed Radius, No Orbit", fixedRadiusCondition.Label);
            Assert.False(fixedRadiusCondition.IsActive);
            Assert.Equal("condition-fixed-radius-no-orbit", fixedRadiusCondition.VisualProfileId);
            Assert.Equal("condition-fixed-radius-breathing", fixedRadiusCondition.ControllerBreathingProfileId);
            Assert.Equal("2..2", fixedRadiusCondition.Properties["visual.sphereRadius"]);
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

    [Fact]
    public async Task LoadAsync_RejectsStudyShellWithMissingPinnedPackageId()
    {
        var root = CreateStudyShellCatalog();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "sussex.json"),
                """
                {
                  "id": "sussex-university",
                  "label": "Sussex University",
                  "partner": "University of Sussex",
                  "description": "Controller breathing study shell.",
                  "app": {
                    "label": "Sussex Controller Study APK",
                    "packageId": "",
                    "apkPath": "payload/Sussex.apk",
                    "launchComponent": "com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity",
                    "sha256": "ABC123",
                    "versionName": "0.1.0",
                    "notes": "Bundled Sussex APK.",
                    "allowManualSelection": false,
                    "launchInKioskMode": true
                  },
                  "deviceProfile": {
                    "id": "sussex-study-profile",
                    "label": "Sussex Study Device Profile",
                    "description": "Quest study settings.",
                    "properties": {
                      "debug.oculus.cpuLevel": "2"
                    }
                  },
                  "monitoring": {
                    "expectedLslStreamName": "HRV_Biofeedback",
                    "expectedLslStreamType": "HRV"
                  },
                  "controls": {
                    "recenterCommandActionId": "2",
                    "particleVisibleOnActionId": "39",
                    "particleVisibleOffActionId": "40",
                    "startBreathingCalibrationActionId": "14",
                    "resetBreathingCalibrationActionId": "41",
                    "startExperimentActionId": "42",
                    "endExperimentActionId": "43",
                    "setBreathingModeControllerVolumeActionId": "20",
                    "setBreathingModeAutomaticCycleActionId": "46",
                    "startAutomaticBreathingActionId": "47",
                    "pauseAutomaticBreathingActionId": "48"
                  }
                }
                """);

            var loader = new StudyShellCatalogLoader();
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => loader.LoadAsync(root));
            Assert.Contains("app.packageId", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsStudyShellWithMissingRequiredCommandId()
    {
        var root = CreateStudyShellCatalog();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "sussex.json"),
                """
                {
                  "id": "sussex-university",
                  "label": "Sussex University",
                  "partner": "University of Sussex",
                  "description": "Controller breathing study shell.",
                  "app": {
                    "label": "Sussex Controller Study APK",
                    "packageId": "com.Viscereality.SussexExperiment",
                    "apkPath": "payload/Sussex.apk",
                    "launchComponent": "com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity",
                    "sha256": "ABC123",
                    "versionName": "0.1.0",
                    "notes": "Bundled Sussex APK.",
                    "allowManualSelection": false,
                    "launchInKioskMode": true
                  },
                  "deviceProfile": {
                    "id": "sussex-study-profile",
                    "label": "Sussex Study Device Profile",
                    "description": "Quest study settings.",
                    "properties": {
                      "debug.oculus.cpuLevel": "2"
                    }
                  },
                  "monitoring": {
                    "expectedLslStreamName": "HRV_Biofeedback",
                    "expectedLslStreamType": "HRV"
                  },
                  "controls": {
                    "recenterCommandActionId": "",
                    "particleVisibleOnActionId": "39",
                    "particleVisibleOffActionId": "40",
                    "startBreathingCalibrationActionId": "14",
                    "resetBreathingCalibrationActionId": "41",
                    "startExperimentActionId": "42",
                    "endExperimentActionId": "43",
                    "setBreathingModeControllerVolumeActionId": "20",
                    "setBreathingModeAutomaticCycleActionId": "46",
                    "startAutomaticBreathingActionId": "47",
                    "pauseAutomaticBreathingActionId": "48"
                  }
                }
                """);

            var loader = new StudyShellCatalogLoader();
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => loader.LoadAsync(root));
            Assert.Contains("controls.recenterCommandActionId", ex.Message, StringComparison.Ordinal);
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
                "packageId": "com.Viscereality.SussexExperiment",
                "apkPath": "payload/Sussex.apk",
                "launchComponent": "com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity",
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
                "automaticBreathingValueKeys": ["signal01.mock_pacer_breathing"],
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
                "particleVisibleOnActionId": "39",
                "particleVisibleOffActionId": "40",
                "startBreathingCalibrationActionId": "14",
                "resetBreathingCalibrationActionId": "41",
                "startExperimentActionId": "42",
                "endExperimentActionId": "43",
                "setBreathingModeControllerVolumeActionId": "20",
                "setBreathingModeAutomaticCycleActionId": "46",
                "startAutomaticBreathingActionId": "47",
                "pauseAutomaticBreathingActionId": "48"
              },
              "conditions": [
                {
                  "id": "current",
                  "label": "Current",
                  "description": "Current study settings.",
                  "visualProfileId": "condition-current-visual",
                  "controllerBreathingProfileId": "condition-current-breathing",
                  "properties": {
                    "visual.orbit": "current",
                    "visual.sphereRadius": "current"
                  }
                },
                {
                  "id": "fixed-radius-no-orbit",
                  "label": "Fixed Radius, No Orbit",
                  "description": "Fixed radius and no orbit.",
                  "isActive": false,
                  "visualProfileId": "condition-fixed-radius-no-orbit",
                  "controllerBreathingProfileId": "condition-fixed-radius-breathing",
                  "properties": {
                    "visual.orbit": "0..0",
                    "visual.sphereRadius": "2..2"
                  }
                }
              ]
            }
            """);

        File.WriteAllBytes(Path.Combine(payloadDirectory, "Sussex.apk"), [0x50, 0x4B, 0x03, 0x04]);

        return root;
    }
}
