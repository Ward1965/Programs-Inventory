using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Reads installed program entries from the Windows Registry (Uninstall keys, 32-bit and 64-bit).
/// </summary>
public interface IInstalledProgramScanner : IProgramSourceScanner
{
}