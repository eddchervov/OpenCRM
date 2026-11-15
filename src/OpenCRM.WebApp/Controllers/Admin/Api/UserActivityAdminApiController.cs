using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.WebApp.Infrastructure.UserActivity;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> Постраничная лента активности с общим доступом и фильтром пользователя. </summary>
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ViewUsers)]
[Route("api/v1/admin/user-activity")]
public sealed class UserActivityAdminApiController(OpenCRMContext context, IOptionsMonitor<UserActivityOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        string? userId = null,
        string? search = null,
        string? kind = null,
        string? scenario = null,
        Guid? organizationId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        long? beforeId = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (search?.Length > 120 || userId?.Length > 450 || kind?.Length > 16 || scenario?.Length > 32
            || from > to || beforeId <= 0 || pageSize < 1 || pageSize > 200)
        {
            return BadRequest(new { message = "Проверьте фильтры истории активности." });
        }

        this.Response.Headers.CacheControl = "no-store";
        var query = context.Set<UserActivityEvent>().AsNoTracking();
        if (!string.IsNullOrEmpty(userId))
        {
            query = query.Where(activity => activity.UserId == userId);
        }

        if (!string.IsNullOrEmpty(kind))
        {
            query = query.Where(activity => activity.Kind == kind);
        }

        if (!string.IsNullOrEmpty(scenario))
        {
            query = query.Where(activity => activity.Scenario == scenario);
        }

        if (organizationId.HasValue)
        {
            query = query.Where(activity => activity.OrganizationId == organizationId);
        }

        if (beforeId.HasValue)
        {
            query = query.Where(activity => activity.Id < beforeId);
        }

        var start = from?.UtcDateTime ?? DateTime.UtcNow.AddDays(-Math.Clamp(options.CurrentValue.RetentionDays, 1, 365));
        query = query.Where(activity => activity.ReceivedAt >= start);
        if (to.HasValue)
        {
            var end = to.Value.UtcDateTime;
            query = query.Where(activity => activity.ReceivedAt <= end);
        }

        var rows = from activity in query
                   join user in context.Users on activity.UserId equals user.Id
                   join organization in context.Organizations on activity.OrganizationId equals organization.Id into organizations
                   from organization in organizations.DefaultIfEmpty()
                   select new
                   {
                       activity.Id, activity.UserId,
                       UserLabel = user.Email ?? user.PhoneNumber ?? user.Id,
                       activity.OrganizationId,
                       OrganizationName = organization == null ? null : organization.Name,
                       activity.SessionId, activity.OccurredAt, activity.ReceivedAt,
                       activity.Kind, activity.Action, activity.Page, activity.Scenario, activity.Section, activity.TargetId
                   };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(activity => activity.UserId == term || activity.UserLabel.Contains(term)
                || (activity.OrganizationName != null && activity.OrganizationName.Contains(term))
                || activity.Action.Contains(term) || activity.Page.Contains(term) || activity.Section.Contains(term)
                || activity.TargetId == term);
        }

        context.Database.SetCommandTimeout(10);
        var items = await rows.OrderByDescending(activity => activity.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(pageSize);
        }

        return Ok(new
        {
            options.CurrentValue.Enabled,
            RetentionDays = Math.Clamp(options.CurrentValue.RetentionDays, 1, 365),
            Items = items,
            NextBeforeId = hasMore ? (long?)items[^1].Id : null
        });
    }
}
