using System.Globalization;
using System.Text;

namespace Sms.Infrastructure.Pdf;

/// <summary>
/// Minimal PDF writer for text reports (cost statement): standard Helvetica fonts (no embedding), WinAnsi text,
/// lines and simple tables with automatic page breaks. A4 portrait, coordinates in points from the top-left.
/// ponytail: no PDF library – ~100 lines cover the report; switch to PDFsharp/QuestPDF if images or Unicode beyond
/// Latin-1 are ever needed.
/// </summary>
public sealed class SimplePdf
{
    public const double PageW = 595.28, PageH = 841.89, Margin = 48;
    private readonly List<StringBuilder> _pages = [];
    private StringBuilder _c = null!;
    public double Y { get; private set; }

    public SimplePdf() => NewPage();

    public int PageCount => _pages.Count;

    public void NewPage()
    {
        _c = new StringBuilder();
        _pages.Add(_c);
        Y = Margin;
    }

    /// <summary>Writes a line of text at the current Y (or an explicit position) and advances Y.</summary>
    public void Text(string text, double size = 10, bool bold = false, double? x = null, double? y = null, bool advance = true)
    {
        var yy = y ?? Y;
        _c.Append(CultureInfo.InvariantCulture, $"BT /{(bold ? "F2" : "F1")} {size} Tf {x ?? Margin:0.##} {PageH - yy - size:0.##} Td ({Escape(text)}) Tj ET\n");
        if (advance) Y = yy + size * 1.4;
    }

    public void Space(double pt) => Y += pt;

    public void Line(double x1, double y1, double x2, double y2, double width = 0.5) =>
        _c.Append(CultureInfo.InvariantCulture, $"{width:0.##} w {x1:0.##} {PageH - y1:0.##} m {x2:0.##} {PageH - y2:0.##} l S\n");

    /// <summary>Table with column widths in points; right-aligned columns get a negative width. Breaks pages and repeats the header.</summary>
    public void Table(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows, IReadOnlyList<double> widths, double size = 9)
    {
        var rowH = size * 1.5;
        void Header()
        {
            var x = Margin;
            for (var i = 0; i < header.Count; i++) { Cell(header[i], x, widths[i], size, true); x += Math.Abs(widths[i]); }
            Y += rowH; Line(Margin, Y - size * 0.35, Margin + widths.Sum(Math.Abs), Y - size * 0.35);
        }
        Header();
        foreach (var r in rows)
        {
            if (Y + rowH > PageH - Margin) { NewPage(); Header(); }
            var x = Margin;
            for (var i = 0; i < r.Count && i < widths.Count; i++) { Cell(r[i], x, widths[i], size, false); x += Math.Abs(widths[i]); }
            Y += rowH;
        }
        Line(Margin, Y - size * 0.35, Margin + widths.Sum(Math.Abs), Y - size * 0.35);
    }

    private void Cell(string text, double x, double width, double size, bool bold)
    {
        var w = Math.Abs(width);
        var t = Fit(text, w - 4, size);
        var tx = width < 0 ? x + w - 2 - Width(t, size) : x + 2;
        Text(t, size, bold, tx, Y, advance: false);
    }

    /// <summary>Approximate Helvetica width (0.5 em per character) – good enough for truncation and right alignment.</summary>
    public static double Width(string s, double size) => s.Length * size * 0.52;

    private static string Fit(string s, double maxW, double size)
    {
        if (Width(s, size) <= maxW) return s;
        var n = Math.Max(1, (int)(maxW / (size * 0.52)) - 1);
        return n >= s.Length ? s : s[..n] + "…";
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            var c = ch switch { '…' => (char)0x85, '€' => (char)0x80, '–' => (char)0x96, '→' => '>', _ => ch }; // WinAnsi code points
            if (c > 255) c = '?';
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (c < 32 || c > 126) sb.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0'));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Serialises the document (PDF 1.4, uncompressed streams).</summary>
    public byte[] Save()
    {
        var objects = new List<byte[]>();
        byte[] Obj(string body) { objects.Add(Encoding.Latin1.GetBytes(body)); return objects[^1]; }
        var n = _pages.Count;
        // 1 catalog, 2 pages, 3 font regular, 4 font bold, then per page: page object + content stream
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj($"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, n).Select(i => $"{5 + i * 2} 0 R"))}] /Count {n} >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < n; i++)
        {
            var content = Encoding.Latin1.GetBytes(_pages[i].ToString());
            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageW.ToString("0.##", CultureInfo.InvariantCulture)} {PageH.ToString("0.##", CultureInfo.InvariantCulture)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {6 + i * 2} 0 R >>");
            objects.Add(Encoding.Latin1.GetBytes($"<< /Length {content.Length} >>\nstream\n").Concat(content).Concat("\nendstream"u8.ToArray()).ToArray());
        }
        using var ms = new MemoryStream();
        void W(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        W("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            W($"{i + 1} 0 obj\n"); ms.Write(objects[i]); W("\nendobj\n");
        }
        var xref = ms.Position;
        W($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:0000000000} 00000 n \n");
        W($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
