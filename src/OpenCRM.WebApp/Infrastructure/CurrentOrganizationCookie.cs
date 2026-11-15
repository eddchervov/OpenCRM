using Microsoft.AspNetCore.Http;

namespace OpenCRM.WebApp.Infrastructure;

/// <summary> Работа с cookie текущей организации. </summary>
public static class CurrentOrganizationCookie
{
    /// <summary> Имя cookie текущей организации. </summary>
    public const string Name = "OpenCRM.CurrentOrganizationId";

    /// <summary> Пытается прочитать идентификатор организации из cookie запроса. </summary>
    public static bool TryRead(HttpRequest request, out Guid organizationId)
    {
        organizationId = Guid.Empty;

        return request.Cookies.TryGetValue(Name, out var rawOrganizationId)
            && Guid.TryParse(rawOrganizationId, out organizationId);
    }

    /// <summary> Записывает идентификатор организации в cookie ответа. </summary>
    public static void Write(HttpResponse response, Guid organizationId)
    {
        if (organizationId == Guid.Empty)
        {
            return;
        }

        response.Cookies.Append(
            Name,
            organizationId.ToString("D"),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            });
    }
}
