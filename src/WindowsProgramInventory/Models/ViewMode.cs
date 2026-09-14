namespace WindowsProgramInventory.Models;

/// <summary>
/// Display modes / icon density of the programs grid.
/// </summary>
public enum ViewMode
{
    Small = 0,
    Medium = 1,
    Large = 2,
    ExtraLarge = 3,
    List = 4,
}

public static class ViewModeExtensions
{
    /// <summary>Grid tile / icon size in pixels for the given mode.</summary>
    public static int IconSize(this ViewMode mode) => mode switch
    {
        ViewMode.Small => 32,
        ViewMode.Medium => 48,
        ViewMode.Large => 64,
        ViewMode.ExtraLarge => 96,
        ViewMode.List => 24,
        _ => 48,
    };
}