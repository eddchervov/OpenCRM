using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class InventoryMovementSourceQueryTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task EnrichesMovementSourcesAndInitiatorNames()
    {
        await using var context = fixture.CreateContext();
        var userWithEmail = new ApplicationUser { Email = "contact@example.invalid" };
        var userWithName = new ApplicationUser { UserName ="worker-name" };
        var userWithPhone = new ApplicationUser { PhoneNumber = "+7 000 000-00-01" };
        var userWithId = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = userWithEmail };
        var stock = new OrganizationMaterialStock { Organization = organization, Name = "Состав", Unit = "л" };
        var vehicleWithName = new Vehicle
        {
            User = userWithEmail, Organization = organization, Name = "Автомобиль клиента", RegistrationPlate = "А123АА154"
        };
        var maintenanceWithVehicle = new OpenCRM.Domain.Maintenance
        {
            User = userWithEmail,
            Vehicle = vehicleWithName,
            WorkActNumber = 42,
            Date = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            PlannedDate = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)
        };
        var maintenanceWithoutVehicleName = new OpenCRM.Domain.Maintenance
        {
            User = userWithEmail,
            Vehicle = new Vehicle { User = userWithEmail, Organization = organization },
            Date = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc)
        };
        var emptyInitiatorUser = new ApplicationUser();
        var emptyInitiatorOrganization = new Organization { Name = "Без инициатора", OwnerUser = emptyInitiatorUser };
        var emptyInitiatorStock = new OrganizationMaterialStock
        {
            Organization = emptyInitiatorOrganization,
            Name = "Материал",
            Unit = "л"
        };
        var movements = new[]
        {
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.Receipt, "manual", userWithEmail.Id),
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.Issue, "manual", userWithName.Id),
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.Adjustment, "manual", userWithPhone.Id),
            CreateMovement(stock, "custom", "manual", userWithId.Id),
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.Transfer, "transfer", "missing-user"),
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.MaintenanceIssue, "maintenance", null, maintenanceWithVehicle.Id),
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.MaintenanceIssue, "maintenance", null, maintenanceWithoutVehicleName.Id),
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.MaintenanceIssue, "maintenance", null, Guid.NewGuid()),
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.Receipt, null, null),
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.Issue, " ", null),
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.MaintenanceIssue, null, null),
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.Adjustment, null, null),
            CreateMovement(stock, "custom", null, null),
            CreateMovement(stock, OrganizationMaterialMovementTypeCodes.Receipt, "manual", null, maintenanceId: maintenanceWithVehicle.Id)
        };
        for (var index = 0; index < movements.Length; index++)
        {
            movements[index].CreatedAt = DateTime.UtcNow.AddMinutes(-index);
        }

        context.AddRange(
            userWithName, userWithPhone, userWithId, maintenanceWithVehicle, maintenanceWithoutVehicleName, stock,
            emptyInitiatorStock);
        context.OrganizationMaterialMovements.AddRange(movements);
        context.OrganizationMaterialMovements.Add(CreateMovement(
            emptyInitiatorStock,
            OrganizationMaterialMovementTypeCodes.Receipt,
            null,
            null));
        await context.SaveChangesAsync();
        var service = new MaterialInventoryQueryService(context, Mock.Of<IOrganizationSubscriptionService>());

        var inventory = await service.GetAsync(userWithEmail.Id, organization.Id);
        var titles = inventory.RecentMovements.ToDictionary(movement => movement.Id, movement => movement.SourceTitle);

        Assert.Equal("Ручной приход на склад", titles[movements[0].Id]);
        Assert.Equal("Ручное списание со склада", titles[movements[1].Id]);
        Assert.Equal("Ручная корректировка остатка", titles[movements[2].Id]);
        Assert.Equal("Ручная складская операция", titles[movements[3].Id]);
        Assert.Equal("Источник: transfer", titles[movements[4].Id]);
        Assert.Equal("Услуга: акт #42 от 02.05.2026, Автомобиль клиента · А123АА154", titles[movements[5].Id]);
        Assert.Equal("Услуга: запись от 03.05.2026", titles[movements[6].Id]);
        Assert.Equal("Источник: maintenance", titles[movements[7].Id]);
        Assert.Equal("Приход на склад", titles[movements[8].Id]);
        Assert.Equal("Списание со склада", titles[movements[9].Id]);
        Assert.Equal("Списание по услуге", titles[movements[10].Id]);
        Assert.Equal("Корректировка остатка", titles[movements[11].Id]);
        Assert.Equal("Складская операция", titles[movements[12].Id]);
        Assert.Equal("Услуга: акт #42 от 02.05.2026, Автомобиль клиента · А123АА154", titles[movements[13].Id]);
        Assert.Equal("contact@example.invalid", inventory.RecentMovements.Single(item => item.Id == movements[0].Id).CreatedByDisplayName);
        Assert.Equal("worker-name", inventory.RecentMovements.Single(item => item.Id == movements[1].Id).CreatedByDisplayName);
        Assert.Equal("+7 000 000-00-01", inventory.RecentMovements.Single(item => item.Id == movements[2].Id).CreatedByDisplayName);
        Assert.Equal(userWithId.Id, inventory.RecentMovements.Single(item => item.Id == movements[3].Id).CreatedByDisplayName);
        Assert.Null(inventory.RecentMovements.Single(item => item.Id == movements[4].Id).CreatedByDisplayName);
        Assert.Equal($"#maintenance/{maintenanceWithVehicle.Id}", inventory.RecentMovements.Single(item => item.Id == movements[5].Id).SourceUrl);
        Assert.Equal($"#maintenance/{maintenanceWithoutVehicleName.Id}", inventory.RecentMovements.Single(item => item.Id == movements[6].Id).SourceUrl);
        Assert.Equal($"#maintenance/{movements[7].SourceEntityId}", inventory.RecentMovements.Single(item => item.Id == movements[7].Id).SourceUrl);
        Assert.Null(inventory.RecentMovements.Single(item => item.Id == movements[8].Id).SourceUrl);
        Assert.Equal($"#maintenance/{maintenanceWithVehicle.Id}", inventory.RecentMovements.Single(item => item.Id == movements[13].Id).SourceUrl);

        var emptyInitiatorInventory = await service.GetAsync(emptyInitiatorUser.Id, emptyInitiatorOrganization.Id);
        Assert.Null(Assert.Single(emptyInitiatorInventory.RecentMovements).CreatedByDisplayName);
    }

    private static OrganizationMaterialMovement CreateMovement(
        OrganizationMaterialStock stock,
        string typeCode,
        string? sourceEntityType,
        string? createdByUserId,
        Guid? sourceEntityId = null,
        Guid? maintenanceId = null)
    {
        return new OrganizationMaterialMovement
        {
            Organization = stock.Organization,
            Stock = stock,
            TypeCode = typeCode,
            Quantity = typeCode == OrganizationMaterialMovementTypeCodes.Issue ? -1 : 1,
            Unit = stock.Unit,
            MaintenanceId = maintenanceId,
            SourceEntityType = sourceEntityType,
            SourceEntityId = sourceEntityId,
            CreatedByUserId = createdByUserId
        };
    }
}
