using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Accounts.Users;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.Modules.Accounts.Tests.Unit;

public sealed class EmailCodeLoginServiceTests : IDisposable
{
    private const string DeletedLoginError = "Этот аккаунт недоступен для входа.";
    private const string DeletedRegistrationError = "Этот аккаунт недоступен для регистрации.";
    private const string ConsentError = "Для создания аккаунта необходимо согласие на обработку персональных данных.";
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly Mock<IUsersService> _users = new();
    private readonly Mock<IEmailNotificationService> _emails = new();
    private readonly Mock<IAccountRegistrationService> _registration = new();
    private readonly TestTimeProvider _timeProvider = new();
    private readonly TestUserManager _userManager = new();

    public void Dispose()
    {
        this._cache.Dispose();
    }

    private string? SentCode { get; set; }

    private EmailCodeLoginService CreateService() => new(
        this._userManager,
        this._users.Object,
        this._emails.Object,
        this._registration.Object,
        this._cache,
        NullLogger<EmailCodeLoginService>.Instance,
        this._timeProvider);

    private void SetupEmailDelivery()
    {
        this._emails
            .Setup(sender => sender.QueueTemplateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string?>(),
                It.IsAny<long?>()))
            .Callback<string, string, string, object, string?, long?>((_, _, _, data, _, _) =>
                this.SentCode = (string?)data.GetType().GetProperty("code")!.GetValue(data))
            .Returns(Task.CompletedTask);
    }

    private static EmailCodeLoginRequest Request(
        string email,
        string? code = null,
        bool consent = true,
        bool advertisingConsent = false) => new(email, code, consent, advertisingConsent);

    private static ApplicationUser User(string email, bool isDeleted = false, bool emailConfirmed = false) => new()
    {
        Id = email,
        UserName = email,
        Email = email,
        IsDeleted = isDeleted,
        EmailConfirmed = emailConfirmed
    };

    private static string CacheKey(string email) => $"auth:email-code:{email.Trim().ToUpperInvariant()}";

    [Fact]
    public async Task SendRejectsDeletedAccount()
    {
        this._userManager.User = User("gone@example.com", isDeleted: true);

        var result = await this.CreateService().SendAsync(Request("gone@example.com"));

        Assert.Equal([DeletedLoginError], result.Errors);
        Assert.False(result.CodeSent);
        this._emails.VerifyNoOtherCalls();
        this._users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendRejectsLockedOutAccount()
    {
        this._userManager.User = User("locked@example.test");
        this._userManager.IsLockedOut = true;

        var result = await this.CreateService().SendAsync(Request("locked@example.test"));

        Assert.Equal(["Этот аккаунт временно заблокирован."], result.Errors);
        Assert.False(result.CodeSent);
        Assert.Null(result.User);
        this._emails.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyRejectsAccountLockedOutAfterCodeWasSent()
    {
        var user = User("locked@example.test");
        this._userManager.User = user;
        this.SetupEmailDelivery();
        var service = this.CreateService();
        await service.SendAsync(Request(user.Email!));
        this._userManager.IsLockedOut = true;

        var result = await service.VerifyAsync(Request(user.Email!, this.SentCode), null);

        Assert.Equal(["Этот аккаунт временно заблокирован."], result.Errors);
        Assert.Null(result.User);
        Assert.False(user.EmailConfirmed);
        Assert.Equal(0, this._userManager.UpdateCount);
        Assert.False(this._cache.TryGetValue(CacheKey(user.Email!), out _));
    }

    [Fact]
    public async Task SendRejectsDeletedOriginalEmail()
    {
        this._users.Setup(users => users.HasDeletedOriginalEmailAsync("old@example.com")).ReturnsAsync(true);

        var result = await this.CreateService().SendAsync(Request("old@example.com"));

        Assert.Equal([DeletedRegistrationError], result.Errors);
        this._users.Verify(users => users.HasDeletedOriginalEmailAsync("old@example.com"), Times.Once);
        this._emails.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendRequiresConsentForUnknownAccount()
    {
        var result = await this.CreateService().SendAsync(Request("new@example.com", consent: false));

        Assert.True(result.RequiresRegistrationConsent);
        Assert.Equal(nameof(EmailCodeLoginRequest.PersonalDataProcessingConsent), result.ErrorField);
        Assert.Equal([ConsentError], result.Errors);
        Assert.False(result.CodeSent);
        this._emails.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendNormalizesCacheKeyAndGeneratesEightDigitCode()
    {
        this.SetupEmailDelivery();
        var result = await this.CreateService().SendAsync(Request("  New@Example.com  "));

        Assert.True(result.IsNewCodeSent);
        Assert.Matches("^\\d{8}$", this.SentCode);
        Assert.True(this._cache.TryGetValue(CacheKey("new@example.com"), out _));
        this._emails.Verify(sender => sender.QueueTemplateAsync(
            "  New@Example.com  ", "Код входа в " + Helpers.ProjectBrand.DefaultName, "system/login-code",
            It.IsAny<object>(), It.IsAny<string?>(), It.IsAny<long?>()), Times.Once);
    }

    [Fact]
    public async Task SendAcceptsExistingAccountWithoutRegistrationConsent()
    {
        this._userManager.User = User("member@example.com");
        this.SetupEmailDelivery();

        var result = await this.CreateService().SendAsync(Request("member@example.com", consent: false));

        Assert.True(result.IsNewCodeSent);
        this._users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendReplacesNullCacheEntry()
    {
        this._cache.Set<object?>(CacheKey("user@example.com"), null, TimeSpan.FromHours(1));
        this.SetupEmailDelivery();

        var result = await this.CreateService().SendAsync(Request("user@example.com"));

        Assert.True(result.IsNewCodeSent);
        Assert.Matches("^\\d{8}$", this.SentCode);
    }

    [Fact]
    public async Task SendUsesSystemTimeWhenNoTimeProviderIsInjected()
    {
        this.SetupEmailDelivery();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new EmailCodeLoginService(
            this._userManager,
            this._users.Object,
            this._emails.Object,
            this._registration.Object,
            cache,
            NullLogger<EmailCodeLoginService>.Instance,
            null);

        var result = await service.SendAsync(Request("user@example.com"));

        Assert.True(result.IsNewCodeSent);
    }

    [Fact]
    public async Task SendDoesNotResendBeforeOneMinuteAndResendsAfterIt()
    {
        this.SetupEmailDelivery();
        var service = this.CreateService();

        var first = await service.SendAsync(Request("user@example.com"));
        var repeat = await service.SendAsync(Request("USER@example.com"));
        this._timeProvider.Advance(TimeSpan.FromMinutes(1));
        var afterLimit = await service.SendAsync(Request("user@example.com"));

        Assert.True(first.IsNewCodeSent);
        Assert.True(repeat.CodeSent);
        Assert.False(repeat.IsNewCodeSent);
        Assert.True(afterLimit.IsNewCodeSent);
        this._emails.Verify(sender => sender.QueueTemplateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<object>(), It.IsAny<string?>(), It.IsAny<long?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SendRemovesCodeWhenEmailDeliveryFails()
    {
        this._emails.Setup(sender => sender.QueueTemplateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<string?>(), It.IsAny<long?>()))
            .ThrowsAsync(new InvalidOperationException("queue unavailable"));

        var result = await this.CreateService().SendAsync(Request("user@example.com"));

        Assert.Equal(["Не удалось отправить код. Попробуйте позже или обратитесь в поддержку."], result.Errors);
        Assert.False(this._cache.TryGetValue(CacheKey("user@example.com"), out _));
    }

    [Fact]
    public async Task VerifyRejectsMissingOrExpiredCodeAndRemovesCacheEntry()
    {
        var service = this.CreateService();
        var missing = await service.VerifyAsync(Request("user@example.com", "12345678"), null);
        this.SetupEmailDelivery();
        await service.SendAsync(Request("user@example.com"));
        this._timeProvider.Advance(TimeSpan.FromHours(1));
        var expired = await service.VerifyAsync(Request("user@example.com", this.SentCode), null);

        Assert.Equal(["Код устарел. Запросите новый код."], missing.Errors);
        Assert.Equal(["Код устарел. Запросите новый код."], expired.Errors);
        Assert.False(this._cache.TryGetValue(CacheKey("user@example.com"), out _));
    }

    [Fact]
    public async Task VerifyRejectsWrongCodeThenAcceptsCorrectCodeCaseInsensitively()
    {
        this._userManager.User = User("user@example.com");
        this.SetupEmailDelivery();
        var service = this.CreateService();
        await service.SendAsync(Request("User@Example.com"));

        var wrong = await service.VerifyAsync(Request(" user@example.com ", WrongCode()), null);
        var correct = await service.VerifyAsync(Request("USER@example.com", this.SentCode), null);

        Assert.Equal(["Неверный код."], wrong.Errors);
        Assert.Empty(correct.Errors);
        Assert.False(correct.IsAccountCreated);
        Assert.True(correct.User!.EmailConfirmed);
        Assert.Equal(1, this._userManager.UpdateCount);
        Assert.False(this._cache.TryGetValue(CacheKey("user@example.com"), out _));
    }

    [Fact]
    public async Task VerifyDoesNotUpdateAlreadyConfirmedAccount()
    {
        this._userManager.User = User("user@example.com", emailConfirmed: true);
        this.SetupEmailDelivery();
        var service = this.CreateService();
        await service.SendAsync(Request("user@example.com"));

        var result = await service.VerifyAsync(Request("user@example.com", this.SentCode), null);

        Assert.Empty(result.Errors);
        Assert.Equal(0, this._userManager.UpdateCount);
    }

    [Fact]
    public async Task VerifyRejectsCodeAfterFiveFailedAttempts()
    {
        this.SetupEmailDelivery();
        var service = this.CreateService();
        await service.SendAsync(Request("user@example.com"));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var result = await service.VerifyAsync(Request("user@example.com", WrongCode()), null);
            Assert.Equal(["Неверный код."], result.Errors);
        }

        var limited = await service.VerifyAsync(Request("user@example.com", this.SentCode), null);

        Assert.Equal(["Слишком много попыток. Запросите новый код."], limited.Errors);
        Assert.False(this._cache.TryGetValue(CacheKey("user@example.com"), out _));
    }

    [Fact]
    public async Task VerifyRejectsAccountDeletedAfterCodeWasSent()
    {
        this._userManager.User = User("user@example.com");
        this.SetupEmailDelivery();
        var service = this.CreateService();
        await service.SendAsync(Request("user@example.com"));
        this._userManager.User = User("user@example.com", isDeleted: true);

        var result = await service.VerifyAsync(Request("user@example.com", this.SentCode), null);

        Assert.Equal([DeletedLoginError], result.Errors);
        Assert.False(this._cache.TryGetValue(CacheKey("user@example.com"), out _));
    }

    [Fact]
    public async Task VerifyDoesNotRegisterWhenExistingAccountDisappears()
    {
        this._userManager.User = User("user@example.com");
        this.SetupEmailDelivery();
        var service = this.CreateService();
        await service.SendAsync(Request("user@example.com"));
        this._userManager.User = null;

        var result = await service.VerifyAsync(Request("user@example.com", this.SentCode), null);

        Assert.Equal(["Аккаунт не найден. Запросите код заново."], result.Errors);
        Assert.False(this._cache.TryGetValue(CacheKey("user@example.com"), out _));
        this._registration.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyCreatesAccountAndPassesRegistrationConsentsAndAttribution()
    {
        this.SetupEmailDelivery();
        var created = User("new@example.com");
        var attribution = new RegistrationAttributionData { FirstUtmCampaign = "campaign", LastUtmSource = "source" };
        this._registration.Setup(registration => registration.CreateAsync(It.IsAny<AccountRegistrationRequest>()))
            .ReturnsAsync(new AccountCreationResult(created, []));
        var service = this.CreateService();
        await service.SendAsync(Request("new@example.com", advertisingConsent: true));

        var result = await service.VerifyAsync(Request("new@example.com", this.SentCode), attribution);

        Assert.Empty(result.Errors);
        Assert.True(result.IsAccountCreated);
        Assert.Same(created, result.User);
        var registrationRequest = Assert.IsType<AccountRegistrationRequest>(this._registration.Invocations[0].Arguments[0]);
        Assert.Equal("new@example.com", registrationRequest.Email);
        Assert.True(registrationRequest.PersonalDataProcessingConsent);
        Assert.True(registrationRequest.AdvertisingConsent);
        Assert.Same(attribution, registrationRequest.Attribution);
        Assert.False(this._cache.TryGetValue(CacheKey("new@example.com"), out _));
    }

    [Fact]
    public async Task VerifyKeepsCodeAfterRegistrationFailureSoTheUserCanRetry()
    {
        this.SetupEmailDelivery();
        this._registration.SetupSequence(registration => registration.CreateAsync(It.IsAny<AccountRegistrationRequest>()))
            .ReturnsAsync(new AccountCreationResult(null, ["Временная ошибка регистрации."]))
            .ReturnsAsync(new AccountCreationResult(User("new@example.com"), []));
        var service = this.CreateService();
        await service.SendAsync(Request("new@example.com"));

        var failed = await service.VerifyAsync(Request("new@example.com", this.SentCode), null);
        Assert.Equal(["Временная ошибка регистрации."], failed.Errors);
        Assert.True(this._cache.TryGetValue(CacheKey("new@example.com"), out _));

        var successful = await service.VerifyAsync(Request("new@example.com", this.SentCode), null);

        Assert.Empty(successful.Errors);
        Assert.True(successful.IsAccountCreated);
        Assert.Equal("new@example.com", successful.User!.Email);
        Assert.False(this._cache.TryGetValue(CacheKey("new@example.com"), out _));
        Assert.Equal(2, this._registration.Invocations.Count);
    }

    [Fact]
    public async Task VerifyConsumesCodeWhenRegistrationReturnsSuccessWithoutUser()
    {
        this.SetupEmailDelivery();
        this._registration.Setup(registration => registration.CreateAsync(It.IsAny<AccountRegistrationRequest>()))
            .ReturnsAsync(new AccountCreationResult(null, []));
        var service = this.CreateService();
        await service.SendAsync(Request("new@example.com"));

        var result = await service.VerifyAsync(Request("new@example.com", this.SentCode), null);

        Assert.True(result.IsAccountCreated);
        Assert.Null(result.User);
        Assert.False(this._cache.TryGetValue(CacheKey("new@example.com"), out _));
    }

    private string WrongCode() => this.SentCode == "00000000" ? "00000001" : "00000000";

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => this._now;

        public void Advance(TimeSpan duration) => this._now += duration;
    }

    private sealed class TestUserManager : UserManager<ApplicationUser>
    {
        public TestUserManager()
            : base(
                Mock.Of<IUserStore<ApplicationUser>>(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                [],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!,
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
        }

        public ApplicationUser? User { get; set; }

        public int UpdateCount { get; private set; }

        public bool IsLockedOut { get; set; }

        public override Task<bool> IsLockedOutAsync(ApplicationUser user) => Task.FromResult(this.IsLockedOut);

        public override Task<ApplicationUser?> FindByEmailAsync(string email) =>
            Task.FromResult(this.User is not null && string.Equals(this.User.Email, email, StringComparison.OrdinalIgnoreCase)
                ? this.User
                : null);

        public override Task<bool> IsEmailConfirmedAsync(ApplicationUser user) => Task.FromResult(user.EmailConfirmed);

        public override Task<IdentityResult> UpdateAsync(ApplicationUser user)
        {
            this.UpdateCount++;
            return Task.FromResult(IdentityResult.Success);
        }
    }
}
