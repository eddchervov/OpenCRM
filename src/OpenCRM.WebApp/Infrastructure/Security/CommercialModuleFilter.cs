using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Проверяет подключение коммерческого модуля, не заменяя авторизацию маршрута. </summary>
public sealed class CommercialModuleFilter(IConfiguration configuration) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (!configuration.GetValue("PlatformPolicy:Commercial:Enabled", false))
        {
            context.Result = new NotFoundResult();

            return;
        }

        await next();
    }
}
