using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleClientSummaryQueryServiceTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task QueryScopesOrganizationAndClientsKeepsArchivedVehiclesAndAggregatesMaintenance()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = $"{Guid.NewGuid()}@example.test" };
        var organization = new Organization { Name = "Организация клиента", OwnerUser = owner, OwnerUserId = owner.Id };
        var foreignOrganization = new Organization { Name = "Другая организация", OwnerUser = owner, OwnerUserId = owner.Id };
        var client = new OrganizationClient { Organization = organization, OrganizationId = organization.Id, Name = "Клиент" };
        var otherClient = new OrganizationClient { Organization = organization, OrganizationId = organization.Id, Name = "Другой клиент" };
        var foreignClient = new OrganizationClient { Organization = foreignOrganization, OrganizationId = foreignOrganization.Id, Name = "Чужой клиент" };
        context.Users.Add(owner);
        context.Organizations.AddRange(organization, foreignOrganization);
        context.OrganizationClients.AddRange(client, otherClient, foreignClient);
        var brand = new Brand { Name = "Acme", Kind = VehicleKind.PassengerCar };
        var model = new Model { Name = "Coupe", Brand = brand, Kind = VehicleKind.PassengerCar };
        context.Models.Add(model);
        await context.SaveChangesAsync();

        var namedVehicle = VehicleClientSummaryQueryServiceTests.CreateVehicle(owner.Id, organization.Id, client.Id, "Acme", "Coupe", "Zeta");
        namedVehicle.BrandId = brand.Id;
        namedVehicle.Brand = brand;
        namedVehicle.ModelId = model.Id;
        namedVehicle.Model = model;
        namedVehicle.YearIssue = 2020;
        namedVehicle.RegistrationPlate = "А123АА154";
        var archivedVehicle = VehicleClientSummaryQueryServiceTests.CreateVehicle(owner.Id, organization.Id, client.Id, "Acme", "Coupe", "Alpha");
        archivedVehicle.IsArchived = true;
        archivedVehicle.ArchiveDate = DateTime.UtcNow;
        var otherClientVehicle = VehicleClientSummaryQueryServiceTests.CreateVehicle(owner.Id, organization.Id, otherClient.Id, "Acme", "Roadster", "Другой клиент");
        var otherBrandVehicle = VehicleClientSummaryQueryServiceTests.CreateVehicle(owner.Id, organization.Id, otherClient.Id, "Lada", "Granta", "Другой бренд");
        var foreignVehicle = VehicleClientSummaryQueryServiceTests.CreateVehicle(owner.Id, foreignOrganization.Id, foreignClient.Id, "Acme", "Coupe", "Чужая организация");
        var deletedVehicle = VehicleClientSummaryQueryServiceTests.CreateVehicle(owner.Id, organization.Id, client.Id, "Acme", "Coupe", "Удалена");
        deletedVehicle.IsDeleted = true;
        deletedVehicle.DeletionDate = DateTime.UtcNow;
        var unassignedVehicle = VehicleClientSummaryQueryServiceTests.CreateVehicle(owner.Id, organization.Id, null, "Acme", "Coupe", "Без клиента");
        context.Vehicles.AddRange(namedVehicle, archivedVehicle, otherClientVehicle, otherBrandVehicle, foreignVehicle, deletedVehicle, unassignedVehicle);
        var fallbackDate = new DateTime(2024, 2, 3, 0, 0, 0, DateTimeKind.Utc);
        var completedDate = new DateTime(2025, 4, 5, 0, 0, 0, DateTimeKind.Utc);
        context.Maintenance.AddRange(
            VehicleClientSummaryQueryServiceTests.CreateMaintenance(namedVehicle, MaintenanceStatus.Completed, fallbackDate, null),
            VehicleClientSummaryQueryServiceTests.CreateMaintenance(namedVehicle, MaintenanceStatus.Completed, fallbackDate.AddDays(-1), completedDate),
            VehicleClientSummaryQueryServiceTests.CreateMaintenance(namedVehicle, MaintenanceStatus.Planned, completedDate.AddDays(2), null),
            VehicleClientSummaryQueryServiceTests.CreateMaintenance(namedVehicle, MaintenanceStatus.Completed, completedDate.AddDays(3), completedDate.AddDays(4), isDeleted: true),
            VehicleClientSummaryQueryServiceTests.CreateMaintenance(otherClientVehicle, MaintenanceStatus.Completed, fallbackDate, null));
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var service = new VehicleClientSummaryQueryService(context);

        var results = await service.GetByClientIdsAsync(organization.Id, [client.Id, otherClient.Id, foreignClient.Id], sortByName: true);

        Assert.Equal(4, results.Count);
        Assert.Equal(
            new[] { archivedVehicle.Id, namedVehicle.Id, otherClientVehicle.Id, otherBrandVehicle.Id },
            results.Select(vehicle => vehicle.Id));
        var namedResult = Assert.Single(results, vehicle => vehicle.Id == namedVehicle.Id);
        Assert.Equal(client.Id, namedResult.ClientId);
        Assert.Equal(2020, namedResult.YearIssue);
        Assert.Equal("Zeta", namedResult.Name);
        Assert.Equal("А123АА154", namedResult.RegistrationPlate);
        Assert.Equal("Acme", namedResult.BrandName);
        Assert.Equal("Coupe", namedResult.ModelName);
        Assert.Equal(3, namedResult.MaintenanceCount);
        Assert.Equal(completedDate, namedResult.LastMaintenanceDate);
        var archivedResult = Assert.Single(results, vehicle => vehicle.Id == archivedVehicle.Id);
        Assert.Null(archivedResult.LastMaintenanceDate);
        Assert.Equal(0, archivedResult.MaintenanceCount);
        var fallbackResult = Assert.Single(results, vehicle => vehicle.Id == otherClientVehicle.Id);
        Assert.Equal(fallbackDate, fallbackResult.LastMaintenanceDate);
        var singleClientRows = await service.GetByClientIdsAsync(organization.Id, [client.Id], sortByName: true);
        Assert.Equal(2, singleClientRows.Count);
        Assert.DoesNotContain(singleClientRows, vehicle => vehicle.Id == otherClientVehicle.Id);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task QueryUsesOneBatchCommandAndEmptyClientIdsSkipDatabase()
    {
        await using var setupContext = fixture.CreateContext();
        var owner = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = $"{Guid.NewGuid()}@example.test" };
        var organization = new Organization { Name = "Пакетная организация", OwnerUser = owner, OwnerUserId = owner.Id };
        var client = new OrganizationClient { Organization = organization, OrganizationId = organization.Id, Name = "Пакетный клиент" };
        var otherClient = new OrganizationClient { Organization = organization, OrganizationId = organization.Id, Name = "Второй пакетный клиент" };
        setupContext.Users.Add(owner);
        setupContext.Organizations.Add(organization);
        setupContext.OrganizationClients.AddRange(client, otherClient);
        await setupContext.SaveChangesAsync();
        setupContext.Vehicles.AddRange(
            VehicleClientSummaryQueryServiceTests.CreateVehicle(owner.Id, organization.Id, client.Id, "Brand", "Model", "Одна"),
            VehicleClientSummaryQueryServiceTests.CreateVehicle(owner.Id, organization.Id, client.Id, "Brand", "Model", "Две"),
            VehicleClientSummaryQueryServiceTests.CreateVehicle(owner.Id, organization.Id, otherClient.Id, "Brand", "Model", "Три"));
        await setupContext.SaveChangesAsync();

        var commandCounter = new ReaderCommandCounter();
        await using var context = fixture.CreateContext(commandCounter);
        var service = new VehicleClientSummaryQueryService(context);

        Assert.Empty(await service.GetByClientIdsAsync(organization.Id, [], sortByName: false));
        Assert.Equal(0, commandCounter.Count);

        var rows = await service.GetByClientIdsAsync(organization.Id, [client.Id, otherClient.Id], sortByName: false);

        Assert.Equal(3, rows.Count);
        Assert.Equal(1, commandCounter.Count);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static Vehicle CreateVehicle(
        string userId,
        Guid organizationId,
        Guid? clientId,
        string brand,
        string model,
        string name) => new()
        {
            UserId = userId,
            OrganizationId = organizationId,
            OrganizationClientId = clientId,
            ManualBrandName = brand,
            ManualModelName = model,
            Name = name,
            DateTimeCreation = DateTime.UtcNow
        };

    private static global::OpenCRM.Domain.Maintenance CreateMaintenance(
        Vehicle vehicle,
        MaintenanceStatus status,
        DateTime date,
        DateTime? completedDate,
        bool isDeleted = false) => new()
        {
            Vehicle = vehicle,
            Status = status,
            Date = date,
            CompletedDate = completedDate,
            IsDeleted = isDeleted,
            UserId = vehicle.UserId
        };

    private sealed class ReaderCommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            this.Count++;

            return new ValueTask<InterceptionResult<DbDataReader>>(result);
        }
    }
}
