using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Controllers;
using OpenCRM.Services.Interfaces.Users;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Фиксирует возвращение на страницу с действующей сессией. </summary>
public sealed class UserLastAccessMiddleware(RequestDelegate next, ILogger<UserLastAccessMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IUsersService usersService)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isPageVisit = context.User.Identity?.IsAuthenticated == true
            && userId != null
            && HttpMethods.IsGet(context.Request.Method)
            && context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() != null
            && context.Request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)
            && (!context.Request.Headers.TryGetValue("Sec-Fetch-Dest", out var destination) || destination == "document");

        await next(context);

        if (!isPageVisit || context.Response.StatusCode is < 200 or >= 300)
        {
            return;
        }

        try
        {
            await usersService.RecordLastAccessAsync(userId!, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Запись посещения отменена после отключения клиента.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Не удалось записать последнее посещение пользователя {UserId}.", userId);
        }
    }
}
