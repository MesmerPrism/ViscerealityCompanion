using ViscerealityCompanion.Cli;

namespace ViscerealityCompanion.Integration.Tests;

[Collection("CliConsole")]
public sealed class CliStudyCommandTests
{
    [Fact]
    public async Task Probe_help_mentions_json_output()
    {
        var help = await InvokeCliAsync("probe", "--help");

        Assert.Contains("--json", help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Study_action_help_exposes_ack_and_timing_options()
    {
        var help = await InvokeCliAsync("study", "action", "--help");

        Assert.Contains("same twin command channel as the GUI controls", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--wait-ack-seconds", help, StringComparison.Ordinal);
        Assert.Contains("--no-ack", help, StringComparison.Ordinal);
        Assert.Contains("--settle-ms", help, StringComparison.Ordinal);
        Assert.Contains("--hold-ms", help, StringComparison.Ordinal);
        Assert.Contains("--allow-recording-command-diagnostic", help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Study_snapshot_help_exposes_bounded_twin_state_options()
    {
        var help = await InvokeCliAsync("study", "snapshot", "--help");

        Assert.Contains("quest_twin_state", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--wait-seconds", help, StringComparison.Ordinal);
        Assert.Contains("--prefix", help, StringComparison.Ordinal);
        Assert.Contains("--all", help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Study_test_sender_run_help_mentions_gui_equivalent_routing()
    {
        var help = await InvokeCliAsync("study", "test-sender", "run", "--help");

        Assert.Contains("GUI-equivalent TEST sender route", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--duration-seconds", help, StringComparison.Ordinal);
        Assert.Contains("--wait-state-seconds", help, StringComparison.Ordinal);
        Assert.Contains("--no-routing", help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Study_actions_list_exposes_recording_and_calibration_aliases()
    {
        var output = await InvokeCliAsync("study", "actions", "sussex-university", "--root", ResolveStudyShellRoot());

        Assert.Contains("calibrate", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("start-recording", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stop-recording", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start Experiment", output, StringComparison.Ordinal);
        Assert.Contains("Controller Volume", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Study_action_start_recording_is_blocked_without_diagnostic_override()
    {
        var output = await InvokeCliAsyncWithExitCode(
            expectedExitCode: 2,
            "study",
            "action",
            "sussex-university",
            "start-recording",
            "--root",
            ResolveStudyShellRoot(),
            "--settle-ms",
            "0",
            "--hold-ms",
            "0",
            "--wait-ack-seconds",
            "0");

        Assert.Contains("requires the Experiment Session workflow", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start-Sussex-VerificationHarness.ps1 -UiInputParity", output, StringComparison.Ordinal);
    }

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

    private static async Task<string> InvokeCliAsync(params string[] args)
        => await InvokeCliAsyncWithExitCode(0, args);

    private static async Task<string> InvokeCliAsyncWithExitCode(int expectedExitCode, params string[] args)
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
            Assert.True(exitCode == expectedExitCode, $"CLI exited with {exitCode}, expected {expectedExitCode}, for: {string.Join(" ", args)}{Environment.NewLine}{writer}");
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
