using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class SussexVisualTuningCompiler
{
    public const string ExpectedSchemaVersion = "sussex-visual-tuning/v1";
    public const string ExpectedDocumentKind = "sussex_visual_tuning";
    public const string ExpectedPackageId = "com.Viscereality.SussexExperiment";
    public const string ExpectedHotloadTargetKey = "showcase_active_runtime_config_json";

    private const double ComparisonTolerance = 0.000001d;
    private const string TracersEnabledControlId = "tracers_enabled";
    private const string TracersLifetimeControlId = "tracers_lifetime_seconds";
    private const string TracersPerOscillatorControlId = "tracers_per_oscillator";
    private const string IntegratedTracersEnabledKey = "integrated_tracers_enabled";
    private const string IntegratedTracersVisualsEnabledKey = "integrated_tracers_visuals_enabled";
    private const string IntegratedTracersLifetimeSecondsKey = "integrated_tracers_lifetime_seconds";
    private const string IntegratedTracersCopiesPerSecondKey = "integrated_tracers_copies_per_second";
    private const string IntegratedTracersMaxSpawnBatchesPerFrameKey = "integrated_tracers_max_spawn_batches_per_frame";
    private const string IntegratedTracersPerOscillatorKey = "integrated_tracers_per_oscillator";
    private const string IntegratedTracersSizeMultiplierKey = "integrated_tracers_size_multiplier";
    private const int FixedTracerMaxSpawnBatchesPerFrame = 4;
    private const double FixedTracerSizeMultiplier = 1d;
    private static readonly TimeSpan ConfirmationGracePeriod = TimeSpan.FromSeconds(4);

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly JsonObject _templateRoot;
    private readonly IReadOnlyList<string> _controlOrder = Array.Empty<string>();

    public SussexVisualTuningCompiler(string templateJson)
    {
        if (string.IsNullOrWhiteSpace(templateJson))
        {
            throw new InvalidDataException("The Sussex visual tuning template is empty.");
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(templateJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The Sussex visual tuning template is not valid JSON: {exception.Message}", exception);
        }

        _templateRoot = rootNode as JsonObject
            ?? throw new InvalidDataException("The Sussex visual tuning template must be a JSON object.");
        TemplateDocument = ParseDocument(_templateRoot, validateAgainstTemplate: false);
        _controlOrder = TemplateDocument.Controls.Select(control => control.Id).ToArray();
    }

    public SussexVisualTuningDocument TemplateDocument { get; }

    public SussexVisualTuningDocument CreateDocument(
        string? profileName,
        string? profileNotes,
        IReadOnlyDictionary<string, double>? controlValues = null)
    {
        var controls = new List<SussexVisualTuningControl>(TemplateDocument.Controls.Count);
        foreach (var templateControl in TemplateDocument.Controls)
        {
            double nextValue = controlValues is not null && controlValues.TryGetValue(templateControl.Id, out var explicitValue)
                ? explicitValue
                : templateControl.Value;
            nextValue = NormalizeValue(templateControl.Type, nextValue);
            ValidateControlValue(
                templateControl.Id,
                nextValue,
                templateControl.SafeMinimum,
                templateControl.SafeMaximum,
                templateControl.Type);

            controls.Add(templateControl with
            {
                Value = nextValue
            });
        }

        ValidateControlPairs(controls);

        return TemplateDocument with
        {
            Profile = new SussexVisualTuningProfile(
                string.IsNullOrWhiteSpace(profileName) ? "Sussex Visual Profile" : profileName.Trim(),
                string.IsNullOrWhiteSpace(profileNotes) ? null : profileNotes.Trim()),
            Controls = controls
        };
    }

    public SussexVisualTuningDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("The Sussex visual tuning file is empty.");
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The Sussex visual tuning file is not valid JSON: {exception.Message}", exception);
        }

        var root = rootNode as JsonObject
            ?? throw new InvalidDataException("The Sussex visual tuning file must be a JSON object.");
        root = UpgradeLegacyDocument(root);
        ValidateLockedMetadata(root);
        return ParseDocument(root, validateAgainstTemplate: true);
    }

    public string Serialize(SussexVisualTuningDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = (JsonObject)_templateRoot.DeepClone();

        var profile = GetRequiredObject(root, "profile");
        profile["name"] = document.Profile.Name;
        profile["notes"] = document.Profile.Notes ?? string.Empty;

        var controls = GetRequiredObject(root, "controls");
        foreach (var control in document.Controls)
        {
            var node = GetRequiredObject(controls, control.Id);
            node["value"] = control.Type switch
            {
                "bool" => control.Value >= 0.5d,
                "int" => (int)Math.Round(control.Value, MidpointRounding.AwayFromZero),
                _ => control.Value
            };
        }

        return root.ToJsonString(PrettyJsonOptions);
    }

    public SussexVisualTuningCompileResult Compile(SussexVisualTuningDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateControlPairs(document.Controls);

        var payload = new JsonObject
        {
            ["UseSphereDeformation"] = GetBooleanControlValue(document, "sphere_deformation_enabled"),
            ["OblatenessByRadiusCurveLimits"] = BuildLimits(
                document,
                "oblateness_by_radius_min",
                "oblateness_by_radius_max"),
            ["SharedRadiusLimits"] = BuildLimits(
                document,
                "sphere_radius_min",
                "sphere_radius_max"),
            ["UsePercentSize"] = GetBooleanControlValue(document, "particle_size_relative_to_radius"),
            ["ParticleSizeEnvelopeLimits"] = BuildLimits(
                document,
                "particle_size_min",
                "particle_size_max"),
            ["DepthWavePercentLimits"] = BuildLimits(
                document,
                "depth_wave_min",
                "depth_wave_max"),
            ["TransparencyLimits"] = BuildLimits(
                document,
                "transparency_min",
                "transparency_max"),
            ["GlobalSaturationLimits"] = BuildLimits(
                document,
                "saturation_min",
                "saturation_max"),
            ["GlobalBrightnessLimits"] = BuildLimits(
                document,
                "brightness_min",
                "brightness_max"),
            ["OrbitRadiusMultiplierLimits"] = BuildLimits(
                document,
                "orbit_distance_min",
                "orbit_distance_max")
        };

        var compactRuntimeConfigJson = payload.ToJsonString();
        var entries = new List<RuntimeConfigEntry>
        {
            new(document.HotloadTargetKey, compactRuntimeConfigJson)
        };
        AppendTracerEntries(document.Controls, entries);

        return new SussexVisualTuningCompileResult(
            document,
            compactRuntimeConfigJson,
            payload.ToJsonString(PrettyJsonOptions),
            document.HotloadTargetKey,
            entries);
    }

    public IReadOnlyDictionary<string, double?> ExtractRuntimeValues(
        string runtimeConfigJson,
        IReadOnlyDictionary<string, string>? reportedTwinState = null)
    {
        var values = BuildEmptyRuntimeValues().ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);

        var sanitizedJson = SanitizeRuntimeConfigJson(runtimeConfigJson);
        if (string.IsNullOrWhiteSpace(sanitizedJson))
        {
            OverlayTracerReportedValues(values, reportedTwinState);
            return values;
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(sanitizedJson);
        }
        catch (JsonException)
        {
            OverlayTracerReportedValues(values, reportedTwinState);
            return values;
        }

        if (rootNode is not JsonObject root)
        {
            OverlayTracerReportedValues(values, reportedTwinState);
            return values;
        }

        values["sphere_deformation_enabled"] = TryGetBoolean01(root, "UseSphereDeformation");
        values["oblateness_by_radius_min"] = TryGetNestedDouble(root, "OblatenessByRadiusCurveLimits", "x");
        values["oblateness_by_radius_max"] = TryGetNestedDouble(root, "OblatenessByRadiusCurveLimits", "y");
        values["sphere_radius_min"] = TryGetNestedDouble(root, "SharedRadiusLimits", "x");
        values["sphere_radius_max"] = TryGetNestedDouble(root, "SharedRadiusLimits", "y");
        values["particle_size_relative_to_radius"] = TryGetBoolean01(root, "UsePercentSize");
        values["particle_size_min"] = TryGetNestedDouble(root, "ParticleSizeEnvelopeLimits", "x");
        values["particle_size_max"] = TryGetNestedDouble(root, "ParticleSizeEnvelopeLimits", "y");
        values["depth_wave_min"] = TryGetNestedDouble(root, "DepthWavePercentLimits", "x");
        values["depth_wave_max"] = TryGetNestedDouble(root, "DepthWavePercentLimits", "y");
        values["transparency_min"] = TryGetNestedDouble(root, "TransparencyLimits", "x");
        values["transparency_max"] = TryGetNestedDouble(root, "TransparencyLimits", "y");
        values["saturation_min"] = TryGetNestedDouble(root, "GlobalSaturationLimits", "x");
        values["saturation_max"] = TryGetNestedDouble(root, "GlobalSaturationLimits", "y");
        values["brightness_min"] = TryGetNestedDouble(root, "GlobalBrightnessLimits", "x");
        values["brightness_max"] = TryGetNestedDouble(root, "GlobalBrightnessLimits", "y");
        values["orbit_distance_min"] = TryGetNestedDouble(root, "OrbitRadiusMultiplierLimits", "x");
        values["orbit_distance_max"] = TryGetNestedDouble(root, "OrbitRadiusMultiplierLimits", "y");
        OverlayTracerReportedValues(values, reportedTwinState);

        return values;
    }

    public SussexVisualConfirmationResult EvaluateConfirmation(
        SussexVisualProfileApplyRecord applyRecord,
        string? reportedRuntimeConfigJson,
        IReadOnlyDictionary<string, string>? reportedTwinState = null)
    {
        ArgumentNullException.ThrowIfNull(applyRecord);
        var reportedValues = ExtractRuntimeValues(reportedRuntimeConfigJson ?? string.Empty, reportedTwinState);
        var previousReportedValues = applyRecord.PreviousReportedValues;
        var withinGracePeriod = DateTimeOffset.UtcNow - applyRecord.AppliedAtUtc <= ConfirmationGracePeriod;
        var rows = new List<SussexVisualConfirmationRow>(TemplateDocument.Controls.Count);
        var confirmed = 0;
        var waiting = 0;
        var mismatch = 0;

        foreach (var templateControl in TemplateDocument.Controls)
        {
            applyRecord.RequestedValues.TryGetValue(templateControl.Id, out var requestedValue);
            reportedValues.TryGetValue(templateControl.Id, out var reportedValue);
            double? previousReportedValue = null;
            if (previousReportedValues is not null &&
                previousReportedValues.TryGetValue(templateControl.Id, out var previousValue))
            {
                previousReportedValue = previousValue;
            }

            SussexVisualConfirmationState state;
            if (reportedValue is null)
            {
                state = SussexVisualConfirmationState.Waiting;
                waiting++;
            }
            else if (NearlyEqual(reportedValue.Value, requestedValue))
            {
                state = SussexVisualConfirmationState.Confirmed;
                confirmed++;
            }
            else if (withinGracePeriod &&
                     previousReportedValue is not null &&
                     NearlyEqual(reportedValue.Value, previousReportedValue.Value) &&
                     !NearlyEqual(previousReportedValue.Value, requestedValue))
            {
                state = SussexVisualConfirmationState.Waiting;
                waiting++;
            }
            else
            {
                state = SussexVisualConfirmationState.Mismatch;
                mismatch++;
            }

            rows.Add(new SussexVisualConfirmationRow(
                templateControl.Id,
                templateControl.Label,
                requestedValue,
                reportedValue,
                state));
        }

        string summary = mismatch > 0
            ? $"Headset reported {confirmed} confirmed, {waiting} waiting, and {mismatch} mismatched Sussex parameter values."
            : waiting > 0
                ? $"Waiting on headset confirmation for {waiting} Sussex parameter values; {confirmed} already confirmed."
                : $"Headset confirmed all {confirmed} requested Sussex parameter values.";

        return new SussexVisualConfirmationResult(summary, rows, confirmed, waiting, mismatch);
    }

    public IReadOnlyList<SussexVisualComparisonRow> BuildComparisonRows(
        SussexVisualTuningDocument selectedDocument,
        SussexVisualTuningDocument? compareDocument = null)
    {
        ArgumentNullException.ThrowIfNull(selectedDocument);
        var selectedControls = selectedDocument.Controls.ToDictionary(control => control.Id, StringComparer.OrdinalIgnoreCase);
        var compareControls = compareDocument?.Controls.ToDictionary(control => control.Id, StringComparer.OrdinalIgnoreCase);
        var rows = new List<SussexVisualComparisonRow>(TemplateDocument.Controls.Count);

        foreach (var templateControl in TemplateDocument.Controls)
        {
            var selectedControl = selectedControls[templateControl.Id];
            SussexVisualTuningControl? compareControl = null;
            if (compareControls is not null)
            {
                compareControls.TryGetValue(templateControl.Id, out compareControl);
            }

            double? compareValue = compareControl?.Value;
            rows.Add(new SussexVisualComparisonRow(
                templateControl.Id,
                templateControl.Label,
                templateControl.BaselineValue,
                selectedControl.Value,
                compareValue,
                selectedControl.Value - templateControl.BaselineValue,
                compareValue is null ? null : selectedControl.Value - compareValue.Value));
        }

        return rows;
    }

    private SussexVisualTuningDocument ParseDocument(JsonObject root, bool validateAgainstTemplate)
    {
        var schemaVersion = GetRequiredString(root, "schemaVersion");
        var documentKind = GetRequiredString(root, "documentKind");
        var study = GetRequiredObject(root, "study");
        var profile = GetRequiredObject(root, "profile");
        var compilerHints = GetRequiredObject(root, "compilerHints");

        var packageId = GetRequiredString(study, "packageId");
        var baselineHotloadProfileId = GetRequiredString(study, "baselineHotloadProfileId");
        var hotloadTargetKey = GetRequiredString(compilerHints, "hotloadTargetKey");

        if (validateAgainstTemplate)
        {
            if (!string.Equals(schemaVersion, TemplateDocument.SchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported Sussex visual tuning schema '{schemaVersion}'. Expected '{TemplateDocument.SchemaVersion}'.");
            }

            if (!string.Equals(documentKind, TemplateDocument.DocumentKind, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unexpected Sussex visual tuning document kind '{documentKind}'. Expected '{TemplateDocument.DocumentKind}'.");
            }

            if (!string.Equals(packageId, TemplateDocument.PackageId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The tuning file targets '{packageId}', but the Sussex compiler only accepts '{TemplateDocument.PackageId}'.");
            }

            if (!string.Equals(hotloadTargetKey, TemplateDocument.HotloadTargetKey, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unexpected hotload target key '{hotloadTargetKey}'. Expected '{TemplateDocument.HotloadTargetKey}'.");
            }
        }

        var controlsRoot = GetRequiredObject(root, "controls");
        var controls = new List<SussexVisualTuningControl>(_controlOrder.Count == 0 ? 8 : _controlOrder.Count);

        var expectedControls = _controlOrder.Count == 0
            ? GetControlIdsFromObject(controlsRoot)
            : _controlOrder;
        foreach (var controlId in expectedControls)
        {
            controls.Add(ParseControl(GetRequiredObject(controlsRoot, controlId)));
        }

        ValidateControlPairs(controls);

        return new SussexVisualTuningDocument(
            schemaVersion,
            documentKind,
            packageId,
            baselineHotloadProfileId,
            hotloadTargetKey,
            new SussexVisualTuningProfile(
                GetRequiredString(profile, "name"),
                GetOptionalString(profile, "notes")),
            controls);
    }

    private SussexVisualTuningControl ParseControl(JsonObject node)
    {
        var id = GetRequiredString(node, "id");
        var label = GetRequiredString(node, "label");
        var editable = GetRequiredBoolean(node, "editable");
        var type = GetRequiredString(node, "type");
        var value = NormalizeValue(type, GetRequiredControlValue(node, "value", type));
        var baselineValue = NormalizeValue(type, GetRequiredControlValue(node, "baselineValue", type));
        var units = GetRequiredString(node, "units");
        var safeRange = GetRequiredObject(node, "safeRange");
        var safeMinimum = GetRequiredDouble(safeRange, "minimum");
        var safeMaximum = GetRequiredDouble(safeRange, "maximum");
        var runtimeMapping = GetRequiredObject(node, "runtimeMapping");
        var runtimeJsonField = GetRequiredString(runtimeMapping, "compiledUnityJsonField");
        var info = GetRequiredObject(node, "info");

        ValidateControlValue(id, value, safeMinimum, safeMaximum, type);

        return new SussexVisualTuningControl(
            id,
            label,
            editable,
            value,
            baselineValue,
            type,
            units,
            safeMinimum,
            safeMaximum,
            runtimeJsonField,
            new SussexVisualTuningInfo(
                GetRequiredString(info, "effect"),
                GetRequiredString(info, "increaseLooksLike"),
                GetRequiredString(info, "decreaseLooksLike"),
                GetRequiredStringArray(info, "tradeoffs")));
    }

    private void ValidateLockedMetadata(JsonObject candidateRoot)
    {
        var normalizedTemplate = (JsonObject)_templateRoot.DeepClone();
        var normalizedCandidate = (JsonObject)candidateRoot.DeepClone();
        NormalizeEditableFields(normalizedTemplate);
        NormalizeEditableFields(normalizedCandidate);

        if (!JsonNodesEqual(normalizedTemplate, normalizedCandidate))
        {
            throw new InvalidDataException(
                "The Sussex visual tuning file changed locked metadata. Only profile.name, profile.notes, and controls.*.value are editable.");
        }
    }

    private static void NormalizeEditableFields(JsonObject root)
    {
        var profile = GetRequiredObject(root, "profile");
        profile["name"] = string.Empty;
        profile["notes"] = string.Empty;

        var controls = GetRequiredObject(root, "controls");
        foreach (var property in controls)
        {
            if (property.Value is JsonObject control)
            {
                var type = GetRequiredString(control, "type");
                control["value"] = string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase)
                    ? false
                    : 0d;
            }
        }
    }

    private static bool JsonNodesEqual(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.GetType() != right.GetType())
        {
            return false;
        }

        return left switch
        {
            JsonObject leftObject when right is JsonObject rightObject => JsonObjectsEqual(leftObject, rightObject),
            JsonArray leftArray when right is JsonArray rightArray => JsonArraysEqual(leftArray, rightArray),
            JsonValue => JsonValueEquals(left, right),
            _ => JsonValueEquals(left, right)
        };
    }

    private static bool JsonObjectsEqual(JsonObject left, JsonObject right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var property in left)
        {
            if (!right.TryGetPropertyValue(property.Key, out var otherValue))
            {
                return false;
            }

            if (!JsonNodesEqual(property.Value, otherValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool JsonArraysEqual(JsonArray left, JsonArray right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!JsonNodesEqual(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool JsonValueEquals(JsonNode left, JsonNode right)
    {
        if (TryGetValue<double>(left, out var leftDouble) && TryGetValue<double>(right, out var rightDouble))
        {
            return Math.Abs(leftDouble - rightDouble) <= ComparisonTolerance;
        }

        if (TryGetValue<bool>(left, out var leftBool) && TryGetValue<bool>(right, out var rightBool))
        {
            return leftBool == rightBool;
        }

        if (TryGetValue<string>(left, out var leftString) && TryGetValue<string>(right, out var rightString))
        {
            return string.Equals(leftString, rightString, StringComparison.Ordinal);
        }

        return string.Equals(left.ToJsonString(), right.ToJsonString(), StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetControlIdsFromObject(JsonObject controlsRoot)
        => controlsRoot.Select(property => property.Key).ToArray();

    private static double NormalizeValue(string type, double value)
    {
        if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return value >= 0.5d ? 1d : 0d;
        }

        if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Round(value, MidpointRounding.AwayFromZero);
        }

        return value;
    }

    private static bool TryGetValue<T>(JsonNode node, out T value)
    {
        try
        {
            value = node.GetValue<T>();
            return true;
        }
        catch
        {
            value = default!;
            return false;
        }
    }

    private static JsonObject BuildLimits(
        SussexVisualTuningDocument document,
        string minimumControlId,
        string maximumControlId)
    {
        var controls = document.Controls.ToDictionary(control => control.Id, StringComparer.OrdinalIgnoreCase);
        return new JsonObject
        {
            ["x"] = controls[minimumControlId].Value,
            ["y"] = controls[maximumControlId].Value
        };
    }

    private static bool GetBooleanControlValue(
        SussexVisualTuningDocument document,
        string controlId)
    {
        var control = document.Controls.First(candidate => string.Equals(candidate.Id, controlId, StringComparison.OrdinalIgnoreCase));
        return control.Value >= 0.5d;
    }

    private static void AppendTracerEntries(
        IReadOnlyList<SussexVisualTuningControl> controls,
        ICollection<RuntimeConfigEntry> entries)
    {
        if (!TryGetControlValue(controls, TracersEnabledControlId, out var tracersEnabledValue) ||
            !TryGetControlValue(controls, TracersLifetimeControlId, out var tracersLifetimeValue) ||
            !TryGetControlValue(controls, TracersPerOscillatorControlId, out var tracersPerOscillatorValue))
        {
            return;
        }

        var tracersEnabled = tracersEnabledValue >= 0.5d;
        var lifetimeSeconds = Math.Max(0.001d, tracersLifetimeValue);
        var perOscillator = Math.Max(1, (int)Math.Round(tracersPerOscillatorValue, MidpointRounding.AwayFromZero));
        var copiesPerSecond = perOscillator / lifetimeSeconds;

        entries.Add(new RuntimeConfigEntry(IntegratedTracersEnabledKey, FormatRuntimeValue("bool", tracersEnabled ? 1d : 0d)));
        entries.Add(new RuntimeConfigEntry(IntegratedTracersVisualsEnabledKey, FormatRuntimeValue("bool", tracersEnabled ? 1d : 0d)));
        entries.Add(new RuntimeConfigEntry(IntegratedTracersLifetimeSecondsKey, FormatRuntimeValue("float", lifetimeSeconds)));
        entries.Add(new RuntimeConfigEntry(IntegratedTracersCopiesPerSecondKey, FormatRuntimeValue("float", copiesPerSecond)));
        entries.Add(new RuntimeConfigEntry(IntegratedTracersMaxSpawnBatchesPerFrameKey, FormatRuntimeValue("int", FixedTracerMaxSpawnBatchesPerFrame)));
        entries.Add(new RuntimeConfigEntry(IntegratedTracersPerOscillatorKey, FormatRuntimeValue("int", perOscillator)));
        entries.Add(new RuntimeConfigEntry(IntegratedTracersSizeMultiplierKey, FormatRuntimeValue("float", FixedTracerSizeMultiplier)));
    }

    private static void OverlayTracerReportedValues(
        IDictionary<string, double?> values,
        IReadOnlyDictionary<string, string>? reportedTwinState)
    {
        if (reportedTwinState is null || reportedTwinState.Count == 0)
        {
            return;
        }

        if (values.ContainsKey(TracersEnabledControlId))
        {
            values[TracersEnabledControlId] =
                TryGetReportedBoolean01(reportedTwinState, IntegratedTracersEnabledKey) ??
                TryGetReportedBoolean01(reportedTwinState, IntegratedTracersVisualsEnabledKey);
        }

        if (values.ContainsKey(TracersLifetimeControlId))
        {
            values[TracersLifetimeControlId] = TryGetReportedDouble(reportedTwinState, IntegratedTracersLifetimeSecondsKey);
        }

        if (values.ContainsKey(TracersPerOscillatorControlId))
        {
            values[TracersPerOscillatorControlId] = TryGetReportedDouble(reportedTwinState, IntegratedTracersPerOscillatorKey);
        }
    }

    private static bool TryGetControlValue(
        IReadOnlyList<SussexVisualTuningControl> controls,
        string controlId,
        out double value)
    {
        var control = controls.FirstOrDefault(candidate => string.Equals(candidate.Id, controlId, StringComparison.OrdinalIgnoreCase));
        if (control is null)
        {
            value = 0d;
            return false;
        }

        value = control.Value;
        return true;
    }

    private static string FormatRuntimeValue(string type, double value)
    {
        if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return value >= 0.5d ? "true" : "false";
        }

        if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static double? TryGetNestedDouble(JsonObject root, string parentKey, string childKey)
    {
        if (root[parentKey] is not JsonObject parent || parent[childKey] is null)
        {
            return null;
        }

        try
        {
            return parent[childKey]!.GetValue<double>();
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static double? TryGetBoolean01(JsonObject root, string key)
    {
        if (root[key] is null)
        {
            return null;
        }

        try
        {
            return root[key]!.GetValue<bool>() ? 1d : 0d;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            try
            {
                return root[key]!.GetValue<double>();
            }
            catch (Exception innerException) when (innerException is FormatException or InvalidOperationException)
            {
                return null;
            }
        }
    }

    private static double? TryGetReportedBoolean01(
        IReadOnlyDictionary<string, string> reportedTwinState,
        string key)
    {
        if (!TryGetReportedToken(reportedTwinState, key, out var token))
        {
            return null;
        }

        return TryParseBooleanToken(token, out var value)
            ? value ? 1d : 0d
            : null;
    }

    private static double? TryGetReportedDouble(
        IReadOnlyDictionary<string, string> reportedTwinState,
        string key)
    {
        if (!TryGetReportedToken(reportedTwinState, key, out var token))
        {
            return null;
        }

        return double.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static bool TryGetReportedToken(
        IReadOnlyDictionary<string, string> reportedTwinState,
        string key,
        out string token)
    {
        if (reportedTwinState.TryGetValue(key, out var directValue) &&
            !string.IsNullOrWhiteSpace(directValue))
        {
            token = directValue.Trim();
            return true;
        }

        var hotloadKey = "hotload." + key;
        if (reportedTwinState.TryGetValue(hotloadKey, out var hotloadValue) &&
            !string.IsNullOrWhiteSpace(hotloadValue))
        {
            token = hotloadValue.Trim();
            return true;
        }

        token = string.Empty;
        return false;
    }

    private static bool TryParseBooleanToken(string token, out bool value)
    {
        switch ((token ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
            case "on":
            case "yes":
                value = true;
                return true;
            case "0":
            case "false":
            case "off":
            case "no":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static void ValidateControlPairs(IReadOnlyList<SussexVisualTuningControl> controls)
    {
        ValidatePair(controls, "oblateness_by_radius_min", "oblateness_by_radius_max");
        ValidatePair(controls, "sphere_radius_min", "sphere_radius_max");
        ValidatePair(controls, "particle_size_min", "particle_size_max");
        ValidatePair(controls, "depth_wave_min", "depth_wave_max");
        ValidatePair(controls, "transparency_min", "transparency_max");
        ValidatePair(controls, "saturation_min", "saturation_max");
        ValidatePair(controls, "brightness_min", "brightness_max");
        ValidatePair(controls, "orbit_distance_min", "orbit_distance_max");
    }

    private static void ValidatePair(
        IReadOnlyList<SussexVisualTuningControl> controls,
        string minimumControlId,
        string maximumControlId)
    {
        var minimum = controls.First(control => string.Equals(control.Id, minimumControlId, StringComparison.Ordinal));
        var maximum = controls.First(control => string.Equals(control.Id, maximumControlId, StringComparison.Ordinal));
        if (minimum.Value > maximum.Value)
        {
            throw new InvalidDataException($"controls.{minimum.Id}.value must be less than or equal to controls.{maximum.Id}.value.");
        }
    }

    private static void ValidateControlValue(
        string controlId,
        double value,
        double safeMinimum,
        double safeMaximum,
        string type)
    {
        if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase) &&
            !NearlyEqual(value, 0d) &&
            !NearlyEqual(value, 1d))
        {
            throw new InvalidDataException($"controls.{controlId}.value must be either false or true.");
        }

        if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase) &&
            !NearlyEqual(value, Math.Round(value, MidpointRounding.AwayFromZero)))
        {
            throw new InvalidDataException($"controls.{controlId}.value must be a whole number.");
        }

        if (value < safeMinimum || value > safeMaximum)
        {
            throw new InvalidDataException(
                $"controls.{controlId}.value must stay within {safeMinimum.ToString("0.###", CultureInfo.InvariantCulture)} .. {safeMaximum.ToString("0.###", CultureInfo.InvariantCulture)}.");
        }
    }

    private static bool NearlyEqual(double left, double right)
        => Math.Abs(left - right) <= ComparisonTolerance;

    private IReadOnlyDictionary<string, double?> BuildEmptyRuntimeValues()
        => _controlOrder.ToDictionary(
            key => key,
            _ => (double?)null,
            StringComparer.OrdinalIgnoreCase);

    private JsonObject UpgradeLegacyDocument(JsonObject root)
    {
        var upgraded = (JsonObject)root.DeepClone();
        var candidateControls = GetRequiredObject(upgraded, "controls");
        var templateControls = GetRequiredObject(_templateRoot, "controls");

        foreach (var property in templateControls)
        {
            if (candidateControls.ContainsKey(property.Key))
            {
                continue;
            }

            if (property.Value is JsonObject templateControl)
            {
                candidateControls[property.Key] = templateControl.DeepClone();
            }
        }

        return upgraded;
    }

    private static string SanitizeRuntimeConfigJson(string? runtimeConfigJson)
    {
        if (string.IsNullOrWhiteSpace(runtimeConfigJson))
        {
            return string.Empty;
        }

        var trimmed = runtimeConfigJson.Trim();
        trimmed = trimmed.Trim('\0', '\uFEFF', '\u200B', '\u200C', '\u200D', '\u2060');
        return trimmed;
    }

    private static JsonObject GetRequiredObject(JsonObject node, string key)
        => node[key] as JsonObject
           ?? throw new InvalidDataException($"Missing required JSON object '{key}'.");

    private static string GetRequiredString(JsonObject node, string key)
        => node[key]?.GetValue<string>() is { Length: > 0 } value
            ? value
            : throw new InvalidDataException($"Missing required string '{key}'.");

    private static string? GetOptionalString(JsonObject node, string key)
    {
        if (node[key] is null)
        {
            return null;
        }

        try
        {
            var value = node[key]!.GetValue<string>();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            throw new InvalidDataException($"Value '{key}' must be a string.", exception);
        }
    }

    private static bool GetRequiredBoolean(JsonObject node, string key)
    {
        if (node[key] is null)
        {
            throw new InvalidDataException($"Missing required boolean value '{key}'.");
        }

        try
        {
            return node[key]!.GetValue<bool>();
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            throw new InvalidDataException($"Value '{key}' must be a boolean.", exception);
        }
    }

    private static double GetRequiredControlValue(JsonObject node, string key, string type)
    {
        if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            if (node[key] is null)
            {
                throw new InvalidDataException($"Missing required boolean value '{key}'.");
            }

            try
            {
                return node[key]!.GetValue<bool>() ? 1d : 0d;
            }
            catch (Exception exception) when (exception is FormatException or InvalidOperationException)
            {
                try
                {
                    return node[key]!.GetValue<double>();
                }
                catch (Exception innerException) when (innerException is FormatException or InvalidOperationException)
                {
                    throw new InvalidDataException($"Value '{key}' must be a boolean.", innerException);
                }
            }
        }

        return GetRequiredDouble(node, key);
    }

    private static double GetRequiredDouble(JsonObject node, string key)
    {
        if (node[key] is null)
        {
            throw new InvalidDataException($"Missing required numeric value '{key}'.");
        }

        try
        {
            return node[key]!.GetValue<double>();
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            throw new InvalidDataException($"Value '{key}' must be numeric.", exception);
        }
    }

    private static IReadOnlyList<string> GetRequiredStringArray(JsonObject node, string key)
    {
        if (node[key] is not JsonArray array)
        {
            throw new InvalidDataException($"Missing required string array '{key}'.");
        }

        var values = new List<string>(array.Count);
        for (int index = 0; index < array.Count; index++)
        {
            if (array[index] is null)
            {
                throw new InvalidDataException($"Array '{key}' contains an empty item.");
            }

            try
            {
                values.Add(array[index]!.GetValue<string>());
            }
            catch (Exception exception) when (exception is FormatException or InvalidOperationException)
            {
                throw new InvalidDataException($"Array '{key}' must contain only strings.", exception);
            }
        }

        return values;
    }
}
