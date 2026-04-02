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
