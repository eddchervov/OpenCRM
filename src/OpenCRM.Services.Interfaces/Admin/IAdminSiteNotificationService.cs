using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary>Управление уведомлениями сайта и их шаблонами.</summary>
public interface IAdminSiteNotificationService
{
    /// <summary> Возвращает список уведомлений сайта. </summary>
    Task<IReadOnlyList<AdminSiteNotificationDto>> GetAsync(CancellationToken cancellationToken);

    /// <summary> Возвращает отдельную страницу истории уведомлений. </summary>
    Task<AdminSiteNotificationHistoryPageDto> GetHistoryPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary> Возвращает шаблоны уведомлений без загрузки истории. </summary>
    Task<IReadOnlyList<AdminSiteNotificationDto>> GetTemplatesAsync(CancellationToken cancellationToken);

    /// <summary> Ищет пользователей для отправки уведомлений сайта. </summary>
    Task<IReadOnlyList<AdminSiteNotificationUserDto>> SearchUsersAsync(
        string? search,
        CancellationToken cancellationToken);

    /// <summary> Пытается удалить уведомление сайта по идентификатору. </summary>
    Task<bool> TryDeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary> Обновляет заголовок и текст шаблона уведомления сайта. </summary>
    Task<AdminSiteNotificationDto?> UpdateTemplateAsync(
        Guid id,
        string title,
        string body,
        CancellationToken cancellationToken);

    /// <summary>Создаёт уведомления и возвращает сообщение об ошибке либо null при успехе.</summary>
    Task<string?> SendAsync(AdminSiteNotificationSendDto dto, CancellationToken cancellationToken);
}
