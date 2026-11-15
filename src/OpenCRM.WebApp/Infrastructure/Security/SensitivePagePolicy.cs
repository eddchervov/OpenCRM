namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Определяет страницы, адрес которых нельзя передавать внешней аналитике или через Referer. </summary>
public static class SensitivePagePolicy
{
    private static readonly HashSet<string> s_paths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/auth/resetpassword",
        "/auth/confirmemail",
        "/auth/confirmemailchange",
        "/email/unsubscribe"
    };

    private static readonly HashSet<string> s_queryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "token", "email", "userId", "password", "returnUrl"
    };

    /// <summary> Проверяет путь внутри приложения и имена чувствительных параметров запроса. </summary>
    public static bool IsSensitive(HttpRequest request)
    {
        return SensitivePagePolicy.s_paths.Contains(request.Path.Value?.TrimEnd('/') ?? string.Empty)
            || request.Query.Keys.Any(SensitivePagePolicy.s_queryKeys.Contains);
    }
}
