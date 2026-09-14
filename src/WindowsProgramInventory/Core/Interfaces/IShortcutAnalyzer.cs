using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Resolves the real target of a Windows shortcut (.lnk/.url) without fabricating data.
/// </summary>
public interface IShortcutAnalyzer
{
    void Analyze(ProgramInfo program);
}