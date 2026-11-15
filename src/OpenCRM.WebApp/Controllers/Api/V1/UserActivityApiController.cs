using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.WebApp.Infrastructure.UserActivity;

namespace OpenCRM.WebApp.Controllers.Api.V1;

/// <summary> Принимает только ограниченные события текущего авторизованного пользователя. </summary>
[ApiController]
[Authorize]
[Route("api/v1/user-activity")]
public sealed class UserActivityApiController(
    OpenCRMContext context,
    UserActivityBuffer buffer,
    IOptionsMonitor<UserActivityOptions> options) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(48 * 1024)]
    [EnableRateLimiting("user-activity")]
    public async Task<IActionResult> Post(UserActivityBatch batch, CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled || this.Request.Cookies.ContainsKey("ImpersonatorId"))
        {
            return StatusCode(StatusCodes.Status410Gone);
        }

        var userId = this.GetApiUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var now = DateTime.UtcNow;
        if (batch.SessionId == Guid.Empty || batch.Events.Any(activity =>
            activity == null || activity.OccurredAt < now.AddDays(-1) || activity.OccurredAt > now.AddMinutes(1)))
        {
            return BadRequest(new { message = "Некорректный сеанс или время события." });
        }

        if (batch.OrganizationId.HasValue && !await context.Organizations.AnyAsync(
            organization => organization.Id == batch.OrganizationId && !organization.IsDeleted
                && (organization.OwnerUserId == userId || organization.Members.Any(member => member.UserId == userId)),
            cancellationToken))
        {
            return Forbid();
        }

        var accepted = 0;
        foreach (var activity in batch.Events)
        {
            if (buffer.TryWrite(new UserActivityEvent
            {
                UserId = userId,
                OrganizationId = batch.OrganizationId,
                SessionId = batch.SessionId,
                OccurredAt = activity.OccurredAt.UtcDateTime,
                ReceivedAt = now,
                Kind = activity.Kind,
                Action = activity.Action,
                Page = activity.Page,
                Scenario = activity.Scenario,
                Section = activity.Section,
                TargetId = activity.TargetId
            }))
            {
                accepted++;
            }
        }

        return Accepted(new { accepted });
    }
}
