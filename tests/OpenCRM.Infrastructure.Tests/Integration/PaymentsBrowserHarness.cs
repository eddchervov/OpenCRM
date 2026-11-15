using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Payments.Contracts;
using Payments.Service.Configuration;
using Payments.Service.Endpoints;
using Payments.Service.Messaging;
using Payments.Service.Persistence;
using Payments.Service.Providers;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace OpenCRM.Infrastructure.Tests.Integration;

internal sealed class PaymentsBrowserHarness : IAsyncDisposable
{
    private const string BaseUrl = "https://localhost:7245";
    internal const string MainApplicationReturnUrl = "https://localhost:7244/payment/complete";
    private const string MerchantAccountId = "999999999";
    private const int MaximumRunMinutes = 60;
    private readonly PostgreSqlContainer _postgres;
    private readonly RabbitMqContainer _rabbit;
    private readonly WebApplication _application;
    private readonly string _artifactDirectory;
    private readonly string _stopFilePath;

    private PaymentsBrowserHarness(
        PostgreSqlContainer postgres,
        RabbitMqContainer rabbit,
        WebApplication application,
        string artifactDirectory,
        string stopFilePath)
    {
        this._postgres = postgres;
        this._rabbit = rabbit;
        this._application = application;
        this._artifactDirectory = artifactDirectory;
        this._stopFilePath = stopFilePath;
    }

    public static async Task<PaymentsBrowserHarness> StartAsync()
    {
        var workspaceDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var artifactDirectory = Path.GetFullPath(Environment.GetEnvironmentVariable("PAYMENTS_BROWSER_ARTIFACTS")
            ?? Path.Combine(workspaceDirectory, "artifacts", "payments-browser-qa"));
        Directory.CreateDirectory(artifactDirectory);
        var stopFilePath = Path.Combine(artifactDirectory, "stop");
        if (File.Exists(stopFilePath))
        {
            File.Delete(stopFilePath);
        }

        var brokerUsername ="qa_" + Guid.NewGuid().ToString("N")[..12];
        var brokerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var virtualHost = "payments-qa-" + Guid.NewGuid().ToString("N")[..12];
        var serviceKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var qaControlKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var providerKind = Environment.GetEnvironmentVariable("PAYMENTS_BROWSER_PROVIDER") ?? "Fake";
        if (providerKind is not ("Fake" or "YooKassaTest"))
        {
            throw new InvalidOperationException("Неизвестный провайдер браузерного стенда.");
        }

        var useRealProvider = providerKind == "YooKassaTest";
        var merchantAccountId = useRealProvider
            ? Environment.GetEnvironmentVariable("PAYMENTS_BROWSER_SHOP_ID") ?? string.Empty
            : MerchantAccountId;
        var providerSecret = useRealProvider
            ? Environment.GetEnvironmentVariable("PAYMENTS_BROWSER_TEST_KEY") ?? string.Empty
            : "test_local_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        if (useRealProvider && (merchantAccountId.Length == 0 || !merchantAccountId.All(char.IsAsciiDigit)
            || !providerSecret.StartsWith("test_", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Для реального API разрешены только реквизиты тестового магазина.");
        }
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        var rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine")
            .WithUsername(brokerUsername)
            .WithPassword(brokerPassword)
            .WithPortBinding(15672, true)
            .Build();
        WebApplication? application = null;

        try
        {
            await Task.WhenAll(postgres.StartAsync(), rabbit.StartAsync());
            await CreateVirtualHostAsync(rabbit, brokerUsername, brokerPassword, virtualHost);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(7245, endpoint => endpoint.UseHttps()));
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PaymentsDb"] = postgres.GetConnectionString(),
                ["Payments:Mode"] = "YooKassaTest",
                ["Payments:ApplicationId"] = "opencrm",
                ["Payments:MerchantAccountId"] = merchantAccountId,
                ["Payments:SecretKey"] = providerSecret,
                ["Payments:ServiceKey"] = serviceKey,
                ["Payments:AllowedReturnUrls:0"] = MainApplicationReturnUrl,
                ["Payments:PollSeconds"] = "1",
                ["RabbitMq:Host"] = rabbit.Hostname,
                ["RabbitMq:Port"] = rabbit.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["RabbitMq:VirtualHost"] = virtualHost,
                ["RabbitMq:Username"] = brokerUsername,
                ["RabbitMq:Password"] = brokerPassword
            });
            builder.Services.AddPayments(builder.Configuration);
            if (!useRealProvider)
            {
                var fakeProvider = new PaymentsBrowserFakeProvider(merchantAccountId, BaseUrl);
                builder.Services.RemoveAll<IPaymentProvider>();
                builder.Services.AddSingleton<IPaymentProvider>(fakeProvider);
                builder.Services.AddSingleton(fakeProvider);
            }
            builder.Services.AddRateLimiter(options => options.AddPolicy("webhooks", context =>
                RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local-qa", _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    })));

            application = builder.Build();
            application.UseRateLimiter();
            application.MapHealthChecks("/health/ready");
            application.MapPaymentEndpoints();
            if (!useRealProvider)
            {
                MapQaEndpoints(application, qaControlKey, application.Services.GetRequiredService<PaymentsBrowserFakeProvider>(), rabbit);
            }
            else
            {
                MapRealTestRefundEndpoint(application, qaControlKey);
            }
            await using (var scope = application.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
            }

            object? scenario = null;
            var mainConnectionString = Environment.GetEnvironmentVariable("PAYMENTS_BROWSER_MAIN_CONNECTION");
            if (!string.IsNullOrWhiteSpace(mainConnectionString))
            {
                scenario = await PaymentsBrowserSeed.SeedAsync(mainConnectionString, useRealProvider);
            }

            await application.StartAsync();
            await WriteHarnessConfigurationAsync(
                artifactDirectory,
                new PaymentsBrowserHarnessConfiguration(
                    rabbit.Hostname,
                    rabbit.GetMappedPublicPort(5672),
                    virtualHost,
                    brokerUsername,
                    brokerPassword,
                    serviceKey,
                    qaControlKey,
                    merchantAccountId,
                    BaseUrl,
                    Environment.ProcessId,
                    DateTimeOffset.UtcNow,
                    providerKind,
                    scenario));
            Console.WriteLine($"Локальный payments harness запущен: {BaseUrl}. Остановка: создайте файл {stopFilePath}.");

            return new PaymentsBrowserHarness(postgres, rabbit, application, artifactDirectory, stopFilePath);
        }
        catch
        {
            if (application is not null)
            {
                await application.DisposeAsync();
            }

            await rabbit.DisposeAsync();
            await postgres.DisposeAsync();
            throw;
        }
    }

    public async Task RunUntilStoppedAsync()
    {
        var stopAt = DateTimeOffset.UtcNow.AddMinutes(MaximumRunMinutes);
        while (!File.Exists(this._stopFilePath) && DateTimeOffset.UtcNow < stopAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await this._application.StopAsync();
        await this._application.DisposeAsync();
        await this._rabbit.DisposeAsync();
        await this._postgres.DisposeAsync();
        var configurationPath = Path.Combine(this._artifactDirectory, "harness.json");
        if (File.Exists(configurationPath))
        {
            File.Delete(configurationPath);
        }
    }

    private static async Task CreateVirtualHostAsync(
        RabbitMqContainer rabbit,
        string username,
        string password,
        string virtualHost)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{rabbit.Hostname}:{rabbit.GetMappedPublicPort(15672)}")
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
        using var createVhost = await client.PutAsync($"/api/vhosts/{Uri.EscapeDataString(virtualHost)}", null);
        createVhost.EnsureSuccessStatusCode();
        using var permissions = await client.PutAsJsonAsync(
            $"/api/permissions/{Uri.EscapeDataString(virtualHost)}/{Uri.EscapeDataString(username)}",
            new { configure = ".*", write = ".*", read = ".*" });
        permissions.EnsureSuccessStatusCode();
    }

    private static void MapQaEndpoints(
        WebApplication application,
        string controlKey,
        PaymentsBrowserFakeProvider provider,
        RabbitMqContainer rabbit)
    {
        application.MapGet("/__qa/checkout/{providerId}", (string providerId) =>
        {
            var payment = provider.FindPayment(providerId);
            if (payment is null)
            {
                return Results.NotFound();
            }

            var markup = $"""<!doctype html><html lang="ru"><meta charset="utf-8"><title>Локальная оплата</title><body style="font:16px system-ui;max-width:640px;margin:4rem auto;padding:2rem;color:#17324d"><h1>Локальная проверка, деньги не списываются</h1><p>Платёж {WebUtility.HtmlEncode(payment.Id)} на сумму {payment.AmountMinor / 100m:0.00} RUB.</p><p>Этот экран работает только с тестовым fake provider.</p><form method="post" action="/__qa/checkout/{Uri.EscapeDataString(payment.Id)}/complete"><button>Оплатить тест</button></form><form method="post" action="/__qa/checkout/{Uri.EscapeDataString(payment.Id)}/cancel"><button>Отменить тест</button></form></body></html>""";

            return Results.Content(markup, "text/html; charset=utf-8");
        });
        application.MapPost("/__qa/checkout/{providerId}/complete", (string providerId) =>
        {
            var returnUrl = provider.MarkPaid(providerId);

            return returnUrl is null ? Results.NotFound() : Results.Redirect(returnUrl);
        });
        application.MapPost("/__qa/checkout/{providerId}/cancel", (string providerId) =>
        {
            var returnUrl = provider.CancelPayment(providerId);

            return returnUrl is null ? Results.NotFound() : Results.Redirect(returnUrl);
        });
        application.MapPost("/__qa/control", async (
            HttpContext context,
            QaControlRequest request,
            PaymentCommandService commands,
            PaymentDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context, controlKey))
            {
                return Results.Unauthorized();
            }

            if (request.Action == "mode")
            {
                if (!provider.TrySetMode(request.Mode))
                {
                    return Results.BadRequest(new { error = "unsupported_mode" });
                }

                return Results.Ok(provider.GetSnapshot());
            }

            if (request.Action == "forcepaid")
            {
                provider.MarkAllPaymentsPaid();

                return Results.Ok(provider.GetSnapshot());
            }

            if (request.Action == "broker_stop")
            {
                await rabbit.ExecAsync(["rabbitmqctl", "stop_app"], cancellationToken);

                return Results.Ok(new { broker = "stopped" });
            }

            if (request.Action == "broker_start")
            {
                await rabbit.ExecAsync(["rabbitmqctl", "start_app"], cancellationToken);

                return Results.Ok(new { broker = "started" });
            }

            if (request.Action == "testrefund")
            {
                if (!Guid.TryParse(request.PurchaseId, out var purchaseId)
                    || request.Kind is not ("full" or "partial"))
                {
                    return Results.BadRequest(new { error = "invalid_test_refund" });
                }

                var order = await database.PaymentOrders.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.PurchaseId == purchaseId, cancellationToken);
                if (order is null || order.Status != global::Payments.Service.Domain.PaymentStatus.Succeeded)
                {
                    return Results.Conflict(new { error = "paid_purchase_required" });
                }

                var refundAmount = request.Kind == "full"
                    ? order.AmountMinor - order.RefundedAmountMinor
                    : Math.Max(1, order.AmountMinor / 2);
                var issuedAt = DateTimeOffset.UtcNow;
                await commands.AcceptAsync(new RequestRefund(
                    Guid.NewGuid(),
                    order.ApplicationId,
                    purchaseId,
                    refundAmount,
                    "Локальная QA-проверка возврата",
                    "local-browser-qa",
                    SchemaVersion: 2,
                    IssuedAt: issuedAt,
                    NotAfter: issuedAt.AddHours(23),
                    MerchantAccountId: MerchantAccountId), cancellationToken);

                return Results.Accepted(value: new { purchaseId, amountMinor = refundAmount, request.Kind });
            }

            return request.Action == "snapshot"
                ? Results.Ok(provider.GetSnapshot())
                : Results.BadRequest(new { error = "unsupported_action" });
        });
    }

    private static void MapRealTestRefundEndpoint(WebApplication application, string controlKey)
    {
        application.MapPost("/__qa/refunds", async (
            HttpContext context,
            QaControlRequest request,
            PaymentCommandService commands,
            PaymentDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context, controlKey))
            {
                return Results.Unauthorized();
            }

            if (request.Action != "testrefund" || !Guid.TryParse(request.PurchaseId, out var purchaseId)
                || request.Kind is not ("full" or "partial"))
            {
                return Results.BadRequest();
            }

            var order = await database.PaymentOrders.AsNoTracking()
                .SingleOrDefaultAsync(item => item.PurchaseId == purchaseId, cancellationToken);
            if (order is null || !order.IsTest || order.Status != global::Payments.Service.Domain.PaymentStatus.Succeeded)
            {
                return Results.Conflict();
            }

            var amount = request.Kind == "full"
                ? order.AmountMinor - order.RefundedAmountMinor : Math.Max(1, order.AmountMinor / 2);
            var issuedAt = DateTimeOffset.UtcNow;
            await commands.AcceptAsync(new RequestRefund(
                Guid.NewGuid(), order.ApplicationId, purchaseId, amount,
                "Проверка возврата тестового платежа", "local-browser-qa",
                SchemaVersion: 2, IssuedAt: issuedAt, NotAfter: issuedAt.AddHours(23),
                MerchantAccountId: order.MerchantAccountId), cancellationToken);

            return Results.Accepted(value: new { purchaseId, amountMinor = amount });
        });
    }

    private static bool IsAuthorized(HttpContext context, string controlKey)
    {
        var supplied = Encoding.UTF8.GetBytes(context.Request.Headers["X-QA-Key"].ToString());
        var expected = Encoding.UTF8.GetBytes(controlKey);

        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static async Task WriteHarnessConfigurationAsync(string artifactDirectory, PaymentsBrowserHarnessConfiguration configuration)
    {
        var configurationPath = Path.Combine(artifactDirectory, "harness.json");
        await File.WriteAllTextAsync(configurationPath, JsonSerializer.Serialize(configuration, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    private sealed record QaControlRequest(string Action, string? Mode = null, string? Kind = null, string? PurchaseId = null);
}

internal sealed record PaymentsBrowserHarnessConfiguration(
    string BrokerHost,
    int BrokerPort,
    string BrokerVirtualHost,
    string BrokerUsername,
    string BrokerPassword,
    string ServiceKey,
    string QaControlKey,
    string MerchantAccountId,
    string BaseUrl,
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    string ProviderKind,
    object? Scenario);
