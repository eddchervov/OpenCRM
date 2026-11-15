using OpenCRM.Testing;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Inventory.Commands;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class MaintenanceInventoryAtomicityTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task InsufficientMaterialRollsBackNewOrderDetailsAndHistory()
    {
        await using var context = fixture.CreateContext();
        var vehicle = await MaintenanceInventoryAtomicityTests.SeedAsync(context);
        using var provider = MaintenanceInventoryAtomicityTests.CreateInventory(context);
        await MaintenanceInventoryAtomicityTests.ReceiveAsync(provider, vehicle);
        var service = MaintenanceInventoryAtomicityTests.CreateMaintenanceService(context, provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(vehicle.UserId, vehicle.OrganizationId, vehicle.Id,
            MaintenanceInventoryAtomicityTests.CreateRequest(6)));
        Assert.Contains("Недостаточно материала", exception.Message);
        context.ChangeTracker.Clear();

        Assert.False(await context.Maintenance.AnyAsync(item => item.VehicleId == vehicle.Id));
        Assert.False(await context.MaintenanceDetails.AnyAsync(detail => context.Maintenance.Any(item =>
            item.Id == detail.MaintenanceId && item.VehicleId == vehicle.Id)));
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(item => item.VehicleId == vehicle.Id));
        Assert.Equal(5, (await context.OrganizationMaterialStocks.SingleAsync(item => item.OrganizationId == vehicle.OrganizationId)).QuantityOnHand);
        Assert.Equal(1, await context.OrganizationMaterialMovements.CountAsync(item => item.OrganizationId == vehicle.OrganizationId));
    }

    [Fact]
    public async Task InsufficientMaterialRollsBackOrderEditAndPreservesSavedCosts()
    {
        await using var context = fixture.CreateContext();
        var vehicle = await MaintenanceInventoryAtomicityTests.SeedAsync(context);
        using var provider = MaintenanceInventoryAtomicityTests.CreateInventory(context);
        await MaintenanceInventoryAtomicityTests.ReceiveAsync(provider, vehicle);
        var service = MaintenanceInventoryAtomicityTests.CreateMaintenanceService(context, provider);
        var id = await service.Create(vehicle.UserId, vehicle.OrganizationId, vehicle.Id, MaintenanceInventoryAtomicityTests.CreateRequest(1));
        var work = await context.Maintenance.SingleAsync(item => item.Id == id);
        var version = work.Version;
        var historyCount = await context.MaintenanceHistoryEvents.CountAsync(item => item.MaintenanceId == id);
        var detailId = work.Details.Single(detail => detail.MaterialQuantity.HasValue).DetailId;
        var request = new UpdateMaintenanceDto
        {
            Status = MaintenanceStatus.Planned, Type = MaintenanceType.Maintenance,
            PlannedDate = new DateTime(2026, 10, 10), CompletedDate = null, Location = null, Performer = null,
            Mileage = null, Comment = "Изменение, которое должно откатиться",
            Details =
            [
                new CreateMaintenanceDetailDto { Name = "Работа", Price = 100 },
                new CreateMaintenanceDetailDto { DetailId = detailId, Name = "Раствор", MaterialName = "Раствор", MaterialUnit = "шт", MaterialQuantity = 6 }
            ]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Update(vehicle.UserId, vehicle.OrganizationId, id, request));
        Assert.Contains("Недостаточно материала", exception.Message);
        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(item => item.Id == id);

        Assert.Null(saved.Comment);
        Assert.Equal(version, saved.Version);
        Assert.Equal(10, saved.MaterialCostAmount);
        var detail = saved.Details.Single(detail => detail.MaterialQuantity.HasValue);
        Assert.Equal(1, detail.MaterialQuantity);
        Assert.Equal(10, detail.MaterialUnitCost);
        Assert.Equal(10, detail.MaterialTotalCost);
        Assert.Equal(historyCount, await context.MaintenanceHistoryEvents.CountAsync(item => item.MaintenanceId == id));
        Assert.Equal(4, (await context.OrganizationMaterialStocks.SingleAsync(item => item.OrganizationId == vehicle.OrganizationId)).QuantityOnHand);
    }

    [Fact]
    public async Task DeletingOrderReturnsMaterialAndKeepsHistoricalLineCosts()
    {
        await using var context = fixture.CreateContext();
        var vehicle = await MaintenanceInventoryAtomicityTests.SeedAsync(context);
        using var provider = MaintenanceInventoryAtomicityTests.CreateInventory(context);
        await MaintenanceInventoryAtomicityTests.ReceiveAsync(provider, vehicle);
        var service = MaintenanceInventoryAtomicityTests.CreateMaintenanceService(context, provider);
        var id = await service.Create(vehicle.UserId, vehicle.OrganizationId, vehicle.Id, MaintenanceInventoryAtomicityTests.CreateRequest(2));

        await service.Delete(vehicle.UserId, vehicle.OrganizationId, id);
        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(item => item.Id == id);
        var stock = await context.OrganizationMaterialStocks.SingleAsync(item => item.OrganizationId == vehicle.OrganizationId);

        Assert.True(saved.IsDeleted);
        Assert.Equal(0, saved.MaterialCostAmount);
        Assert.Equal(20, saved.Details.Single(detail => detail.MaterialQuantity.HasValue).MaterialTotalCost);
        Assert.Equal(5, stock.QuantityOnHand);
        Assert.Equal(50, stock.InventoryValue);
        var movements = await context.OrganizationMaterialMovements.Where(item => item.MaintenanceId == id).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Equal(0, movements.Sum(item => item.Quantity));
        Assert.Equal(0, movements.Sum(item => item.Quantity < 0 ? item.TotalCost : -item.TotalCost));
    }

    [Fact]
    public async Task RepeatedOrderEditIgnoresSubmittedCostWithoutCreatingHistoryOrMovements()
    {
        await using var context = fixture.CreateContext();
        var vehicle = await MaintenanceInventoryAtomicityTests.SeedAsync(context);
        using var provider = MaintenanceInventoryAtomicityTests.CreateInventory(context);
        await MaintenanceInventoryAtomicityTests.ReceiveAsync(provider, vehicle);
        var service = MaintenanceInventoryAtomicityTests.CreateMaintenanceService(context, provider);
        var id = await service.Create(vehicle.UserId, vehicle.OrganizationId, vehicle.Id, MaintenanceInventoryAtomicityTests.CreateRequest(1));
        var saved = await context.Maintenance.SingleAsync(item => item.Id == id);
        var version = saved.Version;
        var historyCount = await context.MaintenanceHistoryEvents.CountAsync(item => item.MaintenanceId == id);
        var movementCount = await context.OrganizationMaterialMovements.CountAsync(item => item.MaintenanceId == id);
        var request = new UpdateMaintenanceDto
        {
            Status = saved.Status, Type = saved.Type, PlannedDate = saved.PlannedDate,
            CompletedDate = saved.CompletedDate, Location = saved.Location, Performer = saved.Performer,
            Mileage = saved.Mileage, Comment = saved.Comment,
            Details = saved.Details.Select(detail => new CreateMaintenanceDetailDto
            {
                DetailId = detail.DetailId, Name = detail.Name, Price = detail.Price,
                MaterialName = detail.MaterialName, MaterialUnit = detail.MaterialUnit,
                MaterialQuantity = detail.MaterialQuantity, MaterialUnitCost = 999, MaterialTotalCost = 999
            }).ToArray()
        };

        await service.Update(vehicle.UserId, vehicle.OrganizationId, id, request);
        context.ChangeTracker.Clear();
        var after = await context.Maintenance.SingleAsync(item => item.Id == id);

        Assert.Equal(version, after.Version);
        Assert.Equal(10, after.MaterialCostAmount);
        Assert.Equal(10, after.Details.Single(detail => detail.MaterialQuantity.HasValue).MaterialUnitCost);
        Assert.Equal(10, after.Details.Single(detail => detail.MaterialQuantity.HasValue).MaterialTotalCost);
        Assert.Equal(historyCount, await context.MaintenanceHistoryEvents.CountAsync(item => item.MaintenanceId == id));
        Assert.Equal(movementCount, await context.OrganizationMaterialMovements.CountAsync(item => item.MaintenanceId == id));
    }

    [Fact]
    public async Task CallerCanRollBackOrderAndConsumptionTogether()
    {
        await using var context = fixture.CreateContext();
        var vehicle = await MaintenanceInventoryAtomicityTests.SeedAsync(context);
        using var provider = MaintenanceInventoryAtomicityTests.CreateInventory(context);
        await MaintenanceInventoryAtomicityTests.ReceiveAsync(provider, vehicle);
        var service = MaintenanceInventoryAtomicityTests.CreateMaintenanceService(context, provider);
        Guid id = Guid.Empty;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            id = await service.Create(vehicle.UserId, vehicle.OrganizationId, vehicle.Id, MaintenanceInventoryAtomicityTests.CreateRequest(1));
            Assert.Equal(10, (await context.Maintenance.SingleAsync(item => item.Id == id)).MaterialCostAmount);
            await transaction.RollbackAsync();
        });
        context.ChangeTracker.Clear();

        Assert.False(await context.Maintenance.AnyAsync(item => item.Id == id));
        Assert.False(await context.OrganizationMaterialMovements.AnyAsync(item => item.MaintenanceId == id));
        Assert.Equal(5, (await context.OrganizationMaterialStocks.SingleAsync(item => item.OrganizationId == vehicle.OrganizationId)).QuantityOnHand);
    }

    [Fact]
    public async Task ConcurrentIssuesCannotSpendTheSameAvailableStockTwice()
    {
        await using var context = fixture.CreateContext();
        var vehicle = await MaintenanceInventoryAtomicityTests.SeedAsync(context);
        using var provider = MaintenanceInventoryAtomicityTests.CreateInventory(context);
        await MaintenanceInventoryAtomicityTests.ReceiveAsync(provider, vehicle);
        var stock = await context.OrganizationMaterialStocks.SingleAsync(item => item.OrganizationId == vehicle.OrganizationId);

        async Task<Exception?> IssueAsync()
        {
            await using var commandContext = fixture.CreateContext();
            using var commandProvider = MaintenanceInventoryAtomicityTests.CreateInventory(commandContext);

            return await Record.ExceptionAsync(() => commandProvider.GetRequiredService<IStockCommandService>()
                .IssueAsync(vehicle.UserId, vehicle.OrganizationId!.Value,
                    new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = 4 }));
        }

        var errors = await Task.WhenAll(IssueAsync(), IssueAsync());
        Assert.Single(errors, error => error == null);
        var rejected = Assert.Single(errors, error => error != null);
        Assert.IsType<InvalidOperationException>(rejected);
        Assert.Contains("доступного остатка", rejected!.Message);
        await context.Entry(stock).ReloadAsync();

        Assert.Equal(1, stock.QuantityOnHand);
        Assert.Equal(10, stock.InventoryValue);
        Assert.Equal(2, await context.OrganizationMaterialMovements.CountAsync(item => item.StockId == stock.Id));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConcurrentReceiptsPreserveBothMovementsAndTheResultingValue(bool hasStock, bool usesCatalog)
    {
        await using var context = fixture.CreateContext();
        var vehicle = await MaintenanceInventoryAtomicityTests.SeedAsync(context);
        using var provider = MaintenanceInventoryAtomicityTests.CreateInventory(context);
        Guid? catalogItemId = null;
        if (usesCatalog)
        {
            var material = new OrganizationScenarioCatalogItem
            {
                OrganizationId = vehicle.OrganizationId!.Value,
                Name = "Раствор", KindCode = OrganizationScenarioCatalogKindCodes.Material
            };
            context.OrganizationScenarioCatalogItems.Add(material);
            await context.SaveChangesAsync();
            catalogItemId = material.Id;
        }

        if (hasStock)
        {
            await provider.GetRequiredService<IStockCommandService>().AddReceiptAsync(
                vehicle.UserId, vehicle.OrganizationId!.Value,
                new CreateOrganizationMaterialReceiptDto
                {
                    CatalogItemId = catalogItemId, Name = "Раствор", Unit = "шт", Quantity = 5, UnitCost = 10
                });
        }

        var barrier = new OrganizationLockArrivalBarrier();

        async Task ReceiveAsync()
        {
            await using var commandContext = fixture.CreateContext(barrier);
            using var commandProvider = MaintenanceInventoryAtomicityTests.CreateInventory(commandContext);
            await commandProvider.GetRequiredService<IStockCommandService>().AddReceiptAsync(
                vehicle.UserId, vehicle.OrganizationId!.Value,
                new CreateOrganizationMaterialReceiptDto
                {
                    CatalogItemId = catalogItemId, Name = "Раствор", Unit = "шт", Quantity = 2, UnitCost = 10
                });
        }

        await Task.WhenAll(ReceiveAsync(), ReceiveAsync());
        context.ChangeTracker.Clear();
        var stock = await context.OrganizationMaterialStocks.SingleAsync(item => item.OrganizationId == vehicle.OrganizationId);

        Assert.Equal(hasStock ? 9 : 4, stock.QuantityOnHand);
        Assert.Equal(hasStock ? 90 : 40, stock.InventoryValue);
        Assert.Equal(hasStock ? 3 : 2, await context.OrganizationMaterialMovements.CountAsync(item => item.StockId == stock.Id));
    }

    [Fact]
    public async Task ConcurrentGetOrCreateCallsReuseTheStockAfterBothInitiallyReadNoStock()
    {
        await using var context = fixture.CreateContext();
        var vehicle = await MaintenanceInventoryAtomicityTests.SeedAsync(context);
        var barrier = new EmptyStockReadBarrier();

        async Task<Guid> GetOrCreateAsync()
        {
            await using var commandContext = fixture.CreateContext(barrier);
            using var commandProvider = MaintenanceInventoryAtomicityTests.CreateInventory(commandContext);

            return await commandContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await commandContext.Database.BeginTransactionAsync();
                var stock = await commandProvider.GetRequiredService<InventoryStockRepository>().GetOrCreateAsync(
                    vehicle.OrganizationId!.Value, null, "Одинаковый материал", "шт");
                await commandContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return stock.Id;
            });
        }

        var stockIds = await Task.WhenAll(GetOrCreateAsync(), GetOrCreateAsync());

        Assert.Equal(stockIds[0], stockIds[1]);
        Assert.Equal(3, barrier.EmptyReadCount);
        Assert.Equal(1, await context.OrganizationMaterialStocks.CountAsync(item => item.OrganizationId == vehicle.OrganizationId));
    }

    [Fact]
    public async Task RepeatedConsumptionChangesKeepHistoricalReturnCostsAndRestoreTheStock()
    {
        await using var context = fixture.CreateContext();
        var vehicle = await MaintenanceInventoryAtomicityTests.SeedAsync(context);
        using var provider = MaintenanceInventoryAtomicityTests.CreateInventory(context);
        await MaintenanceInventoryAtomicityTests.ReceiveAsync(provider, vehicle);
        var maintenance = new OpenCRM.Domain.Maintenance { UserId = vehicle.UserId, VehicleId = vehicle.Id, Date = DateTime.UtcNow };
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var consumption = provider.GetRequiredService<IMaintenanceConsumptionService>();
        var line = new MaintenanceMaterialConsumptionLine(Guid.NewGuid(), null, "Раствор", "Раствор", "шт", 2);
        var issued = await consumption.SynchronizeAsync(vehicle.UserId, vehicle.OrganizationId!.Value, maintenance.Id, [line]);
        var reduced = await consumption.SynchronizeAsync(vehicle.UserId, vehicle.OrganizationId.Value, maintenance.Id, [line with { Quantity = 1 }]);
        var increased = await consumption.SynchronizeAsync(vehicle.UserId, vehicle.OrganizationId.Value, maintenance.Id, [line]);
        var returned = await consumption.SynchronizeAsync(vehicle.UserId, vehicle.OrganizationId.Value, maintenance.Id, []);
        var stock = await context.OrganizationMaterialStocks.SingleAsync(item => item.OrganizationId == vehicle.OrganizationId);

        Assert.Equal(20, issued.TotalCost);
        Assert.Equal(10, reduced.TotalCost);
        Assert.Equal(20, increased.TotalCost);
        Assert.Equal(0, returned.TotalCost);
        Assert.Equal(5, stock.QuantityOnHand);
        Assert.Equal(50, stock.InventoryValue);
        Assert.Equal(0, await context.OrganizationMaterialMovements.Where(item => item.MaintenanceId == maintenance.Id)
            .SumAsync(item => item.Quantity));
    }

    [Fact]
    public async Task CreatingAnotherStockDoesNotBlockForeignKeysOfExistingStockIssue()
    {
        await using var context = fixture.CreateContext();
        var vehicle = await MaintenanceInventoryAtomicityTests.SeedAsync(context);
        using var provider = MaintenanceInventoryAtomicityTests.CreateInventory(context);
        await MaintenanceInventoryAtomicityTests.ReceiveAsync(provider, vehicle);
        var stock = await context.OrganizationMaterialStocks.SingleAsync(item => item.OrganizationId == vehicle.OrganizationId);
        var locks = new StockCreationLockBarrier();

        async Task IssueAsync()
        {
            await using var commandContext = fixture.CreateContext(locks);
            using var commandProvider = MaintenanceInventoryAtomicityTests.CreateInventory(commandContext);
            await commandProvider.GetRequiredService<IStockCommandService>().IssueAsync(
                vehicle.UserId, vehicle.OrganizationId!.Value,
                new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = 1 });
        }

        async Task CreateAndLockAsync()
        {
            await using var commandContext = fixture.CreateContext(locks);
            using var commandProvider = MaintenanceInventoryAtomicityTests.CreateInventory(commandContext);
            await commandContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await commandContext.Database.BeginTransactionAsync();
                await commandProvider.GetRequiredService<InventoryStockRepository>().GetOrCreateAsync(
                    vehicle.OrganizationId!.Value, null, "Другой материал", "шт");
                await commandContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""SELECT 1 FROM "OrganizationMaterialStocks" WHERE "Id" = {stock.Id} FOR UPDATE""");
                await commandContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });
        }

        var issue = IssueAsync();
        await locks.StockLocked.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.WhenAll(issue, CreateAndLockAsync());
        await context.Entry(stock).ReloadAsync();

        Assert.Equal(4, stock.QuantityOnHand);
        Assert.Equal(40, stock.InventoryValue);
        Assert.Equal(2, await context.OrganizationMaterialStocks.CountAsync(item => item.OrganizationId == vehicle.OrganizationId));
        Assert.Equal(2, await context.OrganizationMaterialMovements.CountAsync(item => item.OrganizationId == vehicle.OrganizationId));
        Assert.Equal(2, locks.StockLockCount);
        Assert.Equal(1, locks.OrganizationLockCount);
    }

    private sealed class StockCreationLockBarrier : DbCommandInterceptor
    {
        internal TaskCompletionSource StockLocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _organizationLocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stockLockCount;
        private int _organizationLockCount;

        internal int StockLockCount => Volatile.Read(ref this._stockLockCount);

        internal int OrganizationLockCount => Volatile.Read(ref this._organizationLockCount);

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"OrganizationMaterialStocks\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref this._stockLockCount);
                if (this.StockLocked.TrySetResult())
                {
                    await this._organizationLocked.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }

            if (command.CommandText.Contains("FROM \"Organizations\"", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref this._organizationLockCount);
                this._organizationLocked.TrySetResult();
            }

            return result;
        }
    }

    private sealed class EmptyStockReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _emptyReads;

        internal int EmptyReadCount => Volatile.Read(ref this._emptyReads);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!result.HasRows && command.CommandText.Contains("FROM \"OrganizationMaterialStocks\"", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref this._emptyReads) >= 2)
                {
                    this._bothRead.TrySetResult();
                }

                await this._bothRead.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }

    private sealed class OrganizationLockArrivalBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Organizations\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref this._arrivals) >= 2)
                {
                    this._bothArrived.TrySetResult();
                }

                await this._bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }

    private static async Task<Vehicle> SeedAsync(OpenCRMContext context)
    {
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Студия", OwnerUser = user, PrimaryScenarioCode = "detailing" };
        var vehicle = new Vehicle { User = user, Organization = organization, Name = "Техника" };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        return vehicle;
    }

    private static ServiceProvider CreateInventory(OpenCRMContext context)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(Mock.Of<IOrganizationSubscriptionService>());
        services.AddOpenCRMCatalog();
        services.AddOpenCRMInventory();

        return services.BuildServiceProvider();
    }

    private static Task ReceiveAsync(IServiceProvider provider, Vehicle vehicle) =>
        provider.GetRequiredService<IStockCommandService>().AddReceiptAsync(vehicle.UserId, vehicle.OrganizationId!.Value,
            new CreateOrganizationMaterialReceiptDto { Name = "Раствор", Unit = "шт", Quantity = 5, UnitCost = 10 });

    private static MaintenanceTestScope CreateMaintenanceService(OpenCRMContext context, IServiceProvider provider)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(item => item.UtcNow).Returns(new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
        var access = provider.GetRequiredService<IOrganizationSubscriptionService>();

        return new MaintenanceTestScope(new MaintenanceRepository(context),
            Mock.Of<IVehicleCounterCommandService>(), clock.Object, context, access,
            Mock.Of<IMaintenanceQueryService>(), null,
            provider.GetRequiredService<IMaintenanceConsumptionService>());
    }

    private static CreateMaintenanceDto CreateRequest(decimal quantity) => new()
    {
        Status = MaintenanceStatus.Planned, Type = MaintenanceType.Maintenance,
        PlannedDate = new DateTime(2026, 10, 10), CompletedDate = null, Location = null, Performer = null,
        Mileage = null, Comment = null,
        Details =
        [
            new CreateMaintenanceDetailDto { Name = "Работа", Price = 100 },
            new CreateMaintenanceDetailDto { Name = "Раствор", MaterialName = "Раствор", MaterialUnit = "шт", MaterialQuantity = quantity }
        ]
    };
}
