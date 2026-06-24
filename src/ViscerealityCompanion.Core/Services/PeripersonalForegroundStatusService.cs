using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public static class PeripersonalForegroundStatusContract
{
    public const string ProtocolVersion = "viscereality.foreground_status.v1";
    public const int UdpPort = 47892;
    public const string MulticastGroup = "239.255.42.42";
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromSeconds(4);
}

public enum PeripersonalForegroundOwner
{
    Unknown,
    Unity,
    Panel,
    SystemOrUnknown
}

public sealed record PeripersonalForegroundStatusSnapshot(
    string ProtocolVersion,
    string SourceApp,
    string PackageName,
    string ActivityName,
    long Sequence,
    DateTimeOffset? EmittedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    IPEndPoint? RemoteEndpoint,
    string SessionId,
    string ParticipantRef,
    bool? UnityPaused,
    bool? UnityFocused,
    bool? UnityHasInputFocus,
    bool? HmdMounted,
    bool? PanelActivityStarted,
    bool? PanelActivityResumed,
    bool? PanelWindowFocused,
    string RequestId,
    string QuestionnaireId,
    string OpenStage,
    string Reason,
    string RawJson)
{
    public bool IsFresh(DateTimeOffset now, TimeSpan? freshnessWindow = null)
        => now - ReceivedAtUtc <= (freshnessWindow ?? PeripersonalForegroundStatusContract.FreshnessWindow);
}

public sealed record PeripersonalForegroundSituation(
    PeripersonalForegroundOwner Owner,
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    PeripersonalForegroundStatusSnapshot? Unity,
    PeripersonalForegroundStatusSnapshot? Panel,
    DateTimeOffset EvaluatedAtUtc)
{
    public bool IsUnityForeground => Owner == PeripersonalForegroundOwner.Unity;
    public bool IsPanelForeground => Owner == PeripersonalForegroundOwner.Panel;
}

public sealed class PeripersonalForegroundStatusChangedEventArgs : EventArgs
{
    public PeripersonalForegroundStatusChangedEventArgs(
        PeripersonalForegroundStatusSnapshot snapshot,
        PeripersonalForegroundSituation situation)
    {
        Snapshot = snapshot;
        Situation = situation;
    }

    public PeripersonalForegroundStatusSnapshot Snapshot { get; }
    public PeripersonalForegroundSituation Situation { get; }
}

public static class PeripersonalForegroundStatusParser
{
    public static PeripersonalForegroundStatusSnapshot Parse(
        string json,
        DateTimeOffset receivedAtUtc,
        IPEndPoint? remoteEndpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var obj = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("Foreground status payload must be a JSON object.");
        var protocolVersion = ReadString(obj, "protocol_version", "protocolVersion");
        if (!string.Equals(protocolVersion, PeripersonalForegroundStatusContract.ProtocolVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported foreground status protocol `{protocolVersion}`.");
        }

        var lifecycle = ReadObject(obj, "lifecycle") ?? new JsonObject();
        var questionnaire = ReadObject(obj, "questionnaire") ?? new JsonObject();
        return new PeripersonalForegroundStatusSnapshot(
            protocolVersion,
            ReadString(obj, "source_app", "sourceApp"),
            ReadString(obj, "package_name", "packageName", "package"),
            ReadString(obj, "activity_name", "activityName", "activity"),
            ReadLong(obj, "sequence"),
            ReadDateTimeOffset(obj, "emitted_at_utc", "emittedAtUtc"),
            receivedAtUtc,
            remoteEndpoint,
            ReadString(obj, "session_id", "sessionId"),
            ReadString(obj, "participant_ref", "participantRef"),
            ReadNullableBool(lifecycle, "unity_paused", "unityPaused"),
            ReadNullableBool(lifecycle, "unity_focused", "unityFocused"),
            ReadNullableBool(lifecycle, "has_input_focus", "hasInputFocus", "unity_has_input_focus", "unityHasInputFocus"),
            ReadNullableBool(lifecycle, "hmd_mounted", "hmdMounted"),
            ReadNullableBool(lifecycle, "activity_started", "activityStarted"),
            ReadNullableBool(lifecycle, "activity_resumed", "activityResumed"),
            ReadNullableBool(lifecycle, "window_focused", "windowFocused"),
            ReadString(questionnaire, "request_id", "requestId"),
            ReadString(questionnaire, "questionnaire_id", "questionnaireId"),
            ReadString(questionnaire, "open_stage", "openStage"),
            ReadString(obj, "reason"),
            json);
    }

    private static JsonObject? ReadObject(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetPropertyValue(name, out var node) && node is JsonObject jsonObject)
            {
                return jsonObject;
            }
        }

        return null;
    }

    private static string ReadString(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetPropertyValue(name, out var node) && node is not null)
            {
                return node.GetValueKind() == JsonValueKind.String
                    ? node.GetValue<string>()
                    : node.ToJsonString();
            }
        }

        return string.Empty;
    }

    private static long ReadLong(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetPropertyValue(name, out var node) || node is null)
            {
                continue;
            }

            return node.GetValueKind() switch
            {
                JsonValueKind.Number => node.GetValue<long>(),
                JsonValueKind.String when long.TryParse(node.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 0L
            };
        }

        return 0L;
    }

    private static bool? ReadNullableBool(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetPropertyValue(name, out var node) || node is null)
            {
                continue;
            }

            return node.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => node.GetValue<int>() != 0,
                JsonValueKind.String when bool.TryParse(node.GetValue<string>(), out var parsed) => parsed,
                JsonValueKind.String => node.GetValue<string>().Trim().ToLowerInvariant() switch
                {
                    "1" or "yes" or "on" => true,
                    "0" or "no" or "off" => false,
                    _ => null
                },
                _ => null
            };
        }

        return null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonObject obj, params string[] names)
    {
        var raw = ReadString(obj, names);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}

public sealed class PeripersonalForegroundStatusClassifier
{
    private readonly TimeSpan _freshnessWindow;
    private PeripersonalForegroundStatusSnapshot? _unity;
    private PeripersonalForegroundStatusSnapshot? _panel;

    public PeripersonalForegroundStatusClassifier(TimeSpan? freshnessWindow = null)
    {
        _freshnessWindow = freshnessWindow ?? PeripersonalForegroundStatusContract.FreshnessWindow;
    }

    public PeripersonalForegroundSituation Update(
        PeripersonalForegroundStatusSnapshot snapshot,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.Equals(snapshot.SourceApp, "panel", StringComparison.OrdinalIgnoreCase))
        {
            _panel = snapshot;
        }
        else if (string.Equals(snapshot.SourceApp, "unity", StringComparison.OrdinalIgnoreCase))
        {
            _unity = snapshot;
        }

        return Classify(now);
    }

    public PeripersonalForegroundSituation Classify(DateTimeOffset? now = null)
    {
        var evaluatedAt = now ?? DateTimeOffset.UtcNow;
        var unity = _unity is not null && _unity.IsFresh(evaluatedAt, _freshnessWindow) ? _unity : null;
        var panel = _panel is not null && _panel.IsFresh(evaluatedAt, _freshnessWindow) ? _panel : null;

        if (panel?.PanelActivityResumed == true && panel.PanelWindowFocused == true)
        {
            return Build(
                PeripersonalForegroundOwner.Panel,
                OperationOutcomeKind.Success,
                "Questionnaire panel owns input.",
                $"Panel is resumed/window-focused. Unity input focus: {FormatBool(unity?.UnityHasInputFocus)}.",
                unity,
                panel,
                evaluatedAt);
        }

        if (panel?.PanelActivityResumed == true)
        {
            return Build(
                PeripersonalForegroundOwner.SystemOrUnknown,
                OperationOutcomeKind.Warning,
                "Questionnaire panel is open but not input-focused.",
                $"Panel is resumed, but window focus is {FormatBool(panel.PanelWindowFocused)}. Unity input focus: {FormatBool(unity?.UnityHasInputFocus)}.",
                unity,
                panel,
                evaluatedAt);
        }

        if (unity?.UnityPaused == false && unity.UnityHasInputFocus == true)
        {
            return Build(
                PeripersonalForegroundOwner.Unity,
                OperationOutcomeKind.Success,
                "Peripersonal XR runtime owns input.",
                $"Unity reports paused=false and hasInputFocus=true. Panel focused: {FormatBool(panel?.PanelWindowFocused)}.",
                unity,
                panel,
                evaluatedAt);
        }

        if (unity is not null || panel is not null)
        {
            return Build(
                PeripersonalForegroundOwner.SystemOrUnknown,
                OperationOutcomeKind.Warning,
                "No study app currently owns input.",
                $"Fresh status exists, but Unity input focus is {FormatBool(unity?.UnityHasInputFocus)} and panel window focus is {FormatBool(panel?.PanelWindowFocused)}.",
                unity,
                panel,
                evaluatedAt);
        }

        return Build(
            PeripersonalForegroundOwner.Unknown,
            OperationOutcomeKind.Preview,
            "Waiting for headset foreground beacons.",
            $"No Unity or panel foreground beacon has arrived in the last {_freshnessWindow.TotalSeconds:0.#} seconds. If the headset apps are running on the same Wi-Fi, allow Viscereality Companion through Windows Firewall on Private networks for UDP port {PeripersonalForegroundStatusContract.UdpPort}.",
            null,
            null,
            evaluatedAt);
    }

    private static PeripersonalForegroundSituation Build(
        PeripersonalForegroundOwner owner,
        OperationOutcomeKind level,
        string summary,
        string detail,
        PeripersonalForegroundStatusSnapshot? unity,
        PeripersonalForegroundStatusSnapshot? panel,
        DateTimeOffset evaluatedAtUtc)
    {
        var parts = new List<string> { detail };
        if (unity is not null)
        {
            parts.Add("Unity: " + Describe(unity));
        }

        if (panel is not null)
        {
            parts.Add("Panel: " + Describe(panel));
        }

        return new PeripersonalForegroundSituation(
            owner,
            level,
            summary,
            string.Join(" ", parts),
            unity,
            panel,
            evaluatedAtUtc);
    }

    private static string Describe(PeripersonalForegroundStatusSnapshot snapshot)
    {
        var ageSeconds = Math.Max(0d, (DateTimeOffset.UtcNow - snapshot.ReceivedAtUtc).TotalSeconds);
        var endpoint = snapshot.RemoteEndpoint is null ? "endpoint n/a" : snapshot.RemoteEndpoint.Address.ToString();
        return $"{snapshot.PackageName} seq {snapshot.Sequence} age {ageSeconds:0.0}s from {endpoint}; reason {snapshot.Reason}.";
    }

    private static string FormatBool(bool? value)
        => value.HasValue ? value.Value ? "true" : "false" : "unknown";
}

public sealed class PeripersonalUdpForegroundStatusListener : IDisposable
{
    private readonly object _gate = new();
    private readonly PeripersonalForegroundStatusClassifier _classifier;
    private readonly int _port;
    private readonly IPAddress _multicastGroup;
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public PeripersonalUdpForegroundStatusListener(
        int port = PeripersonalForegroundStatusContract.UdpPort,
        string multicastGroup = PeripersonalForegroundStatusContract.MulticastGroup,
        TimeSpan? freshnessWindow = null)
    {
        _port = port;
        _multicastGroup = IPAddress.Parse(multicastGroup);
        _classifier = new PeripersonalForegroundStatusClassifier(freshnessWindow);
        CurrentSituation = _classifier.Classify();
    }

    public bool IsRunning => _receiveTask is { IsCompleted: false };
    public PeripersonalForegroundSituation CurrentSituation { get; private set; }
    public event EventHandler<PeripersonalForegroundStatusChangedEventArgs>? StatusChanged;

    public OperationOutcome Start()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return new OperationOutcome(OperationOutcomeKind.Success, "Foreground listener already running.", $"Listening on UDP port {_port}.");
            }

            try
            {
                _cts = new CancellationTokenSource();
                _client = new UdpClient(AddressFamily.InterNetwork);
                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _client.EnableBroadcast = true;
                _client.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
                try
                {
                    _client.JoinMulticastGroup(_multicastGroup);
                }
                catch (SocketException)
                {
                    // Broadcast receive still works when multicast join is blocked.
                }

                _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
                return new OperationOutcome(
                    OperationOutcomeKind.Success,
                    "Foreground listener running.",
                    $"Listening for {PeripersonalForegroundStatusContract.ProtocolVersion} UDP beacons on port {_port}.");
            }
            catch (Exception ex)
            {
                Stop();
                return new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Foreground listener could not start.",
                    ex.Message);
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _client?.Dispose();
            _client = null;
            _cts?.Dispose();
            _cts = null;
            _receiveTask = null;
        }
    }

    public PeripersonalForegroundSituation RefreshStaleStatus(DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            CurrentSituation = _classifier.Classify(now);
            return CurrentSituation;
        }
    }

    public void Dispose()
        => Stop();

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                var client = _client;
                if (client is null)
                {
                    return;
                }

                result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(result.Buffer);
            PeripersonalForegroundStatusSnapshot snapshot;
            try
            {
                snapshot = PeripersonalForegroundStatusParser.Parse(json, DateTimeOffset.UtcNow, result.RemoteEndPoint);
            }
            catch
            {
                continue;
            }

            PeripersonalForegroundSituation situation;
            lock (_gate)
            {
                CurrentSituation = _classifier.Update(snapshot);
                situation = CurrentSituation;
            }

            StatusChanged?.Invoke(this, new PeripersonalForegroundStatusChangedEventArgs(snapshot, situation));
        }
    }
}
