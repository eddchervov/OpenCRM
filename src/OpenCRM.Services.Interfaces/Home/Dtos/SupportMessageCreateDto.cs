namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Данные для создания обращения в поддержку. </summary>
public class SupportMessageCreateDto
{
    /// <summary> Код типа обращения. </summary>
    public string TypeCode { get; set; } = string.Empty;

    /// <summary> Идентификатор пользователя, отправляющего обращение. </summary>
    public string? UserId { get; set; }

    /// <summary> Идентификатор организации, от которой отправляется обращение. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Имя отправителя. </summary>
    public string? Name { get; set; }

    /// <summary> Электронная почта отправителя. </summary>
    public string? Email { get; set; }

    /// <summary> Телефон отправителя. </summary>
    public string? Phone { get; set; }

    /// <summary> Тема обращения. </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary> Текст обращения. </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary> Страница, с которой отправлено обращение. </summary>
    public string? SourcePage { get; set; }

    /// <summary> IP-адрес отправителя. </summary>
    public string ClientIp { get; set; } = "unknown";

    /// <summary> User-Agent отправителя. </summary>
    public string? UserAgent { get; set; }

    /// <summary> Файлы, отправляемые вместе с обращением. </summary>
    public IReadOnlyList<SupportAttachmentUploadDto> Attachments { get; set; } = [];
}
