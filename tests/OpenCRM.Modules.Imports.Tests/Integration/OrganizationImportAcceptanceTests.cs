using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Clients;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Workspace;
using OpenCRM.Modules.Workspace.Access;
using OpenCRM.Modules.Imports;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Imports;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Testing;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;

namespace OpenCRM.Modules.Imports.Tests.Integration;

public sealed class OrganizationImportAcceptanceTests(ImportDatabaseFixture fixture) : IClassFixture<ImportDatabaseFixture>
{
    [Fact]
    public async Task ImportFlowUsesRealHttpAuthorizationAndPersistsReviewedRows()
    {
        var seeded = await this.SeedAsync();
        using var host = await StartHostAsync(seeded);
        using var owner = await CreateAuthenticatedClientAsync(host.GetTestClient(), seeded.OwnerId);
        using var viewer = await CreateAuthenticatedClientAsync(host.GetTestClient(), seeded.ViewerId);
        using var foreign = await CreateAuthenticatedClientAsync(host.GetTestClient(), seeded.ForeignId);
        var accessUrl = AccessUrl(seeded.OrganizationId);

        Assert.Contains("\"canImport\":true", await (await owner.GetAsync(accessUrl)).Content.ReadAsStringAsync());
        Assert.Contains("\"canImport\":false", await (await viewer.GetAsync(accessUrl)).Content.ReadAsStringAsync());
        Assert.Contains("\"canImport\":false", await (await foreign.GetAsync(accessUrl)).Content.ReadAsStringAsync());
        Assert.False((await UploadAsync(viewer, seeded.OrganizationId)).IsSuccessStatusCode);
        Assert.False((await UploadAsync(foreign, seeded.OrganizationId)).IsSuccessStatusCode);

        using var upload = await UploadAsync(owner, seeded.OrganizationId);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var draft = (await upload.Content.ReadFromJsonAsync<ImportDraftDto>())!;
        Assert.Empty(draft.Data.Rows);
        draft = await PostImportAsync(
            owner,
            ImportUrl(seeded.OrganizationId, draft.Id) + "/mapping",
            new { revision = draft.Revision, mappings = draft.Data.Mappings });
        Assert.Equal(5, draft.Data.Rows.Count);
        var editedClient = Assert.Single(draft.Data.Rows, row => row.Kind == "client" && row.ExternalId == "c1");
        editedClient.Name = "Иван исправлен в форме";
        draft = await PutImportAsync(
            owner,
            ImportUrl(seeded.OrganizationId, draft.Id), new { revision = draft.Revision, rows = draft.Data.Rows });
        var reopened = await owner.GetFromJsonAsync<ImportDraftDto>(ImportUrl(seeded.OrganizationId, draft.Id));
        Assert.Equal("Иван исправлен в форме", Assert.Single(reopened!.Data.Rows, row => row.Kind == "client" && row.ExternalId == "c1").Name);

        var confirmed = await PostImportAsync(
            owner,
            ImportUrl(seeded.OrganizationId, draft.Id) + "/confirm", new { revision = reopened.Revision });
        var repeated = await PostImportAsync(
            owner,
            ImportUrl(seeded.OrganizationId, draft.Id) + "/confirm", new { revision = confirmed.Revision });

        Assert.Equal(2, confirmed.Result!.CreatedClients);
        Assert.Equal(3, confirmed.Result.CreatedVehicles);
        Assert.Equal(confirmed.Result.ClientIds, repeated.Result!.ClientIds);
        Assert.Equal(confirmed.Result.VehicleIds, repeated.Result.VehicleIds);
        await using var verification = fixture.CreateContext();
        var clients = await verification.OrganizationClients.Where(client => client.OrganizationId == seeded.OrganizationId).ToListAsync();
        var vehicles = await verification.Vehicles.Where(vehicle => vehicle.OrganizationId == seeded.OrganizationId).ToListAsync();
        Assert.Equal(2, clients.Count);
        Assert.Equal(3, vehicles.Count);
        Assert.Contains(clients, client => client.Name == "Иван исправлен в форме");
        Assert.All(vehicles, vehicle => Assert.Contains(clients, client => client.Id == vehicle.OrganizationClientId));
        Assert.Equal(2, vehicles.Select(vehicle => vehicle.OrganizationClientId).Distinct().Count());
    }

    [Fact]
    public async Task BatchEndpointsUseRealPermissionsAndBackgroundProcessing()
    {
        var seeded = await this.SeedAsync();
        using var host = await this.StartHostAsync(seeded);
        using var owner = await CreateAuthenticatedClientAsync(host.GetTestClient(), seeded.OwnerId);
        using var viewer = await CreateAuthenticatedClientAsync(host.GetTestClient(), seeded.ViewerId);
        using var upload = await UploadAsync(owner, seeded.OrganizationId, batch: true);
        upload.EnsureSuccessStatusCode();
        var draft = (await upload.Content.ReadFromJsonAsync<ImportDraftDto>())!;
        var url = ImportUrl(seeded.OrganizationId) + "/batches/" + draft.Id;
        Assert.Equal("upload-queued", draft.State);
        await ProcessPendingAsync(host);
        draft = (await owner.GetFromJsonAsync<ImportDraftDto>(url))!;
        await PostImportAsync(owner, url + "/mapping", new { draft.Revision, mappings = draft.Data.Mappings });
        await ProcessPendingAsync(host);
        draft = (await owner.GetFromJsonAsync<ImportDraftDto>(url + "?pageSize=2"))!;
        Assert.Equal(5, draft.TotalRows);
        Assert.Equal(2, draft.Data.Rows.Count);
        Assert.False((await viewer.GetAsync(url)).IsSuccessStatusCode);
        var changed = draft.Data.Rows.Single(row => row.Kind == "client");
        changed.Name = "Сохранённая правка пакетного импорта";
        await PutImportAsync(owner, url, new { draft.Revision, rows = new[] { changed } });
        await ProcessPendingAsync(host);
        draft = (await owner.GetFromJsonAsync<ImportDraftDto>(url))!;
        Assert.Contains(draft.Data.Rows, row => row.Name == changed.Name);
        await PostImportAsync(owner, url + "/confirm", new { draft.Revision });
        await ProcessPendingAsync(host);
        await ProcessPendingAsync(host);
        draft = (await owner.GetFromJsonAsync<ImportDraftDto>(url))!;

        Assert.Equal("completed", draft.State);
        Assert.Equal(2, draft.Result!.CreatedClients);
        Assert.Equal(3, draft.Result.CreatedVehicles);
        var repeated = await PostImportAsync(owner, url + "/confirm", new { draft.Revision });
        Assert.Equal(draft.ProcessedRows, repeated.ProcessedRows);
        Assert.Equal(draft.Result.VehicleIds, repeated.Result!.VehicleIds);
    }

    private static async Task ProcessPendingAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOrganizationImportBatchService>().ProcessPendingAsync(default);
    }

    private async Task<SeededOrganizations> SeedAsync()
    {
        await using var context = fixture.CreateContext();
        await new OrganizationBillingTestServices.DefaultsInitializer(context).EnsureDefaultsAsync();
        var owner = new ApplicationUser();
        var viewer = new ApplicationUser();
        var foreign = new ApplicationUser();
        var organization = CreateOrganization(owner, "Import acceptance");
        organization.Members.Add(new OrganizationMember { User = viewer, RoleCode = OrganizationRoleCodes.Viewer });
        var plan = await CreatePlanAsync(context);
        context.Users.AddRange(owner, viewer, foreign);
        context.Organizations.Add(organization);
        context.OrganizationSubscriptions.Add(CreateSubscription(organization, plan));
        await context.SaveChangesAsync();

        return new SeededOrganizations(owner.Id, viewer.Id, foreign.Id, organization.Id);
    }

    private static async Task<SubscriptionPlan> CreatePlanAsync(OpenCRMContext context)
    {
        var featureCodes = new[] { Helpers.FeatureCodes.ClientsModule, Helpers.FeatureCodes.VehiclesModule,
            Helpers.FeatureCodes.WorkspaceVehiclesLimit, Helpers.FeatureCodes.MaintenanceModule, Helpers.FeatureCodes.FinanceModule };
        var definitions = await context.FeatureDefinitions.Where(feature => featureCodes.Contains(feature.Code)).ToListAsync();
        var plan = new SubscriptionPlan
        {
            Name = "Import paid plan",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            DurationDays = 30,
            VehicleLimit = 100,
            Features = definitions.Select(feature => new SubscriptionPlanFeature
            {
                FeatureDefinition = feature,
                BoolValue = feature.Code != Helpers.FeatureCodes.WorkspaceVehiclesLimit,
                IntValue = feature.Code == Helpers.FeatureCodes.WorkspaceVehiclesLimit ? 100 : null
            }).ToList()
        };
        context.SubscriptionPlans.Add(plan);

        return plan;
    }

    private static Organization CreateOrganization(ApplicationUser owner, string name) => new()
    {
        Name = name,
        OwnerUser = owner,
        PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation,
        Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
    };

    private static OrganizationSubscription CreateSubscription(Organization organization, SubscriptionPlan plan) => new()
    {
        Organization = organization,
        SubscriptionPlan = plan,
        StartDate = DateTime.UtcNow.AddDays(-1),
        EndDate = DateTime.UtcNow.AddDays(30),
        SourceCode = "acceptance"
    };

    private async Task<IHost> StartHostAsync(SeededOrganizations seeded)
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddScoped(_ => fixture.CreateContext());
                services.AddScoped<IFeatureDefaultsInitializer>(provider => new OrganizationBillingTestServices.DefaultsInitializer(
                    provider.GetRequiredService<OpenCRMContext>()));
                services.AddSingleton(Mock.Of<IOrganizationAuditService>());
                services.AddScoped<IOrganizationPermissionService>(provider => new OrganizationPermissionService(
                    provider.GetRequiredService<OpenCRMContext>(), provider.GetRequiredService<IOrganizationAuditService>()));
                services.AddOpenCRMBilling();
                services.AddSingleton<IPlatformCommercialPolicy>(
                    Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));
                services.AddOpenCRMWorkspace();
                services.AddOpenCRMImports();
                services.AddScoped<OpenCRM.Modules.Maintenance.Contracts.Imports.IImportBusinessRecordProcessor,
                    OpenCRM.Modules.Maintenance.Imports.ImportBusinessRecordProcessor>();
                services.AddOpenCRMRepositories();
                services.AddOpenCRMVehicles();
                services.AddOpenCRMClients();
                services.AddSingleton(Mock.Of<IPaymentCreationGateway>());
                services.AddSingleton(Mock.Of<IPromoCodeService>());
                services.AddSingleton(Mock.Of<IPromotionPaymentCoordinator>());
                services.AddAuthentication("ImportAcceptance")
                    .AddScheme<ImportAcceptanceAuthOptions, ImportAcceptanceAuthenticationHandler>("ImportAcceptance", options =>
                    {
                        options.OwnerId = seeded.OwnerId;
                        options.ViewerId = seeded.ViewerId;
                        options.ForeignId = seeded.ForeignId;
                    });
                services.AddAuthorization();
                services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
                services.AddControllersWithViews().AddApplicationPart(typeof(OrganizationImportsApiController).Assembly);
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints.MapGet("/test/session", async context =>
                    {
                        var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
                        await context.Response.WriteAsJsonAsync(new
                        {
                            userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                            organizationId = seeded.OrganizationId,
                            token = tokens.RequestToken
                        });
                    });
                });
            });
        });

        return await builder.StartAsync();
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(HttpClient client, string userId)
    {
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        var response = await client.GetAsync("/test/session");
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<AntiforgerySession>();
        client.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", session!.Token);

        return client;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid organizationId, bool batch = false)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(
            "Код клиента;Клиент;Телефон;Госномер;Марка;Модель;VIN;Код владельца;Год\r\n" +
            "c1;Иван;79990000101;А001АА;Toyota;Camry;JT123456789012345;c1;2020\r\n" +
            "c1;Иван;79990000101;А002АА;Toyota;RAV4;JT123456789012346;c1;2021\r\n" +
            "c2;Анна;79990000102;В003ВВ;Kia;Rio;KN123456789012345;c2;2019")).ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "combined.csv");

        return await client.PostAsync(ImportUrl(organizationId) + (batch ? "/batches" : string.Empty), content);
    }

    private static async Task<ImportDraftDto> PostImportAsync(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ImportDraftDto>())!;
    }

    private static async Task<ImportDraftDto> PutImportAsync(HttpClient client, string url, object request)
    {
        var response = await client.PutAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ImportDraftDto>())!;
    }

    private static string AccessUrl(Guid organizationId) => $"/api/v1/organizations/{organizationId}/imports/access";
    private static string ImportUrl(Guid organizationId, Guid? draftId = null) =>
        $"/api/v1/organizations/{organizationId}/imports{(draftId is null ? string.Empty : $"/{draftId}")}";

    private sealed record SeededOrganizations(string OwnerId, string ViewerId, string ForeignId, Guid OrganizationId);
    private sealed record AntiforgerySession(string UserId, Guid OrganizationId, string Token);

    private sealed class ImportAcceptanceAuthOptions : AuthenticationSchemeOptions
    {
        public string OwnerId { get; set; } = string.Empty;
        public string ViewerId { get; set; } = string.Empty;
        public string ForeignId { get; set; } = string.Empty;
    }

    private sealed class ImportAcceptanceAuthenticationHandler(
        IOptionsMonitor<ImportAcceptanceAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<ImportAcceptanceAuthOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers["X-Test-User"].ToString();
            if (userId != Options.OwnerId && userId != Options.ViewerId && userId != Options.ForeignId)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
