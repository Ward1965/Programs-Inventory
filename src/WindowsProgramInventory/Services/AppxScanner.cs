using System.IO;
using Microsoft.Win32;
using System.Xml.Linq;
using WindowsProgramInventory.Core;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Services;

/// <summary>
/// Discovers packaged applications (Microsoft Store / AppX / MSIX) for the current user
/// from the AppModel repository and resolves names from each package manifest.
/// </summary>
public sealed class AppxScanner : IAppxScanner
{
    private const string PackagesRoot =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private const int MaxExeDepth = 3;

    private readonly ILogger _logger;

    public AppxScanner(ILogger? logger = null)
    {
        _logger = logger ?? new SilentLogger();
    }

    public string SourceName => Core.Strings.ScanningStoreApps;

    public Task<IReadOnlyList<ProgramInfo>> ScanAsync(CancellationToken cancellationToken)
    {
        var results = new List<ProgramInfo>();

        using var root = Registry.CurrentUser.OpenSubKey(PackagesRoot);
        if (root is null)
        {
            return Task.FromResult<IReadOnlyList<ProgramInfo>>(results);
        }

        foreach (var packageName in root.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var packageKey = root.OpenSubKey(packageName);
            if (packageKey is null)
            {
                continue;
            }

            try
            {
                var program = BuildProgram(packageKey, packageName, cancellationToken);
                if (program is not null)
                {
                    results.Add(program);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"AppX package failed: {packageName}", ex);
            }
        }

        return Task.FromResult<IReadOnlyList<ProgramInfo>>(results);
    }

    private ProgramInfo? BuildProgram(RegistryKey packageKey, string packageName, CancellationToken cancellationToken)
    {
        var installRoot = packageKey.GetValue("PackageRootFolder")?.ToString();
        if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
        {
            return null;
        }

        var displayName = packageName;
        var publisher = string.Empty;
        var version = string.Empty;
        var iconPath = (string?)null;
        var appUserModelId = packageKey.GetValue("AppUserModelId")?.ToString();

        var manifestPath = Path.Combine(installRoot, "AppxManifest.xml");
        if (File.Exists(manifestPath))
        {
            try
            {
                ParseManifest(manifestPath, installRoot, out displayName, out publisher, out version, out iconPath);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Cannot parse AppxManifest for {packageName}: {ex.Message}");
            }
        }

        var executable = FindExecutable(installRoot, cancellationToken);

        return new ProgramInfo
        {
            Name = displayName,
            DisplayName = displayName,
            Publisher = NullIfEmpty(publisher),
            Version = NullIfEmpty(version),
            ExecutablePath = executable,
            InstallLocation = installRoot,
            IconPath = iconPath,
            IconSource = IconSource.AppX,
            AppUserModelId = NullIfEmpty(appUserModelId),
            PackageName = packageName,
            Type = ProgramType.StoreApp,
            Sources = SourceType.AppX,
        };
    }

    private static void ParseManifest(string manifestPath, string installRoot,
        out string displayName, out string publisher, out string version, out string? iconPath)
    {
        displayName = string.Empty;
        publisher = string.Empty;
        version = string.Empty;
        iconPath = null;

        var doc = XDocument.Load(manifestPath);
        var root = doc.Root;
        if (root is null)
        {
            return;
        }

        version = root.Attribute("Version")?.Value ?? string.Empty;

        var props = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "Properties");
        if (props is null)
        {
            return;
        }

        displayName = props.Elements().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value ?? string.Empty;
        publisher = props.Elements().FirstOrDefault(e => e.Name.LocalName == "Publisher")?.Value ?? string.Empty;
        var logo = props.Elements().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value;

        if (!string.IsNullOrWhiteSpace(logo) && !logo.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            var logoPath = Path.Combine(installRoot, logo.Replace('/', '\\'));
            if (File.Exists(logoPath))
            {
                iconPath = logoPath;
            }
        }
    }

    private static string? FindExecutable(string root, CancellationToken cancellationToken)
    {
        var directories = new Queue<(string Path, int Depth)>();
        directories.Enqueue((root, 0));

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, depth) = directories.Dequeue();

            try
            {
                var topExe = Directory.EnumerateFiles(current, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (topExe is not null)
                {
                    return topExe;
                }

                if (depth >= MaxExeDepth)
                {
                    continue;
                }

                foreach (var sub in Directory.EnumerateDirectories(current))
                {
                    var name = Path.GetFileName(sub);
                    if (name.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("AppxMetadata", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Resources", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    directories.Enqueue((sub, depth + 1));
                }
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        return null;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}