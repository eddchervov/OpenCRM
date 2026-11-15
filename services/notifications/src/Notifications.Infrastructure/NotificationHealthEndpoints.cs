using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Notifications.Infrastructure;

/// <summary> Предоставляет краткие состояния процесса и готовности его зависимостей. </summary>
public static class NotificationHealthEndpoints
{
    public static void MapNotificationHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            ResultStatusCodes = { [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable }
        });
    }
}