using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WindowsProgramInventory.UI.Converters;

/// <summary>
/// true → Collapsed, false → Visible.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Multiplies a numeric value by ConverterParameter (a double). Used to scale nested visuals.
/// </summary>
public sealed class MultiplyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var factor = double.TryParse(parameter as string, NumberStyles.Float, culture, out var f) ? f : 1.0;
        return System.Convert.ToDouble(value, culture) * factor;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// null → Visible, any value → Collapsed. Used to show the placeholder when no icon exists.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// null → Collapsed, any value → Visible. Used to show the extracted icon image when it exists.
/// </summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}