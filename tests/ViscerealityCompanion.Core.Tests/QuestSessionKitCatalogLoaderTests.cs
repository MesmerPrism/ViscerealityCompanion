using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class QuestSessionKitCatalogLoaderTests
{
    [Fact]
    public async Task LoadAsync_MapsRepoStyleCatalogStructure()
    {
        var root = CreateSampleCatalog();

        try
        {
            var loader = new QuestSessionKitCatalogLoader();
            var catalog = await loader.LoadAsync(root);

            Assert.Equal("Repo sample session kit", catalog.Source.Label);
            Assert.Single(catalog.Apps);
            Assert.Single(catalog.Bundles);
            Assert.Single(catalog.HotloadProfiles);
            Assert.Single(catalog.DeviceProfiles);
            Assert.True(catalog.HotloadProfiles[0].MatchesPackage("com.example.questsample"));
            Assert.True(Path.IsPathRooted(catalog.HotloadProfiles[0].File));
            Assert.EndsWith(Path.Combine("HotloadProfiles", "runtime-default.csv"), catalog.HotloadProfiles[0].File, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("debug.mode", catalog.DeviceProfiles[0].Properties.Keys.Single());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

      [Fact]
      public async Task LoadAsync_Discovers_Unmapped_Apks_From_Apk_Directory()
      {
        var root = CreateApkMapCatalogWithUnmappedApk();

        try
        {
          var loader = new QuestSessionKitCatalogLoader();
          var catalog = await loader.LoadAsync(root);

          var twinApp = Assert.Single(
            catalog.Apps,
            app => string.Equals(app.ApkFile, "SussexExperiment.apk", StringComparison.OrdinalIgnoreCase));

          Assert.Equal("com.Viscereality.SussexExperiment", twinApp.PackageId);
          Assert.Contains("viscereality", twinApp.Tags, StringComparer.OrdinalIgnoreCase);
          Assert.Contains("runtime", twinApp.Tags, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
          Directory.Delete(root, recursive: true);
        }
      }

      [Fact]
      public async Task LoadAsync_Applies_Hash_Based_Compatibility_Metadata()
      {
        var root = CreateCatalogWithCompatibilityManifest();

        try
        {
          var loader = new QuestSessionKitCatalogLoader();
          var catalog = await loader.LoadAsync(root);

          var twinApp = Assert.Single(
            catalog.Apps,
            app => string.Equals(app.PackageId, "com.Viscereality.SussexExperiment", StringComparison.OrdinalIgnoreCase));

          Assert.Equal(ViscerealityCompanion.Core.Models.ApkCompatibilityStatus.Compatible, twinApp.CompatibilityStatus);
          Assert.Equal("Twin runtime verified", twinApp.CompatibilityProfile);
          Assert.Equal("Publishes quest_twin_state and accepts quest_twin_commands.", twinApp.CompatibilityNotes);
          Assert.NotNull(twinApp.VerificationBaseline);
          Assert.Equal("14", twinApp.VerificationBaseline!.SoftwareVersion);
          Assert.Equal("2921110053000610", twinApp.VerificationBaseline.BuildId);
          Assert.NotNull(twinApp.ApkSha256);
          Assert.Contains("twin", twinApp.Tags, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
          Directory.Delete(root, recursive: true);
        }
      }

    [Fact]
    public async Task WriteAsync_PersistsIndentedManifest()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var writer = new SessionManifestWriter(outputRoot);

        try
        {
            var path = await writer.WriteAsync(new(
                CatalogSourceLabel: "Repo sample session kit",
                CatalogRootPath: @"C:\sample",
                EndpointDraft: "192.168.43.1:5555",
                ActiveEndpoint: "192.168.43.1:5555",
                SelectedAppId: "sample-quest-app",
                SelectedBundleId: "sample-stack",
                SelectedHotloadProfileId: "runtime-default",
                SelectedRuntimeConfigId: "studio-baseline",
                SelectedDeviceProfileId: "debug-balanced",
                ConnectionSummary: "Preview transport ready.",
                RuntimeConfigSummary: "Studio Baseline for preview runtime.",
                RemoteOnlyControlEnabled: true,
                MonitorSummary: "Streaming LSL sample.",
                MonitorDetail: "Preview bridge attached.",
                MonitorValue: 0.64f,
                MonitorSampleRateHz: 30f,
                TwinSummary: "Private twin bridge not installed.",
                TwinDetail: "Public contract only.",
                LastActionLabel: "Connect Quest",
                LastActionDetail: "Endpoint stored.",
                BrowserUrl: "https://www.aliusresearch.org/viscereality.html",
                RecentLogs: Array.Empty<ViscerealityCompanion.Core.Models.OperatorLogEntry>()));

            Assert.True(File.Exists(path));
            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"catalogSourceLabel\": \"Repo sample session kit\"", json);
            Assert.Contains("\"monitor\"", json);
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    private static string CreateSampleCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "APKs"));
        Directory.CreateDirectory(Path.Combine(root, "HotloadProfiles"));
        Directory.CreateDirectory(Path.Combine(root, "DeviceProfiles"));

        File.WriteAllText(
            Path.Combine(root, "APKs", "library.json"),
            """
            {
              "apps": [
                {
                  "id": "sample-quest-app",
                  "label": "Sample Quest App",
                  "packageId": "com.example.questsample",
                  "apkFile": "sample.apk",
                  "launchComponent": "",
                  "browserPackageId": "com.oculus.browser",
                  "description": "Preview app.",
                  "tags": [ "sample" ]
                }
              ],
              "bundles": [
                {
                  "id": "sample-stack",
                  "label": "Sample Stack",
                  "description": "Preview bundle.",
                  "appIds": [ "sample-quest-app" ]
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(root, "HotloadProfiles", "profiles.json"),
            """
            {
              "defaultPackageIds": [ "com.example.questsample" ],
              "profiles": [
                {
                  "id": "runtime-default",
                  "label": "Runtime Default",
                  "file": "runtime-default.csv",
                  "version": "1.0",
                  "channel": "preview",
                  "studyLock": false,
                  "description": "Preview profile."
                }
              ]
            }
            """);

        File.WriteAllText(Path.Combine(root, "HotloadProfiles", "runtime-default.csv"), "alpha,0.5");

        File.WriteAllText(
            Path.Combine(root, "DeviceProfiles", "profiles.json"),
            """
            {
              "profiles": [
                {
                  "id": "debug-balanced",
                  "label": "Debug Balanced",
                  "description": "Preview device profile.",
                  "props": {
                    "debug.mode": "balanced"
                  }
                }
              ]
            }
            """);

        return root;
    }

    private static string CreateApkMapCatalogWithUnmappedApk()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "APKs"));
        Directory.CreateDirectory(Path.Combine(root, "HotloadProfiles"));
        Directory.CreateDirectory(Path.Combine(root, "DeviceProfiles"));

        File.WriteAllText(
            Path.Combine(root, "APKs", "apk_map.json"),
            """
            {
              "apks": [
                {
                  "file": "KarateBio.apk",
                  "packageId": "com.Viscereality.KarateBio"
                }
              ]
            }
            """);

        File.WriteAllBytes(Path.Combine(root, "APKs", "KarateBio.apk"), [0x50, 0x4B, 0x03, 0x04]);
        File.WriteAllBytes(Path.Combine(root, "APKs", "SussexExperiment.apk"), [0x50, 0x4B, 0x03, 0x04]);

        File.WriteAllText(
            Path.Combine(root, "HotloadProfiles", "profiles.json"),
            """
            {
              "defaultPackageIds": [ "com.Viscereality.KarateBio" ],
              "profiles": []
            }
            """);

        File.WriteAllText(
            Path.Combine(root, "DeviceProfiles", "profiles.json"),
            """
            {
              "profiles": []
            }
            """);

        return root;
    }

    private static string CreateCatalogWithCompatibilityManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "APKs"));
        Directory.CreateDirectory(Path.Combine(root, "HotloadProfiles"));
        Directory.CreateDirectory(Path.Combine(root, "DeviceProfiles"));

        File.WriteAllText(
            Path.Combine(root, "APKs", "apk_map.json"),
            """
            {
              "apks": [
                {
                  "file": "SussexExperiment.apk",
                  "packageId": "com.Viscereality.SussexExperiment"
                }
              ]
            }
            """);

        var apkPath = Path.Combine(root, "APKs", "SussexExperiment.apk");
        File.WriteAllBytes(apkPath, [0x50, 0x4B, 0x03, 0x04, 0x10, 0x20]);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(apkPath)));

        File.WriteAllText(
            Path.Combine(root, "APKs", "compatibility.json"),
            $$"""
            {
              "apps": [
                {
                  "sha256": "{{hash}}",
                  "status": "compatible",
                  "label": "Twin runtime verified",
                  "notes": "Publishes quest_twin_state and accepts quest_twin_commands.",
                  "tags": ["viscereality", "runtime", "lsl", "twin"],
                  "verification": {
                    "apkSha256": "{{hash}}",
                    "softwareVersion": "14",
                    "buildId": "2921110053000610",
                    "displayId": "UP1A.231005.007.A1",
                    "deviceProfileId": "sussex-study-profile",
                    "environmentHash": "CAFEBABE",
                    "verifiedAtUtc": "2026-03-29T10:15:00Z",
                    "verifiedBy": "tools/ViscerealityCompanion.VerificationHarness"
                  }
                }
              ]
            }
            """
        );

        File.WriteAllText(
            Path.Combine(root, "HotloadProfiles", "profiles.json"),
            """
            {
              "defaultPackageIds": [ "com.Viscereality.SussexExperiment" ],
              "profiles": []
            }
            """);

        File.WriteAllText(
            Path.Combine(root, "DeviceProfiles", "profiles.json"),
            """
            {
              "profiles": []
            }
            """);

        return root;
    }
}
