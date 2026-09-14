using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Storage;

/// <summary>
/// Phase 1 persistence: keeps the inventory in memory. The real implementation in
/// Phase 10 replaces this with SQLite (scan cache, history, statistics, fast search).
/// </summary>
public sealed class InMemoryProgramRepository : IProgramRepository
{
    private readonly object _gate = new();
    private readonly List<ProgramInfo> _programs = new();

    public Task SaveAsync(IReadOnlyCollection<ProgramInfo> programs, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _programs.Clear();
            _programs.AddRange(programs);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProgramInfo>> LoadAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ProgramInfo>>(_programs.ToList());
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _programs.Clear();
        }

        return Task.CompletedTask;
    }
}