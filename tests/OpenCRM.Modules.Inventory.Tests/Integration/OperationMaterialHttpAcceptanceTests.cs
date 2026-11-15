using OpenCRM.Modules.Maintenance.Contracts.Photos;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class OperationMaterialHttpAcceptanceTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task HttpIssueAndReturnPreserveIdempotencyBalancesAndAuthorization()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var foreignUser = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { OwnerUser = user, Name = "HTTP выдача материала", Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Исполнитель" };
        var detailId = Guid.NewGuid();
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            EmployeeId = employee.Id,
            Date = DateTime.UtcNow,
            Status = MaintenanceStatus.InProgress,
            Type = MaintenanceType.Maintenance,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Полировка" }]
        };
        var stock = new OrganizationMaterialStock
        {
            Organization = organization,
            Name = "Полировальная паста",
            Unit = "шт",
            QuantityOnHand = 5,
            AverageUnitCost = 100,
            InventoryValue = 500
        };
        context.AddRange(employee, maintenance, stock, new MaintenanceWorkOperation
        {
            Id = detailId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.InProgress
        });
        await context.SaveChangesAsync();

        using var server = this.CreateServer();
        using var anonymousClient = server.GetTestClient();
        await AddAntiforgeryTokenAsync(anonymousClient);
        var root = $"/api/v1/organizations/{organization.Id}/work/{maintenance.Id}/operations/{detailId}/materials";
        using var anonymousResponse = await anonymousClient.PostAsJsonAsync(root,
            new IssueOperationMaterialDto { StockId = stock.Id, Quantity = 2, Unit = "шт", CommandId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", user.Id);
        await AddAntiforgeryTokenAsync(client);
        var issueCommand = new IssueOperationMaterialDto
        {
            StockId = stock.Id,
            Quantity = 2,
            Unit = "шт",
            CommandId = Guid.NewGuid(),
            Comment = "Материал для операции"
        };
        using var issueResponse = await client.PostAsJsonAsync(root, issueCommand);
        Assert.Equal(HttpStatusCode.OK, issueResponse.StatusCode);
        using var issueJson = JsonDocument.Parse(await issueResponse.Content.ReadAsStringAsync());
        var issueId = issueJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(stock.Id, issueJson.RootElement.GetProperty("stockId").GetGuid());
        Assert.Equal("Полировальная паста", issueJson.RootElement.GetProperty("materialName").GetString());
        Assert.Equal(2, issueJson.RootElement.GetProperty("quantity").GetDecimal());
        Assert.Equal("шт", issueJson.RootElement.GetProperty("unit").GetString());
        Assert.False(issueJson.RootElement.GetProperty("isReturn").GetBoolean());
        Assert.False(issueJson.RootElement.TryGetProperty("Id", out _));

        using var repeatedIssueResponse = await client.PostAsJsonAsync(root, issueCommand);
        Assert.Equal(HttpStatusCode.OK, repeatedIssueResponse.StatusCode);
        var repeatedIssue = await repeatedIssueResponse.Content.ReadFromJsonAsync<OperationMaterialIssueDto>();
        Assert.NotNull(repeatedIssue);
        Assert.Equal(issueId, repeatedIssue.Id);

        using var foreignClient = server.GetTestClient();
        foreignClient.DefaultRequestHeaders.Add("X-Test-User", foreignUser.Id);
        await AddAntiforgeryTokenAsync(foreignClient);
        using var foreignResponse = await foreignClient.PostAsJsonAsync(root,
            new IssueOperationMaterialDto { StockId = stock.Id, Quantity = 1, Unit = "шт", CommandId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);

        using var externalContext = fixture.CreateContext();
        Assert.Equal(3, await externalContext.OrganizationMaterialStocks.AsNoTracking()
            .Where(item => item.Id == stock.Id).Select(item => item.QuantityOnHand).SingleAsync());

        var returnCommand = new ReverseOperationMaterialDto { CommandId = Guid.NewGuid(), Reason = "Остаток не понадобился" };
        var returnPath = $"{root}/{issueId}/return";
        using var returnResponse = await client.PostAsJsonAsync(returnPath, returnCommand);
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
        using var returnJson = JsonDocument.Parse(await returnResponse.Content.ReadAsStringAsync());
        var returnId = returnJson.RootElement.GetProperty("id").GetGuid();
        Assert.NotEqual(issueId, returnId);
        Assert.Equal(stock.Id, returnJson.RootElement.GetProperty("stockId").GetGuid());
        Assert.Equal(2, returnJson.RootElement.GetProperty("quantity").GetDecimal());
        Assert.True(returnJson.RootElement.GetProperty("isReturn").GetBoolean());
        Assert.Equal(issueId, returnJson.RootElement.GetProperty("sourceMovementId").GetGuid());

        using var repeatedReturnResponse = await client.PostAsJsonAsync(returnPath, returnCommand);
        Assert.Equal(HttpStatusCode.OK, repeatedReturnResponse.StatusCode);
        var repeatedReturn = await repeatedReturnResponse.Content.ReadFromJsonAsync<OperationMaterialIssueDto>();
        Assert.NotNull(repeatedReturn);
        Assert.Equal(returnId, repeatedReturn.Id);

        Assert.Equal(5, await externalContext.OrganizationMaterialStocks.AsNoTracking()
            .Where(item => item.Id == stock.Id).Select(item => item.QuantityOnHand).SingleAsync());
        Assert.Equal(2, await externalContext.OrganizationMaterialMovements
            .CountAsync(item => item.OrganizationId == organization.Id && item.OperationId == detailId));
    }

    private IHost CreateServer()
    {
        var builder = new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
            services.AddSingleton(Mock.Of<IOrganizationSubscriptionService>());
            services.AddSingleton(Mock.Of<IMediaAccessService>());
            services.AddSingleton(Mock.Of<IMaintenancePhotoService>());
            services.AddSingleton(Mock.Of<IFileStorageService>());
            services.AddSingleton(Mock.Of<IMaintenanceWorkQueryService>());
            services.AddSingleton(Mock.Of<IMaintenanceWorkExecutionService>());
            services.AddSingleton(Mock.Of<IMaintenanceWorkReportService>());
            services.AddSingleton(Mock.Of<IMaintenanceWorkTimeService>());
            services.AddScoped(_ => Mock.Of<ICatalogMaterialLookup>());
            services.AddOpenCRMInventory();
            services.AddAuthentication("InventoryTest")
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("InventoryTest", _ => { });
            services.AddAuthorization();
            services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
            services.AddControllersWithViews().AddApplicationPart(typeof(MaintenanceWorkApiController).Assembly);
        }).Configure(application =>
        {
            application.UseRouting();
            application.UseAuthentication();
            application.UseAuthorization();
            application.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapGet("/test-antiforgery", async context =>
                {
                    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                    var tokens = antiforgery.GetAndStoreTokens(context);
                    var options = context.RequestServices.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        tokens.RequestToken,
                        tokens.CookieToken,
                        CookieName = options.Cookie.Name,
                        HeaderName = options.HeaderName
                    });
                });
            });
        }));

        var host = builder.Build();
        host.Start();

        return host;
    }

    private static async Task AddAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/test-antiforgery");
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AntiforgeryTokens>();
        Assert.NotNull(tokens);
        Assert.NotNull(tokens.RequestToken);
        Assert.NotNull(tokens.CookieToken);
        client.DefaultRequestHeaders.Add(tokens.HeaderName, tokens.RequestToken);
        client.DefaultRequestHeaders.Add("Cookie", $"{tokens.CookieName}={tokens.CookieToken}");
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

    private sealed record AntiforgeryTokens(
        string RequestToken,
        string CookieToken,
        string CookieName,
        string HeaderName);
}
