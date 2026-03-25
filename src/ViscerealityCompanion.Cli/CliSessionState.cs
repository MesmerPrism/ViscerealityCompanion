using System.Text.Json;

namespace ViscerealityCompanion.Cli;

internal sealed record CliSessionState(string? ActiveEndpoint, string? LastUsbSerial)
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ViscerealityCompanion",
        "session",
        "cli-state.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static CliSessionState Load()
    {
        try
        {
            if (!File.Exists(StatePath))
                return new CliSessionState(null, null);

            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<CliSessionState>(json, JsonOptions)
                   ?? new CliSessionState(null, null);
        }
        catch
        {
            return new CliSessionState(null, null);
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StatePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(StatePath, json);
        }
        catch
        {
            // Best-effort persistence — no crash on write failure.
        }
    }

    public CliSessionState WithEndpoint(string? endpoint)
        => this with { ActiveEndpoint = endpoint ?? ActiveEndpoint };

    public CliSessionState WithUsbSerial(string? serial)
        => this with { LastUsbSerial = serial ?? LastUsbSerial };
}
