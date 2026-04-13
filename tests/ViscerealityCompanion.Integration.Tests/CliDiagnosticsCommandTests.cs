using ViscerealityCompanion.Cli;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class CliDiagnosticsCommandTests
{
    [Fact]
    public async Task Windows_env_analyze_help_mentions_stream_probe_and_liblssl_scope()
    {
        var help = await InvokeCliAsync("windows-env", "analyze", "--help");

        Assert.Contains("Analyze Windows Environment", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--expected-stream", help, StringComparison.Ordinal);
        Assert.Contains("--expected-type", help, StringComparison.Ordinal);
        Assert.Contains("--skip-stream-probe", help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Study_probe_connection_help_is_exposed()
    {
        var help = await InvokeCliAsync("study", "probe-connection", "--help");

        Assert.Contains("Step 9", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--wait-seconds", help, StringComparison.Ordinal);
        Assert.Contains("--json", help, StringComparison.Ordinal);
    }

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
