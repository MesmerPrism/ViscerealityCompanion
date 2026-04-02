using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class SussexControllerBreathingTuningCompiler
{
    public const string ExpectedSchemaVersion = "sussex-controller-breathing-tuning/v1";
    public const string ExpectedDocumentKind = "sussex_controller_breathing_tuning";
    public const string ExpectedPackageId = "com.Viscereality.SussexExperiment";

    private const double ComparisonTolerance = 0.000001d;
    private static readonly TimeSpan ConfirmationGracePeriod = TimeSpan.FromSeconds(4);

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly JsonObject _templateRoot;
    private readonly IReadOnlyList<string> _controlOrder = Array.Empty<string>();

    public SussexControllerBreathingTuningCompiler(string templateJson)
    {
        if (string.IsNullOrWhiteSpace(templateJson))
        {
            throw new InvalidDataException("The Sussex controller-breathing tuning template is empty.");
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(templateJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The Sussex controller-breathing tuning template is not valid JSON: {exception.Message}", exception);
        }

        _templateRoot = rootNode as JsonObject
            ?? throw new InvalidDataException("The Sussex controller-breathing tuning template must be a JSON object.");
        TemplateDocument = ParseDocument(_templateRoot, validateAgainstTemplate: false);
        _controlOrder = TemplateDocument.Controls.Select(control => control.Id).ToArray();
    }

    public SussexControllerBreathingTuningDocument TemplateDocument { get; }

    public SussexControllerBreathingTuningDocument CreateDocument(
        string? profileName,
        string? profileNotes,
        IReadOnlyDictionary<string, double>? controlValues = null)
    {
        var controls = new List<SussexControllerBreathingTuningControl>(TemplateDocument.Controls.Count);
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
            Profile = new SussexControllerBreathingTuningProfile(
                string.IsNullOrWhiteSpace(profileName) ? "Sussex Controller Breathing Profile" : profileName.Trim(),
                string.IsNullOrWhiteSpace(profileNotes) ? null : profileNotes.Trim()),
            Controls = controls
        };
    }

    public SussexControllerBreathingTuningDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("The Sussex controller-breathing tuning file is empty.");
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The Sussex controller-breathing tuning file is not valid JSON: {exception.Message}", exception);
        }

        var root = rootNode as JsonObject
            ?? throw new InvalidDataException("The Sussex controller-breathing tuning file must be a JSON object.");
        root = UpgradeLegacyDocument(root);
        ValidateLockedMetadata(root);
        return ParseDocument(root, validateAgainstTemplate: true);
    }

    public string Serialize(SussexControllerBreathingTuningDocument document)
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

    public SussexControllerBreathingTuningCompileResult Compile(SussexControllerBreathingTuningDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateControlPairs(document.Controls);

        var entries = document.Controls
            .Select(control => new RuntimeConfigEntry(control.RuntimeKey, FormatRuntimeValue(control.Type, control.Value)))
            .ToArray();

        return new SussexControllerBreathingTuningCompileResult(document, entries);
    }

    public IReadOnlyDictionary<string, double?> ExtractReportedValues(IReadOnlyDictionary<string, string> reportedTwinState)
    {
        var values = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in TemplateDocument.Controls)
        {
            values[control.Id] = TryParseReportedValue(control, reportedTwinState);
        }

        return values;
    }

    public SussexControllerBreathingConfirmationResult EvaluateConfirmation(
        SussexControllerBreathingProfileApplyRecord applyRecord,
        IReadOnlyDictionary<string, string> reportedTwinState)
    {
        ArgumentNullException.ThrowIfNull(applyRecord);
        ArgumentNullException.ThrowIfNull(reportedTwinState);

        var reportedValues = ExtractReportedValues(reportedTwinState);
        var previousReportedValues = applyRecord.PreviousReportedValues;
        var withinGracePeriod = DateTimeOffset.UtcNow - applyRecord.AppliedAtUtc <= ConfirmationGracePeriod;
        var rows = new List<SussexControllerBreathingConfirmationRow>(TemplateDocument.Controls.Count);
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

            SussexControllerBreathingConfirmationState state;
            if (reportedValue is null)
            {
                state = SussexControllerBreathingConfirmationState.Waiting;
                waiting++;
            }
            else if (ValuesMatch(templateControl, reportedValue.Value, requestedValue))
            {
                state = SussexControllerBreathingConfirmationState.Confirmed;
                confirmed++;
            }
            else if (withinGracePeriod &&
                     previousReportedValue is not null &&
                     ValuesMatch(templateControl, reportedValue.Value, previousReportedValue.Value) &&
                     !ValuesMatch(templateControl, previousReportedValue.Value, requestedValue))
            {
                state = SussexControllerBreathingConfirmationState.Waiting;
                waiting++;
            }
            else
            {
                state = SussexControllerBreathingConfirmationState.Mismatch;
                mismatch++;
            }

            rows.Add(new SussexControllerBreathingConfirmationRow(
                templateControl.Id,
                templateControl.Label,
                requestedValue,
                reportedValue,
                state));
        }

        string summary = mismatch > 0
            ? $"Headset reported {confirmed} confirmed, {waiting} waiting, and {mismatch} mismatched controller-tuning values."
            : waiting > 0
                ? $"Waiting on headset confirmation for {waiting} controller-tuning values; {confirmed} already confirmed."
                : $"Headset confirmed all {confirmed} requested controller-tuning values.";

        return new SussexControllerBreathingConfirmationResult(summary, rows, confirmed, waiting, mismatch);
    }

    public IReadOnlyList<SussexControllerBreathingComparisonRow> BuildComparisonRows(
        SussexControllerBreathingTuningDocument selectedDocument,
        SussexControllerBreathingTuningDocument? compareDocument = null)
    {
        ArgumentNullException.ThrowIfNull(selectedDocument);

        var selectedControls = selectedDocument.Controls.ToDictionary(control => control.Id, StringComparer.OrdinalIgnoreCase);
        var compareControls = compareDocument?.Controls.ToDictionary(control => control.Id, StringComparer.OrdinalIgnoreCase);
        var rows = new List<SussexControllerBreathingComparisonRow>(TemplateDocument.Controls.Count);

        foreach (var templateControl in TemplateDocument.Controls)
        {
            var selectedControl = selectedControls[templateControl.Id];
            SussexControllerBreathingTuningControl? compareControl = null;
            if (compareControls is not null)
            {
                compareControls.TryGetValue(templateControl.Id, out compareControl);
            }

            double? compareValue = compareControl?.Value;
            rows.Add(new SussexControllerBreathingComparisonRow(
                templateControl.Id,
                templateControl.Group,
                templateControl.Label,
                templateControl.Type,
                templateControl.BaselineValue,
                selectedControl.Value,
                compareValue,
                selectedControl.Value - templateControl.BaselineValue,
                compareValue is null ? null : selectedControl.Value - compareValue.Value));
        }

        return rows;
    }

    private SussexControllerBreathingTuningDocument ParseDocument(JsonObject root, bool validateAgainstTemplate)
    {
        var schemaVersion = GetRequiredString(root, "schemaVersion");
        var documentKind = GetRequiredString(root, "documentKind");
        var study = GetRequiredObject(root, "study");
        var profile = GetRequiredObject(root, "profile");

        var packageId = GetRequiredString(study, "packageId");
        var baselineHotloadProfileId = GetRequiredString(study, "baselineHotloadProfileId");

        if (validateAgainstTemplate)
        {
            if (!string.Equals(schemaVersion, TemplateDocument.SchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported Sussex controller-breathing tuning schema '{schemaVersion}'. Expected '{TemplateDocument.SchemaVersion}'.");
            }

            if (!string.Equals(documentKind, TemplateDocument.DocumentKind, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unexpected Sussex controller-breathing tuning document kind '{documentKind}'. Expected '{TemplateDocument.DocumentKind}'.");
            }

            if (!string.Equals(packageId, TemplateDocument.PackageId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The tuning file targets '{packageId}', but the Sussex compiler only accepts '{TemplateDocument.PackageId}'.");
            }
        }

        var controlsRoot = GetRequiredObject(root, "controls");
        var expectedControls = _controlOrder.Count == 0 ? GetControlIdsFromObject(controlsRoot) : _controlOrder;
        var controls = new List<SussexControllerBreathingTuningControl>(expectedControls.Count);
        foreach (var controlId in expectedControls)
        {
            controls.Add(ParseControl(GetRequiredObject(controlsRoot, controlId)));
        }

        ValidateControlPairs(controls);

        return new SussexControllerBreathingTuningDocument(
            schemaVersion,
            documentKind,
            packageId,
            baselineHotloadProfileId,
            new SussexControllerBreathingTuningProfile(
                GetRequiredString(profile, "name"),
                GetOptionalString(profile, "notes")),
            controls);
    }

    private SussexControllerBreathingTuningControl ParseControl(JsonObject node)
    {
        var id = GetRequiredString(node, "id");
        var group = GetRequiredString(node, "group");
        var label = GetRequiredString(node, "label");
        var editable = GetRequiredBoolean(node, "editable");
        var type = GetRequiredString(node, "type");
        var value = GetRequiredControlValue(node, "value", type);
        var baselineValue = GetRequiredControlValue(node, "baselineValue", type);
        var units = GetRequiredString(node, "units");
        var safeRange = GetRequiredObject(node, "safeRange");
        var safeMinimum = GetRequiredDouble(safeRange, "minimum");
        var safeMaximum = GetRequiredDouble(safeRange, "maximum");
        var runtimeMapping = GetRequiredObject(node, "runtimeMapping");
        var runtimeKey = GetRequiredString(runtimeMapping, "hotloadKey");
        var info = GetRequiredObject(node, "info");

        ValidateControlValue(id, value, safeMinimum, safeMaximum, type);

        return new SussexControllerBreathingTuningControl(
            id,
            group,
            label,
            editable,
            value,
            baselineValue,
            type,
            units,
            safeMinimum,
            safeMaximum,
            runtimeKey,
            new SussexControllerBreathingTuningInfo(
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
                "The Sussex controller-breathing tuning file changed locked metadata. Only profile.name, profile.notes, and controls.*.value are editable.");
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
            if (property.Value is not JsonObject control)
            {
                continue;
            }

            var type = GetRequiredString(control, "type");
            control["value"] = type switch
            {
                "bool" => false,
                "int" => 0,
                _ => 0d
            };
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
            return NearlyEqual(leftDouble, rightDouble);
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

    private static double NormalizeValue(string type, double value)
        => type switch
        {
            "bool" => value >= 0.5d ? 1d : 0d,
            "int" => Math.Round(value, MidpointRounding.AwayFromZero),
            _ => value
        };

    private static string FormatRuntimeValue(string type, double value)
        => type switch
        {
            "bool" => value >= 0.5d ? "true" : "false",
            "int" => ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture),
            _ => value.ToString("0.######", CultureInfo.InvariantCulture)
        };

    private double? TryParseReportedValue(
        SussexControllerBreathingTuningControl control,
        IReadOnlyDictionary<string, string> reportedTwinState)
    {
        if (TryGetTwinStateValue(control.RuntimeKey, reportedTwinState, out var rawValue))
        {
            return ParseRuntimeValue(control.Type, rawValue);
        }

        return null;
    }

    private static bool TryGetTwinStateValue(
        string runtimeKey,
        IReadOnlyDictionary<string, string> reportedTwinState,
        out string rawValue)
    {
        if (reportedTwinState.TryGetValue(runtimeKey, out var directRawValue) && !string.IsNullOrWhiteSpace(directRawValue))
        {
            rawValue = directRawValue;
            return true;
        }

        var prefixedKey = "hotload." + runtimeKey;
        if (reportedTwinState.TryGetValue(prefixedKey, out var prefixedRawValue) && !string.IsNullOrWhiteSpace(prefixedRawValue))
        {
            rawValue = prefixedRawValue;
            return true;
        }

        rawValue = string.Empty;
        return false;
    }

    private static double? ParseRuntimeValue(string type, string rawValue)
    {
        var token = rawValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseBoolToken(token, out var boolValue)
                ? boolValue ? 1d : 0d
                : null;
        }

        if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue)
                ? intValue
                : null;
        }

        return double.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue)
            ? doubleValue
            : null;
    }

    private static bool TryParseBoolToken(string token, out bool value)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
            case "yes":
            case "y":
            case "on":
                value = true;
                return true;
            case "0":
            case "false":
            case "no":
            case "n":
            case "off":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static void ValidateControlPairs(IReadOnlyList<SussexControllerBreathingTuningControl> controls)
    {
        ValidatePair(controls, "lower_quantile", "upper_quantile");
        ValidatePair(controls, "short_window", "long_window");
    }

    private static void ValidatePair(
        IReadOnlyList<SussexControllerBreathingTuningControl> controls,
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
            throw new InvalidDataException($"controls.{controlId}.value must be an integer.");
        }

        if (value < safeMinimum || value > safeMaximum)
        {
            throw new InvalidDataException(
                $"controls.{controlId}.value must stay within {safeMinimum.ToString("0.###", CultureInfo.InvariantCulture)} .. {safeMaximum.ToString("0.###", CultureInfo.InvariantCulture)}.");
        }
    }

    private static bool ValuesMatch(
        SussexControllerBreathingTuningControl control,
        double left,
        double right)
    {
        if (string.Equals(control.Type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return (left >= 0.5d) == (right >= 0.5d);
        }

        if (string.Equals(control.Type, "int", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Round(left, MidpointRounding.AwayFromZero) == Math.Round(right, MidpointRounding.AwayFromZero);
        }

        return NearlyEqual(left, right);
    }

    private static bool NearlyEqual(double left, double right)
        => Math.Abs(left - right) <= ComparisonTolerance;

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

    private static IReadOnlyList<string> GetRequiredStringArray(JsonObject node, string key)
    {
        if (node[key] is not JsonArray array)
        {
            throw new InvalidDataException($"Missing required string array '{key}'.");
        }

        var values = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is null)
            {
                continue;
            }

            try
            {
                values.Add(item.GetValue<string>());
            }
            catch (Exception exception) when (exception is FormatException or InvalidOperationException)
            {
                throw new InvalidDataException($"Array '{key}' must contain only strings.", exception);
            }
        }

        return values;
    }

    private static double GetRequiredControlValue(JsonObject node, string key, string type)
    {
        if (node[key] is null)
        {
            throw new InvalidDataException($"Missing required value '{key}'.");
        }

        try
        {
            if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase))
            {
                return node[key]!.GetValue<bool>() ? 1d : 0d;
            }

            if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase))
            {
                return node[key]!.GetValue<int>();
            }

            return node[key]!.GetValue<double>();
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            throw new InvalidDataException($"Value '{key}' must be a {type}.", exception);
        }
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
}
