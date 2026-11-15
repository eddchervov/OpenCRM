using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Templates.OpenCRM.V1.Models;

/// <summary>Содержимое произвольного письма, подготовленного уполномоченным приложением.</summary>
public sealed class CustomEmailModel
{
    /// <summary> Тема произвольного письма. </summary>
    [Required, StringLength(300)]
    public string Subject { get; init; } = string.Empty;

    /// <summary> HTML-тело произвольного письма. </summary>
    [Required, StringLength(500000)]
    public string HtmlBody { get; init; } = string.Empty;
}
