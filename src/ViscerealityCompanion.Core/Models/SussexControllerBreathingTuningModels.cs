using System.Globalization;

namespace ViscerealityCompanion.Core.Models;

public sealed record SussexControllerBreathingTuningProfile(
    string Name,
    string? Notes);

public sealed record SussexControllerBreathingTuningInfo(
    string Effect,
    string IncreaseLooksLike,
    string DecreaseLooksLike,
    IReadOnlyList<string> Tradeoffs);

public sealed record SussexControllerBreathingTuningControl(
    string Id,
    string Group,
    string Label,
    bool Editable,
    double Value,
    double BaselineValue,
    string Type,
    string Units,
    double SafeMinimum,
    double SafeMaximum,
    string RuntimeKey,
    SussexControllerBreathingTuningInfo Info)
{
    public string BaselineLabel => Type switch
    {
        "bool" => BaselineValue >= 0.5d ? "On" : "Off",
        "int" => Math.Round(BaselineValue).ToString(CultureInfo.InvariantCulture),
        _ => BaselineValue.ToString("0.###", CultureInfo.InvariantCulture)
    };
}

public sealed record SussexControllerBreathingTuningDocument(
    string SchemaVersion,
    string DocumentKind,
    string PackageId,
    string BaselineHotloadProfileId,
    SussexControllerBreathingTuningProfile Profile,
    IReadOnlyList<SussexControllerBreathingTuningControl> Controls)
{
    public IReadOnlyDictionary<string, double> ControlValues
        => Controls.ToDictionary(control => control.Id, control => control.Value, StringComparer.OrdinalIgnoreCase);
}

public sealed record SussexControllerBreathingTuningCompileResult(
    SussexControllerBreathingTuningDocument Document,
    IReadOnlyList<RuntimeConfigEntry> Entries);

public sealed record SussexControllerBreathingProfileRecord(
    string Id,
    string FilePath,
    string FileHash,
    DateTimeOffset ModifiedAtUtc,
    SussexControllerBreathingTuningDocument Document);

public sealed record SussexControllerBreathingProfileStartupState(
    string ProfileId,
    string ProfileName,
    DateTimeOffset UpdatedAtUtc);

public sealed record SussexControllerBreathingProfileApplyRecord(
    string ProfileId,
    string ProfileName,
    string FileHash,
    string CompiledValuesHash,
    DateTimeOffset AppliedAtUtc,
    IReadOnlyDictionary<string, double> RequestedValues,
    IReadOnlyDictionary<string, double?>? PreviousReportedValues);

public enum SussexControllerBreathingConfirmationState
{
    Waiting = 0,
    Confirmed = 1,
    Mismatch = 2
}

public sealed record SussexControllerBreathingConfirmationRow(
    string Id,
    string Label,
    double RequestedValue,
    double? ReportedValue,
    SussexControllerBreathingConfirmationState State);

public sealed record SussexControllerBreathingConfirmationResult(
    string Summary,
    IReadOnlyList<SussexControllerBreathingConfirmationRow> Rows,
    int ConfirmedCount,
    int WaitingCount,
    int MismatchCount);

public sealed record SussexControllerBreathingComparisonRow(
    string Id,
    string Group,
    string Label,
    string Type,
    double BaselineValue,
    double SelectedValue,
    double? CompareValue,
    double DeltaFromBaseline,
    double? DeltaBetweenProfiles);
