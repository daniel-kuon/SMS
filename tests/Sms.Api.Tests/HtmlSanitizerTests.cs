using Sms.Infrastructure;

namespace Sms.Api.Tests;

public class HtmlSanitizerTests
{
    [Fact]
    public void Keeps_editor_markup_and_strips_scripts()
    {
        var html = new HtmlSanitizerService();
        const string editor = "<h2>T</h2><p><strong>b</strong><em>i</em><u>u</u><s>s</s><a href=\"/harbours/1\">h</a><a target=\"_blank\" rel=\"noopener noreferrer nofollow\" href=\"https://x.org\">x</a></p>"
            + "<figure><img src=\"/api/media/1/preview\" alt=\"a\" width=\"300\"><figcaption>c</figcaption></figure><ul><li>1</li></ul><ol><li>2</li></ol><blockquote><p>q</p></blockquote><pre><code>c</code></pre><hr>";
        Assert.Equal(editor, html.Sanitize(editor));
        Assert.Equal("<p>x</p>", html.Sanitize("<p onclick=\"x()\">x<script>alert(1)</script></p>"));
        Assert.DoesNotContain("javascript", html.Sanitize("<a href=\"javascript:alert(1)\">x</a>"));
    }
}
