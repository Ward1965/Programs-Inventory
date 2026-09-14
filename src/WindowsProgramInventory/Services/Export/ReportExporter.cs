using System.IO;
using System.Text;
using WindowsProgramInventory.Models;
using WindowsProgramInventory.Services.Export.Pdf;

namespace WindowsProgramInventory.Services.Export;

/// <summary>
/// Exports the inventory to a human-readable report (Markdown table or a colored PDF
/// with embedded icon thumbnails next to each program's details).
/// </summary>
public static class ReportExporter
{
    private static readonly string[] Columns = { "Icon", "Status", "Name", "Publisher", "Version", "Type", "Arch", "Path" };

    public static string ExportMarkdown(IReadOnlyList<ProgramInfo> programs, string filePath)
    {
        var sb = new StringBuilder();
        sb.Append("# Windows Program Inventory Report\n\n");
        sb.Append("_").Append(BuildMetaLines(programs).ElementAt(0)).Append("_\n\n");

        foreach (var meta in BuildMetaLines(programs).Skip(1))
        {
            sb.Append(meta).Append('\n');
        }

        sb.Append('\n');
        sb.Append("| ").Append(string.Join(" | ", Columns)).Append(" |\n");
        sb.Append('|').Append(string.Join("|", Columns.Select(_ => "---"))).Append("|\n");
        foreach (var (cells, iconPath, _) in BuildData(programs))
        {
            var row = (string[])cells.Clone();
            row[0] = iconPath is { Length: > 0 } p ? $"![icon]({EscapeUrlPath(p)})" : string.Empty;
            sb.Append("| ").Append(string.Join(" | ", row.Select(EscapeCell))).Append(" |\n");
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return filePath;
    }

    public static string ExportPdf(IReadOnlyList<ProgramInfo> programs, string filePath)
    {
        var data = BuildData(programs).ToList();
        var rows = data.Select(d => { var cells = (string[])d.Cells.Clone(); cells[0] = string.Empty; return cells; }).ToList();
        var icons = data.Select(d => d.IconPath).ToList();
        var colors = data.Select(d => d.Color).ToList();

        var bytes = PdfBuilder.Build(
            "Windows Program Inventory Report",
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}",
            BuildMetaLines(programs).ToList(),
            Columns,
            rows,
            icons,
            colors);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(filePath, bytes);
        return filePath;
    }

    private static IEnumerable<string> BuildMetaLines(IReadOnlyList<ProgramInfo> programs)
    {
        var installed = programs.Count(p => p.IsInstalled);
        var store = programs.Count(p => p.IsStoreApp);
        var broken = programs.Count(p => p.IsBrokenShortcut);
        yield return $"Scanned {programs.Count} programs on {DateTime.Now:yyyy-MM-dd HH:mm:ss}.";
        yield return $"{installed} installed · {store} Store apps · {broken} broken shortcuts · {programs.Count - installed - store} other entries.";
    }

    private static IEnumerable<(string[] Cells, string? IconPath, float[] Color)> BuildData(IReadOnlyList<ProgramInfo> programs)
    {
        foreach (var program in programs.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var path = program.ExecutablePath ?? program.InstallLocation ?? program.ShortcutPath ?? program.ShortcutTarget ?? string.Empty;
            var cells = new[]
            {
                string.Empty,
                program.DisplayStatus,
                program.Name ?? string.Empty,
                program.Publisher ?? string.Empty,
                program.Version ?? string.Empty,
                TypeName(program.Type),
                ArchName(program.Architecture),
                path,
            };

            yield return (cells, program.IconCachePath ?? program.IconPath, StatusColor(program.Status));
        }
    }

    private static float[] StatusColor(ProgramStatus status) => status switch
    {
        ProgramStatus.Installed => new[] { 0.06f, 0.45f, 0.20f },
        ProgramStatus.InstalledAndShortcut => new[] { 0.00f, 0.35f, 0.63f },
        ProgramStatus.ShortcutOnly => new[] { 0.22f, 0.32f, 0.60f },
        ProgramStatus.BrokenShortcut => new[] { 0.82f, 0.20f, 0.20f },
        _ => new[] { 0.45f, 0.45f, 0.45f },
    };

    private static string TypeName(ProgramType type) => type switch
    {
        ProgramType.DesktopApp => "Desktop",
        ProgramType.StoreApp => "Store",
        ProgramType.PortableApp => "Portable",
        ProgramType.SystemComponent => "System",
        ProgramType.Shortcut => "Shortcut",
        _ => "Unknown",
    };

    private static string ArchName(ProgramArchitecture arch) => arch switch
    {
        ProgramArchitecture.X86 => "x86",
        ProgramArchitecture.X64 => "x64",
        ProgramArchitecture.Arm64 => "arm64",
        _ => string.Empty,
    };

    private static string EscapeCell(string value)
        => value?.Replace("|", "\\|").Replace("\r", string.Empty).Replace("\n", " ") ?? string.Empty;

    private static string EscapeUrlPath(string path)
        => path.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");
}