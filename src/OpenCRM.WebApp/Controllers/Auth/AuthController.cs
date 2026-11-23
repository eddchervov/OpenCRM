using Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using OpenCRM.WebApp.Security;
using Microsoft.AspNetCore.Identity;
using OpenCRM.Services.Interfaces.Emails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Users.Dtos;
using OpenCRM.WebApp.Models.Auth;
using OpenCRM.WebApp.Infrastructure.Security;
using System.Security.Cryptography;
using System.Text;

namespace OpenCRM.WebApp.Controllers.Auth;

#nullable disable warnings

/// <summary> Контроллер аутентификации: вход, регистрация, пароли и подтверждение email. </summary>
[EnableRateLimiting(RateLimitPolicyNames.Authentication)]
public sealed class AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IEmailNotificationService emailNotifications,
        EmailConfirmationDelivery confirmationDelivery,
        IUsersService usersService,
        IPersonalSubscriptionQueryService subscriptionService,
        IConfiguration configuration,
        ILogger<AuthController> logger,
        IDataProtectionProvider dataProtectionProvider,
        IAntiforgery antiforgery,
        IAccountRegistrationService accountRegistrationService,
        IAccountDeletionService accountDeletionService,
        IEmailCodeLoginService emailCodeLoginService
    ) : Controller
{
    /// <summary> Имя cookie, сохраняющей данные исходной сессии при имперсонации. </summary>
    private const string ImpersonatorCookieName = "ImpersonatorId";

    private static readonly System.Resources.ResourceManager s_authResources =
        new("OpenCRM.WebApp.Resources.Infrastructure.Localization.LocalizationStrings", typeof(AuthController).Assembly);

    /// <summary> Защитник данных для шифрования cookie имперсонации. </summary>
    private readonly IDataProtector _impersonationProtector = dataProtectionProvider.CreateProtector("OpenCRM.Auth.Impersonation.v1");

    /// <summary> Отображает страницу входа. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [DisableRateLimiting]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        returnUrl = NormalizeReturnUrl(returnUrl);

        var model = new LoginModel
        {
            ErrorMessage = TempData["Error"]?.ToString(),
            ReturnUrl = returnUrl ?? Url.Content("~/")
        };

        return View(model);
    }

    /// <summary> Отображает страницу входа по коду из письма. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [DisableRateLimiting]
    public IActionResult EmailCodeLogin(string? returnUrl = null)
    {
        returnUrl = NormalizeReturnUrl(returnUrl);
        if (!EmailDeliveryPolicy.IsAvailable(configuration))
        {
            return this.RedirectToAction(nameof(Login), new { returnUrl });
        }

        return View(new EmailCodeLoginModel
        {
            ReturnUrl = returnUrl ?? Url.Content("~/")
        });
    }

    /// <summary> Отправляет код входа на email пользователя. </summary>
    [HttpPost]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicyNames.EmailDelivery)]
    public async Task<IActionResult> SendEmailLoginCode(EmailCodeLoginModel model)
    {
        model.ReturnUrl = NormalizeReturnUrl(model.ReturnUrl);
        model.Email = model.Email?.Trim();
        ModelState.Remove(nameof(EmailCodeLoginModel.Code));

        if (!ModelState.IsValid)
        {
            return View(nameof(EmailCodeLogin), model);
        }

        var result = await emailCodeLoginService.SendAsync(new EmailCodeLoginRequest(
            model.Email,
            model.Code,
            model.PersonalDataProcessingConsent,
            false));

        if (result.Errors.Count != 0)
        {
            if (result.RequiresRegistrationConsent)
            {
                model.RequiresRegistrationConsent = true;
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(result.ErrorField, error);
            }

            return View(nameof(EmailCodeLogin), model);
        }

        if (result.IsNewCodeSent)
        {
            ModelState.Clear();
            model.Code = null;
            model.RequiresRegistrationConsent = false;
        }

        model.CodeSent = result.CodeSent;
        model.StatusMessage = result.StatusMessage;

        return View(nameof(EmailCodeLogin), model);
    }

    /// <summary> Проверяет код входа и выполняет вход. </summary>
    [HttpPost]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyEmailLoginCode(EmailCodeLoginModel model)
    {
        model.ReturnUrl = NormalizeReturnUrl(model.ReturnUrl);
        model.Email = model.Email?.Trim();
        model.CodeSent = true;

        if (string.IsNullOrWhiteSpace(model.Code))
        {
            ModelState.AddModelError(nameof(EmailCodeLoginModel.Code), AuthController.Localize("auth.runtime.codeRequired"));
        }

        if (!ModelState.IsValid)
        {
            return View(nameof(EmailCodeLogin), model);
        }

        var result = await emailCodeLoginService.VerifyAsync(new EmailCodeLoginRequest(
            model.Email,
            model.Code,
            model.PersonalDataProcessingConsent,
            false), null);

        if (result.Errors.Count != 0)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(result.ErrorField, error);
            }

            return View(nameof(EmailCodeLogin), model);
        }

        var user = result.User;

        await signInManager.SignInAsync(user!, isPersistent: true);
        await subscriptionService.EnsureDefaultPlanAccessAsync(user!.Id);
        Response.Cookies.Delete(ImpersonatorCookieName);
        HttpContext.Items[nameof(HttpUserAccessInfoAccessor)] = true;
        await usersService.RecordLastLoginDate(user.Id, "email-code");

        return LocalRedirect(model.ReturnUrl);
    }

    /// <summary> Выполняет вход по email и паролю. </summary>
    [HttpPost]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginModel model)
    {
        model.ReturnUrl = NormalizeReturnUrl(model.ReturnUrl);

        if (ModelState.IsValid == false)
        {
            return View(model);
        }

        ApplicationUser userByEmail;

        try
        {
            userByEmail = await userManager.FindByEmailAsync(model.Email);
        }
        catch (Exception exception) when (IsDatabaseTimeout(exception))
        {
            logger.LogError(exception, "Таймаут базы данных при входе пользователя {Email}", model.Email);
            ModelState.AddModelError(
                string.Empty,
                AuthController.Localize("auth.runtime.unavailable"));

            return View(model);
        }

        if (userByEmail != null && userByEmail.IsDeleted)
        {
            ModelState.AddModelError(string.Empty, AuthController.Localize("auth.runtime.invalidAttempt"));
            return View(model);
        }

        if (userByEmail == null)
        {
            ModelState.AddModelError(string.Empty, AuthController.Localize("auth.runtime.invalidCredentials"));
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(
            userName: model.Email, // Используем email как username
            password: model.Password,
            isPersistent: true,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await subscriptionService.EnsureDefaultPlanAccessAsync(userByEmail.Id);
            Response.Cookies.Delete(ImpersonatorCookieName);
            HttpContext.Items[nameof(HttpUserAccessInfoAccessor)] = true;
            await usersService.RecordLastLoginDate(userByEmail.Id, "password");

            return userByEmail.EmailConfirmed || !EmailConfirmationPolicy.IsRequired(configuration)
                ? LocalRedirect(model.ReturnUrl)
                : Redirect(Url.Content($"~{EmailConfirmationMiddleware.ConfirmationPath}"));
        }

        var isPasswordCorrect = await userManager.CheckPasswordAsync(userByEmail, model.Password);
        if (!isPasswordCorrect)
        {
            ModelState.AddModelError(string.Empty, AuthController.Localize("auth.runtime.invalidCredentials"));
            return View(model);
        }

        if (result.IsLockedOut)
        {
            return RedirectToAction(nameof(Lockout));
        }

        ModelState.AddModelError(string.Empty, AuthController.Localize("auth.runtime.invalidAttempt"));
        return View(model);
    }

    /// <summary> Отображает страницу регистрации. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [DisableRateLimiting]
    public async Task<IActionResult> Register(string? returnUrl = null)
    {
        returnUrl = NormalizeReturnUrl(returnUrl);

        var model = new RegisterModel
        {
            ReturnUrl = returnUrl ?? Url.Content("~/")
        };

        return View(model);
    }

    /// <summary> Регистрирует нового пользователя по email и паролю. </summary>
    [HttpPost]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterModel model, string? returnUrl = null)
    {
        returnUrl = NormalizeReturnUrl(returnUrl);
        model.ReturnUrl = returnUrl;
        model.Email = model.Email?.Trim();
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        ValidateRegistrationConsents(model.PersonalDataProcessingConsent);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var activeUser = await userManager.FindByEmailAsync(model.Email?.Trim());
        if (activeUser != null && !activeUser.IsDeleted)
        {
            ModelState.AddModelError(string.Empty, AuthController.Localize("auth.runtime.emailExists"));
            return View(model);
        }

        if (await usersService.HasDeletedOriginalEmailAsync(model.Email))
        {
            ViewBag.PreviouslyDeleted = true;
            ViewBag.OriginalEmail = model.Email;
        }

        try
        {
            var creationResult = await accountRegistrationService.CreateAsync(new AccountRegistrationRequest(
                model.Email,
                model.Password,
                model.PersonalDataProcessingConsent,
                false,
                null));

            if (creationResult.Errors.Count != 0)
            {
                foreach (var error in creationResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error);
                }

                return View(model);
            }

            var user = creationResult.User;

            var confirmationRequired = EmailConfirmationPolicy.IsRequired(configuration);
            var confirmationEmailSent = !confirmationRequired;
            if (confirmationRequired)
            {
                confirmationEmailSent = true;
                try
                {
                    var code = await userManager.GenerateEmailConfirmationTokenAsync(user!);
                    var encodedCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Action(
                        action: "ConfirmEmail",
                        controller: "Auth",
                        values: new { userId = user!.Id, code = encodedCode },
                        protocol: Request.Scheme);

                    await confirmationDelivery.TrySendAsync(user!.Id, () => emailNotifications.QueueTemplateAsync(
                        model.Email, AuthController.Localize("auth.runtime.confirmSubject"), "system/email-confirmation",
                        new { name = model.Email, confirmationUrl = callbackUrl }));
                }
                catch (Exception ex)
                {
                    confirmationEmailSent = false;
                    logger.LogError(
                        ex,
                        "Аккаунт {UserId} создан, но подтверждение не отправлено на {Email}",
                        user!.Id,
                        model.Email);
                }
            }

            await signInManager.SignInAsync(user!, isPersistent: true);
            await subscriptionService.EnsureDefaultPlanAccessAsync(user!.Id);
            Response.Cookies.Delete(ImpersonatorCookieName);
            HttpContext.Items[nameof(HttpUserAccessInfoAccessor)] = true;
            await usersService.RecordLastLoginDate(user.Id, "registration-password");
            if (confirmationRequired)
            {
                TempData[confirmationEmailSent ? "Success" : "Error"] = confirmationEmailSent
                    ? AuthController.Localize("auth.runtime.createdEmailSent")
                    : AuthController.Localize("auth.runtime.createdEmailFailed");

                return Redirect(Url.Content($"~{EmailConfirmationMiddleware.ConfirmationPath}"));
            }

            TempData["Success"] = AuthController.Localize("auth.runtime.created");

            return LocalRedirect(model.ReturnUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при регистрации пользователя {Email}", model.Email);

            ModelState.AddModelError(
                string.Empty,
                AuthController.Localize("auth.runtime.registrationError"));

            return View(model);
        }
    }

    /// <summary> Выполняет выход пользователя из системы. </summary>
    [HttpPost]
    [AllowUnconfirmedEmail]
    public async Task<IActionResult> Logout(string? returnUrl = null)
    {
        var userId = userManager.GetUserId(User);
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException exception)
        {
            logger.LogWarning(exception,
                "Выход отклонён защитой запроса. UserId: {UserId}, TraceId: {TraceId}",
                userId, HttpContext.TraceIdentifier);

            return BadRequest();
        }

        returnUrl = NormalizeReturnUrl(returnUrl);

        Response.Cookies.Delete("phonenum");
        Response.Cookies.Delete(ImpersonatorCookieName);

        try
        {
            await signInManager.SignOutAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Не удалось завершить сеанс. UserId: {UserId}, TraceId: {TraceId}",
                userId, HttpContext.TraceIdentifier);

            throw;
        }

        logger.LogInformation("Сеанс завершён. UserId: {UserId}, TraceId: {TraceId}",
            userId, HttpContext.TraceIdentifier);

        return Redirect(returnUrl ?? Url.Content("~/"));
    }

    /// <summary> Отображает форму восстановления пароля. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [DisableRateLimiting]
    public IActionResult ForgotPassword()
    {
        if (!EmailDeliveryPolicy.IsAvailable(configuration))
        {
            return this.RedirectToAction(nameof(Login));
        }

        return View();
    }

    /// <summary> Отправляет письмо для восстановления пароля. </summary>
    [HttpPost]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicyNames.EmailDelivery)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordModel model)
    {
        if (ModelState.IsValid)
        {
            var user = await userManager.FindByEmailAsync(model.Email);
            if (user == null || user.IsDeleted)
            {
                return RedirectToAction("ForgotPasswordConfirmation");
            }

            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var callbackUrl = Url.Action(
                "ResetPassword",
                "Auth",
                new { userId = user.Id, code = encodedToken },
                protocol: Request.Scheme);

            try
            {
                await emailNotifications.QueueTemplateAsync(
                    model.Email, AuthController.Localize("auth.runtime.resetSubject"), "system/password-reset",
                    new { name = model.Email, resetUrl = callbackUrl });
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);

                model.StatusMessage = AuthController.Localize("auth.runtime.emailError");
                this.ViewData["StatusMessageIsError"] = true;

                return View(model);
            }

            return RedirectToAction("ForgotPasswordConfirmation");
        }

        return View(model);
    }

    /// <summary> Отображает форму установки нового пароля по коду. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    public async Task<IActionResult> ResetPassword(string userId, string code)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
        {
            return View("ResetPasswordInvalid");
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return View("ResetPasswordInvalid");
        }

        var model = new ResetPasswordModel
        {
            Email = user.Email,
            UserId = userId,
            Code = code // Оставляем токен закодированным!
        };

        return View(model);
    }

    /// <summary> Устанавливает новый пароль по коду восстановления. </summary>
    [HttpPost]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByIdAsync(model.UserId);
        if (user == null)
        {
            return RedirectToAction("ResetPasswordConfirmation");
        }

        // Декодируем токен здесь
        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Code));
        }
        catch
        {
            model.StatusMessage = AuthController.Localize("auth.runtime.resetLinkInvalid");
            return View(model);
        }

        var result = await userManager.ResetPasswordAsync(user, decodedToken, model.Password);
        if (result.Succeeded)
        {
            return RedirectToAction("ResetPasswordConfirmation");
        }

        if (result.Errors.Any())
        {
            ModelState.AddModelError(string.Empty, AuthController.Localize("auth.runtime.resetError"));

            logger.LogError(string.Join(";", result.Errors.Select(x => $"code: {x.Code}: {x.Description}")));
        }

        model.Email = user.Email;
        return View(model);
    }

    /// <summary> Отображает страницу подтверждения смены пароля. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    public IActionResult ResetPasswordConfirmation()
    {
        return View();
    }

    /// <summary> Отображает страницу подтверждения регистрации. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    public IActionResult RegisterConfirmation(string email, bool deliveryFailed = false)
    {
        ViewData["Email"] = email;
        ViewData["DeliveryFailed"] = deliveryFailed;
        return View();
    }

    /// <summary> Повторно отправляет письмо с подтверждением email. </summary>
    [HttpPost]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicyNames.EmailDelivery)]
    public async Task<IActionResult> ResendEmailConfirmation(string email)
    {
        var deliveryFailed = false;
        var normalizedEmail = email?.Trim();
        if (!EmailAddressValidation.TryValidate(normalizedEmail, out var validationError))
        {
            TempData["EmailValidationError"] = validationError;
            return RedirectToAction(
                "RegisterConfirmation",
                new { email = normalizedEmail, deliveryFailed = true });
        }

        var user = string.IsNullOrWhiteSpace(normalizedEmail)
            ? null
            : await userManager.FindByEmailAsync(normalizedEmail);

        if (user != null && !await userManager.IsEmailConfirmedAsync(user))
        {
            try
            {
                var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
                var encodedCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Action(
                    action: "ConfirmEmail",
                    controller: "Auth",
                    values: new { userId = user.Id, code = encodedCode },
                    protocol: Request.Scheme);

                await confirmationDelivery.TrySendAsync(user.Id, () => emailNotifications.QueueTemplateAsync(
                    normalizedEmail!, AuthController.Localize("auth.runtime.confirmSubject"), "system/email-confirmation",
                    new { name = normalizedEmail!, confirmationUrl = callbackUrl }));
            }
            catch (Exception ex)
            {
                deliveryFailed = true;
                logger.LogError(
                    ex,
                    "Повторное подтверждение не отправлено пользователю {UserId} на {Email}",
                    user.Id,
                    normalizedEmail);
            }
        }

        return RedirectToAction(
            "RegisterConfirmation",
            new { email = normalizedEmail, deliveryFailed });
    }

    /// <summary> Отображает страницу подтверждения отправки письма восстановления. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }

    /// <summary> Отображает страницу блокировки входа. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    public IActionResult Lockout()
    {
        return View();
    }

    /// <summary> Подтверждает email пользователя по коду. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    public async Task<IActionResult> ConfirmEmail(string userId, string code)
    {
        if (userId == null || code == null)
        {
            return View(new ConfirmEmailModel
            {
                Succeeded = false,
                StatusMessage = AuthController.Localize("auth.runtime.confirmationInvalid")
            });
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userId}'.");
        }

        try
        {
            code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch
        {
            return View(new ConfirmEmailModel
            {
                Succeeded = false,
                StatusMessage = AuthController.Localize("auth.runtime.confirmationExpired")
            });
        }

        var result = await userManager.ConfirmEmailAsync(user, code);

        var model = new ConfirmEmailModel
        {
            Succeeded = result.Succeeded,
            StatusMessage = result.Succeeded ? AuthController.Localize("auth.runtime.emailConfirmed") : AuthController.Localize("auth.runtime.confirmationError")
        };

        return View(model);
    }

    /// <summary> Подтверждает смену email пользователя по коду. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    public async Task<IActionResult> ConfirmEmailChange(string userId, string email, string code)
    {
        if (userId == null || email == null || code == null)
        {
            return View(new ConfirmEmailChangeModel
            {
                StatusMessage = AuthController.Localize("auth.runtime.changeEmailInvalid")
            });
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userId}'.");
        }

        var model = new ConfirmEmailChangeModel
        {
            StatusMessage = AuthController.Localize("auth.runtime.emailChanged")
        };

        try
        {
            code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch
        {
            model.StatusMessage = AuthController.Localize("auth.runtime.changeEmailExpired");
            return View(model);
        }

        var result = await userManager.ChangeEmailAsync(user, email, code);
        if (!result.Succeeded)
        {
            model.StatusMessage = AuthController.Localize("auth.runtime.changeEmailError");

            return View(model);
        }

        // В нашем пользовательском интерфейсе адрес электронной почты и имя пользователя — это одно и то же, поэтому при обновлении адреса электронной почты
        // нам нужно обновить и имя пользователя.
        var setUserNameResult = await userManager.SetUserNameAsync(user, email);
        if (!setUserNameResult.Succeeded)
        {
            model.StatusMessage = AuthController.Localize("auth.runtime.changeUsernameError");

            return View(model);
        }

        await signInManager.RefreshSignInAsync(user);

        model.Succeeded = true;

        return View(model);
    }

    /// <summary> Выполняет вход от имени пользователя для администратора. </summary>
    [Authorize(Policy = AdminAuthorizationPolicies.ImpersonateUsers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImpersonateUser([FromBody] ImpersonateUserRequest request)
    {
        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser == null)
        {
            return BadRequest(new { success = false, message = AuthController.Localize("auth.runtime.currentUserMissing") });
        }

        var targetUser = await userManager.FindByIdAsync(request.UserId);
        if (targetUser == null)
        {
            return BadRequest(new { success = false, message = AuthController.Localize("auth.runtime.targetUserMissing") });
        }

        if (targetUser.IsDeleted)
        {
            return BadRequest(new { success = false, message = AuthController.Localize("auth.runtime.deletedUser") });
        }

        if (targetUser.Id == currentUser.Id)
        {
            return BadRequest(new { success = false, message = AuthController.Localize("auth.runtime.alreadySignedInAsUser") });
        }

        // Сохраняем оригинального пользователя для возможности возврата
        var originalUserId = currentUser.Id;

        // Выходим из текущей сессии
        await signInManager.SignOutAsync();

        // Входим как целевой пользователь
        await signInManager.SignInAsync(targetUser, isPersistent: false);

        // Сохраняем информацию об имперсонации в куки
        Response.Cookies.Append(ImpersonatorCookieName, this._impersonationProtector.Protect(originalUserId), new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.Now.AddHours(2)
        });

        return Ok(new { success = true, message = AuthController.Localize("auth.runtime.impersonationSuccess") });
    }

    /// <summary> Завершает сессию имперсонации и возвращает исходного пользователя. </summary>
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowUnconfirmedEmail]
    public async Task<IActionResult> StopImpersonation()
    {
        var protectedImpersonatorId = Request.Cookies[ImpersonatorCookieName];
        if (string.IsNullOrEmpty(protectedImpersonatorId))
        {
            return BadRequest(new { success = false, message = AuthController.Localize("auth.runtime.noImpersonation") });
        }

        string impersonatorId;
        try
        {
            impersonatorId = this._impersonationProtector.Unprotect(protectedImpersonatorId);
        }
        catch (CryptographicException)
        {
            Response.Cookies.Delete(ImpersonatorCookieName);

            return BadRequest(new { success = false, message = AuthController.Localize("auth.runtime.impersonationExpired") });
        }

        var currentUser = await userManager.GetUserAsync(User);
        var impersonator = await userManager.FindByIdAsync(impersonatorId);

        if (impersonator == null)
        {
            return BadRequest(new { success = false, message = AuthController.Localize("auth.runtime.adminMissing") });
        }

        await signInManager.SignOutAsync();

        await signInManager.SignInAsync(impersonator, isPersistent: false);

        Response.Cookies.Delete(ImpersonatorCookieName);

        return Ok(new { success = true, message = AuthController.Localize("auth.runtime.returnedToAdmin") });
    }

    /// <summary> Отображает страницу удаления аккаунта. </summary>
    [Authorize]
    [HttpGet]
    [AllowUnconfirmedEmail]
    public async Task<IActionResult> DeleteAccount()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToAction("Login");
        }

        return View(new DeleteAccountModel
        {
            HasPassword = await userManager.HasPasswordAsync(user)
        });
    }

    /// <summary> Обрабатывает запрос на удаление аккаунта пользователя. </summary>
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowUnconfirmedEmail]
    public async Task<IActionResult> DeleteAccount(DeleteAccountModel model)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound(AuthController.Localize("auth.runtime.userLoadError"));
        }

        model.HasPassword = await userManager.HasPasswordAsync(user);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!model.ConfirmDeletion)
        {
            ModelState.AddModelError(string.Empty, AuthController.Localize("auth.runtime.confirmDeletion"));
            return View(model);
        }

        if (model.HasPassword)
        {
            var passwordValid = await userManager.CheckPasswordAsync(user, model.Password);
            if (!passwordValid)
            {
                ModelState.AddModelError(string.Empty, AuthController.Localize("auth.runtime.invalidPassword"));
                return View(model);
            }
        }

        try
        {
            var originalEmail = await accountDeletionService.DeleteAsync(user);

            await signInManager.SignOutAsync();

            return RedirectToAction("DeleteAccountConfirmation", new
            {
                wasDeleted = true,
                originalEmail
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при удалении аккаунта пользователя {UserId}", user.Id);

            ModelState.AddModelError(string.Empty, AuthController.Localize("auth.runtime.deleteError"));

            return View(model);
        }
    }

    /// <summary> Отображает страницу подтверждения удаления аккаунта. </summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowUnconfirmedEmail]
    public IActionResult DeleteAccountConfirmation(bool wasDeleted = false, string? originalEmail = null)
    {
        var model = new DeleteAccountConfirmationModel
        {
            WasDeleted = wasDeleted,
            OriginalEmail = originalEmail,
            Message = wasDeleted
                ? AuthController.Localize("auth.runtime.deleted")
                : AuthController.Localize("auth.runtime.tryAgain")
        };

        return View(model);
    }

    /// <summary> Проверяет согласия, необходимые при регистрации. </summary>
    private void ValidateRegistrationConsents(bool personalDataProcessingConsent)
    {
        if (!personalDataProcessingConsent)
        {
            ModelState.AddModelError(
                nameof(RegisterModel.PersonalDataProcessingConsent),
                AuthController.Localize("auth.runtime.consentRequired"));
        }
    }

    /// <summary> Приводит адрес возврата к безопасному локальному значению. </summary>
    private string NormalizeReturnUrl(string? returnUrl)
    {
        var fallbackUrl = Url.Content("~/") ?? "/";
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return fallbackUrl;
        }

        if (!Url.IsLocalUrl(returnUrl))
        {
            return fallbackUrl;
        }

        var normalizedPath = returnUrl.Split('?', '#')[0].Trim().ToLowerInvariant();
        if (normalizedPath == "/auth/login"
            || normalizedPath == "/auth/register"
            || normalizedPath == "/auth/externallogin"
            || normalizedPath == "/auth/externallogincallback")
        {
            return fallbackUrl;
        }

        return returnUrl;
    }

    /// <summary> Проверяет, является ли исключение таймаутом базы данных. </summary>
    private static bool IsDatabaseTimeout(Exception exception)
    {
        for (var currentException = exception; currentException != null; currentException = currentException.InnerException)
        {
            if (currentException is TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private static string Localize(string key) =>
        AuthController.s_authResources.GetString(key, System.Globalization.CultureInfo.CurrentUICulture) ?? key;
}
