using System.Text.Json;
using ViscerealityCompanion.Cli;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

[Collection("CliConsole")]
public sealed class CliSussexConditionCommandTests : IDisposable
{
    private readonly string _operatorDataRoot = CreateTempOperatorDataRoot();

    [Fact]
    public async Task Sussex_condition_list_resolves_bundled_profile_references()
    {
        var conditionRoot = CreateTempConditionRoot();
        try
        {
            var output = await InvokeCliAsync("sussex", "--root", ResolveStudyShellRoot(), "condition", "--condition-root", conditionRoot, "list", "--json");
            using var document = JsonDocument.Parse(output);

            Assert.True(document.RootElement.GetArrayLength() >= 2);
            var current = FindCondition(document, "current");
            Assert.True(current.GetProperty("is_active").GetBoolean());
            Assert.Equal("Condition A - Current Visuals", current.GetProperty("visual_profile_name").GetString());
            Assert.Equal("Condition A - Current Breathing", current.GetProperty("controller_breathing_profile_name").GetString());

            var fixedRadius = FindCondition(document, "fixed-radius-no-orbit");
            Assert.True(fixedRadius.GetProperty("is_active").GetBoolean());
            Assert.Equal("Condition B - Fixed Radius, No Orbit", fixedRadius.GetProperty("visual_profile_name").GetString());
            Assert.Equal("Condition B - Fixed Radius Breathing", fixedRadius.GetProperty("controller_breathing_profile_name").GetString());
        }
        finally
        {
            DeleteDirectoryIfExists(conditionRoot);
        }
    }

    [Fact]
    public async Task Sussex_condition_create_update_export_import_and_delete_round_trip()
    {
        var conditionId = $"cli-test-{Guid.NewGuid():N}";
        var conditionRoot = CreateTempConditionRoot();
        var exportPath = Path.Combine(conditionRoot, $"{conditionId}.json");
        var studyRoot = ResolveStudyShellRoot();
        try
        {
            var createOutput = await InvokeCliAsync(
                "sussex",
                "--root",
                studyRoot,
                "condition",
                "--condition-root",
                conditionRoot,
                "create",
                "--id",
                conditionId,
                "--label",
                "CLI Test",
                "--visual",
                "condition-current-visual",
                "--breathing",
                "condition-current-breathing",
                "--inactive",
                "--property",
                "protocol=smoke",
                "--json");
            using var createDocument = JsonDocument.Parse(createOutput);
            var created = createDocument.RootElement.GetProperty("created");
            Assert.False(created.GetProperty("is_active").GetBoolean());
            Assert.Equal("Condition A - Current Visuals", created.GetProperty("visual_profile_name").GetString());

            var updateOutput = await InvokeCliAsync(
                "sussex",
                "--root",
                studyRoot,
                "condition",
                "--condition-root",
                conditionRoot,
                "update",
                conditionId,
                "--active",
                "--visual",
                "condition-fixed-radius-no-orbit",
                "--property",
                "phase=updated",
                "--json");
            using var updateDocument = JsonDocument.Parse(updateOutput);
            var updated = updateDocument.RootElement.GetProperty("updated");
            Assert.True(updated.GetProperty("is_active").GetBoolean());
            Assert.Equal("condition-fixed-radius-no-orbit", updated.GetProperty("visual_profile_id").GetString());
            Assert.Equal("updated", updated.GetProperty("properties").GetProperty("phase").GetString());

            await InvokeCliAsync("sussex", "--root", studyRoot, "condition", "--condition-root", conditionRoot, "export", conditionId, exportPath, "--json");
            Assert.True(File.Exists(exportPath));

            await InvokeCliAsync("sussex", "--root", studyRoot, "condition", "--condition-root", conditionRoot, "delete", conditionId, "--json");
            var activeOnlyAfterDelete = await InvokeCliAsync("sussex", "--root", studyRoot, "condition", "--condition-root", conditionRoot, "list", "--active-only", "--json");
            Assert.DoesNotContain(conditionId, activeOnlyAfterDelete, StringComparison.OrdinalIgnoreCase);

            var importOutput = await InvokeCliAsync("sussex", "--root", studyRoot, "condition", "--condition-root", conditionRoot, "import", exportPath, "--json");
            using var importDocument = JsonDocument.Parse(importOutput);
            Assert.Equal(conditionId, importDocument.RootElement.GetProperty("imported").GetProperty("id").GetString());
        }
        finally
        {
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }

            try
            {
                await InvokeCliAsync("sussex", "--root", studyRoot, "condition", "--condition-root", conditionRoot, "delete", conditionId, "--json");
            }
            catch
            {
                // Best-effort cleanup for a unique test condition.
            }

            DeleteDirectoryIfExists(conditionRoot);
        }
    }

    [Fact]
    public async Task Sussex_condition_help_mentions_active_selection_and_profile_pairing()
    {
        var help = await InvokeCliAsync("sussex", "condition", "create", "--help");

        Assert.Contains("--visual", help, StringComparison.Ordinal);
        Assert.Contains("--breathing", help, StringComparison.Ordinal);
        Assert.Contains("--active", help, StringComparison.Ordinal);
        Assert.Contains("--inactive", help, StringComparison.Ordinal);
    }

    private static JsonElement FindCondition(JsonDocument document, string id)
        => document.RootElement
            .EnumerateArray()
            .First(element => string.Equals(element.GetProperty("id").GetString(), id, StringComparison.Ordinal));

    private static string ResolveStudyShellRoot()
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "study-shells"));

    private static string CreateTempConditionRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vc-condition-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateTempOperatorDataRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vc-condition-operator-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public void Dispose()
        => DeleteDirectoryIfExists(_operatorDataRoot);

    private async Task<string> InvokeCliAsync(params string[] args)
    {
        await CliConsoleTestGate.Instance.WaitAsync();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalOperatorDataRoot = Environment.GetEnvironmentVariable(CompanionOperatorDataLayout.RootOverrideEnvironmentVariable);
        using var writer = new StringWriter();

        try
        {
            Environment.SetEnvironmentVariable(CompanionOperatorDataLayout.RootOverrideEnvironmentVariable, _operatorDataRoot);
            Console.SetOut(writer);
            Console.SetError(writer);
            var exitCode = await Program.Main(args);
            Assert.True(exitCode == 0, $"CLI exited with {exitCode} for: {string.Join(" ", args)}{Environment.NewLine}{writer}");
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Environment.SetEnvironmentVariable(CompanionOperatorDataLayout.RootOverrideEnvironmentVariable, originalOperatorDataRoot);
            CliConsoleTestGate.Instance.Release();
        }
    }
}
