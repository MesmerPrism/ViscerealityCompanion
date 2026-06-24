using System.Net;
using System.Net.Sockets;
using System.Text;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class PeripersonalForegroundStatusServiceTests
{
    [Fact]
    public void Parser_ReadsUnityForegroundSnapshot()
    {
        var receivedAt = DateTimeOffset.Parse("2026-06-24T12:00:01Z");
        var snapshot = PeripersonalForegroundStatusParser.Parse(
            "{" +
            "\"protocol_version\":\"viscereality.foreground_status.v1\"," +
            "\"source_app\":\"unity\"," +
            "\"package_name\":\"com.Viscereality.ViscerealityPeriPersonal\"," +
            "\"activity_name\":\"com.unity3d.player.UnityPlayerGameActivity\"," +
            "\"sequence\":7," +
            "\"emitted_at_utc\":\"2026-06-24T12:00:00Z\"," +
            "\"session_id\":\"session-001\"," +
            "\"participant_ref\":\"P001\"," +
            "\"reason\":\"tick\"," +
            "\"lifecycle\":{\"unity_paused\":false,\"unity_focused\":true,\"has_input_focus\":true,\"hmd_mounted\":true}" +
            "}",
            receivedAt,
            new IPEndPoint(IPAddress.Parse("192.168.1.42"), 47892));

        Assert.Equal("unity", snapshot.SourceApp);
        Assert.Equal(7, snapshot.Sequence);
        Assert.False(snapshot.UnityPaused);
        Assert.True(snapshot.UnityFocused);
        Assert.True(snapshot.UnityHasInputFocus);
        Assert.Equal("P001", snapshot.ParticipantRef);
        Assert.Equal("192.168.1.42", snapshot.RemoteEndpoint?.Address.ToString());
    }

    [Fact]
    public void Classifier_PrefersFocusedPanelOverVisibleUnity()
    {
        var now = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
        var classifier = new PeripersonalForegroundStatusClassifier();
        classifier.Update(UnitySnapshot(now, paused: false, hasInputFocus: false), now);
        var situation = classifier.Update(PanelSnapshot(now, resumed: true, windowFocused: true), now);

        Assert.Equal(PeripersonalForegroundOwner.Panel, situation.Owner);
        Assert.Equal("Questionnaire panel owns input.", situation.Summary);
        Assert.True(situation.IsPanelForeground);
        Assert.False(situation.IsUnityForeground);
    }

    [Fact]
    public void Classifier_UsesUnityWhenUnityHasInputAndPanelIsNotFocused()
    {
        var now = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
        var classifier = new PeripersonalForegroundStatusClassifier();
        classifier.Update(PanelSnapshot(now, resumed: false, windowFocused: false), now);
        var situation = classifier.Update(UnitySnapshot(now, paused: false, hasInputFocus: true), now);

        Assert.Equal(PeripersonalForegroundOwner.Unity, situation.Owner);
        Assert.True(situation.IsUnityForeground);
    }

    [Fact]
    public void Classifier_ReportsOpenPanelWhenPanelIsResumedButNotFocused()
    {
        var now = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
        var classifier = new PeripersonalForegroundStatusClassifier();
        classifier.Update(UnitySnapshot(now, paused: false, hasInputFocus: false), now);
        var situation = classifier.Update(PanelSnapshot(now, resumed: true, windowFocused: false), now);

        Assert.Equal(PeripersonalForegroundOwner.SystemOrUnknown, situation.Owner);
        Assert.Equal("Questionnaire panel is open but not input-focused.", situation.Summary);
        Assert.Contains("Panel is resumed", situation.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Classifier_MarksStaleSnapshotsUnknown()
    {
        var now = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
        var classifier = new PeripersonalForegroundStatusClassifier(TimeSpan.FromSeconds(1));
        classifier.Update(UnitySnapshot(now, paused: false, hasInputFocus: true), now);

        var situation = classifier.Classify(now.AddSeconds(2));

        Assert.Equal(PeripersonalForegroundOwner.Unknown, situation.Owner);
        Assert.Contains("No Unity or panel foreground beacon", situation.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UdpListener_ReceivesBeaconAndClassifiesPanelForeground()
    {
        var port = GetFreeUdpPort();
        using var listener = new PeripersonalUdpForegroundStatusListener(port: port);
        var tcs = new TaskCompletionSource<PeripersonalForegroundSituation>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.StatusChanged += (_, e) =>
        {
            if (e.Snapshot.SourceApp == "panel")
            {
                tcs.TrySetResult(e.Situation);
            }
        };

        var outcome = listener.Start();
        Assert.Equal(ViscerealityCompanion.Core.Models.OperationOutcomeKind.Success, outcome.Kind);

        using var sender = new UdpClient(AddressFamily.InterNetwork);
        var payload = Encoding.UTF8.GetBytes(
            "{" +
            "\"protocol_version\":\"viscereality.foreground_status.v1\"," +
            "\"source_app\":\"panel\"," +
            "\"package_name\":\"io.github.mesmerprism.questquestionnaire.panel\"," +
            "\"activity_name\":\"QuestionnaireActivity\"," +
            "\"sequence\":3," +
            "\"emitted_at_utc\":\"2026-06-24T12:00:00Z\"," +
            "\"session_id\":\"session-001\"," +
            "\"reason\":\"window_focus_acquired\"," +
            "\"lifecycle\":{\"activity_started\":true,\"activity_resumed\":true,\"window_focused\":true}," +
            "\"questionnaire\":{\"request_id\":\"request-001\",\"questionnaire_id\":\"maia2-spatial-frame-questionnaire-v1\",\"open_stage\":\"maia_spatial:spatial_frame_reference_1\"}" +
            "}");
        await sender.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, port));

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(tcs.Task, completed);
        Assert.Equal(PeripersonalForegroundOwner.Panel, (await tcs.Task).Owner);
    }

    private static PeripersonalForegroundStatusSnapshot UnitySnapshot(
        DateTimeOffset receivedAt,
        bool paused,
        bool hasInputFocus)
        => new(
            PeripersonalForegroundStatusContract.ProtocolVersion,
            "unity",
            "com.Viscereality.ViscerealityPeriPersonal",
            "com.unity3d.player.UnityPlayerGameActivity",
            1,
            receivedAt,
            receivedAt,
            null,
            "session-001",
            "P001",
            paused,
            true,
            hasInputFocus,
            true,
            null,
            null,
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            "test",
            "{}");

    private static PeripersonalForegroundStatusSnapshot PanelSnapshot(
        DateTimeOffset receivedAt,
        bool resumed,
        bool windowFocused)
        => new(
            PeripersonalForegroundStatusContract.ProtocolVersion,
            "panel",
            "io.github.mesmerprism.questquestionnaire.panel",
            ".QuestionnaireActivity",
            2,
            receivedAt,
            receivedAt,
            null,
            "session-001",
            "P001",
            null,
            null,
            null,
            null,
            true,
            resumed,
            windowFocused,
            "request-001",
            "maia2-spatial-frame-questionnaire-v1",
            "maia_spatial:spatial_frame_reference_1",
            "test",
            "{}");

    private static int GetFreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
