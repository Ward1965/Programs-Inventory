using System.Collections.ObjectModel;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Persistence layer for the inventory. Phase 10 will back this with SQLite;
/// Phase 1 uses an in-memory implementation so the UI pipeline works end to end.
/// </summary>
public interface IProgramRepository
{
    Task SaveAsync(IReadOnlyCollection<ProgramInfo> programs, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProgramInfo>> LoadAllAsync(CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}