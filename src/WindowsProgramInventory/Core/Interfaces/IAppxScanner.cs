using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Discovers packaged applications: Microsoft Store, AppX, MSIX and per-user AppModel packages.
/// </summary>
public interface IAppxScanner : IProgramSourceScanner
{
}