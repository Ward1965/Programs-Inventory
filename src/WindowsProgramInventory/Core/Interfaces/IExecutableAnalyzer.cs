using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Reads metadata from an executable: file version info, size, dates and architecture.
/// </summary>
public interface IExecutableAnalyzer
{
    void Analyze(ProgramInfo program);
}