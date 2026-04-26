namespace ViscerealityCompanion.Core.Models;

public sealed record SussexStudyConditionRecord(
    string Id,
    string FilePath,
    string FileHash,
    DateTimeOffset ModifiedAtUtc,
    StudyConditionDefinition Definition);
