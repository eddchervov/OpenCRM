using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary>Чтение журнала писем, событий доставки и сводных показателей.</summary>
public interface IAdminEmailJournalService
{
    /// <summary> Возвращает страницу журнала писем с фильтрами. </summary>
    Task<EmailMessagesPageResponse> GetPageAsync(
        string? search = null,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary> Возвращает письмо журнала по идентификатору. </summary>
    Task<EmailMessageDetails?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary> Возвращает события письма по идентификатору. </summary>
    Task<EmailMessageEventsResponse> GetEventsAsync(int id, CancellationToken cancellationToken);

    /// <summary> Возвращает страницу webhook-событий с фильтрами. </summary>
    Task<EmailWebhookEventsPageResponse> GetWebhookEventsAsync(
        string? search = null,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary> Возвращает сводные показатели по письмам. </summary>
    Task<EmailDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken);
}
