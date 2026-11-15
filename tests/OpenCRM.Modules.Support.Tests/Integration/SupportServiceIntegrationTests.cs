using System.Data.Common;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Support.Admin;
using OpenCRM.Modules.Support.Personal;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Home.Dtos;
using OpenCRM.Testing;
using Moq;
using Npgsql;

namespace OpenCRM.Modules.Support.Tests.Integration;

[Collection(nameof(SupportDatabaseCollection))]
public sealed class SupportServiceIntegrationTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateQueuesNotificationTogetherWithSupportMessage()
    {
        await using var context = fixture.CreateContext();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Support:NotificationEmail"] = "contact@example.invalid"
            })
            .Build();
        var service = CreateSupportMessageService(context, fixture, configuration, Mock.Of<IFileStorageService>());

        var result = await service.CreateAsync(new SupportMessageCreateDto
        {
            Topic = "Помощь",
            Text = "Описание",
            Email = "contact@example.invalid",
            TypeCode = SupportMessageTypeCodes.Question
        });

        Assert.True(result.Succeeded);
        Assert.Equal(1, await context.SupportMessages.CountAsync());
        var notification = await context.EmailMessages.SingleAsync();
        Assert.Equal("system/support-message", notification.TemplateKey);
        Assert.Equal("contact@example.invalid", notification.ToEmail);
        Assert.NotNull(notification.NextDispatchAt);
    }

    [Fact]
    public async Task CreateRollsBackWhenNotificationCannotBeSaved()
    {
        await using var context = fixture.CreateContext();
        var storage = new TestAttachmentStorage();
        var sender = new Mock<IEmailNotificationService>();
        sender.Setup(item => item.QueueTemplateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string?>(), It.IsAny<long?>()))
            .ThrowsAsync(new InvalidOperationException("Тестовый сбой сохранения уведомления."));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Support:NotificationEmail"] = "contact@example.invalid",
            ["Notifications:Transport"] = "RabbitMq"
        }).Build();
        var service = CreateSupportMessageService(context, fixture, configuration, storage.Object, sender: sender.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new SupportMessageCreateDto
            {
                Topic = "Помощь",
                Text = "Описание",
                Attachments = [new SupportAttachmentUploadDto { FileName = "screen.png", Content = [137, 80, 78, 71, 13, 10, 26, 10] }]
            }));

        context.ChangeTracker.Clear();
        Assert.False(await context.SupportMessages.AnyAsync());
        Assert.False(await context.SupportAttachments.AnyAsync());
        Assert.Empty(storage.Files);
        Assert.False(await context.EmailMessages.AnyAsync());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "RabbitMq")]
    [InlineData("contact@example.invalid", "Disabled")]
    [InlineData("contact@example.invalid", "InMemory")]
    public async Task CreateWithoutEmailDeliveryPersistsTicketAndAttachment(string? notificationEmail, string? transport)
    {
        await using var context = fixture.CreateContext();
        var storage = new TestAttachmentStorage();
        var sender = new Mock<IEmailNotificationService>(MockBehavior.Strict);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Support:NotificationEmail"] = notificationEmail,
            ["Notifications:Transport"] = transport
        }).Build();
        var service = CreateSupportMessageService(context, fixture, configuration, storage.Object, sender: sender.Object);

        var result = await service.CreateAsync(new SupportMessageCreateDto
        {
            Topic = "Помощь",
            Text = "Описание",
            Attachments = [new SupportAttachmentUploadDto { FileName = "screen.png", Content = [137, 80, 78, 71, 13, 10, 26, 10] }]
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Обращение сохранено. Ответ появится в разделе поддержки.", result.SuccessMessage);
        Assert.Equal(result.MessageId, (await context.SupportMessages.SingleAsync()).Id);
        Assert.Equal(result.MessageId, (await context.SupportAttachments.SingleAsync()).SupportMessageId);
        Assert.Single(storage.Files);
        Assert.False(await context.EmailMessages.AnyAsync());
        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnknownCommitWithoutEmailDoesNotDuplicateTicketOrAttachment()
    {
        await using var context = fixture.CreateContext();
        var interceptor = new CommitAcknowledgementFailureInterceptor();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(context.Database.GetConnectionString(), postgres => postgres.EnableRetryOnFailure())
            .AddInterceptors(interceptor)
            .Options;
        await using var retryContext = new OpenCRMContext(options);
        var storage = new TestAttachmentStorage();
        var sender = new Mock<IEmailNotificationService>(MockBehavior.Strict);
        var service = CreateSupportMessageService(retryContext, fixture, new ConfigurationBuilder().Build(), storage.Object, sender: sender.Object);

        var result = await service.CreateAsync(new SupportMessageCreateDto
        {
            Topic = "Повтор после коммита",
            Text = "Описание",
            Attachments = [new SupportAttachmentUploadDto { FileName = "screen.png", Content = [137, 80, 78, 71, 13, 10, 26, 10] }]
        });

        Assert.True(result.Succeeded);
        Assert.True(interceptor.HasFailed);
        retryContext.ChangeTracker.Clear();
        Assert.Equal(result.MessageId, (await retryContext.SupportMessages.SingleAsync()).Id);
        Assert.Equal(result.MessageId, (await retryContext.SupportAttachments.SingleAsync()).SupportMessageId);
        Assert.Equal(1, storage.SaveCount);
        Assert.Single(storage.Files);
        Assert.False(await retryContext.EmailMessages.AnyAsync());
        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateRetriesTransientDatabaseFailureWithoutUploadingAttachmentsAgain()
    {
        await using var context = fixture.CreateContext();
        var interceptor = new TransientSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(context.Database.GetConnectionString(), postgres => postgres.EnableRetryOnFailure())
            .AddInterceptors(interceptor)
            .Options;
        await using var retryContext = new OpenCRMContext(options);
        var storage = new TestAttachmentStorage();
        var service = CreateSupportMessageService(
            retryContext,
            fixture,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Support:NotificationEmail"] = "contact@example.invalid" })
                .Build(),
            storage.Object);

        var result = await service.CreateAsync(new SupportMessageCreateDto
        {
            Topic = "Помощь",
            Text = "Описание",
            Attachments = [new SupportAttachmentUploadDto { FileName = "screen.png", Content = [137, 80, 78, 71, 13, 10, 26, 10] }]
        });

        Assert.True(result.Succeeded);
        Assert.True(interceptor.CallCount >= 3);
        Assert.Equal(1, storage.SaveCount);
        retryContext.ChangeTracker.Clear();
        var attachment = await retryContext.SupportAttachments.SingleAsync();
        Assert.Equal(result.MessageId, attachment.SupportMessageId);
        Assert.Equal(1, await retryContext.SupportMessages.CountAsync());
        Assert.Single(storage.Files);
    }

    [Fact]
    public async Task InboxAndRepliesAreLimitedToTheCurrentUser()
    {
        await using var context = fixture.CreateContext();
        var ownMessage = new SupportMessage
        {
            UserId = "support-user-a",
            Topic = "Моё обращение",
            Text = "Моё сообщение",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        var otherMessage = new SupportMessage
        {
            UserId = "support-user-b",
            Topic = "Чужое обращение",
            Text = "Чужое сообщение"
        };
        context.SupportMessages.AddRange(ownMessage, otherMessage);
        await context.SaveChangesAsync();
        context.SupportMessageEntries.AddRange(
            new SupportMessageEntry
            {
                SupportMessageId = ownMessage.Id,
                IsStaff = true,
                Text = "Ответ поддержки",
                CreatedAt = ownMessage.UpdatedAt
            },
            new SupportMessageEntry
            {
                SupportMessageId = otherMessage.Id,
                IsStaff = true,
                Text = "Чужой ответ",
                CreatedAt = otherMessage.UpdatedAt
            });
        await context.SaveChangesAsync();

        var configuration = new ConfigurationBuilder().Build();
        var service = CreateSupportMessageService(context, fixture, configuration, Mock.Of<IFileStorageService>());
        var ownInbox = await service.GetInboxAsync("support-user-a", ownMessage.Id);
        var foreignInbox = await service.GetInboxAsync("support-user-a", otherMessage.Id);
        var replyResult = await service.ReplyAsync("support-user-a", otherMessage.Id, "Чужой ответ");

        Assert.Single(ownInbox.Conversations);
        Assert.Equal(ownMessage.Id, ownInbox.Selected?.Id);
        Assert.Equal(new[] { "Моё сообщение", "Ответ поддержки" }, ownInbox.Selected?.Entries.Select(entry => entry.Text));
        Assert.DoesNotContain(foreignInbox.Conversations, conversation => conversation.Id == otherMessage.Id);
        Assert.Null(foreignInbox.Selected);
        Assert.Equal(SupportMessageReplyResult.NotFound, replyResult);

        context.ChangeTracker.Clear();
        Assert.Equal(2, await context.SupportMessages.CountAsync());
        Assert.Equal(2, await context.SupportMessageEntries.CountAsync());
    }

    [Fact]
    public async Task CreateAndReplyKeepAttachmentsSeparateAndEnforceOwnerAccess()
    {
        await using var context = fixture.CreateContext();
        var storage = new TestAttachmentStorage();
        var dataProtection = new EphemeralDataProtectionProvider();
        var attachmentService = CreateAttachmentService(context, storage.Object, dataProtection);
        var service = CreateSupportMessageService(
            context,
            fixture,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Support:NotificationEmail"] = "contact@example.invalid" })
                .Build(),
            storage.Object,
            dataProtection);
        var initialContent = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var replyContent = Encoding.UTF8.GetBytes("Дополнительная информация");

        var created = await service.CreateAsync(new SupportMessageCreateDto
        {
            UserId = "support-owner",
            Email = "contact@example.invalid",
            Topic = "Проблема с экраном",
            Text = "Прикладываю снимок ошибки",
            Attachments = [new SupportAttachmentUploadDto { FileName = "folder/screen.png", Content = initialContent }]
        });
        Assert.True(created.Succeeded);

        var replyResult = await service.ReplyAsync(
            "support-owner",
            created.MessageId!.Value,
            "Добавляю журнал ошибки",
            CancellationToken.None,
            [new SupportAttachmentUploadDto { FileName = "error.txt", Content = replyContent }]);
        Assert.Equal(SupportMessageReplyResult.Succeeded, replyResult);

        var inbox = await service.GetInboxAsync("support-owner", created.MessageId);
        Assert.Equal("screen.png", Assert.Single(inbox.Selected!.Entries[0].Attachments).FileName);
        var replyAttachment = Assert.Single(inbox.Selected.Entries[1].Attachments);
        Assert.Equal("error.txt", replyAttachment.FileName);
        Assert.NotEqual(inbox.Selected.Entries[0].Attachments[0].Id, replyAttachment.Id);

        Assert.Null(await attachmentService.GetForUserAsync("another-user", replyAttachment.Id));
        Assert.NotNull(await attachmentService.GetForUserAsync("support-owner", replyAttachment.Id));
        var adminContent = await attachmentService.GetForAdminAsync(replyAttachment.Id);
        Assert.Equal(replyContent, adminContent?.Content);

        var adminMessage = await new AdminSupportMessageService(context, fixture.CreateSender(context)).GetAsync(created.MessageId.Value);
        Assert.Equal("screen.png", Assert.Single(adminMessage!.Entries[0].Attachments).FileName);
        Assert.Equal("error.txt", Assert.Single(adminMessage.Entries[1].Attachments).FileName);
    }

    [Fact]
    public async Task AdminGuestReplyQueuesEmailAndPersistsStaffEntry()
    {
        await using var context = fixture.CreateContext();
        var message = new SupportMessage
        {
            Topic = "Вопрос",
            Text = "Исходное сообщение",
            Email = "contact@example.invalid"
        };
        context.SupportMessages.Add(message);
        await context.SaveChangesAsync();
        var service = new AdminSupportMessageService(context, fixture.CreateSender(context));

        var result = await service.ReplyAsync(message.Id, "admin-user", "Ответ поддержки");

        Assert.True(result.Succeeded);
        context.ChangeTracker.Clear();
        var entry = await context.SupportMessageEntries.SingleAsync(item => item.Kind == "guest_email_reply");
        Assert.Equal("guest_email_reply", entry.Kind);
        Assert.Equal("Ответ поддержки", entry.Text);
        Assert.Equal("admin-user", entry.AuthorUserId);
        var notification = await context.EmailMessages.SingleAsync();
        Assert.Equal($"support-reply:{entry.Id}", notification.NotificationKey);
        Assert.Equal("contact@example.invalid", notification.ToEmail);
    }

    private static SupportMessageService CreateSupportMessageService(
        OpenCRMContext context,
        NotificationFixture fixture,
        IConfiguration configuration,
        IFileStorageService storage,
        IDataProtectionProvider? dataProtection = null,
        IEmailNotificationService? sender = null) => new(
            context,
            sender ?? fixture.CreateSender(context),
            configuration,
            CreateAttachmentService(context, storage, dataProtection));

    private static SupportAttachmentService CreateAttachmentService(
        OpenCRMContext context,
        IFileStorageService storage,
        IDataProtectionProvider? dataProtection = null) =>
        new(context, storage, dataProtection ?? new EphemeralDataProtectionProvider(), NullLogger<SupportAttachmentService>.Instance);

    private sealed class TestAttachmentStorage
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public Mock<IFileStorageService> StorageMock { get; } = new();

        public IFileStorageService Object => this.StorageMock.Object;

        public IReadOnlyDictionary<string, byte[]> Files => this._files;

        public int SaveCount { get; private set; }

        public TestAttachmentStorage()
        {
            this.StorageMock.Setup(storage => storage.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()))
                .Returns((string path, Stream content) => this.SaveAsync(path, content));
            this.StorageMock.Setup(storage => storage.GetFileAsync(It.IsAny<string>()))
                .Returns((string path) => Task.FromResult<Stream>(new MemoryStream(this._files[path], writable: false)));
            this.StorageMock.Setup(storage => storage.DeleteFileAsync(It.IsAny<string>()))
                .Returns((string path) => Task.FromResult(this._files.Remove(path)));
        }

        private async Task<string> SaveAsync(string path, Stream content)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            this._files[path] = buffer.ToArray();
            this.SaveCount++;

            return path;
        }
    }

    private sealed class CommitAcknowledgementFailureInterceptor : DbTransactionInterceptor
    {
        public bool HasFailed { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!this.HasFailed)
            {
                this.HasFailed = true;

                throw new NpgsqlException("Тестовый сбой получения результата коммита.", new TimeoutException());
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TransientSaveChangesInterceptor : SaveChangesInterceptor
    {
        public int CallCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            this.CallCount++;
            if (this.CallCount == 2)
            {
                throw new NpgsqlException("Тестовый временный сбой базы данных.", new TimeoutException());
            }

            return ValueTask.FromResult(result);
        }
    }
}
