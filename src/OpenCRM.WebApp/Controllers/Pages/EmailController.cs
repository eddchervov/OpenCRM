using OpenCRM.Services.Interfaces.Emails.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.WebApp.Models.Emails;

namespace OpenCRM.WebApp.Controllers.Pages;

/// <summary> Контроллер обработки отписки от email-уведомлений. </summary>
public sealed class EmailController(
    IEmailUnsubscribeService unsubscribeService,
    ILogger<EmailController> logger) : Controller
{
    /// <summary> Обрабатывает переход по ссылке отписки от email-рассылки по токену. </summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("email/unsubscribe")]
    public async Task<IActionResult> Unsubscribe(
        string userId,
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        var result = await unsubscribeService.UnsubscribeAsync(userId, email, token, cancellationToken);
        if (result == EmailUnsubscribeResult.InvalidLink)
        {
            logger.LogWarning("Invalid email unsubscribe link for {Email}", email);
            return View(new UnsubscribeViewModel
            {
                Title = "Ссылка недействительна",
                Message = "Не удалось подтвердить ссылку отписки. Если письма продолжают приходить, напишите нам в поддержку.",
                Email = email,
                Succeeded = false
            });
        }

        if (result == EmailUnsubscribeResult.SubscriptionNotFound)
        {
            return View(new UnsubscribeViewModel
            {
                Title = "Отписка уже не требуется",
                Message = "Мы не нашли активную подписку на рекламные сообщения для этого адреса.",
                Email = email,
                Succeeded = true
            });
        }

        return View(new UnsubscribeViewModel
        {
            Title = "Вы отписаны от рассылки",
            Message = "Мы больше не будем отправлять рекламные письма на этот адрес. Сервисные письма, например для входа или восстановления пароля, продолжат приходить.",
            Email = email,
            Succeeded = true
        });
    }

}
