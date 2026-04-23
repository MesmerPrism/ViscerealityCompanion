using System.Windows.Media;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App.ViewModels;

internal sealed record ValidationCapturePlotLoadResult(
    bool HasData,
    string Summary,
    PointCollection Points);

internal sealed record PinnedStartupHotloadSyncResult(
    string? CsvPath,
    SussexVisualStartupHotloadPlan? VisualPlan,
    SussexControllerBreathingStartupHotloadPlan? ControllerPlan,
    bool AppliedToDevice);

internal sealed record RecordingSampleSnapshot(
    StudyDataRecordingSession Session,
    IReadOnlyDictionary<string, string> TwinState,
    DateTimeOffset RecordedAtUtc,
    DateTimeOffset SourceTimestampUtc,
    string QuestSelector);

internal sealed record WorkflowGuideHazardState(
    OperationOutcomeKind Level,
    string Summary,
    string Detail);

internal sealed record StudyTwinCommandRequest(
    string ActionId,
    string Label,
    bool? RequestedVisible,
    int? Sequence,
    DateTimeOffset SentAtUtc,
    int? PreviousConfirmedSequence,
    string? PreviousConfirmedTimestampRaw,
    string? PreviousRecenterAnchorTimestampRaw,
    double? PreviousRecenterDistance,
    bool? PreviousObservedVisible);

internal sealed record AutomaticBreathingRequest(
    bool AutomaticModeSelected,
    bool AutomaticRunning,
    string RequestedLabel,
    DateTimeOffset RequestedAtUtc);

internal readonly record struct AutomaticBreathingTelemetry(
    string? RoutingMode,
    string? RoutingLabel,
    bool AutomaticRoute,
    bool? AutomaticRunning,
    bool ControllerVolumeRoute,
    double? AutomaticValue,
    bool HasAnyTelemetry);

internal readonly record struct StudyTwinCommandConfirmation(
    int? Sequence,
    string? TimestampRaw,
    DateTimeOffset? Timestamp,
    string? Label,
    string? Source,
    string? ActionId);

public sealed record WorkflowGuideStepDefinition(
    int Number,
    string Title,
    string Explanation);

public sealed record WorkflowGuideGateState(
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    bool Ready);

public sealed class WorkflowGuideCheckItem : ObservableObject
{
    private string _label;
    private string _summary;
    private string _detail;
    private OperationOutcomeKind _level;

    public WorkflowGuideCheckItem(string label, string summary, string detail, OperationOutcomeKind level)
    {
        _label = label;
        _summary = summary;
        _detail = detail;
        _level = level;
    }

    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public OperationOutcomeKind Level
    {
        get => _level;
        private set => SetProperty(ref _level, value);
    }

    public void UpdateFrom(WorkflowGuideCheckItem source)
    {
        Label = source.Label;
        Summary = source.Summary;
        Detail = source.Detail;
        Level = source.Level;
    }
}

public sealed class WorkflowGuideActionItem : ObservableObject
{
    private string _label;
    private AsyncRelayCommand _command;
    private bool _isEnabled;
    private bool _isRunning;
    private bool? _stateIsOn;
    private bool? _actionIsEnabling;

    public WorkflowGuideActionItem(
        string label,
        AsyncRelayCommand command,
        bool isEnabled,
        bool isRunning,
        bool? stateIsOn,
        bool? actionIsEnabling)
    {
        _label = label;
        _command = command;
        _isEnabled = isEnabled;
        _isRunning = isRunning;
        _stateIsOn = stateIsOn;
        _actionIsEnabling = actionIsEnabling;
    }

    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    public AsyncRelayCommand Command
    {
        get => _command;
        private set => SetProperty(ref _command, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        private set => SetProperty(ref _isEnabled, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public bool? StateIsOn
    {
        get => _stateIsOn;
        private set => SetProperty(ref _stateIsOn, value);
    }

    public bool? ActionIsEnabling
    {
        get => _actionIsEnabling;
        private set => SetProperty(ref _actionIsEnabling, value);
    }

    public void UpdateFrom(WorkflowGuideActionItem source)
    {
        Label = source.Label;
        Command = source.Command;
        IsEnabled = source.IsEnabled;
        IsRunning = source.IsRunning;
        StateIsOn = source.StateIsOn;
        ActionIsEnabling = source.ActionIsEnabling;
    }
}

internal readonly record struct RecenterEffectObservation(
    bool Observed,
    bool AnchorUpdated,
    string? AnchorTimestampRaw,
    DateTimeOffset? AnchorTimestamp,
    double? PreviousDistance,
    double? CurrentDistance,
    bool DistanceImproved);

internal readonly record struct ParticleVisibilityEffectObservation(
    bool Observed,
    bool? PreviousVisible,
    bool? CurrentVisible,
    bool? RequestedVisible);

internal sealed record ControllerCalibrationQualityStatus(
    bool Visible,
    OperationOutcomeKind Level,
    bool Accepted,
    string Badge,
    string Summary,
    string Expectation,
    string Metrics,
    string Cause,
    string Detail);

public sealed record StudyValueSection(string Id, string Label, string Description)
{
    public override string ToString() => Label;
}

public sealed class StudyStatusRowViewModel : ObservableObject
{
    private string _label;
    private string _key;
    private string _value;
    private string _expected;
    private string _detail;
    private OperationOutcomeKind _level;

    public StudyStatusRowViewModel(StudyStatusRow source)
    {
        _label = source.Label;
        _key = source.Key;
        _value = source.Value;
        _expected = source.Expected;
        _detail = source.Detail;
        _level = source.Level;
    }

    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    public string Key
    {
        get => _key;
        private set => SetProperty(ref _key, value);
    }

    public string Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    public string Expected
    {
        get => _expected;
        private set => SetProperty(ref _expected, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public OperationOutcomeKind Level
    {
        get => _level;
        private set => SetProperty(ref _level, value);
    }

    public void Apply(StudyStatusRow source)
    {
        Label = source.Label;
        Key = source.Key;
        Value = source.Value;
        Expected = source.Expected;
        Detail = source.Detail;
        Level = source.Level;
    }
}

public sealed record StudyStatusRow(
    string Label,
    string Key,
    string Value,
    string Expected,
    string Detail,
    OperationOutcomeKind Level);
