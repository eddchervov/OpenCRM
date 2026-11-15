using System.Net;

namespace OpenCRM.Admin.App;

/// <summary> Готовит письмо для просмотра без выполнения кода и внешних запросов. </summary>
public static class EmailPreviewDocument
{
    public static string Create(string? html, string? text)
    {
        var body = string.IsNullOrWhiteSpace(html)
            ? $"<pre style=\"white-space:pre-wrap;overflow-wrap:anywhere;font:14px/1.5 'Segoe UI',sans-serif\">{WebUtility.HtmlEncode(text)}</pre>"
            : html;

        // Политика идёт перед содержимым письма и не позволяет загрузить пиксели отслеживания.
        return """
            <!doctype html>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:; frame-src 'none'; form-action 'none'; base-uri 'none'">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <style>html{background:#fff;color:#171717;color-scheme:light}body{margin:0;padding:16px;font-family:'Segoe UI',sans-serif}img{max-width:100%}</style>
            """ + body;
    }
}
