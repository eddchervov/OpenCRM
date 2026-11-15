using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain.Identity;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Ограничивает доступ неподтверждённых аккаунтов, включая ранее выданные сессии. </summary>
public sealed class EmailConfirmationMiddleware(RequestDelegate next)
{
    /// <summary> Адрес обязательного подтверждения email. </summary>
    public const string ConfirmationPath = "/auth/verify-email";

    /// <summary> Проверяет актуальное состояние аккаунта до проверки разрешений на действие. </summary>
    public async Task InvokeAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        IAuthorizationPolicyProvider policyProvider,
        IPolicyEvaluator policyEvaluator)
    {
        if (EmailConfirmationMiddleware.IsAllowed(context, environment))
        {
            await next(context);

            return;
        }

        var endpoint = context.GetEndpoint();
        if (endpoint != null)
        {
            var policy = await AuthorizationPolicy.CombineAsync(
                policyProvider,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());

            if (policy != null)
            {
                // JWT может входить только в политику endpoint, а не в схему аутентификации по умолчанию.
                await policyEvaluator.AuthenticateAsync(policy, context);
            }
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);

            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        var user = await userManager.GetUserAsync(context.User);
        if (user == null || user.IsDeleted)
        {
            if (BrowserAuthorizationMiddlewareResultHandler.IsPageRequest(context))
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.User = new ClaimsPrincipal(new ClaimsIdentity());
                await context.ChallengeAsync(IdentityConstants.ApplicationScheme);

                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return;
        }

        if (user.EmailConfirmed || !EmailConfirmationPolicy.IsRequired(configuration))
        {
            await next(context);

            return;
        }

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "email_confirmation_required",
                message = "Подтвердите email, чтобы продолжить работу.",
                confirmationUrl = $"{context.Request.PathBase}{ConfirmationPath}"
            });

            return;
        }

        context.Response.Redirect($"{context.Request.PathBase}{ConfirmationPath}");
    }

    /// <summary> Оставляет доступными подтверждение, восстановление доступа и публичные ресурсы оформления. </summary>
    private static bool IsAllowed(HttpContext context, IWebHostEnvironment environment)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/admin-panel") || path.StartsWithSegments("/hangfire"))
        {
            return false;
        }

        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<AllowUnconfirmedEmailAttribute>() != null)
        {
            return true;
        }

        // Файлы, обслуживаемые UseStaticFiles без endpoint, нужны для оформления страницы подтверждения.
        return endpoint == null && environment.WebRootFileProvider.GetFileInfo(path.Value ?? string.Empty).Exists;
    }
}
