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
            var study = Assert.Single(catalog.Studies);
            Assert.Equal("sussex-university", study.Id);
            Assert.Equal("com.Viscereality.LslTwin", study.App.PackageId);
            Assert.Equal("2", study.Controls.RecenterCommandActionId);
            Assert.Contains("debug.oculus.gpuLevel", study.DeviceProfile.Properties.Keys);
            Assert.Contains("connection.lsl.connected_count", study.Monitoring.LslConnectivityKeys);
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
                "label": "Sussex LslTwin Build",
                "packageId": "com.Viscereality.LslTwin",
                "apkPath": "{{(withRelativeApkPath ? "payload/Sussex.apk" : "")}}",
                "launchComponent": "",
                "sha256": "ABC123",
                "versionName": "0.1.0",
                "notes": "Pinned build."
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
                "expectedLslStreamName": "quest_biofeedback_in",
                "expectedLslStreamType": "quest.biofeedback",
                "recenterDistanceThresholdUnits": 0.2,
                "lslConnectivityKeys": ["connection.lsl.connected_count"],
                "lslStreamNameKeys": ["showcase_lsl_in_stream_name"],
                "lslStreamTypeKeys": ["showcase_lsl_in_stream_type"],
                "controllerValueKeys": ["tracker.breathing.controller.volume01"],
                "controllerStateKeys": ["tracker.breathing.controller.state"],
                "controllerTrackingKeys": ["tracker.breathing.controller.active"],
                "heartbeatValueKeys": ["signal01.heartbeat_lsl"],
                "heartbeatStateKeys": ["routing.heartbeat.mode"],
                "coherenceValueKeys": ["signal01.coherence_heartbeat"],
                "coherenceStateKeys": ["routing.coherence.mode"],
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
