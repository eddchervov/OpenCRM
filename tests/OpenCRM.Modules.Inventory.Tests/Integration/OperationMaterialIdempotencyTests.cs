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

public sealed class OperationMaterialIdempotencyTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task IssueRejectsCommandIdsWithDifferentOrganizationMaintenanceOperationStockEmployeeMemberOrQuantity()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var otherUser = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var otherMember = new OrganizationMember { User = otherUser, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { Name = "Основная организация", OwnerUser = user, Members = [member, otherMember] };
        var foreignOrganization = new Organization { Name = "Другая организация", OwnerUser = otherUser };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Исполнитель" };
        var otherEmployee = new OrganizationEmployee { Organization = organization, Member = otherMember, Name = "Другой исполнитель" };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var maintenanceId = Guid.NewGuid();
        var otherMaintenanceId = Guid.NewGuid();
        var detailId = Guid.NewGuid();
        var otherDetailId = Guid.NewGuid();
        var maintenance = CreateMaintenance(user, vehicle, maintenanceId, detailId, "Работа");
        var otherMaintenance = CreateMaintenance(user, vehicle, otherMaintenanceId, otherDetailId, "Другая работа");
        maintenance.EmployeeId = employee.Id;
        otherMaintenance.EmployeeId = employee.Id;
        var stock = CreateStock(organization, "Состав");
        var otherStock = CreateStock(organization, "Другой состав");
        var foreignStock = CreateStock(foreignOrganization, "Чужой состав");
        var missingMaintenance = CreatePreviousIssue(Guid.NewGuid(), organization, stock, maintenance, detailId, employee.Id, member.Id, -1);
        missingMaintenance.Maintenance = null;
        missingMaintenance.MaintenanceId = null;
        var missingOperation = CreatePreviousIssue(Guid.NewGuid(), organization, stock, maintenance, detailId, employee.Id, member.Id, -1);
        missingOperation.OperationId = null;
        var missingEmployee = CreatePreviousIssue(Guid.NewGuid(), organization, stock, maintenance, detailId, employee.Id, member.Id, -1);
        missingEmployee.EmployeeId = null;
        var missingMember = CreatePreviousIssue(Guid.NewGuid(), organization, stock, maintenance, detailId, employee.Id, member.Id, -1);
        missingMember.MemberId = null;
        var mismatchedCommands = new[]
        {
            CreatePreviousIssue(Guid.NewGuid(), foreignOrganization, stock, maintenance, detailId, employee.Id, member.Id, -1),
            CreatePreviousIssue(Guid.NewGuid(), organization, stock, otherMaintenance, detailId, employee.Id, member.Id, -1),
            CreatePreviousIssue(Guid.NewGuid(), organization, stock, maintenance, otherDetailId, employee.Id, member.Id, -1),
            CreatePreviousIssue(Guid.NewGuid(), organization, otherStock, maintenance, detailId, employee.Id, member.Id, -1),
            CreatePreviousIssue(Guid.NewGuid(), organization, stock, maintenance, detailId, otherEmployee.Id, member.Id, -1),
            CreatePreviousIssue(Guid.NewGuid(), organization, stock, maintenance, detailId, employee.Id, otherMember.Id, -1),
            CreatePreviousIssue(Guid.NewGuid(), organization, stock, maintenance, detailId, employee.Id, member.Id, -2),
            missingMaintenance,
            missingOperation,
            missingEmployee,
            missingMember
        };
        mismatchedCommands[0].Stock = foreignStock;
        context.AddRange(employee, otherEmployee, maintenance, otherMaintenance, stock, otherStock, foreignStock);
        context.OrganizationMaterialMovements.AddRange(mismatchedCommands);
        context.MaintenanceWorkOperations.AddRange(
            CreateOperation(organization.Id, maintenance, detailId, employee.Id),
            CreateOperation(organization.Id, otherMaintenance, otherDetailId, employee.Id));
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);

        foreach (var previousMovement in mismatchedCommands)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Commands.IssueForOperationAsync(
                user.Id,
                organization.Id,
                maintenance.Id,
                detailId,
                new IssueOperationMaterialDto
                {
                    StockId = stock.Id,
                    Quantity = 1,
                    Unit = "шт",
                    CommandId = previousMovement.CommandId!.Value
                }));
        }
    }

    [Fact]
    public async Task ReturnRejectsCommandIdsWithDifferentSourceTypeMovementUserOrReason()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var otherUser = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { Name = "Проверка возврата", OwnerUser = user, Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Исполнитель" };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var detailId = Guid.NewGuid();
        var maintenance = CreateMaintenance(user, vehicle, Guid.NewGuid(), detailId, "Работа");
        maintenance.EmployeeId = employee.Id;
        var stock = CreateStock(organization, "Состав");
        var original = new OrganizationMaterialMovement
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
            SourceEntityType = "operation",
            CreatedByUserId = user.Id
        };
        var reason = "Не понадобилось";
        var mismatchedReturns = new[]
        {
            CreatePreviousReturn(Guid.NewGuid(), organization, stock, maintenance, detailId, original.Id, "manual", user.Id, reason),
            CreatePreviousReturn(Guid.NewGuid(), organization, stock, maintenance, detailId, Guid.NewGuid(), "operation_return", user.Id, reason),
            CreatePreviousReturn(Guid.NewGuid(), organization, stock, maintenance, detailId, original.Id, "operation_return", otherUser.Id, reason),
            CreatePreviousReturn(Guid.NewGuid(), organization, stock, maintenance, detailId, original.Id, "operation_return", user.Id, "Другая причина"),
            CreatePreviousReturn(Guid.NewGuid(), organization, stock, maintenance, detailId, original.Id, "operation_return", user.Id, reason)
        };
        mismatchedReturns[^1].SourceEntityId = null;
        context.AddRange(employee, maintenance, stock, original);
        context.OrganizationMaterialMovements.AddRange(mismatchedReturns);
        context.MaintenanceWorkOperations.Add(CreateOperation(organization.Id, maintenance, detailId, employee.Id));
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);

        foreach (var previousMovement in mismatchedReturns)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.Commands.ReverseOperationMaterialAsync(
                user.Id,
                organization.Id,
                maintenance.Id,
                detailId,
                original.Id,
                new ReverseOperationMaterialDto
                {
                    CommandId = previousMovement.CommandId!.Value,
                    Reason = reason
                }));
        }
    }

    [Fact]
    public async Task ReturnUsesStoredUnitAndQuantityWhenOriginalEntryDetailsAreMissing()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { Name = "Проверка старого списания", OwnerUser = user, Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Исполнитель" };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var detailId = Guid.NewGuid();
        var maintenance = CreateMaintenance(user, vehicle, Guid.NewGuid(), detailId, "Работа");
        maintenance.EmployeeId = employee.Id;
        var stock = CreateStock(organization, "Состав");
        stock.QuantityOnHand = 0;
        stock.InventoryValue = 0;
        var original = new OrganizationMaterialMovement
        {
            Organization = organization,
            Stock = stock,
            Maintenance = maintenance,
            OperationId = detailId,
            EmployeeId = employee.Id,
            MemberId = member.Id,
            TypeCode = OrganizationMaterialMovementTypeCodes.MaintenanceIssue,
            Quantity = -2,
            Unit = "шт",
            UnitCost = 10,
            TotalCost = 20,
            SourceEntityType = "operation",
            CreatedByUserId = user.Id
        };
        context.AddRange(employee, maintenance, stock, original);
        context.MaintenanceWorkOperations.Add(CreateOperation(organization.Id, maintenance, detailId, employee.Id));
        await context.SaveChangesAsync();
        using var inventory = CreateInventory(context);

        var returned = await inventory.Commands.ReverseOperationMaterialAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            original.Id,
            new ReverseOperationMaterialDto { CommandId = Guid.NewGuid(), Reason = "Вернули материал" });

        Assert.True(returned.IsReturn);
        Assert.Equal(2, returned.Quantity);
        var returnMovement = await context.OrganizationMaterialMovements.SingleAsync(item => item.Id == returned.Id);
        Assert.Equal(2, returnMovement.EnteredQuantity);
        Assert.Equal("шт", returnMovement.EnteredUnit);
        Assert.Equal(1, returnMovement.ConversionFactor);
    }

    private static OpenCRM.Domain.Maintenance CreateMaintenance(
        ApplicationUser user,
        Vehicle vehicle,
        Guid maintenanceId,
        Guid detailId,
        string detailName) => new()
    {
        Id = maintenanceId,
        User = user,
        Vehicle = vehicle,
        Date = DateTime.UtcNow,
        Status = MaintenanceStatus.InProgress,
        Type = MaintenanceType.Maintenance,
        Details = [new MaintenanceDetail { DetailId = detailId, Name = detailName }]
    };

    private static OrganizationMaterialStock CreateStock(Organization organization, string name) => new()
    {
        Organization = organization,
        Name = name,
        Unit = "шт",
        QuantityOnHand = 10,
        AverageUnitCost = 10,
        InventoryValue = 100
    };

    private static MaintenanceWorkOperation CreateOperation(
        Guid organizationId,
        OpenCRM.Domain.Maintenance maintenance,
        Guid detailId,
        Guid employeeId) => new()
    {
        Id = detailId,
        Maintenance = maintenance,
        OrganizationId = organizationId,
        EmployeeId = employeeId,
        Status = MaintenanceWorkStatus.InProgress
    };

    private static OrganizationMaterialMovement CreatePreviousIssue(
        Guid commandId,
        Organization organization,
        OrganizationMaterialStock stock,
        OpenCRM.Domain.Maintenance maintenance,
        Guid detailId,
        Guid employeeId,
        Guid memberId,
        decimal quantity) => new()
    {
        Organization = organization,
        Stock = stock,
        Maintenance = maintenance,
        OperationId = detailId,
        EmployeeId = employeeId,
        MemberId = memberId,
        CommandId = commandId,
        TypeCode = OrganizationMaterialMovementTypeCodes.MaintenanceIssue,
        Quantity = quantity,
        Unit = "шт",
        SourceEntityType = "operation",
        CreatedByUserId = "existing-user"
    };

    private static OrganizationMaterialMovement CreatePreviousReturn(
        Guid commandId,
        Organization organization,
        OrganizationMaterialStock stock,
        OpenCRM.Domain.Maintenance maintenance,
        Guid detailId,
        Guid sourceMovementId,
        string sourceEntityType,
        string createdByUserId,
        string reason) => new()
    {
        Organization = organization,
        Stock = stock,
        Maintenance = maintenance,
        OperationId = detailId,
        CommandId = commandId,
        TypeCode = OrganizationMaterialMovementTypeCodes.MaintenanceIssue,
        Quantity = 1,
        Unit = "шт",
        SourceEntityType = sourceEntityType,
        SourceEntityId = sourceMovementId,
        CreatedByUserId = createdByUserId,
        Comment = reason
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

        public void Dispose()
        {
            this._scope.Dispose();
            provider.Dispose();
        }
    }
}
