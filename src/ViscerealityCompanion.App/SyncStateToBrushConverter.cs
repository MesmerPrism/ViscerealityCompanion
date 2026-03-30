using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ViscerealityCompanion.App.ViewModels;

namespace ViscerealityCompanion.App;

public sealed class SyncStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SettingSyncState state
            ? state switch
            {
                SettingSyncState.Verified => Application.Current.FindResource("StatusSuccessBrush"),
                SettingSyncState.Pending => Application.Current.FindResource("StatusWarningBrush"),
                SettingSyncState.Unknown => Application.Current.FindResource("StatusFailureBrush"),
                _ => Application.Current.FindResource("LineBrush"),
            }
            : Application.Current.FindResource("LineBrush");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class SyncStateToTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SettingSyncState state
            ? state switch
            {
                SettingSyncState.Verified => "Verified from headset",
                SettingSyncState.Pending => "Reported by headset, but value differs from the current editor value",
                SettingSyncState.Unknown => "No live report for this key from the active runtime",
                _ => "Twin tracking inactive",
            }
            : "Twin tracking inactive";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
