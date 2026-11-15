using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace OpenCRM.WebApp.Infrastructure.UserActivity;

/// <summary> Единственная точка подключения необязательного сбора активности. </summary>
public static class UserActivityServiceCollectionExtensions
{
    public static IServiceCollection AddUserActivity(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<UserActivityOptions>(configuration.GetSection("UserActivity"));
        services.AddSingleton<UserActivityBuffer>();
        services.AddHostedService<UserActivityWriter>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("user-activity", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirst("UserID")?.Value
                        ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                        ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 12,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return services;
    }
}
