using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Models.Auth;

namespace OpenCRM.WebApp.Controllers.Auth;

/// <summary> Обязательное подтверждение почты до доступа к функциям приложения. </summary>
[Authorize]
[AllowUnconfirmedEmail]
[Route("auth/verify-email")]
public sealed class EmailVerificationController(
    UserManager<ApplicationUser> userManager,
    IEmailNotificationService emailNotifications,
    EmailConfirmationDelivery confirmationDelivery,
    ILogger<EmailVerificationController> logger) : Controller
{
    /// <summary> Показывает инструкцию или возвращает подтверждённого пользователя к работе. </summary>
    [HttpGet]
    public async Task<IActionResult> Index(long? retryAt = null)
    {
        this.Response.Headers.CacheControl = "no-store";
        var user = await userManager.GetUserAsync(this.User);
        if (user == null || user.IsDeleted)
        {
            return this.Challenge();
        }

        if (user.EmailConfirmed)
        {
            return this.LocalRedirect("~/");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var rateLimitSeconds = retryAt.HasValue ? (int)(Math.Clamp(retryAt.Value, now, now + 600) - now) : 0;

        return this.View("/Views/Auth/VerifyEmail.cshtml", new EmailVerificationModel
        {
            Email = user.Email ?? string.Empty,
            RetryAfterSeconds = Math.Max(rateLimitSeconds, confirmationDelivery.GetRetryAfterSeconds(user.Id)),
            StatusMessage = this.TempData["Success"] as string,
            ErrorMessage = rateLimitSeconds > 0
                ? "Слишком много попыток отправки. Проверьте уже полученное письмо или дождитесь окончания отсчёта."
                : this.TempData["Error"] as string
        });
    }

    /// <summary> Повторно отправляет подтверждение только на адрес текущего аккаунта. </summary>
    [HttpPost("resend")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicyNames.EmailDelivery)]
    public async Task<IActionResult> Resend()
    {
        var user = await userManager.GetUserAsync(this.User);
        if (user == null || user.IsDeleted)
        {
            return this.Challenge();
        }

        if (user.EmailConfirmed)
        {
            return this.LocalRedirect("~/");
        }

        if (!EmailAddressValidation.TryValidate(user.Email, out var errorMessage))
        {
            this.TempData["Error"] = $"Письмо не отправлено. {errorMessage} Обратитесь в поддержку.";

            return this.RedirectToAction(nameof(Index));
        }

        try
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var confirmationUrl = this.Url.Action(
                nameof(AuthController.ConfirmEmail),
                "Auth",
                new { userId = user.Id, code = encodedToken },
                this.Request.Scheme);

            var sent = await confirmationDelivery.TrySendAsync(user.Id, () => emailNotifications.QueueTemplateAsync(
                user.Email!, "Подтвердите ваш email", "system/email-confirmation",
                new { name = user.Email, confirmationUrl }));

            this.TempData["Success"] = sent
                ? "Письмо отправлено. Откройте почту и перейдите по ссылке подтверждения."
                : "Запрос уже принят. Проверьте почту или дождитесь окончания отсчёта.";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Не удалось отправить подтверждение email пользователю {UserId}", user.Id);
            this.TempData["Error"] = "Не удалось отправить письмо. Попробуйте позже или обратитесь в поддержку.";
        }

        return this.RedirectToAction(nameof(Index));
    }
}
