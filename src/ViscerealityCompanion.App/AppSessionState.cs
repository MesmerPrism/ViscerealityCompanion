using System.IO;
using System.Text.Json;

namespace ViscerealityCompanion.App;

internal sealed record AppSessionState(string? ActiveEndpoint, string? LastUsbSerial)
{
    private static readonly string SessionDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ViscerealityCompanion",
        "session");

    private static readonly string StatePath = Path.Combine(SessionDirectory, "app-state.json");
    private static readonly string CliFallbackPath = Path.Combine(SessionDirectory, "cli-state.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSessionState Load()
        => TryLoad(StatePath)
            ?? TryLoad(CliFallbackPath)
            ?? new AppSessionState(null, null);

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
            // Best-effort persistence only.
        }
    }

    public AppSessionState WithEndpoint(string? endpoint)
        => string.IsNullOrWhiteSpace(endpoint)
            ? this
            : this with { ActiveEndpoint = endpoint };

    public AppSessionState WithUsbSerial(string? serial)
        => string.IsNullOrWhiteSpace(serial)
            ? this
            : this with { LastUsbSerial = serial };

    private static AppSessionState? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSessionState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}