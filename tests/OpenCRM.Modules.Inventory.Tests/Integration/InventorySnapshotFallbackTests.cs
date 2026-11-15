using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class InventorySnapshotFallbackTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task SnapshotUsesHistoricalFallbackAndSortsByNameThenUnit()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка снимка", OwnerUser = user };
        var gramStock = new OrganizationMaterialStock
        {
            Organization = organization,
            Name = "Материал",
            Unit = "г",
            QuantityOnHand = 10,
            AverageUnitCost = 2,
            InventoryValue = 20
        };
        var literStock = new OrganizationMaterialStock
        {
            Organization = organization,
            Name = "Материал",
            Unit = "л",
            QuantityOnHand = 5,
            AverageUnitCost = 3,
            InventoryValue = 15
        };
        var futureMovementStock = new OrganizationMaterialStock
        {
            Organization = organization,
            Name = "Материал",
            Unit = "шт",
            QuantityOnHand = 7,
            AverageUnitCost = 4,
            InventoryValue = 28
        };
        var snapshotAt = DateTime.UtcNow.AddDays(-1);
        context.AddRange(gramStock, literStock, futureMovementStock);
        context.OrganizationMaterialMovements.Add(new OrganizationMaterialMovement
        {
            Organization = organization,
            Stock = futureMovementStock,
            TypeCode = OrganizationMaterialMovementTypeCodes.Receipt,
            Quantity = 7,
            QuantityBalanceAfter = 7,
            CostBalanceAfter = 28,
            CreatedAt = snapshotAt.AddDays(1)
        });
        await context.SaveChangesAsync();
        var service = new MaterialInventoryQueryService(context, Mock.Of<IOrganizationSubscriptionService>());

        var result = await service.GetAsync(
            user.Id,
            organization.Id,
            null,
            null,
            null,
            1,
            1000,
            1,
            30,
            snapshotAt: snapshotAt);

        Assert.Equal(new[] { "г", "л", "шт" }, result.SnapshotStocks.Select(item => item.Unit));
        Assert.Equal(new[] { 10m, 5m, 0m }, result.SnapshotStocks.Select(item => item.QuantityOnHand));
        Assert.Equal(new[] { 2m, 3m, 0m }, result.SnapshotStocks.Select(item => item.AverageUnitCost));
        Assert.Equal(new[] { 20m, 15m, 0m }, result.SnapshotStocks.Select(item => item.InventoryValue));
    }
}
