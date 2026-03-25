using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ViscerealityCompanion.App.ViewModels;

namespace ViscerealityCompanion.App;

public sealed class SyncStateToBrushConverter : IValueConverter
{
    private static readonly Brush VerifiedBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x9D, 0x5C));
    private static readonly Brush PendingBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xA0, 0x2E));
    private static readonly Brush UnknownBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x4C, 0x4C));
    private static readonly Brush InactiveBrush = new SolidColorBrush(Color.FromRgb(0xCD, 0xBE, 0xAC));

    static SyncStateToBrushConverter()
    {
        VerifiedBrush.Freeze();
        PendingBrush.Freeze();
        UnknownBrush.Freeze();
        InactiveBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SettingSyncState state
            ? state switch
            {
                SettingSyncState.Verified => VerifiedBrush,
                SettingSyncState.Pending => PendingBrush,
                SettingSyncState.Unknown => UnknownBrush,
                _ => InactiveBrush,
            }
            : InactiveBrush;

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
