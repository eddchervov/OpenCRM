using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
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
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;
using ISystemClock = OpenCRM.Domain.Interfaces.ISystemClock;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class InventoryHttpAcceptanceTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task HttpStockCommandsAndQueriesUseTheModuleAndPreserveBalancesAndJson()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var organization = new Organization { OwnerUser = owner, Name = "HTTP склад" };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        using var server = this.CreateServer(owner, organization);
        using var client = server.GetTestClient();
        var path = $"/api/v1/organizations/{organization.Id}/materials";
        using var anonymous = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", owner.Id);

        using var invalidReceipt = await client.PostAsJsonAsync(path + "/receipt",
            new CreateOrganizationMaterialReceiptDto { Name = new string('Я', 161), Unit = "шт", Quantity = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidReceipt.StatusCode);
        using var invalidReceiptJson = JsonDocument.Parse(await invalidReceipt.Content.ReadAsStringAsync());
        Assert.Equal("Название материала не должно превышать 160 символов.", invalidReceiptJson.RootElement.GetProperty("message").GetString());

        using var receipt = await client.PostAsJsonAsync(path + "/receipt",
            new CreateOrganizationMaterialReceiptDto { Name = "Паста", Unit = "шт", Quantity = 5, UnitCost = 10 });
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        var stock = await receipt.Content.ReadFromJsonAsync<OrganizationMaterialStockDto>();
        Assert.NotNull(stock);
        Assert.Equal(5, stock.QuantityOnHand);
        Assert.Equal(50, stock.InventoryValue);

        using var issue = await client.PostAsJsonAsync(path + "/issue",
            new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = 2 });
        Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
        using var issueJson = JsonDocument.Parse(await issue.Content.ReadAsStringAsync());
        Assert.Equal(3, issueJson.RootElement.GetProperty("quantityOnHand").GetDecimal());
        Assert.Equal(30, issueJson.RootElement.GetProperty("inventoryValue").GetDecimal());
        Assert.False(issueJson.RootElement.TryGetProperty("QuantityOnHand", out _));
        var updatedAt = issueJson.RootElement.GetProperty("updatedAt").GetDateTime();

        using var rejected = await client.PostAsJsonAsync(path + "/issue",
            new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = 4 });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var rejectedJson = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Contains("Нельзя списать больше доступного остатка", rejectedJson.RootElement.GetProperty("message").GetString());
        using var adjustment = await client.PutAsJsonAsync(path + $"/{stock.Id}",
            new AdjustOrganizationMaterialStockDto
            {
                QuantityOnHand = 4, MinQuantity = 5, ExpectedUpdatedAt = updatedAt, Comment = "Пересчёт"
            });
        Assert.Equal(HttpStatusCode.OK, adjustment.StatusCode);
        var adjustedStock = await adjustment.Content.ReadFromJsonAsync<OrganizationMaterialStockDto>();
        Assert.NotNull(adjustedStock);
        using var snapshot = await client.GetAsync(path + "?stockFilter=low");
        Assert.Equal(HttpStatusCode.OK, snapshot.StatusCode);
        using var snapshotJson = JsonDocument.Parse(await snapshot.Content.ReadAsStringAsync());
        var stocks = snapshotJson.RootElement.GetProperty("stocks");
        Assert.Equal(1, stocks.GetArrayLength());
        Assert.Equal(4, stocks[0].GetProperty("quantityOnHand").GetDecimal());
        Assert.Equal(40, stocks[0].GetProperty("inventoryValue").GetDecimal());
        Assert.Equal(3, snapshotJson.RootElement.GetProperty("recentMovements").GetArrayLength());

        using var preciseAdjustment = await client.PutAsJsonAsync(path + $"/{stock.Id}",
            new
            {
                quantityOnHand = 4, expectedUpdatedAt = adjustedStock.UpdatedAt,
                averageUnitCost = "999999998.12345678", comment = new string('Я', 800)
            });
        Assert.Equal(HttpStatusCode.OK, preciseAdjustment.StatusCode);
        using var preciseJson = JsonDocument.Parse(await preciseAdjustment.Content.ReadAsStringAsync());
        Assert.Equal(999999998.12345678m, preciseJson.RootElement.GetProperty("averageUnitCost").GetDecimal());
        var preciselyAdjustedAt = preciseJson.RootElement.GetProperty("updatedAt").GetDateTime();

        using var unchangedCost = await client.PutAsJsonAsync(path + $"/{stock.Id}",
            new AdjustOrganizationMaterialStockDto
            {
                QuantityOnHand = 3, ExpectedUpdatedAt = preciselyAdjustedAt,
                AverageUnitCost = null, Comment = "Пересчёт без изменения цены"
            });
        Assert.Equal(HttpStatusCode.OK, unchangedCost.StatusCode);
        using var unchangedJson = JsonDocument.Parse(await unchangedCost.Content.ReadAsStringAsync());
        Assert.Equal(999999998.12345678m, unchangedJson.RootElement.GetProperty("averageUnitCost").GetDecimal());

        using var foreign = await client.GetAsync($"/api/v1/organizations/{Guid.NewGuid()}/materials");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    private IHost CreateServer(ApplicationUser owner, Organization organization)
    {
        var manager = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(),
            Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(), Mock.Of<ILogger<UserManager<ApplicationUser>>>());
        manager.Setup(item => item.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(owner);
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(item => item.GetOverviewAsync(owner.Id)).ReturnsAsync(new TeamOverviewDto
        {
            Organizations = [new OrganizationDto { Id = organization.Id, Name = organization.Name }]
        });
        var builder = new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
            services.AddSingleton(manager.Object);
            services.AddSingleton(organizations.Object);
            services.AddSingleton(Mock.Of<IOrganizationProfileCommandService>());
            services.AddSingleton(Mock.Of<IOrganizationArchiveService>());
            services.AddSingleton(Mock.Of<IOrganizationMembershipService>());
            services.AddSingleton(Mock.Of<IOrganizationInvitationCommandService>());
            services.AddSingleton(Mock.Of<IOrganizationSubscriptionService>());
            services.AddSingleton(Mock.Of<IOrganizationSubscriptionCheckoutService>());
            services.AddSingleton(Mock.Of<IOrganizationPermissionService>());
            services.AddSingleton(Mock.Of<IWorkspaceOverviewService>());
            services.AddSingleton(Mock.Of<IOrganizationWorkspaceShellService>());
            services.AddSingleton(Mock.Of<IOrganizationWorkspaceQueryService>());
            services.AddSingleton(Mock.Of<IVehicleOptionQueryService>());
            services.AddSingleton(Mock.Of<ISchedulerQueryService>());
            services.AddSingleton(Mock.Of<ISchedulerTaskCommandService>());
            services.AddSingleton(Mock.Of<ISchedulerRecurrenceService>());
            services.AddSingleton(Mock.Of<ICatalogQueryService>());
                    services.AddSingleton(Mock.Of<ICatalogCommandService>());
                    services.AddSingleton(Mock.Of<ICatalogInitializationService>());
            services.AddSingleton(Mock.Of<ISystemClock>());
            services.AddScoped(_ => Mock.Of<ICatalogMaterialLookup>());
            // Справка организации вынесена из платформы: контроллер рабочего пространства получает статьи справки
            // через этот порт, в приложении его регистрирует слой представления (пустая реализация без статей).
            services.AddSingleton(Mock.Of<OpenCRM.WebApp.Models.Organizations.IOrganizationWikiContentProvider>());
            services.AddOpenCRMInventory();
            services.AddAuthentication("InventoryTest")
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("InventoryTest", _ => { });
            services.AddAuthorization();
            services.AddControllers().AddApplicationPart(typeof(OrganizationWorkspaceApiController).Assembly);
        }).Configure(application =>
        {
            application.UseRouting();
            application.UseAuthentication();
            application.UseAuthorization();
            application.UseEndpoints(endpoints => endpoints.MapControllers());
        }));

        var host = builder.Build();
        host.Start();

        return host;
    }

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
