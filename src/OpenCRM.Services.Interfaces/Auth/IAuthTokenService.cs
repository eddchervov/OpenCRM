using System.Security.Claims;
using OpenCRM.Services.Interfaces.Auth.Dtos;

namespace OpenCRM.Services.Interfaces.Auth;

/// <summary> Аутентификация и управление токенами через API. </summary>
public interface IAuthTokenService
{
    /// <summary> Выполняет вход и возвращает токены. </summary>
    Task<ApiTokenResponse?> LoginAsync(ApiLoginRequest request);

    /// <summary> Регистрирует пользователя и возвращает токены. </summary>
    Task<ApiTokenResponse?> RegisterAsync(ApiRegisterRequest request);

    /// <summary> Обновляет токены по refresh-токену. </summary>
    Task<ApiTokenResponse?> RefreshAsync(ApiRefreshTokenRequest request);

    /// <summary> Отзывает refresh-токен пользователя. </summary>
    Task<bool> RevokeAsync(ApiRevokeTokenRequest request);

    /// <summary> Возвращает данные текущего пользователя по claims. </summary>
    Task<ApiUserResponse?> GetCurrentUserAsync(ClaimsPrincipal principal);
}
