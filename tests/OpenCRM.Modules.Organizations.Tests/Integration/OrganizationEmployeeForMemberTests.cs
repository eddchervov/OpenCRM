using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationEmployeeForMemberTests(OrganizationsDatabaseFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreatesEmployeeCardForExistingMemberAndWritesAuditEvent(bool isActive)
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = $"owner-create-member-card-{Guid.NewGuid():N}@example.test" };
        var organization = new Organization
        {
            Name = "Автосервис",
            OwnerUser = owner,
            Members =
            [
                new OrganizationMember
                {
                    User = owner,
                    RoleCode = OrganizationRoleCodes.Owner
                }
            ]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                owner.Id, organization.Id, OrganizationPermissionCodes.EmployeesManage))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                owner.Id, organization.Id, OrganizationPermissionCodes.MembersRolesManage))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(value => value.EnsureWorkspaceCanReadAsync(
                owner.Id, organization.Id, OrganizationPermissionCodes.EmployeesManage))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(value => value.EnsureWorkspaceCanReadAsync(
                owner.Id, organization.Id, OrganizationPermissionCodes.MembersRolesManage))
            .Returns(Task.CompletedTask);
        var service = new OrganizationEmployeeService(
            context,
            subscriptions.Object,
            Mock.Of<IOrganizationPermissionService>());

        Assert.True(context.Database.CreateExecutionStrategy().RetriesOnFailure);
        var employeeId = isActive
            ? await service.CreateForMemberAsync(
                owner.Id, organization.Id, organization.Members.Single().Id, "  Алексей  ", "  +7 900 000-00-00  ")
            : await service.CreateForMemberAsync(
                owner.Id,
                organization.Id,
                organization.Members.Single().Id,
                "  Алексей  ",
                "  +7 900 000-00-00  ",
                isActive: false);

        var employee = await context.OrganizationEmployees.SingleAsync(item => item.Id == employeeId);
        Assert.Equal("Алексей", employee.Name);
        Assert.Equal("+7 900 000-00-00", employee.Phone);
        Assert.Equal(organization.Members.Single().Id, employee.MemberId);
        Assert.Equal(isActive, employee.IsActive);
        Assert.Contains(await service.GetMemberOptionsAsync(owner.Id, organization.Id), option =>
            option.Id == organization.Members.Single().Id);

        var auditEvent = await context.OrganizationAuditEvents.SingleAsync(item => item.TargetId == employeeId.ToString("D"));
        Assert.Equal(owner.Id, auditEvent.ActorUserId);
        Assert.Equal("employees.created_from_member", auditEvent.EventType);
        Assert.Equal("employee", auditEvent.TargetType);
        subscriptions.Verify(value => value.EnsureWorkspaceCanWriteAsync(
            owner.Id, organization.Id, OrganizationPermissionCodes.EmployeesManage), Times.Once);
        subscriptions.Verify(value => value.EnsureWorkspaceCanWriteAsync(
            owner.Id, organization.Id, OrganizationPermissionCodes.MembersRolesManage), Times.Once);
    }

    [Fact]
    public async Task AllowsOwnerToLinkAndUnlinkTheirOwnAccountWithoutChangingMembership()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "owner-link-own-card@example.test" };
        var organization = new Organization
        {
            Name = "Детейлинг",
            OwnerUser = owner,
            Members =
            [
                new OrganizationMember
                {
                    User = owner,
                    RoleCode = OrganizationRoleCodes.Owner
                }
            ]
        };
        var employee = new OrganizationEmployee { Organization = organization, Name = "Алексей" };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                owner.Id, organization.Id, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var service = new OrganizationEmployeeService(
            context,
            subscriptions.Object,
            Mock.Of<IOrganizationPermissionService>());

        await service.LinkMemberAsync(
            owner.Id, organization.Id, employee.Id, organization.Members.Single().Id, employee.Version);

        await using var verificationContext = fixture.CreateContext();
        Assert.Equal(
            organization.Members.Single().Id,
            await verificationContext.OrganizationEmployees
                .Where(item => item.Id == employee.Id)
                .Select(item => item.MemberId)
                .SingleAsync());

        await context.Entry(employee).ReloadAsync();
        await service.LinkMemberAsync(owner.Id, organization.Id, employee.Id, null, employee.Version);
        var unlinkedEmployee = await verificationContext.OrganizationEmployees.AsNoTracking()
            .SingleAsync(item => item.Id == employee.Id);
        var member = await verificationContext.OrganizationMembers.AsNoTracking()
            .SingleAsync(item => item.Id == organization.Members.Single().Id);

        Assert.Null(unlinkedEmployee.MemberId);
        Assert.Equal("Алексей", unlinkedEmployee.Name);
        Assert.True(unlinkedEmployee.IsActive);
        Assert.Equal(owner.Id, member.UserId);
        Assert.Equal(OrganizationRoleCodes.Owner, member.RoleCode);
        Assert.Equal(owner.Id, await verificationContext.Organizations
            .Where(item => item.Id == organization.Id).Select(item => item.OwnerUserId).SingleAsync());
        Assert.True(await verificationContext.OrganizationAuditEvents.AnyAsync(item =>
            item.TargetId == employee.Id.ToString("D") && item.EventType == "employees.member.unlinked"));
    }

    [Fact]
    public async Task RejectsMemberFromAnotherOrganization()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "owner-create-member-wrong-org@example.test" };
        var anotherOwner = new ApplicationUser { Email = "other-owner-create-member-wrong-org@example.test" };
        var organization = new Organization
        {
            Name = "Автосервис",
            OwnerUser = owner,
            Members =
            [
                new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }
            ]
        };
        var anotherOrganization = new Organization
        {
            Name = "Детейлинг",
            OwnerUser = anotherOwner,
            Members =
            [
                new OrganizationMember { User = anotherOwner, RoleCode = OrganizationRoleCodes.Owner }
            ]
        };
        context.Organizations.AddRange(organization, anotherOrganization);
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                owner.Id, organization.Id, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var service = new OrganizationEmployeeService(
            context,
            subscriptions.Object,
            Mock.Of<IOrganizationPermissionService>());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateForMemberAsync(
            owner.Id,
            organization.Id,
            anotherOrganization.Members.Single().Id,
            "Сотрудник",
            null));

        Assert.Empty(await context.OrganizationEmployees
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync());
    }
}
