using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class InventoryQueryTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Theory]
    [InlineData(null, 4)]
    [InlineData("low", 2)]
    [InlineData("out", 1)]
    [InlineData("ok", 1)]
    [InlineData("unknown", 4)]
    public async Task StockFiltersPreserveOrganizationIsolationAndUnfilteredTotals(string? stockFilter, int expectedCount)
    {
        await using var context = fixture.CreateContext();
        var organization = new Organization { Name = "Студия", OwnerUser = new ApplicationUser() };
        var foreign = new Organization { Name = "Другой склад", OwnerUser = new ApplicationUser() };
        context.AddRange(
            new OrganizationMaterialStock { Organization = organization, Name = "Есть", Unit = "мл", PackageQuantity = 500, QuantityOnHand = 10, ReservedQuantity = 2, MinQuantity = 3, InventoryValue = 100 },
            new OrganizationMaterialStock { Organization = organization, Name = "Мало", Unit = "кг", QuantityOnHand = 2, MinQuantity = 3, InventoryValue = 20 },
            new OrganizationMaterialStock { Organization = organization, Name = "Ровно минимум", Unit = "кг", QuantityOnHand = 3, MinQuantity = 3 },
            new OrganizationMaterialStock { Organization = organization, Name = "Нет", Unit = "legacy", QuantityOnHand = 0, MinQuantity = 1 },
            new OrganizationMaterialStock { Organization = foreign, Name = "Чужая", Unit = "шт", QuantityOnHand = 100, InventoryValue = 1000 });
        await context.SaveChangesAsync();
        IInventoryQueryService service = new MaterialInventoryQueryService(context, Mock.Of<IOrganizationSubscriptionService>());
        var result = await service.GetAsync(organization.OwnerUserId, organization.Id, null, stockFilter, null, 0, 1, -1, 1000);

        Assert.Equal(expectedCount, result.StocksTotalCount);
        Assert.Equal(expectedCount, result.Stocks.Count);
        Assert.Equal(4, result.AllStocksCount);
        Assert.Equal(15, result.TotalQuantityOnHand);
        Assert.Equal(120, result.TotalInventoryValue);
        Assert.Equal(2, result.LowStockCount);
        Assert.Equal(1, result.OutOfStockCount);
        Assert.Equal(1, result.StocksPage);
        Assert.Equal(5, result.StocksPageSize);
        Assert.Equal(100, result.MovementsPageSize);
        Assert.DoesNotContain(result.Stocks, item => item.Name == "Чужая");
        if (stockFilter is null or "unknown")
        {
            Assert.Equal(8, result.Stocks.Single(item => item.Name == "Есть").AvailableQuantity);
            Assert.Equal(500, result.Stocks.Single(item => item.Name == "Есть").PackageQuantity);
            Assert.Equal("шт", result.Stocks.Single(item => item.Name == "Нет").Unit);
            Assert.False(result.Stocks.Single(item => item.Name == "Нет").IsLowStock);
            Assert.True(result.Stocks.Single(item => item.Name == "Мало").IsLowStock);
            Assert.True(result.Stocks.Single(item => item.Name == "Ровно минимум").IsLowStock);
        }
    }

    [Fact]
    public async Task OrganizationStockCountsIgnoreSearchFiltersAndPages()
    {
        await using var context = fixture.CreateContext();
        var organization = new Organization { Name = "Студия", OwnerUser = new ApplicationUser() };
        context.AddRange(
            new OrganizationMaterialStock { Organization = organization, Name = "Очиститель", Unit = "л", QuantityOnHand = 2, MinQuantity = 3 },
            new OrganizationMaterialStock { Organization = organization, Name = "Паста", Unit = "кг", QuantityOnHand = 0, MinQuantity = 1 },
            new OrganizationMaterialStock { Organization = organization, Name = "Полироль", Unit = "л", QuantityOnHand = 8, MinQuantity = 2 });
        await context.SaveChangesAsync();
        var service = new MaterialInventoryQueryService(context, Mock.Of<IOrganizationSubscriptionService>());

        var result = await service.GetAsync(organization.OwnerUserId, organization.Id, "Очиститель", "low", null, 2, 5, 1, 5);

        Assert.Empty(result.Stocks);
        Assert.Equal(3, result.AllStocksCount);
        Assert.Equal(1, result.StocksTotalCount);
        Assert.Equal(1, result.LowStockCount);
        Assert.Equal(1, result.OutOfStockCount);
    }

    [Fact]
    public async Task EmptyWarehouseAndOutOfRangePagesReturnEmptyCollections()
    {
        await using var context = fixture.CreateContext();
        var service = new MaterialInventoryQueryService(context, Mock.Of<IOrganizationSubscriptionService>());
        var result = await service.GetAsync("unknown", Guid.NewGuid());
        Assert.Empty(result.Stocks);
        Assert.Empty(result.RecentMovements);
        Assert.Empty(result.SnapshotStocks);
        Assert.Equal(0, result.TotalQuantityOnHand);
        Assert.Equal(0, result.TotalInventoryValue);
        Assert.Equal(100, result.StocksPageSize);
        var snapshot = await service.GetAsync("unknown", Guid.NewGuid(), " absent ", "low", "receipt", 99, 5, 99, 5,
            DateTime.SpecifyKind(new DateTime(2026, 1, 1), DateTimeKind.Unspecified),
            DateTime.SpecifyKind(new DateTime(2026, 1, 2), DateTimeKind.Local),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Guid.NewGuid());

        Assert.Empty(snapshot.Stocks);
        Assert.Empty(snapshot.RecentMovements);
        Assert.Empty(snapshot.SnapshotStocks);
        Assert.Equal(DateTimeKind.Utc, snapshot.SnapshotAt!.Value.Kind);
    }

    [Fact]
    public async Task MovementFiltersAndHistoricalSnapshotUsePersistedBalances()
    {
        await using var context = fixture.CreateContext();
        var organization = new Organization { Name = "Студия", OwnerUser = new ApplicationUser() };
        var stock = new OrganizationMaterialStock
        {
            Organization = organization, Name = "Очиститель", Unit = "мл",
            QuantityOnHand = 8, AverageUnitCost = 10, InventoryValue = 80,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var receiptDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var issueDate = receiptDate.AddDays(1);
        context.AddRange(
            new OrganizationMaterialMovement
            {
                Organization = organization, Stock = stock, TypeCode = OrganizationMaterialMovementTypeCodes.Receipt,
                Quantity = 10, Unit = "мл", UnitCost = 10, TotalCost = 100,
                QuantityBalanceAfter = 10, CostBalanceAfter = 100, CreatedAt = receiptDate,
                CreatedByUserId = organization.OwnerUser.Id, Comment = "Поставка"
            },
            new OrganizationMaterialMovement
            {
                Organization = organization, Stock = stock, TypeCode = OrganizationMaterialMovementTypeCodes.Issue,
                Quantity = -2, Unit = "мл", UnitCost = 10, TotalCost = 20,
                QuantityBalanceAfter = 8, CostBalanceAfter = 80, CreatedAt = issueDate
            });
        await context.SaveChangesAsync();
        var service = new MaterialInventoryQueryService(context, Mock.Of<IOrganizationSubscriptionService>());
        var result = await service.GetAsync(organization.OwnerUserId, organization.Id, " Поставка ", null, "receipt", 1, 10, 1, 10,
            receiptDate, receiptDate, receiptDate, stock.Id);

        Assert.Empty(result.Stocks);
        var movement = Assert.Single(result.RecentMovements);
        Assert.Equal(10, movement.BalanceAfter);
        Assert.Equal(100, movement.CostBalanceAfter);
        Assert.Equal("Поставка", movement.Comment);
        Assert.Equal(100, result.SnapshotTotalInventoryValue);
        var snapshot = Assert.Single(result.SnapshotStocks);
        Assert.Equal(10, snapshot.QuantityOnHand);
        Assert.Equal(10, snapshot.AverageUnitCost);
        Assert.Equal("мл", snapshot.Unit);
    }

    [Fact]
    public async Task AccessDenialStopsQueryBeforeDatabaseAccess()
    {
        var access = new Mock<IOrganizationSubscriptionService>();
        var organizationId = Guid.NewGuid();
        access.Setup(item => item.EnsureWorkspaceCanReadAsync("user", organizationId, OrganizationPermissionCodes.MaterialsView))
            .ThrowsAsync(new UnauthorizedAccessException("Нет доступа"));
        await using var context = fixture.CreateContext();
        var service = new MaterialInventoryQueryService(context, access.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync("user", organizationId));
        access.VerifyAll();
    }
}
