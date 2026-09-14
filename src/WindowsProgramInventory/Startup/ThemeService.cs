using System.Windows;
using WindowsProgramInventory.Core.Interfaces;

namespace WindowsProgramInventory.Startup;

/// <summary>
/// Applies Light / Dark / System themes at runtime by swapping the active color
/// resource dictionary. References stay on "System" theme definitions so the app
/// follows the OS color preference by default (Win10/Win11).
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string LightUri = "UI/Themes/Light.xaml";
    private const string DarkUri = "UI/Themes/Dark.xaml";

    private ResourceDictionary? _current;

    public ThemeService()
    {
        CurrentTheme = "System";
    }

    public IReadOnlyList<string> AvailableThemes { get; } = new[] { "System", "Light", "Dark" };

    public string CurrentTheme { get; private set; }

    public void Apply(string theme)
    {
        var target = theme switch
        {
            "Light" => LightUri,
            "Dark" => DarkUri,
            _ => ResolveSystemTheme(),
        };

        CurrentTheme = theme;

        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var merged = application.Resources.MergedDictionaries;
        if (_current is not null)
        {
            merged.Remove(_current);
        }

        _current = new ResourceDictionary { Source = new Uri(target, UriKind.Relative) };
        merged.Insert(0, _current);

        // Brushes in Base.xaml are bound with DynamicResource; they recolor automatically.
    }

    /// <summary>Reads the OS "apps use light theme" preference from the registry.</summary>
    private static string ResolveSystemTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 1 ? LightUri : DarkUri;
        }
        catch
        {
            return DarkUri;
        }
    }

    /// <summary>Applies the currently active theme again (helper for tests / re-application).</summary>
    public void Reapply() => Apply(CurrentTheme);
}