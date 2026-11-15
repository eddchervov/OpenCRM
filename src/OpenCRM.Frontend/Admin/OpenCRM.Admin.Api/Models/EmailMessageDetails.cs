namespace OpenCRM.Admin.Api.Models;

/// <summary> Детальная информация о письме. </summary>
public sealed class EmailMessageDetails : EmailMessageListItem
{
    /// <summary> HTML-версия текста письма. </summary>
    public string HtmlBody { get; init; } = string.Empty;

    /// <summary> Текстовая версия письма. </summary>
    public string TextBody { get; init; } = string.Empty;
}
