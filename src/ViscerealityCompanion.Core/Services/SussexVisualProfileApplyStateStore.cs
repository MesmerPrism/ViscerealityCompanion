using System.Text.Json;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class SussexVisualProfileApplyStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _statePath;

    public SussexVisualProfileApplyStateStore(string studyId, string? stateRoot = null)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            throw new ArgumentException("A study id is required for the Sussex visual apply-state store.", nameof(studyId));
        }

        var root = stateRoot ?? CompanionOperatorDataLayout.SessionRootPath;
        Directory.CreateDirectory(root);
        _statePath = Path.Combine(root, $"sussex-visual-apply-{SanitizeToken(studyId)}.json");
    }

    public SussexVisualProfileApplyRecord? Load()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return null;
            }

            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<SussexVisualProfileApplyRecord>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(SussexVisualProfileApplyRecord? record)
    {
        try
        {
            if (record is null)
            {
                if (File.Exists(_statePath))
                {
                    File.Delete(_statePath);
                }

                return;
            }

            var json = JsonSerializer.Serialize(record, JsonOptions);
            File.WriteAllText(_statePath, json);
        }
        catch
        {
            // Best-effort persistence only.
        }
    }

    private static string SanitizeToken(string value)
    {
        var characters = value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        return new string(characters).Trim('-');
    }
}
