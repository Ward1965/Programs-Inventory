namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Switches the application between Light, Dark and System themes at runtime.
/// Implementation is UI-specific (WPF resource dictionaries); VMs depend only on this abstraction.
/// </summary>
public interface IThemeService
{
    /// <summary>Available themes: "System", "Light", "Dark".</summary>
    IReadOnlyList<string> AvailableThemes { get; }

    string CurrentTheme { get; }

    void Apply(string theme);
}