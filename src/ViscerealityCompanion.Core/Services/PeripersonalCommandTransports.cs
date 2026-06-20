using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class PeripersonalCompositeCommandTransport : IPeripersonalCommandTransport
{
    private readonly IPeripersonalCommandTransport _unityTransport;
    private readonly IPeripersonalCommandTransport _panelTransport;

    public PeripersonalCompositeCommandTransport(
        IPeripersonalCommandTransport unityTransport,
        IPeripersonalCommandTransport panelTransport)
    {
        _unityTransport = unityTransport ?? throw new ArgumentNullException(nameof(unityTransport));
        _panelTransport = panelTransport ?? throw new ArgumentNullException(nameof(panelTransport));
    }

    public Task<PeripersonalCommandReceiptEnvelope> SendAsync(
        PeripersonalCommandEnvelope command,
        CancellationToken cancellationToken = default)
        => string.Equals(command.TargetApp, "panel", StringComparison.OrdinalIgnoreCase)
            ? _panelTransport.SendAsync(command, cancellationToken)
            : _unityTransport.SendAsync(command, cancellationToken);
}

public sealed class PeripersonalLslCommandTransport : IPeripersonalCommandTransport, IDisposable
{
    public const string CommandStreamName = "peripersonal_operator_command";
    public const string CommandStreamType = "peripersonal.operator.command";
    public const string AckStreamName = "peripersonal_operator_command_ack";
    public const string AckStreamType = "peripersonal.operator.command.ack";
    private static readonly TimeSpan AckPrearmTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CommandRepublishInterval = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ILslOutletService _commandOutlet;
    private readonly ILslMonitorService _ackMonitor;
    private readonly TimeSpan _receiptTimeout;

    public PeripersonalLslCommandTransport(
        ILslOutletService commandOutlet,
        ILslMonitorService ackMonitor,
        TimeSpan? receiptTimeout = null)
    {
        _commandOutlet = commandOutlet ?? throw new ArgumentNullException(nameof(commandOutlet));
        _ackMonitor = ackMonitor ?? throw new ArgumentNullException(nameof(ackMonitor));
        _receiptTimeout = receiptTimeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<PeripersonalCommandReceiptEnvelope> SendAsync(
        PeripersonalCommandEnvelope command,
        CancellationToken cancellationToken = default)
    {
        EnsureUnityCommand(command);

        if (!_commandOutlet.IsOpen)
        {
            var open = _commandOutlet.Open(CommandStreamName, CommandStreamType, channelCount: 1);
            if (open.Kind == OperationOutcomeKind.Failure)
            {
                throw new InvalidOperationException(open.Detail);
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_receiptTimeout);
        var ackMonitorReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiptTask = WaitForMatchingReceiptAsync(command, ackMonitorReady, timeout.Token);

        try
        {
            await WaitForAckMonitorPrearmAsync(ackMonitorReady.Task, timeout.Token).ConfigureAwait(false);
            var commandJson = JsonSerializer.Serialize(command, JsonOptions);

            while (true)
            {
                _commandOutlet.PushSample([commandJson]);
                var delayTask = Task.Delay(CommandRepublishInterval, timeout.Token);
                var completed = await Task.WhenAny(receiptTask, delayTask).ConfigureAwait(false);
                if (completed == receiptTask)
                {
                    return await receiptTask.ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No matching LSL command receipt arrived for `{command.CommandId}` within {_receiptTimeout.TotalSeconds:0.#}s.");
        }

        throw new TimeoutException($"No matching LSL command receipt arrived for `{command.CommandId}`.");
    }

    public void Dispose()
    {
        _commandOutlet.Dispose();
    }

    private static void EnsureUnityCommand(PeripersonalCommandEnvelope command)
    {
        if (!string.Equals(command.TargetApp, "unity", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Peripersonal LSL command transport only sends Unity-targeted commands.");
        }
    }

    private static string FirstStringSample(LslMonitorReading reading)
    {
        if (!string.IsNullOrWhiteSpace(reading.TextValue))
        {
            return reading.TextValue;
        }

        return reading.SampleValues?.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private async Task<PeripersonalCommandReceiptEnvelope> WaitForMatchingReceiptAsync(
        PeripersonalCommandEnvelope command,
        TaskCompletionSource ackMonitorReady,
        CancellationToken cancellationToken)
    {
        await foreach (var reading in _ackMonitor.MonitorAsync(
                           new LslMonitorSubscription(AckStreamName, AckStreamType, ChannelIndex: 0),
                           cancellationToken).ConfigureAwait(false))
        {
            if (IsAckMonitorReady(reading))
            {
                ackMonitorReady.TrySetResult();
            }

            var receiptJson = FirstStringSample(reading);
            if (string.IsNullOrWhiteSpace(receiptJson))
            {
                continue;
            }

            var receipt = PeripersonalCommandReceiptEnvelope.ParseJson(receiptJson);
            if (string.Equals(receipt.CommandId, command.CommandId, StringComparison.Ordinal))
            {
                ackMonitorReady.TrySetResult();
                return receipt;
            }
        }

        throw new TimeoutException($"No matching LSL command receipt arrived for `{command.CommandId}`.");
    }

    private static async Task WaitForAckMonitorPrearmAsync(
        Task ackMonitorReady,
        CancellationToken cancellationToken)
    {
        using var prearmTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        prearmTimeout.CancelAfter(AckPrearmTimeout);
        try
        {
            await ackMonitorReady.WaitAsync(prearmTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The command outlet stays open and the receipt monitor keeps resolving. Publish anyway
            // so a late-resolving Unity inlet can still receive repeated command samples.
        }
    }

    private static bool IsAckMonitorReady(LslMonitorReading reading)
    {
        if (!string.IsNullOrWhiteSpace(FirstStringSample(reading)))
        {
            return true;
        }

        return reading.Status.Contains("connected", StringComparison.OrdinalIgnoreCase) ||
            reading.Status.Contains("streaming", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class PeripersonalAndroidBroadcastCommandTransport : IPeripersonalCommandTransport
{
    private readonly string _adbPath;
    private readonly string _selector;
    private readonly string _broadcastAction;
    private readonly string _commandJsonExtra;
    private readonly string _receiverComponent;
    private readonly TimeSpan _timeout;

    public PeripersonalAndroidBroadcastCommandTransport(
        string adbPath,
        string selector,
        string broadcastAction = "io.github.mesmerprism.questquestionnaire.panel.action.PERIPERSONAL_COMMAND",
        string commandJsonExtra = "io.github.mesmerprism.questquestionnaire.panel.extra.COMMAND_JSON",
        string receiverComponent = "io.github.mesmerprism.questquestionnaire.panel/.PeripersonalPanelCommandReceiver",
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        _adbPath = adbPath;
        _selector = selector;
        _broadcastAction = broadcastAction;
        _commandJsonExtra = commandJsonExtra;
        _receiverComponent = receiverComponent;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task<PeripersonalCommandReceiptEnvelope> SendAsync(
        PeripersonalCommandEnvelope command,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(command.TargetApp, "panel", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Peripersonal Android broadcast transport only sends panel-targeted commands.");
        }

        var commandJson = JsonSerializer.Serialize(
            command,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var shellCommand = BuildBroadcastShellCommand(
            _broadcastAction,
            _receiverComponent,
            _commandJsonExtra,
            commandJson);
        var result = await RunAdbAsync(
                ["-s", _selector, "shell", shellCommand],
                cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"ADB broadcast failed: {result.CombinedOutput}");
        }

        var receiptJson = ExtractResultData(result.CombinedOutput);
        if (string.IsNullOrWhiteSpace(receiptJson))
        {
            throw new InvalidOperationException($"ADB broadcast did not return receipt JSON: {result.CombinedOutput}");
        }

        return PeripersonalCommandReceiptEnvelope.ParseJson(receiptJson);
    }

    internal static string ExtractResultData(string output)
    {
        const string Marker = "data=\"";
        var source = output ?? string.Empty;
        var start = source.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        start += Marker.Length;
        var end = source.IndexOf("\", extras:", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = source.LastIndexOf('"');
        }

        if (end <= start)
        {
            return string.Empty;
        }

        return source[start..end]
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\/", "/", StringComparison.Ordinal);
    }

    internal static string BuildBroadcastShellCommand(
        string broadcastAction,
        string receiverComponent,
        string commandJsonExtra,
        string commandJson)
        => string.Join(
            " ",
            [
                "am",
                "broadcast",
                "-a",
                ShellQuote(broadcastAction),
                "-n",
                ShellQuote(receiverComponent),
                "--es",
                ShellQuote(commandJsonExtra),
                ShellQuote(commandJson)
            ]);

    internal static string ShellQuote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "''";
        }

        return $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }

    private async Task<AdbCommandResult> RunAdbAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _adbPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        return new AdbCommandResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private sealed record AdbCommandResult(int ExitCode, string StdOut, string StdErr)
    {
        public string CombinedOutput => string.Join(Environment.NewLine, new[] { StdOut, StdErr }.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }
}

public interface IPeripersonalQuestAppCloser
{
    Task<PeripersonalQuestAppCloseResult> CloseQuestAppsAsync(
        string unityPackage,
        string panelPackage,
        CancellationToken cancellationToken = default);
}

public sealed record PeripersonalQuestAppCloseResult(bool Succeeded, string Detail);

public sealed class NoOpPeripersonalQuestAppCloser : IPeripersonalQuestAppCloser
{
    public static NoOpPeripersonalQuestAppCloser Instance { get; } = new();

    private NoOpPeripersonalQuestAppCloser()
    {
    }

    public Task<PeripersonalQuestAppCloseResult> CloseQuestAppsAsync(
        string unityPackage,
        string panelPackage,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new PeripersonalQuestAppCloseResult(
            true,
            "No Quest app closer is configured; the Unity receipt recorded the close request."));
}

public sealed class PeripersonalAdbQuestAppCloser : IPeripersonalQuestAppCloser
{
    private readonly string _adbPath;
    private readonly string _selector;
    private readonly TimeSpan _timeout;

    public PeripersonalAdbQuestAppCloser(
        string adbPath,
        string selector,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        _adbPath = adbPath;
        _selector = selector;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task<PeripersonalQuestAppCloseResult> CloseQuestAppsAsync(
        string unityPackage,
        string panelPackage,
        CancellationToken cancellationToken = default)
    {
        var packages = new[] { panelPackage, unityPackage }
            .Where(static package => !string.IsNullOrWhiteSpace(package))
            .Select(static package => package.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (packages.Length == 0)
        {
            return new PeripersonalQuestAppCloseResult(false, "No Quest packages were supplied for final app close.");
        }

        var details = new List<string>(packages.Length);
        var succeeded = true;
        foreach (var package in packages)
        {
            var result = await RunAdbAsync(
                    ["-s", _selector, "shell", "am", "force-stop", package],
                    cancellationToken)
                .ConfigureAwait(false);
            succeeded &= result.ExitCode == 0;
            var detail = string.IsNullOrWhiteSpace(result.CombinedOutput)
                ? $"{package}: exit {result.ExitCode}"
                : $"{package}: exit {result.ExitCode}; {result.CombinedOutput}";
            details.Add(detail);
        }

        return new PeripersonalQuestAppCloseResult(
            succeeded,
            string.Join(" | ", details));
    }

    private async Task<AdbCloseResult> RunAdbAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _adbPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        return new AdbCloseResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private sealed record AdbCloseResult(int ExitCode, string StdOut, string StdErr)
    {
        public string CombinedOutput => string.Join(Environment.NewLine, new[] { StdOut, StdErr }.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }
}
