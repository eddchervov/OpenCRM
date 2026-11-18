using OpenCRM.WebApp.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Home;
using OpenCRM.Services.Interfaces.Home.Dtos;
using OpenCRM.WebApp.Models.Personal;
using OpenCRM.WebApp.Infrastructure;
using OpenCRM.Modules.Support.Personal;
using System.Globalization;
using System.Resources;
using OpenCRM.WebApp.Infrastructure.Localization;

namespace OpenCRM.WebApp.Controllers.Personal;

/// <summary> Контроллер обращений в поддержку пользователя. </summary>
[Authorize]
[Route("personal/support")]
[AllowUnconfirmedEmail]
public class SupportController(
    UserManager<ApplicationUser> userManager,
    ISupportMessageService supportMessageService) : Controller
{
    private static readonly ResourceManager s_resources = new(
        "OpenCRM.WebApp.Resources.Infrastructure.Localization.LocalizationStrings",
        typeof(LocalizationStrings).Assembly);

    /// <summary> Отображает список обращений в поддержку или конкретное обращение. </summary>
    [HttpGet]
    public async Task<IActionResult> Index(int? id = null, CancellationToken cancellationToken = default)
    {
        var userId = userManager.GetUserId(this.User);
        if (userId == null)
        {
            return this.Challenge();
        }

        var inbox = await supportMessageService.GetInboxAsync(userId, id, cancellationToken);

        var model = new SupportViewModel
        {
            Organizations = inbox.Organizations,
            Conversations = inbox.Conversations,
            Selected = inbox.Selected,
            Error = this.TempData["SupportError"] as string
        };

        return this.View("/Views/Personal/Support.cshtml", model);
    }

    /// <summary> Отображает отдельную форму нового обращения в поддержку. </summary>
    [HttpGet("create")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(this.User);
        if (userId == null)
        {
            return this.Challenge();
        }

        return await this.CreateViewAsync(
            userId,
            new CreateSupportRequest { TypeCode = SupportMessageTypeCodes.Question },
            null,
            cancellationToken);
    }

    /// <summary> Создаёт новое обращение в поддержку. </summary>
    [HttpPost("create")]
    [RequestSizeLimit(SupportAttachmentRules.MaximumRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = SupportAttachmentRules.MaximumRequestSize)]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.ContactForm)]
    public async Task<IActionResult> Create(CreateSupportRequest request, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(this.User);
        if (userId == null)
        {
            return this.Challenge();
        }

        if (!this.ModelState.IsValid || !SupportMessageTypeCodes.All.Contains(request.TypeCode) ||
            string.IsNullOrWhiteSpace(request.Topic) || request.Topic.Trim().Length > 180 ||
            string.IsNullOrWhiteSpace(request.Text) || request.Text.Trim().Length > 4000)
        {
            return await this.CreateViewAsync(userId, request,
                SupportController.Localize("personal.support.error.invalidRequest"), cancellationToken);
        }

        if (request.IsOrganizationQuestion && request.OrganizationId == null)
        {
            return await this.CreateViewAsync(userId, request,
                SupportController.Localize("personal.support.error.selectOrganization"), cancellationToken);
        }

        if (request.IsOrganizationQuestion && !await supportMessageService.HasOrganizationAccessAsync(
            userId, request.OrganizationId!.Value, cancellationToken))
        {
            return await this.CreateViewAsync(userId, request,
                SupportController.Localize("personal.support.error.organizationUnavailable"), cancellationToken);
        }

        IReadOnlyList<SupportAttachmentUploadDto> attachments;
        try
        {
            attachments = await SupportAttachmentUploadReader.ReadAsync(request.Attachments, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await this.CreateViewAsync(userId, request, SupportController.LocalizeAttachmentError(exception.Message), cancellationToken);
        }

        var user = await userManager.FindByIdAsync(userId);
        var result = await supportMessageService.CreateAsync(new SupportMessageCreateDto
        {
            UserId = userId,
            Name = user?.UserName,
            Email = user?.Email,
            TypeCode = request.TypeCode,
            OrganizationId = request.IsOrganizationQuestion ? request.OrganizationId : null,
            Topic = request.Topic.Trim(),
            Text = request.Text.Trim(),
            Attachments = attachments,
            SourcePage = "/personal/support/create",
            ClientIp = this.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent = this.Request.Headers.UserAgent.ToString()
        });

        if (!result.Succeeded)
        {
            return await this.CreateViewAsync(userId, request,
                SupportController.LocalizeKnownError(result.ErrorMessage ?? "Не удалось создать обращение."), cancellationToken);
        }

        return this.RedirectToAction(nameof(Index), new { id = result.MessageId });
    }

    /// <summary> Отправляет ответ на обращение в поддержку. </summary>
    [HttpPost("reply")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimitPolicyNames.ConversationSend)]
    [RequestSizeLimit(SupportAttachmentRules.MaximumRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = SupportAttachmentRules.MaximumRequestSize)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(ReplySupportRequest request, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(this.User);
        if (userId == null)
        {
            return this.Challenge();
        }

        if (!this.ModelState.IsValid)
        {
            this.TempData["SupportError"] = SupportController.Localize("personal.support.error.invalidReply");

            return this.RedirectToAction(nameof(Index), new { id = request.Id });
        }

        SupportMessageReplyResult result;
        try
        {
            var attachments = await SupportAttachmentUploadReader.ReadAsync(request.Attachments, cancellationToken);
            result = attachments.Count == 0
                ? await supportMessageService.ReplyAsync(userId, request.Id, request.Text, cancellationToken)
                : await supportMessageService.ReplyAsync(userId, request.Id, request.Text, cancellationToken, attachments);
        }
        catch (ArgumentException exception)
        {
            this.TempData["SupportError"] = string.Format(
                CultureInfo.CurrentCulture,
                SupportController.Localize("personal.support.error.replyAttachmentRetry"),
                SupportController.LocalizeAttachmentError(exception.Message));

            return this.RedirectToAction(nameof(Index), new { id = request.Id });
        }

        if (result == SupportMessageReplyResult.NotFound)
        {
            return this.NotFound();
        }

        if (result == SupportMessageReplyResult.Forbidden)
        {
            return this.Forbid();
        }

        if (result == SupportMessageReplyResult.InvalidText)
        {
            this.TempData["SupportError"] = SupportController.Localize("personal.support.error.invalidReplyText");
        }

        return this.RedirectToAction(nameof(Index), new { id = request.Id });
    }

    /// <summary> Скачивает вложение только из обращения текущего пользователя. </summary>
    [HttpGet("attachments/{id:guid}")]
    public async Task<IActionResult> Download(
        Guid id,
        [FromServices] ISupportAttachmentService attachments,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(this.User);
        if (userId == null)
        {
            return this.Challenge();
        }

        var file = await attachments.GetForUserAsync(userId, id, cancellationToken);
        if (file == null)
        {
            return this.NotFound();
        }

        this.Response.Headers.CacheControl = "no-store";
        this.Response.Headers.XContentTypeOptions = "nosniff";

        return this.File(file.Content, "application/octet-stream", file.FileName);
    }

    private async Task<IActionResult> CreateViewAsync(
        string userId,
        CreateSupportRequest request,
        string? error,
        CancellationToken cancellationToken)
    {
        var inbox = await supportMessageService.GetInboxAsync(userId, null, cancellationToken);

        return this.View("/Views/Personal/CreateSupport.cshtml", new CreateSupportViewModel
        {
            Organizations = inbox.Organizations,
            Request = request,
            Error = error
        });
    }

    private static string Localize(string key) =>
        SupportController.s_resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private static string LocalizeKnownError(string message) => message switch
    {
        "Выбранная организация недоступна." => SupportController.Localize("personal.support.error.organizationUnavailable"),
        "Слишком много сообщений за короткое время. Попробуйте позже." => SupportController.Localize("personal.support.error.rateLimited"),
        "Не удалось создать обращение." => SupportController.Localize("personal.support.error.createFailed"),
        _ => message
    };

    private static string LocalizeAttachmentError(string message) => message switch
    {
        "Укажите имя файла длиной до 200 символов без управляющих символов." => SupportController.Localize("personal.support.attachments.invalidName"),
        "К одному сообщению можно прикрепить не более 5 файлов." or "Можно прикрепить не более 5 файлов." => SupportController.Localize("personal.support.attachments.serverFileCount"),
        "Выберите непустые файлы: до 10 МБ каждый и до 20 МБ суммарно." => SupportController.Localize("personal.support.attachments.serverFileSize"),
        "Допустимы изображения JPEG, PNG, WebP, PDF и текстовые файлы TXT, LOG. Содержимое должно соответствовать формату." => SupportController.Localize("personal.support.attachments.serverFileFormat"),
        "Размер файла не соответствует запросу." => SupportController.Localize("personal.support.attachments.sizeMismatch"),
        _ => message
    };
}
