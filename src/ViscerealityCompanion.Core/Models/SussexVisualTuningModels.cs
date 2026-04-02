using System.Globalization;

namespace ViscerealityCompanion.Core.Models;

public sealed record SussexVisualTuningProfile(
    string Name,
    string? Notes);

public sealed record SussexVisualTuningInfo(
    string Effect,
    string IncreaseLooksLike,
    string DecreaseLooksLike,
    IReadOnlyList<string> Tradeoffs);

public sealed record SussexVisualTuningControl(
    string Id,
    string Label,
    bool Editable,
    double Value,
    double BaselineValue,
    string Type,
    string Units,
    double SafeMinimum,
    double SafeMaximum,
    string RuntimeJsonField,
    SussexVisualTuningInfo Info)
{
    public string BaselineLabel => BaselineValue.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed record SussexVisualTuningDocument(
    string SchemaVersion,
    string DocumentKind,
    string PackageId,
    string BaselineHotloadProfileId,
    string HotloadTargetKey,
    SussexVisualTuningProfile Profile,
    IReadOnlyList<SussexVisualTuningControl> Controls)
{
    public IReadOnlyDictionary<string, double> ControlValues
        => Controls.ToDictionary(control => control.Id, control => control.Value, StringComparer.OrdinalIgnoreCase);
}

public sealed record SussexVisualTuningCompileResult(
    SussexVisualTuningDocument Document,
    string CompactRuntimeConfigJson,
    string PrettyRuntimeConfigJson,
    string HotloadTargetKey);

public sealed record SussexVisualProfileRecord(
    string Id,
    string FilePath,
    string FileHash,
    DateTimeOffset ModifiedAtUtc,
    SussexVisualTuningDocument Document);

public sealed record SussexVisualProfileStartupState(
    string ProfileId,
    string ProfileName,
    DateTimeOffset UpdatedAtUtc);

public sealed record SussexVisualProfileApplyRecord(
    string ProfileId,
    string ProfileName,
    string FileHash,
    string CompiledJsonHash,
    DateTimeOffset AppliedAtUtc,
    IReadOnlyDictionary<string, double> RequestedValues,
    IReadOnlyDictionary<string, double?>? PreviousReportedValues);

public enum SussexVisualConfirmationState
{
    Waiting = 0,
    Confirmed = 1,
    Mismatch = 2
}

public sealed record SussexVisualConfirmationRow(
    string Id,
    string Label,
    double RequestedValue,
    double? ReportedValue,
    SussexVisualConfirmationState State);

public sealed record SussexVisualConfirmationResult(
    string Summary,
    IReadOnlyList<SussexVisualConfirmationRow> Rows,
    int ConfirmedCount,
    int WaitingCount,
    int MismatchCount);

public sealed record SussexVisualComparisonRow(
    string Id,
    string Label,
    double BaselineValue,
    double SelectedValue,
    double? CompareValue,
    double DeltaFromBaseline,
    double? DeltaBetweenProfiles);
