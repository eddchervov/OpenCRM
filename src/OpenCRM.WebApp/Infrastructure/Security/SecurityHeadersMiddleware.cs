namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Промежуточное ПО добавления заголовков безопасности в ответ. </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    /// <summary> Добавляет заголовки безопасности к ответу. </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "SAMEORIGIN");
            headers.TryAdd("Referrer-Policy", SensitivePagePolicy.IsSensitive(context.Request)
                ? "no-referrer"
                : "strict-origin-when-cross-origin");
            headers.TryAdd("Permissions-Policy", "geolocation=(), microphone=()");
            headers.TryAdd(
                "Content-Security-Policy",
                "object-src 'none'; base-uri 'self'; frame-ancestors 'self'; form-action 'self';");

            if (!environment.IsDevelopment() && context.Request.IsHttps)
            {
                headers.TryAdd("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
            }

            return Task.CompletedTask;
        });

        await next(context);
    }
}
