using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;

namespace OpenCRM.WebApp.Infrastructure.UserActivity;

/// <summary> Фиксирует результат изменяющего API-запроса, не читая его тело или параметры поиска. </summary>
public sealed class UserActivityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserActivityBuffer buffer, IOptionsMonitor<UserActivityOptions> options)
    {
        var request = context.Request;
        var userId = context.User.FindFirst("UserID")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (!options.CurrentValue.Enabled || !UserActivityMiddleware.IsTrackedRequest(context) || string.IsNullOrEmpty(userId)
            || !Guid.TryParse(request.Cookies["opencrmActivitySession"], out var sessionId))
        {
            await next(context);

            return;
        }

        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        if (string.IsNullOrEmpty(route) || route.Length > 239)
        {
            await next(context);

            return;
        }

        var status = StatusCodes.Status500InternalServerError;
        try
        {
            await next(context);
            status = context.Response.StatusCode;
        }
        finally
        {
            var now = DateTime.UtcNow;
            var organizationId = Guid.TryParse(request.RouteValues["organizationId"]?.ToString(), out var organization)
                ? (Guid?)organization : null;

            var targetId = request.RouteValues["id"]?.ToString() ?? request.RouteValues["maintenanceId"]?.ToString()
                ?? request.RouteValues["clientId"]?.ToString() ?? request.RouteValues["vehicleId"]?.ToString();

            buffer.TryWrite(new UserActivityEvent
            {
                UserId = userId,
                OrganizationId = organizationId,
                SessionId = sessionId,
                OccurredAt = now,
                ReceivedAt = now,
                Kind = "operation",
                Action = $"{request.Method}:{(status < 400 ? "success" : "error")}:{status}",
                Page = "/" + route.TrimStart('/'),
                Scenario = organizationId.HasValue ? "account" : "personal",
                Section = "api",
                TargetId = Guid.TryParse(targetId, out var target) ? target.ToString() : null
            });
        }
    }

    private static bool IsTrackedRequest(HttpContext context)
    {
        var request = context.Request;
        if (context.User.Identity?.IsAuthenticated != true
            || request.Cookies.ContainsKey("ImpersonatorId"))
        {
            return false;
        }

        if (request.Method is not ("POST" or "PUT" or "PATCH" or "DELETE"))
        {
            return false;
        }

        return request.Path.StartsWithSegments("/api/v1")
            && !request.Path.StartsWithSegments("/api/v1/admin")
            && !request.Path.StartsWithSegments("/api/v1/user-activity")
            && !request.Path.StartsWithSegments("/api/v1/auth")
            && !request.Path.StartsWithSegments("/api/v1/antiforgery");
    }
}
