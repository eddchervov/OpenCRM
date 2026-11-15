using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Infrastructure.Payments.ServiceIntegration;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using Payments.Service.Configuration;
using Payments.Service.Endpoints;
using Payments.Service.Persistence;
using Payments.Service.Processing;
using Payments.Service.Providers;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Moq;
using System.Threading.RateLimiting;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class PaymentsServiceTransportAcceptanceFixture : IAsyncLifetime
{
    private const string VirtualHost = "payments";
    private const string BrokerUser = "payments-acceptance";
    private const string BrokerPassword = "acceptance-only-broker-password";
    private const string ServiceKey = "acceptance-only-service-key-32-chars";
    private const string ServiceSecret = "test_acceptance_only_fake_secret";
    private const string ReturnUrl = "https://app.example.test/payment/complete";
    private const string MerchantAccountId = "123456789";
    private readonly PostgreSqlContainer _opencrmPostgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly PostgreSqlContainer _paymentsPostgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername(BrokerUser).WithPassword(BrokerPassword).WithPortBinding(15672, true).Build();
    private WebApplication? _paymentsApplication;
    private IHost? _opencrmHost;
    private bool _acceptNewPurchases = true;
    private DbContextOptions<OpenCRMContext> _opencrmContextOptions = null!;
    private DbContextOptions<PaymentDbContext> _paymentsContextOptions = null!;

    public FakePaymentProvider Provider { get; } = new(MerchantAccountId);

    public IServiceProvider OpenCRMServices => this._opencrmHost?.Services
        ?? throw new InvalidOperationException("OpenCRM acceptance host не запущен.");

    public IServiceProvider PaymentsServices => this._paymentsApplication?.Services
        ?? throw new InvalidOperationException("Payments acceptance host не запущен.");

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            this._opencrmPostgres.StartAsync(),
            this._paymentsPostgres.StartAsync(),
            this._rabbit.StartAsync());
        await this.CreatePaymentsVirtualHostAsync();

        this._opencrmContextOptions = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._opencrmPostgres.GetConnectionString(), postgres =>
            {
                postgres.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                postgres.EnableRetryOnFailure();
            })
            .Options;
        this._paymentsContextOptions = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(this._paymentsPostgres.GetConnectionString(), postgres =>
            {
                postgres.MigrationsAssembly(typeof(PaymentDbContext).Assembly.GetName().Name);
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", "payments");
            })
            .Options;

        await using (var opencrmContext = this.CreateOpenCRMContext())
        {
            await opencrmContext.Database.MigrateAsync();
        }

        await this.StartPaymentsServiceAsync();
        await this.StartOpenCRMAsync();
    }

    public OpenCRMContext CreateOpenCRMContext() => new(this._opencrmContextOptions);

    public PaymentDbContext CreatePaymentsContext() => new(this._paymentsContextOptions);

    public async Task DisposeAsync()
    {
        if (this._opencrmHost != null)
        {
            await this._opencrmHost.StopAsync();
            this._opencrmHost.Dispose();
        }

        if (this._paymentsApplication != null)
        {
            await this._paymentsApplication.StopAsync();
            await this._paymentsApplication.DisposeAsync();
        }

        await this._rabbit.DisposeAsync();
        await this._paymentsPostgres.DisposeAsync();
        await this._opencrmPostgres.DisposeAsync();
    }

    private async Task CreatePaymentsVirtualHostAsync()
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{this._rabbit.Hostname}:{this._rabbit.GetMappedPublicPort(15672)}")
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{BrokerUser}:{BrokerPassword}")));
        using var createVhost = await client.PutAsync($"/api/vhosts/{VirtualHost}", null);
        createVhost.EnsureSuccessStatusCode();

        using var permissions = await client.PutAsJsonAsync($"/api/permissions/{VirtualHost}/{BrokerUser}", new
        {
            configure = ".*",
            write = ".*",
            read = ".*"
        });
        permissions.EnsureSuccessStatusCode();
    }

    private async Task StartPaymentsServiceAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(this.GetPaymentsConfiguration());
        builder.Services.AddRateLimiter(options => options.AddPolicy("webhooks", context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                })));
        builder.Services.AddPayments(builder.Configuration);
        builder.Services.RemoveAll<IPaymentProvider>();
        builder.Services.AddSingleton<IPaymentProvider>(this.Provider);

        this._paymentsApplication = builder.Build();
        this._paymentsApplication.UseRateLimiter();
        this._paymentsApplication.MapPaymentEndpoints();
        await using (var scope = this._paymentsApplication.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
        }

        await this._paymentsApplication.StartAsync();
    }

    public async Task RestartOpenCRMAsync(bool acceptNewPurchases)
    {
        await this._opencrmHost!.StopAsync();
        this._opencrmHost.Dispose();
        this._opencrmHost = null;
        this._acceptNewPurchases = acceptNewPurchases;
        await this.StartOpenCRMAsync();
    }

    private async Task StartOpenCRMAsync()
    {
        var server = this._paymentsApplication!.Services.GetRequiredService<IServer>();
        var paymentsAddress = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(this.GetOpenCRMConfiguration(paymentsAddress)).Build();

        this._opencrmHost = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<OpenCRMContext>(options => options.UseNpgsql(
                    this._opencrmPostgres.GetConnectionString(), postgres =>
                    {
                        postgres.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                        postgres.EnableRetryOnFailure();
                    }));
                services.AddSingleton<IPromotionPaymentCoordinator>(PaymentsServiceTransportAcceptanceFixture.CreatePromotionsCoordinator().Object);
                services.AddSingleton(Mock.Of<IOrganizationPermissionService>());
                services.AddOpenCRMPaymentsInfrastructure(configuration);
                services.AddMassTransit(registration => registration.UsingInMemory((_, _) => { }));
            })
            .Build();

        await this._opencrmHost.StartAsync();
    }

    private IEnumerable<KeyValuePair<string, string?>> GetPaymentsConfiguration()
    {
        var rabbitPort = this._rabbit.GetMappedPublicPort(5672);
        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:PaymentsDb"] = this._paymentsPostgres.GetConnectionString(),
            ["Payments:Mode"] = "YooKassaTest",
            ["Payments:ApplicationId"] = "opencrm",
            ["Payments:MerchantAccountId"] = MerchantAccountId,
            ["Payments:SecretKey"] = ServiceSecret,
            ["Payments:ServiceKey"] = ServiceKey,
            ["Payments:AllowedReturnUrls:0"] = ReturnUrl,
            ["Payments:PollSeconds"] = "1",
            ["RabbitMq:Host"] = this._rabbit.Hostname,
            ["RabbitMq:Port"] = rabbitPort.ToString(CultureInfo.InvariantCulture),
            ["RabbitMq:VirtualHost"] = VirtualHost,
            ["RabbitMq:Username"] = BrokerUser,
            ["RabbitMq:Password"] = BrokerPassword
        };
    }

    private IEnumerable<KeyValuePair<string, string?>> GetOpenCRMConfiguration(string paymentsAddress)
    {
        var rabbitPort = this._rabbit.GetMappedPublicPort(5672);
        return new Dictionary<string, string?>
        {
            ["PlatformPolicy:Commercial:Enabled"] = "true",
            ["PaymentsService:Enabled"] = "true",
            ["PaymentsService:AcceptNewPurchases"] = this._acceptNewPurchases.ToString(),
            ["PaymentsService:ExpectedIsTest"] = "true",
            ["PaymentsService:MerchantAccountId"] = MerchantAccountId,
            ["PaymentsService:BaseUrl"] = paymentsAddress,
            ["PaymentsService:ServiceKey"] = ServiceKey,
            ["PaymentsService:ReturnUrl"] = ReturnUrl,
            ["PaymentsService:AllowedReturnUrls:0"] = ReturnUrl,
            ["PaymentsService:Host"] = this._rabbit.Hostname,
            ["PaymentsService:Port"] = rabbitPort.ToString(CultureInfo.InvariantCulture),
            ["PaymentsService:VirtualHost"] = VirtualHost,
            ["PaymentsService:Username"] = BrokerUser,
            ["PaymentsService:Password"] = BrokerPassword,
            ["PaymentsService:PollSeconds"] = "1",
            ["PaymentsService:SendTimeoutSeconds"] = "3",
            ["PaymentsService:AllowedConfirmationHosts:0"] = "provider.example.test",
            ["YooKassa:ShopId"] = "test-only",
            ["YooKassa:SecretKey"] = "test_only_no_external_request"
        };
    }

    private static Mock<IPromotionPaymentCoordinator> CreatePromotionsCoordinator()
    {
        var promotions = new Mock<IPromotionPaymentCoordinator>(MockBehavior.Strict);
        promotions.Setup(service => service.ClaimAsync(It.IsAny<OpenCRM.Domain.PaymentCreationOperation>()))
            .Returns(Task.CompletedTask);
        promotions.Setup(service => service.AttachPaymentAsync(
                It.IsAny<OpenCRM.Domain.PaymentCreationOperation>(), It.IsAny<OpenCRM.Domain.SubscriptionPayment>()))
            .Returns(Task.CompletedTask);
        promotions.Setup(service => service.ConsumeAsync(
                It.IsAny<OpenCRM.Domain.SubscriptionPayment>(), It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);

        return promotions;
    }
}

public sealed class FakePaymentProvider(string merchantAccountId) : IPaymentProvider
{
    private readonly Dictionary<string, ProviderPayment> _payments = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private int _creationCount;

    public int CreationCount => Volatile.Read(ref this._creationCount);

    public string BuildPaymentRequest(global::Payments.Service.Domain.PaymentOrder order) => JsonSerializer.Serialize(new
    {
        amount = new { value = (order.AmountMinor / 100m).ToString("F2", CultureInfo.InvariantCulture), currency = order.Currency },
        metadata = new { paymentOrderId = order.Id.ToString("D"), purchaseId = order.PurchaseId.ToString("D"), applicationId = order.ApplicationId }
    });

    public string BuildRefundRequest(global::Payments.Service.Domain.PaymentOrder order, long amountMinor, string reason) =>
        throw new NotSupportedException("Возврат не входит в acceptance-путь.");

    public Task<ProviderPayment> CreatePaymentAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var request = JsonDocument.Parse(requestBody);
        var amount = request.RootElement.GetProperty("amount");
        var amountMinor = decimal.ToInt64(decimal.Parse(amount.GetProperty("value").GetString()!, CultureInfo.InvariantCulture) * 100m);
        var paymentOrderId = Guid.Parse(request.RootElement.GetProperty("metadata").GetProperty("paymentOrderId").GetString()!);
        var providerId = "fake-" + idempotenceKey;
        var payment = new ProviderPayment(providerId, "pending", amountMinor, "RUB", false, true, 0,
            "https://provider.example.test/payment/" + Uri.EscapeDataString(providerId), paymentOrderId.ToString("D"), merchantAccountId);

        lock (this._sync)
        {
            if (this._payments.TryAdd(providerId, payment))
            {
                Interlocked.Increment(ref this._creationCount);
            }

            return Task.FromResult(this._payments[providerId]);
        }
    }

    public Task<ProviderPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this._sync)
        {
            return Task.FromResult(this._payments.TryGetValue(paymentId, out var payment)
                ? payment
                : throw new KeyNotFoundException("Fake provider payment id was not created."));
        }
    }

    public Task<ProviderRefund> CreateRefundAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Возврат не входит в acceptance-путь.");

    public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Возврат не входит в acceptance-путь.");

    public void MarkPaymentsSucceeded()
    {
        lock (this._sync)
        {
            foreach (var (paymentId, payment) in this._payments.ToArray())
            {
                this._payments[paymentId] = payment with { Status = "succeeded", Paid = true, ConfirmationUrl = null };
            }
        }
    }
}
