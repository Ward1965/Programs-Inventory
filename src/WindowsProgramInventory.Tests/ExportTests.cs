using System.IO;
using System.Text;
using WindowsProgramInventory.Models;
using WindowsProgramInventory.Services.Export;

namespace WindowsProgramInventory.Tests;

public class ExportTests
{
    private static IReadOnlyList<ProgramInfo> Sample() => new List<ProgramInfo>
    {
        new()
        {
            Name = "Alpha|Tool",
            Publisher = "Corp",
            Version = "1.0",
            Status = ProgramStatus.Installed,
            Type = ProgramType.DesktopApp,
            Architecture = ProgramArchitecture.X64,
            ExecutablePath = @"C:\Apps\alpha.exe",
        },
        new()
        {
            Name = "Beta",
            Status = ProgramStatus.BrokenShortcut,
            Type = ProgramType.Shortcut,
            ShortcutPath = @"C:\Users\X\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\beta.lnk",
        },
    };

    private static string TempPath(string extension)
        => Path.Combine(Path.GetTempPath(), $"wpi-{Guid.NewGuid():N}.{extension}");

    [Fact]
    public void ExportMarkdown_WritesTableAndEscapesCells()
    {
        var path = TempPath("md");
        try
        {
            var written = ReportExporter.ExportMarkdown(Sample(), path);

            Assert.Equal(path, written);
            var text = File.ReadAllText(path, Encoding.UTF8);
            Assert.StartsWith("# Windows Program Inventory Report", text);
            Assert.Contains("| Icon |", text);
            Assert.Contains("| Status |", text);
            Assert.Contains("Alpha\\|Tool", text);
            Assert.Contains("Broken Shortcut", text);
            Assert.Contains("x64", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExportMarkdown_EmbedsIconLinks()
    {
        var iconPath = Path.Combine(Path.GetTempPath(), $"wpi-icon-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(iconPath, new byte[] { 137, 80, 78, 71, 0, 0, 0, 0 }); // minimal stub bytes

        var programs = Sample();
        programs[0].IconCachePath = iconPath;

        var path = TempPath("md");
        try
        {
            ReportExporter.ExportMarkdown(programs, path);
            var text = File.ReadAllText(path, Encoding.UTF8);

            Assert.Contains("![icon](", text);
            Assert.Contains(iconPath.Replace(" ", "%20"), text);
        }
        finally
        {
            File.Delete(path);
            File.Delete(iconPath);
        }
    }

    [Fact]
    public void ExportPdf_WritesValidPdfWithPages()
    {
        var path = TempPath("pdf");
        try
        {
            var written = ReportExporter.ExportPdf(Sample(), path);

            Assert.Equal(path, written);
            var bytes = File.ReadAllBytes(path);
            var ascii = Encoding.ASCII.GetString(bytes);

            Assert.StartsWith("%PDF-1.4", ascii);
            Assert.EndsWith("%%EOF", ascii);
            Assert.Contains("/Type /Page", ascii);
            Assert.Contains("/Type /Pages", ascii);
            Assert.Contains("/Helvetica", ascii);
            Assert.Contains("/WinAnsiEncoding", ascii);
            Assert.Contains("0.13 0.31 0.68 rg", ascii); // colored header band
            Assert.Contains("0.06 0.45 0.2 rg", ascii);   // green "Installed" status text
            Assert.Contains("0.82 0.2 0.2 rg", ascii);    // red "Broken Shortcut" status text
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExportPdf_ProducesMultiplePagesForLargeInventory()
    {
        var programs = Enumerable.Range(0, 400)
            .Select(i => new ProgramInfo
            {
                Name = $"Program {i} - A somewhat long name to fill the table rows nicely",
                Publisher = $"Publisher Number {i}",
                Version = $"{i}.{i}.0",
                Status = ProgramStatus.Installed,
                Type = ProgramType.DesktopApp,
                ExecutablePath = $@"C:\Program Files\Vendor{i}\app-very-long-name{i}.exe",
            })
            .ToList();

        var path = TempPath("pdf");
        try
        {
            ReportExporter.ExportPdf(programs, path);
            var ascii = Encoding.ASCII.GetString(File.ReadAllBytes(path));
            var pageCount = ascii.Split("/MediaBox").Length - 1;
            Assert.True(pageCount >= 2, $"Expected multiple pages, got {pageCount}.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExportPdf_EmbedsIconImages()
    {
        var iconCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsProgramInventory", "IconCache");
        var icons = Directory.Exists(iconCache)
            ? Directory.GetFiles(iconCache, "*.png").Take(3).ToArray()
            : Array.Empty<string>();
        if (icons.Length == 0)
        {
            return; // no icon cache available on this machine
        }

        var programs = icons.Select((icon, i) => new ProgramInfo
        {
            Name = $"Cached App {i}",
            Status = ProgramStatus.Installed,
            Type = ProgramType.DesktopApp,
            ExecutablePath = $@"C:\Apps\app{i}.exe",
            IconCachePath = icon,
        }).ToList();

        var path = TempPath("pdf");
        try
        {
            ReportExporter.ExportPdf(programs, path);
            var ascii = Encoding.ASCII.GetString(File.ReadAllBytes(path));

            Assert.Contains("/Subtype /Image", ascii);
            Assert.Contains("/FlateDecode", ascii);
            Assert.Contains("/Im0 Do", ascii);
        }
        finally
        {
            File.Delete(path);
        }
    }
}