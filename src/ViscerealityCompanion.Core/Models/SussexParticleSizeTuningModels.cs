namespace ViscerealityCompanion.Core.Models;

public sealed record SussexParticleSizeTuningDocument(
    string SchemaVersion,
    string DocumentKind,
    string PackageId,
    string BaselineHotloadProfileId,
    string HotloadTargetKey,
    SussexParticleSizeTuningControl ParticleSizeMinimum,
    SussexParticleSizeTuningControl ParticleSizeMaximum);

public sealed record SussexParticleSizeTuningControl(
    string Id,
    string Label,
    double Value,
    double BaselineValue,
    double SafeMinimum,
    double SafeMaximum,
    string RuntimeJsonField);

public sealed record SussexParticleSizeTuningCompileResult(
    SussexParticleSizeTuningDocument Document,
    string CompactRuntimeConfigJson,
    string PrettyRuntimeConfigJson,
    string HotloadTargetKey);
