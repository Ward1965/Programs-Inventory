using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Orchestrates the whole discovery pipeline: Start Menu → Registry → AppX → shortcut analysis →
/// executable analysis → icons → identity resolution → classification. Async, cancelable, progress-aware.
/// </summary>
public interface IScanEngine
{
    Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>Snapshot of discovery progress, fed to the UI.</summary>
public sealed class ScanProgress
{
    public required string Stage { get; init; }
    public int Percent { get; init; }
    public int ProgramsFound { get; init; }
}

/// <summary>Final report of one scan run.</summary>
public sealed class ScanResult
{
    public required IReadOnlyList<ProgramInfo> Programs { get; init; }
    public required TimeSpan Duration { get; init; }
    public int ErrorCount { get; init; }

    public int InstalledCount => Programs.Count(p => p.Status == ProgramStatus.Installed);
    public int ShortcutOnlyCount => Programs.Count(p => p.Status == ProgramStatus.ShortcutOnly);
    public int InstalledAndShortcutCount => Programs.Count(p => p.Status == ProgramStatus.InstalledAndShortcut);
    public int BrokenCount => Programs.Count(p => p.Status == ProgramStatus.BrokenShortcut);
    public int StoreAppCount => Programs.Count(p => p.IsStoreApp);
}