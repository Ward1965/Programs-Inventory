using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.UI.ViewModels;

/// <summary>
/// One entry in the sidebar. Glyph uses the Segoe MDL2 / Segoe Fluent Icons font
/// so no image assets are needed and the UI stays lightweight.
/// </summary>
public sealed class NavItem
{
    public required NavigationKey Key { get; init; }
    public required string Title { get; init; }

    /// <summary>Segoe MDL2 glyph codepoint (e.g. "\uE8F1").</summary>
    public required string Glyph { get; init; }
}