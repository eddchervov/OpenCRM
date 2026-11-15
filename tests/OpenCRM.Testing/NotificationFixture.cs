using System.Threading.Channels;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Modules.Notifications;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Notifications.Contracts;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace OpenCRM.Testing;

public sealed class NotificationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();
    private readonly Channel<object> _commands = Channel.CreateUnbounded<object>();
    private ServiceProvider _services = null!;
    private IBusControl _bus = null!;

    public RabbitMqOptions Options { get; } = new()
    {
        ApplicationId = "opencrm-test",
        DispatchIntervalSeconds = 1,
        ResponseTimeoutSeconds = 60,
        SendTimeoutSeconds = 3
    };

    public OpenCRMContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), db =>
            {
                db.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                db.EnableRetryOnFailure();
            }).Options;

        return new OpenCRMContext(options);
    }

    public EmailSender CreateSender(OpenCRMContext context)
    {
        return new EmailSender(context);
    }

    public NotificationOutboxDispatcher CreateDispatcher(OpenCRMContext context)
    {
        return new NotificationOutboxDispatcher(context, this._bus, Microsoft.Extensions.Options.Options.Create(this.Options));
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(this._postgres.StartAsync(), this._rabbit.StartAsync());
        await using (var context = this.CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => this.CreateContext());
        services.AddSingleton<IOptions<RabbitMqOptions>>(Microsoft.Extensions.Options.Options.Create(this.Options));
        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<NotificationResultConsumer>();
            bus.AddConsumer<NotificationDeliveryConsumer>();
            bus.UsingRabbitMq((registration, transport) =>
            {
                transport.Host(this._rabbit.Hostname, this._rabbit.GetMappedPublicPort(5672), "/", host =>
                {
                    host.Username("rabbitmq");
                    host.Password("rabbitmq");
                });
                transport.ReceiveEndpoint(NotificationQueues.Results(this.Options.ApplicationId), endpoint =>
                {
                    endpoint.ConfigureConsumeTopology = false;
                    endpoint.ConcurrentMessageLimit = 1;
                    endpoint.ConfigureConsumer<NotificationResultConsumer>(registration);
                    endpoint.ConfigureConsumer<NotificationDeliveryConsumer>(registration);
                });
                transport.ReceiveEndpoint(NotificationQueues.Commands(this.Options.ApplicationId), endpoint =>
                {
                    endpoint.ConfigureConsumeTopology = false;
                    endpoint.Handler<SendNotification>(message => this.RecordAsync(message.Message));
                    endpoint.Handler<UpsertNotificationTemplate>(message => this.RecordAsync(message.Message));
                    endpoint.Handler<GetNotificationStatus>(message => this.RecordAsync(message.Message));
                });
            });
        });
        this._services = services.BuildServiceProvider();
        this._bus = this._services.GetRequiredService<IBusControl>();
        await this._bus.StartAsync();
    }

    public async Task ResetAsync()
    {
        await using var context = this.CreateContext();
        await context.EmailMessageEvents.ExecuteDeleteAsync();
        await context.SupportMessages.ExecuteDeleteAsync();
        await context.EmailMessages.ExecuteDeleteAsync();
        await context.EmailCampaignRecipients.ExecuteDeleteAsync();
        await context.EmailCampaigns.ExecuteDeleteAsync();
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE "identity"."AspNetUsers", "SiteNotifications", "TelegramNotificationOutbox" CASCADE
            """);
        while (this._commands.Reader.TryRead(out _))
        {
        }
    }

    public async Task<T> NextCommandAsync<T>()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var command = await this._commands.Reader.ReadAsync(timeout.Token);

        return Assert.IsType<T>(command);
    }

    public async Task ReplyAsync<T>(T result) where T : class
    {
        var endpoint = await this._bus.GetSendEndpoint(new Uri($"queue:{NotificationQueues.Results(this.Options.ApplicationId)}"));
        await endpoint.Send(result);
    }

    public async Task WaitAsync(Func<OpenCRMContext, Task<bool>> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await using var context = this.CreateContext();
            if (await predicate(context))
            {
                return;
            }

            await Task.Delay(30);
        }

        throw new TimeoutException("Не получено ожидаемое состояние уведомления.");
    }

    private async Task RecordAsync(object message)
    {
        await this._commands.Writer.WriteAsync(message);
    }

    public async Task DisposeAsync()
    {
        if (this._bus != null)
        {
            await this._bus.StopAsync();
        }

        if (this._services != null)
        {
            await this._services.DisposeAsync();
        }

        await this._rabbit.DisposeAsync();
        await this._postgres.DisposeAsync();
    }
}
