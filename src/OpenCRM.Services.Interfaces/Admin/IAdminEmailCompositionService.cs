using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Подготовка и планирование email-рассылок. </summary>
public interface IAdminEmailCompositionService
{
    /// <summary> Возвращает данные для подготовки рассылки с поиском пользователей. </summary>
    Task<EmailComposeDataResponse> GetComposeDataAsync(string? userSearch, CancellationToken cancellationToken = default);

    /// <summary> Возвращает предпросмотр получателей по критериям запроса. </summary>
    Task<EmailAdminOperationResult<EmailRecipientsPreviewResponse>> PreviewRecipientsAsync(
        EmailCampaignRecipientsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary> Планирует email-рассылку по запросу. </summary>
    Task<EmailAdminOperationResult<EmailCampaignScheduleResponse>> ScheduleCampaignAsync(
        EmailCampaignScheduleRequest request,
        string? createdByUserId,
        CancellationToken cancellationToken = default);
}
