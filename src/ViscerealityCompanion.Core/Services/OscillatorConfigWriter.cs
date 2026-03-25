using System.Text.Json;
using System.Text.Json.Serialization;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class OscillatorConfigWriter
{
    private readonly string _outputRoot;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public OscillatorConfigWriter(string? outputRoot = null)
    {
        _outputRoot = outputRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ViscerealityCompanion",
            "oscillator-config");
    }

    public async Task<string> WriteAsync(
        OscillatorConfigProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Directory.CreateDirectory(_outputRoot);

        var safeId = string.IsNullOrWhiteSpace(profile.Id) ? "oscillator-config" : profile.Id.Trim();
        var fileName = $"oscillator_config_{safeId}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.json";
        var outputPath = Path.Combine(_outputRoot, fileName);

        var payload = new
        {
            exportedAtUtc = DateTimeOffset.UtcNow,
            profile.Id,
            profile.Label,
            profile.Description,
            profile.PackageIds,
            document = profile.Document
        };

        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken).ConfigureAwait(false);
        return outputPath;
    }
}
