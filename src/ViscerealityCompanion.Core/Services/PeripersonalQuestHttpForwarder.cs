using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public interface IPeripersonalQuestHttpForwarder
{
    Task<OperationOutcome> EnsureUnityHttpBridgeForwardAsync(CancellationToken cancellationToken = default);
}

public sealed class NoOpPeripersonalQuestHttpForwarder : IPeripersonalQuestHttpForwarder
{
    public static NoOpPeripersonalQuestHttpForwarder Instance { get; } = new();

    private NoOpPeripersonalQuestHttpForwarder()
    {
    }

    public Task<OperationOutcome> EnsureUnityHttpBridgeForwardAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new OperationOutcome(
            OperationOutcomeKind.Success,
            "Unity HTTP bridge forwarding skipped.",
            "No peripersonal Quest HTTP forwarder is configured for this workflow service instance."));
}

public sealed class PeripersonalAdbQuestHttpForwarder : IPeripersonalQuestHttpForwarder
{
    public const int DefaultUnityHttpBridgePort = 8787;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly string _adbPath;
    private readonly string _selector;
    private readonly int _localPort;
    private readonly int _remotePort;
    private readonly TimeSpan _timeout;
    private readonly IPeripersonalAdbProcessRunner _processRunner;

    public PeripersonalAdbQuestHttpForwarder(
        string adbPath,
        string selector,
        int localPort = DefaultUnityHttpBridgePort,
        int remotePort = DefaultUnityHttpBridgePort,
        TimeSpan? timeout = null)
        : this(
            adbPath,
            selector,
            localPort,
            remotePort,
            timeout,
            new PeripersonalAdbProcessRunner())
    {
    }

    internal PeripersonalAdbQuestHttpForwarder(
        string adbPath,
        string selector,
        int localPort,
        int remotePort,
        TimeSpan? timeout,
        IPeripersonalAdbProcessRunner processRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentNullException.ThrowIfNull(processRunner);
        _adbPath = adbPath;
        _selector = selector;
        _localPort = ValidatePort(localPort, nameof(localPort));
        _remotePort = ValidatePort(remotePort, nameof(remotePort));
        _timeout = timeout ?? DefaultTimeout;
        _processRunner = processRunner;
    }

    public async Task<OperationOutcome> EnsureUnityHttpBridgeForwardAsync(CancellationToken cancellationToken = default)
    {
        var forward = await _processRunner.RunTextAsync(
                _adbPath,
                [
                    "-s",
                    _selector,
                    "forward",
                    $"tcp:{_localPort}",
                    $"tcp:{_remotePort}"
                ],
                _timeout,
                cancellationToken)
            .ConfigureAwait(false);
        if (forward.ExitCode != 0)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Unity HTTP bridge forwarding failed.",
                $"adb forward tcp:{_localPort} tcp:{_remotePort} failed for {_selector}: {forward.CombinedOutput}".Trim());
        }

        var list = await _processRunner.RunTextAsync(
                _adbPath,
                ["-s", _selector, "forward", "--list"],
                _timeout,
                cancellationToken)
            .ConfigureAwait(false);
        var expectedLocal = $"tcp:{_localPort}";
        var expectedRemote = $"tcp:{_remotePort}";
        var listOutput = list.CombinedOutput;
        if (list.ExitCode != 0)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                $"Unity HTTP bridge forwarded on 127.0.0.1:{_localPort}.",
                $"adb forward succeeded, but adb forward --list failed: {listOutput}".Trim(),
                Endpoint: $"127.0.0.1:{_localPort}");
        }

        var hasExpectedForward = listOutput.Contains(expectedLocal, StringComparison.OrdinalIgnoreCase) &&
                                 listOutput.Contains(expectedRemote, StringComparison.OrdinalIgnoreCase);
        return hasExpectedForward
            ? new OperationOutcome(
                OperationOutcomeKind.Success,
                $"Unity HTTP bridge forwarded on 127.0.0.1:{_localPort}.",
                $"ADB forward is active for {_selector}: {Collapse(listOutput)}",
                Endpoint: $"127.0.0.1:{_localPort}")
            : new OperationOutcome(
                OperationOutcomeKind.Warning,
                $"Unity HTTP bridge forward command completed for 127.0.0.1:{_localPort}.",
                $"adb forward --list did not echo tcp:{_localPort} -> tcp:{_remotePort}. Output: {Collapse(listOutput)}",
                Endpoint: $"127.0.0.1:{_localPort}");
    }

    private static int ValidatePort(int port, string parameterName)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(parameterName, port, "Port must be between 1 and 65535.");
        }

        return port;
    }

    private static string Collapse(string value)
        => string.Join(
            " ",
            (value ?? string.Empty)
            .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
