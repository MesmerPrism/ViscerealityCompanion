using System.IO;
using System.Globalization;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class SussexParticleSizeTuningCompilerTests
{
    private readonly SussexParticleSizeTuningCompiler _compiler = new();

    [Fact]
    public void Parse_accepts_valid_v1_document()
    {
        var document = _compiler.Parse(BuildTuningJson(0.04, 0.115));

        Assert.Equal(SussexParticleSizeTuningCompiler.ExpectedSchemaVersion, document.SchemaVersion);
        Assert.Equal(SussexParticleSizeTuningCompiler.ExpectedDocumentKind, document.DocumentKind);
        Assert.Equal(SussexParticleSizeTuningCompiler.ExpectedPackageId, document.PackageId);
        Assert.Equal(0.04, document.ParticleSizeMinimum.Value, 3);
        Assert.Equal(0.115, document.ParticleSizeMaximum.Value, 3);
    }

    [Fact]
    public void Parse_rejects_invalid_schema_version()
    {
        var json = BuildTuningJson(0.04, 0.115)
            .Replace(
                SussexParticleSizeTuningCompiler.ExpectedSchemaVersion,
                "sussex-particle-size-tuning/v0",
                StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() => _compiler.Parse(json));

        Assert.Contains("Unsupported Sussex particle-size tuning schema", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_minimum_above_maximum()
    {
        var exception = Assert.Throws<InvalidDataException>(() => _compiler.Parse(BuildTuningJson(0.2, 0.1)));

        Assert.Contains("particle_size_min.value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_patches_only_particle_size_limits()
    {
        var document = _compiler.Parse(BuildTuningJson(0.055, 0.145));
        const string baselineJson = """
            {
              "ParticleSizeEnvelopeLimits": {
                "x": 0.04,
                "y": 0.115
              },
              "OrbitRadiusMultiplierLimits": {
                "x": 0.2,
                "y": 1.4
              },
              "UnrelatedValue": 7
            }
            """;

        var compiled = _compiler.Compile(document, baselineJson);

        Assert.Contains("\"ParticleSizeEnvelopeLimits\":{\"x\":0.055,\"y\":0.145}", compiled.CompactRuntimeConfigJson, StringComparison.Ordinal);
        Assert.Contains("\"OrbitRadiusMultiplierLimits\"", compiled.CompactRuntimeConfigJson, StringComparison.Ordinal);
        Assert.Contains("\"UnrelatedValue\":7", compiled.CompactRuntimeConfigJson, StringComparison.Ordinal);
        Assert.Equal(SussexParticleSizeTuningCompiler.ExpectedHotloadTargetKey, compiled.HotloadTargetKey);
    }

    private static string BuildTuningJson(double minimum, double maximum)
    {
        var minimumText = minimum.ToString(CultureInfo.InvariantCulture);
        var maximumText = maximum.ToString(CultureInfo.InvariantCulture);

        return $$"""
        {
          "schemaVersion": "sussex-particle-size-tuning/v1",
          "documentKind": "sussex_particle_size_tuning",
          "study": {
            "packageId": "com.Viscereality.SussexExperiment",
            "baselineHotloadProfileId": "viscereality_lsltwin_scene"
          },
          "controls": {
            "particle_size_min": {
              "id": "particle_size_min",
              "label": "Particle Size Minimum",
              "value": {{minimumText}},
              "baselineValue": 0.04,
              "safeRange": {
                "minimum": 0.0,
                "maximum": 0.5
              },
              "runtimeMapping": {
                "compiledUnityJsonField": "ParticleSizeEnvelopeLimits.x"
              }
            },
            "particle_size_max": {
              "id": "particle_size_max",
              "label": "Particle Size Maximum",
              "value": {{maximumText}},
              "baselineValue": 0.115,
              "safeRange": {
                "minimum": 0.0,
                "maximum": 0.5
              },
              "runtimeMapping": {
                "compiledUnityJsonField": "ParticleSizeEnvelopeLimits.y"
              }
            }
          },
          "compilerHints": {
            "hotloadTargetKey": "showcase_active_runtime_config_json"
          }
        }
        """;
    }
}
