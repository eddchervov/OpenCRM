using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class InventoryConcurrentReadTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task ConcurrentMovementDeletionPreservesThePreviouslyLoadedHistoricalBalance()
    {
        await using var seed = fixture.CreateContext();
        var owner = new ApplicationUser();
        var organization = new Organization { OwnerUser = owner, Name = "Исторический остаток" };
        var stock = new OrganizationMaterialStock
        {
            Organization = organization, Name = "Паста", Unit = "шт", QuantityOnHand = 9,
            AverageUnitCost = 10, InventoryValue = 90
        };
        var movement = new OrganizationMaterialMovement
        {
            Organization = organization, Stock = stock, Quantity = 5, QuantityBalanceAfter = 5,
            Unit = "шт", UnitCost = 10, TotalCost = 50, CreatedAt = DateTime.UtcNow
        };
        seed.OrganizationMaterialMovements.Add(movement);
        await seed.SaveChangesAsync();
        var removal = new RemoveMovementBeforeBalanceLookup(fixture, movement.Id);
        await using var context = fixture.CreateContext(removal);
        using var provider = new ServiceCollection().AddSingleton(context)
            .AddSingleton(Mock.Of<IOrganizationSubscriptionService>()).AddOpenCRMCatalog().AddOpenCRMInventory().BuildServiceProvider();
        var result = await provider.GetRequiredService<IInventoryQueryService>().GetAsync(owner.Id, organization.Id);

        Assert.True(removal.Removed);
        Assert.Equal(5, Assert.Single(result.RecentMovements).BalanceAfter);
        Assert.Equal(9, Assert.Single(result.Stocks).QuantityOnHand);
        Assert.False(await seed.OrganizationMaterialMovements.AnyAsync(item => item.Id == movement.Id));
    }

    private sealed class RemoveMovementBeforeBalanceLookup(InventoryDatabaseFixture fixture, Guid movementId) : DbCommandInterceptor
    {
        internal bool Removed { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!this.Removed && command.CommandText.Contains("oldestDate", StringComparison.Ordinal))
            {
                await using var concurrent = fixture.CreateContext();
                await concurrent.OrganizationMaterialMovements.Where(item => item.Id == movementId)
                    .ExecuteDeleteAsync(cancellationToken);
                this.Removed = true;
            }

            return result;
        }
    }
}
