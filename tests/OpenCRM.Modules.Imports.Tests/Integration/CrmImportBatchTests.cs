using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Imports;
using OpenCRM.Modules.Imports.Crm;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Imports;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Modules.Billing.Contracts;

namespace OpenCRM.Modules.Imports.Tests.Integration;

public sealed class CrmImportBatchTests(ImportDatabaseFixture fixture) : IClassFixture<ImportDatabaseFixture>
{
    [Fact]
    public async Task ConfirmCreatesHistoryForActualActorAndRejectsRepeat()
    {
        var (targetUserId, adminUserId, organizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context, maxVehicles: 5).Service;
        var preview = await CreatePreviewAsync(service, targetUserId, organizationId, "CRM source");

        var result = await service.ConfirmAsync(preview.Id, adminUserId, default);

        Assert.Equal(1, result.CreatedClients);
        Assert.Equal(1, result.CreatedVehicles);
        var client = await context.OrganizationClients.SingleAsync(item => item.OrganizationId == organizationId);
        var clientHistory = await context.OrganizationClientHistoryEvents.SingleAsync(item => item.OrganizationClientId == client.Id);
        Assert.Equal(adminUserId, clientHistory.UserId);
        Assert.Equal("crm-import", clientHistory.RelatedEntityType);
        Assert.Equal(preview.Id, clientHistory.RelatedEntityId);
        var vehicle = await context.Vehicles.SingleAsync(item => item.OrganizationId == organizationId);
        var vehicleHistory = await context.VehicleHistoryEvents.SingleAsync(item => item.VehicleId == vehicle.Id);
        Assert.Equal(adminUserId, vehicleHistory.UserId);
        Assert.Equal("crm-import", vehicleHistory.RelatedEntityType);
        Assert.Equal(preview.Id, vehicleHistory.RelatedEntityId);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ConfirmAsync(preview.Id, adminUserId, default));
        Assert.Equal(1, await context.OrganizationClients.CountAsync(item => item.OrganizationId == organizationId));
        Assert.Equal(1, await context.Vehicles.CountAsync(item => item.OrganizationId == organizationId));
    }

    [Fact]
    public async Task VehicleImportDoesNotEnforceZeroLimitWhenCommercialModeIsDisabled()
    {
        var (targetUserId, adminUserId, organizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context, maxVehicles: 0, commercialEnabled: false).Service;
        var preview = await CreatePreviewAsync(service, targetUserId, organizationId, "CRM source");

        var result = await service.ConfirmAsync(preview.Id, adminUserId, default);

        Assert.Equal(1, result.CreatedVehicles);
        Assert.Equal(1, await context.Vehicles.CountAsync(item => item.OrganizationId == organizationId));
    }

    [Fact]
    public async Task ConfirmChecksVehicleLimitAfterPreviewAndUsesOrganizationOwnerForReadPermission()
    {
        var (targetUserId, adminUserId, organizationId, organizationOwnerId) = await this.CreateOrganizationWithSeparateOwnerAsync();
        await using var context = fixture.CreateContext();
        context.Vehicles.Add(new Vehicle
        {
            OrganizationId = organizationId,
            UserId = targetUserId,
            Name = "Существующий автомобиль",
            RegistrationPlate = "B001BB"
        });
        await context.SaveChangesAsync();
        var (service, organizationSubscriptions) = CreateService(context, maxVehicles: 1);
        organizationSubscriptions.Setup(item => item.EnsureWorkspaceCanReadAsync(
                targetUserId,
                organizationId,
                OpenCRM.Services.Interfaces.Organizations.OrganizationPermissionCodes.VehiclesView))
            .ThrowsAsync(new UnauthorizedAccessException());
        var preview = await CreatePreviewAsync(service, targetUserId, organizationId, "CRM source");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.ConfirmAsync(preview.Id, adminUserId, default));

        Assert.Contains("Превышен лимит техники", exception.Message);
        organizationSubscriptions.Verify(item => item.EnsureWorkspaceCanReadAsync(
            organizationOwnerId,
            organizationId,
            OpenCRM.Services.Interfaces.Organizations.OrganizationPermissionCodes.VehiclesView), Times.Once);
        Assert.Equal(1, await context.Vehicles.CountAsync(item => item.OrganizationId == organizationId));
        Assert.Empty(await context.OrganizationClients.Where(item => item.OrganizationId == organizationId).ToListAsync());
        Assert.Null(await context.CrmImportBatches.Where(item => item.Id == preview.Id).Select(item => item.ImportedAt).SingleAsync());
    }

    [Fact]
    public async Task ClientOnlyImportDoesNotRequireVehicleReadAccessOrAvailableCapacity()
    {
        var (targetUserId, adminUserId, organizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        context.Vehicles.Add(new Vehicle
        {
            OrganizationId = organizationId,
            UserId = targetUserId,
            Name = "Существующий автомобиль",
            RegistrationPlate = "B001BB"
        });
        await context.SaveChangesAsync();
        var (service, organizationSubscriptions) = CreateService(context, maxVehicles: 0);
        var preview = await service.CreatePreviewAsync(
            targetUserId,
            organizationId,
            "clients.csv",
            Encoding.UTF8.GetBytes("Тип;Имя;Телефон\r\nclient;Анна;79990000002"),
            default);

        var result = await service.ConfirmAsync(preview.Id, adminUserId, default);

        Assert.Equal(1, result.CreatedClients);
        Assert.Equal(0, result.CreatedVehicles);
        organizationSubscriptions.Verify(item => item.EnsureWorkspaceCanReadAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        Assert.Equal(1, await context.Vehicles.CountAsync(item => item.OrganizationId == organizationId));
        Assert.Equal(1, await context.OrganizationClients.CountAsync(item => item.OrganizationId == organizationId));
    }

    [Fact]
    public async Task ConcurrentBatchesShareOrganizationCapacityLock()
    {
        var (targetUserId, adminUserId, organizationId) = await this.CreateOrganizationAsync();
        CrmImportPreview firstPreview;
        CrmImportPreview secondPreview;
        await using (var context = fixture.CreateContext())
        {
            var service = CreateService(context, maxVehicles: 1).Service;
            firstPreview = await CreatePreviewAsync(
                service, targetUserId, organizationId, "First client", "VIN-ONE", "79990000001", "A001AA");
            secondPreview = await CreatePreviewAsync(
                service, targetUserId, organizationId, "Second client", "VIN-TWO", "79990000002", "A002AA");
        }

        var barrier = new OrganizationLockBarrierInterceptor();
        async Task<string?> ConfirmAsync(Guid previewId)
        {
            await using var context = fixture.CreateContext(barrier);
            try
            {
                await CreateService(context, maxVehicles: 1).Service.ConfirmAsync(previewId, adminUserId, default);
                return null;
            }
            catch (InvalidDataException exception)
            {
                return exception.Message;
            }
        }

        var outcomes = await Task.WhenAll(ConfirmAsync(firstPreview.Id), ConfirmAsync(secondPreview.Id));

        Assert.Equal(2, barrier.ArrivedRequests);
        Assert.Null(Assert.Single(outcomes, message => message == null));
        var rejectedOutcome = Assert.Single(outcomes, message => message != null);
        Assert.NotNull(rejectedOutcome);
        Assert.StartsWith("Превышен лимит техники", rejectedOutcome);
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.Vehicles.CountAsync(item => item.OrganizationId == organizationId));
        Assert.Equal(1, await verification.OrganizationClients.CountAsync(item => item.OrganizationId == organizationId));
    }

    [Fact]
    public async Task DatabaseFailureAfterRecordWritesRollsBackRecordsHistoryAndBatchResult()
    {
        var (targetUserId, adminUserId, organizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context, maxVehicles: 5).Service;
        var preview = await CreatePreviewAsync(service, targetUserId, organizationId, "CRM source");
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_crm_batch_result_update() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW."ImportedAt" IS NOT NULL THEN
                    RAISE EXCEPTION 'Injected CRM batch result failure';
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER fail_crm_batch_result_update
            BEFORE UPDATE ON "CrmImportBatches"
            FOR EACH ROW EXECUTE FUNCTION fail_crm_batch_result_update();
            """);

        try
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => service.ConfirmAsync(preview.Id, adminUserId, default));
            var databaseException = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal("P0001", databaseException.SqlState);
            Assert.Contains("Injected CRM batch result failure", databaseException.MessageText);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS fail_crm_batch_result_update ON \"CrmImportBatches\";");
            await context.Database.ExecuteSqlRawAsync("DROP FUNCTION IF EXISTS fail_crm_batch_result_update();");
        }

        context.ChangeTracker.Clear();
        Assert.Empty(await context.OrganizationClients.Where(item => item.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await context.Vehicles.Where(item => item.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await context.OrganizationClientHistoryEvents.Where(item => item.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await context.VehicleHistoryEvents.Where(item => item.OrganizationId == organizationId).ToListAsync());
        Assert.Null(await context.CrmImportBatches.Where(item => item.Id == preview.Id).Select(item => item.ImportedAt).SingleAsync());
    }

    [Fact]
    public void ImportsRegistrationResolvesCrmContract()
    {
        var services = new ServiceCollection();
        services.AddOpenCRMImports();
        services.AddDbContext<OpenCRMContext>(options => options.UseNpgsql("Host=localhost;Database=imports-di;Username=test;Password=test"));
        services.AddScoped<IVehicleLimitQueryService>(_ => Mock.Of<IVehicleLimitQueryService>());
        services.AddScoped<IOrganizationSubscriptionService>(_ => Mock.Of<IOrganizationSubscriptionService>());
        using var provider = services.BuildServiceProvider();

        Assert.IsType<CrmImportService>(provider.GetRequiredService<ICrmImportService>());
    }

    [Theory]
    [InlineData("crm.csv", "Тип;Имя;Телефон\r\nclient;Анна;79990000001", "Анна")]
    [InlineData("crm.json", "[{\"type\":\"client\",\"name\":\"Анна\",\"phone\":\"79990000001\"}]", "Анна")]
    [InlineData("crm.xml", "<clients><client name=\"Анна\" phone=\"79990000001\" /></clients>", "Анна")]
    public async Task ParserKeepsExistingFileFormats(string fileName, string content, string expectedName)
    {
        var (targetUserId, _, organizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context, maxVehicles: 5).Service;

        var preview = await service.CreatePreviewAsync(targetUserId, organizationId, fileName, Encoding.UTF8.GetBytes(content), default);

        Assert.Equal(expectedName, Assert.Single(preview.Rows).Name);
        Assert.Equal(fileName, preview.FileName);
        Assert.False(preview.AlreadyImported);
    }

    private async Task<(string TargetUserId, string AdminUserId, Guid OrganizationId)> CreateOrganizationAsync()
    {
        await using var context = fixture.CreateContext();
        var targetUser = new ApplicationUser();
        var adminUser = new ApplicationUser();
        var organization = new Organization { Name = "CRM import", OwnerUser = targetUser };
        organization.Members.Add(new OrganizationMember { User = targetUser, RoleCode = "owner" });
        context.ApplicationUsers.Add(adminUser);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return (targetUser.Id, adminUser.Id, organization.Id);
    }

    private async Task<(string TargetUserId, string AdminUserId, Guid OrganizationId, string OrganizationOwnerId)> CreateOrganizationWithSeparateOwnerAsync()
    {
        await using var context = fixture.CreateContext();
        var targetUser = new ApplicationUser();
        var organizationOwner = new ApplicationUser();
        var adminUser = new ApplicationUser();
        var organization = new Organization { Name = "CRM import", OwnerUser = organizationOwner };
        organization.Members.Add(new OrganizationMember { User = organizationOwner, RoleCode = "owner" });
        organization.Members.Add(new OrganizationMember { User = targetUser, RoleCode = "member" });
        context.ApplicationUsers.Add(adminUser);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return (targetUser.Id, adminUser.Id, organization.Id, organizationOwner.Id);
    }

    private static async Task<CrmImportPreview> CreatePreviewAsync(
        ICrmImportService service,
        string userId,
        Guid organizationId,
        string clientName,
        string vin = "VIN-001",
        string phone = "79990000001",
        string plate = "A001AA")
    {
        var content = $"Тип;Имя;Телефон;Телефон клиента;Марка;Модель;VIN;Госномер;Год\r\n" +
            $"client;{clientName};{phone};;;;;;\r\n" +
            $"vehicle;{clientName}; ;{phone};Toyota;Camry;{vin};{plate};2020";

        return await service.CreatePreviewAsync(userId, organizationId, "crm.csv", Encoding.UTF8.GetBytes(content), default);
    }

    private static (CrmImportService Service, Mock<IOrganizationSubscriptionService> OrganizationSubscriptions) CreateService(
        OpenCRMContext context,
        int maxVehicles,
        bool commercialEnabled = true)
    {
        var vehicleRepository = new Mock<IVehicleRepository>();
        vehicleRepository.Setup(repository => repository.CountVehicles(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<bool?>()))
            .Returns((string _, Guid? organizationId, bool? isArchived) => context.Vehicles
                .Where(vehicle => vehicle.OrganizationId == organizationId && !vehicle.IsDeleted)
                .Where(vehicle => isArchived == null || vehicle.IsArchived == isArchived.Value)
                .CountAsync());

        var personalSubscriptions = new Mock<IPersonalSubscriptionQueryService>();
        var organizationSubscriptions = new Mock<IOrganizationSubscriptionService>();
        organizationSubscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        organizationSubscriptions.Setup(service => service.GetVehicleLimitAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>()))
            .ReturnsAsync(maxVehicles);
        organizationSubscriptions.Setup(service => service.HasActiveSubscriptionAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(false);
        organizationSubscriptions.Setup(service => service.GetEffectivePlanAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync((OpenCRM.Services.Interfaces.Subscriptions.Dtos.SubscriptionPlanDto?)null);

        var featureAccess = new Mock<IFeatureAccessService>();
        featureAccess.Setup(service => service.GetIntValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(25);
        var vehicleLimits = new VehicleLimitQueryService(
            vehicleRepository.Object,
            personalSubscriptions.Object,
            organizationSubscriptions.Object,
            featureAccess.Object,
            Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == commercialEnabled));

        organizationSubscriptions.Setup(service => service.GetFeatureSnapshotAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new FeatureAccessSnapshotDto());

        return (new CrmImportService(context, vehicleLimits, organizationSubscriptions.Object), organizationSubscriptions);
    }

    private sealed class OrganizationLockBarrierInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<Guid, bool> _contexts = new();
        private readonly TaskCompletionSource _bothRequestsReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ArrivedRequests => this._contexts.Count;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("FROM \"Organizations\" WHERE \"Id\"", StringComparison.Ordinal) ||
                !command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal) ||
                !this._contexts.TryAdd(eventData.Context!.ContextId.InstanceId, true))
            {
                return result;
            }

            if (this._contexts.Count == 2)
            {
                this._bothRequestsReady.TrySetResult();
            }

            await this._bothRequestsReady.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);

            return result;
        }
    }
}
