using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Modules.Accounts.Users;
using System.Text.RegularExpressions;

namespace OpenCRM.Modules.Accounts.Tests.Integration;

public sealed class AccountDeletionTests(AccountsDatabaseFixture fixture) : IClassFixture<AccountsDatabaseFixture>
{
    private const string Password ="Deletion-test-123!";

    [Fact]
    public async Task DeleteAsync_MasksEmailAndUserNameAndMarksDeleted()
    {
        await using var context = fixture.CreateContext();
        var email = $"deletion-full-{Guid.NewGuid():N}@example.com";
        var user = await CreateUserAsync(context, email, withExternalLogin: true);
        var before = DateTime.UtcNow;

        await using var harness = CreateService(context);
        var result = await harness.Service.DeleteAsync(user);

        // Возвращаемое значение — исходный OriginalEmail, подставленный из email.
        Assert.Equal(email, result);

        var deleted = await context.Users.AsNoTracking()
            .SingleAsync(item => item.Id == user.Id);
        Assert.Matches(DeletionPattern(user.Id), deleted.Email!);
        Assert.Matches(DeletionPattern(user.Id), deleted.UserName!);
        Assert.Equal(email, deleted.OriginalEmail);
        Assert.True(deleted.IsDeleted);
        Assert.NotNull(deleted.DeletedAt);
        Assert.InRange(deleted.DeletedAt!.Value, before, DateTime.UtcNow);

        // Внешние логины удалены.
        var logins = await context.UserLogins.AsNoTracking()
            .Where(item => item.UserId == user.Id).ToListAsync();
        Assert.Empty(logins);

        // Строка Telegram-outbox с исходным email и идентификатором пользователя.
        var outbox = await context.TelegramNotificationOutbox.AsNoTracking()
            .SingleAsync(item => item.NotificationKey == $"deletion:{user.Id}");
        Assert.Contains(email, outbox.Body);
        Assert.Contains(user.Id, outbox.Body);
        Assert.Contains("Пользователь удалил аккаунт", outbox.Body);
    }

    [Fact]
    public async Task DeleteAsync_WithExistingOriginalEmail_KeepsItAndReturnsIt()
    {
        await using var context = fixture.CreateContext();
        var email = $"deletion-keep-{Guid.NewGuid():N}@example.com";
        var user = await CreateUserAsync(context, email, withExternalLogin: false);
        var original = $"original-{Guid.NewGuid():N}@example.com";
        user.OriginalEmail = original;
        await context.SaveChangesAsync();

        await using var harness = CreateService(context);
        var result = await harness.Service.DeleteAsync(user);

        Assert.Equal(original, result);
        var deleted = await context.Users.AsNoTracking()
            .SingleAsync(item => item.Id == user.Id);
        Assert.Equal(original, deleted.OriginalEmail);
        Assert.True(deleted.IsDeleted);
        Assert.NotNull(deleted.DeletedAt);
    }

    // Маска: deleted_<timestamp yyyyMMddHHmmss>_<id>@deleted.local
    private static string DeletionPattern(string userId) =>
        $"^deleted_\\d{{14}}_{Regex.Escape(userId)}@deleted\\.local$";

    [Fact]
    public async Task OutboxWriteFailureRollsBackMaskingAndRemovalOfExternalLogin()
    {
        await using var context = fixture.CreateContext();
        var email = $"deletion-rollback-{Guid.NewGuid():N}@example.com";
        var user = await CreateUserAsync(context, email, withExternalLogin: true);
        await using var harness = CreateService(context);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION accounts_reject_deletion_outbox() RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'Проверка отката удаления аккаунта.';
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER accounts_reject_deletion_outbox
                BEFORE INSERT ON "TelegramNotificationOutbox"
                FOR EACH ROW EXECUTE FUNCTION accounts_reject_deletion_outbox();
            """);

        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => harness.Service.DeleteAsync(user));
            Assert.Empty(context.ChangeTracker.Entries());
            await using var verification = fixture.CreateContext();
            var saved = await verification.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
            Assert.Equal(email, saved.Email);
            Assert.Equal(email, saved.UserName);
            Assert.Null(saved.OriginalEmail);
            Assert.False(saved.IsDeleted);
            Assert.Null(saved.DeletedAt);
            Assert.True(await verification.UserLogins.AnyAsync(item => item.UserId == user.Id));
            Assert.False(await verification.TelegramNotificationOutbox.AnyAsync(item => item.NotificationKey == $"deletion:{user.Id}"));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER accounts_reject_deletion_outbox ON "TelegramNotificationOutbox";
                DROP FUNCTION accounts_reject_deletion_outbox();
                """);
        }
    }

    private static DeletionHarness CreateService(OpenCRMContext context)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddAuthentication();
        services.AddSingleton(context);
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<OpenCRMContext>()
            .AddSignInManager();
        var provider = services.BuildServiceProvider();

        var service = new AccountDeletionService(
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            new ApplicationUnitOfWork(context),
            context);

        return new DeletionHarness(provider, service);
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        OpenCRMContext context, string email, bool withExternalLogin)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddAuthentication();
        services.AddSingleton(context);
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<OpenCRMContext>()
            .AddSignInManager();
        await using var provider = services.BuildServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DatetimeCreation = DateTime.UtcNow
        };
        var creation = await userManager.CreateAsync(user, Password);
        Assert.True(creation.Succeeded, string.Join("; ", creation.Errors.Select(error => error.Description)));

        if (withExternalLogin)
        {
            var login = await userManager.AddLoginAsync(
                user,
                new UserLoginInfo("Google", $"google-{Guid.NewGuid():N}", "Google"));
            Assert.True(login.Succeeded, string.Join("; ", login.Errors.Select(error => error.Description)));
        }

        // Возвращаем отслеживаемый контекстом экземпляр: сервис работает с UserManager,
        // который прикрепляет переданную сущность, поэтому копия вызовет конфликт идентичности.
        return user;
    }
    private sealed class DeletionHarness(ServiceProvider provider, AccountDeletionService service) : IAsyncDisposable
    {
        public AccountDeletionService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
        }
    }
}
