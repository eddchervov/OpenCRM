using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class OperationMaterialQueryTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task QueriesAvailableAndIssuedMaterialsForAssignedEmployee()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner };
        var organization = new Organization { Name = "Студия", OwnerUser = user, Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var detailId = Guid.NewGuid();
        var inheritedAssignmentDetailId = Guid.NewGuid();
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
                new MaintenanceDetail { DetailId = detailId, Name = "Полировка", EmployeeId = employee.Id },
                new MaintenanceDetail { DetailId = inheritedAssignmentDetailId, Name = "Работа по умолчанию" }
            ]
        };
        var stock = new OrganizationMaterialStock
        {
            Organization = organization, Name = "Паста", Unit = "кг", PackageQuantity = 600,
            QuantityOnHand = 2, ReservedQuantity = 0
        };
        var issue = new OrganizationMaterialMovement
        {
            Organization = organization, Stock = stock, Maintenance = maintenance, OperationId = detailId,
            TypeCode = OrganizationMaterialMovementTypeCodes.MaintenanceIssue, Quantity = -0.5m,
            Unit = "кг", SourceEntityType = "operation", CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };
        var returned = new OrganizationMaterialMovement
        {
            Organization = organization, Stock = stock, Maintenance = maintenance, OperationId = detailId,
            TypeCode = OrganizationMaterialMovementTypeCodes.MaintenanceIssue, Quantity = 0.25m,
            Unit = "кг", SourceEntityType = "operation_return", SourceEntityId = issue.Id,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        context.AddRange(employee, maintenance, stock, issue, returned,
            new OrganizationMaterialStock
            {
                Organization = organization, Name = "Закончился", Unit = "шт", QuantityOnHand = 1, ReservedQuantity = 1
            },
            new OrganizationMaterialStock
            {
                Organization = new Organization { Name = "Другой склад", OwnerUser = new ApplicationUser() },
                Name = "Чужой материал", Unit = "шт", QuantityOnHand = 5
            });
        await context.SaveChangesAsync();
        var service = new OperationMaterialQueryService(context, Mock.Of<IOrganizationSubscriptionService>());

        var available = await service.GetAvailableForOperationAsync(user.Id, organization.Id, maintenance.Id, detailId);
        var availableStock = Assert.Single(available);
        Assert.Equal(stock.Id, availableStock.Id);
        Assert.Equal("кг", availableStock.Unit);
        Assert.Equal(600, availableStock.PackageQuantity);
        Assert.Equal(2, availableStock.AvailableQuantity);
        Assert.Single(await service.GetAvailableForOperationAsync(user.Id, organization.Id, maintenance.Id, inheritedAssignmentDetailId));

        var movements = await service.GetIssuedForOperationAsync(user.Id, organization.Id, maintenance.Id, detailId);
        Assert.Equal(2, movements.Count);
        Assert.Equal(returned.Id, movements[0].Id);
        Assert.True(movements[0].IsReturn);
        Assert.Equal(issue.Id, movements[0].SourceMovementId);
        Assert.Equal(0.25m, movements[0].Quantity);
        Assert.Equal(issue.Id, movements[1].Id);
        Assert.False(movements[1].IsReturn);
        Assert.Null(movements[1].SourceMovementId);
        Assert.Equal(0.5m, movements[1].Quantity);
    }

    [Fact]
    public async Task QueriesRejectMissingMemberEmployeeMaintenanceAndUnassignedWork()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner };
        var organization = new Organization { Name = "Студия", OwnerUser = user, Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var detailId = Guid.NewGuid();
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Date = DateTime.UtcNow,
            Status = MaintenanceStatus.InProgress,
            Type = MaintenanceType.Maintenance,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Полировка", EmployeeId = employee.Id }]
        };
        context.AddRange(employee, maintenance);
        await context.SaveChangesAsync();
        var service = new OperationMaterialQueryService(context, Mock.Of<IOrganizationSubscriptionService>());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAvailableForOperationAsync(
            "missing-member", organization.Id, maintenance.Id, detailId));
        employee.IsActive = false;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAvailableForOperationAsync(
            user.Id, organization.Id, maintenance.Id, detailId));
        employee.IsActive = true;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAvailableForOperationAsync(
            user.Id, organization.Id, Guid.NewGuid(), detailId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAvailableForOperationAsync(
            user.Id, organization.Id, maintenance.Id, Guid.NewGuid()));

        var detail = maintenance.Details.Single();
        detail.MaterialName = "Состав";
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAvailableForOperationAsync(
            user.Id, organization.Id, maintenance.Id, detailId));
        detail.MaterialName = null;
        detail.MaterialCatalogItemId = Guid.NewGuid();
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAvailableForOperationAsync(
            user.Id, organization.Id, maintenance.Id, detailId));
        detail.MaterialCatalogItemId = null;
        detail.MaterialQuantity = 1;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAvailableForOperationAsync(
            user.Id, organization.Id, maintenance.Id, detailId));
        detail.MaterialQuantity = null;
        detail.EmployeeId = Guid.NewGuid();
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAvailableForOperationAsync(
            user.Id, organization.Id, maintenance.Id, detailId));
        detail.EmployeeId = employee.Id;
        maintenance.IsDeleted = true;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAvailableForOperationAsync(
            user.Id, organization.Id, maintenance.Id, detailId));
    }
}
