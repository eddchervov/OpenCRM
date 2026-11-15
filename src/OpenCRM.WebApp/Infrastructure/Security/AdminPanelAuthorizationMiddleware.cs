using System.Text.Encodings.Web;
using System.Text.Json;
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
    public async Task InvokeAsync(
        HttpContext context,
        IAuthorizationService authorizationService,
        IConfiguration configuration)
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
                    var encodedName = HtmlEncoder.Default.Encode(brand.DisplayName);
                    var sectionsJson = JsonSerializer.Serialize(ReadProductSections(configuration, context.Request.PathBase));
                    var encodedSections = HtmlEncoder.Default.Encode(sectionsJson);
                    html = html.Replace(
                        "<head>",
                        $"<head><meta name=\"project-brand\" content=\"{encodedName}\" /><meta name=\"admin-product-sections\" content=\"{encodedSections}\" />",
                        StringComparison.Ordinal);
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

    private static IReadOnlyList<object> ReadProductSections(IConfiguration configuration, PathString pathBase)
    {
        var sections = new List<object>();
        foreach (var item in configuration.GetSection("Administration:Sections").GetChildren())
        {
            var title = item["Title"]?.Trim();
            var url = item["Url"];
            var permission = item["Permission"]?.Trim();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(permission) || !IsLocalPath(url))
            {
                continue;
            }

            sections.Add(new
            {
                title,
                url = pathBase.Add(new PathString(url)).Value,
                permission
            });
        }

        return sections;
    }

    private static bool IsLocalPath(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/' || url.StartsWith("//", StringComparison.Ordinal)
            || url.Contains('\\') || url.Contains('?') || url.Contains('#') || url.Any(char.IsControl))
        {
            return false;
        }

        var decoded = Uri.UnescapeDataString(url);

        return !decoded.Contains(':') && !decoded.Contains('\\') && !decoded.Any(char.IsControl)
            && !decoded.StartsWith("//", StringComparison.Ordinal)
            && !decoded.Split('/').Any(segment => segment is "." or "..");
    }
}
