using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsProgramInventory.Services.Export.Pdf;

/// <summary>
/// Minimal, dependency-free PDF writer (A4 portrait, built-in Helvetica base fonts).
/// Renders a title block, meta lines and a data table with repeating headers, page footers,
/// colored status cells and embedded icon thumbnails (FlateDecode RGB, hex-encoded streams).
/// Non-WinAnsi characters are replaced so the document always stays valid.
/// </summary>
public static class PdfBuilder
{
    public const float PageWidth = 595.276f;   // A4 portrait
    public const float PageHeight = 841.89f;

    private const float MarginLeft = 36f;

    private const int StatusColumnIndex = 1;
    private const float MarginRight = 36f;
    private const float MarginTop = 54f;
    private const float MarginBottom = 46f;
    private const float HeaderRowHeight = 18f;
    private const float RowHeight = 15f;
    private const float CellPadding = 4f;

    private const int FontNormal = 1;
    private const int FontBold = 2;

    private const int IconThumbnail = 24;
    private const float IconDrawSize = 18f;

    public static byte[] Build(
        string title,
        string subtitle,
        IReadOnlyList<string> metaLines,
        IReadOnlyList<string> columns,
        IReadOnlyList<string[]> rows,
        IReadOnlyList<string?>? iconPaths = null,
        IReadOnlyList<float[]>? rowColors = null)
    {
        var (iconRefs, iconPdfs) = LoadIcons(iconPaths, rows.Count);
        var columnWidths = ComputeColumnWidths(columns, rows);
        var usableWidth = PageWidth - MarginLeft - MarginRight;
        var pages = new List<List<RenderOp>>();
        var current = NewPage(pages);
        var y = MarginTop;

        // ── Title block ─────────────────────────────────────────
        AddText(current, MarginLeft, y, title, 18f, FontBold, 0f, 0f, 0f);
        y += 24f;

        if (!string.IsNullOrEmpty(subtitle))
        {
            AddText(current, MarginLeft, y, subtitle, 10f, FontNormal, 0.25f, 0.25f, 0.25f);
            y += 17f;
        }

        foreach (var line in metaLines)
        {
            if (y > PageHeight - MarginBottom - 16f)
            {
                current = NewPage(pages);
                y = MarginTop;
            }

            AddText(current, MarginLeft, y, line, 9f, FontNormal, 0.19f, 0.19f, 0.19f);
            y += 13f;
        }

        y += 8f;

        // ── Table with a header repeated on every page ──────────
        var rowIndex = 0;
        while (rowIndex < rows.Count)
        {
            if (y + HeaderRowHeight + RowHeight > PageHeight - MarginBottom)
            {
                current = NewPage(pages);
                y = MarginTop;
            }

            DrawHeader(current, MarginLeft, y, usableWidth, HeaderRowHeight, columns, columnWidths);
            y += HeaderRowHeight;

            while (rowIndex < rows.Count)
            {
                if (y + RowHeight > PageHeight - MarginBottom)
                {
                    break;
                }

                if ((rowIndex & 1) == 1)
                {
                    AddRect(current, MarginLeft, y, usableWidth, RowHeight, 0.94f, 0.94f, 0.94f);
                }

                DrawRow(current, MarginLeft, y, RowHeight, rows[rowIndex], columnWidths, white: false, rowColors?[rowIndex]);
                if (iconRefs[rowIndex] >= 0)
                {
                    AddImage(current, MarginLeft + (columnWidths[0] - IconDrawSize) / 2f, y + (RowHeight - IconDrawSize) / 2f, IconDrawSize, IconDrawSize, iconRefs[rowIndex]);
                }

                y += RowHeight;
                rowIndex++;
            }
        }

        // ── Footers (page count is known only now) ──────────────
        for (var i = 0; i < pages.Count; i++)
        {
            var footer = pages[i];
            var footerLeft = string.IsNullOrEmpty(subtitle) ? title : $"{subtitle} — {title}";
            AddText(footer, MarginLeft, MarginBottom - 14f, footerLeft, 8f, FontNormal, 0.55f, 0.55f, 0.55f);

            var pageLabel = $"Page {i + 1} of {pages.Count}";
            AddText(footer, PageWidth - MarginRight - Measure(pageLabel, 8f), MarginBottom - 14f, pageLabel, 8f, FontNormal, 0.55f, 0.55f, 0.55f);
        }

        return WriteDocument(pages, iconPdfs);
    }

    private static List<RenderOp> NewPage(List<List<RenderOp>> pages)
    {
        var page = new List<RenderOp>();
        pages.Add(page);
        return page;
    }

    private static void AddText(List<RenderOp> ops, float x, float y, string text, float size, int font, float r, float g, float b)
        => ops.Add(new RenderOp(RenderKind.Text, x, y, text, size, font, r, g, b, 0f, 0f, 0));

    private static void AddRect(List<RenderOp> ops, float x, float y, float width, float height, float r, float g, float b)
        => ops.Add(new RenderOp(RenderKind.Rect, x, y, string.Empty, 0f, 0, r, g, b, width, height, 0));

    private static void AddImage(List<RenderOp> ops, float x, float y, float width, float height, int imageId)
        => ops.Add(new RenderOp(RenderKind.Image, x, y, string.Empty, 0f, 0, 0f, 0f, 0f, width, height, imageId));

    private static void DrawHeader(
        List<RenderOp> ops, float x, float y, float width, float height,
        IReadOnlyList<string> headers, IReadOnlyList<float> widths)
    {
        AddRect(ops, x, y, width, height, 0.13f, 0.31f, 0.68f);
        DrawRow(ops, x, y, height, headers, widths, white: true, null);
    }

    private static void DrawRow(
        List<RenderOp> ops,
        float x, float y, float rowHeight,
        IReadOnlyList<string> cells,
        IReadOnlyList<float> widths,
        bool white,
        float[]? firstCellColor)
    {
        var cellX = x + CellPadding;
        var baseline = y + rowHeight - 4.5f;
        for (var c = 0; c < cells.Count; c++)
        {
            var text = ClipToFit(cells[c], widths[c] - CellPadding - 2f);
            if (text.Length > 0)
            {
                if (white)
                {
                    AddText(ops, cellX, baseline, text, 8f, FontBold, 1f, 1f, 1f);
                }
                else if (c == StatusColumnIndex && firstCellColor is not null)
                {
                    AddText(ops, cellX, baseline, text, 8f, FontBold, firstCellColor[0], firstCellColor[1], firstCellColor[2]);
                }
                else
                {
                    AddText(ops, cellX, baseline, text, 8f, FontNormal, 0.08f, 0.08f, 0.08f);
                }
            }

            cellX += widths[c];
        }
    }

    private static List<float> ComputeColumnWidths(IReadOnlyList<string> columns, IReadOnlyList<string[]> rows)
    {
        var usable = PageWidth - MarginLeft - MarginRight;
        var count = columns.Count;
        var widths = new List<float>(count);

        if (count <= 1)
        {
            widths.Add(usable);
            return widths;
        }

        var preferred = new float[count];
        for (var c = 0; c < count; c++)
        {
            preferred[c] = Math.Min(usable, Measure(columns[c], 8f) + 14f);
        }

        foreach (var row in rows)
        {
            for (var c = 0; c < Math.Min(count, row.Length); c++)
            {
                preferred[c] = Math.Min(usable, Math.Max(preferred[c], Measure(row[c], 8f) + 14f));
            }
        }

        const float fixedThreshold = 90f;
        var fixedTotal = 0f;
        var flexibleColumns = 0;
        for (var c = 0; c < count; c++)
        {
            if (preferred[c] <= fixedThreshold)
            {
                fixedTotal += preferred[c];
            }
            else
            {
                flexibleColumns++;
            }
        }

        var remaining = Math.Max(0f, usable - fixedTotal);
        for (var c = 0; c < count; c++)
        {
            widths.Add(preferred[c] <= fixedThreshold
                ? preferred[c]
                : Math.Max(fixedThreshold, remaining / Math.Max(1, flexibleColumns)));
        }

        return widths;
    }

    private static string ClipToFit(string value, float maxWidth)
    {
        var text = value ?? string.Empty;
        if (text.Length == 0 || Measure(text, 8f) <= maxWidth)
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (Measure(result.ToString() + ch, 8f) <= maxWidth)
            {
                result.Append(ch);
            }
            else
            {
                result.Append('\u2026');
                break;
            }
        }

        return result.ToString();
    }

    /// <summary>Approximate Helvetica advance width (points) – used for column sizing and clipping.</summary>
    internal static float Measure(string text, float fontSize)
    {
        var width = 0f;
        foreach (var ch in text)
        {
            width += char.IsWhiteSpace(ch) ? 0.30f : 0.55f;
        }

        return width * fontSize;
    }

    // ── Icon thumbnails ────────────────────────────────────────

    private sealed record PdfIcon(byte[] Pixels, int Width, int Height);

    /// <summary>Resolves the icon for each row (deduplicated); -1 means "no icon".</summary>
    private static (int[] Refs, List<PdfIcon> Icons) LoadIcons(IReadOnlyList<string?>? iconPaths, int rowCount)
    {
        var result = new int[rowCount];
        var icons = new List<PdfIcon>();
        if (iconPaths is null)
        {
            return (result, icons);
        }

        var byPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rowCount; i++)
        {
            result[i] = -1;
            var path = i < iconPaths.Count ? iconPaths[i] : null;
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (!byPath.TryGetValue(path, out var id))
            {
                if (TryLoadThumbnail(path, out var icon))
                {
                    id = icons.Count;
                    icons.Add(icon);
                    byPath[path] = id;
                }
            }

            result[i] = byPath.TryGetValue(path, out var existing) ? existing : -1;
        }

        return (result, icons);
    }

    private static bool TryLoadThumbnail(string path, out PdfIcon icon)
    {
        icon = null!;
        try
        {
            using var source = new Bitmap(path);
            if (source.Width <= 0 || source.Height <= 0)
            {
                return false;
            }

            using var thumb = new Bitmap(IconThumbnail, IconThumbnail);
            using (var g = Graphics.FromImage(thumb))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                var scale = Math.Min(IconThumbnail / (float)source.Width, IconThumbnail / (float)source.Height);
                var w = Math.Max(1, source.Width * scale);
                var h = Math.Max(1, source.Height * scale);
                g.DrawImage(source, (IconThumbnail - w) / 2f, (IconThumbnail - h) / 2f, w, h);
            }

            var rect = new Rectangle(0, 0, IconThumbnail, IconThumbnail);
            var data = thumb.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var buffer = new byte[data.Stride * data.Height];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

                // Row 0 of a Windows DIB is the bottom row; the PDF needs rows top-down.
                var pixels = new byte[IconThumbnail * IconThumbnail * 3];
                for (var row = 0; row < IconThumbnail; row++)
                {
                    Array.Copy(buffer, (IconThumbnail - 1 - row) * data.Stride, pixels, row * IconThumbnail * 3, IconThumbnail * 3);
                }

                icon = new PdfIcon(pixels, IconThumbnail, IconThumbnail);
                return true;
            }
            finally
            {
                thumb.UnlockBits(data);
            }
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Flate(byte[] data)
    {
        // Hand-rolled zlib wrapper (2-byte header + raw deflate + adler32),
        // using DeflateStream so no extra compression assembly surface is required.
        using var ms = new MemoryStream();
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);

        using (var deflate = new DeflateStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        var adler = Adler32(data);
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));
        ms.WriteByte((byte)adler);
        return ms.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    // ── PDF structure emission ─────────────────────────────────

    private static byte[] WriteDocument(List<List<RenderOp>> pages, List<PdfIcon> icons)
    {
        // Object plan:
        //  1 Catalog · 2 Helvetica · 3 Helvetica-Bold · 4 Pages · 5..(5+n-1) Page i
        //  (5+n)..(5+2n-1) content i · (5+2n).. images
        var contentStreams = new List<string>(pages.Count);
        var pageImageIds = new List<HashSet<int>>(pages.Count);
        foreach (var ops in pages)
        {
            contentStreams.Add(BuildContentStream(ops));
            pageImageIds.Add(new HashSet<int>(ops.Where(o => o.Kind == RenderKind.Image).Select(o => o.Ref)));
        }

        var pageObjectIds = new int[pages.Count];
        var contentObjectIds = new int[pages.Count];
        var nextId = 5;
        for (var i = 0; i < pages.Count; i++)
        {
            pageObjectIds[i] = nextId++;
        }
        for (var i = 0; i < pages.Count; i++)
        {
            contentObjectIds[i] = nextId++;
        }

        var imageBase = nextId; // first image object id

        var bodies = new List<string>();
        bodies.Add("<< /Type /Catalog /Pages 4 0 R >>"); // 1
        bodies.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"); // 2
        bodies.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"); // 3

        var kids = string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"));
        bodies.Add($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>"); // 4

        for (var i = 0; i < pages.Count; i++)
        {
            var resources = "/Font << /F1 2 0 R /F2 3 0 R >>";
            if (pageImageIds[i].Count > 0)
            {
                var xobjects = string.Join(" ", pageImageIds[i].Select(id => $"/Im{id} {imageBase + id} 0 R"));
                resources += $" /XObject << {xobjects} >>";
            }

            bodies.Add(
                $"<< /Type /Page /Parent 4 0 R " +
                $"/MediaBox [0 0 {F(PageWidth)} {F(PageHeight)}] " +
                $"/Resources << {resources} >> " +
                $"/Contents {contentObjectIds[i]} 0 R >>");
        }

        foreach (var content in contentStreams)
        {
            bodies.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        for (var id = 0; id < icons.Count; id++)
        {
            var compressed = Flate(icons[id].Pixels);
            bodies.Add(
                $"<< /Type /XObject /Subtype /Image " +
                $"/Width {icons[id].Width} /Height {icons[id].Height} " +
                $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode " +
                $"/Length {compressed.Length} >>\nstream\n{Convert.ToHexString(compressed)}\nendstream");
        }

        // Header + objects + xref + trailer
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");

        var offsets = new List<long>();
        for (var i = 0; i < bodies.Count; i++)
        {
            var before = Encoding.ASCII.GetByteCount(sb.ToString());
            offsets.Add(before);
            sb.Append($"{i + 1} 0 obj\n{bodies[i]}\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append("xref\n");
        sb.Append($"0 {bodies.Count + 1}\n");
        sb.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            sb.Append($"{offset:0000000000} 00000 n \n");
        }

        sb.Append("trailer\n");
        sb.Append($"<< /Size {bodies.Count + 1} /Root 1 0 R >>\n");
        sb.Append("startxref\n");
        sb.Append($"{xrefOffset}\n%%EOF");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string BuildContentStream(List<RenderOp> ops)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            if (op.Kind == RenderKind.Rect)
            {
                sb.Append(F(op.X)).Append(' ').Append(F(op.Y)).Append(' ')
                  .Append(F(op.Width)).Append(' ').Append(F(op.Height)).Append(" re\n");
                sb.Append(F(op.R)).Append(' ').Append(F(op.G)).Append(' ').Append(F(op.B)).Append(" rg\nf\n");
            }
            else if (op.Kind == RenderKind.Image)
            {
                sb.Append("q\n")
                  .Append(F(op.Width)).Append(" 0 0 ").Append(F(op.Height))
                  .Append(' ').Append(F(op.X)).Append(' ').Append(F(op.Y)).Append(" cm\n")
                  .Append("/Im").Append(op.Ref).Append(" Do\nQ\n");
            }
            else
            {
                sb.Append("BT\n")
                  .Append(op.Font == FontBold ? "/F2" : "/F1").Append(' ').Append(F(op.Size)).Append(" Tf\n")
                  .Append(F(op.R)).Append(' ').Append(F(op.G)).Append(' ').Append(F(op.B)).Append(" rg\n")
                  .Append(F(op.X)).Append(' ').Append(F(op.Y)).Append(" Td\n")
                  .Append('(').Append(Escape(op.Text)).Append(") Tj\n")
                  .Append("ET\n");
            }
        }

        return sb.ToString();
    }

    private static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == '(')
            {
                sb.Append("\\(");
            }
            else if (ch == ')')
            {
                sb.Append("\\)");
            }
            else if (ch == '\\')
            {
                sb.Append("\\\\");
            }
            else if (ch < 32 || ch > 255)
            {
                sb.Append('?');
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private enum RenderKind
    {
        Text,
        Rect,
        Image,
    }

    private readonly record struct RenderOp(
        RenderKind Kind,
        float X,
        float Y,
        string Text,
        float Size,
        int Font,
        float R,
        float G,
        float B,
        float Width,
        float Height,
        int Ref);
}