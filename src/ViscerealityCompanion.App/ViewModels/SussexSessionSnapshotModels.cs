using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.App.ViewModels;

public sealed record SussexVisualSessionSnapshot(
    bool IsAvailable,
    OperationOutcomeKind ApplyLevel,
    string ApplySummary,
    string ApplyDetail,
    SussexVisualProfileRecord? CurrentProfile,
    SussexVisualProfileRecord? EffectiveProfile,
    SussexVisualProfileStartupState? StartupProfile,
    SussexVisualProfileApplyRecord? LastApplyRecord,
    IReadOnlyDictionary<string, double?> ReportedValues,
    bool SelectedMatchesLastApplied,
    bool HasUnappliedEdits);

public sealed record SussexControllerBreathingSessionSnapshot(
    bool IsAvailable,
    OperationOutcomeKind ApplyLevel,
    string ApplySummary,
    string ApplyDetail,
    SussexControllerBreathingProfileRecord? CurrentProfile,
    SussexControllerBreathingProfileRecord? EffectiveProfile,
    SussexControllerBreathingProfileStartupState? StartupProfile,
    SussexControllerBreathingProfileApplyRecord? LastApplyRecord,
    IReadOnlyDictionary<string, double?> ReportedValues,
    bool SelectedMatchesLastApplied,
    bool HasUnappliedEdits);

internal sealed record SussexSessionParameterDelta(
    string Id,
    string Label,
    string Type,
    double? PreviousValue,
    double CurrentValue);

internal sealed record SussexSessionParameterActivity(
    string Surface,
    string Kind,
    string ProfileId,
    string ProfileName,
    DateTimeOffset RecordedAtUtc,
    IReadOnlyList<SussexSessionParameterDelta> Changes,
    IReadOnlyDictionary<string, double> CurrentValues,
    IReadOnlyDictionary<string, double?>? PreviousReportedValues = null,
    OperationOutcomeKind? OutcomeKind = null,
    string? Summary = null,
    string? Detail = null);
