using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using OpenCRM.Services.Interfaces.Emails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Auth;
using OpenCRM.Services.Interfaces.Auth.Dtos;
using OpenCRM.WebApp.Infrastructure.Security;
using Swashbuckle.AspNetCore.Annotations;
using System.Text;

namespace OpenCRM.WebApp.Controllers.Api.V1;

/// <summary> API аутентификации: вход, регистрация и работа с токенами. </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthApiController(
    IAuthTokenService authTokenService,
    UserManager<ApplicationUser> userManager,
    IEmailNotificationService emailSender,
    EmailConfirmationDelivery confirmationDelivery,
    IConfiguration configuration,
    ILogger<AuthApiController> logger) : ControllerBase
{
    /// <summary> Выполняет вход по email и паролю, возвращает токены. </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [EnableRateLimiting(RateLimitPolicyNames.Authentication)]
    [SwaggerOperation(Summary = "Вход по email и паролю", Description = "Возвращает JWT access token, refresh token и базовые данные пользователя.")]
    [ProducesResponseType(typeof(ApiTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] ApiLoginRequest request)
    {
        var response = await authTokenService.LoginAsync(request);
        return response == null ? Unauthorized(new { message = "Неверный email или пароль." }) : Ok(response);
    }

    /// <summary> Регистрирует пользователя и возвращает токены. </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [EnableRateLimiting(RateLimitPolicyNames.Authentication)]
    [SwaggerOperation(Summary = "Регистрация пользователя", Description = "Создаёт аккаунт и возвращает токены. Подтверждение email требуется, если включено в конфигурации.")]
    [ProducesResponseType(typeof(ApiTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] ApiRegisterRequest request)
    {
        if (!request.PersonalDataProcessingConsent)
        {
            return BadRequest(new { message = "Для регистрации необходимо согласие на обработку персональных данных." });
        }

        request = new ApiRegisterRequest
        {
            Email = request.Email,
            Password = request.Password,
            PersonalDataProcessingConsent = request.PersonalDataProcessingConsent,
            AdvertisingConsent = false
        };
        var response = await authTokenService.RegisterAsync(request);
        if (response == null)
        {
            return BadRequest(new { message = "Не удалось зарегистрировать пользователя." });
        }

        if (EmailConfirmationPolicy.IsRequired(configuration))
        {
            var user = await userManager.FindByIdAsync(response.User.Id);
            if (user != null)
            {
                try
                {
                    var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                    var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

                    var confirmationUrl = Url.Action(
                        nameof(Auth.AuthController.ConfirmEmail),
                        "Auth",
                        new { userId = user.Id, code = encodedToken },
                        Request.Scheme);

                    await confirmationDelivery.TrySendAsync(user.Id, () => emailSender.QueueTemplateAsync(
                        user.Email!, "Подтвердите ваш email", "system/email-confirmation",
                        new { name = user.Email, confirmationUrl }));
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Аккаунт {UserId} создан через API, но подтверждение email не отправлено", user.Id);
                }
            }
        }

        return Ok(response);
    }

    /// <summary> Отправляет письмо со ссылкой сброса пароля. </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [EnableRateLimiting(RateLimitPolicyNames.EmailDelivery)]
    [SwaggerOperation(Summary = "Запрос сброса пароля", Description = "Отправляет письмо со ссылкой сброса пароля, если активный аккаунт существует.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ForgotPassword([FromBody] ApiForgotPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null || user.IsDeleted)
        {
            return NoContent();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var webCallbackUrl = Url.Action(
            action: "ResetPassword",
            controller: "Auth",
            values: new { userId = user.Id, code = encodedToken },
            protocol: Request.Scheme);

        try
        {
            await emailSender.QueueTemplateAsync(
                request.Email, "Сбросить пароль", "system/password-reset-mobile",
                new { userId = user.Id, code = encodedToken, resetUrl = webCallbackUrl });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to send password reset email to {Email}", request.Email);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Не удалось отправить письмо для сброса пароля." });
        }

        return NoContent();
    }

    /// <summary> Сбрасывает пароль по коду из письма. </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [EnableRateLimiting(RateLimitPolicyNames.Authentication)]
    [SwaggerOperation(Summary = "Сброс пароля", Description = "Сбрасывает пароль по userId и code из письма.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ApiResetPasswordRequest request)
    {
        var user = await userManager.FindByIdAsync(request.UserId);
        if (user == null)
        {
            return NoContent();
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Code));
        }
        catch
        {
            return BadRequest(new { message = "Ссылка для сброса пароля повреждена или устарела." });
        }

        var result = await userManager.ResetPasswordAsync(user, decodedToken, request.Password);
        if (result.Succeeded)
        {
            return NoContent();
        }

        return BadRequest(new
        {
            message = "Не удалось сбросить пароль.",
            errors = result.Errors.Select(x => x.Description)
        });
    }

    /// <summary> Обновляет пару токенов по refresh token. </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [EnableRateLimiting(RateLimitPolicyNames.Authentication)]
    [SwaggerOperation(Summary = "Обновление токенов", Description = "Принимает userId и refresh token, ротирует refresh token и возвращает новую пару токенов.")]
    [ProducesResponseType(typeof(ApiTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] ApiRefreshTokenRequest request)
    {
        var response = await authTokenService.RefreshAsync(request);
        return response == null ? Unauthorized(new { message = "Refresh token недействителен." }) : Ok(response);
    }

    /// <summary> Отзывает refresh token текущей сессии. </summary>
    [AllowUnconfirmedEmail]
    [HttpPost("revoke")]
    [Authorize]
    [SwaggerOperation(Summary = "Отзыв refresh token", Description = "Удаляет refresh token текущей API-сессии.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Revoke([FromBody] ApiRevokeTokenRequest request)
    {
        var currentUserId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(currentUserId) || !string.Equals(currentUserId, request.UserId, StringComparison.Ordinal))
        {
            return Forbid();
        }

        return await authTokenService.RevokeAsync(request) ? NoContent() : BadRequest(new { message = "Не удалось отозвать refresh token." });
    }

    /// <summary> Возвращает данные текущего пользователя. </summary>
    [HttpGet("me")]
    [Authorize]
    [SwaggerOperation(Summary = "Текущий пользователь", Description = "Возвращает базовые данные пользователя из cookie- или JWT-сессии.")]
    [ProducesResponseType(typeof(ApiUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me()
    {
        var response = await authTokenService.GetCurrentUserAsync(User);
        return response == null ? Unauthorized() : Ok(response);
    }

    /// <summary> Запрос на сброс пароля по email. </summary>
    public sealed class ApiForgotPasswordRequest
    {
        /// <summary> Email пользователя. </summary>
        public string Email { get; init; } = string.Empty;
    }

    /// <summary> Запрос на сброс пароля по коду. </summary>
    public sealed class ApiResetPasswordRequest
    {
        /// <summary> Идентификатор пользователя. </summary>
        public string UserId { get; init; } = string.Empty;

        /// <summary> Код сброса пароля из письма. </summary>
        public string Code { get; init; } = string.Empty;

        /// <summary> Новый пароль. </summary>
        public string Password { get; init; } = string.Empty;
    }
}
