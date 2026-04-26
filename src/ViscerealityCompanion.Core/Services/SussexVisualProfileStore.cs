using System.Security.Cryptography;
using System.Text;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class SussexVisualProfileStore
{
    private readonly SussexVisualTuningCompiler _compiler;

    public SussexVisualProfileStore(
        SussexVisualTuningCompiler compiler,
        string? rootPath = null)
    {
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        RootPath = rootPath ?? CompanionOperatorDataLayout.SussexVisualProfilesRootPath;
    }

    public string RootPath { get; }

    public async Task<IReadOnlyList<SussexVisualProfileRecord>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootPath);
        var records = new List<SussexVisualProfileRecord>();
        foreach (var path in Directory.EnumerateFiles(RootPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                records.Add(await LoadRecordAsync(path, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (
                exception is InvalidDataException ||
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                // Skip invalid or transiently unavailable files already on disk; import validates before writing.
            }
        }

        return records
            .OrderBy(record => record.Document.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<SussexVisualProfileRecord> CreateFromTemplateAsync(
        string profileName,
        string? notes = null,
        CancellationToken cancellationToken = default)
        => SaveAsync(
            existingPath: null,
            profileName,
            notes,
            _compiler.TemplateDocument.ControlValues,
            cancellationToken);

    public async Task<SussexVisualProfileRecord> SaveAsync(
        string? existingPath,
        string profileName,
        string? notes,
        IReadOnlyDictionary<string, double> controlValues,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(controlValues);

        Directory.CreateDirectory(RootPath);
        var document = _compiler.CreateDocument(profileName, notes, controlValues);
        var fileName = BuildSafeFileName(document.Profile.Name);
        var targetPath = ResolveTargetPath(existingPath, fileName);
        var payload = _compiler.Serialize(document);
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

    public async Task<SussexVisualProfileRecord> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new InvalidDataException("Select a Sussex visual profile JSON file to import.");
        }

        var json = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var document = _compiler.Parse(json);
        return await SaveAsync(
            existingPath: null,
            document.Profile.Name,
            document.Profile.Notes,
            document.ControlValues,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportAsync(
        SussexVisualTuningDocument document,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new InvalidDataException("Select a destination path for the exported Sussex visual profile.");
        }

        var payload = _compiler.Serialize(document);
        await File.WriteAllTextAsync(destinationPath, payload, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
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

    private async Task<SussexVisualProfileRecord> LoadRecordAsync(string path, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var document = _compiler.Parse(json);
        return new SussexVisualProfileRecord(
            Path.GetFileNameWithoutExtension(path),
            Path.GetFullPath(path),
            await ComputeFileSha256Async(path, cancellationToken).ConfigureAwait(false),
            File.GetLastWriteTimeUtc(path),
            document);
    }

    private string ResolveTargetPath(string? existingPath, string fileName)
    {
        var fullExistingPath = string.IsNullOrWhiteSpace(existingPath) ? null : Path.GetFullPath(existingPath);
        var candidate = Path.Combine(RootPath, fileName);
        if (!File.Exists(candidate) ||
            string.Equals(candidate, fullExistingPath, StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var suffix = 2;
        while (true)
        {
            var next = Path.Combine(RootPath, $"{stem}-{suffix}{extension}");
            if (!File.Exists(next) ||
                string.Equals(next, fullExistingPath, StringComparison.OrdinalIgnoreCase))
            {
                return next;
            }

            suffix++;
        }
    }

    private static string BuildSafeFileName(string profileName)
    {
        var trimmed = string.IsNullOrWhiteSpace(profileName) ? "sussex-visual-profile" : profileName.Trim().ToLowerInvariant();
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
        return string.IsNullOrWhiteSpace(slug)
            ? "sussex-visual-profile.json"
            : slug + ".json";
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
}
