using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class SussexStudyConditionStore
{
    private const string SchemaVersion = "sussex-study-condition-v1";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public SussexStudyConditionStore(string studyId, string? rootPath = null)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            throw new ArgumentException("A study id is required for the Sussex condition store.", nameof(studyId));
        }

        RootPath = Path.Combine(rootPath ?? CompanionOperatorDataLayout.StudyConditionsRootPath, BuildSafeFileStem(studyId));
    }

    public string RootPath { get; }

    public async Task<IReadOnlyList<SussexStudyConditionRecord>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootPath);
        var records = new List<SussexStudyConditionRecord>();
        foreach (var path in Directory.EnumerateFiles(RootPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                records.Add(await LoadRecordAsync(path, cancellationToken).ConfigureAwait(false));
            }
            catch (InvalidDataException)
            {
                // Leave invalid operator-authored files on disk but keep them out of the active UI.
            }
        }

        return records
            .OrderBy(record => record.Definition.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<SussexStudyConditionRecord> SaveAsync(
        string? existingPath,
        StudyConditionDefinition condition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        Validate(condition);

        Directory.CreateDirectory(RootPath);
        var targetPath = ResolveTargetPath(existingPath, BuildSafeFileName(condition.Id));
        var payload = Serialize(condition);
        var tempPath = targetPath + ".tmp";

        await File.WriteAllTextAsync(tempPath, payload, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempPath, targetPath);

        if (!string.IsNullOrWhiteSpace(existingPath) &&
            !string.Equals(Path.GetFullPath(existingPath), targetPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(existingPath))
        {
            File.Delete(existingPath);
        }

        return await LoadRecordAsync(targetPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SussexStudyConditionRecord> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new InvalidDataException("Select a Sussex condition JSON file to load.");
        }

        var json = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var condition = Parse(json);
        return await SaveAsync(existingPath: null, condition, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportAsync(
        StudyConditionDefinition condition,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new InvalidDataException("Select a destination path for the shared Sussex condition.");
        }

        await File.WriteAllTextAsync(destinationPath, Serialize(condition), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    public static StudyConditionDefinition Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("The Sussex condition file is empty.");
        }

        var dto = JsonSerializer.Deserialize<ConditionDto>(json, ReadOptions)
            ?? throw new InvalidDataException("The Sussex condition file could not be read.");
        var condition = new StudyConditionDefinition(
            dto.Id?.Trim() ?? string.Empty,
            dto.Label?.Trim() ?? dto.Id?.Trim() ?? string.Empty,
            dto.Description?.Trim() ?? string.Empty,
            dto.VisualProfileId?.Trim() ?? string.Empty,
            dto.ControllerBreathingProfileId?.Trim() ?? string.Empty,
            dto.IsActive ?? true,
            new Dictionary<string, string>(dto.Properties ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
        Validate(condition);
        return condition;
    }

    public static string Serialize(StudyConditionDefinition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        Validate(condition);

        return JsonSerializer.Serialize(
            new ConditionDto
            {
                SchemaVersion = SchemaVersion,
                Id = condition.Id,
                Label = condition.Label,
                Description = condition.Description,
                IsActive = condition.IsActive,
                VisualProfileId = condition.VisualProfileId,
                ControllerBreathingProfileId = condition.ControllerBreathingProfileId,
                Properties = new Dictionary<string, string>(condition.Properties, StringComparer.OrdinalIgnoreCase)
            },
            WriteOptions);
    }

    private async Task<SussexStudyConditionRecord> LoadRecordAsync(string path, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var condition = Parse(json);
        return new SussexStudyConditionRecord(
            condition.Id,
            Path.GetFullPath(path),
            await ComputeFileSha256Async(path, cancellationToken).ConfigureAwait(false),
            File.GetLastWriteTimeUtc(path),
            condition);
    }

    private string ResolveTargetPath(string? existingPath, string fileName)
    {
        var targetPath = Path.Combine(RootPath, fileName);
        if (string.IsNullOrWhiteSpace(existingPath))
        {
            return targetPath;
        }

        var fullExistingPath = Path.GetFullPath(existingPath);
        if (File.Exists(targetPath) &&
            !string.Equals(targetPath, fullExistingPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A local Sussex condition with that id already exists.");
        }

        return targetPath;
    }

    private static void Validate(StudyConditionDefinition condition)
    {
        if (string.IsNullOrWhiteSpace(condition.Id))
        {
            throw new InvalidDataException("A Sussex condition requires an id.");
        }

        if (string.IsNullOrWhiteSpace(condition.Label))
        {
            throw new InvalidDataException("A Sussex condition requires a label.");
        }

        if (string.IsNullOrWhiteSpace(condition.VisualProfileId))
        {
            throw new InvalidDataException("A Sussex condition requires a visual profile reference.");
        }

        if (string.IsNullOrWhiteSpace(condition.ControllerBreathingProfileId))
        {
            throw new InvalidDataException("A Sussex condition requires a breathing profile reference.");
        }
    }

    private static string BuildSafeFileName(string value)
        => BuildSafeFileStem(value) + ".json";

    private static string BuildSafeFileStem(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "sussex-condition" : value.Trim().ToLowerInvariant();
        var builder = new StringBuilder(trimmed.Length);
        var previousWasDash = false;
        foreach (var character in trimmed)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasDash = false;
                continue;
            }

            if (previousWasDash)
            {
                continue;
            }

            builder.Append('-');
            previousWasDash = true;
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "sussex-condition" : slug;
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private sealed class ConditionDto
    {
        [JsonPropertyName("schemaVersion")]
        public string? SchemaVersion { get; init; }

        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("isActive")]
        public bool? IsActive { get; init; }

        [JsonPropertyName("visualProfileId")]
        public string? VisualProfileId { get; init; }

        [JsonPropertyName("controllerBreathingProfileId")]
        public string? ControllerBreathingProfileId { get; init; }

        [JsonPropertyName("properties")]
        public Dictionary<string, string>? Properties { get; init; }
    }
}
