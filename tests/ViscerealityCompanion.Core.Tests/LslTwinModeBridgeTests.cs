using System.Runtime.CompilerServices;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public class LslTwinModeBridgeTests
{
    [Fact]
    public void Status_reports_available_before_open()
    {
        using var bridge = CreateBridge();
        Assert.True(bridge.Status.IsAvailable);
        Assert.Contains("available", bridge.Status.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_creates_command_and_config_outlets()
    {
        using var bridge = CreateBridge(out var commandOutlet, out var configOutlet, out _, out _);
        var result = bridge.Open();

        Assert.Equal(OperationOutcomeKind.Success, result.Kind);
        Assert.True(commandOutlet.IsOpen);
        Assert.True(configOutlet.IsOpen);
    }

    [Fact]
    public async Task SendCommandAsync_publishes_through_outlet()
    {
        using var bridge = CreateBridge(out var commandOutlet, out _, out _, out _);
        var command = new TwinModeCommand("twin-start", "Start twin session");

        var result = await bridge.SendCommandAsync(command);

        Assert.Equal(OperationOutcomeKind.Preview, result.Kind);
        Assert.Contains("twin-start", commandOutlet.LastPublishedCommand ?? "");
    }

    [Fact]
    public async Task SendCommandAsync_updates_transport_counters()
    {
        using var bridge = CreateBridge(out _, out _, out _, out var sequenceStore);

        await bridge.SendCommandAsync(new TwinModeCommand("2", "Recenter"));

        Assert.True(bridge.IsCommandOutletOpen);
        Assert.Equal(1, bridge.PublishedCommandCount);
        Assert.Equal(1, bridge.LastPublishedCommandSequence);
        Assert.Contains("sent 1 command", bridge.Status.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, sequenceStore.LastIssuedSequence);
    }

    [Fact]
    public async Task SendCommandAsync_keeps_monotonic_sequence_across_bridge_instances()
    {
        var sharedSequenceStore = new FakeSequenceStore();
        using (var firstBridge = CreateBridge(out _, out _, out _, out _, sharedSequenceStore))
        {
            await firstBridge.SendCommandAsync(new TwinModeCommand("2", "Recenter"));
            Assert.Equal(1, firstBridge.LastPublishedCommandSequence);
        }

        using var secondBridge = CreateBridge(out _, out _, out _, out _, sharedSequenceStore);
        await secondBridge.SendCommandAsync(new TwinModeCommand("14", "Particles Off"));

        Assert.Equal(2, secondBridge.LastPublishedCommandSequence);
    }

    [Fact]
    public async Task PublishRuntimeConfigAsync_tracks_requested_settings()
    {
        using var bridge = CreateBridge(out _, out var configOutlet, out _, out _);
        var profile = new RuntimeConfigProfile(
            "test-profile", "Test Profile", "test.csv", "1.0", "preview", false,
            "Test description", ["org.test.app"],
            [new RuntimeConfigEntry("alpha", "0.5"), new RuntimeConfigEntry("beta", "1.0")]);
        var target = new QuestAppTarget(
            "test-app", "Test App", "org.test.app", "", "", "", "", []);

        var result = await bridge.PublishRuntimeConfigAsync(profile, target);

        Assert.Equal(OperationOutcomeKind.Preview, result.Kind);
        Assert.Equal(2, configOutlet.PublishedEntryCount);

        var requested = bridge.RequestedSettings;
        Assert.Equal("0.5", requested["alpha"]);
        Assert.Equal("1.0", requested["beta"]);
    }

    [Fact]
    public async Task ComputeSettingsDelta_compares_requested_and_reported()
    {
        using var bridge = CreateBridge(out _, out _, out var stateMonitor, out _);
        stateMonitor.EnqueueReading(new LslMonitorReading(
            "Streaming", "set alpha=0.7", 0f, 30f, DateTimeOffset.UtcNow));

        bridge.Open();

        // Wait briefly for the background monitor task to process
        await Task.Delay(200);

        // Published a config to establish requested settings
        var profile = new RuntimeConfigProfile(
            "test", "Test", "t.csv", "1.0", "preview", false, "",
            ["org.test"],
            [new RuntimeConfigEntry("alpha", "0.5"), new RuntimeConfigEntry("gamma", "2.0")]);
        var target = new QuestAppTarget("t", "T", "org.test", "", "", "", "", []);
        await bridge.PublishRuntimeConfigAsync(profile, target);

        var delta = bridge.ComputeSettingsDelta();

        Assert.Equal(2, delta.Count); // alpha (both sides), gamma (requested only)
        var alphaDelta = delta.First(d => d.Key == "alpha");
        Assert.Equal("0.5", alphaDelta.Requested);
        Assert.Equal("0.7", alphaDelta.Reported);
        Assert.False(alphaDelta.Matches);

        var gammaDelta = delta.First(d => d.Key == "gamma");
        Assert.Equal("2.0", gammaDelta.Requested);
        Assert.Null(gammaDelta.Reported);
        Assert.False(gammaDelta.Matches);
    }

    [Fact]
    public async Task ApplyConfigAsync_returns_success()
    {
        using var bridge = CreateBridge();
        var profile = new HotloadProfile("hp1", "Profile 1", "p.csv", "1.0", "preview", false, "", ["org.test"]);
        var target = new QuestAppTarget("t", "T", "org.test", "", "", "", "", []);

        var result = await bridge.ApplyConfigAsync(profile, target);

        Assert.Equal(OperationOutcomeKind.Success, result.Kind);
    }

    [Fact]
    public async Task Structured_snapshot_updates_revision_and_last_remote_command_detail()
    {
        using var bridge = CreateBridge(out _, out _, out var stateMonitor, out _);
        var timestamp = DateTimeOffset.UtcNow;

        stateMonitor.EnqueueReading(new LslMonitorReading(
            "Streaming", string.Empty, null, 20f, timestamp,
            SampleValues: ["begin", "7", string.Empty, "hash-7"]));
        stateMonitor.EnqueueReading(new LslMonitorReading(
            "Streaming", string.Empty, null, 20f, timestamp,
            SampleValues: ["set", "7", "study.command.last_action_sequence", "3"]));
        stateMonitor.EnqueueReading(new LslMonitorReading(
            "Streaming", string.Empty, null, 20f, timestamp,
            SampleValues: ["set", "7", "study.command.last_action_label", "Particles On"]));
        stateMonitor.EnqueueReading(new LslMonitorReading(
            "Streaming", string.Empty, null, 20f, timestamp,
            SampleValues: ["set", "7", "study.command.last_action_source", "lsl twin command"]));
        stateMonitor.EnqueueReading(new LslMonitorReading(
            "Streaming", string.Empty, null, 20f, timestamp,
            SampleValues: ["set", "7", "study.command.last_action_at_utc", "2026-03-26T11:24:32.1135492+00:00"]));
        stateMonitor.EnqueueReading(new LslMonitorReading(
            "Streaming", string.Empty, null, 20f, timestamp,
            SampleValues: ["end", "7", "4", "hash-7"]));

        bridge.Open();
        await Task.Delay(200);

        Assert.Equal("7", bridge.LastCommittedSnapshotRevision);
        Assert.Equal(4, bridge.LastCommittedSnapshotEntryCount);
        Assert.Contains("Headset last executed Particles On seq 3", bridge.Status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateShared_returns_same_bridge_instance()
    {
        var first = TwinModeBridgeFactory.CreateShared();
        var second = TwinModeBridgeFactory.CreateShared();
        var isolated = TwinModeBridgeFactory.CreateDefault();

        try
        {
            Assert.Same(first, second);
            Assert.NotSame(first, isolated);
        }
        finally
        {
            (isolated as IDisposable)?.Dispose();
        }
    }

    private static LslTwinModeBridge CreateBridge() =>
        CreateBridge(out _, out _, out _, out _);

    private static LslTwinModeBridge CreateBridge(
        out FakeOutletService commandOutlet,
        out FakeOutletService configOutlet,
        out FakeMonitorService stateMonitor,
        out FakeSequenceStore sequenceStore,
        FakeSequenceStore? providedSequenceStore = null)
    {
        commandOutlet = new FakeOutletService();
        configOutlet = new FakeOutletService();
        stateMonitor = new FakeMonitorService();
        sequenceStore = providedSequenceStore ?? new FakeSequenceStore();
        return new LslTwinModeBridge(commandOutlet, configOutlet, stateMonitor, sequenceStore);
    }

    private sealed class FakeOutletService : ILslOutletService
    {
        public LslRuntimeState RuntimeState { get; } = new(false, "Fake outlet");
        public bool IsOpen { get; private set; }
        public string? LastPublishedCommand { get; private set; }
        public int? LastPublishedSequence { get; private set; }
        public int PublishedEntryCount { get; private set; }

        public OperationOutcome Open(string streamName, string streamType, int channelCount)
        {
            IsOpen = true;
            return new OperationOutcome(OperationOutcomeKind.Preview, "Fake opened.", "test");
        }

        public void Close() => IsOpen = false;

        public void PushSample(string[] values) { }

        public OperationOutcome PublishConfigSnapshot(IReadOnlyList<RuntimeConfigEntry> entries)
        {
            PublishedEntryCount = entries.Count;
            return new OperationOutcome(OperationOutcomeKind.Preview, "Fake config.", "test");
        }

        public OperationOutcome PublishCommand(TwinModeCommand command, int sequence)
        {
            LastPublishedCommand = command.ActionId;
            LastPublishedSequence = sequence;
            return new OperationOutcome(OperationOutcomeKind.Preview, "Fake command.", "test");
        }

        public void Dispose() { }
    }

    private sealed class FakeSequenceStore : ITwinCommandSequenceStore
    {
        public int LastIssuedSequence { get; private set; }

        public int Next()
        {
            LastIssuedSequence++;
            return LastIssuedSequence;
        }
    }

    private sealed class FakeMonitorService : ILslMonitorService
    {
        private readonly Queue<LslMonitorReading> _readings = new();

        public LslRuntimeState RuntimeState { get; } = new(true, "Fake liblsl runtime for core tests.");

        public void EnqueueReading(LslMonitorReading reading) => _readings.Enqueue(reading);

        public async IAsyncEnumerable<LslMonitorReading> MonitorAsync(
            LslMonitorSubscription subscription,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (_readings.TryDequeue(out var reading))
            {
                yield return reading;
            }

            // Wait for cancellation
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
