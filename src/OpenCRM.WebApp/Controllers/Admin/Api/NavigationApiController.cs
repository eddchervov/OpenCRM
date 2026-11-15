using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.WebApp.Controllers.Api.V1;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> Дневные счётчики разделов административного меню. </summary>
[Route("api/v1/admin/navigation")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.Access)]
public sealed class NavigationApiController(
    IAdminNavigationService navigationService,
    IAuthorizationService authorizationService) : ControllerBase
{
    [HttpGet("counts")]
    public async Task<IActionResult> GetCounts(
        [FromQuery] DateTimeOffset fromUtc,
        [FromQuery] DateTimeOffset toUtc,
        CancellationToken cancellationToken,
        [FromQuery] bool unread = false)
    {
        // Локальные сутки при переводе часов могут длиться 23 или 25 часов.
        if (fromUtc == default || toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromHours(25))
        {
            return this.BadRequest("Укажите границы одного дня.");
        }

        string[] policies =
        [
            AdminAuthorizationPolicies.ViewUsers,
            AdminAuthorizationPolicies.ViewBilling,
            AdminAuthorizationPolicies.ViewContent,
            AdminAuthorizationPolicies.ViewCommunications
        ];

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var policy in policies)
        {
            if ((await authorizationService.AuthorizeAsync(this.User, policy)).Succeeded)
            {
                permissions.Add(policy);
            }
        }

        AdminNavigationCountsDto counts;
        if (!unread)
        {
            counts = await navigationService.GetCountsAsync(
                fromUtc.UtcDateTime, toUtc.UtcDateTime, permissions, cancellationToken);

            return this.Ok(counts);
        }

        if (fromUtc > DateTimeOffset.UtcNow)
        {
            return this.BadRequest("Начало интервала не может быть в будущем.");
        }

        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return this.Unauthorized();
        }

        counts = await navigationService.GetUnreadCountsAsync(
            userId, fromUtc.UtcDateTime, permissions, cancellationToken);

        return this.Ok(counts);
    }

    [HttpPost("sections/{section}/viewed")]
    public async Task<IActionResult> MarkSectionViewed(
        string section,
        [FromBody] MarkNavigationSectionViewedRequest request,
        CancellationToken cancellationToken)
    {
        var policy = GetSectionPolicy(section);
        if (policy == null)
        {
            return this.BadRequest("Неизвестный раздел навигации.");
        }

        if (!(await authorizationService.AuthorizeAsync(this.User, policy)).Succeeded)
        {
            return this.Forbid();
        }

        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return this.Unauthorized();
        }

        var viewedThroughUtc = request.ViewedThroughUtc.UtcDateTime;
        var nowUtc = TruncateToMicroseconds(DateTime.UtcNow);
        if (viewedThroughUtc == default || viewedThroughUtc > nowUtc)
        {
            return this.BadRequest("Укажите просмотренное время в UTC, не позднее текущего времени.");
        }

        viewedThroughUtc = TruncateToMicroseconds(viewedThroughUtc);
        await navigationService.MarkSectionViewedAsync(userId, section, viewedThroughUtc, cancellationToken);

        return this.Ok(new { message = "Просмотр раздела сохранён." });
    }

    private static string? GetSectionPolicy(string section) => section switch
    {
        AdminNavigationSections.Users => AdminAuthorizationPolicies.ViewUsers,
        AdminNavigationSections.SupportMessages => AdminAuthorizationPolicies.ViewContent,
        AdminNavigationSections.Emails => AdminAuthorizationPolicies.ViewCommunications,
        _ => null
    };

    private static DateTime TruncateToMicroseconds(DateTime valueUtc) =>
        new(valueUtc.Ticks - valueUtc.Ticks % 10, DateTimeKind.Utc);

    public sealed class MarkNavigationSectionViewedRequest
    {
        public DateTimeOffset ViewedThroughUtc { get; init; }
    }
}
