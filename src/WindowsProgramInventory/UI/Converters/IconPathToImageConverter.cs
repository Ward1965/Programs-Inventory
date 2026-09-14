using System.Globalization;
using System.Windows.Data;
using WindowsProgramInventory.Services.Icons;

namespace WindowsProgramInventory.UI.Converters;

/// <summary>
/// Resolves an extracted-icon cache path to a shared, frozen ImageSource
/// (decoded once and reused across every card / filter switch).
/// </summary>
public sealed class IconPathToImageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string path && path.Length > 0 ? IconCache.Get(path) ?? Binding.DoNothing : Binding.DoNothing;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}