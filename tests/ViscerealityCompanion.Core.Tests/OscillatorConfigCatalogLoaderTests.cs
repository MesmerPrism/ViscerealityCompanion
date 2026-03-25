using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class OscillatorConfigCatalogLoaderTests
{
    [Fact]
    public async Task LoadAsync_MapsPublicOscillatorConfigCatalog()
    {
        var root = CreateSampleCatalog();

        try
        {
            var loader = new OscillatorConfigCatalogLoader();
            var catalog = await loader.LoadAsync(root);

            Assert.Equal("Repo sample oscillator configs", catalog.Source.Label);
            Assert.Single(catalog.Profiles);
            Assert.True(catalog.Profiles[0].MatchesPackage("org.aliusresearch.viscereality.preview"));
            Assert.Equal(2, catalog.Profiles[0].Document.Dimensions.OscillatorDimensionCount);
            Assert.Equal(OscillatorCouplingDriver.Coherence, catalog.Profiles[0].Document.Coupling.NeighborDistance1.Driver);
            Assert.Equal("#BB5522", catalog.Profiles[0].Document.Color.Gradient.Stops[1].Color);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_ExportsProfileMetadataAndDocument()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var writer = new OscillatorConfigWriter(outputRoot);
        var profile = new OscillatorConfigProfile(
            "studio-baseline",
            "Studio Baseline",
            "studio-baseline.json",
            "Public sample profile.",
            ["org.aliusresearch.viscereality.preview"],
            new OscillatorConfigDocument(
                "1.0",
                new StretchSettings(false, new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]), new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)])),
                new OscillatorDimensionSettings(1, [[0f]], 1f, new DimensionRoutingSettings(0, 0, 0, 0, 0, 0, 0, 0, 0, 0)),
                new NaturalFrequencySettings(new FloatRange(0.05f, 0.25f), 1f, new Vector3Value(0f, 0f, 0f), 1),
                new DebugSettings(false, 1f),
                new BandGapSettings(0.4f, 1f),
                new SphereSettings("fibonacci-512", "Fibonacci", 512, new FloatRange(1f, 1f)),
                new DriverOverrideSettings(false, 0.5f, false, 0.25f, false, 0.5f),
                new ColorSettings(
                    new GradientDefinition([new GradientStop(0f, "#111111"), new GradientStop(1f, "#EEEEEE")]),
                    1,
                    new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]),
                    true,
                    new Vector3Value(1f, 0f, 0f)),
                new SizeSettings(true, new FloatRange(0.01f, 0.02f), new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]), true, new Vector3Value(1f, 0f, 0f), 1),
                new DepthWaveSettings(new FloatRange(0.05f, 0.1f), new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]), true, new Vector3Value(1f, 0f, 0f), 1),
                new VisualEnvelopeSettings(new FloatRange(0.8f, 1f), new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]), false, new Vector3Value(1f, 0f, 0f), 1),
                new VisualEnvelopeSettings(new FloatRange(1f, 1.2f), new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]), false, new Vector3Value(1f, 0f, 0f), 1),
                new VisualEnvelopeSettings(new FloatRange(1f, 1.2f), new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]), false, new Vector3Value(1f, 0f, 0f), 1),
                new MotionEnvelopeSettings(new FloatRange(0f, 1f), new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]), false, new Vector3Value(1f, 0f, 0f), 1),
                new OrbitSettings(false, new Vector3Value(1f, 0f, 0f), 1, true, new Vector3Value(1f, 0f, 0f), 1, false, new FloatRange(0f, 0.05f), new FloatRange(0f, 6.28f), new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]), new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)])),
                new PairOffsetSettings(false, new Vector3Value(1f, 0f, 0f), 1, new FloatRange(0f, 1f), new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]), new FloatRange(0f, 6.28f), new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)]), false, new Vector3Value(1f, 0f, 0f), 1),
                new AnimationPhaseSettings(false, new Vector3Value(1f, 0f, 0f), 1, new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)])),
                new CouplingSettings(
                    0.5f,
                    1,
                    new CouplingCurveSettings(new FloatRange(-1f, 1f), OscillatorCouplingDriver.RadiusProgress, new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)])),
                    new CouplingCurveSettings(new FloatRange(-1f, 1f), OscillatorCouplingDriver.RadiusProgress, new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)])),
                    new CouplingCurveSettings(new FloatRange(-1f, 1f), OscillatorCouplingDriver.RadiusProgress, new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)])),
                    new SmallWorldCouplingSettings(false, new FloatRange(-1f, 1f), OscillatorCouplingDriver.RadiusProgress, new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)])),
                    new CouplingCurveSettings(new FloatRange(0.6f, 0.8f), OscillatorCouplingDriver.RadiusProgress, new CurveDefinition([new CurvePoint(0f, 0f), new CurvePoint(1f, 1f)])))));

        try
        {
            var path = await writer.WriteAsync(profile);

            Assert.True(File.Exists(path));
            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"id\": \"studio-baseline\"", json);
            Assert.Contains("\"document\"", json);
            Assert.Contains("\"PackageIds\"", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    private static string CreateSampleCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        File.WriteAllText(
            Path.Combine(root, "profiles.json"),
            """
            {
              "label": "Repo sample oscillator configs",
              "profiles": [
                {
                  "id": "studio-baseline",
                  "label": "Studio Baseline",
                  "file": "studio-baseline.json",
                  "description": "Public sample profile.",
                  "packageIds": [ "org.aliusresearch.viscereality.preview" ]
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(root, "studio-baseline.json"),
            """
            {
              "schemaVersion": "1.0",
              "stretch": {
                "useSphereDeformation": false,
                "oblatenessByRadiusCurve": {
                  "keys": [
                    { "time": 0.0, "value": 1.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                },
                "axisProfileCurve": {
                  "keys": [
                    { "time": 0.0, "value": 1.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                }
              },
              "dimensions": {
                "oscillatorDimensionCount": 2,
                "crossCouplingMatrix": [
                  [ 0.0, 0.2 ],
                  [ 0.2, 0.0 ]
                ],
                "crossCouplingStrength": 0.9,
                "routing": {
                  "colorDimensionIndex": 0,
                  "sizeDimensionIndex": 1,
                  "rotationDimensionIndex": 1,
                  "orbitDimensionIndex": 1,
                  "pairDimensionIndex": 0,
                  "waveDimensionIndex": 0,
                  "animationDimensionIndex": 1,
                  "transparencyDimensionIndex": 0,
                  "saturationDimensionIndex": 1,
                  "brightnessDimensionIndex": 1
                }
              },
              "naturalFrequency": {
                "hzLimits": { "minimum": 0.05, "maximum": 0.2 },
                "noiseScale": 1.0,
                "noiseOffset": { "x": 0.0, "y": 0.0, "z": 0.0 },
                "noiseSeed": 3
              },
              "debug": {
                "debugLogDriverMixes": false,
                "debugLogIntervalSeconds": 1.0
              },
              "bandGap": {
                "gapBlackHalfWidth": 0.4,
                "gapCenterBlack": 1.0
              },
              "sphere": {
                "sphereDataId": "fibonacci-512",
                "layout": "Fibonacci",
                "oscillatorCount": 512,
                "radiusLimits": { "minimum": 1.0, "maximum": 1.2 }
              },
              "driverOverrides": {
                "manualOverrideCoherence": false,
                "manualCoherence01": 0.5,
                "manualOverrideHeartbeatPulse": false,
                "manualHeartbeatPulse01": 0.2,
                "manualOverrideBreathing": false,
                "manualBreath01": 0.5
              },
              "color": {
                "gradient": {
                  "stops": [
                    { "position": 0.0, "color": "#111111" },
                    { "position": 0.5, "color": "#BB5522" },
                    { "position": 1.0, "color": "#EEEEEE" }
                  ]
                },
                "cycleMultiplier": 1,
                "driverCurve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                },
                "usePerOscillatorPhase": true,
                "externalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 }
              },
              "size": {
                "usePercentSize": true,
                "limits": { "minimum": 0.01, "maximum": 0.02 },
                "envelopeCurve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                },
                "usePerOscillatorPhase": true,
                "externalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 },
                "cycleMultiplier": 1
              },
              "depthWave": {
                "percentLimits": { "minimum": 0.05, "maximum": 0.1 },
                "envelopeCurve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                },
                "usePerOscillatorPhase": true,
                "externalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 },
                "cycleMultiplier": 1
              },
              "transparency": {
                "limits": { "minimum": 0.8, "maximum": 1.0 },
                "envelopeCurve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                },
                "usePerOscillatorPhase": false,
                "externalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 },
                "cycleMultiplier": 1
              },
              "saturation": {
                "limits": { "minimum": 1.0, "maximum": 1.1 },
                "envelopeCurve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                },
                "usePerOscillatorPhase": false,
                "externalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 },
                "cycleMultiplier": 1
              },
              "brightness": {
                "limits": { "minimum": 1.0, "maximum": 1.2 },
                "envelopeCurve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                },
                "usePerOscillatorPhase": false,
                "externalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 },
                "cycleMultiplier": 1
              },
              "spinSpeed": {
                "limits": { "minimum": 0.0, "maximum": 1.0 },
                "envelopeCurve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                },
                "usePerOscillatorPhase": false,
                "externalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 },
                "cycleMultiplier": 1
              },
              "orbit": {
                "orbitRadiusUsePerOscillatorPhase": false,
                "orbitRadiusExternalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 },
                "orbitRadiusDriverCycleMultiplier": 1,
                "orbitAngleUsePerOscillatorPhase": true,
                "orbitAngleExternalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 },
                "orbitAngleDriverCycleMultiplier": 1,
                "dualSpinAnimation": false,
                "orbitRadiusMultiplierLimits": { "minimum": 0.0, "maximum": 0.05 },
                "orbitAngleLimits": { "minimum": 0.0, "maximum": 6.28 },
                "orbitRadiusEnvelopeCurve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                },
                "orbitAngleEnvelopeCurve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                }
              },
              "pairOffset": {
                "pairOffsetUsePerOscillatorPhase": false,
                "pairOffsetExternalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 },
                "pairOffsetDriverCycleMultiplier": 1,
                "pairOffsetMultiplierLimits": { "minimum": 0.0, "maximum": 1.0 },
                "pairOffsetEnvelopeCurve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                },
                "pairOffsetAngleLimits": { "minimum": 0.0, "maximum": 6.28 },
                "pairOffsetAngleEnvelopeCurve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                },
                "pairOffsetAngleUsePerOscillatorPhase": false,
                "pairOffsetAngleExternalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 },
                "pairOffsetAngleDriverCycleMultiplier": 1
              },
              "animationPhase": {
                "usePerOscillatorPhase": false,
                "externalDriverWeights": { "x": 1.0, "y": 0.0, "z": 0.0 },
                "cycleMultiplier": 1,
                "curve": {
                  "keys": [
                    { "time": 0.0, "value": 0.0 },
                    { "time": 1.0, "value": 1.0 }
                  ]
                }
              },
              "coupling": {
                "baseCouplingStrength": 0.5,
                "maxNeighborTier": 1,
                "neighborDistance1": {
                  "limits": { "minimum": -0.5, "maximum": 0.8 },
                  "driver": "coherence",
                  "curve": {
                    "keys": [
                      { "time": 0.0, "value": 0.0 },
                      { "time": 1.0, "value": 1.0 }
                    ]
                  }
                },
                "neighborDistance2": {
                  "limits": { "minimum": -0.2, "maximum": 0.4 },
                  "driver": "radiusProgress",
                  "curve": {
                    "keys": [
                      { "time": 0.0, "value": 0.0 },
                      { "time": 1.0, "value": 1.0 }
                    ]
                  }
                },
                "neighborDistance3": {
                  "limits": { "minimum": -0.1, "maximum": 0.2 },
                  "driver": "heartbeatPulse",
                  "curve": {
                    "keys": [
                      { "time": 0.0, "value": 0.0 },
                      { "time": 1.0, "value": 1.0 }
                    ]
                  }
                },
                "smallWorld": {
                  "enabled": true,
                  "limits": { "minimum": -0.1, "maximum": 0.3 },
                  "driver": "coherence",
                  "curve": {
                    "keys": [
                      { "time": 0.0, "value": 0.0 },
                      { "time": 1.0, "value": 1.0 }
                    ]
                  }
                },
                "naturalFrequencyMultiplier": {
                  "limits": { "minimum": 0.6, "maximum": 0.8 },
                  "driver": "radiusProgress",
                  "curve": {
                    "keys": [
                      { "time": 0.0, "value": 0.0 },
                      { "time": 1.0, "value": 1.0 }
                    ]
                  }
                }
              }
            }
            """);

        return root;
    }
}
