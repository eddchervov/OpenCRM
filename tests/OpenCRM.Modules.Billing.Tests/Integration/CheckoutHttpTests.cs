using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using OpenCRM.WebApp.Controllers.Api.V1;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class CheckoutHttpTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public static IEnumerable<object[]> StatusCases()
    {
        foreach (var command in new[] { "personal-purchase", "personal-extend", "workspace-purchase", "workspace-extend" })
        {
            foreach (var status in Enum.GetValues<PaymentCreationStatus>())
            {
                yield return [command, status];
            }
        }
    }

    [Theory]
    [MemberData(nameof(StatusCases))]
    public async Task PaidRoutesPreserveBuyerBodyAndOperationKeyAndExposeUnambiguousStatus(string command, PaymentCreationStatus status)
    {
        var operationId = Guid.NewGuid();
        var observedIds = new List<Guid>();
        var result = new PaymentCreationResult(status, "Результат операции", "payment", status == PaymentCreationStatus.PaymentRequired ? "https://pay.example/payment" : null);
        using var host = await this.CreateHostAsync(result, observedIds);
        using var client = host.GetTestClient();
        var route = CheckoutHttpTests.Route(command);

        using var anonymous = await client.PostAsJsonAsync(route, CheckoutHttpTests.Body(command));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "checkout-user");
        client.DefaultRequestHeaders.Add("Idempotency-Key", operationId.ToString("D"));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1)
            {
                client.DefaultRequestHeaders.Remove("Idempotency-Key");
                client.DefaultRequestHeaders.Add("Idempotency-Key", operationId.ToString("N"));
            }
            using var response = await client.PostAsJsonAsync(route, CheckoutHttpTests.Body(command));
            var expectedCode = status switch
            {
                PaymentCreationStatus.PaymentRequired or PaymentCreationStatus.Completed => HttpStatusCode.OK,
                PaymentCreationStatus.InProgress => HttpStatusCode.ServiceUnavailable,
                PaymentCreationStatus.Conflict or PaymentCreationStatus.ManualReview => HttpStatusCode.Conflict,
                _ => HttpStatusCode.BadRequest
            };
            Assert.Equal(expectedCode, response.StatusCode);
            Assert.Equal(operationId.ToString("D"), Assert.Single(response.Headers.GetValues("Idempotency-Key")));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(status.ToString(), body.RootElement.GetProperty("code").GetString());
            Assert.Equal(operationId, body.RootElement.GetProperty("operationId").GetGuid());
            Assert.Equal(status is PaymentCreationStatus.Completed or PaymentCreationStatus.PaymentRequired,
                body.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(status == PaymentCreationStatus.PaymentRequired, body.RootElement.GetProperty("requiresPayment").GetBoolean());
        }

        Assert.Equal(new[] { operationId, operationId }, observedIds);
    }

    [Fact]
    public async Task PurchaseCheckoutReturnsAcceptedWithExactPurchaseIdForAsyncCreation()
    {
        var purchaseId = Guid.NewGuid();
        var result = new PaymentCreationResult(PaymentCreationStatus.InProgress, PurchaseId: purchaseId);
        using var host = await this.CreateHostAsync(result, []);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "checkout-user");
        using var response = await client.PostAsJsonAsync(CheckoutHttpTests.Route("personal-purchase"), CheckoutHttpTests.Body("personal-purchase"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("InProgress", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(purchaseId, body.RootElement.GetProperty("purchaseId").GetGuid());
        Assert.False(body.RootElement.GetProperty("requiresPayment").GetBoolean());
        Assert.False(body.RootElement.GetProperty("error").ValueKind == JsonValueKind.String);
    }

    [Fact]
    public async Task PurchaseStatusIsScopedToCurrentUserAndPurchaseId()
    {
        var purchaseId = Guid.NewGuid();
        var purchaseQuery = new Mock<IPaymentPurchaseQueryService>(MockBehavior.Strict);
        purchaseQuery.Setup(service => service.GetAsync("checkout-user", purchaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentPurchaseState(
                new PaymentCreationResult(PaymentCreationStatus.InProgress, "Оплата получена, активируем подписку.", PurchaseId: purchaseId),
                true,
                "/detailing/organization#billing"));
        purchaseQuery.Setup(service => service.GetAsync("other-user", purchaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentPurchaseState?)null);
        using var host = await this.CreateHostAsync(
            new PaymentCreationResult(PaymentCreationStatus.Completed),
            [],
            purchaseQuery: purchaseQuery);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "checkout-user");
        using var ownerResponse = await client.GetAsync($"/api/v1/payments/purchases/{purchaseId:D}");

        Assert.Equal(HttpStatusCode.Accepted, ownerResponse.StatusCode);
        using var body = JsonDocument.Parse(await ownerResponse.Content.ReadAsStringAsync());
        Assert.Equal(purchaseId, body.RootElement.GetProperty("purchaseId").GetGuid());
        Assert.True(body.RootElement.GetProperty("isPaid").GetBoolean());
        Assert.Equal("/detailing/organization#billing", body.RootElement.GetProperty("primaryActionUrl").GetString());

        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", "other-user");
        using var wrongUserResponse = await client.GetAsync($"/api/v1/payments/purchases/{purchaseId:D}");
        Assert.Equal(HttpStatusCode.NotFound, wrongUserResponse.StatusCode);
        purchaseQuery.Verify(service => service.GetAsync("checkout-user", purchaseId, It.IsAny<CancellationToken>()), Times.Once);
        purchaseQuery.Verify(service => service.GetAsync("other-user", purchaseId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("personal-purchase")]
    [InlineData("personal-extend")]
    [InlineData("workspace-purchase")]
    [InlineData("workspace-extend")]
    public async Task InvalidKeysNeverReachCheckoutAndMissingKeyRemainsCompatible(string command)
    {
        var observedIds = new List<Guid>();
        using var host = await this.CreateHostAsync(new PaymentCreationResult(PaymentCreationStatus.Completed), observedIds);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "checkout-user");
        foreach (var key in new[] { "invalid", Guid.Empty.ToString("D"), Guid.NewGuid().ToString("B") })
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", key);
            using var invalid = await client.PostAsJsonAsync(CheckoutHttpTests.Route(command), CheckoutHttpTests.Body(command));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Empty(observedIds);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        using var response = await client.PostAsJsonAsync(CheckoutHttpTests.Route(command), CheckoutHttpTests.Body(command));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, Assert.Single(observedIds));
        Assert.Equal(observedIds[0].ToString("D"), Assert.Single(response.Headers.GetValues("Idempotency-Key")));
    }

    [Theory]
    [InlineData("personal-purchase")]
    [InlineData("personal-extend")]
    [InlineData("workspace-purchase")]
    [InlineData("workspace-extend")]
    public async Task TestModeBypassesPaidCheckoutAndDoesNotRequirePaymentKey(string command)
    {
        var observedIds = new List<Guid>();
        using var host = await this.CreateHostAsync(new PaymentCreationResult(PaymentCreationStatus.Completed), observedIds, testMode: true);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "checkout-user");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "ignored-in-test-mode");
        using var response = await client.PostAsJsonAsync(CheckoutHttpTests.Route(command), CheckoutHttpTests.Body(command));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(observedIds);
    }

    [Theory]
    [InlineData("workspace-purchase")]
    [InlineData("workspace-extend")]
    public async Task RevokedBillingPermissionReturnsForbiddenWithoutExposingPayment(string command)
    {
        var observedIds = new List<Guid>();
        using var host = await this.CreateHostAsync(new PaymentCreationResult(PaymentCreationStatus.PaymentRequired), observedIds, denied: true);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "checkout-user");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await client.PostAsJsonAsync(CheckoutHttpTests.Route(command), CheckoutHttpTests.Body(command));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(observedIds);
    }

    [Theory]
    [InlineData("workspace-purchase")]
    [InlineData("workspace-extend")]
    public async Task OrganizationOutsideMembershipNeverReachesCheckout(string command)
    {
        var observedIds = new List<Guid>();
        using var host = await this.CreateHostAsync(new PaymentCreationResult(PaymentCreationStatus.PaymentRequired), observedIds);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "checkout-user");
        var route = CheckoutHttpTests.Route(command).Replace(CheckoutHttpTests.OrganizationId.ToString(), Guid.NewGuid().ToString());
        using var response = await client.PostAsJsonAsync(route, CheckoutHttpTests.Body(command));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(observedIds);
    }

    private async Task<IHost> CreateHostAsync(
        PaymentCreationResult result,
        List<Guid> observedIds,
        bool testMode = false,
        bool denied = false,
        Mock<IPaymentPurchaseQueryService>? purchaseQuery = null)
    {
        await using var context = fixture.CreateContext();
        context.Users.Add(new ApplicationUser { Id = "checkout-user", CanUseTestSubscriptions = testMode });
        await context.SaveChangesAsync();
        var personal = new Mock<IPersonalSubscriptionCheckoutService>(MockBehavior.Strict);
        var organization = new Mock<IOrganizationSubscriptionCheckoutService>(MockBehavior.Strict);
        Task<PaymentCreationResult> Record(string userId, PaymentBuyerDto? buyer, Guid operationId)
        {
            if (denied)
            {
                throw new UnauthorizedAccessException("Нет права управлять оплатой.");
            }
            Assert.Equal("checkout-user", userId);
            Assert.Equal("business", buyer?.TypeCode);
            Assert.Equal("Компания", buyer?.Name);
            Assert.Equal("1234567890", buyer?.Inn);
            observedIds.Add(operationId);

            return Task.FromResult(result);
        }
        personal.Setup(service => service.PurchaseAsync("checkout-user", 7, It.IsAny<PaymentBuyerDto>(), It.IsAny<Guid>(), null))
            .Returns((string user, int plan, PaymentBuyerDto buyer, Guid id, string? url) => Record(user, buyer, id));
        personal.Setup(service => service.ExtendAsync("checkout-user", It.IsAny<PaymentBuyerDto>(), It.IsAny<Guid>(), null))
            .Returns((string user, PaymentBuyerDto buyer, Guid id, string? url) => Record(user, buyer, id));
        organization.Setup(service => service.PurchaseAsync("checkout-user", CheckoutHttpTests.OrganizationId, 7, It.IsAny<PaymentBuyerDto>(), It.IsAny<Guid>(), null))
            .Returns((string user, Guid org, int plan, PaymentBuyerDto buyer, Guid id, string? url) => Record(user, buyer, id));
        organization.Setup(service => service.ExtendAsync("checkout-user", CheckoutHttpTests.OrganizationId, It.IsAny<PaymentBuyerDto>(), It.IsAny<Guid>(), null))
            .Returns((string user, Guid org, PaymentBuyerDto buyer, Guid id, string? url) => Record(user, buyer, id));
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(service => service.GetOverviewAsync("checkout-user")).ReturnsAsync(new TeamOverviewDto
        {
            Organizations = [new OrganizationDto { Id = CheckoutHttpTests.OrganizationId }]
        });
        var commands = new Mock<ISubscriptionService>(MockBehavior.Strict);
        commands.Setup(service => service.ActivateSubscriptionForTestingAsync("checkout-user", 7)).ReturnsAsync(SubscriptionResult.CreateSuccess());
        commands.Setup(service => service.ExtendSubscriptionForTestingAsync("checkout-user")).ReturnsAsync(SubscriptionResult.CreateSuccess());
        var orgCommands = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        orgCommands.Setup(service => service.ActivateForTestingAsync("checkout-user", CheckoutHttpTests.OrganizationId, 7)).ReturnsAsync(SubscriptionResult.CreateSuccess());
        orgCommands.Setup(service => service.ExtendSubscriptionForTestingAsync("checkout-user", CheckoutHttpTests.OrganizationId)).ReturnsAsync(SubscriptionResult.CreateSuccess());

        var host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformPolicy:Commercial:Enabled"] = "true"
            })).ConfigureServices(services =>
        {
            services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
            services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<OpenCRMContext>();
            foreach (var type in new[] { typeof(SubscriptionsApiController), typeof(OrganizationWorkspaceApiController) }
                .SelectMany(type => type.GetConstructors().Single().GetParameters()).Select(parameter => parameter.ParameterType).Distinct())
            {
                if (!type.IsInterface || type.IsGenericType)
                {
                    continue;
                }
                var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!;
                services.AddSingleton(type, mock.Object);
            }
            services.AddSingleton(personal.Object);
            services.AddSingleton(organization.Object);
            services.AddSingleton(organizations.Object);
            services.AddSingleton(commands.Object);
            services.AddSingleton(orgCommands.Object);
            if (purchaseQuery == null)
            {
                var defaultPurchaseQuery = new Mock<IPaymentPurchaseQueryService>();
                defaultPurchaseQuery.Setup(service => service.GetAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((PaymentPurchaseState?)null);
                services.AddSingleton(defaultPurchaseQuery.Object);
            }
            else
            {
                services.AddSingleton(purchaseQuery.Object);
            }
            services.AddAuthentication("CheckoutTest").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("CheckoutTest", _ => { });
            services.AddAuthorization();
            services.AddControllers().AddApplicationPart(typeof(SubscriptionsApiController).Assembly);
        }).Configure(application =>
        {
            application.UseRouting();
            application.UseAuthentication();
            application.UseAuthorization();
            application.UseEndpoints(endpoints => endpoints.MapControllers());
        })).Build();
        await host.StartAsync();

        return host;
    }

    private static Guid OrganizationId => Guid.Parse("3b29e23f-269d-4110-bc8d-75b775ac29ef");

    private static string Route(string command) => command switch
    {
        "personal-purchase" => "/api/v1/subscriptions/purchase",
        "personal-extend" => "/api/v1/subscriptions/extend",
        "workspace-purchase" => $"/api/v1/organizations/{CheckoutHttpTests.OrganizationId}/workspace/billing/plans/7/purchase",
        _ => $"/api/v1/organizations/{CheckoutHttpTests.OrganizationId}/workspace/billing/extend"
    };

    private static object Body(string command)
    {
        var buyer = new PaymentBuyerDto { TypeCode = "business", Name = "Компания", Inn = "1234567890" };

        return command.StartsWith("personal", StringComparison.Ordinal) ? new { PlanId = 7, Buyer = buyer } : buyer;
    }

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = this.Request.Headers["X-Test-User"].FirstOrDefault();
            if (string.IsNullOrEmpty(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], this.Scheme.Name));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, this.Scheme.Name)));
        }
    }
}
