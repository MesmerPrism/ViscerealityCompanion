using System.IO;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class SussexVisualProfileStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "vc-sussex-visual-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Save_and_load_round_trip_profile_library()
    {
        Directory.CreateDirectory(_tempRoot);
        var compiler = new SussexVisualTuningCompiler(LoadTemplateJson());
        var store = new SussexVisualProfileStore(compiler, _tempRoot);

        var saved = await store.SaveAsync(
            existingPath: null,
            "Round Trip Profile",
            "notes",
            new Dictionary<string, double>(compiler.TemplateDocument.ControlValues, StringComparer.OrdinalIgnoreCase)
            {
                ["particle_size_min"] = 0.07
            });
        var library = await store.LoadAllAsync();

        var loaded = Assert.Single(library);
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal("Round Trip Profile", loaded.Document.Profile.Name);
        Assert.Equal(0.07, loaded.Document.ControlValues["particle_size_min"], 3);
    }

    [Fact]
    public async Task Save_renames_existing_profile_file_when_name_changes()
    {
        Directory.CreateDirectory(_tempRoot);
        var compiler = new SussexVisualTuningCompiler(LoadTemplateJson());
        var store = new SussexVisualProfileStore(compiler, _tempRoot);

        var saved = await store.CreateFromTemplateAsync("Original Profile");
        var renamed = await store.SaveAsync(
            saved.FilePath,
            "Renamed Profile",
            saved.Document.Profile.Notes,
            saved.Document.ControlValues);

        Assert.DoesNotContain("original-profile", renamed.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("renamed-profile", renamed.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(saved.FilePath));
        Assert.True(File.Exists(renamed.FilePath));
    }

    [Fact]
    public async Task Import_normalizes_profile_into_library_folder()
    {
        Directory.CreateDirectory(_tempRoot);
        var compiler = new SussexVisualTuningCompiler(LoadTemplateJson());
        var store = new SussexVisualProfileStore(compiler, _tempRoot);
        var sourceDocument = compiler.CreateDocument("Imported Profile", "from file");
        var sourcePath = Path.Combine(_tempRoot, "source.json");
        await File.WriteAllTextAsync(sourcePath, compiler.Serialize(sourceDocument));

        var imported = await store.ImportAsync(sourcePath);

        Assert.False(string.Equals(sourcePath, imported.FilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("imported-profile", imported.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Imported Profile", imported.Document.Profile.Name);
    }

    private static string LoadTemplateJson()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "oscillator-config",
            "llm-tuning",
            "sussex-visual-tuning-v1.template.json"));
        return File.ReadAllText(path);
    }
}
