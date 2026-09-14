using System.IO;
using Microsoft.Win32;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Services;

/// <summary>
/// Reads installed programs from the Windows Uninstall registry keys
/// (HKLM 64-bit, HKLM WOW6432Node, HKCU). Only facts are recorded –
/// an executable is stored only when it can be validated to exist.
/// </summary>
public sealed class RegistryScanner : IInstalledProgramScanner
{
    private readonly ILogger _logger;

    public RegistryScanner(ILogger? logger = null)
    {
        _logger = logger ?? new SilentLogger();
    }

    public string SourceName => Core.Strings.ScanningInstalledPrograms;

    public Task<IReadOnlyList<ProgramInfo>> ScanAsync(CancellationToken cancellationToken)
    {
        var results = new List<ProgramInfo>();

        ScanHive("HKLM", Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", results, cancellationToken);
        ScanHive("HKLM", Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", results, cancellationToken);
        ScanHive("HKCU", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", results, cancellationToken);

        return Task.FromResult<IReadOnlyList<ProgramInfo>>(results);
    }

    private void ScanHive(string hiveName, RegistryKey hive, string subKeyPath, List<ProgramInfo> results, CancellationToken cancellationToken)
    {
        using var key = hive.OpenSubKey(subKeyPath);
        if (key is null)
        {
            return;
        }

        foreach (var entryName in key.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var entry = key.OpenSubKey(entryName);
            if (entry is null)
            {
                continue;
            }

            try
            {
                var program = BuildProgram(hiveName, subKeyPath, entryName, entry);
                if (program is not null)
                {
                    results.Add(program);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Registry entry failed: {subKeyPath}\\{entryName}", ex);
            }
        }
    }

    private static ProgramInfo? BuildProgram(string hiveName, string subKeyPath, string entryName, RegistryKey entry)
    {
        var displayName = ReadString(entry, "DisplayName");
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var isSystemComponent = ReadBool(entry, "SystemComponent") ?? false;
        var installLocation = ReadString(entry, "InstallLocation");
        var displayIcon = ReadString(entry, "DisplayIcon");
        var (iconPath, _) = IconParts.Parse(displayIcon);

        var executable = ResolveExecutable(installLocation, iconPath);

        return new ProgramInfo
        {
            Name = displayName,
            DisplayName = displayName,
            Publisher = ReadString(entry, "Publisher"),
            Version = ReadString(entry, "DisplayVersion"),
            InstallLocation = NullIfEmpty(installLocation),
            InstallSource = ReadString(entry, "InstallSource"),
            UninstallString = ReadString(entry, "UninstallString"),
            QuietUninstallString = ReadString(entry, "QuietUninstallString"),
            ExecutablePath = ExecutableExists(executable),
            IconPath = ExecutableExists(iconPath) ?? iconPath,
            IconSource = IconSource.Registry,
            RegistryKey = $"{hiveName}\\{subKeyPath}\\{entryName}",
            EstimatedSizeKB = ReadInt(entry, "EstimatedSize"),
            UrlInfoAbout = ReadString(entry, "URLInfoAbout"),
            HelpLink = ReadString(entry, "HelpLink"),
            ReleaseType = ReadString(entry, "ReleaseType"),
            IsSystemComponent = isSystemComponent,
            IsWindowsInstaller = ReadBool(entry, "WindowsInstaller"),
            Type = isSystemComponent ? ProgramType.SystemComponent : ProgramType.Unknown,
            Sources = SourceType.Registry,
        };
    }

    /// <summary>Finds an executable for the install if it can be done without guessing.</summary>
    private static string? ResolveExecutable(string? installLocation, string? displayIcon)
    {
        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            var topExes = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly).Take(3).ToList();
            if (topExes.Count == 1)
            {
                return topExes[0];
            }

            if (topExes.Count > 1 && !string.IsNullOrWhiteSpace(displayIcon))
            {
                var match = topExes.FirstOrDefault(e =>
                    Path.GetFileName(e).Equals(Path.GetFileName(displayIcon), StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }
            }
        }

        return displayIcon;
    }

    private static string? ExecutableExists(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? ReadString(RegistryKey key, string name)
        => NullIfEmpty(key.GetValue(name)?.ToString());

    private static int? ReadInt(RegistryKey key, string name)
        => key.GetValue(name) is int i ? i : null;

    private static bool? ReadBool(RegistryKey key, string name)
        => key.GetValue(name) is int i ? i != 0 : null;
}