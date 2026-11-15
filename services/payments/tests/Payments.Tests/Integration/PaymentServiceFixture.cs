using DotNet.Testcontainers.Builders;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payments.Contracts;
using Payments.Service.Domain;
using Payments.Service.Persistence;
using Payments.Service.Processing;
using Payments.Service.Providers;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Payments.Tests.Integration;

public sealed class PaymentServiceFixture : IAsyncLifetime
{
    public const string ServiceKey = "payments-integration-tests-service-key-only";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername("payments").WithPassword("payments-integration-tests-only")
        .WithEnvironment("RABBITMQ_DEFAULT_VHOST", "payments").Build();

    private WebApplicationFactory<Program>? _application;

    public HttpClient Client { get; private set; } = null!;
    public IServiceProvider Services => this._application!.Services;
    public string RabbitHost => this._rabbit.Hostname;
    public ushort RabbitPort => this._rabbit.GetMappedPublicPort(5672);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(this._postgres.StartAsync(), this._rabbit.StartAsync());
        await using (var database = this.CreateDatabase())
        {
            await database.Database.MigrateAsync();
        }

        this._application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PaymentsDb", this._postgres.GetConnectionString());
            builder.UseSetting("Payments:ServiceKey", ServiceKey);
            builder.UseSetting("Payments:Mode", "YooKassaTest");
            builder.UseSetting("Payments:MerchantAccountId", "12345");
            builder.UseSetting("Payments:SecretKey", "test_integration_only");
            builder.UseSetting("Payments:ApplicationId", "opencrm");
            builder.UseSetting("Brand:PublicBaseUrl", "https://opencrm.example.invalid");
            builder.UseSetting("RabbitMq:Host", this.RabbitHost);
            builder.UseSetting("RabbitMq:Port", this.RabbitPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RabbitMq:VirtualHost", "payments");
            builder.UseSetting("RabbitMq:Username", "payments");
            builder.UseSetting("RabbitMq:Password", "payments-integration-tests-only");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPaymentProvider>();
                services.AddSingleton<IPaymentProvider>(new NoNetworkPaymentProvider());
                var worker = services.FirstOrDefault(descriptor => descriptor.ImplementationType == typeof(PaymentWorker));
                if (worker != null)
                {
                    services.Remove(worker);
                }
            });
        });

        this.Client = this._application.CreateClient();
        this.Client.DefaultRequestHeaders.Add("X-Payments-Key", ServiceKey);
        await this.Services.GetRequiredService<IBusControl>().StartAsync();
    }

    public PaymentDbContext CreateDatabase() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(this._postgres.GetConnectionString(),
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "payments")).Options);

    public async Task<Guid> SendAsync<T>(T command) where T : class
    {
        var endpoint = await this.Services.GetRequiredService<IBus>().GetSendEndpoint(new Uri("queue:payments-opencrm-commands"));
        var messageId = Guid.NewGuid();
        await endpoint.Send(command, context => context.MessageId = messageId);

        return messageId;
    }

    public async Task WaitAsync(Func<PaymentDbContext, Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            await using var database = this.CreateDatabase();
            if (await condition(database))
            {
                return;
            }

            await Task.Delay(100, timeout.Token);
        }

        throw new TimeoutException("Условие не выполнено в тестовом сервисе Payments.");
    }

    public async Task DisposeAsync()
    {
        this.Client?.Dispose();
        if (this._application != null)
        {
            await this._application.DisposeAsync();
        }

        await this._rabbit.DisposeAsync();
        await this._postgres.DisposeAsync();
    }

    public static CreatePayment Command()
    {
        var purchaseId = Guid.NewGuid();

        var now = DateTimeOffset.UtcNow;
        var issuedAt = new DateTimeOffset(now.UtcDateTime.Ticks - now.UtcDateTime.Ticks % 10, TimeSpan.Zero);

        return new CreatePayment(Guid.NewGuid(), "opencrm", purchaseId, 125_050,
            "RUB", "Подписка opencrm.example.invalid", $"https://opencrm.example.invalid/payment/complete?purchaseId={purchaseId:D}",
            "buyer", "organization", IssuedAt: issuedAt, NotAfter: issuedAt.AddHours(22), MerchantAccountId: "12345");
    }

    private sealed class NoNetworkPaymentProvider : IPaymentProvider
    {
        public string BuildPaymentRequest(PaymentOrder order) => System.Text.Json.JsonSerializer.Serialize(new
        {
            metadata = new
            {
                paymentOrderId = order.Id,
                purchaseId = order.PurchaseId,
                applicationId = order.ApplicationId,
                merchantAccountId = order.MerchantAccountId
            }
        });

        public string BuildRefundRequest(PaymentOrder order, long amountMinor, string reason) => "{}";

        public Task<ProviderPayment> CreatePaymentAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Тестовая фикстура не вызывает провайдера.");

        public Task<ProviderPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Тестовая фикстура не вызывает провайдера.");

        public Task<ProviderRefund> CreateRefundAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Тестовая фикстура не вызывает провайдера.");

        public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Тестовая фикстура не вызывает провайдера.");
    }
}
