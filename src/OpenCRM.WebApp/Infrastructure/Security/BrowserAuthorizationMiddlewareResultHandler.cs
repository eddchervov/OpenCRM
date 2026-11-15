using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Отправляет гостя HTML-страницы на вход, сохраняя ответы авторизации API. </summary>
public sealed class BrowserAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged
            && policy.AuthenticationSchemes.Contains(IdentityConstants.ApplicationScheme)
            && BrowserAuthorizationMiddlewareResultHandler.IsPageRequest(context))
        {
            return context.ChallengeAsync(IdentityConstants.ApplicationScheme);
        }

        return this._defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary> Определяет страницу MVC отдельно от API, независимо от заголовка Accept. </summary>
    public static bool IsPageRequest(HttpContext context)
    {
        var endpoint = context.GetEndpoint();

        return !context.Request.Path.StartsWithSegments("/api")
            && endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>() != null
            && endpoint.Metadata.GetMetadata<IApiBehaviorMetadata>() == null;
    }
}
