using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class SussexStudyConditionStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "vc-sussex-condition-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Save_and_load_round_trip_condition_library()
    {
        var store = new SussexStudyConditionStore("sussex-university", _tempRoot);
        var saved = await store.SaveAsync(
            existingPath: null,
            new StudyConditionDefinition(
                "condition-a",
                "Condition A",
                "Round trip condition.",
                "condition-current-visual",
                "condition-current-breathing",
                isActive: true,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["visual.orbit"] = "current"
                }));

        var library = await store.LoadAllAsync();

        var loaded = Assert.Single(library);
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal("Condition A", loaded.Definition.Label);
        Assert.True(loaded.Definition.IsActive);
        Assert.Equal("condition-current-visual", loaded.Definition.VisualProfileId);
        Assert.Equal("current", loaded.Definition.Properties["visual.orbit"]);
    }

    [Fact]
    public async Task Import_and_export_use_shareable_condition_schema()
    {
        var store = new SussexStudyConditionStore("sussex-university", _tempRoot);
        var source = new StudyConditionDefinition(
            "shared-condition",
            "Shared Condition",
            "Condition shared across operators.",
            "condition-fixed-radius-no-orbit",
            "condition-fixed-radius-breathing",
            isActive: false);
        var sourcePath = Path.Combine(_tempRoot, "source.json");
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(sourcePath, SussexStudyConditionStore.Serialize(source));

        var imported = await store.ImportAsync(sourcePath);
        var exportPath = Path.Combine(_tempRoot, "exported.json");
        await store.ExportAsync(imported.Definition, exportPath);

        Assert.False(string.Equals(sourcePath, imported.FilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("shared-condition", imported.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(exportPath));
        var exported = SussexStudyConditionStore.Parse(await File.ReadAllTextAsync(exportPath));
        Assert.Equal("shared-condition", exported.Id);
        Assert.Equal("condition-fixed-radius-no-orbit", exported.VisualProfileId);
        Assert.False(exported.IsActive);
    }
}
