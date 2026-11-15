using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Notifications;

namespace OpenCRM.WebApp.Controllers.Api.V1;

/// <summary> API сайтовых уведомлений пользователя. </summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public sealed class SiteNotificationsApiController(
    ISiteNotificationService siteNotificationService,
    IAuthorizationService authorizationService) : ControllerBase
{
    /// <summary> Возвращает уведомления текущего пользователя. </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return this.Unauthorized();
        }

        var isAdmin = (await authorizationService.AuthorizeAsync(
            this.User, AdminAuthorizationPolicies.Access)).Succeeded;

        var notifications = await siteNotificationService.GetAsync(userId, isAdmin, cancellationToken);

        return this.Ok(notifications);
    }

    /// <summary> Закрывает указанное уведомление пользователя. </summary>
    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return this.Unauthorized();
        }

        var isAdmin = (await authorizationService.AuthorizeAsync(
            this.User, AdminAuthorizationPolicies.Access)).Succeeded;

        var isClosed = await siteNotificationService.CloseAsync(userId, isAdmin, id, cancellationToken);
        if (!isClosed)
        {
            return this.NotFound();
        }

        return this.NoContent();
    }
}
