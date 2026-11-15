using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Auth;
using OpenCRM.Services.Interfaces.Auth.Dtos;
using OpenCRM.WebApp.Security;
using OpenCRM.WebApp.Controllers.Admin.Api;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class AdminBillingReadHttpTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Fact]
    public async Task ReadRoutesKeepJsonAndDatabasePermissionsWithoutCallingPaymentOrFileAdapters()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var role = new IdentityRole("BillingReadTest");
        context.Roles.Add(role);
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = organization.OwnerUserId, RoleId = role.Id });
        context.AdminRolePermissions.Add(new AdminRolePermission
        {
            RoleId = role.Id,
            PermissionCode = AdminAuthorizationPolicies.ViewBilling
        });
        await context.SaveChangesAsync();
        var paymentProvider = new Mock<IYooKassaService>(MockBehavior.Strict);
        var files = new Mock<IFileStorageService>(MockBehavior.Strict);
        var auth = new Mock<IAuthTokenService>(MockBehavior.Strict);
        auth.Setup(service => service.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new ApiUserResponse { Id = organization.OwnerUserId });
        using var host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformPolicy:Commercial:Enabled"] = "true"
            }))
            .ConfigureServices(services =>
            {
                services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
                services.AddOpenCRMBilling();
                services.AddOpenCRMAdminModule();
                services.AddSingleton(paymentProvider.Object);
                services.AddSingleton(files.Object);
                services.AddSingleton(auth.Object);
                services.AddSingleton(Mock.Of<INpdReceiptDeliveryService>(MockBehavior.Strict));
                services.AddAuthentication("Bearer")
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Bearer", _ => { })
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(IdentityConstants.ApplicationScheme, _ => { });
                services.AddControllers().AddApplicationPart(typeof(BillingApiController).Assembly);
            }).Configure(application =>
            {
                application.UseRouting();
                application.UseAuthentication();
                application.UseAuthorization();
                application.UseEndpoints(endpoints => endpoints.MapControllers());
            })).Build();
        await host.StartAsync();
        using var client = host.GetTestClient();
        const string RootRoute = "/api/v1/admin/billing";
        using var anonymous = await client.GetAsync(RootRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);

        foreach (var route in new[] { "", "/attention", "/purchases", "/payments/999/status-audits", "/subscriptions/users/999/audits" })
        {
            using var response = await client.GetAsync(RootRoute + route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (route.Length == 0)
            {
                Assert.Equal(0, json.RootElement.GetProperty("kpis").GetProperty("totalPayments").GetInt32());
                Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("payments").ValueKind);
            }
        }

        using var paymentsPage = await client.GetAsync(RootRoute + "/payments?status=all&periodDays=0&page=1&pageSize=5&sort=date&descending=true");
        Assert.Equal(HttpStatusCode.OK, paymentsPage.StatusCode);
        using var paymentsPageJson = JsonDocument.Parse(await paymentsPage.Content.ReadAsStringAsync());
        Assert.Equal(1, paymentsPageJson.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(5, paymentsPageJson.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(JsonValueKind.Array, paymentsPageJson.RootElement.GetProperty("items").ValueKind);

        using var organizationSubscriptionsPage = await client.GetAsync(
            RootRoute + "/subscriptions/organizations?status=active&page=1&pageSize=5&sort=organization");
        Assert.Equal(HttpStatusCode.OK, organizationSubscriptionsPage.StatusCode);
        using var organizationSubscriptionsPageJson = JsonDocument.Parse(
            await organizationSubscriptionsPage.Content.ReadAsStringAsync());
        Assert.Equal(1, organizationSubscriptionsPageJson.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(JsonValueKind.Array, organizationSubscriptionsPageJson.RootElement.GetProperty("items").ValueKind);

        using var attentionWithoutPeriod = await client.GetAsync(RootRoute + "/attention");
        using var allTimeJson = JsonDocument.Parse(await attentionWithoutPeriod.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, allTimeJson.RootElement.GetProperty("periodDays").ValueKind);

        using var attentionForSevenDays = await client.GetAsync(RootRoute + "/attention?periodDays=7");
        using var sevenDaysJson = JsonDocument.Parse(await attentionForSevenDays.Content.ReadAsStringAsync());
        Assert.Equal(7, sevenDaysJson.RootElement.GetProperty("periodDays").GetInt32());

        using var attentionForInvalidPeriod = await client.GetAsync(RootRoute + "/attention?periodDays=14");
        using var invalidPeriodJson = JsonDocument.Parse(await attentionForInvalidPeriod.Content.ReadAsStringAsync());
        Assert.Equal(1, invalidPeriodJson.RootElement.GetProperty("periodDays").GetInt32());

        using var missingPayment = await client.GetAsync(RootRoute + "/payments/999");
        Assert.Equal(HttpStatusCode.NotFound, missingPayment.StatusCode);
        using var deniedPlans = await client.GetAsync(RootRoute + "/available-plans");
        Assert.Equal(HttpStatusCode.Forbidden, deniedPlans.StatusCode);
        using var deniedSync = await client.PostAsync(RootRoute + "/payments/999/sync-yookassa", null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedSync.StatusCode);
        using var deniedReceipt = await client.PutAsJsonAsync(RootRoute + "/payments/999/npd-receipt/details", new { comment = "Чек" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedReceipt.StatusCode);
        using var deniedGrant = await client.PostAsJsonAsync(RootRoute + $"/organizations/{organization.Id}/plan",
            new { planId = 999, days = 30, reason = "Выдать" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedGrant.StatusCode);
        context.AdminRolePermissions.Add(new AdminRolePermission
        {
            RoleId = role.Id,
            PermissionCode = AdminAuthorizationPolicies.ManageBilling
        });
        await context.SaveChangesAsync();
        using var allowedPlans = await client.GetAsync(RootRoute + "/available-plans");
        Assert.Equal(HttpStatusCode.OK, allowedPlans.StatusCode);
        using var missingSync = await client.PostAsync(RootRoute + "/payments/999/sync-yookassa", null);
        Assert.Equal(HttpStatusCode.NotFound, missingSync.StatusCode);
        using var missingReceipt = await client.PutAsJsonAsync(RootRoute + "/payments/999/npd-receipt/details", new { comment = "Чек" });
        Assert.Equal(HttpStatusCode.NotFound, missingReceipt.StatusCode);
        var plan = await fixture.CreatePlanAsync(context, "Административный тариф");
        using var grant = await client.PostAsJsonAsync(RootRoute + $"/organizations/{organization.Id}/plan",
            new { planId = plan.Id, days = 30, reason = "HTTP выдача" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        using var grantedJson = JsonDocument.Parse(await grant.Content.ReadAsStringAsync());
        Assert.True(grantedJson.RootElement.TryGetProperty("endDate", out _));
        var payment = new SubscriptionPayment
        {
            UserId = organization.OwnerUserId, SubscriptionPlanId = plan.Id, Amount = 100,
            CreatedAt = DateTime.UtcNow, PaymentStatus = "pending", PaymentMethod = "manual"
        };
        context.SubscriptionPayments.Add(payment);
        await context.SaveChangesAsync();
        var correction = new { status = "failed", expectedCurrentStatus = "pending", reason = "HTTP исправление" };
        using var deniedCorrection = await client.PostAsJsonAsync(RootRoute + $"/payments/{payment.Id}/mark-status", correction);
        Assert.Equal(HttpStatusCode.Forbidden, deniedCorrection.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Role", AdminRoleNames.System);
        using var corrected = await client.PostAsJsonAsync(RootRoute + $"/payments/{payment.Id}/mark-status", correction);
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        using var correctedJson = JsonDocument.Parse(await corrected.Content.ReadAsStringAsync());
        Assert.Equal("failed", correctedJson.RootElement.GetProperty("status").GetString());
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        context.AdminRolePermissions.RemoveRange(context.AdminRolePermissions.Where(permission => permission.RoleId == role.Id));
        await context.SaveChangesAsync();
        using var revokedRead = await client.GetAsync(RootRoute);
        Assert.Equal(HttpStatusCode.Forbidden, revokedRead.StatusCode);
        paymentProvider.VerifyNoOtherCalls();
        files.VerifyNoOtherCalls();
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

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], this.Scheme.Name);
            var role = this.Request.Headers["X-Test-Role"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
