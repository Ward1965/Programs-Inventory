using System.IO;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;
using WindowsProgramInventory.Windows.Shortcuts;

namespace WindowsProgramInventory.Services;

/// <summary>
/// Resolves the real target of a Start Menu shortcut (.lnk via COM IShellLinkW,
/// .url via its INI content). Never fabricates data; missing targets are left null
/// so the Classification Engine can flag broken shortcuts.
/// </summary>
public sealed class ShortcutAnalyzer : IShortcutAnalyzer
{
    private readonly ILogger _logger;

    public ShortcutAnalyzer(ILogger? logger = null)
    {
        _logger = logger ?? new SilentLogger();
    }

    public void Analyze(ProgramInfo program)
    {
        var shortcutPath = program.ShortcutPath;
        if (string.IsNullOrWhiteSpace(shortcutPath))
        {
            return;
        }

        try
        {
            switch (Path.GetExtension(shortcutPath).ToLowerInvariant())
            {
                case ".lnk":
                    AnalyzeLnk(program, shortcutPath);
                    break;
                case ".url":
                    AnalyzeUrl(program, shortcutPath);
                    break;
                case ".exe":
                    if (File.Exists(shortcutPath))
                    {
                        program.ExecutablePath = shortcutPath;
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Shortcut analysis failed: {shortcutPath}", ex);
        }
    }

    private static void AnalyzeLnk(ProgramInfo program, string shortcutPath)
    {
        var data = ShellLinkReader.Read(shortcutPath);
        var target = Environment.ExpandEnvironmentVariables(data.Target);

        program.ShortcutTarget = string.IsNullOrWhiteSpace(target) ? null : target;
        program.ShortcutArguments = string.IsNullOrWhiteSpace(data.Arguments) ? null : data.Arguments;
        program.ShortcutWorkingDirectory = string.IsNullOrWhiteSpace(data.WorkingDirectory) ? null : data.WorkingDirectory;
        program.ShortcutDescription = string.IsNullOrWhiteSpace(data.Description) ? null : data.Description;
        if (!string.IsNullOrWhiteSpace(data.IconPath))
        {
            program.ShortcutIconLocation = IconParts.Format(data.IconPath, data.IconIndex);
        }

        if (File.Exists(target) && !Directory.Exists(target))
        {
            program.ExecutablePath = target;
        }
    }

    private static void AnalyzeUrl(ProgramInfo program, string shortcutPath)
    {
        string? url = null;
        string? iconFile = null;
        var iconIndex = 0;

        foreach (var line in File.ReadLines(shortcutPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                url = trimmed[4..].Trim();
            }
            else if (trimmed.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
            {
                iconFile = trimmed[9..].Trim().Trim('"');
            }
            else if (trimmed.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(trimmed[10..].Trim(), out iconIndex);
            }
        }

        program.ShortcutTarget = url;
        if (!string.IsNullOrWhiteSpace(iconFile))
        {
            program.ShortcutIconLocation = IconParts.Format(iconFile, iconIndex);
        }
    }
}

internal static class IconParts
{
    /// <summary>Encodes "path,index" following the DisplayIcon convention.</summary>
    public static string Format(string path, int index) => index == 0 ? path : $"{path},{index}";

    /// <summary>Splits an encoded value into (path, index). Safe for paths containing commas.</summary>
    public static (string Path, int Index) Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (string.Empty, 0);
        }

        var lastComma = value.LastIndexOf(',');
        const string numberChars = "-0123456789";

        if (lastComma > 0 && lastComma < value.Length - 1 && value[(lastComma + 1)..].Trim().All(numberChars.Contains))
        {
            var path = value[..lastComma].Trim();
            var index = int.TryParse(value[(lastComma + 1)..].Trim(), out var i) ? i : 0;
            return (path, index);
        }

        return (value, 0);
    }
}