namespace WindowsProgramInventory.Models;

/// <summary>
/// Kind of program, based on the evidence that was collected for it.
/// </summary>
public enum ProgramType
{
    Unknown = 0,
    DesktopApp = 1,
    StoreApp = 2,
    PortableApp = 3,
    SystemComponent = 4,
    Shortcut = 5,
}