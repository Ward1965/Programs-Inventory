namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Extracts the best icon available for a program: shortcut → registry → executable → AppX → default.
/// Results are written as PNGs to the on-disk IconCache keyed by content hash
/// (path + modified time + icon index), so icons are never re-extracted needlessly.
/// </summary>
public interface IIconExtractor
{
    /// <summary>Returns the cached PNG path for the given program (or null when no icon is available).</summary>
    Task<string?> GetCachedIconAsync(Models.ProgramInfo program, int size, CancellationToken cancellationToken);
}