using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Управление email-кампаниями. </summary>
public interface IEmailCampaignService
{
    /// <summary> Создаёт кампанию рассылки. </summary>
    Task<EmailCampaignCreateResultDto> CreateCampaignAsync(EmailCampaignCreateDto request, string? createdByUserId = null);

    /// <summary> Обрабатывает кампанию рассылки. </summary>
    Task ProcessCampaignAsync(Guid campaignId);

    /// <summary> Возвращает страницу кампаний рассылки. </summary>
    Task<EmailCampaignsPageDto> GetCampaignsAsync(int page = 1, int pageSize = 20);

    /// <summary> Устанавливает статус кампании рассылки. </summary>
    Task SetStatusAsync(Guid campaignId, string statusCode);
}
