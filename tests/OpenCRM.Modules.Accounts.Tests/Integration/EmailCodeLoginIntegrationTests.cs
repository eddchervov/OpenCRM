using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Accounts.Users;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.Modules.Accounts.Tests.Integration;

public sealed class EmailCodeLoginIntegrationTests(AccountsDatabaseFixture fixture) : IClassFixture<AccountsDatabaseFixture>
{
    private const string DeletedByMailError = "Этот аккаунт недоступен для входа.";
    private const string DeletedOriginalEmailError = "Этот аккаунт недоступен для регистрации.";
    private const string ExpiredCodeError = "Код устарел. Запросите новый код.";
    private const string WrongCodeError = "Неверный код.";
    private const string AttemptLimitError = "Слишком много попыток. Запросите новый код.";
    private const string DeliveryError = "Не удалось отправить код. Попробуйте позже или обратитесь в поддержку.";

    [Fact]
    public async Task SendAsync_RejectsLoginForDeletedAccountByEmail()
    {
        await using var context = fixture.CreateContext();
        var email = $"email-code-deleted-{Guid.NewGuid():N}@example.com";
        context.Users.Add(User(email, isDeleted: true));
        await context.SaveChangesAsync();
        await using var harness = CreateService(context);

        var result = await harness.Service.SendAsync(Request(email));

        Assert.Equal([DeletedByMailError], result.Errors);
        Assert.False(result.CodeSent);
        harness.Emails.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendAsync_RejectsDeletedAccountByOriginalEmail()
    {
        await using var context = fixture.CreateContext();
        var newEmail = $"email-code-deleted-new-{Guid.NewGuid():N}@example.com";
        var originalEmail = $"email-code-deleted-old-{Guid.NewGuid():N}@example.com";
        context.Users.Add(User(newEmail, isDeleted: true, originalEmail: originalEmail));
        await context.SaveChangesAsync();
        await using var harness = CreateService(context);

        var result = await harness.Service.SendAsync(Request(originalEmail));

        Assert.Equal([DeletedOriginalEmailError], result.Errors);
        Assert.False(result.CodeSent);
        harness.Emails.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendAsync_DoesNotResendWithinAntiSpamMinute()
    {
        await using var context = fixture.CreateContext();
        var email = $"email-code-antispam-{Guid.NewGuid():N}@example.com";
        await using var harness = CreateService(context);

        var first = await harness.Service.SendAsync(Request(email));
        var repeat = await harness.Service.SendAsync(Request(email.ToUpperInvariant()));

        Assert.True(first.IsNewCodeSent);
        Assert.True(repeat.CodeSent);
        Assert.False(repeat.IsNewCodeSent);
        Assert.Contains("через минуту", repeat.StatusMessage);
        harness.Emails.Verify(
            sender => sender.QueueTemplateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<string?>(), It.IsAny<long?>()),
            Times.Once);
        Assert.Single(harness.Codes);
    }

    [Fact]
    public async Task SendAsync_ClearsPendingCodeWhenEmailDeliveryFails()
    {
        await using var context = fixture.CreateContext();
        var email = $"email-code-delivery-{Guid.NewGuid():N}@example.com";
        await using var harness = CreateService(context);
        harness.Emails
            .Setup(sender => sender.QueueTemplateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<string?>(), It.IsAny<long?>()))
            .ThrowsAsync(new InvalidOperationException("queue unavailable"));

        var result = await harness.Service.SendAsync(Request(email));

        Assert.Equal([DeliveryError], result.Errors);
        Assert.False(harness.Cache.TryGetValue(CacheKey(email), out _));
    }

    [Fact]
    public async Task VerifyAsync_RejectsWrongCodeAndKeepsEntry()
    {
        await using var context = fixture.CreateContext();
        var email = $"email-code-wrong-{Guid.NewGuid():N}@example.com";
        context.Users.Add(User(email));
        await context.SaveChangesAsync();
        await using var harness = CreateService(context);
        await harness.Service.SendAsync(Request(email));

        var wrong = await harness.Service.VerifyAsync(Request(email, WrongCode(harness.Codes[0])), null);

        Assert.Equal([WrongCodeError], wrong.Errors);
        Assert.True(harness.Cache.TryGetValue(CacheKey(email), out _));

        // Запись не удалена: верный код после неверного всё ещё принимается.
        var correct = await harness.Service.VerifyAsync(Request(email, harness.Codes[0]), null);

        Assert.Empty(correct.Errors);
        Assert.False(correct.IsAccountCreated);
        Assert.True(correct.User!.EmailConfirmed);
        Assert.False(harness.Cache.TryGetValue(CacheKey(email), out _));
    }

    [Fact]
    public async Task VerifyAsync_RejectsExpiredCode()
    {
        await using var context = fixture.CreateContext();
        var email = $"email-code-expired-{Guid.NewGuid():N}@example.com";
        await using var harness = CreateService(context);
        await harness.Service.SendAsync(Request(email));
        harness.TimeProvider.Advance(TimeSpan.FromHours(1));

        var result = await harness.Service.VerifyAsync(Request(email, harness.Codes[0]), null);

        Assert.Equal([ExpiredCodeError], result.Errors);
        Assert.False(harness.Cache.TryGetValue(CacheKey(email), out _));
    }

    [Fact]
    public async Task VerifyAsync_BlocksAfterAttemptLimit()
    {
        await using var context = fixture.CreateContext();
        var email = $"email-code-attempts-{Guid.NewGuid():N}@example.com";
        await using var harness = CreateService(context);
        await harness.Service.SendAsync(Request(email));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await harness.Service.VerifyAsync(Request(email, WrongCode(harness.Codes[0])), null);
            Assert.Equal([WrongCodeError], wrong.Errors);
        }

        var limited = await harness.Service.VerifyAsync(Request(email, harness.Codes[0]), null);

        Assert.Equal([AttemptLimitError], limited.Errors);
        Assert.False(harness.Cache.TryGetValue(CacheKey(email), out _));
    }

    [Fact]
    public async Task VerifyAsync_CreatesAccountOnRegistration()
    {
        await using var context = fixture.CreateContext();
        var email = $"email-code-register-{Guid.NewGuid():N}@example.com";
        await using var harness = CreateService(context);
        await harness.Service.SendAsync(Request(email));

        var result = await harness.Service.VerifyAsync(Request(email, harness.Codes[0]), null);

        Assert.Empty(result.Errors);
        Assert.True(result.IsAccountCreated);
        await using var verifyContext = fixture.CreateContext();
        var created = await verifyContext.Users.AsNoTracking()
            .SingleAsync(user => user.Email == email);
        Assert.True(created.EmailConfirmed);
        Assert.Equal(email, created.OriginalEmail);
    }

    [Fact]
    public async Task VerifyAsync_RetriesRegistrationWithTheSameCodeAfterTemporaryIdentityFailure()
    {
        await using var context = fixture.CreateContext();
        var email = $"email-code-retry-{Guid.NewGuid():N}@example.com";
        await using var harness = CreateService(context, new FailOnceUserValidator());
        await harness.Service.SendAsync(Request(email));

        var rejected = await harness.Service.VerifyAsync(Request(email, harness.Codes[0]), null);

        Assert.NotEmpty(rejected.Errors);
        Assert.Null(rejected.User);
        Assert.True(harness.Cache.TryGetValue(CacheKey(email), out _));
        Assert.False(await context.Users.AnyAsync(user => user.Email == email));

        var retried = await harness.Service.VerifyAsync(Request(email, harness.Codes[0]), null);

        Assert.Empty(retried.Errors);
        Assert.True(retried.IsAccountCreated);
        Assert.Equal(email, retried.User!.Email);
        Assert.False(harness.Cache.TryGetValue(CacheKey(email), out _));
    }

    [Fact]
    public async Task VerifyAsync_ConfirmsEmailForExistingUser()
    {
        await using var context = fixture.CreateContext();
        var email = $"email-code-confirm-{Guid.NewGuid():N}@example.com";
        context.Users.Add(User(email));
        await context.SaveChangesAsync();
        await using var harness = CreateService(context);
        await harness.Service.SendAsync(Request(email));

        var result = await harness.Service.VerifyAsync(Request(email, harness.Codes[0]), null);

        Assert.Empty(result.Errors);
        Assert.False(result.IsAccountCreated);
        Assert.True(result.User!.EmailConfirmed);
        await using var verifyContext = fixture.CreateContext();
        var stored = await verifyContext.Users.AsNoTracking()
            .SingleAsync(user => user.Email == email);
        Assert.True(stored.EmailConfirmed);
    }

    private static EmailCodeLoginRequest Request(
        string email,
        string? code = null,
        bool consent = true) => new(email, code, consent, false);

    private static ApplicationUser User(
        string email,
        bool isDeleted = false,
        string? originalEmail = null) => new()
    {
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        IsDeleted = isDeleted,
        OriginalEmail = originalEmail ?? email
    };

    private static string CacheKey(string email) => $"auth:email-code:{email.Trim().ToUpperInvariant()}";

    private static string WrongCode(string code) => code == "00000000" ? "00000001" : "00000000";

    private static LoginHarness CreateService(
        OpenCRMContext context,
        IUserValidator<ApplicationUser>? userValidator = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        if (userValidator != null)
        {
            services.AddSingleton<IUserValidator<ApplicationUser>>(userValidator);
        }

        services.AddSingleton(context);
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<OpenCRMContext>();
        var provider = services.BuildServiceProvider();

        // Отправка писем подменяется: код запоминается, реальные письма не отправляются.
        var emails = new Mock<IEmailNotificationService>();
        var codes = new List<string>();
        emails
            .Setup(sender => sender.QueueTemplateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<string?>(), It.IsAny<long?>()))
            .Callback<string, string, string, object, string?, long?>((_, _, _, data, _, _) =>
                codes.Add((string)data.GetType().GetProperty("code")!.GetValue(data)!))
            .Returns(Task.CompletedTask);

        var registration = new AccountRegistrationService(
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            new ApplicationUnitOfWork(context),
            new Mock<ISiteNotificationPublisher>().Object,
            context);

        var timeProvider = new TestTimeProvider();
        var service = new EmailCodeLoginService(
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            new UsersService(context),
            emails.Object,
            registration,
            provider.GetRequiredService<IMemoryCache>(),
            NullLogger<EmailCodeLoginService>.Instance,
            timeProvider);

        return new LoginHarness(
            provider,
            service,
            emails,
            codes,
            provider.GetRequiredService<IMemoryCache>(),
            timeProvider);
    }

    private sealed class LoginHarness(
        ServiceProvider provider,
        EmailCodeLoginService service,
        Mock<IEmailNotificationService> emails,
        List<string> codes,
        IMemoryCache cache,
        TestTimeProvider timeProvider) : IAsyncDisposable
    {
        public EmailCodeLoginService Service { get; } = service;

        public Mock<IEmailNotificationService> Emails { get; } = emails;

        public List<string> Codes { get; } = codes;

        public IMemoryCache Cache { get; } = cache;

        public TestTimeProvider TimeProvider { get; } = timeProvider;

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => this._now;

        public void Advance(TimeSpan duration) => this._now += duration;
    }

    private sealed class FailOnceUserValidator : IUserValidator<ApplicationUser>
    {
        private int _remainingFailures = 1;

        public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
        {
            if (Interlocked.Exchange(ref this._remainingFailures, 0) == 0)
            {
                return Task.FromResult(IdentityResult.Success);
            }

            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "TemporaryValidationFailure",
                Description = "Временная ошибка проверки регистрации."
            }));
        }
    }
}
