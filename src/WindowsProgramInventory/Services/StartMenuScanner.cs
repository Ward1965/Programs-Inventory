using System.IO;
using WindowsProgramInventory.Core;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Services;

/// <summary>
/// Recursively discovers shortcut files in the user and machine Start Menu
/// (resp. %APPDATA% and %ProgramData% ...\Start Menu\Programs).
/// Produces raw ProgramInfo records; the ShortcutAnalyzer resolves their targets afterwards.
/// </summary>
public sealed class StartMenuScanner : IStartMenuScanner
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".url", ".exe", ".appref-ms", ".website",
    };

    private readonly ILogger _logger;

    public StartMenuScanner(ILogger? logger = null)
    {
        _logger = logger ?? new SilentLogger();
    }

    public string SourceName => Strings.DiscoveringStartMenu;

    public Task<IReadOnlyList<ProgramInfo>> ScanAsync(CancellationToken cancellationToken)
    {
        var results = new List<ProgramInfo>();
        var roots = new List<string>();

        AddRoot(UserStartMenuRoot());
        AddRoot(CommonStartMenuRoot());

        foreach (var root in roots)
        {
            ScanDirectory(root, results, cancellationToken);
        }

        return Task.FromResult<IReadOnlyList<ProgramInfo>>(results);

        void AddRoot(string? root)
        {
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                roots.Add(root);
            }
        }
    }

    private void ScanDirectory(string directory, List<ProgramInfo> results, CancellationToken cancellationToken)
    {
        foreach (var file in SafeEnumerateFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(file);
            if (!AcceptedExtensions.Contains(extension))
            {
                continue;
            }

            var name = SafeName(file, extension);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            try
            {
                results.Add(new ProgramInfo
                {
                    Name = name,
                    ShortcutPath = file,
                    Sources = SourceType.StartMenu | SourceType.Shortcut,
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to record Start Menu entry: {file}", ex);
            }
        }

        foreach (var sub in SafeEnumerateDirectories(directory))
        {
            ScanDirectory(sub, results, cancellationToken);
        }
    }

    private IEnumerable<string> SafeEnumerateFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Cannot enumerate files in {directory}: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private IEnumerable<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Cannot enumerate directories in {directory}: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static string SafeName(string file, string extension)
    {
        var baseName = Path.GetFileNameWithoutExtension(file);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return baseName;
        }

        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            return baseName;
        }

        return $"{baseName} ({extension.TrimStart('.')})";
    }

    private static string? UserStartMenuRoot()
    {
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        return string.IsNullOrEmpty(startMenu) ? null : Path.Combine(startMenu, "Programs");
    }

    private static string? CommonStartMenuRoot()
    {
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        return string.IsNullOrEmpty(common) ? null : common;
    }
}