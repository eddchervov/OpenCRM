using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications.Tests.Integration;

[Collection(ConversationDatabaseCollection.Name)]
public sealed class OrganizationConversationServiceTests(ConversationDatabaseFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var context = fixture.CreateContext();
        // Общая тестовая БД не должна переносить отложенные отправки между проверками.
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"OrganizationConversations\" CASCADE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task InboundMessagesAreTenantScopedDeduplicatedAndUnreadPerEmployee()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, employee) = await fixture.CreateOrganizationAsync(context);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12001));
        var otherOrganization = await fixture.CreateOrganizationAsync(context, "Другая студия");
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(otherOrganization.Organization, 12002, "other_bot"));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        var update = CreateUpdate(organization.Id, 12001, 88001, 3001);

        Assert.True(await conversationService.IngestTelegramMessageAsync(update));
        Assert.False(await conversationService.IngestTelegramMessageAsync(update));
        Assert.True(await conversationService.IngestTelegramMessageAsync(CreateUpdate(otherOrganization.Organization.Id, 12002, 88001, 3002)));

        var ownerInbox = await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto
        {
            OnlyUnread = true
        });
        var employeeInbox = await conversationService.GetInboxAsync(employee.Id, organization.Id, new ConversationInboxQueryDto
        {
            OnlyUnread = true
        });
        Assert.Single(ownerInbox.Items);
        Assert.Equal(1, ownerInbox.UnreadCount);
        Assert.Equal(1, employeeInbox.Items[0].UnreadCount);
        Assert.Equal(1, await context.OrganizationConversationMessages.CountAsync(item =>
            item.Conversation.OrganizationId == organization.Id));
        Assert.Equal(2, await context.OrganizationConversations.CountAsync(item => item.TelegramChatId == 88001));

        Assert.True(await conversationService.MarkReadAsync(owner.Id, organization.Id, ownerInbox.Items[0].Id, 1));
        Assert.True(await conversationService.MarkReadAsync(owner.Id, organization.Id, ownerInbox.Items[0].Id, 0));
        Assert.Equal(0, (await conversationService.GetUnreadSummaryAsync(owner.Id, organization.Id)).UnreadCount);
        Assert.Equal(1, (await conversationService.GetUnreadSummaryAsync(employee.Id, organization.Id)).UnreadCount);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => conversationService.GetInboxAsync(
            owner.Id, otherOrganization.Organization.Id, new ConversationInboxQueryDto()));
    }

    [Fact]
    public async Task InboundPreservesTelegramTextAndLargeAttachmentMetadataWithinConversationDedupScope()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12012));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        var fullText = new string('т', 4096);
        var firstUpdate = CreateUpdate(organization.Id, 12012, 88012, 90012) with
        {
            Text = fullText
        };
        var secondUpdate = CreateUpdate(organization.Id, 12012, 88013, 90012) with
        {
            UpdateId = 90013,
            Text = "Файл из Telegram",
            Attachments =
            [
                new IngestTelegramAttachmentDto
                {
                    Kind = "document",
                    FileName = "large.zip",
                    ContentType = "application/zip",
                    SizeBytes = 50L * 1024 * 1024,
                    ProviderFileId = "telegram-file-90012"
                }
            ]
        };

        Assert.True(await conversationService.IngestTelegramMessageAsync(firstUpdate));
        Assert.True(await conversationService.IngestTelegramMessageAsync(secondUpdate));

        var inbox = await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto());
        Assert.Equal(2, inbox.Items.Count);
        var firstMessages = await conversationService.GetMessagesAsync(owner.Id, organization.Id,
            inbox.Items.Single(item => item.LastMessageText == fullText).Id);
        var secondMessages = await conversationService.GetMessagesAsync(owner.Id, organization.Id,
            inbox.Items.Single(item => item.LastMessageText == "Файл из Telegram").Id);
        Assert.Equal(fullText, Assert.Single(firstMessages!.Items).Text);
        var attachmentMessage = Assert.Single(secondMessages!.Items);
        Assert.Equal("Файл из Telegram", attachmentMessage.Text);
        Assert.Equal(50L * 1024 * 1024, Assert.Single(attachmentMessage.Attachments).SizeBytes);
    }

    [Fact]
    public async Task ConcurrentWebhookDuplicatesAllocateOnlyOneMessageSequence()
    {
        await using var seedContext = fixture.CreateContext();
        var (organization, _, _) = await fixture.CreateOrganizationAsync(seedContext);
        seedContext.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12003));
        await seedContext.SaveChangesAsync();
        var update = CreateUpdate(organization.Id, 12003, 88003, 3003);

        var deliveries = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = fixture.CreateContext();
            using var services = fixture.CreateServices(context);
            return await services.GetRequiredService<IOrganizationConversationService>().IngestTelegramMessageAsync(update);
        });

        var outcomes = await Task.WhenAll(deliveries);
        await using var verifyContext = fixture.CreateContext();
        var conversation = await verifyContext.OrganizationConversations.SingleAsync(item => item.OrganizationId == organization.Id);
        Assert.Single(outcomes, item => item);
        Assert.Equal(1, conversation.LastSequence);
        Assert.Equal(1, await verifyContext.OrganizationConversationMessages.CountAsync(item => item.ConversationId == conversation.Id));
    }

    [Fact]
    public async Task ConcurrentSendRetryWithSameRequestIdReturnsSingleMessage()
    {
        await using var seedContext = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(seedContext);
        seedContext.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12010));
        await seedContext.SaveChangesAsync();
        using (var seedServices = fixture.CreateServices(seedContext))
        {
            await seedServices.GetRequiredService<IOrganizationConversationService>()
                .IngestTelegramMessageAsync(CreateUpdate(organization.Id, 12010, 88010, 3010));
        }

        await using var lookupContext = fixture.CreateContext();
        Guid conversationId;
        using (var lookupServices = fixture.CreateServices(lookupContext))
        {
            conversationId = (await lookupServices.GetRequiredService<IOrganizationConversationService>()
                .GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto())).Items.Single().Id;
        }

        var request = new SendConversationMessageDto { RequestId = Guid.NewGuid(), Text = "Один ответ" };
        var sends = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var context = fixture.CreateContext();
            using var services = fixture.CreateServices(context);
            return await services.GetRequiredService<IOrganizationConversationService>()
                .SendAsync(owner.Id, organization.Id, conversationId, request);
        });
        var results = await Task.WhenAll(sends);

        Assert.Equal(results[0].Id, results[1].Id);
        await using var verifyContext = fixture.CreateContext();
        Assert.Equal(1, await verifyContext.OrganizationConversationMessages.CountAsync(item =>
            item.ConversationId == conversationId && item.Direction == "outgoing"));
    }

    [Fact]
    public async Task SendUsesTransactionalDispatchIdempotencyAndFencedDeliveryResults()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12004));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        var deliveryOutbox = services.GetRequiredService<IConversationDeliveryOutbox>();
        var dispatchOutbox = services.GetRequiredService<IConversationDispatchOutbox>();
        await conversationService.IngestTelegramMessageAsync(CreateUpdate(organization.Id, 12004, 88004, 3004));
        var conversation = (await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto())).Items.Single();
        var requestId = Guid.NewGuid();
        var request = new SendConversationMessageDto { RequestId = requestId, Text = "Здравствуйте!" };

        var first = await conversationService.SendAsync(owner.Id, organization.Id, conversation.Id, request);
        var repeated = await conversationService.SendAsync(owner.Id, organization.Id, conversation.Id, request);
        Assert.Equal(first.Id, repeated.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversationService.SendAsync(
            owner.Id, organization.Id, conversation.Id, request with { Text = "Иной текст" }));
        Assert.Single(await context.OrganizationConversationMessages.Where(item => item.Direction == "outgoing").ToListAsync());

        var dispatch = Assert.Single(await dispatchOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromSeconds(30)));
        Assert.Equal("telegram", dispatch.ProviderCode);
        Assert.True(await dispatchOutbox.CompletePublicationAsync(dispatch.DispatchId, dispatch.LeaseId, false));
        var recoveredDispatch = Assert.Single(await dispatchOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromSeconds(30)));
        Assert.Equal(dispatch.DispatchId, recoveredDispatch.DispatchId);
        Assert.True(await dispatchOutbox.CompletePublicationAsync(recoveredDispatch.DispatchId, recoveredDispatch.LeaseId, true));
        var firstLease = Assert.IsType<ConversationDeliveryLeaseDto>(await deliveryOutbox.ClaimByMessageIdAsync(
            first.Id, requestId, TimeSpan.FromSeconds(30)));
        Assert.False(firstLease.IsRecovery);
        Assert.True(await deliveryOutbox.CompleteAsync(first.Id, firstLease.LeaseId, new ConversationDeliveryResultDto
        {
            Status = ConversationMessageStatus.Uncertain,
            ErrorCode = "timeout",
            CompletedAt = DateTime.UtcNow
        }));
        Assert.False(await deliveryOutbox.CompleteAsync(first.Id, firstLease.LeaseId, new ConversationDeliveryResultDto
        {
            Status = ConversationMessageStatus.Accepted,
            ProviderMessageId = "3005",
            CompletedAt = DateTime.UtcNow
        }));
        Assert.Null(await deliveryOutbox.ClaimByMessageIdAsync(first.Id, requestId, TimeSpan.FromSeconds(30)));

        var retryRequestId = Guid.NewGuid();
        var retryMessage = await conversationService.SendAsync(owner.Id, organization.Id, conversation.Id, new SendConversationMessageDto
        {
            RequestId = retryRequestId,
            Text = "Второй ответ"
        });
        var retryDispatch = Assert.Single(await dispatchOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromSeconds(30)));
        Assert.True(await dispatchOutbox.CompletePublicationAsync(retryDispatch.DispatchId, retryDispatch.LeaseId, true));
        var retryLease = Assert.IsType<ConversationDeliveryLeaseDto>(await deliveryOutbox.ClaimByMessageIdAsync(
            retryMessage.Id, retryRequestId, TimeSpan.FromSeconds(30)));
        var activeDelivery = await Assert.ThrowsAsync<ConversationDeliveryDeferredException>(() => deliveryOutbox.ClaimByMessageIdAsync(
            retryMessage.Id, retryRequestId, TimeSpan.FromSeconds(30)));
        Assert.Equal("delivery-in-progress", activeDelivery.ReasonCode);
        Assert.False(await deliveryOutbox.CompleteAsync(retryMessage.Id, Guid.NewGuid(), new ConversationDeliveryResultDto
        {
            Status = ConversationMessageStatus.Accepted,
            ProviderMessageId = "wrong-lease",
            CompletedAt = DateTime.UtcNow
        }));
        await context.OrganizationConversationMessages
            .Where(item => item.Id == retryMessage.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeaseExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        context.ChangeTracker.Clear();
        var recoveredLease = Assert.IsType<ConversationDeliveryLeaseDto>(await deliveryOutbox.ClaimByMessageIdAsync(
            retryMessage.Id, retryRequestId, TimeSpan.FromSeconds(30)));
        Assert.True(recoveredLease.IsRecovery);
        Assert.Equal(retryLease.LeaseId, recoveredLease.LeaseId);
        Assert.True(await deliveryOutbox.CompleteAsync(retryMessage.Id, recoveredLease.LeaseId, new ConversationDeliveryResultDto
        {
            Status = ConversationMessageStatus.Failed,
            ErrorCode = "rate_limited_before_publish",
            SafeToRetry = true,
            RetryAfterSeconds = 60,
            CompletedAt = DateTime.UtcNow
        }));
        Assert.Empty(await dispatchOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromSeconds(30)));
        var scheduledRetry = await Assert.ThrowsAsync<ConversationDeliveryDeferredException>(() => deliveryOutbox.ClaimByMessageIdAsync(
            retryMessage.Id, retryRequestId, TimeSpan.FromSeconds(30)));
        Assert.Equal("delivery-retry-scheduled", scheduledRetry.ReasonCode);
        await context.OrganizationConversationMessages
            .Where(item => item.Id == retryMessage.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RetryAt, DateTime.UtcNow.AddSeconds(-1)));
        context.ChangeTracker.Clear();
        var retryDispatchAgain = Assert.Single(await dispatchOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromSeconds(30)));
        Assert.True(await dispatchOutbox.CompletePublicationAsync(retryDispatchAgain.DispatchId, retryDispatchAgain.LeaseId, true));
        var newLease = Assert.IsType<ConversationDeliveryLeaseDto>(await deliveryOutbox.ClaimByMessageIdAsync(
            retryMessage.Id, retryRequestId, TimeSpan.FromSeconds(30)));
        Assert.NotEqual(retryLease.LeaseId, newLease.LeaseId);
        Assert.True(await deliveryOutbox.CompleteAsync(retryMessage.Id, newLease.LeaseId, new ConversationDeliveryResultDto
        {
            Status = ConversationMessageStatus.Failed,
            ErrorCode = "safe_retry",
            SafeToRetry = true,
            CompletedAt = DateTime.UtcNow
        }));
        var lastDispatch = Assert.Single(await dispatchOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromSeconds(30)));
        Assert.True(await dispatchOutbox.CompletePublicationAsync(lastDispatch.DispatchId, lastDispatch.LeaseId, true));
        await context.OrganizationConversationMessages
            .Where(item => item.Id == retryMessage.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DeliveryAttemptCount, 4));
        context.ChangeTracker.Clear();
        var lastAttempt = Assert.IsType<ConversationDeliveryLeaseDto>(await deliveryOutbox.ClaimByMessageIdAsync(
            retryMessage.Id, retryRequestId, TimeSpan.FromSeconds(30)));
        Assert.True(await deliveryOutbox.CompleteAsync(retryMessage.Id, lastAttempt.LeaseId, new ConversationDeliveryResultDto
        {
            Status = ConversationMessageStatus.Failed,
            ErrorCode = "provider_rejected",
            SafeToRetry = true,
            CompletedAt = DateTime.UtcNow
        }));
        Assert.False((await context.OrganizationConversationMessages.AsNoTracking()
            .SingleAsync(item => item.Id == retryMessage.Id)).SafeToRetry);
        Assert.Empty(await dispatchOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task DowngradeKeepsConversationReadableAndCancelsQueuedMessageBeforeProviderLease()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12031));
        await context.SaveChangesAsync();
        var incoming = CreateUpdate(organization.Id, 12031, 88031, 3031);
        Guid conversationId;
        Guid messageId;
        Guid requestId;
        Guid dispatchId;
        Guid dispatchLeaseId;

        using (var proServices = fixture.CreateServices(context))
        {
            var conversationService = proServices.GetRequiredService<IOrganizationConversationService>();
            await conversationService.IngestTelegramMessageAsync(incoming);
            var conversation = Assert.Single((await conversationService.GetInboxAsync(
                owner.Id, organization.Id, new ConversationInboxQueryDto())).Items);
            conversationId = conversation.Id;
            requestId = Guid.NewGuid();
            var outgoing = await conversationService.SendAsync(owner.Id, organization.Id, conversationId,
                new SendConversationMessageDto { RequestId = requestId, Text = "До окончания тарифа" });
            messageId = outgoing.Id;

            var dispatchOutbox = proServices.GetRequiredService<IConversationDispatchOutbox>();
            var dispatch = Assert.Single(await dispatchOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromSeconds(30)));
            dispatchId = dispatch.DispatchId;
            dispatchLeaseId = dispatch.LeaseId;
            Assert.True(await dispatchOutbox.CompletePublicationAsync(dispatchId, dispatchLeaseId, true));
        }

        using var freeServices = fixture.CreateServices(context, hasClientMessagingFeature: false);
        var readService = freeServices.GetRequiredService<IOrganizationConversationService>();
        var page = (await readService.GetMessagesAsync(owner.Id, organization.Id, conversationId))!;
        Assert.False(page.CanSend);
        Assert.False(page.CanManage);
        var messages = page.Items;
        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, message => message.Id == messageId);
        var deliveryOutbox = freeServices.GetRequiredService<IConversationDeliveryOutbox>();

        Assert.Null(await deliveryOutbox.ClaimByMessageIdAsync(messageId, requestId, TimeSpan.FromSeconds(30)));
        var persisted = await context.OrganizationConversationMessages.AsNoTracking().SingleAsync(item => item.Id == messageId);
        Assert.Equal("cancelled", persisted.Status);
        Assert.Equal("client-messaging-pro-required", persisted.ErrorCode);
        Assert.False(persisted.SafeToRetry);
        Assert.Null(persisted.RetryAt);
    }

    [Fact]
    public async Task DeliveryClaimsPreserveConversationOrderAndReplayPublishedWakeupsWithBackoff()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12015));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        var deliveryOutbox = services.GetRequiredService<IConversationDeliveryOutbox>();
        await conversationService.IngestTelegramMessageAsync(CreateUpdate(organization.Id, 12015, 88015, 3015));
        var conversation = (await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto())).Items.Single();
        var firstRequestId = Guid.NewGuid();
        var secondRequestId = Guid.NewGuid();
        var first = await conversationService.SendAsync(owner.Id, organization.Id, conversation.Id,
            new SendConversationMessageDto { RequestId = firstRequestId, Text = "Первое сообщение" });
        var second = await conversationService.SendAsync(owner.Id, organization.Id, conversation.Id,
            new SendConversationMessageDto { RequestId = secondRequestId, Text = "Второе сообщение" });

        var firstLease = Assert.IsType<ConversationDeliveryLeaseDto>(await deliveryOutbox.ClaimByMessageIdAsync(
            first.Id, firstRequestId, TimeSpan.FromSeconds(30)));
        var deferred = await Assert.ThrowsAsync<ConversationDeliveryDeferredException>(() => deliveryOutbox.ClaimByMessageIdAsync(
            second.Id, secondRequestId, TimeSpan.FromSeconds(30)));
        Assert.Equal("earlier-delivery-pending", deferred.ReasonCode);
        Assert.True(deferred.RetryAfter >= TimeSpan.FromSeconds(1));
        Assert.True(await deliveryOutbox.CompleteAsync(first.Id, firstLease.LeaseId, new ConversationDeliveryResultDto
        {
            Status = ConversationMessageStatus.Accepted,
            ProviderMessageId = "telegram-3016",
            CompletedAt = DateTime.UtcNow
        }));
        var secondLease = Assert.IsType<ConversationDeliveryLeaseDto>(await deliveryOutbox.ClaimByMessageIdAsync(
            second.Id, secondRequestId, TimeSpan.FromSeconds(30)));
        Assert.True(await deliveryOutbox.CompleteAsync(second.Id, secondLease.LeaseId, new ConversationDeliveryResultDto
        {
            Status = ConversationMessageStatus.Accepted,
            ProviderMessageId = "telegram-3017",
            CompletedAt = DateTime.UtcNow
        }));
    }

    [Fact]
    public async Task PublishedQueuedDispatchIsReplayedOnlyWhenItsBackoffIsDue()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12018));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        var dispatchOutbox = services.GetRequiredService<IConversationDispatchOutbox>();
        await conversationService.IngestTelegramMessageAsync(CreateUpdate(organization.Id, 12018, 88018, 3018));
        var conversation = (await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto())).Items.Single();
        var message = await conversationService.SendAsync(owner.Id, organization.Id, conversation.Id,
            new SendConversationMessageDto { RequestId = Guid.NewGuid(), Text = "Не потерять при потере команды" });
        var dispatchBatch = await dispatchOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromSeconds(30));
        var firstDispatch = Assert.Single(dispatchBatch, item => item.MessageId == message.Id);
        Assert.False(firstDispatch.IsReplay);
        Assert.True(await dispatchOutbox.CompletePublicationAsync(firstDispatch.DispatchId, firstDispatch.LeaseId, true));
        Assert.Empty(await dispatchOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromSeconds(30)));

        await context.OrganizationConversationDispatches
            .Where(item => item.MessageId == message.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.NextReconcileAt, DateTime.UtcNow.AddSeconds(-1))
                .SetProperty(item => item.PublishedAt, DateTime.UtcNow.AddMinutes(-16)));
        context.ChangeTracker.Clear();

        var replayBatch = await dispatchOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromSeconds(30));
        var replay = Assert.Single(replayBatch, item => item.MessageId == message.Id);
        Assert.True(replay.IsReplay);
        Assert.Equal(firstDispatch.DispatchId, replay.DispatchId);
        Assert.True(await dispatchOutbox.CompletePublicationAsync(replay.DispatchId, replay.LeaseId, true));
        context.ChangeTracker.Clear();
        var persistedDispatch = await context.OrganizationConversationDispatches.SingleAsync(item => item.MessageId == message.Id);
        Assert.Equal(1, persistedDispatch.PublicationReplayCount);
        Assert.True(persistedDispatch.NextReconcileAt > DateTime.UtcNow.AddMinutes(20));
    }

    [Fact]
    public async Task MaxMessageTextLimitAllowsLongCaptionsAndRejectsMoreThanFourThousandCharacters()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        const string botIdentity = "max-bot-communications-test";
        context.OrganizationMaxBots.Add(new OrganizationMaxBot
        {
            OrganizationId = organization.Id,
            Username ="max_communications_test",
            ImmutableProviderBotId = botIdentity,
            ProtectedToken = "protected-token",
            ProtectedWebhookSecret = "protected-secret",
            IsEnabled = true
        });
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var channelService = services.GetRequiredService<IOrganizationChannelService>();
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        var connectionId = await channelService.EnsureConnectionAsync(new EnsureOrganizationChannelConnectionDto
        {
            OrganizationId = organization.Id,
            ProviderCode = "max",
            ImmutableProviderIdentity = botIdentity,
            Username ="max_communications_test",
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow
        });
        Assert.True(await channelService.IngestMessageAsync(new ChannelMessageReceivedDto
        {
            OrganizationId = organization.Id,
            ConnectionId = connectionId,
            ProviderCode = "max",
            ExternalConversationId = "max-chat-1",
            ExternalUserId = "max-user-1",
            ProviderMessageId = "max-message-1",
            Text = "Входящее сообщение",
            OccurredAt = DateTime.UtcNow
        }));

        var conversation = (await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto()))
            .Items.Single();
        var attachment = new OrganizationConversationAttachment
        {
            OrganizationId = organization.Id,
            ConversationId = conversation.Id,
            UploadedByUserId = owner.Id,
            Kind = "photo",
            FileName = "photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 128,
            StorageReference = "test-attachment",
            CreatedAt = DateTime.UtcNow
        };
        context.OrganizationConversationAttachments.Add(attachment);
        await context.SaveChangesAsync();
        var caption = await conversationService.SendAsync(owner.Id, organization.Id, conversation.Id, new SendConversationMessageDto
        {
            RequestId = Guid.NewGuid(),
            Text = new string('а', 1200),
            AttachmentIds = [attachment.Id]
        });
        Assert.Equal(1200, caption.Text!.Length);
        Assert.True(await context.OrganizationChannelConnections.AsNoTracking()
            .AnyAsync(item => item.Id == connectionId && item.IsCurrent && item.ImmutableProviderIdentity == botIdentity));
        Assert.True(await context.OrganizationMaxBots.AsNoTracking()
            .AnyAsync(item => item.OrganizationId == organization.Id && item.IsEnabled && item.ImmutableProviderBotId == botIdentity));
        var persistedConversation = await context.OrganizationConversations.AsNoTracking()
            .SingleAsync(item => item.Id == conversation.Id);
        Assert.False(persistedConversation.IsReadOnly);
        Assert.True(persistedConversation.IsChatActive);
        Assert.Equal(connectionId, persistedConversation.ConnectionId);

        var maximum = await conversationService.SendAsync(owner.Id, organization.Id, conversation.Id, new SendConversationMessageDto
        {
            RequestId = Guid.NewGuid(),
            Text = new string('б', 4000)
        });
        Assert.Equal(4000, maximum.Text!.Length);
        await Assert.ThrowsAsync<ArgumentException>(() => conversationService.SendAsync(
            owner.Id, organization.Id, conversation.Id,
            new SendConversationMessageDto { RequestId = Guid.NewGuid(), Text = new string('в', 4001) }));
    }

    [Fact]
    public async Task TelegramCaptionCannotExceedOneThousandTwentyFourCharacters()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12019));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        await conversationService.IngestTelegramMessageAsync(CreateUpdate(organization.Id, 12019, 88019, 3019));
        var conversation = (await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto()))
            .Items.Single();
        var attachment = new OrganizationConversationAttachment
        {
            OrganizationId = organization.Id,
            ConversationId = conversation.Id,
            UploadedByUserId = owner.Id,
            Kind = "photo",
            FileName = "photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 128,
            StorageReference = "test-attachment",
            CreatedAt = DateTime.UtcNow
        };
        context.OrganizationConversationAttachments.Add(attachment);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => conversationService.SendAsync(
            owner.Id, organization.Id, conversation.Id,
            new SendConversationMessageDto
            {
                RequestId = Guid.NewGuid(),
                Text = new string('а', 1025),
                AttachmentIds = [attachment.Id]
            }));
    }

    [Fact]
    public async Task ClientInboxIncludesVerifiedCurrentChannelWithoutMessages()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        var client = new OrganizationClient
        {
            OrganizationId = organization.Id,
            Name = "Клиент с двумя каналами",
            TelegramChatId = 88020,
            TelegramConnectedAt = DateTime.UtcNow,
            TelegramNotificationsEnabled = true,
            MaxUserId = 99220,
            MaxConnectedAt = DateTime.UtcNow,
            MaxNotificationsEnabled = true
        };
        context.OrganizationClients.Add(client);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12020));
        context.OrganizationMaxBots.Add(new OrganizationMaxBot
        {
            OrganizationId = organization.Id,
            Username ="max_communications_two_channels",
            ImmutableProviderBotId = "max-bot-two-channels",
            ProtectedToken = "protected-token",
            ProtectedWebhookSecret = "protected-secret",
            IsEnabled = true
        });
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var channelService = services.GetRequiredService<IOrganizationChannelService>();
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        _ = await channelService.EnsureConnectionAsync(new EnsureOrganizationChannelConnectionDto
        {
            OrganizationId = organization.Id,
            ProviderCode = "telegram",
            ImmutableProviderIdentity = "12020",
            Username ="studio_12020_bot",
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow
        });
        var maxConnectionId = await channelService.EnsureConnectionAsync(new EnsureOrganizationChannelConnectionDto
        {
            OrganizationId = organization.Id,
            ProviderCode = "max",
            ImmutableProviderIdentity = "max-bot-two-channels",
            Username ="max_communications_two_channels",
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow
        });
        await conversationService.IngestTelegramMessageAsync(CreateUpdate(organization.Id, 12020, 88020, 3020));

        var group = Assert.Single((await conversationService.GetClientInboxAsync(
            owner.Id, organization.Id, new ConversationInboxQueryDto())).Items);
        Assert.Equal(client.Id, group.ClientId);
        Assert.Equal(2, group.Routes.Count);
        var maxRoute = Assert.Single(group.Routes, route => route.ChannelCode == "max");
        Assert.Null(maxRoute.LastMessageAt);
        Assert.False(maxRoute.IsReadOnly);
        Assert.Equal(maxConnectionId, maxRoute.ConnectionId);
        var maxConversation = await context.OrganizationConversations.AsNoTracking()
            .SingleAsync(item => item.Id == maxRoute.Id);
        Assert.Equal("99220", maxConversation.DeliveryAddress);

        Assert.True(await channelService.IngestMessageAsync(new ChannelMessageReceivedDto
        {
            OrganizationId = organization.Id,
            ConnectionId = maxConnectionId,
            ProviderCode = "max",
            ExternalConversationId = maxConversation.ExternalConversationId!,
            ExternalUserId = "99220",
            ProviderMessageId = "max-read-preview",
            Text = "Новое прочитанное сообщение MAX",
            OccurredAt = DateTime.UtcNow.AddMinutes(1)
        }));
        Assert.True(await conversationService.MarkReadAsync(owner.Id, organization.Id, maxRoute.Id, 1));

        var unreadGroup = Assert.Single((await conversationService.GetClientInboxAsync(
            owner.Id, organization.Id, new ConversationInboxQueryDto { OnlyUnread = true })).Items);
        var telegramRoute = Assert.Single(unreadGroup.Routes, route => route.ChannelCode == "telegram");
        Assert.Equal(2, unreadGroup.Routes.Count);
        Assert.Equal(telegramRoute.LastMessageAt, unreadGroup.LastMessageAt);
        Assert.Equal(telegramRoute.LastMessageText, unreadGroup.LastMessageText);
        Assert.Equal(1, unreadGroup.UnreadCount);
    }

    [Fact]
    public async Task UnknownSenderMustBeLinkedExplicitlyAndThenAppearsInClientConversation()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        var client = new OrganizationClient { OrganizationId = organization.Id, Name = "Новый клиент" };
        context.OrganizationClients.Add(client);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12006));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        await conversationService.IngestTelegramMessageAsync(CreateUpdate(organization.Id, 12006, 88006, 3006));
        var conversation = (await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto())).Items.Single();

        Assert.Null(conversation.ClientId);
        Assert.Null(await conversationService.GetForClientAsync(owner.Id, organization.Id, client.Id));
        Assert.True(await conversationService.LinkClientAsync(owner.Id, organization.Id, conversation.Id, client.Id));

        var clientMessages = await conversationService.GetForClientAsync(owner.Id, organization.Id, client.Id);
        Assert.NotNull(clientMessages);
        Assert.Equal(conversation.Id, clientMessages.Conversation.Id);
        var clientInbox = await conversationService.GetClientInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto());
        var clientGroup = Assert.Single(clientInbox.Items);
        Assert.Equal(client.Id, clientGroup.ClientId);
        Assert.Equal(conversation.Id, Assert.Single(clientGroup.Routes).Id);
    }

    [Fact]
    public async Task PermissionsAndBotSwitchKeepHistoryReadableButBlockMutation()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        var oldBot = ConversationDatabaseFixture.AddBot(organization, 12007);
        context.OrganizationTelegramBots.Add(oldBot);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        await conversationService.IngestTelegramMessageAsync(CreateUpdate(organization.Id, 12007, 88007, 3007));
        var conversation = (await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto())).Items.Single();

        await using (var deniedSendContext = fixture.CreateContext())
        {
            using var deniedSendServices = fixture.CreateServices(deniedSendContext, new HashSet<string> { "messages.send" });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => deniedSendServices.GetRequiredService<IOrganizationConversationService>()
                .SendAsync(owner.Id, organization.Id, conversation.Id,
                    new SendConversationMessageDto { RequestId = Guid.NewGuid(), Text = "Нет права" }));
        }

        await using (var deniedManageContext = fixture.CreateContext())
        {
            using var deniedManageServices = fixture.CreateServices(deniedManageContext, new HashSet<string> { "messages.manage" });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => deniedManageServices.GetRequiredService<IOrganizationConversationService>()
                .LinkClientAsync(owner.Id, organization.Id, conversation.Id, null));
        }

        oldBot.TelegramBotId = 12008;
        oldBot.Username ="new_studio_bot";
        await context.SaveChangesAsync();
        var history = await conversationService.GetMessagesAsync(owner.Id, organization.Id, conversation.Id);
        Assert.True(history!.IsReadOnly);
        Assert.False(history.CanSend);
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversationService.SendAsync(owner.Id, organization.Id, conversation.Id,
            new SendConversationMessageDto { RequestId = Guid.NewGuid(), Text = "Нельзя" }));

        using var deniedServices = fixture.CreateServices(context, new HashSet<string> { "messages.view" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => deniedServices.GetRequiredService<IOrganizationConversationService>()
            .GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto()));
    }

    [Fact]
    public async Task TelegramChatLifecycleIgnoresStaleEventsAndOnlyExplicitStartReactivatesChat()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12009));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        await conversationService.IngestTelegramMessageAsync(CreateUpdate(organization.Id, 12009, 88009, 3009));
        var conversation = (await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto())).Items.Single();
        var changedAt = DateTime.UtcNow;

        await conversationService.SetTelegramChatActiveAsync(organization.Id, 12009, 88009, false, changedAt);
        var disconnected = await conversationService.GetMessagesAsync(owner.Id, organization.Id, conversation.Id);
        Assert.True(disconnected!.IsReadOnly);

        await conversationService.SetTelegramChatActiveAsync(organization.Id, 12009, 88009, true, changedAt.AddSeconds(-1));
        Assert.True((await conversationService.GetMessagesAsync(owner.Id, organization.Id, conversation.Id))!.IsReadOnly);

        await conversationService.SetTelegramChatActiveAsync(organization.Id, 12009, 88009, true, changedAt.AddSeconds(1));
        Assert.False((await conversationService.GetMessagesAsync(owner.Id, organization.Id, conversation.Id))!.IsReadOnly);
    }

    [Fact]
    public async Task TelegramClientRebindClosesPreviousChatAndKeepsCurrentConversationSelected()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        var client = new OrganizationClient
        {
            OrganizationId = organization.Id,
            Name = "Клиент со сменой аккаунта",
            TelegramChatId = 88016,
            TelegramNotificationsEnabled = true
        };
        context.OrganizationClients.Add(client);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12016));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        await conversationService.IngestTelegramMessageAsync(CreateUpdate(organization.Id, 12016, 88016, 3016));
        var oldConversation = (await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto()))
            .Items.Single();
        var occurredAt = DateTime.UtcNow.AddMinutes(1);

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.True(await conversationService.BindTelegramClientAsync(
                organization.Id, 12016, 88017, client.Id, occurredAt));
            client.TelegramChatId = 88017;
            client.TelegramConnectedAt = occurredAt;
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var currentConversation = (await conversationService.GetForClientAsync(owner.Id, organization.Id, client.Id))!;
        Assert.NotEqual(oldConversation.Id, currentConversation.ConversationId);
        Assert.False(currentConversation.IsReadOnly);
        var retiredConversation = await conversationService.GetMessagesAsync(owner.Id, organization.Id, oldConversation.Id);
        Assert.True(retiredConversation!.IsReadOnly);
        Assert.False(await conversationService.BindTelegramClientAsync(
            organization.Id, 12016, 88016, client.Id, occurredAt.AddSeconds(-1)));
        Assert.Equal(currentConversation.ConversationId,
            (await conversationService.GetForClientAsync(owner.Id, organization.Id, client.Id))!.ConversationId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversationService.SendAsync(
            owner.Id, organization.Id, oldConversation.Id,
            new SendConversationMessageDto { RequestId = Guid.NewGuid(), Text = "Ответ в старый чат" }));
        var sent = await conversationService.SendAsync(owner.Id, organization.Id, currentConversation.ConversationId,
            new SendConversationMessageDto { RequestId = Guid.NewGuid(), Text = "Ответ в новый чат" });
        Assert.Equal(ConversationMessageStatus.Queued, sent.Status);
    }

    [Fact]
    public async Task UnauthorizedClientSearchAndMaintenanceLinkAreNotExposed()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        var client = new OrganizationClient { OrganizationId = organization.Id, Name = "Скрытый клиент" };
        context.OrganizationClients.Add(client);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12005));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context, new HashSet<string> { "clients.view" });
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        await conversationService.IngestTelegramMessageAsync(CreateUpdate(organization.Id, 12005, 88005, 3005));
        var inbox = await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto
        {
            Search = "Скрытый"
        });
        Assert.Empty(inbox.Items);
        var clientInbox = await conversationService.GetClientInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto());
        Assert.Null(Assert.Single(clientInbox.Items).ClientId);
        Assert.NotEqual(client.Id, clientInbox.Items[0].GroupId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => conversationService.GetInboxAsync(
            owner.Id, organization.Id, new ConversationInboxQueryDto { ClientId = client.Id }));
    }

    private static IngestTelegramMessageDto CreateUpdate(Guid organizationId, long botId, long userId, long messageId) => new()
    {
        OrganizationId = organizationId,
        BotId = botId,
        BotUsername ="studio_bot",
        UpdateId = messageId + 10000,
        ChatId = userId,
        SenderTelegramUserId = userId,
        SenderDisplayName = "Тестовый клиент",
        SenderUsername ="test_client",
        ProviderMessageId = messageId,
        Text = "Добрый день, подскажите стоимость?",
        SentAt = DateTime.UtcNow
    };
}
