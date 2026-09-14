using System.IO;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Services;

/// <summary>
/// Derives the final classification from collected evidence, never from assumptions:
/// a shortcut alone does not make a program "installed", and a registry entry alone
/// does not imply the executable still exists.
/// </summary>
public sealed class ClassificationEngine : IClassificationEngine
{
    public void Classify(ProgramInfo program)
    {
        var hasInstallEvidence = (program.Sources & (SourceType.Registry | SourceType.AppX | SourceType.Msix)) != 0;
        var hasStartMenu = (program.Sources & SourceType.StartMenu) != 0;
        var isStoreApp = (program.Sources & (SourceType.AppX | SourceType.Msix)) != 0;

        var targetPath = program.ExecutablePath ?? program.ShortcutTarget;
        var targetExists = !string.IsNullOrWhiteSpace(targetPath)
            && File.Exists(targetPath)
            && !Directory.Exists(targetPath);

        if (hasInstallEvidence && hasStartMenu && targetExists)
        {
            program.Status = ProgramStatus.InstalledAndShortcut;
        }
        else if (hasInstallEvidence)
        {
            program.Status = ProgramStatus.Installed;
        }
        else if (hasStartMenu)
        {
            var isLnk = program.ShortcutPath?.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) == true;
            program.Status = isLnk && !targetExists ? ProgramStatus.BrokenShortcut : ProgramStatus.ShortcutOnly;
        }
        else if (targetExists)
        {
            program.Status = ProgramStatus.ShortcutOnly;
        }
        else
        {
            program.Status = ProgramStatus.Unknown;
        }

        if (program.Type == ProgramType.Unknown)
        {
            if (isStoreApp)
            {
                program.Type = ProgramType.StoreApp;
            }
            else if (hasInstallEvidence && !hasStartMenu)
            {
                program.Type = ProgramType.DesktopApp;
            }
            else if (hasStartMenu && !hasInstallEvidence)
            {
                program.Type = targetExists ? ProgramType.PortableApp : ProgramType.Shortcut;
            }
        }
    }
}