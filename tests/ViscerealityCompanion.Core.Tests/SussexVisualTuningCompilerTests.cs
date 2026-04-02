using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class SussexVisualTuningCompilerTests
{
    [Fact]
    public void Parse_accepts_bundled_template_document()
    {
        var templateJson = LoadTemplateJson();
        var compiler = new SussexVisualTuningCompiler(templateJson);

        var document = compiler.Parse(templateJson);

        Assert.Equal(SussexVisualTuningCompiler.ExpectedSchemaVersion, document.SchemaVersion);
        Assert.Equal(SussexVisualTuningCompiler.ExpectedDocumentKind, document.DocumentKind);
        Assert.Equal(SussexVisualTuningCompiler.ExpectedPackageId, document.PackageId);
        Assert.Equal(13, document.Controls.Count);
    }

    [Fact]
    public void Parse_upgrades_legacy_document_missing_new_controls()
    {
        var templateJson = LoadTemplateJson();
        var compiler = new SussexVisualTuningCompiler(templateJson);
        var root = JsonNode.Parse(templateJson)!.AsObject();
        var controls = root["controls"]!.AsObject();
        controls.Remove("sphere_deformation_enabled");
        controls.Remove("depth_wave_min");
        controls.Remove("depth_wave_max");
        controls.Remove("orbit_distance_min");
        controls.Remove("orbit_distance_max");

        var document = compiler.Parse(root.ToJsonString(new() { WriteIndented = true }));

        Assert.Equal(13, document.Controls.Count);
        Assert.Contains(document.Controls, control => control.Id == "sphere_deformation_enabled");
        Assert.Contains(document.Controls, control => control.Id == "depth_wave_max");
        Assert.Contains(document.Controls, control => control.Id == "orbit_distance_max");
    }

    [Fact]
    public void Parse_rejects_changed_locked_metadata()
    {
        var templateJson = LoadTemplateJson();
        var compiler = new SussexVisualTuningCompiler(templateJson);
        var mutated = templateJson.Replace("\"maximum\": 1.0", "\"maximum\": 1.1", StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() => compiler.Parse(mutated));

        Assert.Contains("locked metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_unknown_control()
    {
        var templateJson = LoadTemplateJson();
        var compiler = new SussexVisualTuningCompiler(templateJson);
        var marker = @"""brightness_max"": {";
        var rogueControl = """
            "rogue": {
              "id": "rogue",
              "label": "Rogue",
              "editable": true,
              "value": 0.1,
              "baselineValue": 0.1,
              "type": "float",
              "units": "none",
              "safeRange": { "minimum": 0.0, "maximum": 1.0 },
              "runtimeMapping": {
                "unityConfigField": "PEOscillatorConfig.None",
                "compiledUnityJsonField": "None"
              },
              "info": {
                "effect": "x",
                "increaseLooksLike": "x",
                "decreaseLooksLike": "x",
                "tradeoffs": [ "x" ]
              }
            },
        """;
        var mutated = templateJson.Replace(marker, rogueControl + Environment.NewLine + "    " + marker, StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() => compiler.Parse(mutated));

        Assert.Contains("locked metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_minimum_above_maximum()
    {
        var templateJson = LoadTemplateJson();
        var compiler = new SussexVisualTuningCompiler(templateJson);
        var root = JsonNode.Parse(templateJson)!.AsObject();
        var controls = root["controls"]!.AsObject();
        controls["brightness_min"]!["value"] = 0.8;
        controls["brightness_max"]!["value"] = 0.2;
        var mutated = root.ToJsonString(new() { WriteIndented = true });

        var exception = Assert.Throws<InvalidDataException>(() => compiler.Parse(mutated));

        Assert.Contains("brightness_min", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_emits_only_approved_visual_fields()
    {
        var compiler = new SussexVisualTuningCompiler(LoadTemplateJson());
        var document = compiler.CreateDocument(
            "Compare",
            null,
            new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase)
            {
                ["sphere_deformation_enabled"] = 0,
                ["particle_size_min"] = 0.055,
                ["particle_size_max"] = 0.145,
                ["depth_wave_min"] = 0.02,
                ["depth_wave_max"] = 0.2,
                ["brightness_min"] = 0.45,
                ["brightness_max"] = 0.9,
                ["orbit_distance_min"] = 0.4,
                ["orbit_distance_max"] = 1.75
            });

        var compiled = compiler.Compile(document);

        Assert.Contains("\"UseSphereDeformation\":false", compiled.CompactRuntimeConfigJson, StringComparison.Ordinal);
        Assert.Contains("\"ParticleSizeEnvelopeLimits\"", compiled.CompactRuntimeConfigJson, StringComparison.Ordinal);
        Assert.Contains("\"DepthWavePercentLimits\"", compiled.CompactRuntimeConfigJson, StringComparison.Ordinal);
        Assert.Contains("\"TransparencyLimits\"", compiled.CompactRuntimeConfigJson, StringComparison.Ordinal);
        Assert.Contains("\"GlobalSaturationLimits\"", compiled.CompactRuntimeConfigJson, StringComparison.Ordinal);
        Assert.Contains("\"GlobalBrightnessLimits\"", compiled.CompactRuntimeConfigJson, StringComparison.Ordinal);
        Assert.Contains("\"OrbitRadiusMultiplierLimits\"", compiled.CompactRuntimeConfigJson, StringComparison.Ordinal);
        Assert.DoesNotContain("CrossCouplingMatrix", compiled.CompactRuntimeConfigJson, StringComparison.Ordinal);
        Assert.Equal(SussexVisualTuningCompiler.ExpectedHotloadTargetKey, compiled.HotloadTargetKey);
    }

    [Fact]
    public void Serialize_emits_boolean_value_for_deformation_toggle()
    {
        var compiler = new SussexVisualTuningCompiler(LoadTemplateJson());
        var document = compiler.CreateDocument(
            "Compare",
            null,
            new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase)
            {
                ["sphere_deformation_enabled"] = 0
            });

        var json = compiler.Serialize(document);

        Assert.Contains("\"sphere_deformation_enabled\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value\": false", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_confirmation_reports_confirmed_waiting_and_mismatch()
    {
        var compiler = new SussexVisualTuningCompiler(LoadTemplateJson());
        var requested = new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase)
        {
            ["particle_size_min"] = 0.05
        };
        var applyRecord = new SussexVisualProfileApplyRecord(
            "profile-a",
            "Profile A",
            "HASH",
            "JSONHASH",
            DateTimeOffset.UtcNow,
            requested,
            null);
        const string reportedJson = """
            {
              "ParticleSizeEnvelopeLimits": { "x": 0.05, "y": 0.115 },
              "TransparencyLimits": { "x": 0.2, "y": 1.0 },
              "GlobalSaturationLimits": { "x": 0.3, "y": 1.0 }
            }
            """;

        var result = compiler.EvaluateConfirmation(applyRecord, reportedJson);

        Assert.True(result.ConfirmedCount > 0);
        Assert.True(result.WaitingCount > 0);
        Assert.True(result.MismatchCount > 0);
    }

    [Fact]
    public void Evaluate_confirmation_waits_for_changed_field_while_report_is_still_pre_apply_value()
    {
        var compiler = new SussexVisualTuningCompiler(LoadTemplateJson());
        var requested = new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase)
        {
            ["particle_size_max"] = 0.04
        };
        var previousReported = compiler.ExtractRuntimeValues("""
            {
              "ParticleSizeEnvelopeLimits": { "x": 0.04, "y": 0.115 },
              "TransparencyLimits": { "x": 1.0, "y": 1.0 },
              "GlobalSaturationLimits": { "x": 0.3, "y": 1.0 },
              "GlobalBrightnessLimits": { "x": 0.3, "y": 1.0 }
            }
            """);

        var applyRecord = new SussexVisualProfileApplyRecord(
            "profile-a",
            "Profile A",
            "HASH",
            "JSONHASH",
            DateTimeOffset.UtcNow,
            requested,
            previousReported);

        var result = compiler.EvaluateConfirmation(applyRecord, """
            {
              "ParticleSizeEnvelopeLimits": { "x": 0.04, "y": 0.115 },
              "TransparencyLimits": { "x": 1.0, "y": 1.0 },
              "GlobalSaturationLimits": { "x": 0.3, "y": 1.0 },
              "GlobalBrightnessLimits": { "x": 0.3, "y": 1.0 }
            }
            """);

        var changedRow = Assert.Single(result.Rows, row => row.Id == "particle_size_max");
        Assert.Equal(SussexVisualConfirmationState.Waiting, changedRow.State);
        Assert.Equal(0, result.MismatchCount);
    }

    [Fact]
    public void Evaluate_confirmation_reports_mismatch_after_grace_period_when_changed_field_never_updates()
    {
        var compiler = new SussexVisualTuningCompiler(LoadTemplateJson());
        var requested = new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase)
        {
            ["particle_size_max"] = 0.04
        };
        var previousReported = compiler.ExtractRuntimeValues("""
            {
              "ParticleSizeEnvelopeLimits": { "x": 0.04, "y": 0.115 },
              "TransparencyLimits": { "x": 1.0, "y": 1.0 },
              "GlobalSaturationLimits": { "x": 0.3, "y": 1.0 },
              "GlobalBrightnessLimits": { "x": 0.3, "y": 1.0 }
            }
            """);

        var applyRecord = new SussexVisualProfileApplyRecord(
            "profile-a",
            "Profile A",
            "HASH",
            "JSONHASH",
            DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10),
            requested,
            previousReported);

        var result = compiler.EvaluateConfirmation(applyRecord, """
            {
              "ParticleSizeEnvelopeLimits": { "x": 0.04, "y": 0.115 },
              "TransparencyLimits": { "x": 1.0, "y": 1.0 },
              "GlobalSaturationLimits": { "x": 0.3, "y": 1.0 },
              "GlobalBrightnessLimits": { "x": 0.3, "y": 1.0 }
            }
            """);

        var changedRow = Assert.Single(result.Rows, row => row.Id == "particle_size_max");
        Assert.Equal(SussexVisualConfirmationState.Mismatch, changedRow.State);
        Assert.True(result.MismatchCount > 0);
    }

    [Fact]
    public void Extract_runtime_values_tolerates_malformed_twin_state_json()
    {
        var compiler = new SussexVisualTuningCompiler(LoadTemplateJson());

        var values = compiler.ExtractRuntimeValues("\0not-json");

        Assert.Equal(13, values.Count);
        Assert.All(values.Values, value => Assert.Null(value));
    }

    [Fact]
    public void Build_comparison_rows_uses_template_baseline_and_profile_deltas()
    {
        var compiler = new SussexVisualTuningCompiler(LoadTemplateJson());
        var selected = compiler.CreateDocument(
            "Selected",
            null,
            new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase)
            {
                ["particle_size_max"] = 0.2
            });
        var compare = compiler.CreateDocument(
            "Compare",
            null,
            new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase)
            {
                ["particle_size_max"] = 0.15
            });

        var rows = compiler.BuildComparisonRows(selected, compare);
        var row = Assert.Single(rows, candidate => candidate.Id == "particle_size_max");

        Assert.Equal(0.115, row.BaselineValue, 3);
        Assert.Equal(0.2, row.SelectedValue, 3);
        Assert.NotNull(row.CompareValue);
        Assert.True(Math.Abs(row.CompareValue!.Value - 0.15) < 0.001);
        Assert.Equal(0.085, row.DeltaFromBaseline, 3);
        Assert.NotNull(row.DeltaBetweenProfiles);
        Assert.True(Math.Abs(row.DeltaBetweenProfiles!.Value - 0.05) < 0.001);
    }

    private static string LoadTemplateJson()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "oscillator-config",
            "llm-tuning",
            "sussex-visual-tuning-v1.template.json"));
        return File.ReadAllText(path);
    }
}
