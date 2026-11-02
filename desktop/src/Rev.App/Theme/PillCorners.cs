using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Rev.App.Theme;

/// <summary>
/// Makes a perfect pill (capsule) shape: rounded ends whose corners are exactly half the height.
/// (A plain huge corner value would make WPF draw an oval instead.)
///
/// In XAML: CornerRadius="{Binding ActualHeight, RelativeSource={RelativeSource Self},
///                         Converter={StaticResource PillCorners}}"
/// </summary>
public sealed class PillCorners : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        new CornerRadius(value is double height ? height / 2 : 0);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
