using System.Text.Json;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using MassTransit;
using RabbitMQ.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Notifications.Domain;
using Notifications.Infrastructure;
using Testcontainers.RabbitMq;

namespace Notifications.Tests.Integration;

/// <summary>Проверяет публичные команды и результаты через настоящие RabbitMQ и PostgreSQL.</summary>
[Trait("Category", "Integration")]
public sealed class MessagingTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>
{
    /// <summary>Одинаковый request одного сообщения из разных каналов сохраняется отдельно.</summary>
    [Fact]
    public async Task ConversationInboxDeduplicatesByProviderMessageAndRequest()
    {
        var messageId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        using var scope = fixture.Services.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<ConversationDeliveryInboxStore>();
        await inbox.EnqueueAsync(ConversationDeliveryProvider.Telegram, messageId, requestId, CancellationToken.None);
        await inbox.EnqueueAsync(ConversationDeliveryProvider.Max, messageId, requestId, CancellationToken.None);
        await inbox.EnqueueAsync(ConversationDeliveryProvider.Max, messageId, requestId, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var savedProviders = await db.ConversationDeliveryInbox
            .Where(item => item.MessageId == messageId && item.RequestId == requestId)
            .Select(item => item.ProviderCode)
            .OrderBy(providerCode => providerCode)
            .ToArrayAsync();

        Assert.Equal(["max", "telegram"], savedProviders);
    }

    /// <summary>Два экземпляра MAX-транспорта разделяют лимит одного диалога через PostgreSQL.</summary>
    [Fact]
    public async Task MaxConversationRateLimitIsSharedAcrossInstances()
    {
        using var firstScope = fixture.Services.CreateScope();
        using var secondScope = fixture.Services.CreateScope();
        var firstLimiter = new MaxConversationRateLimiter(
            firstScope.ServiceProvider.GetRequiredService<NotificationDbContext>(),
            TimeProvider.System);
        var secondLimiter = new MaxConversationRateLimiter(
            secondScope.ServiceProvider.GetRequiredService<NotificationDbContext>(),
            TimeProvider.System);
        var firstLease = await firstLimiter.AcquireConversationAsync("bot-1:user-1", CancellationToken.None);
        var secondStartedAt = Stopwatch.GetTimestamp();
        var secondLeaseTask = secondLimiter.AcquireConversationAsync("bot-1:user-1", CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(secondLeaseTask.IsCompleted);

        await firstLease.DisposeAsync();
        var secondLease = await secondLeaseTask;
        var elapsed = Stopwatch.GetElapsedTime(secondStartedAt);
        await secondLease.DisposeAsync();

        Assert.True(elapsed >= TimeSpan.FromMilliseconds(450));
    }

    /// <summary>Два MAX-транспорта резервируют глобальные API интервалы через PostgreSQL.</summary>
    [Fact]
    public async Task MaxGlobalApiRequestRateLimitIsSharedAcrossInstances()
    {
        using var firstScope = fixture.Services.CreateScope();
        using var secondScope = fixture.Services.CreateScope();
        var firstLimiter = new MaxConversationRateLimiter(
            firstScope.ServiceProvider.GetRequiredService<NotificationDbContext>(),
            TimeProvider.System);
        var secondLimiter = new MaxConversationRateLimiter(
            secondScope.ServiceProvider.GetRequiredService<NotificationDbContext>(),
            TimeProvider.System);
        var firstStartedAt = Stopwatch.GetTimestamp();

        await Task.WhenAll(
            firstLimiter.WaitForGlobalRequestSlotAsync(CancellationToken.None),
            secondLimiter.WaitForGlobalRequestSlotAsync(CancellationToken.None));

        Assert.True(Stopwatch.GetElapsedTime(firstStartedAt) >= TimeSpan.FromMilliseconds(60));
    }

    [Fact]
    public async Task DurableConversationWakeupSurvivesRabbitRestartAndInboxReplayIsIdempotent()
    {
        var messageId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var providerCalls = 0;
        var queueName = $"conversation-restart-{Guid.NewGuid():N}";
        using (var cleanupScope = fixture.Services.CreateScope())
        {
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            await cleanupDb.ConversationDeliveryInbox
                .Where(item => item.ProviderCode == "max")
                .ExecuteDeleteAsync();
        }

        await using var rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await rabbitMq.StartAsync(timeout.Token);

        var factory = new ConnectionFactory
        {
            HostName = rabbitMq.Hostname,
            Port = rabbitMq.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq"
        };
        await PublishDurableWakeupAsync(factory, queueName, messageId, requestId);
        await rabbitMq.StopAsync(timeout.Token);
        await rabbitMq.StartAsync(timeout.Token);
        factory.Port = rabbitMq.GetMappedPublicPort(5672);

        await using var connection = await ConnectRabbitMqAsync(factory, timeout.Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
        var queued = await channel.QueueDeclarePassiveAsync(queueName, timeout.Token);
        Assert.Equal(1u, queued.MessageCount);

        using var scope = fixture.Services.CreateScope();
        var inboxStore = scope.ServiceProvider.GetRequiredService<ConversationDeliveryInboxStore>();
        var firstDelivery = await channel.BasicGetAsync(queueName, autoAck: false, timeout.Token);
        Assert.NotNull(firstDelivery);
        var firstWakeup = JsonSerializer.Deserialize<ConversationDeliveryWakeup>(firstDelivery.Body.Span);
        Assert.NotNull(firstWakeup);
        await inboxStore.EnqueueAsync(firstWakeup.ProviderCode, firstWakeup.MessageId, firstWakeup.RequestId, timeout.Token);
        await channel.BasicAckAsync(firstDelivery.DeliveryTag, multiple: false, timeout.Token);

        await PublishDurableWakeupAsync(factory, queueName, messageId, requestId, timeout.Token);
        var replayDelivery = await channel.BasicGetAsync(queueName, autoAck: false, timeout.Token);
        Assert.NotNull(replayDelivery);
        var replayWakeup = JsonSerializer.Deserialize<ConversationDeliveryWakeup>(replayDelivery.Body.Span);
        Assert.NotNull(replayWakeup);
        await inboxStore.EnqueueAsync(replayWakeup.ProviderCode, replayWakeup.MessageId, replayWakeup.RequestId, timeout.Token);
        await channel.BasicAckAsync(replayDelivery.DeliveryTag, multiple: false, timeout.Token);

        var claim = Assert.Single(await inboxStore.ClaimDueAsync(
            ConversationDeliveryProvider.Max,
            1,
            TimeSpan.FromMinutes(5),
            timeout.Token));
        providerCalls++;
        await inboxStore.CompleteAsync(claim, claim.LeaseId!.Value, null, null, timeout.Token);
        using var verificationScope = fixture.Services.CreateScope();
        var verificationStore = verificationScope.ServiceProvider.GetRequiredService<ConversationDeliveryInboxStore>();
        var duplicateClaim = await verificationStore.ClaimDueAsync(
                ConversationDeliveryProvider.Max,
                1,
                TimeSpan.FromMinutes(5),
                timeout.Token);
        var remaining = await channel.QueueDeclarePassiveAsync(queueName, timeout.Token);
        var inboxCount = await scope.ServiceProvider.GetRequiredService<NotificationDbContext>()
            .ConversationDeliveryInbox
            .CountAsync(item => item.ProviderCode == "max" && item.MessageId == messageId && item.RequestId == requestId,
                timeout.Token);

        Assert.Empty(duplicateClaim);
        Assert.Equal(0u, remaining.MessageCount);
        Assert.Equal(1, inboxCount);
        Assert.Equal(1, providerCalls);
    }

    /// <summary>Параллельные повторы с разными MessageId создают одну запись и одну доставку.</summary>
    [Fact]
    public async Task ConcurrentDuplicatesProduceOneDelivery()
    {
        var key = Guid.NewGuid().ToString("N");
        var request = CreateRequest(key);
        await Task.WhenAll(
            fixture.SendAsync("opencrm", new SendNotification(request)),
            fixture.SendAsync("opencrm", new SendNotification(request with { IdempotencyKey = $" {key} " })));

        var sent = await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "simulated");

        await fixture.SendAsync("opencrm", new GetNotificationStatus(key));
        var snapshot = await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "simulated");
        Assert.Equal(sent.NotificationId, snapshot.NotificationId);
        Assert.StartsWith("log:", snapshot.ProviderMessageId);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var notification = await db.Notifications.Include(item => item.Attempts)
            .SingleAsync(item => item.ApplicationId == "opencrm" && item.IdempotencyKey == key);
        Assert.Single(notification.Attempts);
        Assert.Equal(1, notification.AttemptCount);
    }

    /// <summary>Одинаковые ключи разных приложений не смешивают данные и результаты.</summary>
    [Fact]
    public async Task ApplicationsAreIsolatedByInputQueue()
    {
        var key = Guid.NewGuid().ToString("N");
        await fixture.SendAsync("opencrm", new SendNotification(CreateRequest(key)));
        var opencrm = await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "simulated");

        await fixture.SendAsync("catalog", new GetNotificationStatus(key));
        var missing = await fixture.WaitAsync<NotificationStatusChanged>(
            "catalog", result => result.IdempotencyKey == key);
        Assert.Equal("not-found", missing.Status);
        Assert.Null(missing.NotificationId);

        await fixture.SendAsync("catalog", new SendNotification(CreateRequest(key)));
        var catalog = await fixture.WaitAsync<NotificationStatusChanged>(
            "catalog", result => result.IdempotencyKey == key && result.Status == "simulated");
        Assert.Equal("catalog", catalog.ApplicationId);
        Assert.NotEqual(opencrm.NotificationId, catalog.NotificationId);
    }

    /// <summary>Повторная доставка не запускает внешний вызов второй раз после фиксации результата.</summary>
    [Fact]
    public async Task TelegramDeliveryStateMakesProviderAttemptIdempotent()
    {
        using var scope = fixture.Services.CreateScope();
        var stateStore = new ConversationDeliveryAttemptStore(
            scope.ServiceProvider.GetRequiredService<NotificationDbContext>());
        var leaseId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var firstAttempt = await stateStore.BeginAsync(leaseId, requestId, messageId, CancellationToken.None);
        Assert.Equal("ready", firstAttempt.Status);
        Assert.True(await stateStore.TryMarkProviderStartedAsync(leaseId, CancellationToken.None));
        Assert.True(await stateStore.SaveResultAsync(
            leaseId,
            "accepted",
            "735",
            null,
            null,
            false,
            null,
            CancellationToken.None));
        Assert.False(await stateStore.SaveResultAsync(
            leaseId,
            "uncertain",
            null,
            "provider-result-unknown",
            "Telegram не подтвердил результат предыдущей попытки.",
            false,
            null,
            CancellationToken.None));

        var duplicateAttempt = await stateStore.BeginAsync(leaseId, requestId, messageId, CancellationToken.None);

        Assert.False(await stateStore.TryMarkProviderStartedAsync(leaseId, CancellationToken.None));
        Assert.Equal("accepted", duplicateAttempt.Status);
        Assert.Equal("735", duplicateAttempt.ProviderMessageId);
    }

    /// <summary>Повтор после неопределённого provider вызова остаётся помеченным как начатый.</summary>
    [Fact]
    public async Task TelegramDeliveryStatePreservesUnknownProviderAttempt()
    {
        using var scope = fixture.Services.CreateScope();
        var firstStateStore = new ConversationDeliveryAttemptStore(
            scope.ServiceProvider.GetRequiredService<NotificationDbContext>());
        var leaseId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var firstAttempt = await firstStateStore.BeginAsync(leaseId, requestId, messageId, CancellationToken.None);
        Assert.Equal("ready", firstAttempt.Status);

        using var duplicateScope = fixture.Services.CreateScope();
        var duplicateStateStore = new ConversationDeliveryAttemptStore(
            duplicateScope.ServiceProvider.GetRequiredService<NotificationDbContext>());
        var duplicateAttempt = await duplicateStateStore.BeginAsync(
            leaseId,
            requestId,
            messageId,
            CancellationToken.None);
        Assert.Equal("ready", duplicateAttempt.Status);

        var startResults = await Task.WhenAll(
            firstStateStore.TryMarkProviderStartedAsync(leaseId, CancellationToken.None),
            duplicateStateStore.TryMarkProviderStartedAsync(leaseId, CancellationToken.None));

        Assert.Single(startResults, started => started);
        using var verificationScope = fixture.Services.CreateScope();
        var verificationStore = new ConversationDeliveryAttemptStore(
            verificationScope.ServiceProvider.GetRequiredService<NotificationDbContext>());
        var persistedAttempt = await verificationStore.BeginAsync(
            leaseId,
            requestId,
            messageId,
            CancellationToken.None);
        Assert.Equal("started", persistedAttempt.Status);
    }

    /// <summary>Очередь подтверждает только durable inbox, дедуплицирует повторы и держит worker параллелизм ограниченным.</summary>
    [Fact]
    public async Task TelegramChatInboxDeduplicatesBurstAndBoundsWorkerConcurrency()
    {
        var commands = Enumerable.Range(0, 100)
            .Select(_ => new ConversationDeliveryWakeup(
                ConversationDeliveryProvider.Telegram,
                Guid.NewGuid(),
                Guid.NewGuid()))
            .ToArray();
        var messageIds = commands.Select(command => command.MessageId).ToArray();
        var duplicate = commands[0];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        foreach (var command in commands)
        {
            await fixture.SendChatAsync("opencrm", command).WaitAsync(timeout.Token);
        }

        await fixture.SendChatAsync("opencrm", duplicate).WaitAsync(timeout.Token);
        await fixture.SendChatAsync("opencrm", duplicate).WaitAsync(timeout.Token);

        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            var completedCount = await db.ConversationDeliveryInbox.CountAsync(item =>
                messageIds.Contains(item.MessageId) && item.Status == "completed");
            if (completedCount == commands.Length)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }

        using var verificationScope = fixture.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.Equal(commands.Length, await verificationDb.ConversationDeliveryInbox.CountAsync(item =>
            messageIds.Contains(item.MessageId)));
        Assert.Equal(commands.Length, messageIds.Sum(fixture.TelegramOpenCRM.GetRequestCount));
        Assert.InRange(fixture.TelegramOpenCRM.MaxConcurrentRequests, 1, 4);
    }

    /// <summary>Сбой core API сохраняется в inbox и повторяется worker без повторной Rabbit-доставки.</summary>
    [Fact]
    public async Task TelegramChatInboxDefersCoreOutageWithoutRabbitRetry()
    {
        fixture.TelegramOpenCRM.FailNextRequests(1);
        var command = new ConversationDeliveryWakeup(
            ConversationDeliveryProvider.Telegram,
            Guid.NewGuid(),
            Guid.NewGuid());
        await fixture.SendChatAsync("opencrm", command);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            var inboxMessage = await db.ConversationDeliveryInbox.SingleOrDefaultAsync(item =>
                item.MessageId == command.MessageId && item.RequestId == command.RequestId);
            if (inboxMessage?.Status == "completed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }

        Assert.Equal(2, fixture.TelegramOpenCRM.GetRequestCount(command.MessageId));
    }

    /// <summary>Publisher и consumer используют общую bounded очередь при любом порядке запуска.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TelegramChatQueueRejectsOverflowWithoutLosingAcceptedMessages(bool startProducerFirst)
    {
        var queueName = $"notifications-topology-{Guid.NewGuid():N}";
        var resultChannel = Channel.CreateUnbounded<SendTelegramChatMessage>();
        IBusControl? producer = null;
        IBusControl? consumer = null;
        try
        {
            if (startProducerFirst)
            {
                producer = this.CreateTopologyProducer();
                await producer.StartAsync();
                await Assert.ThrowsAnyAsync<Exception>(() => SendTopologyMessageAsync(producer, queueName));
                await producer.StopAsync();
                consumer = this.CreateTopologyConsumer(queueName, resultChannel);
                await consumer.StartAsync();
                await consumer.StopAsync();
                producer = this.CreateTopologyProducer();
                await producer.StartAsync();
            }
            else
            {
                consumer = this.CreateTopologyConsumer(queueName, resultChannel);
                await consumer.StartAsync();
                await consumer.StopAsync();
                producer = this.CreateTopologyProducer();
                await producer.StartAsync();
            }

            await SendTopologyMessageAsync(producer, queueName);
            await SendTopologyMessageAsync(producer, queueName);
            await AssertRabbitRejectsOverflowAsync(queueName);
            Assert.Equal(2, await this.GetQueueMessageCountAsync(queueName));

            consumer = this.CreateTopologyConsumer(queueName, resultChannel);
            await consumer.StartAsync();
            var firstMessage = await ReadTopologyMessageAsync(resultChannel.Reader);
            var secondMessage = await ReadTopologyMessageAsync(resultChannel.Reader);
            Assert.NotEqual(firstMessage.MessageId, secondMessage.MessageId);
        }
        finally
        {
            if (consumer != null)
            {
                await consumer.StopAsync();
            }

            if (producer != null)
            {
                await producer.StopAsync();
            }
        }
    }

    /// <summary>RabbitMQ оставляет poison-сообщение в bounded error queue без второго skipped consumer.</summary>
    [Fact]
    public async Task RabbitErrorQueueIsBoundedAndNotDrainedIntoSkippedQueue()
    {
        var queueName = $"notifications-error-{Guid.NewGuid():N}";
        IBusControl? consumer = null;
        IBusControl? producer = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            consumer = this.CreateFailingConsumer(queueName);
            await consumer.StartAsync(timeout.Token);
            producer = this.CreateTopologyProducer();
            await producer.StartAsync(timeout.Token);
            var endpoint = await producer.GetSendEndpoint(new Uri($"exchange:{queueName}"));
            await endpoint.Send(new SendTelegramChatMessage(Guid.NewGuid(), Guid.NewGuid()), timeout.Token);

            using var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri($"http://{fixture.RabbitMqHost}:{fixture.RabbitMqManagementPort}/");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("rabbitmq:rabbitmq")));
            var errorQueue = Uri.EscapeDataString($"{queueName}_error");
            HttpResponseMessage? errorResponse = null;
            while (!timeout.IsCancellationRequested)
            {
                errorResponse?.Dispose();
                errorResponse = await httpClient.GetAsync($"api/queues/%2F/{errorQueue}", timeout.Token);
                if (errorResponse.IsSuccessStatusCode)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }

            Assert.NotNull(errorResponse);
            Assert.True(errorResponse.IsSuccessStatusCode);
            using var queueDocument = JsonDocument.Parse(await errorResponse.Content.ReadAsStringAsync(timeout.Token));
            var queueArguments = queueDocument.RootElement.GetProperty("arguments");
            Assert.Equal(2, queueArguments.GetProperty("x-max-length").GetInt32());
            Assert.Equal("reject-publish", queueArguments.GetProperty("x-overflow").GetString());

            using var skippedResponse = await httpClient.GetAsync(
                $"api/queues/%2F/{Uri.EscapeDataString($"{queueName}_error_skipped")}", timeout.Token);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, skippedResponse.StatusCode);
        }
        finally
        {
            if (producer != null)
            {
                await producer.StopAsync();
            }

            if (consumer != null)
            {
                await consumer.StopAsync();
            }
        }
    }

    /// <summary>Команда шаблона подтверждается до отправки; числа и HTML-подстановка обрабатываются корректно.</summary>
    [Fact]
    public async Task TemplateCommandSupportsJsonValuesAndEscaping()
    {
        var key = Guid.NewGuid().ToString("N");
        await fixture.SendAsync("opencrm", new UpsertNotificationTemplate(
            $" {key} ", new UpsertTemplateRequest("email", "Тема", "<b>{{ name }}</b>: {{count}}", true)));
        await fixture.WaitAsync<NotificationTemplateUpdated>("opencrm", result => result.Key == key);

        var request = CreateRequest(key) with
        {
            TemplateKey = key,
            Body = null,
            Data = new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("A&B"),
                ["count"] = JsonSerializer.SerializeToElement(42)
            }
        };
        await fixture.SendAsync("opencrm", new SendNotification(request));
        await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "simulated");
        Assert.Contains(fixture.Sender.Calls, call => call.Body == "<b>A&amp;B</b>: 42" && call.IsHtml);
    }

    /// <summary>Ошибочная команда отклоняется с корреляцией, без создания уведомления.</summary>
    [Fact]
    public async Task InvalidCommandProducesRejection()
    {
        var key = Guid.NewGuid().ToString("N");
        var messageId = Guid.NewGuid();
        await fixture.SendAsync("opencrm", new SendNotification(CreateRequest(key) with { Body = null }), messageId);
        var rejected = await fixture.WaitAsync<NotificationCommandRejected>(
            "opencrm", result => result.MessageId == messageId);
        Assert.Equal("invalid-command", rejected.ErrorCode);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.False(await db.Notifications.AnyAsync(item => item.IdempotencyKey == key));
    }

    /// <summary>Повтор одного MessageId не применяет обновление шаблона второй раз.</summary>
    [Fact]
    public async Task InboxSuppressesRepeatedMessageId()
    {
        var key = Guid.NewGuid().ToString("N");
        var messageId = Guid.NewGuid();
        await fixture.SendAsync("opencrm", new UpsertNotificationTemplate(
            key, new UpsertTemplateRequest("email", null, "original")), messageId);
        await fixture.WaitAsync<NotificationTemplateUpdated>("opencrm", result => result.Key == key);
        await fixture.SendAsync("opencrm", new UpsertNotificationTemplate(
            key, new UpsertTemplateRequest("email", null, "duplicate")), messageId);
        // Следующая команда служит барьером обработки; содержимое проверяется в БД.
        await fixture.SendAsync("opencrm", new GetNotificationStatus(key));
        await fixture.WaitAsync<NotificationStatusChanged>("opencrm", result => result.IdempotencyKey == key);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var template = await db.NotificationTemplates.SingleAsync(item => item.Key == key);
        Assert.Equal("original", template.BodyTemplate);
    }

    /// <summary>Планировщик отправляет отложенную запись только после наступления срока.</summary>
    [Fact]
    public async Task SchedulerDispatchesDueNotification()
    {
        var key = Guid.NewGuid().ToString("N");
        await fixture.SendAsync("opencrm", new SendNotification(
            CreateRequest(key) with { ScheduledAt = DateTimeOffset.UtcNow.AddDays(1) }));
        var queued = await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "queued");
        Assert.Equal(0, queued.AttemptCount);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        await db.Notifications.Where(item => item.Id == queued.NotificationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ScheduledAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "simulated");
    }

    /// <summary>Аренда после падения освобождается вместе с отметкой старой публикации.</summary>
    [Fact]
    public async Task SchedulerRecoversExpiredLease()
    {
        var key = Guid.NewGuid().ToString("N");
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var notification = Notification.Create(
            "opencrm", key, NotificationChannel.Email, "test@example.test", null,
            "{}", null, "recovery", DateTimeOffset.UtcNow, null);
        notification.MarkDispatchRequested(DateTimeOffset.UtcNow.AddMinutes(-20));
        notification.MarkProcessing();
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        await db.Notifications.Where(item => item.Id == notification.Id).ExecuteUpdateAsync(
            setters => setters.SetProperty(item => item.ProcessingStartedAt, DateTimeOffset.UtcNow.AddMinutes(-20)));

        var recovered = await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "simulated");
        Assert.Equal(2, recovered.AttemptCount);
    }

    /// <summary>Неуспешная доставка публикует retrying, затем окончательное failed.</summary>
    [Fact]
    public async Task ProviderFailurePublishesRetryAndTerminalStatus()
    {
        var key = Guid.NewGuid().ToString("N");
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var notification = Notification.Create(
            "opencrm", key, NotificationChannel.Email, "contact@example.invalid", null,
            "{}", null, "failure", DateTimeOffset.UtcNow, null);
        notification.MarkDispatchRequested(DateTimeOffset.UtcNow);
        notification.MarkProcessing();
        notification.MarkProcessing();
        notification.MarkQueuedForRetry("previous failure");
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        await fixture.DispatchAsync(notification.Id);
        var retry = await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "retrying");
        Assert.Equal(3, retry.AttemptCount);
        var failed = await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "failed");
        Assert.Equal(4, failed.AttemptCount);
        Assert.Equal("delivery-failed", failed.ErrorCode);
    }

    /// <summary>Отложенное письмо сохраняет выбранную версию даже после регистрации новой и удаления исходного шаблона.</summary>
    [Fact]
    public async Task ScheduledNotificationUsesCapturedVersion()
    {
        var key = Guid.NewGuid().ToString("N");
        await fixture.SendAsync("opencrm", new UpsertNotificationTemplate(
            key, new UpsertTemplateRequest("email", "Первая", "<p>Первая: {{name}}</p>", true)));
        await fixture.WaitAsync<NotificationTemplateUpdated>("opencrm", result => result.Key == key && result.Version == 1);
        await fixture.SendAsync("opencrm", new SendNotification(CreateRequest(key) with
        {
            TemplateKey = key,
            Body = null,
            Subject = null,
            Data = new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Иван") },
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1)
        }));
        var queued = await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "queued");

        await fixture.SendAsync("opencrm", new UpsertNotificationTemplate(
            key, new UpsertTemplateRequest("email", "Вторая", "<p>Вторая</p>", true, Version: 2)));
        await fixture.WaitAsync<NotificationTemplateUpdated>("opencrm", result => result.Key == key && result.Version == 2);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var saved = await db.Notifications.SingleAsync(item => item.Id == queued.NotificationId);
        Assert.Equal("<p>Первая: Иван</p>", saved.Body);
        Assert.Equal("Первая", saved.Subject);
        Assert.True(saved.IsHtml);
        Assert.Equal(1, saved.TemplateVersion);

        // Удаление только тестовых данных доказывает независимость доставки от хранения шаблона.
        await db.NotificationTemplates.Where(item => item.Key == key && item.Version == 1).ExecuteDeleteAsync();
        await db.Notifications.Where(item => item.Id == saved.Id).ExecuteUpdateAsync(
            setters => setters.SetProperty(item => item.ScheduledAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "simulated");
        Assert.Contains(fixture.Sender.Calls, call => call.Body == "<p>Первая: Иван</p>" && call.IsHtml);

        var secondKey = $"{key}-v2";
        await fixture.SendAsync("opencrm", new SendNotification(CreateRequest(secondKey) with
        {
            TemplateKey = key,
            TemplateVersion = 2,
            Subject = null,
            Body = null
        }));
        await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == secondKey && result.Status == "simulated");
        Assert.Contains(fixture.Sender.Calls, call => call.Body == "<p>Вторая</p>" && call.IsHtml);
    }

    /// <summary>Параллельные регистрации одного содержимого безопасны, а другое содержимое той же версии отклоняется.</summary>
    [Fact]
    public async Task TemplateRegistrationIsIdempotentAndImmutable()
    {
        var key = Guid.NewGuid().ToString("N");
        var command = new UpsertNotificationTemplate(
            key, new UpsertTemplateRequest("email", "Тема", "Оригинал"));
        await Task.WhenAll(fixture.SendAsync("opencrm", command), fixture.SendAsync("opencrm", command));
        await fixture.WaitAsync<NotificationTemplateUpdated>("opencrm", result => result.Key == key);
        await fixture.WaitAsync<NotificationTemplateUpdated>("opencrm", result => result.Key == key);

        var conflictId = Guid.NewGuid();
        await fixture.SendAsync("opencrm", command with
        {
            Template = command.Template with { BodyTemplate = "Изменённое" }
        }, conflictId);
        await fixture.WaitAsync<NotificationCommandRejected>("opencrm", result => result.MessageId == conflictId);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var template = await db.NotificationTemplates.SingleAsync(item => item.Key == key);
        Assert.Equal("Оригинал", template.BodyTemplate);
        Assert.Equal(1, template.Version);
    }

    /// <summary>Неизвестная версия и отсутствующие параметры отклоняются без попыток доставки.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidTemplateSendIsRejectedBeforeEnqueue(int version)
    {
        var key = Guid.NewGuid().ToString("N");
        await fixture.SendAsync("opencrm", new UpsertNotificationTemplate(
            key, new UpsertTemplateRequest("email", null, "{{required}}")));
        await fixture.WaitAsync<NotificationTemplateUpdated>("opencrm", result => result.Key == key);

        var messageId = Guid.NewGuid();
        await fixture.SendAsync("opencrm", new SendNotification(CreateRequest(key) with
        {
            TemplateKey = key,
            TemplateVersion = version,
            Body = null
        }), messageId);
        await fixture.WaitAsync<NotificationCommandRejected>("opencrm", result => result.MessageId == messageId);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.False(await db.Notifications.AnyAsync(item => item.IdempotencyKey == key));
    }

    /// <summary>Старое задание без снимка получает содержимое версии 1 перед отправкой.</summary>
    [Fact]
    public async Task LegacyTemplateNotificationGetsPersistentSnapshot()
    {
        var key = Guid.NewGuid().ToString("N");
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        db.NotificationTemplates.Add(new NotificationTemplate(
            "opencrm", key, NotificationChannel.Email, "Старая тема",
            "<p>Старое письмо</p>", true, DateTimeOffset.UtcNow));
        var notification = Notification.Create(
            "opencrm", key, NotificationChannel.Email, "test@example.test", key,
            "{}", null, null, DateTimeOffset.UtcNow, null);
        notification.MarkDispatchRequested(DateTimeOffset.UtcNow);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        await fixture.DispatchAsync(notification.Id);
        await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "simulated");
        await db.Entry(notification).ReloadAsync();
        Assert.Equal("<p>Старое письмо</p>", notification.Body);
        Assert.Equal(1, notification.TemplateVersion);
        Assert.True(notification.IsHtml);
    }

    /// <summary>Шаблон другого приложения недоступен даже при совпадении ключа и версии.</summary>
    [Fact]
    public async Task TemplateVersionIsIsolatedByApplication()
    {
        var key = Guid.NewGuid().ToString("N");
        await fixture.SendAsync("catalog", new UpsertNotificationTemplate(
            key, new UpsertTemplateRequest("email", null, "Письмо каталога", Version: 2)));
        await fixture.WaitAsync<NotificationTemplateUpdated>("catalog", result => result.Key == key);
        var messageId = Guid.NewGuid();

        await fixture.SendAsync("opencrm", new SendNotification(CreateRequest(key) with
        {
            TemplateKey = key,
            TemplateVersion = 2,
            Body = null
        }), messageId);
        await fixture.WaitAsync<NotificationCommandRejected>("opencrm", result => result.MessageId == messageId);
    }

    /// <summary>Системное письмо формируется без регистрации шаблона и сохраняется до отправки.</summary>
    [Fact]
    public async Task RazorEmailIsRenderedAndDeliveredWithoutTemplateRegistration()
    {
        var key = Guid.NewGuid().ToString("N");
        await fixture.SendAsync("opencrm", new SendNotification(CreateRequest(key) with
        {
            TemplateKey = "system/email-confirmation",
            TemplateVersion = 1,
            Subject = null,
            Body = null,
            Data = new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("<Иван>"),
                ["confirmationUrl"] = JsonSerializer.SerializeToElement("https://example.com/confirm?a=1&b=2")
            }
        }));
        var result = await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", status => status.IdempotencyKey == key && status.Status == "simulated");

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var saved = await db.Notifications.SingleAsync(item => item.Id == result.NotificationId);
        Assert.Equal("Подтвердите электронную почту — opencrm.example.invalid", saved.Subject);
        Assert.Contains("<!doctype html>", saved.Body);
        Assert.Contains("href=\"https://example.com/confirm?a=1&amp;b=2\"", saved.Body);
        Assert.DoesNotContain("<Иван>", saved.Body);
        Assert.True(saved.IsHtml);
        Assert.Equal(1, saved.TemplateVersion);
        Assert.Contains(fixture.Sender.Calls, call => call.Body == saved.Body && call.IsHtml);
        Assert.False(await db.NotificationTemplates.AnyAsync(item => item.Key == "system/email-confirmation"));
    }

    /// <summary>Входящая команда не может заменить встроенное системное письмо.</summary>
    [Fact]
    public async Task SystemTemplateRegistrationIsRejected()
    {
        var messageId = Guid.NewGuid();
        await fixture.SendAsync("opencrm", new UpsertNotificationTemplate(
            "system/email-confirmation", new UpsertTemplateRequest("email", null, "Подмена")), messageId);
        await fixture.WaitAsync<NotificationCommandRejected>("opencrm", result => result.MessageId == messageId);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.False(await db.NotificationTemplates.AnyAsync(item => item.Key == "system/email-confirmation"));
    }

    /// <summary>Неверные данные Razor-модели отклоняются до сохранения задания.</summary>
    [Fact]
    public async Task InvalidRazorModelIsRejectedBeforeEnqueue()
    {
        var key = Guid.NewGuid().ToString("N");
        var messageId = Guid.NewGuid();
        await fixture.SendAsync("opencrm", new SendNotification(CreateRequest(key) with
        {
            TemplateKey = "system/password-reset",
            Subject = null,
            Body = null,
            Data = new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Иван") }
        }), messageId);
        await fixture.WaitAsync<NotificationCommandRejected>("opencrm", result => result.MessageId == messageId);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.False(await db.Notifications.AnyAsync(item => item.IdempotencyKey == key));
    }

    /// <summary>Два приложения используют общий Razor-шаблон со своим оформлением и отдельными результатами.</summary>
    [Fact]
    public async Task SharedRazorTemplateUsesApplicationConfiguration()
    {
        var key = Guid.NewGuid().ToString("N");
        var request = CreateRequest(key) with
        {
            TemplateKey = "system/email-confirmation",
            Subject = null,
            Body = null,
            Data = new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("Иван"),
                ["confirmationUrl"] = JsonSerializer.SerializeToElement("https://example.com/confirm")
            }
        };
        await fixture.SendAsync("catalog", new SendNotification(request));
        var catalogResult = await fixture.WaitAsync<NotificationStatusChanged>(
            "catalog", result => result.IdempotencyKey == key && result.Status == "simulated");
        await fixture.SendAsync("opencrm", new SendNotification(request));
        var opencrmResult = await fixture.WaitAsync<NotificationStatusChanged>(
            "opencrm", result => result.IdempotencyKey == key && result.Status == "simulated");

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var catalog = await db.Notifications.SingleAsync(item => item.Id == catalogResult.NotificationId);
        var opencrm = await db.Notifications.SingleAsync(item => item.Id == opencrmResult.NotificationId);
        Assert.EndsWith(" — Catalog", catalog.Subject);
        Assert.EndsWith(" — opencrm.example.invalid", opencrm.Subject);
        Assert.DoesNotContain("opencrm.example.invalid", catalog.Body);
        Assert.NotEqual(catalog.Id, opencrm.Id);
    }

    /// <summary> Создаёт запрос уведомления с указанным ключом шаблона. </summary>
    private static CreateNotificationRequest CreateRequest(string key)
    {
        return new CreateNotificationRequest(
            key, "email", "test@example.test", null, null, "Тест", "Сообщение", null);
    }

    private IBusControl CreateTopologyProducer()
    {
        return Bus.Factory.CreateUsingRabbitMq(configuration =>
        {
            configuration.Host(fixture.RabbitMqHost, fixture.RabbitMqPort, "/", host =>
            {
                host.Username("rabbitmq");
                host.Password("rabbitmq");
            });
        });
    }

    private IBusControl CreateTopologyConsumer(
        string queueName,
        Channel<SendTelegramChatMessage> resultChannel)
    {
        return Bus.Factory.CreateUsingRabbitMq(configuration =>
        {
            configuration.Host(fixture.RabbitMqHost, fixture.RabbitMqPort, "/", host =>
            {
                host.Username("rabbitmq");
                host.Password("rabbitmq");
            });
            configuration.ReceiveEndpoint(queueName, endpoint =>
            {
                endpoint.ConfigureConsumeTopology = false;
                endpoint.PrefetchCount = 1;
                endpoint.ConcurrentMessageLimit = 1;
                endpoint.SetQueueArgument("x-max-length", 2);
                endpoint.SetQueueArgument("x-overflow", "reject-publish");
                endpoint.Handler<SendTelegramChatMessage>(context =>
                    resultChannel.Writer.WriteAsync(context.Message, context.CancellationToken).AsTask());
            });
        });
    }

    private IBusControl CreateFailingConsumer(string queueName)
    {
        return Bus.Factory.CreateUsingRabbitMq(configuration =>
        {
            configuration.Host(fixture.RabbitMqHost, fixture.RabbitMqPort, "/", host =>
            {
                host.Username("rabbitmq");
                host.Password("rabbitmq");
            });
            configuration.SendTopology.ConfigureErrorSettings = settings =>
            {
                settings.SetQueueArgument("x-max-length", 2);
                settings.SetQueueArgument("x-max-length-bytes", 1024 * 1024);
                settings.SetQueueArgument("x-overflow", "reject-publish");
            };
            configuration.ReceiveEndpoint(queueName, endpoint =>
            {
                endpoint.ConfigureConsumeTopology = false;
                endpoint.UseMessageRetry(retry => retry.None());
                endpoint.Handler<SendTelegramChatMessage>(
                    _ => Task.FromException(new InvalidOperationException("Test poison message.")));
            });
        });
    }

    private static async Task SendTopologyMessageAsync(IBusControl bus, string queueName)
    {
        var endpoint = await bus.GetSendEndpoint(new Uri($"exchange:{queueName}"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await endpoint.Send(
            new SendTelegramChatMessage(Guid.NewGuid(), Guid.NewGuid()),
            context =>
            {
                context.GetPayload<RabbitMqSendContext>().Mandatory = true;
                context.SetAwaitAck(true);
            },
            timeout.Token);
    }

    private static async Task PublishDurableWakeupAsync(
        ConnectionFactory factory,
        string queueName,
        Guid messageId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await channel.QueueDeclareAsync(
            queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Persistent = true
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new ConversationDeliveryWakeup(ConversationDeliveryProvider.Max, messageId, requestId));
        await channel.BasicPublishAsync("", queueName, true, properties, body, cancellationToken);
    }

    private static async Task<SendTelegramChatMessage> ReadTopologyMessageAsync(
        ChannelReader<SendTelegramChatMessage> reader)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        return await reader.ReadAsync(timeout.Token);
    }

    private async Task<int> GetQueueMessageCountAsync(string queueName)
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.RabbitMqHost,
            Port = fixture.RabbitMqPort,
            UserName = "rabbitmq",
            Password = "rabbitmq"
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var queue = await channel.QueueDeclarePassiveAsync(queueName);

        return (int)queue.MessageCount;
    }

    private static async Task<IConnection> ConnectRabbitMqAsync(
        ConnectionFactory factory,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                return await factory.CreateConnectionAsync(cancellationToken);
            }
            catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }
    }

    private async Task AssertRabbitRejectsOverflowAsync(string queueName)
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.RabbitMqHost,
            Port = fixture.RabbitMqPort,
            UserName = "rabbitmq",
            Password = "rabbitmq"
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Persistent = true
        };
        var body = System.Text.Encoding.UTF8.GetBytes("{}");

        await Assert.ThrowsAnyAsync<RabbitMQ.Client.Exceptions.PublishException>(async () => await channel.BasicPublishAsync(
                queueName,
                string.Empty,
                true,
                properties,
                body));
    }
}
