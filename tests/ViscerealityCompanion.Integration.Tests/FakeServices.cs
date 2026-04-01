using System.Runtime.CompilerServices;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Integration.Tests;

/// <summary>
/// Fake ILslOutletService that captures published data without needing real LSL.
/// </summary>
public sealed class FakeOutletService : ILslOutletService
{
    private readonly List<string[]> _publishedSamples = [];
    private readonly List<TwinModeCommand> _publishedCommands = [];
    private readonly List<RuntimeConfigEntry> _publishedEntries = [];

    public LslRuntimeState RuntimeState { get; } = new(false, "Fake outlet");
    public bool IsOpen { get; private set; }
    public string? LastPublishedCommand { get; private set; }
    public int? LastPublishedSequence { get; private set; }
    public int PublishedEntryCount { get; private set; }
    public IReadOnlyList<string[]> PublishedSamples => _publishedSamples;
    public IReadOnlyList<TwinModeCommand> PublishedCommands => _publishedCommands;

    public OperationOutcome Open(string streamName, string streamType, int channelCount)
    {
        IsOpen = true;
        return new OperationOutcome(OperationOutcomeKind.Preview, $"Fake outlet opened: {streamName}/{streamType}", "test");
    }

    public void Close() => IsOpen = false;

    public void PushSample(string[] values) => _publishedSamples.Add(values);

    public OperationOutcome PublishConfigSnapshot(IReadOnlyList<RuntimeConfigEntry> entries)
    {
        _publishedEntries.AddRange(entries);
        PublishedEntryCount = entries.Count;
        return new OperationOutcome(OperationOutcomeKind.Preview, "Fake config snapshot published.", "test");
    }

    public OperationOutcome PublishCommand(TwinModeCommand command, int sequence)
    {
        _publishedCommands.Add(command);
        LastPublishedCommand = command.ActionId;
        LastPublishedSequence = sequence;
        return new OperationOutcome(OperationOutcomeKind.Preview, "Fake command published.", "test");
    }

    public void Dispose() { }
}

/// <summary>
/// Fake ILslMonitorService that returns queued readings for testing.
/// </summary>
public sealed class FakeMonitorService : ILslMonitorService
{
    private readonly Queue<LslMonitorReading> _readings = new();

    public LslRuntimeState RuntimeState { get; } = new(true, "Fake liblsl runtime for integration tests.");

    public void EnqueueReading(LslMonitorReading reading) => _readings.Enqueue(reading);

    public void EnqueueReadings(IEnumerable<LslMonitorReading> readings)
    {
        foreach (var r in readings)
            _readings.Enqueue(r);
    }

    /// <summary>
    /// Enqueue a simple "set key=value" state reading.
    /// </summary>
    public void EnqueueSetting(string key, string value)
    {
        _readings.Enqueue(new LslMonitorReading(
            "Streaming", $"set {key}={value}", 0f, 20f, DateTimeOffset.UtcNow));
    }

    public async IAsyncEnumerable<LslMonitorReading> MonitorAsync(
        LslMonitorSubscription subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (_readings.TryDequeue(out var reading))
        {
            yield return reading;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
