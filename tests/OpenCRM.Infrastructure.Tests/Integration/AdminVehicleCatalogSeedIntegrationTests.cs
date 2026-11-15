using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Testing;
using OpenCRM.WebApp.Controllers.Admin.Api;
using OpenCRM.WebApp.Infrastructure.Security;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class AdminVehicleCatalogSeedIntegrationTests(NotificationFixture fixture)
    : IClassFixture<NotificationFixture>
{
    private const string SeedResourceName = "OpenCRM.Infrastructure.Admin.VehicleCatalog.vehicle-catalog-seed.json";
    private const string SeedResourceHash = "edfd91ae2716fc315f44e11cde8d2f2b6b88945aae63255c92d7a3cfcee73e67";

    [Fact]
    public async Task BrandLogoCanBeAssignedPreservedAndClearedThroughCatalogService()
    {
        await using var context = fixture.CreateContext();
        var service = new AdminVehicleCatalogService(context);
        var name = Guid.NewGuid().ToString();
        await service.CreateBrandAsync(new AdminSaveBrandDto
        {
            Name = name, Kind = VehicleKind.PassengerCar, LogoKey = "toyota"
        });
        var brand = await context.Brands.SingleAsync(item => item.Name == name);
        Assert.Equal("toyota", brand.LogoKey);
        Assert.Equal("toyota", Assert.Single(await service.GetBrandsAsync(VehicleKind.PassengerCar, name)).LogoKey);

        await service.UpdateBrandAsync(brand.Id, new AdminSaveBrandDto
        {
            Name = name, Kind = VehicleKind.PassengerCar
        });
        Assert.Equal("toyota", brand.LogoKey);

        await service.UpdateBrandAsync(brand.Id, new AdminSaveBrandDto
        {
            Name = name, Kind = VehicleKind.PassengerCar, LogoKey = new string('a', 65)
        });
        Assert.Equal("toyota", brand.LogoKey);

        await service.UpdateBrandAsync(brand.Id, new AdminSaveBrandDto
        {
            Name = name, Kind = VehicleKind.PassengerCar, LogoKey = ""
        });
        await context.Entry(brand).ReloadAsync();
        Assert.Null(brand.LogoKey);
    }

    [Fact]
    public async Task EmbeddedCatalogPayloadHasExpectedHashAndSeedIsIdempotent()
    {
        await AdminVehicleCatalogSeedIntegrationTests.ResetCatalogAsync(fixture);
        AdminVehicleCatalogSeedIntegrationTests.AssertSeedResource();

        await using var context = fixture.CreateContext();
        var service = new AdminVehicleCatalogSeedService(context);

        var firstResult = await service.SeedAsync();

        Assert.Equal(157, firstResult.CreatedBrands);
        Assert.Equal(947, firstResult.CreatedModels);
        Assert.Equal(0, firstResult.SkippedDeletedBrands);
        Assert.Equal(0, firstResult.SkippedDeletedModels);
        Assert.Equal(157, await context.Brands.IgnoreQueryFilters().CountAsync());
        Assert.Equal(947, await context.Models.IgnoreQueryFilters().CountAsync());

        Assert.Equal("volvo", (await context.Brands.SingleAsync(brand =>
            brand.Kind == VehicleKind.Truck && brand.Name == "Volvo")).LogoKey);
        Assert.Null((await context.Brands.SingleAsync(brand =>
            brand.Kind == VehicleKind.Truck && brand.Name == "Scania")).LogoKey);

        var repeatedResult = await service.SeedAsync();

        Assert.Equal(0, repeatedResult.CreatedBrands);
        Assert.Equal(0, repeatedResult.CreatedModels);
        Assert.Equal(0, repeatedResult.SkippedDeletedBrands);
        Assert.Equal(0, repeatedResult.SkippedDeletedModels);
        Assert.Equal(157, await context.Brands.IgnoreQueryFilters().CountAsync());
        Assert.Equal(947, await context.Models.IgnoreQueryFilters().CountAsync());

        var deletedBrand = await context.Brands.SingleAsync(brand => brand.Kind == VehicleKind.Truck && brand.Name == "Volvo");
        var deletedBrandModelCount = await context.Models.IgnoreQueryFilters().CountAsync(model => model.BrandId == deletedBrand.Id);
        deletedBrand.IsDeleted = true;
        var activeBrand = await context.Brands.SingleAsync(brand => brand.Kind == VehicleKind.Truck && brand.Name == "Scania");
        var deletedModel = await context.Models.SingleAsync(model =>
            model.BrandId == activeBrand.Id && model.Kind == VehicleKind.Truck && model.Name == "P-Series");
        deletedModel.IsDeleted = true;
        await context.SaveChangesAsync();

        var deletedEntriesResult = await service.SeedAsync();

        Assert.Equal(0, deletedEntriesResult.CreatedBrands);
        Assert.Equal(0, deletedEntriesResult.CreatedModels);
        Assert.Equal(1, deletedEntriesResult.SkippedDeletedBrands);
        Assert.Equal(deletedBrandModelCount + 1, deletedEntriesResult.SkippedDeletedModels);
        Assert.True((await context.Brands.IgnoreQueryFilters().SingleAsync(brand => brand.Id == deletedBrand.Id)).IsDeleted);
        Assert.True((await context.Models.IgnoreQueryFilters().SingleAsync(model => model.Id == deletedModel.Id)).IsDeleted);
    }

    [Fact]
    public async Task SeedEndpointRequiresManageContentAndRunsThroughHttpPipeline()
    {
        await AdminVehicleCatalogSeedIntegrationTests.ResetCatalogAsync(fixture);
        await using var context = fixture.CreateContext();
        var manageRole = AdminVehicleCatalogSeedIntegrationTests.CreateRole("CatalogSeedManage");
        var emptyRole = AdminVehicleCatalogSeedIntegrationTests.CreateRole("CatalogSeedEmpty");
        var authorizedUser = AdminVehicleCatalogSeedIntegrationTests.CreateUser("catalog-seed-admin");
        var deniedUser = AdminVehicleCatalogSeedIntegrationTests.CreateUser("catalog-seed-denied");
        context.Roles.AddRange(manageRole, emptyRole);
        context.Users.AddRange(authorizedUser, deniedUser);
        context.UserRoles.AddRange(
            AdminVehicleCatalogSeedIntegrationTests.CreateUserRole(authorizedUser, manageRole),
            AdminVehicleCatalogSeedIntegrationTests.CreateUserRole(deniedUser, emptyRole));
        context.AdminRolePermissions.Add(new AdminRolePermission
        {
            RoleId = manageRole.Id,
            PermissionCode = AdminAuthorizationPolicies.ManageContent
        });
        await context.SaveChangesAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped(_ => fixture.CreateContext());
        builder.Services.AddOpenCRMAdminModule();
        builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                IdentityConstants.ApplicationScheme,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Bearer", _ => { });
        builder.Services.AddControllers(options => options.Filters.Add<ApiAntiforgeryAuthorizationFilter>())
            .AddApplicationPart(typeof(VehicleCatalogAdminApiController).Assembly);

        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var anonymousResponse = await client.PostAsync("/api/v1/admin/catalog/seed", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var (deniedToken, deniedCookie) = await AdminVehicleCatalogSeedIntegrationTests.GetAntiforgeryTokenAsync(client, deniedUser.Id);
        using var deniedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/catalog/seed");
        deniedRequest.Headers.Add("X-Test-User", deniedUser.Id);
        deniedRequest.Headers.Add("RequestVerificationToken", deniedToken);
        deniedRequest.Headers.Add("Cookie", deniedCookie);
        using var deniedResponse = await client.SendAsync(deniedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var missingTokenRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/catalog/seed");
        missingTokenRequest.Headers.Add("X-Test-User", authorizedUser.Id);
        using var missingTokenResponse = await client.SendAsync(missingTokenRequest);
        Assert.Equal(HttpStatusCode.BadRequest, missingTokenResponse.StatusCode);

        var (authorizedToken, authorizedCookie) = await AdminVehicleCatalogSeedIntegrationTests.GetAntiforgeryTokenAsync(client, authorizedUser.Id);
        using var authorizedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/catalog/seed");
        authorizedRequest.Headers.Add("X-Test-User", authorizedUser.Id);
        authorizedRequest.Headers.Add("RequestVerificationToken", authorizedToken);
        authorizedRequest.Headers.Add("Cookie", authorizedCookie);
        using var authorizedResponse = await client.SendAsync(authorizedRequest);
        Assert.Equal(HttpStatusCode.OK, authorizedResponse.StatusCode);
        var result = await authorizedResponse.Content.ReadFromJsonAsync<AdminVehicleCatalogSeedResultDto>();
        Assert.NotNull(result);
        Assert.Equal(157, result.CreatedBrands);
        Assert.Equal(947, result.CreatedModels);
        Assert.Equal(0, result.SkippedDeletedBrands);
        Assert.Equal(0, result.SkippedDeletedModels);
    }

    [Fact]
    public async Task SeedMatchesExistingCatalogEntriesWithoutCaseSensitiveDuplicates()
    {
        await AdminVehicleCatalogSeedIntegrationTests.ResetCatalogAsync(fixture);
        await using var context = fixture.CreateContext();
        var brand = new Brand
        {
            Name = "volvo",
            Kind = VehicleKind.Truck
        };
        context.Brands.Add(brand);
        await context.SaveChangesAsync();
        var model = new Model
        {
            Name = "fh",
            BrandId = brand.Id,
            Kind = VehicleKind.Truck
        };
        context.Models.Add(model);
        await context.SaveChangesAsync();

        var result = await new AdminVehicleCatalogSeedService(context).SeedAsync();

        Assert.Equal(156, result.CreatedBrands);
        Assert.Equal(946, result.CreatedModels);
        Assert.Equal(0, result.SkippedDeletedBrands);
        Assert.Equal(0, result.SkippedDeletedModels);
        Assert.Equal("volvo", (await context.Brands.IgnoreQueryFilters().SingleAsync(item => item.Id == brand.Id)).Name);
        Assert.Equal("fh", (await context.Models.IgnoreQueryFilters().SingleAsync(item => item.Id == model.Id)).Name);
        Assert.Equal(157, await context.Brands.IgnoreQueryFilters().CountAsync());
        Assert.Equal(947, await context.Models.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ConcurrentSeedRequestsCreateOneCatalogWithoutDuplicates()
    {
        await AdminVehicleCatalogSeedIntegrationTests.ResetCatalogAsync(fixture);

        var results = await Task.WhenAll(
            AdminVehicleCatalogSeedIntegrationTests.SeedWithNewContextAsync(fixture),
            AdminVehicleCatalogSeedIntegrationTests.SeedWithNewContextAsync(fixture));

        Assert.Contains(results, result => result.CreatedBrands == 157 && result.CreatedModels == 947);
        Assert.Contains(results, result => result.CreatedBrands == 0 && result.CreatedModels == 0);
        await using var context = fixture.CreateContext();
        Assert.Equal(157, await context.Brands.IgnoreQueryFilters().CountAsync());
        Assert.Equal(947, await context.Models.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task TransientFailureRetriesWithFreshTrackedCatalog()
    {
        await AdminVehicleCatalogSeedIntegrationTests.ResetCatalogAsync(fixture);
        await using var connectionContext = fixture.CreateContext();
        var connectionString = connectionContext.Database.GetConnectionString();
        var interceptor = new OneTimeTransientSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(connectionString, database => database.EnableRetryOnFailure(3))
            .AddInterceptors(interceptor)
            .Options;

        await using var context = new OpenCRMContext(options);
        var result = await new AdminVehicleCatalogSeedService(context).SeedAsync();

        Assert.Equal(1, interceptor.InjectedFailures);
        Assert.Equal(157, result.CreatedBrands);
        Assert.Equal(947, result.CreatedModels);
        Assert.Equal(157, await context.Brands.IgnoreQueryFilters().CountAsync());
        Assert.Equal(947, await context.Models.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task CommitFailureRollsBackTheSeedTransaction()
    {
        await AdminVehicleCatalogSeedIntegrationTests.ResetCatalogAsync(fixture);
        await using var connectionContext = fixture.CreateContext();
        var connectionString = connectionContext.Database.GetConnectionString();
        var interceptor = new OneTimeCommitFailureInterceptor();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using var context = new OpenCRMContext(options);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AdminVehicleCatalogSeedService(context).SeedAsync());

        Assert.Equal(1, interceptor.FailureCount);
        await using var verificationContext = fixture.CreateContext();
        Assert.Equal(0, await verificationContext.Brands.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await verificationContext.Models.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task SeedHonorsCancellationBeforeOpeningDatabaseTransaction()
    {
        await AdminVehicleCatalogSeedIntegrationTests.ResetCatalogAsync(fixture);
        await using var context = fixture.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new AdminVehicleCatalogSeedService(context).SeedAsync(cancellation.Token));
        Assert.Equal(0, await context.Brands.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await context.Models.IgnoreQueryFilters().CountAsync());
    }

    private static async Task ResetCatalogAsync(NotificationFixture fixture)
    {
        await using var context = fixture.CreateContext();
        await context.Models.IgnoreQueryFilters().ExecuteDeleteAsync();
        await context.Brands.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    private static async Task<AdminVehicleCatalogSeedResultDto> SeedWithNewContextAsync(NotificationFixture fixture)
    {
        await using var context = fixture.CreateContext();

        return await new AdminVehicleCatalogSeedService(context).SeedAsync();
    }

    private static async Task<(string RequestToken, string Cookie)> GetAntiforgeryTokenAsync(HttpClient client, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/antiforgery/token");
        request.Headers.Add("X-Test-User", userId);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];

        return (
            Assert.IsType<string>(json.RootElement.GetProperty("requestToken").GetString()),
            cookie);
    }

    private static void AssertSeedResource()
    {
        using var stream = typeof(AdminVehicleCatalogSeedService).Assembly.GetManifestResourceStream(SeedResourceName);
        Assert.NotNull(stream);
        Assert.Equal(SeedResourceHash, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        stream.Position = 0;
        using var json = JsonDocument.Parse(stream);
        var kinds = json.RootElement.EnumerateArray().ToArray();
        var brands = kinds.Sum(kind => kind.GetProperty("brands").GetArrayLength());
        var models = kinds.Sum(kind => kind.GetProperty("brands").EnumerateArray()
            .Sum(brand => brand.GetProperty("models").GetArrayLength()));

        Assert.Equal(10, kinds.Length);
        Assert.Equal(157, brands);
        Assert.Equal(947, models);
    }

    private static IdentityRole CreateRole(string name)
    {
        return new IdentityRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            NormalizedName = name.ToUpperInvariant()
        };
    }

    private static ApplicationUser CreateUser(string name)
    {
        var email = $"{name}-{Guid.NewGuid():N}@example.test";

        return new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        };
    }

    private static IdentityUserRole<string> CreateUserRole(ApplicationUser user, IdentityRole role)
    {
        return new IdentityUserRole<string>
        {
            UserId = user.Id,
            RoleId = role.Id
        };
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!this.Request.Headers.TryGetValue("X-Test-User", out var userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                this.Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class OneTimeTransientSaveChangesInterceptor : SaveChangesInterceptor
    {
        private int _injectedFailures;

        public int InjectedFailures => this._injectedFailures;

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            var saveResult = await base.SavedChangesAsync(eventData, result, cancellationToken);
            if (Interlocked.CompareExchange(ref this._injectedFailures, 1, 0) == 0)
            {
                throw new Npgsql.NpgsqlException(
                    "Искусственная временная ошибка для проверки повтора.",
                    new IOException("Временная ошибка подключения для проверки повтора."));
            }

            return saveResult;
        }
    }

    private sealed class OneTimeCommitFailureInterceptor : DbTransactionInterceptor
    {
        public int FailureCount { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            this.FailureCount++;

            throw new InvalidOperationException("Искусственная ошибка перед фиксацией транзакции.");
        }
    }
}
