using System.Globalization;
using System.Windows.Data;

namespace PW64Editor.App.Converters;

/// <summary>
/// Shows how many items a group contains, including the items of all its subgroups.
/// (A group's own ItemCount only counts its direct subgroups.)
/// </summary>
public sealed class GroupItemCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int count = value is CollectionViewGroup group ? CountItems(group) : 0;
        return count == 1 ? "1 text" : $"{count} texts";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static int CountItems(CollectionViewGroup group) =>
        group.IsBottomLevel ? group.ItemCount : group.Items.OfType<CollectionViewGroup>().Sum(CountItems);
}
