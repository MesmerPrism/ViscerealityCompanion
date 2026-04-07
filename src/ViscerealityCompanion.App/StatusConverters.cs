using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.App;

internal static class BrushResourceLookup
{
    public static object Muted() => Resource("MutedBrush", Brushes.Gray);

    public static object Panel() => Resource("PanelBrush", Brushes.Transparent);

    public static object PanelAlt() => Resource("PanelAltBrush", Brushes.Transparent);

    public static object Success() => Resource("StatusSuccessBrush", Brushes.ForestGreen);

    public static object SuccessSoft() => Resource("StatusSuccessSoftBrush", Brushes.Honeydew);

    public static object Warning() => Resource("StatusWarningBrush", Brushes.Goldenrod);

    public static object WarningSoft() => Resource("StatusWarningSoftBrush", Brushes.LemonChiffon);

    public static object Failure() => Resource("StatusFailureBrush", Brushes.IndianRed);

    public static object FailureSoft() => Resource("StatusFailureSoftBrush", Brushes.MistyRose);

    public static object Info() => Resource("StatusInfoBrush", Brushes.SteelBlue);

    public static object InfoSoft() => Resource("StatusInfoSoftBrush", Brushes.AliceBlue);

    private static object Resource(string key, Brush fallback)
        => Application.Current?.TryFindResource(key) ?? fallback;
}

public sealed class OutcomeKindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not OperationOutcomeKind kind)
            return BrushResourceLookup.Muted();

        return kind switch
        {
            OperationOutcomeKind.Success => BrushResourceLookup.Success(),
            OperationOutcomeKind.Warning => BrushResourceLookup.Warning(),
            OperationOutcomeKind.Failure => BrushResourceLookup.Failure(),
            _ => BrushResourceLookup.Muted(),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class OutcomeKindToSoftBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not OperationOutcomeKind kind)
            return BrushResourceLookup.Panel();

        return kind switch
        {
            OperationOutcomeKind.Success => BrushResourceLookup.SuccessSoft(),
            OperationOutcomeKind.Warning => BrushResourceLookup.WarningSoft(),
            OperationOutcomeKind.Failure => BrushResourceLookup.FailureSoft(),
            _ => BrushResourceLookup.Panel(),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return BrushResourceLookup.Success();
        return BrushResourceLookup.Failure();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToSoftBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return BrushResourceLookup.SuccessSoft();
        return BrushResourceLookup.FailureSoft();
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
            return BrushResourceLookup.Muted();

        return level switch
        {
            OperatorLogLevel.Warning => BrushResourceLookup.Warning(),
            OperatorLogLevel.Failure => BrushResourceLookup.Failure(),
            _ => BrushResourceLookup.Info(),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BatteryToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int pct)
            return BrushResourceLookup.Warning();

        return pct switch
        {
            <= 15 => BrushResourceLookup.Failure(),
            <= 35 => BrushResourceLookup.Warning(),
            _ => BrushResourceLookup.Success(),
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
            return BrushResourceLookup.Muted();

        bool isViscereality = tags.Any(t => t.Equals("viscereality", StringComparison.OrdinalIgnoreCase));
        if (!isViscereality)
            return BrushResourceLookup.Warning();

        if (tags.Any(t => t.Equals("lsl", StringComparison.OrdinalIgnoreCase)))
            return BrushResourceLookup.Info();

        return BrushResourceLookup.Success();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TagsToSoftBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<string> tags || tags.Count == 0)
            return BrushResourceLookup.PanelAlt();

        bool isViscereality = tags.Any(t => t.Equals("viscereality", StringComparison.OrdinalIgnoreCase));
        if (!isViscereality)
            return BrushResourceLookup.WarningSoft();

        if (tags.Any(t => t.Equals("lsl", StringComparison.OrdinalIgnoreCase)))
            return BrushResourceLookup.InfoSoft();

        return BrushResourceLookup.SuccessSoft();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
