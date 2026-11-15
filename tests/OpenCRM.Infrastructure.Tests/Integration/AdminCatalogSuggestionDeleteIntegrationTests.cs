using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Testing;
using OpenCRM.WebApp.Controllers.Admin.Api;
using Npgsql;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class AdminCatalogSuggestionDeleteIntegrationTests(NotificationFixture fixture)
    : IClassFixture<NotificationFixture>
{
    [Theory]
    [InlineData(VehicleCatalogSuggestionStatus.Pending)]
    [InlineData(VehicleCatalogSuggestionStatus.Rejected)]
    [InlineData(VehicleCatalogSuggestionStatus.Approved)]
    public async Task DeleteRemovesOnlySelectedSuggestionWithoutRequiringAuthor(
        VehicleCatalogSuggestionStatus status)
    {
        await AdminCatalogSuggestionDeleteIntegrationTests.ResetSuggestionsAsync(fixture);
        await using var setupContext = fixture.CreateContext();
        var manageRole = await AdminCatalogSuggestionDeleteIntegrationTests.GetOrCreateSystemRoleAsync(setupContext);
        var manageUser = AdminCatalogSuggestionDeleteIntegrationTests.CreateUser("suggestion-manage");
        setupContext.Users.Add(manageUser);
        setupContext.UserRoles.Add(AdminCatalogSuggestionDeleteIntegrationTests.CreateUserRole(manageUser, manageRole));
        var brand = new Brand
        {
            Name = $"Suggestion delete brand {Guid.NewGuid():N}",
            Kind = VehicleKind.PassengerCar
        };
        setupContext.Brands.Add(brand);
        await setupContext.SaveChangesAsync();
        var model = new Model
        {
            Name = "Suggestion delete model",
            BrandId = brand.Id,
            Kind = VehicleKind.PassengerCar
        };
        setupContext.Models.Add(model);
        await setupContext.SaveChangesAsync();

        var selected = new VehicleCatalogSuggestion
        {
            Kind = VehicleKind.PassengerCar,
            BrandName = $"Selected {status}",
            ModelName = "Selected model",
            SuggestedByUserId = $"missing-author-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            Status = status,
            ApprovedBrandId = status == VehicleCatalogSuggestionStatus.Approved ? brand.Id : null,
            ApprovedModelId = status == VehicleCatalogSuggestionStatus.Approved ? model.Id : null
        };
        var otherStatuses = Enum.GetValues<VehicleCatalogSuggestionStatus>()
            .Where(item => item != status)
            .Select(item => new VehicleCatalogSuggestion
            {
                Kind = VehicleKind.PassengerCar,
                BrandName = $"Preserved {item}",
                ModelName = "Preserved model",
                SuggestedByUserId = $"missing-author-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow,
                Status = item
            })
            .ToArray();
        setupContext.VehicleCatalogSuggestions.AddRange([selected, .. otherStatuses]);
        await setupContext.SaveChangesAsync();

        await using var app = await AdminCatalogSuggestionDeleteIntegrationTests.CreateAppAsync(fixture);
        using var client = app.GetTestClient();
        using var response = await AdminCatalogSuggestionDeleteIntegrationTests.DeleteAsManageContentAsync(
            client,
            selected.Id,
            manageUser.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AdminOperationResult>();
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.Equal(selected.Id, result.Id);

        await using var verificationContext = fixture.CreateContext();
        Assert.False(await verificationContext.VehicleCatalogSuggestions.AnyAsync(item => item.Id == selected.Id));
        Assert.Equal(otherStatuses.Length, await verificationContext.VehicleCatalogSuggestions.CountAsync());
        Assert.True(await verificationContext.Brands.AnyAsync(item => item.Id == brand.Id));
        Assert.True(await verificationContext.Models.AnyAsync(item => item.Id == model.Id));
    }

    [Fact]
    public async Task DeleteRequiresManageContentAndReturnsNotFoundForMissingSuggestion()
    {
        await AdminCatalogSuggestionDeleteIntegrationTests.ResetSuggestionsAsync(fixture);
        await using var context = fixture.CreateContext();
        var manageRole = await AdminCatalogSuggestionDeleteIntegrationTests.GetOrCreateSystemRoleAsync(context);
        var viewRole = AdminCatalogSuggestionDeleteIntegrationTests.CreateRole("CatalogSuggestionView");
        var manageUser = AdminCatalogSuggestionDeleteIntegrationTests.CreateUser("suggestion-manage");
        var viewUser = AdminCatalogSuggestionDeleteIntegrationTests.CreateUser("suggestion-view");
        context.Roles.Add(viewRole);
        context.Users.AddRange(manageUser, viewUser);
        context.UserRoles.AddRange(
            AdminCatalogSuggestionDeleteIntegrationTests.CreateUserRole(manageUser, manageRole),
            AdminCatalogSuggestionDeleteIntegrationTests.CreateUserRole(viewUser, viewRole));
        context.AdminRolePermissions.AddRange(
            new AdminRolePermission
            {
                RoleId = manageRole.Id,
                PermissionCode = AdminAuthorizationPolicies.ManageContent
            },
            new AdminRolePermission
            {
                RoleId = manageRole.Id,
                PermissionCode = AdminAuthorizationPolicies.ViewContent
            },
            new AdminRolePermission
            {
                RoleId = viewRole.Id,
                PermissionCode = AdminAuthorizationPolicies.Access
            },
            new AdminRolePermission
            {
                RoleId = viewRole.Id,
                PermissionCode = AdminAuthorizationPolicies.ViewContent
            });
        var suggestion = new VehicleCatalogSuggestion
        {
            Kind = VehicleKind.PassengerCar,
            BrandName = "Secure suggestion",
            ModelName = "Model",
            SuggestedByUserId = manageUser.Id,
            CreatedAt = DateTime.UtcNow,
            Status = VehicleCatalogSuggestionStatus.Pending
        };
        context.VehicleCatalogSuggestions.Add(suggestion);
        await context.SaveChangesAsync();

        await using var app = await AdminCatalogSuggestionDeleteIntegrationTests.CreateAppAsync(fixture);
        using var client = app.GetTestClient();
        using var viewResponse = await AdminCatalogSuggestionDeleteIntegrationTests.DeleteAsync(
            client,
            suggestion.Id,
            viewUser.Id);
        using var missingResponse = await AdminCatalogSuggestionDeleteIntegrationTests.DeleteAsync(
            client,
            int.MaxValue,
            manageUser.Id);

        Assert.Equal(HttpStatusCode.Forbidden, viewResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        await using var verificationContext = fixture.CreateContext();
        Assert.True(await verificationContext.VehicleCatalogSuggestions.AnyAsync(item => item.Id == suggestion.Id));
    }

    [Fact]
    public async Task RejectingPairWithHistoricalRejectedSuggestionPreservesBothHistoryRows()
    {
        await AdminCatalogSuggestionDeleteIntegrationTests.ResetSuggestionsAsync(fixture);
        await using var context = fixture.CreateContext();
        context.VehicleCatalogSuggestions.AddRange(
            new VehicleCatalogSuggestion
            {
                Kind = VehicleKind.PassengerCar,
                BrandName = "Repeated brand",
                ModelName = "Repeated model",
                SuggestedByUserId = $"missing-author-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                Status = VehicleCatalogSuggestionStatus.Rejected
            },
            new VehicleCatalogSuggestion
            {
                Kind = VehicleKind.PassengerCar,
                BrandName = "Repeated brand",
                ModelName = "Repeated model",
                SuggestedByUserId = $"missing-author-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow,
                Status = VehicleCatalogSuggestionStatus.Pending
            });
        await context.SaveChangesAsync();
        var pending = await context.VehicleCatalogSuggestions
            .SingleAsync(item => item.Status == VehicleCatalogSuggestionStatus.Pending);

        var result = await new AdminCatalogSuggestionService(context, new MemoryCache(new MemoryCacheOptions()))
            .RejectAsync(pending.Id, "admin-id", null);

        Assert.True(result.Succeeded);
        Assert.Equal(2, await context.VehicleCatalogSuggestions.CountAsync(item =>
            item.Kind == VehicleKind.PassengerCar &&
            item.BrandName == "Repeated brand" &&
            item.ModelName == "Repeated model" &&
            item.Status == VehicleCatalogSuggestionStatus.Rejected));
    }

    [Fact]
    public async Task ApprovingPendingPairWithHistoricalSuggestionPreservesHistoryAndOtherPendingItems()
    {
        await AdminCatalogSuggestionDeleteIntegrationTests.ResetSuggestionsAsync(fixture);
        await using var context = fixture.CreateContext();
        var owner = AdminCatalogSuggestionDeleteIntegrationTests.CreateUser("catalog-suggestion-vehicle-owner");
        context.Users.Add(owner);
        var historical = new VehicleCatalogSuggestion
        {
            Kind = VehicleKind.PassengerCar,
            BrandName = "Repeated % brand",
            ModelName = "Model_1",
            SuggestedByUserId = $"missing-author-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            Status = VehicleCatalogSuggestionStatus.Rejected
        };
        var pending = new VehicleCatalogSuggestion
        {
            Kind = VehicleKind.PassengerCar,
            BrandName = historical.BrandName,
            ModelName = historical.ModelName,
            SuggestedByUserId = $"missing-author-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            Status = VehicleCatalogSuggestionStatus.Pending
        };
        var unrelatedPending = new VehicleCatalogSuggestion
        {
            Kind = VehicleKind.PassengerCar,
            BrandName = "Unrelated brand",
            ModelName = "Unrelated model",
            SuggestedByUserId = $"missing-author-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            Status = VehicleCatalogSuggestionStatus.Pending
        };
        var exactVehicle = new Vehicle
        {
            User = owner,
            Kind = VehicleKind.PassengerCar,
            ManualBrandName = historical.BrandName,
            ManualModelName = historical.ModelName,
            Name = "Exact manual match",
            DateTimeCreation = DateTime.UtcNow
        };
        var wildcardNearMatch = new Vehicle
        {
            User = owner,
            Kind = VehicleKind.PassengerCar,
            ManualBrandName = "Repeated long brand",
            ManualModelName = "ModelA1",
            Name = "Must remain manual",
            DateTimeCreation = DateTime.UtcNow
        };
        context.VehicleCatalogSuggestions.AddRange(historical, pending, unrelatedPending);
        context.Vehicles.AddRange(exactVehicle, wildcardNearMatch);
        await context.SaveChangesAsync();

        var result = await new AdminCatalogSuggestionService(context, new MemoryCache(new MemoryCacheOptions()))
            .ApproveAsync(pending.Id, "admin-id", null);

        Assert.True(result.Succeeded);
        Assert.Equal(VehicleCatalogSuggestionStatus.Rejected,
            (await context.VehicleCatalogSuggestions.SingleAsync(item => item.Id == historical.Id)).Status);
        Assert.Equal(VehicleCatalogSuggestionStatus.Approved,
            (await context.VehicleCatalogSuggestions.SingleAsync(item => item.Id == pending.Id)).Status);
        Assert.Equal(VehicleCatalogSuggestionStatus.Pending,
            (await context.VehicleCatalogSuggestions.SingleAsync(item => item.Id == unrelatedPending.Id)).Status);
        var updatedVehicle = await context.Vehicles.SingleAsync(item => item.Id == exactVehicle.Id);
        Assert.NotNull(updatedVehicle.ModelId);
        Assert.Null(updatedVehicle.ManualBrandName);
        Assert.Null(updatedVehicle.ManualModelName);
        var preservedVehicle = await context.Vehicles.SingleAsync(item => item.Id == wildcardNearMatch.Id);
        Assert.Null(preservedVehicle.BrandId);
        Assert.Null(preservedVehicle.ModelId);
        Assert.Equal("Repeated long brand", preservedVehicle.ManualBrandName);
        Assert.Equal("ModelA1", preservedVehicle.ManualModelName);
    }

    [Fact]
    public async Task UniqueIndexAllowsOnlyOnePendingSuggestionForSameCatalogIdentity()
    {
        await AdminCatalogSuggestionDeleteIntegrationTests.ResetSuggestionsAsync(fixture);
        await using var context = fixture.CreateContext();
        context.VehicleCatalogSuggestions.Add(new VehicleCatalogSuggestion
        {
            Kind = VehicleKind.PassengerCar,
            BrandName = "Unique pending brand",
            ModelName = "Unique pending model",
            SuggestedByUserId = "pending-one",
            CreatedAt = DateTime.UtcNow,
            Status = VehicleCatalogSuggestionStatus.Pending
        });
        context.VehicleCatalogSuggestions.Add(new VehicleCatalogSuggestion
        {
            Kind = VehicleKind.PassengerCar,
            BrandName = "Unique pending brand",
            ModelName = "Unique pending model",
            SuggestedByUserId = "pending-two",
            CreatedAt = DateTime.UtcNow,
            Status = VehicleCatalogSuggestionStatus.Pending
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("IX_VehicleCatalogSuggestions_Kind_BrandName_ModelName_Status", exception.InnerException?.Message);
    }

    [Fact]
    public async Task DownMigrationRefusesToDropIndexWhenHistoricalDuplicatesExist()
    {
        await AdminCatalogSuggestionDeleteIntegrationTests.ResetSuggestionsAsync(fixture);
        await using var context = fixture.CreateContext();
        context.VehicleCatalogSuggestions.AddRange(
            new VehicleCatalogSuggestion
            {
                Kind = VehicleKind.PassengerCar,
                BrandName = "Historical duplicate",
                ModelName = "Model",
                SuggestedByUserId = "historical-one",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                Status = VehicleCatalogSuggestionStatus.Rejected
            },
            new VehicleCatalogSuggestion
            {
                Kind = VehicleKind.PassengerCar,
                BrandName = "Historical duplicate",
                ModelName = "Model",
                SuggestedByUserId = "historical-two",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                Status = VehicleCatalogSuggestionStatus.Rejected
            });
        await context.SaveChangesAsync();

        var migrator = context.GetService<IMigrator>();
        try
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                migrator.MigrateAsync("20261015092404_AddOrganizationDataExportRequestDate"));

            Assert.Contains("Нельзя восстановить уникальный индекс предложений", exception.Message);
        }
        finally
        {
            await migrator.MigrateAsync();
        }

        Assert.Equal(2, await context.VehicleCatalogSuggestions.CountAsync(item => item.Status == VehicleCatalogSuggestionStatus.Rejected));
    }

    [Fact]
    public async Task ApproveAndRejectOnlyAllowPendingSuggestions()
    {
        await AdminCatalogSuggestionDeleteIntegrationTests.ResetSuggestionsAsync(fixture);
        await using var context = fixture.CreateContext();
        var approved = new VehicleCatalogSuggestion
        {
            Kind = VehicleKind.PassengerCar,
            BrandName = "Already approved",
            ModelName = "Model",
            SuggestedByUserId = "approved-user",
            CreatedAt = DateTime.UtcNow,
            Status = VehicleCatalogSuggestionStatus.Approved
        };
        var rejected = new VehicleCatalogSuggestion
        {
            Kind = VehicleKind.PassengerCar,
            BrandName = "Already rejected",
            ModelName = "Model",
            SuggestedByUserId = "rejected-user",
            CreatedAt = DateTime.UtcNow,
            Status = VehicleCatalogSuggestionStatus.Rejected
        };
        context.VehicleCatalogSuggestions.AddRange(approved, rejected);
        await context.SaveChangesAsync();
        var service = new AdminCatalogSuggestionService(context, new MemoryCache(new MemoryCacheOptions()));

        var approveResult = await service.ApproveAsync(rejected.Id, "admin-id", null);
        var rejectResult = await service.RejectAsync(approved.Id, "admin-id", null);

        Assert.False(approveResult.Succeeded);
        Assert.False(rejectResult.Succeeded);
        Assert.Equal(VehicleCatalogSuggestionStatus.Approved,
            (await context.VehicleCatalogSuggestions.SingleAsync(item => item.Id == approved.Id)).Status);
        Assert.Equal(VehicleCatalogSuggestionStatus.Rejected,
            (await context.VehicleCatalogSuggestions.SingleAsync(item => item.Id == rejected.Id)).Status);
    }

    private static async Task<WebApplication> CreateAppAsync(NotificationFixture fixture)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped(_ => fixture.CreateContext());
        builder.Services.AddMemoryCache();
        builder.Services.AddOpenCRMAdminModule();
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                IdentityConstants.ApplicationScheme,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Bearer", _ => { });
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(VehicleCatalogSuggestionsApiController).Assembly);

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        return app;
    }

    private static async Task ResetSuggestionsAsync(NotificationFixture fixture)
    {
        await using var context = fixture.CreateContext();
        await context.VehicleCatalogSuggestions.ExecuteDeleteAsync();
    }

    private static Task<HttpResponseMessage> DeleteAsManageContentAsync(
        HttpClient client,
        int suggestionId,
        string userId)
    {
        return AdminCatalogSuggestionDeleteIntegrationTests.DeleteAsync(client, suggestionId, userId);
    }

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, int suggestionId, string userId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/admin/catalog-suggestions/{suggestionId}");
        request.Headers.Add("X-Test-User", userId);

        return client.SendAsync(request);
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

    private static async Task<IdentityRole> GetOrCreateSystemRoleAsync(OpenCRMContext context)
    {
        var role = await context.Roles.FirstOrDefaultAsync(item => item.Name == "System");
        if (role != null)
        {
            return role;
        }

        role = AdminCatalogSuggestionDeleteIntegrationTests.CreateRole("System");
        context.Roles.Add(role);

        return role;
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
}
