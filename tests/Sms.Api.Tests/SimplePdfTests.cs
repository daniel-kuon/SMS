using System.Text;
using Sms.Infrastructure.Pdf;

namespace Sms.Api.Tests;

public class SimplePdfTests
{
    private static string Latin(byte[] b) => Encoding.Latin1.GetString(b);

    [Fact]
    public void Writes_a_valid_single_page_document_with_escaped_text()
    {
        var pdf = new SimplePdf();
        pdf.Text("Bordkasse (Test) \\ Törn – 12,50 €", 12, bold: true);
        var s = Latin(pdf.Save());
        Assert.StartsWith("%PDF-1.4", s);
        Assert.Contains("/Count 1", s);
        Assert.Contains("/BaseFont /Helvetica-Bold", s);
        Assert.Contains(@"\(Test\) \\ T\366rn \226 12,50 \200", s); // WinAnsi octal escapes for ö, en dash, euro
        Assert.Contains("startxref", s);
        Assert.EndsWith("%%EOF\n", s);
        // xref offsets point at "n 0 obj"
        var xref = s.IndexOf("xref\n", StringComparison.Ordinal);
        var lines = s[(xref + 5)..].Split('\n').Skip(2).Take(6).ToList();
        for (var i = 0; i < 6; i++) Assert.StartsWith($"{i + 1} 0 obj", s[int.Parse(lines[i][..10])..]);
    }

    [Fact]
    public void Tables_break_pages_and_repeat_the_header()
    {
        var pdf = new SimplePdf();
        pdf.Text("Title", 14, bold: true);
        pdf.Table(["A", "B"], Enumerable.Range(1, 120).Select(i => (IReadOnlyList<string>)[$"row {i}", (i * 1.5).ToString("0.00")]), [200, -80]);
        Assert.True(pdf.PageCount >= 2, $"pages: {pdf.PageCount}");
        var s = Latin(pdf.Save());
        Assert.Equal(pdf.PageCount, s.Split("/Type /Page ").Length - 1);
        Assert.True(s.Split("(A) Tj").Length - 1 >= 2, "header repeated on the next page");
        Assert.Contains("(row 120) Tj", s);
    }

    [Fact]
    public void Long_cells_are_truncated_and_non_latin_characters_replaced()
    {
        var pdf = new SimplePdf();
        pdf.Table(["X"], [(IReadOnlyList<string>)[new string('a', 200)], (IReadOnlyList<string>)["日本"]], [60]);
        var s = Latin(pdf.Save());
        Assert.DoesNotContain(new string('a', 30), s);
        Assert.Contains(@"\205) Tj", s); // ellipsis
        Assert.Contains("(??) Tj", s);
    }
}
