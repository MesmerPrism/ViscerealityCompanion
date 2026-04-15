using System.Text.Json;
using ViscerealityCompanion.Cli;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class CliSussexControllerCommandTests
{
    [Fact]
    public async Task Sussex_controller_fields_use_public_template_metadata()
    {
        var output = await InvokeCliAsync("sussex", "controller", "fields", "--json");
        using var document = JsonDocument.Parse(output);

        var modeField = FindField(document, "use_principal_axis_calibration");
        Assert.Equal("Calibration Setup", modeField.GetProperty("Group").GetString());
        Assert.Equal("Use Dynamic Motion Axis", modeField.GetProperty("Label").GetString());

        var deltaField = FindField(document, "min_accepted_delta");
        Assert.Equal("Calibration Acceptance", deltaField.GetProperty("Group").GetString());
        Assert.Equal("Minimum Accepted Movement", deltaField.GetProperty("Label").GetString());
        Assert.Equal("0.0004", deltaField.GetProperty("Baseline").GetString());

        var travelField = FindField(document, "min_acceptable_travel");
        Assert.Equal("Calibration Acceptance", travelField.GetProperty("Group").GetString());
        Assert.Equal("Minimum Calibration Travel", travelField.GetProperty("Label").GetString());
        Assert.Equal("0.01", travelField.GetProperty("Baseline").GetString());
    }

    [Fact]
    public async Task Sussex_controller_help_mentions_calibration_mode_and_acceptance_examples()
    {
        var createHelp = await InvokeCliAsync("sussex", "controller", "create", "--help");
        Assert.Contains("use_principal_axis_calibration=off", createHelp, StringComparison.Ordinal);
        Assert.Contains("min_accepted_delta=0.0004", createHelp, StringComparison.Ordinal);
        Assert.Contains("min_acceptable_travel=0.01", createHelp, StringComparison.Ordinal);

        var updateHelp = await InvokeCliAsync("sussex", "controller", "update", "--help");
        Assert.Contains("use_principal_axis_calibration=off", updateHelp, StringComparison.Ordinal);
        Assert.Contains("min_accepted_delta=0.0004", updateHelp, StringComparison.Ordinal);
        Assert.Contains("min_acceptable_travel=0.01", updateHelp, StringComparison.Ordinal);
    }

    private static JsonElement FindField(JsonDocument document, string id)
        => document.RootElement
            .EnumerateArray()
            .First(element => string.Equals(element.GetProperty("Id").GetString(), id, StringComparison.Ordinal));

    private static async Task<string> InvokeCliAsync(params string[] args)
    {
        await CliConsoleTestGate.Instance.WaitAsync();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);
            Console.SetError(writer);
            var exitCode = await Program.Main(args);
            Assert.Equal(0, exitCode);
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            CliConsoleTestGate.Instance.Release();
        }
    }
}
