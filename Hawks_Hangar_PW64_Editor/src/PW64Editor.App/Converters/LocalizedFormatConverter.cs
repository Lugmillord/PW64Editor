using System.Globalization;
using System.Windows.Data;
using PW64Editor.App.Services;

namespace PW64Editor.App.Converters;

/// <summary>
/// Formats a bound value with a translated pattern: <c>ConverterParameter='Warning: {0}'</c>.
/// </summary>
public sealed class LocalizedFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        L.F(parameter as string ?? "{0}", value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
