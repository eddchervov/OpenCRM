using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Catalog;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Domain.Interfaces.Models;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;
using Npgsql;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogPersonalUsageConcurrencyTests(CatalogDatabaseFixture fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PersonalUsageAndPublicVehicleTransferKeepCommittedCatalogUsage(
        bool shouldTransferFirst,
        bool shouldInsertDetail)
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var vehicle = new Vehicle { User = organization.OwnerUser };
        var date = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = organization.OwnerUser,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Planned,
            Date = date,
            PlannedDate = date,
            Details = []
        };
        var catalogItem = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = shouldInsertDetail ? OrganizationScenarioCatalogKindCodes.Service : OrganizationScenarioCatalogKindCodes.WorkPost,
            Code = "concurrent-" + Guid.NewGuid().ToString("N"),
            Name = "Пост личной записи"
        };
        seedContext.Maintenance.Add(maintenance);
        seedContext.OrganizationScenarioCatalogItems.Add(catalogItem);
        await seedContext.SaveChangesAsync();
        await using var sourceContext = fixture.CreateContext();
        await using var transferContext = fixture.CreateContext();
        using var sourceServices = CatalogPersonalUsageConcurrencyTests.CreateServices(sourceContext);
        using var transferServices = CatalogPersonalUsageConcurrencyTests.CreateServices(transferContext);
        using var sourceScope = sourceServices.CreateScope();
        using var transferScope = transferServices.CreateScope();
        var workflow = sourceScope.ServiceProvider.GetRequiredService<IMaintenanceWorkflowService>();
        var transfer = transferScope.ServiceProvider.GetRequiredService<IVehicleCommandService>();
        var firstContext = shouldTransferFirst ? transferContext : sourceContext;
        var secondContext = shouldTransferFirst ? sourceContext : transferContext;
        await secondContext.Database.OpenConnectionAsync();
        var secondConnection = (NpgsqlConnection)secondContext.Database.GetDbConnection();
        var detailId = Guid.NewGuid();

        async Task SaveSourceAsync()
        {
            if (!shouldInsertDetail)
            {
                await workflow.ChangeWorkflow(organization.OwnerUserId, maintenance.Id, new MaintenanceWorkflowActionDto
                {
                    Action = "reschedule",
                    Location = catalogItem.Name,
                    PlannedDate = date.AddDays(1)
                });

                return;
            }

            sourceContext.MaintenanceDetails.Add(new MaintenanceDetail
            {
                MaintenanceId = maintenance.Id,
                DetailId = detailId,
                Name = "Добавленная личная работа",
                CatalogItemId = catalogItem.Id
            });
            await sourceContext.SaveChangesAsync();
        }

        Task TransferAsync() => transfer.TransferToOrganizationAsync(organization.OwnerUserId, vehicle.Id, organization.Id);

        await firstContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await firstContext.Database.BeginTransactionAsync();
            if (shouldTransferFirst)
            {
                await TransferAsync();
            }
            else
            {
                await SaveSourceAsync();
            }

            var pending = shouldTransferFirst ? SaveSourceAsync() : TransferAsync();
            await CatalogPersonalUsageConcurrencyTests.WaitForDatabaseLockAsync(seedContext, secondConnection.ProcessID);
            Assert.False(pending.IsCompleted);
            await transaction.CommitAsync();
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
        });

        await using var verificationContext = fixture.CreateContext();
        Assert.Equal(organization.Id, await verificationContext.Vehicles
            .Where(item => item.Id == vehicle.Id).Select(item => item.OrganizationId).SingleAsync());
        Assert.True(await verificationContext.OrganizationScenarioCatalogItems
            .Where(item => item.Id == catalogItem.Id).Select(item => item.HasBeenUsed).SingleAsync());
        if (shouldInsertDetail)
        {
            Assert.True(await verificationContext.MaintenanceDetails.AnyAsync(item =>
                item.MaintenanceId == maintenance.Id && item.DetailId == detailId && item.CatalogItemId == catalogItem.Id));

            return;
        }

        var saved = await verificationContext.Maintenance.AsNoTracking().SingleAsync(item => item.Id == maintenance.Id);
        Assert.Equal(MaintenanceStatus.Planned, saved.Status);
        Assert.Equal(catalogItem.Name, saved.Location);
        Assert.Equal(date.AddDays(1), saved.PlannedDate);
    }

    private static ServiceProvider CreateServices(OpenCRMContext context)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc));
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(value => value.GetVehicleLimitAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>()))
            .ReturnsAsync(100);
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "catalog-appsettings.json")).Build());
        services.AddSingleton(clock.Object);
        services.AddSingleton(subscription.Object);
        services.AddSingleton<IMaintenanceRepository>(new MaintenanceRepository(context));
        services.AddSingleton<IVehicleRepository>(new VehicleRepository(context));
        services.AddSingleton(Mock.Of<IModelRepository>());
        services.AddSingleton(Mock.Of<IVehicleCatalogSuggestionRepository>());
        services.AddSingleton(Mock.Of<IPersonalSubscriptionQueryService>());
        services.AddSingleton(Mock.Of<IFeatureAccessService>());
        services.AddSingleton(Mock.Of<IMaintenanceSchedulerSyncService>());
        services.AddOpenCRMVehicles();
        services.AddOpenCRMMaintenance();
        services.AddSingleton(new Mock<IVehicleCounterCommandService>(MockBehavior.Strict).Object);

        return services.BuildServiceProvider();
    }

    private static async Task WaitForDatabaseLockAsync(OpenCRMContext context, int processId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var isWaiting = await context.Database.SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity WHERE pid = {processId} AND wait_event_type = 'Lock'
                ) AS "Value"
                """).SingleAsync();

            if (isWaiting)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Перенос техники и сохранение личной записи не дождались общей блокировки техники.");
    }
}
