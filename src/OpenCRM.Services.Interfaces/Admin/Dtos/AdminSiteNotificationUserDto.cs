namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO пользователя для рассылки уведомлений. </summary>
public sealed record AdminSiteNotificationUserDto(string Id, string? Email);
