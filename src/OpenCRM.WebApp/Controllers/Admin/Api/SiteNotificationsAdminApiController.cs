using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API управления шаблонами и рассылками внутрисайтовых уведомлений. </summary>
[ApiController]
[Route("api/v1/admin/site-notifications")]
[Authorize(Policy = AdminAuthorizationPolicies.ViewCommunications)]
public sealed class SiteNotificationsAdminApiController(
    IAdminSiteNotificationService siteNotificationService) : ControllerBase
{
    /// <summary> Возвращает список внутрисайтовых уведомлений. </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var notifications = await siteNotificationService.GetAsync(cancellationToken);

        return this.Ok(notifications);
    }

    /// <summary> Возвращает страницу истории уведомлений. </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var notifications = await siteNotificationService.GetHistoryPageAsync(page, pageSize, cancellationToken);

        return this.Ok(notifications);
    }

    /// <summary> Возвращает шаблоны уведомлений без истории. </summary>
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates(CancellationToken cancellationToken)
    {
        var templates = await siteNotificationService.GetTemplatesAsync(cancellationToken);

        return this.Ok(templates);
    }

    /// <summary> Ищет пользователей для отправки уведомлений. </summary>
    [HttpGet("users")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<IActionResult> GetUsers(string? search, CancellationToken cancellationToken)
    {
        var users = await siteNotificationService.SearchUsersAsync(search, cancellationToken);

        return this.Ok(users);
    }

    /// <summary> Удаляет внутрисайтовое уведомление по идентификатору. </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var isDeleted = await siteNotificationService.TryDeleteAsync(id, cancellationToken);
        if (!isDeleted)
        {
            return this.NotFound();
        }

        return this.NoContent();
    }

    /// <summary> Обновляет шаблон внутрисайтового уведомления. </summary>
    [HttpPut("templates/{id:guid}")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<IActionResult> UpdateTemplate(
        Guid id,
        [FromBody] UpdateSiteNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        var body = request.Body?.Trim();
        if (!SiteNotificationsAdminApiController.IsValidContent(title, body))
        {
            return this.BadRequest(new { message = "Укажите заголовок до 200 символов и текст до 4000 символов." });
        }

        var template = await siteNotificationService.UpdateTemplateAsync(id, title!, body!, cancellationToken);
        if (template == null)
        {
            return this.NotFound();
        }

        return this.Ok(template);
    }

    /// <summary> Отправляет внутрисайтовое уведомление получателям. </summary>
    [HttpPost("send")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageCommunications)]
    public async Task<IActionResult> Send(
        [FromBody] SendSiteNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        var body = request.Body?.Trim();
        if (!SiteNotificationsAdminApiController.IsValidContent(title, body))
        {
            return this.BadRequest(new { message = "Укажите заголовок до 200 символов и текст до 4000 символов." });
        }

        var notification = new AdminSiteNotificationSendDto
        {
            Title = title!,
            Body = body!,
            RecipientScope = request.RecipientScope,
            UserIds = request.UserIds,
            Audiences = request.Audiences
        };

        var errorMessage = await siteNotificationService.SendAsync(notification, cancellationToken);
        if (errorMessage != null)
        {
            return this.BadRequest(new { message = errorMessage });
        }

        return this.NoContent();
    }

    /// <summary> Проверяет, что заголовок и текст уведомления заполнены. </summary>
    private static bool IsValidContent(string? title, string? body)
    {
        return !string.IsNullOrWhiteSpace(title) && title.Length <= 200 &&
            !string.IsNullOrWhiteSpace(body) && body.Length <= 4000;
    }

    /// <summary> Запрос на обновление шаблона внутрисайтового уведомления. </summary>
    public sealed class UpdateSiteNotificationTemplateRequest
    {
        /// <summary> Новый заголовок шаблона уведомления. </summary>
        public string? Title { get; init; }

        /// <summary> Новый текст шаблона уведомления. </summary>
        public string? Body { get; init; }
    }

    /// <summary> Запрос на рассылку внутрисайтового уведомления. </summary>
    public sealed class SendSiteNotificationRequest
    {
        /// <summary> Заголовок отправляемого уведомления. </summary>
        public string? Title { get; init; }

        /// <summary> Текст отправляемого уведомления. </summary>
        public string? Body { get; init; }

        /// <summary> Область получателей рассылки. </summary>
        public string? RecipientScope { get; init; }

        /// <summary> Идентификаторы пользователей-получателей. </summary>
        public IReadOnlyCollection<string>? UserIds { get; init; }

        /// <summary> Аудитории получателей рассылки. </summary>
        public IReadOnlyCollection<string>? Audiences { get; init; }
    }
}
