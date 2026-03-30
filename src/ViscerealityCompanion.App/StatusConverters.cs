using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.App;

public sealed class OutcomeKindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not OperationOutcomeKind kind)
            return Application.Current.FindResource("MutedBrush");

        var key = kind switch
        {
            OperationOutcomeKind.Success => "StatusSuccessBrush",
            OperationOutcomeKind.Warning => "StatusWarningBrush",
            OperationOutcomeKind.Failure => "StatusFailureBrush",
            _ => "MutedBrush",
        };
        return Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class OutcomeKindToSoftBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not OperationOutcomeKind kind)
            return Application.Current.FindResource("PanelBrush");

        var key = kind switch
        {
            OperationOutcomeKind.Success => "StatusSuccessSoftBrush",
            OperationOutcomeKind.Warning => "StatusWarningSoftBrush",
            OperationOutcomeKind.Failure => "StatusFailureSoftBrush",
            _ => "PanelBrush",
        };
        return Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Application.Current.FindResource("StatusSuccessBrush");
        return Application.Current.FindResource("StatusFailureBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToSoftBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Application.Current.FindResource("StatusSuccessSoftBrush");
        return Application.Current.FindResource("StatusFailureSoftBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "Connected" : "Disconnected";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not OperatorLogLevel level)
            return Application.Current.FindResource("MutedBrush");

        var key = level switch
        {
            OperatorLogLevel.Warning => "StatusWarningBrush",
            OperatorLogLevel.Failure => "StatusFailureBrush",
            _ => "StatusInfoBrush",
        };
        return Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BatteryToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int pct)
            return Application.Current.FindResource("StatusWarningBrush");

        return pct switch
        {
            <= 15 => Application.Current.FindResource("StatusFailureBrush"),
            <= 35 => Application.Current.FindResource("StatusWarningBrush"),
            _ => Application.Current.FindResource("StatusSuccessBrush"),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TagsToBadgeTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<string> tags || tags.Count == 0)
            return "Unknown";

        bool isViscereality = tags.Any(t => t.Equals("viscereality", StringComparison.OrdinalIgnoreCase));
        if (!isViscereality)
        {
            if (tags.Any(t => t.Equals("browser", StringComparison.OrdinalIgnoreCase)))
                return "Browser";
            if (tags.Any(t => t.Equals("utility", StringComparison.OrdinalIgnoreCase)))
                return "Utility";
            return "External";
        }

        if (tags.Any(t => t.Equals("twin", StringComparison.OrdinalIgnoreCase)))
            return "Twin Mode";
        if (tags.Any(t => t.Equals("lsl", StringComparison.OrdinalIgnoreCase)))
            return "LSL Relay";
        if (tags.Any(t => t.Equals("runtime", StringComparison.OrdinalIgnoreCase)))
            return "Runtime";

        return "Viscereality";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TagsToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<string> tags || tags.Count == 0)
            return Application.Current.FindResource("MutedBrush");

        bool isViscereality = tags.Any(t => t.Equals("viscereality", StringComparison.OrdinalIgnoreCase));
        if (!isViscereality)
            return Application.Current.FindResource("StatusWarningBrush");

        if (tags.Any(t => t.Equals("lsl", StringComparison.OrdinalIgnoreCase)))
            return Application.Current.FindResource("StatusInfoBrush");

        return Application.Current.FindResource("StatusSuccessBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TagsToSoftBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<string> tags || tags.Count == 0)
            return Application.Current.FindResource("PanelAltBrush");

        bool isViscereality = tags.Any(t => t.Equals("viscereality", StringComparison.OrdinalIgnoreCase));
        if (!isViscereality)
            return Application.Current.FindResource("StatusWarningSoftBrush");

        if (tags.Any(t => t.Equals("lsl", StringComparison.OrdinalIgnoreCase)))
            return Application.Current.FindResource("StatusInfoSoftBrush");

        return Application.Current.FindResource("StatusSuccessSoftBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
