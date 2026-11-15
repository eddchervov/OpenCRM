using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using OpenCRM.Services.Interfaces.Emails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Navigation;
using OpenCRM.WebApp.Product;
using OpenCRM.WebApp.Models.Personal;
using System.Text;

namespace OpenCRM.WebApp.Controllers.Personal;

/// <summary> Контроллер личного кабинета: профиль, пароль, приглашения и организация. </summary>
[Authorize]
public class PersonalController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IEmailNotificationService emailSender,
    EmailConfirmationDelivery confirmationDelivery,
    IUsersService usersService,
    ILogger<PersonalController> logger,
    IOrganizationInvitationQueryService invitationQueries,
    IOrganizationInvitationCommandService invitationCommands) : Controller
{
    /// <summary> Отображает главную страницу личного кабинета. </summary>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        await usersService.RecordLastDateOfEntry(user.Id);
        var personalStats = await usersService.GetPersonalStatsAsync(user.Id);

        var model = new IndexModel
        {
            CountVehicles = personalStats.VehiclesCount,
            CountMaintenance = personalStats.MaintenanceCount,
            Username = await userManager.GetUserNameAsync(user),
            PhoneNumber = await userManager.GetPhoneNumberAsync(user),
            Email = await userManager.GetEmailAsync(user),
            DatetimeCreation = user.DatetimeCreation,
            IsEmailConfirmed = user.EmailConfirmed,
            StatusMessage = TempData["Success"]?.ToString() ?? TempData["Error"]?.ToString()
        };

        return View(model);
    }

    /// <summary> Отображает страницу профиля аккаунта. </summary>
    [HttpGet]
    public async Task<IActionResult> AccountProfile()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        var model = new AccountProfileViewModel
        {
            OrganizationInvitations = await invitationQueries.GetPendingInvitationsAsync(user.Id),
            StatusMessage = TempData["Success"]?.ToString() ?? TempData["Error"]?.ToString()
        };

        return View(model);
    }

    /// <summary> Принимает приглашение в организацию. </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptOrganizationInvitation(Guid invitationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        try
        {
            var pendingInvitations = await invitationQueries.GetPendingInvitationsAsync(user.Id);
            var invitation = pendingInvitations.FirstOrDefault(x => x.InvitationId == invitationId)
                ?? throw new InvalidOperationException("Приглашение не найдено.");

            await invitationCommands.AcceptInvitationAsync(user.Id, invitationId);

            TempData["Success"] = "Приглашение принято.";
            return Redirect(OrganizationWorkspaceUrlHelper.Resolve(invitation.OrganizationId, invitation.OrganizationScenarioCode));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to accept organization invitation {InvitationId} for user {UserId}", invitationId, user.Id);
            TempData["Error"] = ex.Message;
            return RedirectBackAfterInvitationAction();
        }
    }

    /// <summary> Отклоняет приглашение в организацию. </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclineOrganizationInvitation(Guid invitationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        try
        {
            await invitationCommands.DeclineInvitationAsync(user.Id, invitationId);
            TempData["Success"] = "Приглашение отклонено.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to decline organization invitation {InvitationId} for user {UserId}", invitationId, user.Id);
            TempData["Error"] = ex.Message;
        }

        return RedirectBackAfterInvitationAction();
    }

    /// <summary> Отображает страницу команды. </summary>
    [HttpGet]
    public IActionResult Team()
    {
        return Redirect("/organizations");
    }

    /// <summary> Создаёт организацию из личного кабинета. </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CreateOrganization([Bind(Prefix = "CreateOrganization")] CreateOrganizationModel model)
    {
        return Redirect("/organizations");
    }

    /// <summary> Отправляет приглашение участнику организации. </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult InviteOrganizationMember(InviteOrganizationMemberModel model)
    {
        return Redirect("/organizations");
    }

    /// <summary> Изменяет роль участника организации. </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateOrganizationMemberRole(UpdateOrganizationMemberRoleModel model)
    {
        return Redirect("/organizations");
    }

    /// <summary> Удаляет участника из организации. </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveOrganizationMember(Guid organizationId, Guid memberId)
    {
        return Redirect("/organizations");
    }

    /// <summary> Отзывает приглашение в организацию. </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RevokeOrganizationInvitation(Guid organizationId, Guid invitationId)
    {
        return Redirect("/organizations");
    }

    /// <summary> Отображает форму установки пароля. </summary>
    [HttpGet]
    public async Task<IActionResult> SetPassword()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        if (await userManager.HasPasswordAsync(user))
        {
            return RedirectToAction(nameof(ChangePassword));
        }

        return View();
    }

    /// <summary> Устанавливает пароль аккаунта. </summary>
    [HttpPost]
    public async Task<IActionResult> SetPassword(SetPasswordModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        var addPasswordResult = await userManager.AddPasswordAsync(user, model.NewPassword);
        if (!addPasswordResult.Succeeded)
        {
            foreach (var error in addPasswordResult.Errors)
            {
                ModelState.AddModelError("Password", error.Description);
            }

            return View(model);
        }

        await signInManager.RefreshSignInAsync(user);

        return RedirectToAction(nameof(ChangePassword), new
        {
            statusMessage = "Ваш пароль установлен."
        });
    }

    /// <summary> Отображает форму смены пароля. </summary>
    [HttpGet]
    public async Task<IActionResult> ChangePassword(string? statusMessage)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        if (!await userManager.HasPasswordAsync(user))
        {
            return RedirectToAction(nameof(SetPassword));
        }

        return View(new ChangePasswordModel
        {
            StatusMessage = statusMessage
        });
    }

    /// <summary> Выполняет смену пароля. </summary>
    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        if (model.OldPassword == model.NewPassword)
        {
            ModelState.AddModelError(string.Empty, "Старый и новый пароль совпадают.");
            return View(model);
        }

        var changePasswordResult = await userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
        if (!changePasswordResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Неверный пароль.");
            return View(model);
        }

        await signInManager.RefreshSignInAsync(user);

        return RedirectToAction(nameof(ChangePassword), new
        {
            statusMessage = "Ваш пароль был изменен."
        });
    }

    /// <summary> Отправляет письмо для подтверждения email. </summary>
    [HttpPost]
    public async Task<IActionResult> SendVerificationEmail()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Пользователь с ID '{userManager.GetUserId(User)}' не найден.");
        }

        var email = await userManager.GetEmailAsync(user);
        if (!EmailAddressValidation.TryValidate(email, out var validationError))
        {
            logger.LogWarning(
                "Письмо подтверждения не отправлено пользователю {UserId}: некорректный email {Email}",
                user.Id,
                email);
            TempData["Error"] = $"Письмо не отправлено. {validationError}";
            return RedirectToAction(nameof(Index));
        }

        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

        var callbackUrl = Url.Action(
            action: "ConfirmEmail",
            controller: "Auth",
            values: new { userId = user.Id, code },
            protocol: Request.Scheme);

        try
        {
            await confirmationDelivery.TrySendAsync(user.Id, () => emailSender.QueueTemplateAsync(
                email!, "Подтвердите ваш email", "system/email-confirmation",
                new { name = email!, confirmationUrl = callbackUrl }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при отправке письма подтверждения email пользователю {UserId}", user.Id);
            TempData["Error"] = "Произошла ошибка отправки email. Попробуйте позже или обратитесь в поддержку.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = "Письмо с подтверждением отправлено. Проверьте вашу почту.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary> Отображает страницу подписки пользователя. </summary>
    [HttpGet]
    public IActionResult Subscription()
    {
        return this.Redirect("/organizations");
    }

    /// <summary> Выполняет возврат на предыдущую страницу после действия с приглашением. </summary>
    private IActionResult RedirectBackAfterInvitationAction()
    {
        var referer = Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            var localUrl = string.Concat(refererUri.PathAndQuery, refererUri.Fragment);
            if (Url.IsLocalUrl(localUrl))
            {
                return Redirect(localUrl);
            }
        }

        return RedirectToAction(nameof(AccountProfile));
    }
}
