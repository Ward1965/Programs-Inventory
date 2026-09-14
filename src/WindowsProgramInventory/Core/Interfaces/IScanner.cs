using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Marker interface for every scanner that can discover programs from one source.
/// The scan engine orchestrates many scanners in parallel and merges their results.
/// </summary>
public interface IScanner
{
    string SourceName { get; }
}

/// <summary>
/// A scanner that produces whole list of programs from a dedicated source.
/// Uniform contract so the Scan Engine can orchestrate them in parallel.
/// </summary>
public interface IProgramSourceScanner : IScanner
{
    Task<IReadOnlyList<ProgramInfo>> ScanAsync(CancellationToken cancellationToken);
}