namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO уведомления сайта для админ-панели. </summary>
public sealed record AdminSiteNotificationDto(
    Guid Id,
    string Title,
    string Body,
    string TypeCode,
    bool IsTemplate,
    IReadOnlyCollection<string> Audiences,
    DateTime CreatedAt,
    string? RecipientEmail);
