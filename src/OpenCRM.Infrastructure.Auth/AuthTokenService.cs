using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Auth;
using OpenCRM.Services.Interfaces.Auth.Dtos;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Infrastructure.Data.Notifications;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Infrastructure.Auth;

/// <summary> Сервис выпуска и обновления JWT-токенов для API. </summary>
public sealed class AuthTokenService(
    UserManager<ApplicationUser> userManager,
    IUsersService usersService,
    IPersonalSubscriptionQueryService subscriptionService,
    IOptions<JwtTokenOptions> options,
    IApplicationUnitOfWork unitOfWork,
    OpenCRMContext context,
    ILogger<AuthTokenService> logger) : IAuthTokenService
{
    /// <summary> Имя провайдера refresh-токенов. </summary>
    private const string RefreshTokenProvider = "OpenCRMApi";

    /// <summary> Префикс имени refresh-токена. </summary>
    private const string RefreshTokenNamePrefix = "refresh:";

    /// <summary> Временный ключ подписи для случаев без настроенного ключа. </summary>
    private static readonly byte[] EphemeralSigningKey = RandomNumberGenerator.GetBytes(64);

    /// <summary> Настройки JWT-токенов. </summary>
    private readonly JwtTokenOptions _options = options.Value;

    /// <summary> Выполняет вход по логину и паролю, выпуская токены. </summary>
    public async Task<ApiTokenResponse?> LoginAsync(ApiLoginRequest request)
    {
        var email = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user == null || user.IsDeleted)
        {
            return null;
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            return null;
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        await subscriptionService.EnsureDefaultPlanAccessAsync(user.Id);
        await usersService.RecordLastLoginDate(user.Id, "api-password");

        return await CreateTokenResponseAsync(user);
    }

    /// <summary> Регистрирует пользователя и выпускает токены. </summary>
    public async Task<ApiTokenResponse?> RegisterAsync(ApiRegisterRequest request)
    {
        if (!request.PersonalDataProcessingConsent)
        {
            return null;
        }

        var email = request.Email.Trim();
        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser != null && !existingUser.IsDeleted)
        {
            return null;
        }

        var consentAcceptedAt = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            DatetimeCreation = consentAcceptedAt,
            OriginalEmail = email,
            Email = email,
            UserName = email,
            PersonalDataProcessingConsentAccepted = true,
            PersonalDataProcessingConsentAcceptedAt = consentAcceptedAt,
            AdvertisingConsentAccepted = request.AdvertisingConsent,
            AdvertisingConsentAcceptedAt = request.AdvertisingConsent ? consentAcceptedAt : null
        };
        request.Attribution?.ApplyTo(user);

        var created = false;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return;
            }

            TelegramAdminNotificationPublisher.AddRegistration(context, user);
            await context.SaveChangesAsync();
            created = true;
        });

        if (!created)
        {
            return null;
        }

        await subscriptionService.EnsureDefaultPlanAccessAsync(user.Id);
        await usersService.RecordLastLoginDate(user.Id, "api-registration-password");

        return await CreateTokenResponseAsync(user);
    }

    /// <summary> Обновляет токены по refresh-токену. </summary>
    public async Task<ApiTokenResponse?> RefreshAsync(ApiRefreshTokenRequest request)
    {
        var user = await userManager.FindByIdAsync(request.UserId);
        if (user == null || user.IsDeleted)
        {
            return null;
        }

        var parsedToken = ParseRefreshToken(request.RefreshToken);
        if (parsedToken == null)
        {
            return null;
        }

        var (sessionId, secret) = parsedToken.Value;
        var storedToken = await userManager.GetAuthenticationTokenAsync(user, RefreshTokenProvider, RefreshTokenNamePrefix + sessionId);
        var storedTokenParts = storedToken?.Split('.', 2);
        if (storedTokenParts?.Length != 2
            || !long.TryParse(storedTokenParts[1], out var expiresAtUnixTime)
            || DateTimeOffset.FromUnixTimeSeconds(expiresAtUnixTime) <= DateTimeOffset.UtcNow
            || !CryptographicOperations.FixedTimeEquals(HashToken(secret), Convert.FromBase64String(storedTokenParts[0])))
        {
            return null;
        }

        await userManager.RemoveAuthenticationTokenAsync(user, RefreshTokenProvider, RefreshTokenNamePrefix + sessionId);
        var response = await CreateTokenResponseAsync(user);
        try
        {
            await usersService.RecordLastAccessAsync(user.Id);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Не удалось записать посещение при восстановлении API-сессии пользователя {UserId}.", user.Id);
        }

        return response;
    }

    /// <summary> Отзывает refresh-токен сессии. </summary>
    public async Task<bool> RevokeAsync(ApiRevokeTokenRequest request)
    {
        var user = await userManager.FindByIdAsync(request.UserId);
        var parsedToken = ParseRefreshToken(request.RefreshToken);
        if (user == null || parsedToken == null)
        {
            return false;
        }

        var result = await userManager.RemoveAuthenticationTokenAsync(user, RefreshTokenProvider, RefreshTokenNamePrefix + parsedToken.Value.SessionId);
        return result.Succeeded;
    }

    /// <summary> Возвращает данные текущего пользователя по claims. </summary>
    public async Task<ApiUserResponse?> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user == null)
        {
            var userId = principal.FindFirstValue("UserID")
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirstValue("sub")
                ?? principal.FindFirstValue("nameid");

            user = string.IsNullOrWhiteSpace(userId)
                ? null
                : await userManager.FindByIdAsync(userId);
        }

        return user == null || user.IsDeleted ? null : await CreateUserResponseAsync(user);
    }

    /// <summary> Формирует ответ с парой токенов для пользователя. </summary>
    private async Task<ApiTokenResponse> CreateTokenResponseAsync(ApplicationUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var accessTokenExpiresAt = now.AddMinutes(Math.Max(_options.AccessTokenMinutes, 5));
        var refreshTokenExpiresAt = now.AddDays(Math.Max(_options.RefreshTokenDays, 1));
        var refreshToken = CreateRefreshToken();
        var parsedRefreshToken = ParseRefreshToken(refreshToken)!.Value;

        await userManager.SetAuthenticationTokenAsync(
            user,
            RefreshTokenProvider,
            RefreshTokenNamePrefix + parsedRefreshToken.SessionId,
            $"{Convert.ToBase64String(HashToken(parsedRefreshToken.Secret))}.{refreshTokenExpiresAt.ToUnixTimeSeconds()}");

        return new ApiTokenResponse
        {
            AccessToken = await CreateAccessTokenAsync(user, accessTokenExpiresAt),
            AccessTokenExpiresAt = accessTokenExpiresAt,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAt = refreshTokenExpiresAt,
            User = await CreateUserResponseAsync(user)
        };
    }

    /// <summary> Создаёт JWT access-токен для пользователя. </summary>
    private async Task<string> CreateAccessTokenAsync(ApplicationUser user, DateTimeOffset expiresAt)
    {
        var roles = await userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("UserID", user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(CreateSecurityKey(_options.SigningKey), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary> Формирует ответ с данными пользователя. </summary>
    private async Task<ApiUserResponse> CreateUserResponseAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new ApiUserResponse
        {
            Id = user.Id,
            Email = user.Email,
            UserName = user.UserName,
            Roles = roles.ToArray()
        };
    }

    /// <summary> Генерирует новый refresh-токен. </summary>
    private static string CreateRefreshToken()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return $"{sessionId}.{Base64UrlEncoder.Encode(secret)}";
    }

    /// <summary> Разбирает refresh-токен на идентификатор сессии и секрет. </summary>
    private static (string SessionId, string Secret)? ParseRefreshToken(string refreshToken)
    {
        var parts = refreshToken.Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out _))
        {
            return null;
        }

        return (parts[0], Base64UrlEncoder.Decode(parts[1]));
    }

    /// <summary> Вычисляет хеш токена. </summary>
    private static byte[] HashToken(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    /// <summary> Создаёт ключ подписи из конфигурации или временный. </summary>
    public static SymmetricSecurityKey CreateSecurityKey(string? configuredSigningKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredSigningKey))
        {
            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuredSigningKey));
        }

        // Keep the API usable without a configured key, but never fall back to a
        // repository-visible signing secret. Access tokens become invalid after
        // an application restart; persisted refresh tokens can issue a new pair.
        return new SymmetricSecurityKey(EphemeralSigningKey);
    }

}
