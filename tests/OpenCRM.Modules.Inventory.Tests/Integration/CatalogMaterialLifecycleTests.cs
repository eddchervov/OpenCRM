using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Inventory;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using Npgsql;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class CatalogMaterialLifecycleTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task ReceiptCreatesStockForActiveCatalogMaterial()
    {
        await using var context = fixture.CreateContext();
        var (user, organization, material) = await CatalogMaterialLifecycleTests.SeedAsync(context, isActive: true);
        using var services = CatalogMaterialLifecycleTests.CreateServices(context, user.Id, organization.Id);
        using var scope = services.CreateScope();

        var stock = await scope.ServiceProvider.GetRequiredService<IStockCommandService>().AddReceiptAsync(
            user.Id,
            organization.Id,
            new CreateOrganizationMaterialReceiptDto
            {
                CatalogItemId = material.Id,
                Unit = "л",
                Quantity = 2,
                UnitCost = 150
            });

        Assert.Equal(material.Id, stock.CatalogItemId);
        Assert.Equal(material.Name, stock.Name);
        Assert.Equal("мл", stock.Unit);
        Assert.Equal(2000, stock.QuantityOnHand);
        Assert.Equal(0.15m, stock.AverageUnitCost);
        Assert.Equal(300, stock.InventoryValue);
        var movement = await context.OrganizationMaterialMovements.SingleAsync(item => item.OrganizationId == organization.Id);
        Assert.Equal(material.Id, movement.CatalogItemId);
        Assert.Equal(OrganizationMaterialMovementTypeCodes.Receipt, movement.TypeCode);
        Assert.Equal(2, movement.EnteredQuantity);
        Assert.Equal("л", movement.EnteredUnit);
        Assert.Equal(2000, movement.Quantity);
        Assert.Equal(300, movement.TotalCost);
    }

    [Fact]
    public async Task ReceiptRejectsArchivedCatalogMaterialWithoutCreatingStockOrMovement()
    {
        await using var context = fixture.CreateContext();
        var (user, organization, material) = await CatalogMaterialLifecycleTests.SeedAsync(context, isActive: true, isSystem: true);
        using var services = CatalogMaterialLifecycleTests.CreateServices(context, user.Id, organization.Id);
        using var scope = services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        await catalog.DeleteAsync(user.Id, organization.Id, material.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IStockCommandService>().AddReceiptAsync(
                user.Id,
                organization.Id,
                new CreateOrganizationMaterialReceiptDto
                {
                    CatalogItemId = material.Id,
                    Unit = "л",
                    Quantity = 2,
                    UnitCost = 150
                }));

        Assert.Equal("Материал не найден в справочнике сценария.", exception.Message);
        Assert.False(await context.OrganizationMaterialStocks.AnyAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await context.OrganizationMaterialMovements.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task HistoricalConsumptionCanStillUseArchivedCatalogMaterialAndExistingStock()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Исторический склад", OwnerUser = user };
        var material = new OrganizationScenarioCatalogItem
        {
            Organization = organization,
            ScenarioCode = "detailing",
            KindCode = OrganizationScenarioCatalogKindCodes.Material,
            Code = "archived-material",
            Name = "Архивный состав",
            IsSystem = true,
            IsActive = false
        };
        var vehicle = new Vehicle { User = user, Organization = organization, DateTimeCreation = DateTime.UtcNow };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            User = user,
            Date = DateTime.UtcNow,
            DateTimeCreation = DateTime.UtcNow,
            Type = MaintenanceType.Maintenance,
            Details = []
        };
        var stock = new OrganizationMaterialStock
        {
            Organization = organization,
            CatalogItemId = material.Id,
            Name = material.Name,
            Unit = "л",
            QuantityOnHand = 5,
            AverageUnitCost = 10,
            InventoryValue = 50
        };
        context.AddRange(material, maintenance, stock);
        await context.SaveChangesAsync();
        using var services = CatalogMaterialLifecycleTests.CreateServices(context, user.Id, organization.Id);
        using var scope = services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMaintenanceConsumptionService>().SynchronizeAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            [new MaintenanceMaterialConsumptionLine(Guid.NewGuid(), material.Id, material.Name, material.Name, "л", 2)]);

        Assert.Equal(20, result.TotalCost);
        Assert.Equal(3, (await context.OrganizationMaterialStocks.SingleAsync(item => item.Id == stock.Id)).QuantityOnHand);
        var movement = await context.OrganizationMaterialMovements.SingleAsync(item => item.MaintenanceId == maintenance.Id);
        Assert.Equal(material.Id, movement.CatalogItemId);
        Assert.Equal(-2, movement.Quantity);
    }

    [Fact]
    public async Task ArchiveLockSerializesConcurrentReceiptAndRejectsItWithoutWrites()
    {
        await using var seedContext = fixture.CreateContext();
        var (user, organization, material) = await CatalogMaterialLifecycleTests.SeedAsync(seedContext, isActive: true, isSystem: true);
        var archiveBarrier = new ArchiveOrganizationLockBarrier();
        var receiptBarrier = new ReceiptOrganizationLockBarrier();
        await using var archiveContext = fixture.CreateContext(archiveBarrier);
        await using var receiptContext = fixture.CreateContext(receiptBarrier);
        using var archiveServices = CatalogMaterialLifecycleTests.CreateServices(archiveContext, user.Id, organization.Id);
        using var receiptServices = CatalogMaterialLifecycleTests.CreateServices(receiptContext, user.Id, organization.Id);
        using var archiveScope = archiveServices.CreateScope();
        using var receiptScope = receiptServices.CreateScope();
        await receiptContext.Database.OpenConnectionAsync();
        var receiptProcessId = ((NpgsqlConnection)receiptContext.Database.GetDbConnection()).ProcessID;
        var archiveTask = archiveScope.ServiceProvider.GetRequiredService<ICatalogCommandService>()
            .DeleteAsync(user.Id, organization.Id, material.Id);
        await archiveBarrier.WaitUntilOrganizationLockIsHeldAsync();

        var receiptTask = receiptScope.ServiceProvider.GetRequiredService<IStockCommandService>().AddReceiptAsync(
            user.Id,
            organization.Id,
            new CreateOrganizationMaterialReceiptDto
            {
                CatalogItemId = material.Id,
                Unit = "л",
                Quantity = 2,
                UnitCost = 150
            });
        try
        {
            await receiptBarrier.WaitUntilOrganizationLockIsRequestedAsync();
            await using var observer = fixture.CreateContext();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (await observer.Database.SqlQuery<int>(
                $"SELECT cardinality(pg_blocking_pids({receiptProcessId})) AS \"Value\"")
                .SingleAsync(timeout.Token) == 0)
            {
                timeout.Token.ThrowIfCancellationRequested();
            }

            Assert.False(receiptTask.IsCompleted);
        }
        finally
        {
            archiveBarrier.ReleaseArchive();
        }

        await archiveTask;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => receiptTask);

        Assert.Equal("Материал не найден в справочнике сценария.", exception.Message);
        await using var verificationContext = fixture.CreateContext();
        Assert.False((await verificationContext.OrganizationScenarioCatalogItems.AsNoTracking()
            .SingleAsync(item => item.Id == material.Id)).IsActive);
        Assert.False(await verificationContext.OrganizationMaterialStocks.AnyAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await verificationContext.OrganizationMaterialMovements.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    private static async Task<(ApplicationUser User, Organization Organization, OrganizationScenarioCatalogItem Material)> SeedAsync(
        OpenCRMContext context,
        bool isActive,
        bool isSystem = false)
    {
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Каталог материалов", OwnerUser = user };
        var material = new OrganizationScenarioCatalogItem
        {
            Organization = organization,
            ScenarioCode = "detailing",
            KindCode = OrganizationScenarioCatalogKindCodes.Material,
            Code = "catalog-material",
            Name = "Каталожный состав",
            IsSystem = isSystem,
            IsActive = isActive
        };
        context.OrganizationScenarioCatalogItems.Add(material);
        await context.SaveChangesAsync();

        return (user, organization, material);
    }

    private static ServiceProvider CreateServices(OpenCRMContext context, string userId, Guid organizationId)
    {
        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                userId, organizationId, OrganizationPermissionCodes.MaterialsManage))
            .Returns(Task.CompletedTask);
        access.Setup(service => service.EnsureWorkspaceCanReadAsync(
                userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit))
            .Returns(Task.CompletedTask);
        access.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit))
            .Returns(Task.CompletedTask);

        return new ServiceCollection()
            .AddLogging()
            .AddScoped(_ => context)
            .AddScoped(_ => access.Object)
            .AddOpenCRMInventory()
            .AddOpenCRMCatalog()
            .BuildServiceProvider();
    }

    private sealed class ArchiveOrganizationLockBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _lockHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseArchive = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task WaitUntilOrganizationLockIsHeldAsync() => this._lockHeld.Task.WaitAsync(TimeSpan.FromSeconds(15));

        internal void ReleaseArchive() => this._releaseArchive.TrySetResult();

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Organizations\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal))
            {
                this._lockHeld.TrySetResult();
                await this._releaseArchive.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }

            return result;
        }
    }

    private sealed class ReceiptOrganizationLockBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _lockRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task WaitUntilOrganizationLockIsRequestedAsync() => this._lockRequested.Task.WaitAsync(TimeSpan.FromSeconds(15));

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Organizations\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal))
            {
                this._lockRequested.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }
    }
}
