using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Фильтр проверки antiforgery-токенов для API-запросов. </summary>
public sealed class ApiAntiforgeryAuthorizationFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    /// <summary> Множество безопасных HTTP-методов, не требующих проверки токена. </summary>
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options,
        HttpMethods.Trace
    };

    /// <summary> Проверяет antiforgery-токен для небезопасных HTTP-методов. </summary>
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var httpContext = context.HttpContext;
        var request = httpContext.Request;

        if (!request.Path.StartsWithSegments("/api") || SafeMethods.Contains(request.Method))
        {
            return;
        }

        var endpoint = httpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            return;
        }

        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new BadRequestObjectResult(new
            {
                message = "Не удалось проверить защитный токен запроса."
            });
        }
    }
}
