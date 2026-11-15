namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO сообщения поддержки в админ-панели. </summary>
public class AdminSupportMessageDto
{
    /// <summary> Идентификатор сообщения. </summary>
    public int Id { get; set; }

    /// <summary> Код типа сообщения. </summary>
    public string TypeCode { get; set; } = string.Empty;

    /// <summary> Название типа сообщения. </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary> Код статуса сообщения. </summary>
    public string StatusCode { get; set; } = string.Empty;

    /// <summary> Название статуса сообщения. </summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary> Идентификатор пользователя. </summary>
    public string? UserId { get; set; }

    /// <summary> Email пользователя. </summary>
    public string? UserEmail { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Название организации. </summary>
    public string? OrganizationName { get; set; }

    /// <summary> Имя отправителя. </summary>
    public string? Name { get; set; }

    /// <summary> Email отправителя. </summary>
    public string? Email { get; set; }

    /// <summary> Телефон отправителя. </summary>
    public string? Phone { get; set; }

    /// <summary> Тема сообщения. </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary> Текст сообщения. </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary> Страница, с которой отправлено сообщение. </summary>
    public string? SourcePage { get; set; }

    /// <summary> Комментарий администратора. </summary>
    public string? AdminComment { get; set; }

    /// <summary> Дата создания сообщения. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Дата обновления сообщения. </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary> Список записей переписки по сообщению. </summary>
    public IReadOnlyList<AdminSupportMessageEntryDto> Entries { get; set; } = [];
}
