using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using OpenCRM.Services.Interfaces.Admin;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Промежуточное ПО проверки доступа к панели администратора. </summary>
public sealed class AdminPanelAuthorizationMiddleware(RequestDelegate next, Helpers.ProjectBrand brand, IWebHostEnvironment environment)
{
    /// <summary> Следующий обработчик в конвейере запросов. </summary>
    private readonly RequestDelegate _next = next;

    /// <summary> Проверяет авторизацию пользователя при доступе к панели администратора. </summary>
    public async Task InvokeAsync(HttpContext context, IAuthorizationService authorizationService)
    {
        var authorizationResult = await authorizationService.AuthorizeAsync(
            context.User,
            resource: null,
            AdminAuthorizationPolicies.Access);

        if (authorizationResult.Succeeded)
        {
            var requestPath = context.Request.Path.Value;
            if (string.Equals(requestPath, "/admin-panel", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requestPath, "/admin-panel/", StringComparison.OrdinalIgnoreCase))
            {
                context.Request.Path = "/admin-panel/index.html";
            }

            if (HttpMethods.IsGet(context.Request.Method)
                && string.Equals(context.Request.Path.Value, "/admin-panel/index.html", StringComparison.OrdinalIgnoreCase))
            {
                var file = Path.Combine(environment.WebRootPath, "admin-panel", "index.html");
                if (File.Exists(file))
                {
                    var html = await File.ReadAllTextAsync(file, context.RequestAborted);
                    var encodedName = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(brand.DisplayName);
                    html = html.Replace("<head>", $"<head><meta name=\"project-brand\" content=\"{encodedName}\" />", StringComparison.Ordinal);
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-store";
                    await context.Response.WriteAsync(html, context.RequestAborted);

                    return;
                }
            }

            context.SetEndpoint(null);
            await this._next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            await context.ForbidAsync();
            return;
        }

        await context.ChallengeAsync();
    }
}
