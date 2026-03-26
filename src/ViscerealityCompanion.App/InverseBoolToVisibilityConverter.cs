using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ViscerealityCompanion.App;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool visible && visible ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility visibility && visibility != Visibility.Visible;
}
