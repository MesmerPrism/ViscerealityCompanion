using System.Text;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class RuntimeConfigWriter
{
    private readonly string _outputRoot;

    public RuntimeConfigWriter(string? outputRoot = null)
    {
        _outputRoot = outputRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ViscerealityCompanion",
            "runtime-config");
    }

    public async Task<string> WriteAsync(
        RuntimeConfigProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Directory.CreateDirectory(_outputRoot);
        var safeId = string.IsNullOrWhiteSpace(profile.Id) ? "runtime-config" : profile.Id.Trim();
        var fileName = $"{safeId}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.csv";
        var outputPath = Path.Combine(_outputRoot, fileName);

        var builder = new StringBuilder();
        builder.AppendLine("key,value");
        foreach (var entry in profile.Entries)
        {
            builder.Append(entry.Key.Trim());
            builder.Append(',');
            builder.AppendLine(entry.Value);
        }

        await File.WriteAllTextAsync(outputPath, builder.ToString(), cancellationToken).ConfigureAwait(false);
        return outputPath;
    }
}
