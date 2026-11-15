using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class StockCommandGuardTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task ReceiptRejectsInvalidQuantitiesAndPackageSizes()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = user };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);
        var invalidRequests = new[]
        {
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = 0 },
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = -1 },
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = 0.0001m },
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = 999_999_999.001m },
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = 1, PackageQuantity = 0 },
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = 1, PackageQuantity = -1 },
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = 1, PackageQuantity = 999_999_999.001m },
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = 1, PackageQuantity = 0.0001m },
            new CreateOrganizationMaterialReceiptDto { Name = new string('Я', 161), Quantity = 1 },
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = 1, Comment = new string('Я', 1001) },
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = 1, UnitCost = -1 },
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = 1, UnitCost = 1_000_000_000 },
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Quantity = 1, UnitCost = 0.00001m }
        };

        foreach (var request in invalidRequests)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AddReceiptAsync(user.Id, organization.Id, request));
        }

        Assert.Empty(await context.OrganizationMaterialStocks
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync());
        Assert.Empty(await context.OrganizationMaterialMovements
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task ReceiptRejectsQuantityThatExceedsLimitAfterUnitConversion()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = user };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AddReceiptAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialReceiptDto
            {
                Name = "Крупный приход",
                Unit = "кг",
                Quantity = 1_000_000,
                UnitCost = 1
            }));

        Assert.Empty(await context.OrganizationMaterialStocks.Where(item => item.OrganizationId == organization.Id).ToListAsync());
        Assert.Empty(await context.OrganizationMaterialMovements.Where(item => item.OrganizationId == organization.Id).ToListAsync());
    }

    [Fact]
    public async Task ReceiptRejectsMissingNamesWrongCatalogKindsAndInvalidUnits()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = user };
        var serviceItem = new OrganizationScenarioCatalogItem
        {
            Organization = organization,
            Name = "Услуга",
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Code = "service",
            ScenarioCode = "detailing"
        };
        context.OrganizationScenarioCatalogItems.Add(serviceItem);
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AddReceiptAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialReceiptDto { Quantity = 1, Name = null }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AddReceiptAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialReceiptDto { Quantity = 1, Name = " " }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AddReceiptAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialReceiptDto { Quantity = 1, CatalogItemId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AddReceiptAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialReceiptDto { Quantity = 1, CatalogItemId = serviceItem.Id }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AddReceiptAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialReceiptDto { Quantity = 1, Name = "Состав", Unit = "ведро" }));
        Assert.Empty(await context.OrganizationMaterialStocks
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task ReceiptReusesCatalogAndLegacyStocksAndKeepsPackageSizeFixed()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = user };
        var material = new OrganizationScenarioCatalogItem
        {
            Organization = organization,
            Name = "Шампунь",
            KindCode = OrganizationScenarioCatalogKindCodes.Material,
            Code = "shampoo",
            ScenarioCode = "detailing"
        };
        var legacyStock = new OrganizationMaterialStock
        {
            Organization = organization, Name = "Порошок", Unit = "кг", QuantityOnHand = 2,
            AverageUnitCost = 10, InventoryValue = 20
        };
        context.AddRange(material, legacyStock);
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);
        var catalogStock = await inventory.Stock.AddReceiptAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialReceiptDto
            {
                CatalogItemId = material.Id, Unit = "л", Quantity = 1, UnitCost = 100, PackageQuantity = 500
            });
        var reusedCatalogStock = await inventory.Stock.AddReceiptAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialReceiptDto { CatalogItemId = material.Id, Unit = "л", Quantity = 1, UnitCost = 200 });
        var reusedLegacyStock = await inventory.Stock.AddReceiptAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialReceiptDto { Name = "Порошок", Unit = "г", Quantity = 1000, UnitCost = 10 });

        Assert.Equal(catalogStock.Id, reusedCatalogStock.Id);
        Assert.Equal(500, reusedCatalogStock.PackageQuantity);
        Assert.Equal(3, reusedLegacyStock.QuantityOnHand);
        Assert.Equal("кг", reusedLegacyStock.Unit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AddReceiptAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialReceiptDto
            {
                CatalogItemId = material.Id, Unit = "л", Quantity = 1, PackageQuantity = 600
            }));
        Assert.Equal(2, await context.OrganizationMaterialStocks.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task AdjustmentAndIssueRejectInvalidBalancesAndCosts()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = user };
        var reservedStock = new OrganizationMaterialStock
        {
            Organization = organization, Name = "Зарезервированный", Unit = "шт",
            QuantityOnHand = 5, ReservedQuantity = 2
        };
        context.AddRange(organization, reservedStock);
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);
        var stock = await inventory.Stock.AddReceiptAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialReceiptDto { Name = "Состав", Unit = "шт", Quantity = 3, UnitCost = 10 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AdjustAsync(user.Id, organization.Id, stock.Id,
            new AdjustOrganizationMaterialStockDto { ExpectedUpdatedAt = stock.UpdatedAt, QuantityOnHand = -1, Comment = "Пересчет" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AdjustAsync(user.Id, organization.Id, stock.Id,
            new AdjustOrganizationMaterialStockDto { ExpectedUpdatedAt = stock.UpdatedAt, QuantityOnHand = 1_000_000_000, Comment = "Пересчет" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AdjustAsync(user.Id, organization.Id, Guid.NewGuid(),
            new AdjustOrganizationMaterialStockDto { ExpectedUpdatedAt = stock.UpdatedAt, QuantityOnHand = 1, Comment = "Пересчет" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AdjustAsync(user.Id, organization.Id, reservedStock.Id,
            new AdjustOrganizationMaterialStockDto { ExpectedUpdatedAt = stock.UpdatedAt, QuantityOnHand = 1, Comment = "Пересчет" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AdjustAsync(user.Id, organization.Id, stock.Id,
            new AdjustOrganizationMaterialStockDto { ExpectedUpdatedAt = stock.UpdatedAt, QuantityOnHand = 1 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AdjustAsync(user.Id, organization.Id, stock.Id,
            new AdjustOrganizationMaterialStockDto { ExpectedUpdatedAt = stock.UpdatedAt, QuantityOnHand = 1, Comment = "Пересчет", AverageUnitCost = -1 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AdjustAsync(user.Id, organization.Id, stock.Id,
            new AdjustOrganizationMaterialStockDto { ExpectedUpdatedAt = stock.UpdatedAt, QuantityOnHand = 1.5m, Comment = "Пересчет" }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.IssueAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = 0 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.IssueAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = -1 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.IssueAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = 1_000_000_000 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.IssueAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialIssueDto { StockId = Guid.NewGuid(), Quantity = 1 }));

        var fractionalStock = new OrganizationMaterialStock
        {
            Organization = organization, Name = "Жидкий состав", Unit = "мл",
            QuantityOnHand = 5, ReservedQuantity = 2
        };
        context.OrganizationMaterialStocks.Add(fractionalStock);
        await context.SaveChangesAsync();
        var movementCount = await context.OrganizationMaterialMovements.CountAsync(item => item.OrganizationId == organization.Id);
        var invalidAdjustments = new[]
        {
            new AdjustOrganizationMaterialStockDto { ExpectedUpdatedAt = fractionalStock.UpdatedAt, QuantityOnHand = 3.0001m, Comment = "Пересчёт" },
            new AdjustOrganizationMaterialStockDto { ExpectedUpdatedAt = fractionalStock.UpdatedAt, QuantityOnHand = 3, Comment = new string('Я', 1001) },
            new AdjustOrganizationMaterialStockDto { ExpectedUpdatedAt = fractionalStock.UpdatedAt, QuantityOnHand = 3, AverageUnitCost = 1_000_000_000, Comment = "Пересчёт" },
            new AdjustOrganizationMaterialStockDto { ExpectedUpdatedAt = fractionalStock.UpdatedAt, QuantityOnHand = 3, AverageUnitCost = 0.000000001m, Comment = "Пересчёт" }
        };

        foreach (var request in invalidAdjustments)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AdjustAsync(user.Id, organization.Id, fractionalStock.Id, request));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.IssueAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialIssueDto { StockId = fractionalStock.Id, Quantity = 0.0001m }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.IssueAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialIssueDto { StockId = fractionalStock.Id, Quantity = 4 }));
        Assert.Equal(5, (await context.OrganizationMaterialStocks.AsNoTracking().SingleAsync(item => item.Id == fractionalStock.Id)).QuantityOnHand);
        Assert.Equal(movementCount, await context.OrganizationMaterialMovements.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task AdjustmentUsesOptionalCostAndMinimumAndIssueRejectsLongComments()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = user };
        var stock = new OrganizationMaterialStock
        {
            Organization = organization,
            Name = "Состав",
            Unit = "шт",
            QuantityOnHand = 3,
            MinQuantity = 2,
            AverageUnitCost = 10,
            InventoryValue = 30
        };
        context.OrganizationMaterialStocks.Add(stock);
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);

        var adjusted = await inventory.Stock.AdjustAsync(user.Id, organization.Id, stock.Id,
            new AdjustOrganizationMaterialStockDto { QuantityOnHand = 4, MinQuantity = -1, ExpectedUpdatedAt = stock.UpdatedAt, Comment = "Пересчет" });
        Assert.Equal(10, adjusted.AverageUnitCost);
        Assert.Equal(0, adjusted.MinQuantity);
        Assert.False(adjusted.IsLowStock);
        Assert.False(adjusted.IsOutOfStock);

        adjusted = await inventory.Stock.AdjustAsync(user.Id, organization.Id, stock.Id,
            new AdjustOrganizationMaterialStockDto { QuantityOnHand = 5, AverageUnitCost = 20, ExpectedUpdatedAt = adjusted.UpdatedAt, Comment = "Пересчет" });
        Assert.Equal(20, adjusted.AverageUnitCost);
        Assert.Equal(0, adjusted.MinQuantity);

        adjusted = await inventory.Stock.AdjustAsync(user.Id, organization.Id, stock.Id,
            new AdjustOrganizationMaterialStockDto { QuantityOnHand = 5, AverageUnitCost = 1.12345678m, ExpectedUpdatedAt = adjusted.UpdatedAt, Comment = "Пересчёт" });
        Assert.Equal(1.12345678m, adjusted.AverageUnitCost);
        Assert.Equal(1.12345678m, (await context.OrganizationMaterialStocks.AsNoTracking()
            .SingleAsync(item => item.Id == stock.Id)).AverageUnitCost);

        adjusted = await inventory.Stock.AdjustAsync(user.Id, organization.Id, stock.Id,
            new AdjustOrganizationMaterialStockDto { QuantityOnHand = 1, MinQuantity = 1, ExpectedUpdatedAt = adjusted.UpdatedAt, Comment = "Пересчет" });
        Assert.True(adjusted.IsLowStock);
        Assert.False(adjusted.IsOutOfStock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.IssueAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = 1, Comment = new string('Я', 1001) }));
        Assert.Equal(1, (await context.OrganizationMaterialStocks.AsNoTracking().SingleAsync(item => item.Id == stock.Id)).QuantityOnHand);
        Assert.Empty(await context.OrganizationMaterialMovements.Where(item => item.StockId == stock.Id
            && item.TypeCode == OrganizationMaterialMovementTypeCodes.Issue).ToListAsync());
        var openedAt = adjusted.UpdatedAt;
        await Task.Delay(10);
        var issued = await inventory.Stock.IssueAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = 1, Comment = new string('Я', 1000) });
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Stock.AdjustAsync(user.Id, organization.Id, stock.Id,
            new AdjustOrganizationMaterialStockDto
            {
                QuantityOnHand = adjusted.QuantityOnHand,
                ExpectedUpdatedAt = openedAt,
                Comment = "Устаревшая корректировка"
            }));
        Assert.Equal(0, (await context.OrganizationMaterialStocks.AsNoTracking().SingleAsync(item => item.Id == stock.Id)).QuantityOnHand);
        Assert.True(issued.IsLowStock);
        Assert.True(issued.IsOutOfStock);
        Assert.Equal(1000, (await context.OrganizationMaterialMovements
            .SingleAsync(item => item.OrganizationId == organization.Id && item.TypeCode == OrganizationMaterialMovementTypeCodes.Issue))
            .Comment!.Length);
    }

    private static InventoryTestScope CreateInventory(OpenCRMContext context)
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddScoped(_ => context)
            .AddScoped(_ => Mock.Of<IOrganizationSubscriptionService>())
            .AddOpenCRMCatalog()
            .AddOpenCRMInventory()
            .BuildServiceProvider();

        return new InventoryTestScope(provider);
    }

    private sealed class InventoryTestScope(ServiceProvider provider) : IDisposable
    {
        private readonly IServiceScope _scope = provider.CreateScope();

        public IStockCommandService Stock => this._scope.ServiceProvider.GetRequiredService<IStockCommandService>();

        public void Dispose()
        {
            this._scope.Dispose();
            provider.Dispose();
        }
    }
}
