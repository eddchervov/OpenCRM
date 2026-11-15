using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationEmployeeServiceTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task EmployeeAndAuditRollBackWithTheCallerTransaction()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = this.CreateService(context);
        Guid employeeId = default;

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            employeeId = await service.SaveAsync(organization.OwnerUserId, organization.Id, null,
                new OrganizationEmployeeDto { Name = "  Полировщик  ", Phone = "  +7 900 0000000  ", IsActive = true });
            Assert.True(await context.OrganizationAuditEvents.AnyAsync(item => item.TargetId == employeeId.ToString()));
            await transaction.RollbackAsync();
        });

        await using var verification = fixture.CreateContext();
        Assert.False(await verification.OrganizationEmployees.AnyAsync(item => item.Id == employeeId));
        Assert.False(await verification.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task UpdateWithStaleVersionLeavesEmployeeAndAuditUnchanged()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = this.CreateService(context);
        var id = await service.SaveAsync(organization.OwnerUserId, organization.Id, null,
            new OrganizationEmployeeDto { Name = "Первое имя", IsActive = true });
        var first = await context.OrganizationEmployees.AsNoTracking().SingleAsync(item => item.Id == id);
        await service.SaveAsync(organization.OwnerUserId, organization.Id, id,
            new OrganizationEmployeeDto { Name = "Второе имя", IsActive = true, Version = first.Version });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(organization.OwnerUserId,
            organization.Id, id, new OrganizationEmployeeDto { Name = "Устаревшее изменение", Version = first.Version }));

        await using var verification = fixture.CreateContext();
        Assert.Equal("Второе имя", await verification.OrganizationEmployees.Where(item => item.Id == id)
            .Select(item => item.Name).SingleAsync());
        Assert.Equal(2, await verification.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData(MaintenanceWorkStatus.InProgress, true)]
    [InlineData(MaintenanceWorkStatus.Paused, true)]
    [InlineData(MaintenanceWorkStatus.InProgress, false)]
    [InlineData(MaintenanceWorkStatus.Paused, false)]
    public async Task ActiveWorkPreventsArchivingOrUnlinking(MaintenanceWorkStatus status, bool archive)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var employee = new OrganizationEmployee
        {
            Organization = organization, MemberId = organization.Members.Single().Id, Name = "Мастер"
        };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            UserId = organization.OwnerUserId,
            Vehicle = new Vehicle { UserId = organization.OwnerUserId, OrganizationId = organization.Id }
        };
        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = Guid.NewGuid(), OrganizationId = organization.Id, EmployeeId = employee.Id,
            Maintenance = maintenance, Status = status
        });
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        var service = this.CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => archive
            ? service.SaveAsync(organization.OwnerUserId, organization.Id, employee.Id,
                new OrganizationEmployeeDto { Name = employee.Name, IsActive = false, Version = employee.Version })
            : service.LinkMemberAsync(organization.OwnerUserId, organization.Id, employee.Id, null, employee.Version));

        await using var verification = fixture.CreateContext();
        var saved = await verification.OrganizationEmployees.SingleAsync(item => item.Id == employee.Id);
        Assert.True(saved.IsActive);
        Assert.Equal(employee.MemberId, saved.MemberId);
        Assert.False(await verification.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task LinkingAlreadyLinkedMemberDoesNotCreateAnotherAuditEvent()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = this.CreateService(context);
        var memberId = organization.Members.Single().Id;
        var id = await service.CreateForMemberAsync(organization.OwnerUserId, organization.Id, memberId, "Мастер", null);
        var employee = await context.OrganizationEmployees.AsNoTracking().SingleAsync(item => item.Id == id);

        await service.LinkMemberAsync(organization.OwnerUserId, organization.Id, id, memberId, employee.Version);

        Assert.Equal(1, await context.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task DeniedEmployeeWriteDoesNotPersistEmployeeOrAudit()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var subscriptions = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.EmployeesManage))
            .ThrowsAsync(new UnauthorizedAccessException("Запись запрещена."));
        var service = new OrganizationEmployeeService(context, subscriptions.Object,
            new OrganizationPermissionService(context, new OrganizationAuditService(context)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(organization.OwnerUserId,
            organization.Id, null, new OrganizationEmployeeDto { Name = "Мастер", IsActive = true }));

        await using var verification = fixture.CreateContext();
        Assert.False(await verification.OrganizationEmployees.AnyAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await verification.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task SaveUpdatesDetailsAndLinksOwnerInOneTransaction()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = this.CreateService(context);
        var employeeId = await service.SaveAsync(organization.OwnerUserId, organization.Id, null,
            new OrganizationEmployeeDto { Name = "Мастер" });
        var employee = await context.OrganizationEmployees.AsNoTracking().SingleAsync(item => item.Id == employeeId);
        var ownerMemberId = organization.Members.Single().Id;

        await service.SaveAsync(organization.OwnerUserId, organization.Id, employeeId,
            new OrganizationEmployeeDto
            {
                Name = "  Новое имя  ", Phone = "+79000000000", IsActive = false,
                MemberIdToLink = ownerMemberId, Version = employee.Version
            });

        await using var verification = fixture.CreateContext();
        var saved = await verification.OrganizationEmployees.SingleAsync(item => item.Id == employeeId);
        Assert.Equal("Новое имя", saved.Name);
        Assert.Equal("+79000000000", saved.Phone);
        Assert.False(saved.IsActive);
        Assert.Equal(ownerMemberId, saved.MemberId);
        Assert.Equal(organization.OwnerUserId, await verification.Organizations.Where(item => item.Id == organization.Id)
            .Select(item => item.OwnerUserId).SingleAsync());
        Assert.Equal(3, await verification.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidLinkRollsBackDetailsAndAudit(bool alreadyLinked)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = this.CreateService(context);
        var employeeId = await service.SaveAsync(organization.OwnerUserId, organization.Id, null,
            new OrganizationEmployeeDto { Name = "Мастер" });
        var employee = await context.OrganizationEmployees.AsNoTracking().SingleAsync(item => item.Id == employeeId);
        var memberId = alreadyLinked ? organization.Members.Single().Id : Guid.NewGuid();
        if (alreadyLinked)
        {
            await service.CreateForMemberAsync(organization.OwnerUserId, organization.Id, memberId, "Другой мастер", null);
        }

        var auditCount = await context.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(
            organization.OwnerUserId, organization.Id, employeeId,
            new OrganizationEmployeeDto
            {
                Name = "Новое имя", IsActive = false, MemberIdToLink = memberId, Version = employee.Version
            }));

        await using var verification = fixture.CreateContext();
        var saved = await verification.OrganizationEmployees.SingleAsync(item => item.Id == employeeId);
        Assert.Equal("Мастер", saved.Name);
        Assert.True(saved.IsActive);
        Assert.Null(saved.MemberId);
        Assert.Equal(employee.Version, saved.Version);
        Assert.Equal(auditCount, await verification.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task DeniedLinkPermissionRollsBackDetailsAndAudit()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var employeeId = await this.CreateService(context).SaveAsync(organization.OwnerUserId, organization.Id, null,
            new OrganizationEmployeeDto { Name = "Мастер" });
        var employee = await context.OrganizationEmployees.AsNoTracking().SingleAsync(item => item.Id == employeeId);
        var subscriptions = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.EmployeesManage))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.MembersRolesManage))
            .ThrowsAsync(new UnauthorizedAccessException("Привязка запрещена."));
        var service = new OrganizationEmployeeService(context, subscriptions.Object,
            new OrganizationPermissionService(context, new OrganizationAuditService(context)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(
            organization.OwnerUserId, organization.Id, employeeId,
            new OrganizationEmployeeDto
            {
                Name = "Новое имя", MemberIdToLink = organization.Members.Single().Id, Version = employee.Version
            }));

        await using var verification = fixture.CreateContext();
        var saved = await verification.OrganizationEmployees.SingleAsync(item => item.Id == employeeId);
        Assert.Equal("Мастер", saved.Name);
        Assert.Null(saved.MemberId);
        Assert.Equal(employee.Version, saved.Version);
        Assert.Equal(1, await verification.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id));
    }

    private OrganizationEmployeeService CreateService(OpenCRMContext context) =>
        new(context, Mock.Of<IOrganizationSubscriptionService>(),
            new OrganizationPermissionService(context, new OrganizationAuditService(context)));
}
