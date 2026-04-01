using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class SussexParticleSizeTuningCompiler
{
    public const string ExpectedSchemaVersion = "sussex-particle-size-tuning/v1";
    public const string ExpectedDocumentKind = "sussex_particle_size_tuning";
    public const string ExpectedPackageId = "com.Viscereality.SussexExperiment";
    public const string ExpectedHotloadTargetKey = "showcase_active_runtime_config_json";

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    public SussexParticleSizeTuningDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("The Sussex particle-size tuning file is empty.");
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The Sussex particle-size tuning file is not valid JSON: {exception.Message}", exception);
        }

        var root = rootNode as JsonObject
            ?? throw new InvalidDataException("The Sussex particle-size tuning file must be a JSON object.");

        var schemaVersion = GetRequiredString(root, "schemaVersion");
        if (!string.Equals(schemaVersion, ExpectedSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported Sussex particle-size tuning schema '{schemaVersion}'. Expected '{ExpectedSchemaVersion}'.");
        }

        var documentKind = GetRequiredString(root, "documentKind");
        if (!string.Equals(documentKind, ExpectedDocumentKind, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected Sussex particle-size tuning document kind '{documentKind}'. Expected '{ExpectedDocumentKind}'.");
        }

        var study = GetRequiredObject(root, "study");
        var packageId = GetRequiredString(study, "packageId");
        if (!string.Equals(packageId, ExpectedPackageId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The tuning file targets '{packageId}', but the Sussex compiler only accepts '{ExpectedPackageId}'.");
        }

        var baselineHotloadProfileId = GetRequiredString(study, "baselineHotloadProfileId");
        var compilerHints = GetRequiredObject(root, "compilerHints");
        var hotloadTargetKey = GetRequiredString(compilerHints, "hotloadTargetKey");
        if (!string.Equals(hotloadTargetKey, ExpectedHotloadTargetKey, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected hotload target key '{hotloadTargetKey}'. Expected '{ExpectedHotloadTargetKey}'.");
        }

        var controls = GetRequiredObject(root, "controls");
        var minControl = ParseControl(GetRequiredObject(controls, "particle_size_min"), "particle_size_min");
        var maxControl = ParseControl(GetRequiredObject(controls, "particle_size_max"), "particle_size_max");

        ValidateControlRange(minControl);
        ValidateControlRange(maxControl);
        if (minControl.Value > maxControl.Value)
        {
            throw new InvalidDataException("controls.particle_size_min.value must be less than or equal to controls.particle_size_max.value.");
        }

        return new SussexParticleSizeTuningDocument(
            schemaVersion,
            documentKind,
            packageId,
            baselineHotloadProfileId,
            hotloadTargetKey,
            minControl,
            maxControl);
    }

    public SussexParticleSizeTuningCompileResult Compile(
        SussexParticleSizeTuningDocument document,
        string baselineRuntimeConfigJson)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(baselineRuntimeConfigJson))
        {
            throw new InvalidDataException("The live Sussex runtime did not expose a baseline showcase_active_runtime_config_json payload to compile against.");
        }

        JsonNode? baselineNode;
        try
        {
            baselineNode = JsonNode.Parse(baselineRuntimeConfigJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The live Sussex runtime config baseline is not valid JSON: {exception.Message}", exception);
        }

        var baselineObject = baselineNode as JsonObject
            ?? throw new InvalidDataException("The live Sussex runtime config baseline must be a JSON object.");

        var limitsNode = baselineObject["ParticleSizeEnvelopeLimits"] as JsonObject ?? new JsonObject();
        limitsNode["x"] = document.ParticleSizeMinimum.Value;
        limitsNode["y"] = document.ParticleSizeMaximum.Value;
        baselineObject["ParticleSizeEnvelopeLimits"] = limitsNode;

        return new SussexParticleSizeTuningCompileResult(
            document,
            baselineObject.ToJsonString(),
            baselineObject.ToJsonString(PrettyJsonOptions),
            document.HotloadTargetKey);
    }

    private static SussexParticleSizeTuningControl ParseControl(JsonObject node, string expectedId)
    {
        var id = GetRequiredString(node, "id");
        if (!string.Equals(id, expectedId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected control id '{expectedId}', but found '{id}'.");
        }

        var label = GetRequiredString(node, "label");
        var value = GetRequiredDouble(node, "value");
        var baselineValue = GetRequiredDouble(node, "baselineValue");
        var safeRange = GetRequiredObject(node, "safeRange");
        var safeMinimum = GetRequiredDouble(safeRange, "minimum");
        var safeMaximum = GetRequiredDouble(safeRange, "maximum");
        var runtimeMapping = GetRequiredObject(node, "runtimeMapping");
        var runtimeJsonField = GetRequiredString(runtimeMapping, "compiledUnityJsonField");

        return new SussexParticleSizeTuningControl(
            id,
            label,
            value,
            baselineValue,
            safeMinimum,
            safeMaximum,
            runtimeJsonField);
    }

    private static void ValidateControlRange(SussexParticleSizeTuningControl control)
    {
        if (control.Value < control.SafeMinimum || control.Value > control.SafeMaximum)
        {
            throw new InvalidDataException(
                $"{control.Id}.value must stay within {control.SafeMinimum.ToString(CultureInfo.InvariantCulture)} .. {control.SafeMaximum.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static JsonObject GetRequiredObject(JsonObject node, string key)
        => node[key] as JsonObject
           ?? throw new InvalidDataException($"Missing required JSON object '{key}'.");

    private static string GetRequiredString(JsonObject node, string key)
        => node[key]?.GetValue<string>() is { Length: > 0 } value
            ? value
            : throw new InvalidDataException($"Missing required string '{key}'.");

    private static double GetRequiredDouble(JsonObject node, string key)
    {
        var valueNode = node[key];
        if (valueNode is null)
        {
            throw new InvalidDataException($"Missing required numeric value '{key}'.");
        }

        try
        {
            return valueNode.GetValue<double>();
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            throw new InvalidDataException($"Value '{key}' must be numeric.", exception);
        }
    }
}
