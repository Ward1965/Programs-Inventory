namespace WindowsProgramInventory.Models;

/// <summary>
/// Identifies a sidebar destination. In later phases filters map onto program status.
/// </summary>
public enum NavigationKey
{
    Home = 0,
    Installed = 1,
    Shortcuts = 2,
    Broken = 3,
    Store = 4,
    Statistics = 5,
    Settings = 6,
}