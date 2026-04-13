using System.IO;
using System.Text.Json;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App;

internal sealed record StudyShellSessionState(IReadOnlyDictionary<string, string>? ApkPaths)
{
    private static readonly string SessionDirectory = CompanionOperatorDataLayout.SessionRootPath;

    private static readonly string StatePath = Path.Combine(SessionDirectory, "study-shell-state.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static StudyShellSessionState Load()
        => TryLoad(StatePath)
            ?? new StudyShellSessionState(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SessionDirectory);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(StatePath, json);
        }
        catch
        {
        }
    }

    public string? GetApkPath(string studyId)
    {
        if (ApkPaths is null || string.IsNullOrWhiteSpace(studyId))
        {
            return null;
        }

        return ApkPaths.TryGetValue(studyId, out var path) && !string.IsNullOrWhiteSpace(path)
            ? path
            : null;
    }

    public StudyShellSessionState WithApkPath(string studyId, string? apkPath)
    {
        var next = new Dictionary<string, string>(ApkPaths ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return new StudyShellSessionState(next);
        }

        if (string.IsNullOrWhiteSpace(apkPath))
        {
            next.Remove(studyId);
        }
        else
        {
            next[studyId] = apkPath;
        }

        return new StudyShellSessionState(next);
    }

    private static StudyShellSessionState? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StudyShellSessionState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
