using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Modules.Notifications;
using OpenCRM.Infrastructure.Data;
using Notifications.Contracts;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Providers.UnisenderGo;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

/// <summary>Проверяет оба сервиса целиком на изолированных PostgreSQL и RabbitMQ без отправки реальных писем.</summary>
public sealed class NotificationFullFlowTests
{
    [Fact]
    public async Task OpenCRMWorkerAndProviderWebhookCompleteRoundTrip()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await using var rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();
        await Task.WhenAll(postgres.StartAsync(), rabbit.StartAsync());
        var opencrmOptions = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(postgres.GetConnectionString(), db =>
                db.MigrationsAssembly(typeof(OpenCRM.Infrastructure.Migrations.AssemblyMarker).Assembly.GetName().Name))
            .Options;
        await using var context = new OpenCRMContext(opencrmOptions);
        await context.Database.MigrateAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:NotificationsDb"] = postgres.GetConnectionString(),
            ["RabbitMq:Host"] = rabbit.Hostname,
            ["RabbitMq:Port"] = rabbit.GetMappedPublicPort(5672).ToString(),
            ["RabbitMq:VirtualHost"] = "/", ["RabbitMq:Username"] = "rabbitmq", ["RabbitMq:Password"] = "rabbitmq",
            ["RabbitMq:Applications:0"] = "opencrm-test",
            ["Email:Enabled"] = "true", ["Email:Provider"] = "unisender-go",
            ["Smtp:Port"] = "587", ["Smtp:UseSsl"] = "false",
            ["Sms:Enabled"] = "false", ["Delivery:LogOnly"] = "true",
            ["Telegram:Enabled"] = "false",
            ["TelegramClient:Enabled"] = "false",
            ["Max:Enabled"] = "false",
            ["UnisenderGo:WebhookEnabled"] = "true", ["UnisenderGo:WebhookKey"] = "test-webhook-key",
            ["SystemTemplates:Applications:opencrm-test:BrandName"] = "TEST OPENCRM",
            ["SystemTemplates:Applications:opencrm-test:Templates:0"] = "system/email-confirmation",
            ["SystemTemplates:Applications:opencrm-test:Templates:1"] = "system/custom-email"
        });
        StartupConfigurationValidator.Validate(builder.Configuration, builder.Environment);
        builder.Services.AddNotificationInfrastructure(builder.Configuration);
        await using var worker = builder.Build();
        worker.MapNotificationWebhooks();
        await worker.Services.ApplyNotificationMigrationsAsync();
        await worker.StartAsync();

        var clientServices = new ServiceCollection();
        var options = new RabbitMqOptions
        {
            ApplicationId = "opencrm-test", DispatchIntervalSeconds = 1, ResponseTimeoutSeconds = 60, SendTimeoutSeconds = 10
        };
        clientServices.AddLogging();
        clientServices.AddScoped(_ => new OpenCRMContext(opencrmOptions));
        clientServices.AddSingleton<IOptions<RabbitMqOptions>>(Options.Create(options));
        clientServices.AddMassTransit(bus =>
        {
            bus.AddConsumer<NotificationResultConsumer>();
            bus.AddConsumer<NotificationDeliveryConsumer>();
            bus.UsingRabbitMq((registration, transport) =>
            {
                transport.Host(rabbit.Hostname, rabbit.GetMappedPublicPort(5672), "/", host =>
                {
                    host.Username("rabbitmq");
                    host.Password("rabbitmq");
                });
                transport.ReceiveEndpoint(NotificationQueues.Results(options.ApplicationId), endpoint =>
                {
                    endpoint.ConfigureConsumeTopology = false;
                    endpoint.ConfigureConsumer<NotificationResultConsumer>(registration);
                    endpoint.ConfigureConsumer<NotificationDeliveryConsumer>(registration);
                });
            });
        });
        await using var provider = clientServices.BuildServiceProvider();
        var client = provider.GetRequiredService<IBusControl>();
        await client.StartAsync();
        try
        {
            var sender = new EmailSender(context);
            await sender.QueueTemplateAsync("test@example.com", "Подтверждение", "system/email-confirmation",
                new { name = "Иван", confirmationUrl = "https://example.com/confirm?code=isolated" }, "full-flow");
            var dispatcher = new NotificationOutboxDispatcher(context, client, Options.Create(options));
            await dispatcher.DispatchNextAsync(CancellationToken.None);
            await WaitAsync(context, () => context.EmailMessages.AnyAsync(x => x.NotificationStatus == "simulated"));
            var message = await context.EmailMessages.SingleAsync();
            Assert.Contains("<!doctype html>", message.HtmlBody);
            Assert.Contains("TEST OPENCRM", message.HtmlBody);
            Assert.Contains("Иван", message.TextBody);
            Assert.Null(message.SentAt);
            Assert.Null(message.NextDispatchAt);

            using var http = new HttpClient { BaseAddress = new Uri(worker.Urls.Single()) };
            var webhook = CreateSignedWebhook();
            foreach (var iteration in Enumerable.Range(0, 2))
            {
                using var payload = new StringContent(webhook, Encoding.UTF8, "application/json");
                using var response = await http.PostAsync("/webhooks/unisender-go", payload);
                response.EnsureSuccessStatusCode();
            }

            await WaitAsync(context, () => context.EmailMessageEvents.AnyAsync());
            message = await context.EmailMessages.SingleAsync();
            Assert.Equal("opened", message.StatusCode);
            Assert.Equal(1, message.OpenCount);
            Assert.Equal(1, await context.EmailMessageEvents.CountAsync());
            using var invalidPayload = new StringContent(webhook.Replace("opened", "clicked"), Encoding.UTF8, "application/json");
            using var rejected = await http.PostAsync("/webhooks/unisender-go", invalidPayload);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            using var absent = await http.PostAsync("/api/notifications", new StringContent("{}"));
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        }
        finally
        {
            await client.StopAsync();
            await worker.StopAsync();
        }
    }

    private static async Task WaitAsync(OpenCRMContext context, Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (!await predicate())
        {
            await Task.Delay(50, timeout.Token);
        }

        context.ChangeTracker.Clear();
    }

    private static string CreateSignedWebhook()
    {
        const string key = "test-webhook-key";
        var body = JsonSerializer.Serialize(new
        {
            auth = key,
            events = new[]
            {
                new
                {
                    event_name = "transactional_email_status",
                    event_data = new
                    {
                        status = "opened", event_time = "2026-09-09 11:00:00", email = "test@example.com", job_id = "test-job",
                        metadata = new { application_id = "opencrm-test", notification_key = "full-flow" }
                    }
                }
            }
        });
        var signature = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        return body.Replace(key, signature, StringComparison.Ordinal);
    }
}
