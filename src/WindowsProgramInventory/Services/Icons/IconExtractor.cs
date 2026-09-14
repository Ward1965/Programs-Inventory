using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WindowsProgramInventory.Core;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Services.Icons;

/// <summary>
/// Extracts the best icon for a program (shortcut icon → registry icon → executable → AppX logo)
/// at its native resolution for the requested size and persists it as a PNG in the IconCache keyed
/// by (source + modified time + icon index + requested size). Icons keep their true pixel size;
/// the UI scales them to the classic shell sizes (16–256).
/// </summary>
public sealed class IconExtractor : IIconExtractor
{
    private readonly ILogger _logger;

    public IconExtractor(ILogger? logger = null)
    {
        _logger = logger ?? new SilentLogger();
    }

    public Task<string?> GetCachedIconAsync(ProgramInfo program, int size, CancellationToken cancellationToken)
    {
        var source = ResolveSource(program);
        if (source.Path is null || string.IsNullOrWhiteSpace(source.Path))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            Directory.CreateDirectory(AppConstants.IconCachePath);

            var hash = ComputeHash(source.Path, source.Index, size);
            var cachedFile = Path.Combine(AppConstants.IconCachePath, $"{hash}.png");
            if (File.Exists(cachedFile))
            {
                return Task.FromResult<string?>(cachedFile);
            }

            using var bitmap = ExtractFrom(source.Path, source.Index, size, cancellationToken);
            if (bitmap is null)
            {
                return Task.FromResult<string?>(null);
            }

            SavePng(bitmap, cachedFile);
            return Task.FromResult<string?>(cachedFile);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Icon extraction failed for {source.Path}: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }

    private static (string? Path, int Index) ResolveSource(ProgramInfo program)
    {
        if (!string.IsNullOrWhiteSpace(program.ShortcutIconLocation))
        {
            return IconParts.Parse(program.ShortcutIconLocation);
        }

        if (!string.IsNullOrWhiteSpace(program.IconPath))
        {
            var (path, index) = IconParts.Parse(program.IconPath);
            return (path, index);
        }

        if (!string.IsNullOrWhiteSpace(program.ExecutablePath))
        {
            return (program.ExecutablePath, 0);
        }

        return (null, 0);
    }

    private static Bitmap? ExtractFrom(string path, int index, int size, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path))
        {
            return ExtractShellIcon(path);
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".png")
        {
            return LoadImage(path);
        }

        if (extension == ".ico")
        {
            return LoadIcon(path, size);
        }

        // .exe / .dll / anything else: ask the shell for the icon at the requested size.
        return ExtractIconResource(path, index, size) ?? ExtractShellIcon(path);
    }

    /// <summary>PrivateExtractIcons returns the source's closest matching icon, keeping its real resolution.</summary>
    private static Bitmap? ExtractIconResource(string path, int index, int size)
    {
        var handles = new IntPtr[1];
        try
        {
            var count = PrivateExtractIcons(path, index, size, size, handles, null, 1, 0);
            if (count > 0 && handles[0] != IntPtr.Zero)
            {
                using var icon = Icon.FromHandle(handles[0]);
                using var bitmap = icon.ToBitmap();
                return new Bitmap(bitmap);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handles[0] != IntPtr.Zero)
            {
                DestroyIcon(handles[0]);
            }
        }

        return null;
    }

    /// <summary>Picks the .ico frame closest to the requested size; keeps frame's true pixels.</summary>
    private static Bitmap? LoadIcon(string path, int size)
    {
        try
        {
            using var icon = new Icon(path, size, size);
            using var bitmap = icon.ToBitmap();
            return new Bitmap(bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? ExtractShellIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            return icon is null ? null : new Bitmap(icon.ToBitmap());
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? LoadImage(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeHash(string path, int index, int size)
    {
        var modified = DateTime.MinValue;
        try
        {
            modified = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch
        {
        }

        var key = $"{path}|{modified.Ticks}|{index}|{size}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
    }

    private static void SavePng(Bitmap bitmap, string cachedFile)
    {
        using var stream = new FileStream(cachedFile, FileMode.Create, FileAccess.Write, FileShare.None);
        bitmap.Save(stream, ImageFormat.Png);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(
        string szFileName, int nIconIndex, int cxIcon, int cyIcon,
        IntPtr[] phicon, int[]? piconid, uint nIcons, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}