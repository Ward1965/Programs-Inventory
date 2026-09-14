using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowsProgramInventory.Services.Icons;

/// <summary>
/// In-memory cache of decoded, frozen icons keyed by cache file path.
/// Each PNG is decoded once and reused by every card that binds to it, so
/// switching groups or rebuilding a window never re-decodes images on the UI thread.
/// </summary>
public static class IconCache
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string path)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue(path, out var cached))
            {
                return cached;
            }
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.UriSource = new Uri("file:///" + System.IO.Path.GetFullPath(path).Replace('\\', '/'));
            bitmap.EndInit();
            bitmap.Freeze();

            lock (Sync)
            {
                Cache[path] = bitmap;
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}