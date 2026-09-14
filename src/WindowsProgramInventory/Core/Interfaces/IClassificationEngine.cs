using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Combines all evidence (shortcut / registry / AppX / executable) into a final classification:
/// Installed, Shortcut Only, Installed + Shortcut, Broken Shortcut or Unknown.
/// </summary>
public interface IClassificationEngine
{
    void Classify(ProgramInfo program);
}