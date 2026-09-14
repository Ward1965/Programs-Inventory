using System.IO;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Services;

/// <summary>
/// Merges the same program discovered from multiple sources into one ProgramInfo.
/// Matching priority: executable path → (name + publisher) → display name.
/// Fields are merged (union of sources, first-wins for details, gap filling).
/// </summary>
public sealed class IdentityResolver : IProgramIdentityResolver
{
    public IReadOnlyList<ProgramInfo> Merge(IEnumerable<ProgramInfo> discovered)
    {
        var merged = new Dictionary<string, ProgramInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var program in discovered)
        {
            var key = ComputeKey(program);
            if (merged.TryGetValue(key, out var existing))
            {
                MergeInto(existing, program);
            }
            else
            {
                merged[key] = program;
            }
        }

        return merged.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ComputeKey(ProgramInfo program)
    {
        if (!string.IsNullOrWhiteSpace(program.ExecutablePath))
        {
            return $"exe:{Path.GetFullPath(program.ExecutablePath)}";
        }

        if (!string.IsNullOrWhiteSpace(program.Name) && !string.IsNullOrWhiteSpace(program.Publisher))
        {
            return $"name:{program.Name}|{program.Publisher}";
        }

        return $"name:{program.Name ?? program.DisplayName ?? Guid.NewGuid().ToString()}";
    }

    private static void MergeInto(ProgramInfo target, ProgramInfo source)
    {
        target.Sources |= source.Sources;

        target.Name ??= source.Name;
        target.DisplayName ??= source.DisplayName;
        target.Publisher ??= source.Publisher;
        target.Version ??= source.Version;

        target.ExecutablePath ??= source.ExecutablePath;
        target.InstallLocation ??= source.InstallLocation;
        target.InstallSource ??= source.InstallSource;
        target.UninstallString ??= source.UninstallString;
        target.QuietUninstallString ??= source.QuietUninstallString;

        target.ShortcutPath ??= source.ShortcutPath;
        target.ShortcutTarget ??= source.ShortcutTarget;
        target.ShortcutArguments ??= source.ShortcutArguments;
        target.ShortcutWorkingDirectory ??= source.ShortcutWorkingDirectory;
        target.ShortcutDescription ??= source.ShortcutDescription;
        target.ShortcutIconLocation ??= source.ShortcutIconLocation;

        target.IconPath ??= source.IconPath;
        if (target.IconSource == IconSource.None)
        {
            target.IconSource = source.IconSource;
        }

        target.FileSizeBytes ??= source.FileSizeBytes;
        target.CreatedDate ??= source.CreatedDate;
        target.ModifiedDate ??= source.ModifiedDate;
        target.FileVersion ??= source.FileVersion;
        target.ProductVersion ??= source.ProductVersion;
        target.ProductName ??= source.ProductName;
        target.CompanyName ??= source.CompanyName;
        target.Copyright ??= source.Copyright;
        target.OriginalFilename ??= source.OriginalFilename;
        target.Description ??= source.Description;
        if (target.Architecture == ProgramArchitecture.Unknown)
        {
            target.Architecture = source.Architecture;
        }

        target.RegistryKey ??= source.RegistryKey;
        target.EstimatedSizeKB ??= source.EstimatedSizeKB;
        target.UrlInfoAbout ??= source.UrlInfoAbout;
        target.HelpLink ??= source.HelpLink;
        target.ReleaseType ??= source.ReleaseType;
        target.IsSystemComponent ??= source.IsSystemComponent;
        target.IsWindowsInstaller ??= source.IsWindowsInstaller;

        target.PackageName ??= source.PackageName;
        target.PackageFamilyName ??= source.PackageFamilyName;
        target.AppUserModelId ??= source.AppUserModelId;

        if (target.Type == ProgramType.Unknown)
        {
            target.Type = source.Type;
        }

        if (string.IsNullOrWhiteSpace(target.ShortcutPath) && !string.IsNullOrWhiteSpace(source.ShortcutPath))
        {
            target.ShortcutPath = source.ShortcutPath;
        }
    }
}