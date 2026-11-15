using Microsoft.Extensions.Configuration;
using OpenCRM.Modules.Payments.Contracts;
using System.Net;
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
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.WebApp.Controllers.Api.V1;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class PersonalSubscriptionHttpTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Fact]
    public async Task PersonalReadRoutesUseActualBillingWithoutCallingPaymentFacade()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "Личный HTTP");
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        context.UserSubscriptions.Add(new UserSubscription { UserId = user.Id, SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(2) });
        context.UserSubscriptionHistory.Add(new UserSubscriptionHistory { UserId = user.Id, SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddDays(-31), EndDate = DateTime.UtcNow.AddDays(-1), CreatedAt = DateTime.UtcNow, Status = "created" });
        await context.SaveChangesAsync();
        var commands = new Mock<ISubscriptionService>(MockBehavior.Strict);
        using var host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformPolicy:Commercial:Enabled"] = "true"
            }))
            .ConfigureServices(services =>
            {
                services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
                services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<OpenCRMContext>();
                services.AddSingleton(fixture.CreateDefaultsInitializer().Object);
                services.AddSingleton(Mock.Of<IPaymentCreationGateway>());
                services.AddOpenCRMBilling();
                services.AddSingleton(Mock.Of<IPromoCodeService>());
                services.AddSingleton(commands.Object);
                services.AddAuthentication("BillingTest").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("BillingTest", _ => { });
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
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/subscriptions/me")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", user.Id);
        foreach (var route in new[] { "me", "plans", "overview", "history" })
        {
            using var response = await client.GetAsync($"/api/v1/subscriptions/{route}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (route == "me")
            {
                Assert.True(body.RootElement.GetProperty("hasActiveSubscription").GetBoolean());
                Assert.Equal(plan.Id, body.RootElement.GetProperty("subscription").GetProperty("subscriptionPlanId").GetInt32());
            }
            else if (route is "plans" or "history")
            {
                Assert.Equal(1, body.RootElement.GetArrayLength());
            }
        }

        commands.VerifyNoOtherCalls();
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
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId), new Claim("UserID", userId)], this.Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
