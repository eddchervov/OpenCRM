using OpenCRM.Admin.App;

namespace OpenCRM.Admin.App.Tests.Unit;

public sealed class EmailPreviewDocumentTests
{
    [Fact]
    public void HtmlKeepsOriginalLayoutAndStylesBehindRestrictivePolicy()
    {
        const string html = "<html><head><style>.button{background:#f90}</style></head><body><table><tr><td>Письмо</td></tr></table></body></html>";

        var document = EmailPreviewDocument.Create(html, "Текстовая версия");

        Assert.EndsWith(html, document);
        Assert.Contains("default-src 'none'", document);
        Assert.Contains("img-src data:", document);
        Assert.Contains("form-action 'none'", document);
        Assert.True(document.IndexOf("Content-Security-Policy", StringComparison.Ordinal)
            < document.IndexOf(".button", StringComparison.Ordinal));
    }

    [Fact]
    public void PlainTextFallbackIsEncodedAndKeepsLineBreaks()
    {
        var document = EmailPreviewDocument.Create(null, "Привет\n<script>alert('test')</script>");

        Assert.Contains("Привет\n&lt;script&gt;", document);
        Assert.DoesNotContain("<script>", document);
        Assert.Contains("white-space:pre-wrap", document);
    }
}
