using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using System.Security.Claims;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API администрирования почтовых сообщений, кампаний и журнала событий. </summary>
[Route("api/v1/admin/email-messages")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ViewCommunications)]
public sealed class EmailMessagesApiController(
    IEmailCampaignService emailCampaignService,
    IAdminEmailCompositionService emailCompositionService,
    IAdminEmailTestOperationsService emailTestOperationsService,
    IAdminEmailJournalService emailJournalService) : ControllerBase
{
    /// <summary> Возвращает страницу почтовых сообщений с фильтрацией. </summary>
    [HttpGet]
    public Task<EmailMessagesPageResponse> Get(
        string? search = null,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return emailJournalService.GetPageAsync(search, status, page, pageSize, cancellationToken);
    }

    /// <summary> Возвращает сообщение электронной почты по идентификатору. </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<EmailMessageDetails>> GetById(
        int id,
        CancellationToken cancellationToken = default)
    {
        var message = await emailJournalService.GetByIdAsync(id, cancellationToken);
        if (message == null)
        {
            return this.NotFound();
        }

        return message;
    }

    /// <summary> Возвращает события по сообщению электронной почты. </summary>
    [HttpGet("{id:int}/events")]
    public Task<EmailMessageEventsResponse> GetEvents(int id, CancellationToken cancellationToken = default)
    {
        return emailJournalService.GetEventsAsync(id, cancellationToken);
    }

    /// <summary> Возвращает страницу событий вебхуков с фильтрацией. </summary>
    [HttpGet("webhook-events")]
    public Task<EmailWebhookEventsPageResponse> GetWebhookEvents(
        string? search = null,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return emailJournalService.GetWebhookEventsAsync(search, status, page, pageSize, cancellationToken);
    }

    /// <summary> Возвращает сводку по почтовым сообщениям. </summary>
    [HttpGet("dashboard")]
    public Task<EmailDashboardResponse> GetDashboard(CancellationToken cancellationToken = default)
    {
        return emailJournalService.GetDashboardAsync(cancellationToken);
    }

    /// <summary> Отправляет тестовое письмо. </summary>
    [HttpPost("test-email")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<IActionResult> SendTestEmail(EmailTestSendRequest request)
    {
        var result = await emailTestOperationsService.SendTestEmailAsync(request);
        if (!result.Succeeded)
        {
            return this.BadRequest(new { message = result.Error });
        }

        return this.Ok(new { message = result.Value });
    }

    /// <summary> Создаёт тестовое событие вебхука. </summary>
    [HttpPost("webhook-test")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<ActionResult<EmailMessageEventItem>> CreateWebhookTest(
        EmailWebhookTestRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await emailTestOperationsService.CreateWebhookTestAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return this.BadRequest(new { message = result.Error });
        }

        return result.Value!;
    }

    /// <summary> Возвращает данные для формирования рассылки, включая поиск пользователей. </summary>
    [HttpGet("compose-data")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<ActionResult<EmailComposeDataResponse>> GetComposeData(
        string? userSearch = null,
        CancellationToken cancellationToken = default)
    {
        return await emailCompositionService.GetComposeDataAsync(userSearch, cancellationToken);
    }

    /// <summary> Возвращает предпросмотр получателей рассылки. </summary>
    [HttpPost("preview-recipients")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<ActionResult<EmailRecipientsPreviewResponse>> PreviewRecipients(
        EmailCampaignRecipientsRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await emailCompositionService.PreviewRecipientsAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return this.BadRequest(new { message = result.Error });
        }

        return result.Value!;
    }

    /// <summary> Планирует почтовую кампанию. </summary>
    [HttpPost("schedule")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<ActionResult<EmailCampaignScheduleResponse>> ScheduleCampaign(
        EmailCampaignScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await emailCompositionService.ScheduleCampaignAsync(
            request,
            this.User.FindFirstValue(ClaimTypes.NameIdentifier),
            cancellationToken);
        if (!result.Succeeded)
        {
            return this.BadRequest(new { message = result.Error });
        }

        return result.Value!;
    }

    /// <summary> Возвращает страницу почтовых кампаний. </summary>
    [HttpGet("campaigns")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public Task<EmailCampaignsPageDto> GetCampaigns(int page = 1, int pageSize = 20)
    {
        return emailCampaignService.GetCampaignsAsync(page, pageSize);
    }

    /// <summary> Приостанавливает почтовую кампанию. </summary>
    [HttpPost("campaigns/{campaignId:guid}/pause")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<IActionResult> PauseCampaign(Guid campaignId)
    {
        await emailCampaignService.SetStatusAsync(campaignId, EmailCampaignStatusCodes.Paused);
        return NoContent();
    }

    /// <summary> Возобновляет приостановленную почтовую кампанию. </summary>
    [HttpPost("campaigns/{campaignId:guid}/resume")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<IActionResult> ResumeCampaign(Guid campaignId)
    {
        await emailCampaignService.SetStatusAsync(campaignId, EmailCampaignStatusCodes.Running);
        return NoContent();
    }

    /// <summary> Отменяет почтовую кампанию. </summary>
    [HttpPost("campaigns/{campaignId:guid}/cancel")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<IActionResult> CancelCampaign(Guid campaignId)
    {
        await emailCampaignService.SetStatusAsync(campaignId, EmailCampaignStatusCodes.Canceled);
        return NoContent();
    }

}
