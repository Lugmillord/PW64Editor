using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PW64Editor.App.Converters;

/// <summary>
/// The opposite of WPF's BooleanToVisibilityConverter: true hides the element, false shows it.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
