using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class InventoryCommandTransactionRetryTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task ReceiptRetriesAfterTransientFailureBeforeCommitAndClearsTrackedEntities()
    {
        var interceptor = new OneShotTransactionFailureInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка повторной попытки", OwnerUser = user };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        using var inventory = CreateInventoryScope(context);
        interceptor.Arm(TransactionFailurePoint.Committing);

        var result = await inventory.StockCommands.AddReceiptAsync(user.Id, organization.Id, new CreateOrganizationMaterialReceiptDto
        {
            Name = "Материал",
            Unit = "л",
            Quantity = 2,
            UnitCost = 100
        });

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(2, interceptor.CommittingCount);
        Assert.Equal(1, interceptor.CommittedCount);
        Assert.Equal(1, await context.OrganizationMaterialMovements.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.Equal(2000, (await context.OrganizationMaterialStocks.SingleAsync(item => item.OrganizationId == organization.Id)).QuantityOnHand);
    }

    [Fact]
    public async Task SuccessfulVerificationPreventsRetryAfterTransientFailureAfterCommit()
    {
        var interceptor = new OneShotTransactionFailureInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка успешной фиксации", OwnerUser = user };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        using var inventory = CreateInventoryScope(context);
        interceptor.Arm(TransactionFailurePoint.Committed);

        await inventory.StockCommands.AddReceiptAsync(user.Id, organization.Id, new CreateOrganizationMaterialReceiptDto
        {
            Name = "Материал",
            Unit = "л",
            Quantity = 2,
            UnitCost = 100
        });

        Assert.Equal(1, interceptor.CommittingCount);
        Assert.Equal(1, interceptor.CommittedCount);
        Assert.Equal(1, await context.OrganizationMaterialMovements.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.Equal(2000, (await context.OrganizationMaterialStocks.SingleAsync(item => item.OrganizationId == organization.Id)).QuantityOnHand);
    }

    [Fact]
    public async Task SuccessfulVerificationPreventsDuplicateMaintenanceConsumptionAfterCommitFailure()
    {
        var interceptor = new OneShotTransactionFailureInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка повтора расхода", OwnerUser = user };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Date = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Details = []
        };
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        using var inventory = CreateInventoryScope(context);
        await inventory.StockCommands.AddReceiptAsync(user.Id, organization.Id, new CreateOrganizationMaterialReceiptDto
        {
            Name = "Материал",
            Unit = "л",
            Quantity = 2,
            UnitCost = 100
        });
        interceptor.Arm(TransactionFailurePoint.Committed);

        var result = await inventory.Consumption.SynchronizeAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            [new MaintenanceMaterialConsumptionLine(Guid.NewGuid(), null, "Работа", "Материал", "л", 1)]);

        Assert.Equal(100, result.TotalCost);
        Assert.Equal(1, interceptor.CommittingCount);
        Assert.Equal(1, interceptor.CommittedCount);
        Assert.Single(await context.OrganizationMaterialMovements
            .Where(item => item.OrganizationId == organization.Id && item.MaintenanceId == maintenance.Id)
            .ToListAsync());
        Assert.Equal(1000, (await context.OrganizationMaterialStocks.SingleAsync(item => item.OrganizationId == organization.Id)).QuantityOnHand);
    }

    [Fact]
    public async Task FailedVerificationRetriesNoChangeSynchronizationAfterCommitFailure()
    {
        var interceptor = new OneShotTransactionFailureInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка пустой синхронизации", OwnerUser = user };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Date = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Details = []
        };
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        using var inventory = CreateInventoryScope(context);
        interceptor.Arm(TransactionFailurePoint.Committed);

        var result = await inventory.Consumption.SynchronizeAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            Array.Empty<MaintenanceMaterialConsumptionLine>());

        Assert.Equal(0, result.TotalCost);
        Assert.Equal(2, interceptor.CommittingCount);
        Assert.Equal(2, interceptor.CommittedCount);
        Assert.False(await context.OrganizationMaterialMovements.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task SuccessfulVerificationPreventsDuplicateOperationReturnAfterCommitFailure()
    {
        var interceptor = new OneShotTransactionFailureInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { Name = "Проверка повтора возврата", OwnerUser = user, Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Исполнитель" };
        var detailId = Guid.NewGuid();
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            EmployeeId = employee.Id,
            Date = DateTime.UtcNow,
            Status = MaintenanceStatus.InProgress,
            Type = MaintenanceType.Maintenance,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Работа" }]
        };
        var stock = new OrganizationMaterialStock
        {
            Organization = organization,
            Name = "Материал",
            Unit = "шт",
            QuantityOnHand = 3,
            AverageUnitCost = 10,
            InventoryValue = 30
        };
        context.AddRange(employee, maintenance, stock, new MaintenanceWorkOperation
        {
            Id = detailId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.InProgress
        });
        await context.SaveChangesAsync();
        using var inventory = CreateInventoryScope(context);
        var issue = await inventory.OperationCommands.IssueForOperationAsync(user.Id, organization.Id, maintenance.Id, detailId,
            new IssueOperationMaterialDto { StockId = stock.Id, Quantity = 1, Unit = "шт", CommandId = Guid.NewGuid() });
        var returnCommand = new ReverseOperationMaterialDto { CommandId = Guid.NewGuid(), Reason = "Не понадобилось" };
        interceptor.Arm(TransactionFailurePoint.Committed);

        await inventory.OperationCommands.ReverseOperationMaterialAsync(
            user.Id, organization.Id, maintenance.Id, detailId, issue.Id, returnCommand);

        Assert.Equal(1, interceptor.CommittingCount);
        Assert.Equal(1, interceptor.CommittedCount);
        Assert.Equal(2, await context.OrganizationMaterialMovements.CountAsync(item => item.OperationId == detailId));
        Assert.Equal(3, (await context.OrganizationMaterialStocks.SingleAsync(item => item.Id == stock.Id)).QuantityOnHand);
    }

    private static InventoryTestScope CreateInventoryScope(OpenCRMContext context)
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

        public IStockCommandService StockCommands => this._scope.ServiceProvider.GetRequiredService<IStockCommandService>();

        public IMaintenanceConsumptionService Consumption => this._scope.ServiceProvider.GetRequiredService<IMaintenanceConsumptionService>();

        public IOperationMaterialCommandService OperationCommands => this._scope.ServiceProvider.GetRequiredService<IOperationMaterialCommandService>();

        public void Dispose()
        {
            this._scope.Dispose();
            provider.Dispose();
        }
    }

    private enum TransactionFailurePoint
    {
        Committing,
        Committed
    }

    private sealed class OneShotTransactionFailureInterceptor : DbTransactionInterceptor
    {
        private TransactionFailurePoint? _failurePoint;

        internal int CommittingCount { get; private set; }

        internal int CommittedCount { get; private set; }

        internal void Arm(TransactionFailurePoint failurePoint)
        {
            this.CommittingCount = 0;
            this.CommittedCount = 0;
            this._failurePoint = failurePoint;
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            this.CommittingCount++;
            if (this._failurePoint == TransactionFailurePoint.Committing)
            {
                this._failurePoint = null;
                throw this.CreateTransientFailure();
            }

            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            this.CommittedCount++;
            if (this._failurePoint == TransactionFailurePoint.Committed)
            {
                this._failurePoint = null;
                throw this.CreateTransientFailure();
            }

            return Task.CompletedTask;
        }

        private PostgresException CreateTransientFailure() => new(
            "Транзакция временно не завершилась.",
            "ERROR",
            "ERROR",
            "40001");
    }
}
