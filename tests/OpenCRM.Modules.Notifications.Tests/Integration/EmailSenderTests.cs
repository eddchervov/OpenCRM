using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class EmailSenderTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task StableKeyKeepsOriginalIntentForCustomAndTemplateEmails()
    {
        await using var context = fixture.CreateContext();
        var sender = new EmailSender(context);
        var key = Guid.NewGuid().ToString();
        await sender.QueueAsync("test@example.com", "Первая тема", "Первый текст", key);
        await sender.QueueAsync("invalid", "Вторая тема", "Другой текст", key);
        await sender.QueueTemplateAsync("invalid", "Другая тема", "changed", new { text = "Другой текст" }, key);

        var saved = await context.EmailMessages.SingleAsync(item => item.NotificationKey == key);
        Assert.Equal("Первая тема", saved.Subject);
        Assert.Equal("system/custom-email", saved.TemplateKey);
        using var templateData = System.Text.Json.JsonDocument.Parse(saved.TemplateDataJson!);
        Assert.Contains("Первый", templateData.RootElement.GetProperty("htmlBody").GetString());
    }

    [Fact]
    public async Task TemplateAndIdentityEmailsGenerateKeysAndLinkNormalizedRecipient()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = $"{Guid.NewGuid():N}@example.com" };
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var sender = new EmailSender(context);

        await sender.SendEmailAsync(user.Email, "Тема", "Текст");
        await sender.QueueTemplateAsync(user.Email.ToUpperInvariant(), "Шаблон", "system/test", new { value = "Привет" });
        var messages = await context.EmailMessages.Where(item => item.UserId == user.Id).ToListAsync();
        Assert.Equal(2, messages.Count);
        Assert.Equal(2, messages.Select(item => item.NotificationKey).Distinct().Count());
        Assert.All(messages, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.NotificationKey));
            Assert.Equal("pending", item.NotificationStatus);
            Assert.NotNull(item.NextDispatchAt);
        });
        Assert.Contains(messages, item => item.TemplateKey == "system/test");
    }

    [Theory]
    [InlineData("invalid", "Тема")]
    [InlineData("test@example.com", "")]
    [InlineData("test@example.com", " ")]
    public async Task InvalidInputsAreRejectedBeforeIntentIsSaved(string email, string subject)
    {
        await using var context = fixture.CreateContext();
        var sender = new EmailSender(context);
        var key = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<ArgumentException>(() => sender.QueueAsync(email, subject, "Текст", key));
        Assert.False(await context.EmailMessages.AnyAsync(item => item.NotificationKey == key));
    }

    [Fact]
    public async Task SubjectLengthBoundaryIsEnforced()
    {
        await using var context = fixture.CreateContext();
        var sender = new EmailSender(context);
        var key = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<ArgumentException>(() => sender.QueueTemplateAsync("test@example.com", new string('a', 301), "test", new { }, key));
        await sender.QueueTemplateAsync("test@example.com", new string('a', 300), "test", new { }, key);
        Assert.Equal(300, (await context.EmailMessages.SingleAsync(item => item.NotificationKey == key)).Subject.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSaveDetachesIntentAndDoesNotPersistItOnNextSave(bool template)
    {
        await using var original = fixture.CreateContext();
        var options = (DbContextOptions<OpenCRMContext>)original.GetService<IDbContextOptions>();
        var interceptor = new FailOneSaveInterceptor();
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>(options)
            .AddInterceptors(interceptor).Options);
        var sender = new EmailSender(context);
        var key = Guid.NewGuid().ToString();
        Task Queue() => template
            ? sender.QueueTemplateAsync("test@example.com", "Тема", "test", new { }, key)
            : sender.QueueAsync("test@example.com", "Тема", "Текст", key);

        await Assert.ThrowsAsync<InvalidOperationException>(Queue);
        Assert.DoesNotContain(context.ChangeTracker.Entries<EmailMessage>(), item => item.Entity.NotificationKey == key);
        await context.SaveChangesAsync();
        Assert.False(await context.EmailMessages.AnyAsync(item => item.NotificationKey == key));
        await Queue();
        Assert.True(await context.EmailMessages.AnyAsync(item => item.NotificationKey == key));
    }

    private sealed class FailOneSaveInterceptor : SaveChangesInterceptor
    {
        private bool _hasFailed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!this._hasFailed)
            {
                this._hasFailed = true;
                throw new InvalidOperationException("Тестовый сбой сохранения.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
