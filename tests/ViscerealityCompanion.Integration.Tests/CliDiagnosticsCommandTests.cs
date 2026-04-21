using ViscerealityCompanion.Cli;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class CliDiagnosticsCommandTests
{
    [Fact]
    public async Task Windows_env_analyze_help_mentions_stream_probe_and_liblssl_scope()
    {
        var help = await InvokeCliAsync("windows-env", "analyze", "--help");

        Assert.Contains("Analyze Windows Environment", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install-footprint", help, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task Study_diagnostics_report_help_is_exposed()
    {
        var help = await InvokeCliAsync("study", "diagnostics-report", "--help");

        Assert.Contains("shareable Sussex LSL/twin diagnostics report", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--output-dir", help, StringComparison.Ordinal);
        Assert.Contains("--skip-command-check", help, StringComparison.Ordinal);
        Assert.Contains("--no-pdf", help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintStudyConnectionProbe_EmitsExpectedStreamMissingLinksAndFocusNext()
    {
        await CliConsoleTestGate.Instance.WaitAsync();
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);
            DiagnosticsCliSupport.PrintStudyConnectionProbe(new DiagnosticsCliSupport.StudyConnectionProbeResult(
                Level: ViscerealityCompanion.Core.Models.OperationOutcomeKind.Warning,
                Summary: "Windows is receiving quest_twin_state, but Sussex has not confirmed an LSL inlet yet.",
                Detail: "Detailed probe output.",
                InletReady: false,
                ReturnPathReady: true,
                PinnedBuildReady: true,
                DeviceProfileReady: true,
                Selector: "Selector 192.168.0.10:5555 over Wi-Fi ADB.",
                ForegroundAndSnapshot: "Foreground confirmed.",
                PinnedBuild: "Pinned public Sussex build matches.",
                DeviceProfile: "Pinned device profile active.",
                WifiTransport: "[OK] Quest Wi-Fi transport looks healthy.",
                ExpectedInlet: "HRV_Biofeedback / HRV",
                WindowsExpectedStreamVisible: true,
                WindowsExpectedStreamViaCompanionTestSender: true,
                WindowsExpectedStream: "HRV_Biofeedback / HRV is visible on Windows via the companion TEST sender.",
                MissingLinks:
                [
                    "Sussex has not confirmed an active HRV_Biofeedback / HRV inlet."
                ],
                FocusNext: "The return path is already alive, so focus next on why Sussex has not subscribed the expected inlet.",
                RuntimeTarget: "HRV_Biofeedback / HRV",
                ConnectedInlet: "n/a / n/a",
                Counts: "Connected 0, connecting 0, total 1",
                QuestStatus: "LSL status pending.",
                QuestEcho: "No inlet value reported yet.",
                ReturnPath: "Latest quest_twin_state / quest.twin.state frame 15:00:00.",
                TwinStatePublisher: "Expected quest_twin_state publisher visible.",
                CommandChannel: "quest_twin_commands / quest.twin.command",
                HotloadChannel: "quest_hotload_config / quest.config",
                TransportDetail: "Twin bridge transport healthy.",
                CheckedAtUtc: new DateTimeOffset(2026, 04, 21, 15, 0, 0, TimeSpan.Zero)));

            var output = writer.ToString();
            Assert.Contains("Windows expected:", output, StringComparison.Ordinal);
            Assert.Contains("companion TEST sender", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Missing links:", output, StringComparison.Ordinal);
            Assert.Contains("active HRV_Biofeedback / HRV inlet", output, StringComparison.Ordinal);
            Assert.Contains("Focus next:", output, StringComparison.Ordinal);
            Assert.Contains("return path is already alive", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetOut(originalOut);
            CliConsoleTestGate.Instance.Release();
        }
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
