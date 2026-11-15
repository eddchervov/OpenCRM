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
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Auth;
using OpenCRM.Services.Interfaces.Auth.Dtos;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.WebApp.Controllers.Admin.Api;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class AdminPaymentServiceActionsHttpTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Fact]
    public async Task ServiceActionsRequireManagePermissionAndUseAuthenticatedActor()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var role = new IdentityRole("PaymentActionsTest");
        context.Roles.Add(role);
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = organization.OwnerUserId, RoleId = role.Id });
        context.AdminRolePermissions.Add(new AdminRolePermission
        {
            RoleId = role.Id,
            PermissionCode = AdminAuthorizationPolicies.ViewBilling
        });
        await context.SaveChangesAsync();
        var operationId = Guid.NewGuid();
        var token = new string('A', 64);
        var state = new AdminPaymentServiceActions(token, true, false, false, false, 0,
            "Проверить", "Оплата не подтверждена", "Доступ не выдан", "Разбор не нужен", false, []);
        var operation = new AdminPaymentServiceAction(operationId, "check", "queued", "accepted",
            "Проверка запрошена", null, DateTime.UtcNow, null, organization.OwnerUserId, "Проверка по обращению");
        var actions = new Mock<IAdminPaymentServiceActions>(MockBehavior.Strict);
        actions.Setup(service => service.GetAsync(22, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        actions.Setup(service => service.ExecuteAsync(22,
                It.Is<AdminPaymentServiceActionRequest>(request => request.OperationId == operationId && request.StateToken == token),
                organization.OwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminPaymentServiceActionResult(operation, 202));
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
                services.AddSingleton(Mock.Of<IYooKassaService>(MockBehavior.Strict));
                services.AddSingleton(Mock.Of<IFileStorageService>(MockBehavior.Strict));
                services.AddSingleton(Mock.Of<INpdReceiptDeliveryService>(MockBehavior.Strict));
                services.AddSingleton(auth.Object);
                services.AddSingleton(actions.Object);
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
        const string Route = "/api/v1/admin/billing/payments/22/service-actions";
        var requestBody = new
        {
            operationId,
            action = "check",
            reason = "Проверка по обращению",
            stateToken = token,
            actorUserId = "spoofed-user"
        };
        using var anonymousRead = await client.GetAsync(Route);
        using var anonymousWrite = await client.PostAsJsonAsync(Route, requestBody);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousWrite.StatusCode);
        actions.VerifyNoOtherCalls();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var read = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(token, readJson.RootElement.GetProperty("stateToken").GetString());
        using var deniedWrite = await client.PostAsJsonAsync(Route, requestBody);
        Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
        actions.Verify(service => service.ExecuteAsync(It.IsAny<int>(), It.IsAny<AdminPaymentServiceActionRequest>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        context.AdminRolePermissions.Add(new AdminRolePermission
        {
            RoleId = role.Id,
            PermissionCode = AdminAuthorizationPolicies.ManageBilling
        });
        await context.SaveChangesAsync();
        using var accepted = await client.PostAsJsonAsync(Route, requestBody);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var acceptedJson = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        Assert.Equal(operationId, acceptedJson.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("queued", acceptedJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(organization.OwnerUserId, acceptedJson.RootElement.GetProperty("actor").GetString());
        actions.Verify(service => service.ExecuteAsync(22, It.IsAny<AdminPaymentServiceActionRequest>(),
            organization.OwnerUserId, It.IsAny<CancellationToken>()), Times.Once);
        actions.Setup(service => service.ExecuteAsync(22, It.IsAny<AdminPaymentServiceActionRequest>(),
                organization.OwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminPaymentServiceActionResult(null, 409, "Идентификатор уже использован", "operation_id_conflict"));
        using var conflict = await client.PostAsJsonAsync(Route, requestBody);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var conflictJson = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal("operation_id_conflict", conflictJson.RootElement.GetProperty("code").GetString());
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
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
