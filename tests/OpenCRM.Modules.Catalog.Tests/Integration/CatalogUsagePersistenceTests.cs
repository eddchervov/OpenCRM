using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using Npgsql;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogUsagePersistenceTests(CatalogDatabaseFixture fixture)
{
    [Fact]
    public async Task SavedMaintenanceMarksServiceMaterialAndPostWithoutReadingCatalog()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Полировка");
        var material = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Паста");
        var post = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.WorkPost, "\tПост 1\r\n");
        context.OrganizationScenarioCatalogItems.AddRange(service, material, post);
        await context.SaveChangesAsync();
        var order = Order(organization, "\t ПОСТ 1 \n\r");
        order.IsDeleted = true;
        order.Details.Add(new MaintenanceDetail
        {
            Name = service.Name,
            CatalogItemId = service.Id,
            MaterialCatalogItemId = material.Id
        });
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();

        await using var verificationContext = fixture.CreateContext();
        var items = await verificationContext.OrganizationScenarioCatalogItems.AsNoTracking()
            .Where(item => item.OrganizationId == organization.Id)
            .ToArrayAsync();

        Assert.Equal(3, items.Length);
        Assert.All(items, item => Assert.True(item.HasBeenUsed));
    }

    [Fact]
    public async Task RemovingLastMaintenanceReferenceKeepsUsageAndProtectsMaterial()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Работа");
        var material = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Материал");
        var post = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.WorkPost, "Бокс");
        context.OrganizationScenarioCatalogItems.AddRange(service, material, post);
        var order = Order(organization, "Бокс");
        order.Details.Add(new MaintenanceDetail
        {
            Name = service.Name,
            CatalogItemId = service.Id,
            MaterialCatalogItemId = material.Id
        });
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();
        await context.Maintenance.Where(item => item.Id == order.Id).ExecuteDeleteAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();

        var catalog = await query.GetAsync(organization.OwnerUserId, organization.Id);
        var activeService = await query.GetActiveServiceAsync(organization.OwnerUserId, organization.Id, service.Id);

        Assert.True(catalog.Services.Single(item => item.Id == service.Id).IsUsed);
        Assert.True(catalog.Materials.Single(item => item.Id == material.Id).IsUsed);
        Assert.True(catalog.WorkPosts.Single(item => item.Id == post.Id).IsUsed);
        Assert.True(activeService.IsUsed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.DeleteAsync(
            organization.OwnerUserId, organization.Id, material.Id));
        await command.DeleteAsync(organization.OwnerUserId, organization.Id, post.Id);

        var archivedPost = await context.OrganizationScenarioCatalogItems.AsNoTracking()
            .SingleAsync(item => item.Id == post.Id);
        Assert.False(archivedPost.IsActive);
        Assert.True(archivedPost.HasBeenUsed);
        Assert.Equal(post.Name, archivedPost.Name);
    }

    [Fact]
    public async Task ArchivedBundleMarksOnlyServiceAndRemovingLinkKeepsUsage()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Мойка");
        var bundle = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.ServiceBundle, "Комплекс");
        bundle.IsActive = false;
        bundle.BundleItems.Add(new OrganizationServiceBundleItem
        {
            BundleId = bundle.Id,
            ServiceId = service.Id,
            OrganizationId = organization.Id
        });
        context.OrganizationScenarioCatalogItems.AddRange(service, bundle);
        await context.SaveChangesAsync();
        await context.OrganizationServiceBundleItems.Where(item => item.BundleId == bundle.Id).ExecuteDeleteAsync();

        await using var verificationContext = fixture.CreateContext();
        Assert.True(await verificationContext.OrganizationScenarioCatalogItems
            .Where(item => item.Id == service.Id).Select(item => item.HasBeenUsed).SingleAsync());
        Assert.False(await verificationContext.OrganizationScenarioCatalogItems
            .Where(item => item.Id == bundle.Id).Select(item => item.HasBeenUsed).SingleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectSqlStockAndMovementUpdatesPersistUsageAfterReferenceRemoval(bool shouldUseMovement)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var material = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Паста");
        var stock = new OrganizationMaterialStock { OrganizationId = organization.Id, Name = "Ручной материал", Unit = "л" };
        context.OrganizationScenarioCatalogItems.Add(material);
        context.OrganizationMaterialStocks.Add(stock);
        await context.SaveChangesAsync();

        if (shouldUseMovement)
        {
            var movement = new OrganizationMaterialMovement
            {
                OrganizationId = organization.Id,
                StockId = stock.Id,
                Quantity = 1,
                Unit = "л"
            };
            context.OrganizationMaterialMovements.Add(movement);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "OrganizationMaterialMovements" SET "CatalogItemId" = {material.Id} WHERE "Id" = {movement.Id}
                """);
            await context.OrganizationMaterialMovements.Where(item => item.Id == movement.Id).ExecuteDeleteAsync();
        }
        else
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "OrganizationMaterialStocks" SET "CatalogItemId" = {material.Id} WHERE "Id" = {stock.Id}
                """);
        }

        await context.OrganizationMaterialStocks.Where(item => item.Id == stock.Id).ExecuteDeleteAsync();
        await using var verificationContext = fixture.CreateContext();
        Assert.True(await verificationContext.OrganizationScenarioCatalogItems
            .Where(item => item.Id == material.Id).Select(item => item.HasBeenUsed).SingleAsync());
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "OrganizationScenarioCatalogItems" WHERE "Id" = {material.Id}
            """));
    }

    [Fact]
    public async Task DirectSqlMaintenanceReferenceUpdateIsScopedToOwningOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var service = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Работа");
        var foreignService = CatalogItem(foreignOrganization, OrganizationScenarioCatalogKindCodes.Service, "Работа");
        context.OrganizationScenarioCatalogItems.AddRange(service, foreignService);
        var order = Order(organization);
        var detail = new MaintenanceDetail { Name = "Историческая строка" };
        order.Details.Add(detail);
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "MaintenanceDetails" SET "CatalogItemId" = {foreignService.Id} WHERE "DetailId" = {detail.DetailId}
            """);

        Assert.False(await context.OrganizationScenarioCatalogItems.AsNoTracking()
            .Where(item => item.Id == foreignService.Id).Select(item => item.HasBeenUsed).SingleAsync());

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "MaintenanceDetails" SET "CatalogItemId" = {service.Id} WHERE "DetailId" = {detail.DetailId}
            """);

        Assert.True(await context.OrganizationScenarioCatalogItems.AsNoTracking()
            .Where(item => item.Id == service.Id).Select(item => item.HasBeenUsed).SingleAsync());
    }

    [Fact]
    public async Task RollingBackFirstUseRollsBackFlagWithMaintenanceAndStock()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Работа");
        var material = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Материал");
        var post = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.WorkPost, "Пост");
        context.OrganizationScenarioCatalogItems.AddRange(service, material, post);
        await context.SaveChangesAsync();

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var order = Order(organization, post.Name);
            order.Details.Add(new MaintenanceDetail { Name = service.Name, CatalogItemId = service.Id });
            context.Maintenance.Add(order);
            context.OrganizationMaterialStocks.Add(new OrganizationMaterialStock
            {
                OrganizationId = organization.Id,
                CatalogItemId = material.Id,
                Name = material.Name,
                Unit = "шт"
            });
            await context.SaveChangesAsync();

            Assert.Equal(3, await context.OrganizationScenarioCatalogItems.CountAsync(item =>
                item.OrganizationId == organization.Id && item.HasBeenUsed));
            await transaction.RollbackAsync();
        });

        await using var verificationContext = fixture.CreateContext();
        Assert.False(await verificationContext.OrganizationScenarioCatalogItems.AnyAsync(item =>
            item.OrganizationId == organization.Id && item.HasBeenUsed));
        Assert.False(await verificationContext.Maintenance.AnyAsync(item => item.Vehicle.OrganizationId == organization.Id));
        Assert.False(await verificationContext.OrganizationMaterialStocks.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task LateWorkPostCreationAndRenameRecognizeExistingLocationOnlyInSameOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        context.Maintenance.Add(Order(organization, "\t ПОСТ ИСТОРИЯ \n\r"));
        await context.SaveChangesAsync();
        var latePost = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.WorkPost, "\r\nПост история\t");
        var renamedPost = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.WorkPost, "Новое название");
        var foreignPost = CatalogItem(foreignOrganization, OrganizationScenarioCatalogKindCodes.WorkPost, latePost.Name);
        context.OrganizationScenarioCatalogItems.AddRange(latePost, renamedPost, foreignPost);
        await context.SaveChangesAsync();
        renamedPost.Name = "\tпост история \r\n";
        await context.SaveChangesAsync();

        var flags = await context.OrganizationScenarioCatalogItems.AsNoTracking()
            .Where(item => item.Id == latePost.Id || item.Id == renamedPost.Id || item.Id == foreignPost.Id)
            .ToDictionaryAsync(item => item.Id, item => item.HasBeenUsed);

        Assert.True(flags[latePost.Id]);
        Assert.True(flags[renamedPost.Id]);
        Assert.False(flags[foreignPost.Id]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingOrderOrVehicleOrganizationMarksReferencesInNewOrganization(bool shouldMoveVehicle)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var targetOrganization = await fixture.CreateOrganizationAsync(context);
        var service = CatalogItem(targetOrganization, OrganizationScenarioCatalogKindCodes.Service, "Работа");
        var post = CatalogItem(targetOrganization, OrganizationScenarioCatalogKindCodes.WorkPost, "Пост");
        context.OrganizationScenarioCatalogItems.AddRange(service, post);
        var order = Order(organization, post.Name);
        order.Details.Add(new MaintenanceDetail { Name = service.Name, CatalogItemId = service.Id });
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();
        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item =>
            item.OrganizationId == targetOrganization.Id && item.HasBeenUsed));

        if (shouldMoveVehicle)
        {
            order.Vehicle.Organization = targetOrganization;
        }
        else
        {
            var targetVehicle = new Vehicle { Organization = targetOrganization, User = targetOrganization.OwnerUser };
            context.Vehicles.Add(targetVehicle);
            order.Vehicle = targetVehicle;
        }

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.OrganizationScenarioCatalogItems.CountAsync(item =>
            item.OrganizationId == targetOrganization.Id && item.HasBeenUsed));
    }

    [Fact]
    public async Task ConcurrentMaterialDeletionWaitsForFirstUseAndRejectsCommittedReference()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var material = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Материал");
        context.OrganizationScenarioCatalogItems.Add(material);
        await context.SaveChangesAsync();
        var interceptor = new CatalogItemLockRecorder();
        await using var deletionContext = fixture.CreateContext(interceptor);
        using var services = fixture.CreateServices(deletionContext);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.OrganizationMaterialStocks.Add(new OrganizationMaterialStock
            {
                OrganizationId = organization.Id,
                CatalogItemId = material.Id,
                Name = material.Name,
                Unit = "шт"
            });
            await context.SaveChangesAsync();
            var deletion = command.DeleteAsync(organization.OwnerUserId, organization.Id, material.Id);
            await interceptor.LockAttempted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(deletion.IsCompleted);
            await transaction.CommitAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => deletion.WaitAsync(TimeSpan.FromSeconds(15)));
        });

        await using var verificationContext = fixture.CreateContext();
        Assert.True(await verificationContext.OrganizationScenarioCatalogItems
            .Where(item => item.Id == material.Id).Select(item => item.HasBeenUsed).SingleAsync());
    }

    [Fact]
    public async Task DirectSqlAndStaleEfUpdatesCannotClearPersistedUsage()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var material = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Паста");
        context.OrganizationScenarioCatalogItems.Add(material);
        await context.SaveChangesAsync();
        context.OrganizationMaterialStocks.Add(new OrganizationMaterialStock
        {
            OrganizationId = organization.Id,
            CatalogItemId = material.Id,
            Name = material.Name,
            Unit = "л"
        });
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "OrganizationScenarioCatalogItems" SET "HasBeenUsed" = FALSE WHERE "Id" = {material.Id}
            """);
        material.Name = "Паста новая";
        context.Entry(material).Property(item => item.HasBeenUsed).IsModified = true;
        await context.SaveChangesAsync();

        await using var verificationContext = fixture.CreateContext();
        Assert.True(await verificationContext.OrganizationScenarioCatalogItems
            .Where(item => item.Id == material.Id).Select(item => item.HasBeenUsed).SingleAsync());
    }

    [Fact]
    public async Task RepeatedReferencesDoNotRewriteAlreadyUsedCatalogItem()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Работа");
        context.OrganizationScenarioCatalogItems.Add(service);
        var order = Order(organization);
        order.Details.Add(new MaintenanceDetail { Name = service.Name, CatalogItemId = service.Id });
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();
        var firstVersion = await context.Database.SqlQuery<string>($"""
            SELECT xmin::text AS "Value" FROM "OrganizationScenarioCatalogItems" WHERE "Id" = {service.Id}
            """).SingleAsync();

        context.MaintenanceDetails.Add(new MaintenanceDetail
        {
            MaintenanceId = order.Id,
            Name = service.Name,
            CatalogItemId = service.Id,
            Position = 1
        });
        await context.SaveChangesAsync();
        var repeatedVersion = await context.Database.SqlQuery<string>($"""
            SELECT xmin::text AS "Value" FROM "OrganizationScenarioCatalogItems" WHERE "Id" = {service.Id}
            """).SingleAsync();

        Assert.Equal(firstVersion, repeatedVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoringHistoricalCatalogIdThroughSetupMarksOnlyMatchingOrganization(bool shouldUseMaterialColumn)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var restoredId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        var order = Order(organization);

        order.Details = new[] { restoredId, foreignId }.Select((id, position) => new MaintenanceDetail
        {
            Position = position,
            Name = "Историческая ссылка",
            CatalogItemId = shouldUseMaterialColumn ? null : id,
            MaterialCatalogItemId = shouldUseMaterialColumn ? id : null
        }).ToList();

        context.Maintenance.Add(order);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var setup = scope.ServiceProvider.GetRequiredService<ICatalogSetupService>();
        var kindCode = shouldUseMaterialColumn ? OrganizationScenarioCatalogKindCodes.Material : OrganizationScenarioCatalogKindCodes.Service;

        await setup.SaveItemsAsync(organization.Id, kindCode,
            [new CatalogSetupItemDto { Id = restoredId, Name = "Восстановленная позиция", IsActive = true }]);
        await setup.SaveItemsAsync(foreignOrganization.Id, kindCode,
            [new CatalogSetupItemDto { Id = foreignId, Name = "Позиция другой организации", IsActive = true }]);
        await context.SaveChangesAsync();

        await using var verificationContext = fixture.CreateContext();
        Assert.True(await verificationContext.OrganizationScenarioCatalogItems
            .Where(item => item.Id == restoredId).Select(item => item.HasBeenUsed).SingleAsync());
        Assert.False(await verificationContext.OrganizationScenarioCatalogItems
            .Where(item => item.Id == foreignId).Select(item => item.HasBeenUsed).SingleAsync());
        var historicalIds = await verificationContext.MaintenanceDetails.Where(item => item.MaintenanceId == order.Id)
            .OrderBy(item => item.Position)
            .Select(item => shouldUseMaterialColumn ? item.MaterialCatalogItemId : item.CatalogItemId)
            .ToArrayAsync();

        Assert.Equal(new Guid?[] { restoredId, foreignId }, historicalIds);
    }

    [Fact]
    public async Task CatalogReadsUsePersistedFlagsWithoutQueryingMaintenanceOrInventory()
    {
        var interceptor = new CatalogReadRecorder();
        await using var context = fixture.CreateContext(interceptor);
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICatalogInitializationService>().EnsureDefaultsAsync(organization.Id);
        var query = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();
        interceptor.Commands.Clear();

        var catalog = await query.GetAsync(organization.OwnerUserId, organization.Id);
        await query.GetActiveServiceAsync(organization.OwnerUserId, organization.Id, catalog.Services.First().Id);

        Assert.NotEmpty(interceptor.Commands);
        Assert.DoesNotContain(interceptor.Commands, sql => sql.Contains("\"Maintenance\"", StringComparison.Ordinal)
            || sql.Contains("\"MaintenanceDetails\"", StringComparison.Ordinal)
            || sql.Contains("\"OrganizationMaterialStocks\"", StringComparison.Ordinal)
            || sql.Contains("\"OrganizationMaterialMovements\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeletingOrganizationCanCascadePreviouslyUsedCatalogItems()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var material = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Материал");
        context.OrganizationScenarioCatalogItems.Add(material);
        context.OrganizationMaterialStocks.Add(new OrganizationMaterialStock
        {
            OrganizationId = organization.Id,
            CatalogItemId = material.Id,
            Name = material.Name,
            Unit = "шт"
        });
        await context.SaveChangesAsync();

        await context.Organizations.Where(item => item.Id == organization.Id).ExecuteDeleteAsync();

        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == material.Id));
    }

    private static OrganizationScenarioCatalogItem CatalogItem(Organization organization, string kindCode, string name) => new()
    {
        OrganizationId = organization.Id,
        ScenarioCode = organization.PrimaryScenarioCode,
        KindCode = kindCode,
        Code = "test-" + Guid.NewGuid().ToString("N"),
        Name = name
    };

    private static OpenCRM.Domain.Maintenance Order(Organization organization, string? location = null) => new()
    {
        User = organization.OwnerUser,
        Vehicle = new Vehicle { Organization = organization, User = organization.OwnerUser },
        Location = location,
        Date = DateTime.UtcNow,
        Details = []
    };

    private sealed class CatalogReadRecorder : DbCommandInterceptor
    {
        internal List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            this.Commands.Add(command.CommandText);

            return ValueTask.FromResult(result);
        }
    }

    private sealed class CatalogItemLockRecorder : DbCommandInterceptor
    {
        internal TaskCompletionSource LockAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && command.CommandText.Contains("\"OrganizationScenarioCatalogItems\"", StringComparison.Ordinal))
            {
                this.LockAttempted.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }
    }
}
