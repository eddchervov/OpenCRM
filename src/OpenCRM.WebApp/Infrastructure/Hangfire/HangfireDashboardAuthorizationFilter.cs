using Hangfire.Dashboard;

namespace OpenCRM.WebApp.Infrastructure.Hangfire;

/// <summary> Фильтр авторизации доступа к дашборду Hangfire. </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    /// <summary> Проверяет право текущего пользователя на доступ к дашборду. </summary>
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
            && (httpContext.User.IsInRole("System") || httpContext.User.IsInRole("Admin"));
    }
}

