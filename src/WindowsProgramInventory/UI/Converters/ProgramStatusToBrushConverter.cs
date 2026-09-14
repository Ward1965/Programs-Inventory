using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.UI.Converters;

/// <summary>
/// Maps a program status to its badge color.
/// </summary>
public sealed class ProgramStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            ProgramStatus.Installed => AppResource("BadgeInstalledBrush"),
            ProgramStatus.ShortcutOnly => AppResource("BadgeShortcutBrush"),
            ProgramStatus.InstalledAndShortcut => AppResource("BadgeBothBrush"),
            ProgramStatus.BrokenShortcut => AppResource("BadgeBrokenBrush"),
            _ => AppResource("BadgeNeutralBrush"),
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static object AppResource(string key)
        => System.Windows.Application.Current?.TryFindResource(key) as SolidColorBrush
           ?? System.Windows.Media.Brushes.Gray;
}