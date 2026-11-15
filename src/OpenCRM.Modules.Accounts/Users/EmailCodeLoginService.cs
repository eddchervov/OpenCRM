using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.Modules.Accounts.Users;

/// <summary> Сервис входа по коду из письма. </summary>
public sealed class EmailCodeLoginService(
    UserManager<ApplicationUser> userManager,
    IUsersService usersService,
    IEmailNotificationService emailNotifications,
    IAccountRegistrationService accountRegistrationService,
    IMemoryCache memoryCache,
    ILogger<EmailCodeLoginService> logger,
    TimeProvider? timeProvider = null,
    Helpers.ProjectBrand? brand = null) : IEmailCodeLoginService
{
    /// <summary> Длина кода входа. </summary>
    private const int EmailLoginCodeLength = 8;

    /// <summary> Максимальное число попыток ввода кода. </summary>
    private const int EmailLoginCodeMaxAttempts = 5;

    /// <summary> Время жизни кода входа. </summary>
    private static readonly TimeSpan s_emailLoginCodeLifetime = TimeSpan.FromHours(1);

    /// <summary> Зависимость источника времени. </summary>
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary> Отправляет код входа на email. </summary>
    public async Task<EmailCodeLoginResult> SendAsync(EmailCodeLoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user != null && user.IsDeleted)
        {
            return new EmailCodeLoginResult { Errors = ["Этот аккаунт недоступен для входа."] };
        }

        if (user != null && await userManager.IsLockedOutAsync(user))
        {
            return new EmailCodeLoginResult { Errors = ["Этот аккаунт временно заблокирован."] };
        }

        if (user == null && await usersService.HasDeletedOriginalEmailAsync(request.Email))
        {
            return new EmailCodeLoginResult { Errors = ["Этот аккаунт недоступен для регистрации."] };
        }

        if (user == null && !request.PersonalDataProcessingConsent)
        {
            return new EmailCodeLoginResult
            {
                RequiresRegistrationConsent = true,
                ErrorField = nameof(request.PersonalDataProcessingConsent),
                Errors = ["Для создания аккаунта необходимо согласие на обработку персональных данных."]
            };
        }

        var normalizedEmail = EmailCodeLoginService.NormalizeEmailForCode(request.Email);
        if (memoryCache.TryGetValue(EmailCodeLoginService.GetEmailLoginCodeCacheKey(normalizedEmail), out EmailLoginCodeCacheEntry? existingEntry)
            && existingEntry?.SentAt.AddMinutes(1) > this._timeProvider.GetUtcNow())
        {
            return new EmailCodeLoginResult
            {
                CodeSent = true,
                StatusMessage = "Код уже отправлен. Повторно запросить письмо можно через минуту."
            };
        }

        var code = EmailCodeLoginService.GenerateEmailLoginCode();
        var expiresAt = this._timeProvider.GetUtcNow().Add(EmailCodeLoginService.s_emailLoginCodeLifetime);
        var cacheEntry = new EmailLoginCodeCacheEntry
        {
            Email = request.Email,
            CodeHash = EmailCodeLoginService.HashEmailLoginCode(normalizedEmail, code),
            SentAt = this._timeProvider.GetUtcNow(),
            ExpiresAt = expiresAt,
            IsRegistration = user == null,
            PersonalDataProcessingConsent = request.PersonalDataProcessingConsent,
            AdvertisingConsent = request.AdvertisingConsent
        };

        memoryCache.Set(EmailCodeLoginService.GetEmailLoginCodeCacheKey(normalizedEmail), cacheEntry, expiresAt);

        try
        {
            await emailNotifications.QueueTemplateAsync(
                request.Email, $"Код входа в {brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName}", "system/login-code", new { code });
        }
        catch (Exception ex)
        {
            memoryCache.Remove(EmailCodeLoginService.GetEmailLoginCodeCacheKey(normalizedEmail));
            logger.LogError(ex, "Ошибка при отправке кода входа на {Email}", request.Email);

            return new EmailCodeLoginResult
            {
                Errors = ["Не удалось отправить код. Попробуйте позже или обратитесь в поддержку."]
            };
        }

        return new EmailCodeLoginResult
        {
            CodeSent = true,
            IsNewCodeSent = true,
            StatusMessage = "Мы отправили 8-значный код на указанный email."
        };
    }

    /// <summary> Проверяет код входа и выполняет вход или регистрацию. </summary>
    public async Task<EmailCodeLoginResult> VerifyAsync(
        EmailCodeLoginRequest request,
        RegistrationAttributionData? attribution)
    {
        var normalizedEmail = EmailCodeLoginService.NormalizeEmailForCode(request.Email);
        var cacheKey = EmailCodeLoginService.GetEmailLoginCodeCacheKey(normalizedEmail);
        if (!memoryCache.TryGetValue(cacheKey, out EmailLoginCodeCacheEntry? cacheEntry)
            || cacheEntry == null
            || cacheEntry.ExpiresAt <= this._timeProvider.GetUtcNow())
        {
            memoryCache.Remove(cacheKey);

            return new EmailCodeLoginResult { Errors = ["Код устарел. Запросите новый код."] };
        }

        if (cacheEntry.Attempts >= EmailLoginCodeMaxAttempts)
        {
            memoryCache.Remove(cacheKey);

            return new EmailCodeLoginResult { Errors = ["Слишком много попыток. Запросите новый код."] };
        }

        cacheEntry.Attempts++;
        if (!EmailCodeLoginService.VerifyEmailLoginCodeHash(normalizedEmail, request.Code!, cacheEntry.CodeHash))
        {
            return new EmailCodeLoginResult { Errors = ["Неверный код."] };
        }

        var user = await userManager.FindByEmailAsync(cacheEntry.Email);
        if (user != null && user.IsDeleted)
        {
            memoryCache.Remove(cacheKey);

            return new EmailCodeLoginResult { Errors = ["Этот аккаунт недоступен для входа."] };
        }

        if (user != null && await userManager.IsLockedOutAsync(user))
        {
            memoryCache.Remove(cacheKey);

            return new EmailCodeLoginResult { Errors = ["Этот аккаунт временно заблокирован."] };
        }

        var accountCreated = user == null;
        if (user == null)
        {
            if (!cacheEntry.IsRegistration || !cacheEntry.PersonalDataProcessingConsent)
            {
                memoryCache.Remove(cacheKey);

                return new EmailCodeLoginResult { Errors = ["Аккаунт не найден. Запросите код заново."] };
            }

            var creationResult = await accountRegistrationService.CreateAsync(new AccountRegistrationRequest(
                cacheEntry.Email,
                null,
                cacheEntry.PersonalDataProcessingConsent,
                cacheEntry.AdvertisingConsent,
                attribution));

            if (creationResult.Errors.Count != 0)
            {
                return new EmailCodeLoginResult { Errors = creationResult.Errors };
            }

            user = creationResult.User;
        }
        else if (!await userManager.IsEmailConfirmedAsync(user))
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        memoryCache.Remove(cacheKey);

        return new EmailCodeLoginResult { User = user, IsAccountCreated = accountCreated };
    }

    /// <summary> Генерирует случайный код входа. </summary>
    private static string GenerateEmailLoginCode()
    {
        return RandomNumberGenerator
            .GetInt32(0, (int)Math.Pow(10, EmailLoginCodeLength))
            .ToString($"D{EmailLoginCodeLength}", CultureInfo.InvariantCulture);
    }

    /// <summary> Нормализует email для ключа кода. </summary>
    private static string NormalizeEmailForCode(string email)
    {
        return email.Trim().ToUpperInvariant();
    }

    /// <summary> Формирует ключ кеша для кода входа. </summary>
    private static string GetEmailLoginCodeCacheKey(string normalizedEmail)
    {
        return $"auth:email-code:{normalizedEmail}";
    }

    /// <summary> Вычисляет хеш кода входа. </summary>
    private static string HashEmailLoginCode(string normalizedEmail, string code)
    {
        var bytes = Encoding.UTF8.GetBytes($"{normalizedEmail}|{code}|OpenCRM.EmailLoginCode.v1");

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary> Проверяет соответствие кода входа хешу. </summary>
    private static bool VerifyEmailLoginCodeHash(string normalizedEmail, string code, string expectedHash)
    {
        var actualBytes = Convert.FromHexString(EmailCodeLoginService.HashEmailLoginCode(normalizedEmail, code));
        var expectedBytes = Convert.FromHexString(expectedHash);

        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    /// <summary> Запись кеша кода входа. </summary>
    private sealed class EmailLoginCodeCacheEntry
    {
        /// <summary> Email адрес. </summary>
        public required string Email { get; init; }

        /// <summary> Хеш кода. </summary>
        public required string CodeHash { get; init; }

        /// <summary> Время отправки кода. </summary>
        public DateTimeOffset SentAt { get; init; }

        /// <summary> Время истечения кода. </summary>
        public DateTimeOffset ExpiresAt { get; init; }

        /// <summary> Признак регистрации нового пользователя. </summary>
        public bool IsRegistration { get; init; }

        /// <summary> Согласие на обработку персональных данных. </summary>
        public bool PersonalDataProcessingConsent { get; init; }

        /// <summary> Согласие на рекламу. </summary>
        public bool AdvertisingConsent { get; init; }

        /// <summary> Число попыток ввода кода. </summary>
        public int Attempts { get; set; }
    }
}
