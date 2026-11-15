using System.Threading.Channels;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notifications.Application;
using Notifications.Contracts;
using Notifications.Infrastructure;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Notifications.Infrastructure.Providers.Smtp;

namespace Notifications.Tests.Integration;

/// <summary>Запускает настоящий Worker с изолированными PostgreSQL и RabbitMQ и тестовым провайдером.</summary>
public sealed class MessagingFixture : IAsyncLifetime
{
    /// <summary> Контейнер PostgreSQL для интеграционных тестов. </summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine")
        .WithDatabase("notifications")
        .Build();

    /// <summary> Контейнер RabbitMQ для интеграционных тестов. </summary>
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithPortBinding(15672, true)
        .Build();

    /// <summary> Словарь результатов обработки сообщений по ключам. </summary>
    private readonly Dictionary<string, Channel<object>> _results = new(StringComparer.Ordinal)
    {
        ["opencrm"] = Channel.CreateUnbounded<object>(),
        ["catalog"] = Channel.CreateUnbounded<object>()
    };

    /// <summary> Запущенный хост Worker в тестовом окружении, если есть. </summary>
    private IHost? _worker;

    /// <summary> Клиент шины сообщений теста, если создан. </summary>
    private IBusControl? _client;

    /// <summary>Провайдер фиксирует фактические вызовы, не отправляя письма и SMS.</summary>
    public RecordingNotificationSender Sender { get; } = new();

    /// <summary>Фиктивный core API не выполняет реальных сетевых вызовов.</summary>
    public TelegramChatOpenCRMStub TelegramOpenCRM { get; } = new();

    /// <summary>Области зависимостей работающего Worker.</summary>
    public IServiceProvider Services => this._worker!.Services;

    internal string RabbitMqHost => this._rabbitMq.Hostname;

    internal ushort RabbitMqPort => this._rabbitMq.GetMappedPublicPort(5672);

    internal ushort RabbitMqManagementPort => this._rabbitMq.GetMappedPublicPort(15672);

    /// <summary>Создаёт инфраструктуру, применяет существующую миграцию и запускает транспорт.</summary>
    public async Task InitializeAsync()
    {
        await Task.WhenAll(this._postgres.StartAsync(), this._rabbitMq.StartAsync());
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:NotificationsDb"] = this._postgres.GetConnectionString(),
            ["RabbitMq:Host"] = this._rabbitMq.Hostname,
            ["RabbitMq:Port"] = this._rabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:VirtualHost"] = "/",
            ["Email:Enabled"] = "true",
            ["Email:Provider"] = "smtp",
            ["Smtp:Port"] = "587",
            ["Smtp:UseSsl"] = "false",
            ["Sms:Enabled"] = "false",
            ["Telegram:Enabled"] = "false",
            ["TelegramClient:Enabled"] = "false",
            ["TelegramChat:Enabled"] = "true",
            ["TelegramChat:OpenCRMBaseUrl"] = "https://opencrm.example",
            ["TelegramChat:ServiceKey"] = new string('s', 40),
            ["TelegramChat:RelayBaseUrl"] = "https://relay.example",
            ["TelegramChat:RelayKey"] = new string('r', 40),
            ["TelegramChat:RequestTimeoutSeconds"] = "5",
            ["TelegramChat:InboxWorkerConcurrency"] = "4",
            ["TelegramChat:InboxPollIntervalSeconds"] = "1",
            ["TelegramChat:InboxLeaseSeconds"] = "300",
            ["Max:Enabled"] = "false",
            ["RabbitMq:Username"] = "rabbitmq",
            ["RabbitMq:Password"] = "rabbitmq",
            ["RabbitMq:ApplicationId"] = "opencrm",
            ["RabbitMq:Applications:0"] = "opencrm",
            ["RabbitMq:Applications:1"] = "catalog",
            ["Conversations:ApplicationId"] = "opencrm",
            ["SystemTemplates:Applications:opencrm:BrandName"] = "opencrm.example.invalid",
            ["SystemTemplates:Applications:opencrm:Templates:0"] = "system/email-confirmation",
            ["SystemTemplates:Applications:opencrm:Templates:1"] = "system/password-reset",
            ["SystemTemplates:Applications:opencrm:Templates:2"] = "system/subscription-expiring",
            ["SystemTemplates:Applications:opencrm:Templates:3"] = "system/custom-email",
            ["SystemTemplates:Applications:catalog:BrandName"] = "Catalog",
            ["SystemTemplates:Applications:catalog:Templates:0"] = "system/email-confirmation",
            ["Delivery:LogOnly"] = "true"
        });
        StartupConfigurationValidator.Validate(builder.Configuration, builder.Environment);
        builder.Services.AddNotificationInfrastructure(builder.Configuration);
        builder.Services.AddSingleton(this.TelegramOpenCRM);
        builder.Services.AddHttpClient("TelegramChatOpenCRM")
            .ConfigurePrimaryHttpMessageHandler(provider =>
                provider.GetRequiredService<TelegramChatOpenCRMStub>().CreateHandler());
        builder.Services.AddSingleton<INotificationChannelSender>(this.Sender);
        // Тестовый отправитель заменяет только email, оставляя транспорт, БД и outbox настоящими.
        var emailDescriptor = builder.Services.Single(descriptor =>
            descriptor.ImplementationType == typeof(EmailNotificationSender));
        builder.Services.Remove(emailDescriptor);
        this._worker = builder.Build();
        await this._worker.Services.ApplyNotificationMigrationsAsync();

        this._client = Bus.Factory.CreateUsingRabbitMq(configuration =>
        {
            configuration.Host(
                this._rabbitMq.Hostname, this._rabbitMq.GetMappedPublicPort(5672), "/", host =>
                {
                    host.Username("rabbitmq");
                    host.Password("rabbitmq");
                });

            foreach (var (applicationId, results) in this._results)
            {
                configuration.ReceiveEndpoint(NotificationQueues.Results(applicationId), endpoint =>
                {
                    endpoint.ConfigureConsumeTopology = false;
                    endpoint.Handler<NotificationStatusChanged>(context =>
                        results.Writer.WriteAsync(context.Message).AsTask());
                    endpoint.Handler<NotificationDeliveryReported>(context =>
                        results.Writer.WriteAsync(context.Message).AsTask());
                    endpoint.Handler<NotificationTemplateUpdated>(context =>
                        results.Writer.WriteAsync(context.Message).AsTask());
                    endpoint.Handler<NotificationCommandRejected>(context =>
                        results.Writer.WriteAsync(context.Message).AsTask());
                });
            }
        });

        await this._client.StartAsync();
        await this._worker.StartAsync();
    }

    /// <summary>Посылает команду по публичному адресу с заданным идентификатором сообщения.</summary>
    public async Task SendAsync<T>(string applicationId, T message, Guid? messageId = null) where T : class
    {
        var endpoint = await this._client!.GetSendEndpoint(new Uri($"queue:{NotificationQueues.Commands(applicationId)}"));
        await endpoint.Send(message, context => context.MessageId = messageId ?? Guid.NewGuid());
    }

    /// <summary>Отправляет адресную wakeup-команду через настоящую Telegram-очередь.</summary>
    public async Task SendChatAsync(string applicationId, ConversationDeliveryWakeup message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var endpoint = await this._client!.GetSendEndpoint(
            new Uri($"exchange:{NotificationQueues.ConversationDelivery(applicationId, message.ProviderCode)}"));
        await endpoint.Send(
            message,
            context =>
            {
                context.MessageId = Guid.NewGuid();
                context.GetPayload<RabbitMqSendContext>().Mandatory = true;
                context.SetAwaitAck(true);
            },
            timeout.Token);
    }

    /// <summary>Имитирует внутреннее задание предыдущей версии сервиса.</summary>
    public Task DispatchAsync(Guid notificationId)
    {
        return this._client!.Publish(new NotificationDispatchRequested(notificationId));
    }

    /// <summary>Ожидает нужный результат с ограничением времени.</summary>
    public async Task<T> WaitAsync<T>(string applicationId, Func<T, bool> predicate) where T : class
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var reader = this._results[applicationId].Reader;
        while (await reader.WaitToReadAsync(timeout.Token))
        {
            while (reader.TryRead(out var value))
            {
                if (value is T message && predicate(message))
                {
                    return message;
                }
            }
        }

        throw new InvalidOperationException("Result queue was closed.");
    }

    /// <summary>Останавливает только созданные тестом процессы и контейнеры.</summary>
    public async Task DisposeAsync()
    {
        if (this._worker != null)
        {
            await this._worker.StopAsync();
            this._worker.Dispose();
        }

        if (this._client != null)
        {
            await this._client.StopAsync();
        }

        await this._rabbitMq.DisposeAsync();
        await this._postgres.DisposeAsync();
    }
}
