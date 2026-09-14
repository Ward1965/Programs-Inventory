using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Discovers shortcut files (.lnk, .url, .exe, .appref-ms) inside the user and machine Start Menu.
/// </summary>
public interface IStartMenuScanner : IProgramSourceScanner
{
}