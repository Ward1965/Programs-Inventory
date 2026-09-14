using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Merges the same program discovered from multiple sources into a single ProgramInfo,
/// using ordered identity matches (package → executable → product code → registry → shortcut target → name+publisher).
/// </summary>
public interface IProgramIdentityResolver
{
    IReadOnlyList<ProgramInfo> Merge(IEnumerable<ProgramInfo> discovered);
}