using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace OpenCRM.WebApp.Controllers.Api.V1;

/// <summary> Расширения для получения пользователя API-контроллеров. </summary>
internal static class ApiControllerUserExtensions
{
    /// <summary> Возвращает идентификатор пользователя API-контроллера. </summary>
    public static string? GetApiUserId(this ControllerBase controller)
    {
        return controller.User.FindFirstValue("UserID")
            ?? controller.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? controller.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? controller.User.FindFirstValue("sub")
            ?? controller.User.FindFirstValue("nameid")
            ?? controller.User.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
    }
}
