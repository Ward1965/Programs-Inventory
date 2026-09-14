namespace WindowsProgramInventory.Models;

/// <summary>
/// Classification status of a discovered program, produced by the Classification Engine.
/// </summary>
public enum ProgramStatus
{
    Unknown = 0,
    Installed = 1,
    ShortcutOnly = 2,
    InstalledAndShortcut = 3,
    BrokenShortcut = 4,
}