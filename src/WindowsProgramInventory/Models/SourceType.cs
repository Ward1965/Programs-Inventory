namespace WindowsProgramInventory.Models;

/// <summary>
/// Where a piece of evidence about a program came from. Flags, a program can have many sources.
/// </summary>
[Flags]
public enum SourceType
{
    None = 0,
    StartMenu = 1 << 0,
    Registry = 1 << 1,
    AppX = 1 << 2,
    Msix = 1 << 3,
    Executable = 1 << 4,
    Shortcut = 1 << 5,
}