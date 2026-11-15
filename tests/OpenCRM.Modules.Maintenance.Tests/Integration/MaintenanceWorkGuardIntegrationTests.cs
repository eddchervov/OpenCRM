using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceWorkGuardIntegrationTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task EnsureTrackedWorkCompletedBlocksPendingAndUnfinishedWorkThenAllowsCompletedWork()
    {
        await using var context = fixture.CreateContext();
        var (organization, maintenance, employee) = await CreateMaintenanceAsync(context, "staged");
        var serviceDetail = maintenance.Details.Single();
        var materialDetail = new MaintenanceDetail
        {
            DetailId = Guid.NewGuid(),
            Name = "Масло",
            MaterialName = "Масло",
            MaterialQuantity = 1
        };
        maintenance.ReplaceDetails([serviceDetail, materialDetail]);
        await context.SaveChangesAsync();

        var guard = new MaintenanceWorkGuard(context);
        context.MaintenanceWorkReports.Add(new MaintenanceWorkReport
        {
            CommandId = Guid.NewGuid(),
            OrganizationId = organization.Id,
            MaintenanceId = maintenance.Id,
            Maintenance = maintenance,
            DetailId = serviceDetail.DetailId,
            EmployeeId = employee.Id,
            MemberId = Guid.NewGuid(),
            ActorUserId = "pending",
            Kind = "extra",
            Text = "Дополнительная работа",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.EnsureTrackedWorkCompletedAsync(maintenance));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.EnsureTrackedWorkCompletedAsync(maintenance, allowUntrackedAssignedWork: true));

        var pendingReport = await context.MaintenanceWorkReports.SingleAsync(report => report.MaintenanceId == maintenance.Id);
        pendingReport.Decision = "rejected";
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.EnsureTrackedWorkCompletedAsync(maintenance));

        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = serviceDetail.DetailId,
            MaintenanceId = maintenance.Id,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.Completed
        });
        await context.SaveChangesAsync();

        await guard.EnsureTrackedWorkCompletedAsync(maintenance);
    }

    [Fact]
    public async Task EnsureTrackedWorkCompletedAllowsSimpleWorkflowButRequiresTrackingForAssignedWork()
    {
        await using var context = fixture.CreateContext();
        var (organization, maintenance, employee) = await CreateMaintenanceAsync(context, "simple");
        var guard = new MaintenanceWorkGuard(context);

        await guard.EnsureTrackedWorkCompletedAsync(maintenance);

        organization.MaintenanceWorkflowMode = "staged";
        maintenance.Details.Single().EmployeeId = employee.Id;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.EnsureTrackedWorkCompletedAsync(maintenance));

        var (detailingOrganization, detailingMaintenance, _) = await CreateMaintenanceAsync(context, "staged");
        detailingOrganization.PrimaryScenarioCode = "detailing";
        await context.SaveChangesAsync();
        await guard.EnsureTrackedWorkCompletedAsync(detailingMaintenance);
    }

    [Fact]
    public async Task SimpleHandoverAllowsUntrackedAssignedWorkButBlocksStartedOperations()
    {
        await using var context = fixture.CreateContext();
        var (organization, maintenance, employee) = await CreateMaintenanceAsync(context, "simple");
        organization.PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation;
        var detail = maintenance.Details.Single();
        detail.EmployeeId = employee.Id;
        await context.SaveChangesAsync();

        var guard = new MaintenanceWorkGuard(context);
        await guard.EnsureTrackedWorkCompletedAsync(maintenance, allowUntrackedAssignedWork: true);

        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = detail.DetailId,
            MaintenanceId = maintenance.Id,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.InProgress
        });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.EnsureTrackedWorkCompletedAsync(maintenance, allowUntrackedAssignedWork: true));
    }

    [Fact]
    public async Task EnsureTrackedWorkCompletedSkipsOrdersContainingOnlyMaterials()
    {
        await using var context = fixture.CreateContext();
        var (_, maintenance, _) = await CreateMaintenanceAsync(context, "staged");
        maintenance.ReplaceDetails([new MaintenanceDetail
        {
            DetailId = Guid.NewGuid(),
            Name = "Паста",
            MaterialCatalogItemId = Guid.NewGuid(),
            MaterialName = "Паста",
            MaterialQuantity = 1
        }]);
        await context.SaveChangesAsync();

        await new MaintenanceWorkGuard(context).EnsureTrackedWorkCompletedAsync(maintenance);
    }

    [Fact]
    public async Task EnsureWorkAllowsStatusChangeBlocksOnlyStartedWorkCancellationOrRescheduling()
    {
        await using var context = fixture.CreateContext();
        var (organization, maintenance, employee) = await CreateMaintenanceAsync(context, "staged");
        var guard = new MaintenanceWorkGuard(context);

        await guard.EnsureWorkAllowsStatusChangeAsync(maintenance, maintenance.Status);
        await guard.EnsureWorkAllowsStatusChangeAsync(maintenance, MaintenanceStatus.Completed);

        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = maintenance.Details.Single().DetailId,
            MaintenanceId = maintenance.Id,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.InProgress
        });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.EnsureWorkAllowsStatusChangeAsync(maintenance, MaintenanceStatus.Cancelled));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.EnsureWorkAllowsStatusChangeAsync(maintenance, MaintenanceStatus.Planned));
    }

    [Fact]
    public async Task ValidateStartedWorkProtectsTrackedAndApprovedRowsButAllowsUnchangedRows()
    {
        await using var context = fixture.CreateContext();
        var (organization, maintenance, employee) = await CreateMaintenanceAsync(context, "staged");
        var tracked = maintenance.Details.Single();
        var approved = new MaintenanceDetail
        {
            DetailId = Guid.NewGuid(),
            Name = "Дополнительная работа",
            CatalogItemId = Guid.NewGuid(),
            Price = 500,
            EmployeeId = employee.Id
        };
        maintenance.ReplaceDetails([tracked, approved]);
        await context.SaveChangesAsync();
        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = tracked.DetailId,
            MaintenanceId = maintenance.Id,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.InProgress
        });
        context.MaintenanceWorkReports.Add(new MaintenanceWorkReport
        {
            CommandId = Guid.NewGuid(),
            OrganizationId = organization.Id,
            MaintenanceId = maintenance.Id,
            Maintenance = maintenance,
            DetailId = tracked.DetailId,
            EmployeeId = employee.Id,
            MemberId = Guid.NewGuid(),
            ActorUserId = "approved",
            Kind = "extra",
            Text = approved.Name,
            Decision = "approved",
            ApprovedDetailId = approved.DetailId,
            ApprovedPrice = approved.Price,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var guard = new MaintenanceWorkGuard(context);
        var unchanged = maintenance.Details.Select(CloneDetail).ToArray();
        await guard.ValidateStartedWorkAsync(maintenance, unchanged, maintenance.EmployeeId);

        var changedTrackedWork = unchanged.Select(CloneDetail).ToArray();
        changedTrackedWork[0].Name = "Изменённая работа";
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.ValidateStartedWorkAsync(
            maintenance, changedTrackedWork, maintenance.EmployeeId));

        var changedApprovedWork = unchanged.Select(CloneDetail).ToArray();
        changedApprovedWork[1].Price = 600;
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.ValidateStartedWorkAsync(
            maintenance, changedApprovedWork, maintenance.EmployeeId));

        var changedCatalogReference = unchanged.Select(CloneDetail).ToArray();
        changedCatalogReference[0].CatalogItemId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.ValidateStartedWorkAsync(
            maintenance, changedCatalogReference, maintenance.EmployeeId));

        var changedEmployee = unchanged.Select(CloneDetail).ToArray();
        changedEmployee[0].EmployeeId = employee.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.ValidateStartedWorkAsync(
            maintenance, changedEmployee, maintenance.EmployeeId));

        var changedQuantity = unchanged.Select(CloneDetail).ToArray();
        changedQuantity[0].Quantity = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.ValidateStartedWorkAsync(
            maintenance, changedQuantity, maintenance.EmployeeId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.ValidateStartedWorkAsync(
            maintenance, unchanged, Guid.NewGuid()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.ValidateStartedWorkAsync(
            maintenance, [unchanged[1]], maintenance.EmployeeId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.ValidateStartedWorkAsync(
            maintenance, unchanged, employee.Id));

        maintenance.EmployeeId = employee.Id;
        await guard.ValidateStartedWorkAsync(maintenance, unchanged, employee.Id);
    }

    private static async Task<(Organization Organization, MaintenanceEntity Maintenance, OrganizationEmployee Employee)>
        CreateMaintenanceAsync(OpenCRMContext context, string workflowMode)
    {
        var user = new ApplicationUser { UserName = $"work-guard-{Guid.NewGuid():N}", Email = "work-guard@example.test" };
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            Name = "Проверка защиты работ",
            OwnerUser = user,
            MaintenanceWorkflowMode = workflowMode,
            Members = [member]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { Name = "Полировка" }]
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();

        return (organization, maintenance, employee);
    }

    private static MaintenanceDetail CloneDetail(MaintenanceDetail detail) => new()
    {
        DetailId = detail.DetailId,
        CatalogItemId = detail.CatalogItemId,
        EmployeeId = detail.EmployeeId,
        Name = detail.Name,
        Quantity = detail.Quantity,
        Price = detail.Price
    };
}
