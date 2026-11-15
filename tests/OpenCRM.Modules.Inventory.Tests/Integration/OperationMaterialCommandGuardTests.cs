using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

public sealed class OperationMaterialCommandGuardTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task MissingMaintenanceAssignmentIsRejectedByCommandAndAvailabilityQuery()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { Name = "Проверка отсутствующего назначения", OwnerUser = user, Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Исполнитель" };
        var detailId = Guid.NewGuid();
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Date = DateTime.UtcNow,
            Status = MaintenanceStatus.InProgress,
            Type = MaintenanceType.Maintenance,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Работа" }]
        };
        context.AddRange(employee, maintenance, new MaintenanceWorkOperation
        {
            Id = detailId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.InProgress
        });
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inventory.Commands.IssueForOperationAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            CreateIssue(Guid.NewGuid())));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inventory.Queries.GetAvailableForOperationAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            detailId));
    }

    [Fact]
    public async Task IssueAndReturnRejectWhenMaintenanceDetailHasNoWorkOperation()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { Name = "Проверка отсутствующей работы", OwnerUser = user, Members = [member] };
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
            QuantityOnHand = 5,
            AverageUnitCost = 10,
            InventoryValue = 50
        };
        var previousIssue = new OrganizationMaterialMovement
        {
            Organization = organization,
            Stock = stock,
            Maintenance = maintenance,
            OperationId = detailId,
            EmployeeId = employee.Id,
            MemberId = member.Id,
            TypeCode = OrganizationMaterialMovementTypeCodes.MaintenanceIssue,
            Quantity = -1,
            Unit = "шт",
            UnitCost = 10,
            TotalCost = 10,
            SourceEntityType = "operation",
            CreatedByUserId = user.Id
        };
        context.AddRange(employee, maintenance, stock, previousIssue);
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Commands.IssueForOperationAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            CreateIssue(stock.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Commands.ReverseOperationMaterialAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            previousIssue.Id,
            new ReverseOperationMaterialDto { CommandId = Guid.NewGuid(), Reason = "Не понадобилось" }));
    }

    [Fact]
    public async Task IssueRechecksEmployeeAfterAcquiringEmployeeLock()
    {
        var interceptor = new DeactivateEmployeeBeforeLockInterceptor(fixture);
        await using var context = fixture.CreateContext(interceptor);
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { Name = "Проверка назначения", OwnerUser = user, Members = [member] };
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
            QuantityOnHand = 2,
            AverageUnitCost = 10,
            InventoryValue = 20
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
        interceptor.EmployeeId = employee.Id;
        using var inventory = CreateInventory(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inventory.Commands.IssueForOperationAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            CreateIssue(Guid.NewGuid())));

        Assert.True(interceptor.DeactivatedEmployee);
        Assert.False(await context.OrganizationEmployees.Where(item => item.Id == employee.Id).Select(item => item.IsActive).SingleAsync());
    }

    [Fact]
    public async Task IssueAndReturnRequireCurrentAssignmentAndActiveWork()
    {
        var maintenanceStateInterceptor = new MaintenanceStateInterceptor();
        await using var context = fixture.CreateContext(maintenanceStateInterceptor);
        maintenanceStateInterceptor.Context = context;
        var user = new ApplicationUser();
        var otherUser = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var otherMember = new OrganizationMember { User = otherUser, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { Name = "Проверка выдачи", OwnerUser = user, Members = [member, otherMember] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Исполнитель" };
        var otherEmployee = new OrganizationEmployee { Organization = organization, Member = otherMember, Name = "Другой исполнитель" };
        var assignedDetailId = Guid.NewGuid();
        var unassignedDetailId = Guid.NewGuid();
        var namedMaterialDetailId = Guid.NewGuid();
        var catalogMaterialDetailId = Guid.NewGuid();
        var quantifiedMaterialDetailId = Guid.NewGuid();
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            EmployeeId = employee.Id,
            Date = DateTime.UtcNow,
            Status = MaintenanceStatus.InProgress,
            Type = MaintenanceType.Maintenance,
            Details =
            [
                new MaintenanceDetail { DetailId = assignedDetailId, Name = "Работа" },
                new MaintenanceDetail { DetailId = unassignedDetailId, Name = "Чужая работа", EmployeeId = otherEmployee.Id },
                new MaintenanceDetail { DetailId = namedMaterialDetailId, Name = "Материал", MaterialName = "Состав" },
                new MaintenanceDetail { DetailId = catalogMaterialDetailId, Name = "Каталог", MaterialCatalogItemId = Guid.NewGuid() },
                new MaintenanceDetail { DetailId = quantifiedMaterialDetailId, Name = "Количество", MaterialQuantity = 1 }
            ]
        };
        var stock = new OrganizationMaterialStock
        {
            Organization = organization,
            Name = "Состав",
            Unit = "шт",
            QuantityOnHand = 3,
            AverageUnitCost = 10,
            InventoryValue = 20
        };
        var operation = new MaintenanceWorkOperation
        {
            Id = assignedDetailId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.InProgress
        };
        context.AddRange(employee, otherEmployee, maintenance, stock, operation);
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);
        var commands = inventory.Commands;

        await Assert.ThrowsAsync<KeyNotFoundException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, Guid.NewGuid(), assignedDetailId, CreateIssue(stock.Id)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => commands.IssueForOperationAsync(
            "missing-member", organization.Id, maintenance.Id, assignedDetailId, CreateIssue(stock.Id)));
        employee.IsActive = false;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, CreateIssue(stock.Id)));
        employee.IsActive = true;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, Guid.NewGuid(), CreateIssue(stock.Id)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, CreateIssue(Guid.NewGuid())));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, namedMaterialDetailId, CreateIssue(stock.Id)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, catalogMaterialDetailId, CreateIssue(stock.Id)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, quantifiedMaterialDetailId, CreateIssue(stock.Id)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, unassignedDetailId, CreateIssue(stock.Id)));

        await commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, CreateIssue(stock.Id, string.Empty));
        var fallbackIssue = await commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, CreateIssue(stock.Id, "шт"));
        var returnRequest = new ReverseOperationMaterialDto { CommandId = Guid.NewGuid(), Reason = "Не понадобилось" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ReverseOperationMaterialAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            assignedDetailId,
            fallbackIssue.Id,
            new ReverseOperationMaterialDto { CommandId = Guid.NewGuid(), Reason = null! }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => commands.ReverseOperationMaterialAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, Guid.NewGuid(), returnRequest));
        maintenance.Status = MaintenanceStatus.Planned;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, CreateIssue(stock.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ReverseOperationMaterialAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, fallbackIssue.Id, returnRequest));

        maintenance.Status = MaintenanceStatus.InProgress;
        await context.SaveChangesAsync();
        maintenanceStateInterceptor.ArmHandedOver(maintenance.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, CreateIssue(stock.Id)));
        maintenanceStateInterceptor.ArmHandedOver(maintenance.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ReverseOperationMaterialAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, fallbackIssue.Id, returnRequest));

        maintenanceStateInterceptor.ArmReadyForHandover(maintenance.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, CreateIssue(stock.Id)));
        maintenanceStateInterceptor.ArmReadyForHandover(maintenance.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ReverseOperationMaterialAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, fallbackIssue.Id, returnRequest));

        await context.Entry(maintenance).ReloadAsync();
        operation.EmployeeId = otherEmployee.Id;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, CreateIssue(stock.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ReverseOperationMaterialAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, fallbackIssue.Id, returnRequest));

        operation.EmployeeId = employee.Id;
        operation.Status = MaintenanceWorkStatus.Completed;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.IssueForOperationAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, CreateIssue(stock.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ReverseOperationMaterialAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, fallbackIssue.Id, returnRequest));

        operation.Status = MaintenanceWorkStatus.InProgress;
        await context.SaveChangesAsync();
        var returned = await commands.ReverseOperationMaterialAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, fallbackIssue.Id, returnRequest);
        Assert.True(returned.IsReturn);
        var repeatedReturn = await commands.ReverseOperationMaterialAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId, fallbackIssue.Id, returnRequest);
        Assert.Equal(returned.Id, repeatedReturn.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ReverseOperationMaterialAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            assignedDetailId,
            fallbackIssue.Id,
            new ReverseOperationMaterialDto { CommandId = Guid.NewGuid(), Reason = "Повтор" }));
    }

    private static IssueOperationMaterialDto CreateIssue(Guid stockId, string unit = "шт") => new()
    {
        StockId = stockId,
        Quantity = 1,
        Unit = unit,
        CommandId = Guid.NewGuid()
    };

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

        public IOperationMaterialCommandService Commands => this._scope.ServiceProvider.GetRequiredService<IOperationMaterialCommandService>();

        public IOperationMaterialQueryService Queries => this._scope.ServiceProvider.GetRequiredService<IOperationMaterialQueryService>();

        public void Dispose()
        {
            this._scope.Dispose();
            provider.Dispose();
        }
    }

    private sealed class MaintenanceStateInterceptor : DbCommandInterceptor
    {
        private Guid _maintenanceId;
        private bool _markHandedOver;
        private bool _markReadyForHandover;

        internal OpenCRMContext? Context { get; set; }

        internal void ArmHandedOver(Guid maintenanceId)
        {
            this._maintenanceId = maintenanceId;
            this._markHandedOver = true;
            this._markReadyForHandover = false;
        }

        internal void ArmReadyForHandover(Guid maintenanceId)
        {
            this._maintenanceId = maintenanceId;
            this._markHandedOver = false;
            this._markReadyForHandover = true;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"OrganizationMembers\"", StringComparison.Ordinal))
            {
                var maintenance = this.Context?.ChangeTracker.Entries<OpenCRM.Domain.Maintenance>()
                    .SingleOrDefault(entry => entry.Entity.Id == this._maintenanceId)?.Entity;
                if (maintenance != null && this._markHandedOver)
                {
                    maintenance.HandedOverAt = DateTime.UtcNow;
                    this._markHandedOver = false;
                }
                else if (maintenance != null && this._markReadyForHandover)
                {
                    maintenance.ReadyForHandoverAt = DateTime.UtcNow;
                    this._markReadyForHandover = false;
                }
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class DeactivateEmployeeBeforeLockInterceptor(InventoryDatabaseFixture fixture) : DbCommandInterceptor
    {
        internal Guid EmployeeId { get; set; }

        internal bool DeactivatedEmployee { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!this.DeactivatedEmployee
                && command.CommandText.Contains("FROM \"OrganizationEmployees\" WHERE \"Id\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                await using var concurrentContext = fixture.CreateContext();
                await concurrentContext.OrganizationEmployees
                    .Where(employee => employee.Id == this.EmployeeId)
                    .ExecuteUpdateAsync(update => update.SetProperty(employee => employee.IsActive, false), cancellationToken);
                this.DeactivatedEmployee = true;
            }

            return result;
        }
    }
}
