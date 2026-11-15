using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Queries;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationQueryTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task GetByIdMapsMembersInvitationsScenarioAndPermissionFlags()
    {
        await using var context = fixture.CreateContext();
        var owner = CreateUser("query-owner");
        var employee = new ApplicationUser { Email = "member-b@example.test", UserName ="member-b" };
        var earlierEmployee = new ApplicationUser { Email = "member-a@example.test", UserName ="member-a" };
        var organization = CreateOrganization(owner, "Рабочая организация", "service-station");
        organization.Members.Add(new OrganizationMember { User = employee, RoleCode = OrganizationRoleCodes.Employee });
        organization.Members.Add(new OrganizationMember { User = earlierEmployee, RoleCode = OrganizationRoleCodes.Employee });
        organization.Invitations.Add(CreateInvitation(organization, owner, "pending@example.test", "PENDING@EXAMPLE.TEST", OrganizationInvitationStatusCodes.Pending));
        organization.Invitations.Add(CreateInvitation(organization, owner, "member@example.test", "MEMBER@EXAMPLE.TEST", OrganizationInvitationStatusCodes.Pending));
        organization.Invitations.Add(CreateInvitation(organization, owner, "MEMBER@example.test", "MEMBER@EXAMPLE.TEST", OrganizationInvitationStatusCodes.Declined, DateTime.UtcNow.AddMinutes(1)));
        var employeeRecord = new OrganizationEmployee { Organization = organization, Name = "Мастер" };
        organization.Invitations.Add(CreateInvitation(
            organization,
            owner,
            "employee@example.test",
            "EMPLOYEE@EXAMPLE.TEST",
            OrganizationInvitationStatusCodes.Pending,
            employee: employeeRecord));
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var permissionMock = CreatePermissions(
            OrganizationPermissionCodes.MembersInvite,
            OrganizationPermissionCodes.WorkspaceProfileEdit,
            OrganizationPermissionCodes.BillingManage,
            OrganizationPermissionCodes.WorkspaceArchive);
        var service = CreateService(context, permissionMock.Object);

        var result = await service.GetByIdAsync(owner.Id, organization.Id);

        Assert.Equal("Автосервис", result.PrimaryScenarioName);
        Assert.Null(result.DaysLeft);
        Assert.Equal(3, result.MembersCount);
        Assert.Equal([owner.Id, earlierEmployee.Id, employee.Id], result.Members.Select(member => member.UserId));
        Assert.Contains(result.Members, member => member.UserId == owner.Id && member.IsOwner);
        Assert.Equal("Сотрудник", result.Members.Single(member => member.UserId == employee.Id).RoleName);
        Assert.Equal(["employee@example.test", "member@example.test", "pending@example.test"], result.PendingInvitations.Select(invitation => invitation.Email));
        Assert.Equal(["employee@example.test", "MEMBER@example.test", "pending@example.test"], result.Invitations.Select(invitation => invitation.Email));
        Assert.Equal("Отклонено", result.Invitations.Single(invitation => invitation.Email == "MEMBER@example.test").StatusName);
        Assert.Equal("Мастер", result.PendingInvitations.Single(invitation => invitation.EmployeeId == employeeRecord.Id).EmployeeName);
        Assert.True(result.CanManageMembers);
        Assert.True(result.CanManageSettings);
        Assert.True(result.CanManageBilling);
        Assert.False(result.CanArchive);
        Assert.Equal(3, permissionMock.Invocations.Count);
    }

    [Fact]
    public async Task GetByIdFallsBackToScenarioCodeAndChecksEveryPermissionWhenDenied()
    {
        await using var context = fixture.CreateContext();
        var owner = CreateUser("query-fallback-owner");
        var organization = CreateOrganization(owner, "Неизвестный тип", "custom-type");
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var permissionMock = CreatePermissions();
        var service = CreateService(context, permissionMock.Object);

        var result = await service.GetByIdAsync(owner.Id, organization.Id);

        Assert.Equal("custom-type", result.PrimaryScenarioName);
        Assert.False(result.CanManageMembers);
        Assert.False(result.CanManageSettings);
        Assert.False(result.CanManageBilling);
        Assert.False(result.CanArchive);
        Assert.Equal(6, permissionMock.Invocations.Count);
    }

    [Fact]
    public async Task GetByIdChecksLastPermissionInEachAlternativeGroup()
    {
        await using var context = fixture.CreateContext();
        var owner = CreateUser("query-last-permission-owner");
        var organization = CreateOrganization(owner, "Права", "detailing");
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var permissions = CreatePermissions(
            OrganizationPermissionCodes.MembersRemove,
            OrganizationPermissionCodes.WorkspaceRequisitesEdit);
        var service = CreateService(context, permissions.Object);

        var result = await service.GetByIdAsync(owner.Id, organization.Id);

        Assert.True(result.CanManageMembers);
        Assert.True(result.CanManageSettings);
        Assert.False(result.CanManageBilling);
        Assert.False(result.CanArchive);
        Assert.Equal(6, permissions.Invocations.Count);
    }

    [Fact]
    public async Task GetByIdRejectsMissingOrganizationsAndNonMembers()
    {
        await using var context = fixture.CreateContext();
        var owner = CreateUser("query-access-owner");
        var outsider = CreateUser("query-outsider");
        var organization = CreateOrganization(owner, "Закрытая", "detailing");
        context.Organizations.Add(organization);
        context.ApplicationUsers.Add(outsider);
        await context.SaveChangesAsync();
        var service = CreateService(context, CreatePermissions().Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetByIdAsync(outsider.Id, organization.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetByIdAsync(owner.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByIdDoesNotAllowEmployeeToRestoreArchivedOrganization()
    {
        await using var context = fixture.CreateContext();
        var owner = CreateUser("query-restore-owner");
        var employee = CreateUser("query-restore-employee");
        var organization = CreateOrganization(owner, "Архив для сотрудника", "detailing");
        organization.Members.Add(new OrganizationMember { User = employee, RoleCode = OrganizationRoleCodes.Employee });
        organization.Archive(DateTime.UtcNow);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var service = CreateService(context, CreatePermissions().Object);

        var result = await service.GetByIdAsync(employee.Id, organization.Id);

        Assert.Equal(OrganizationRoleCodes.Employee, result.CurrentUserRoleCode);
        Assert.False(result.CanRestore);
    }

    [Fact]
    public async Task GetByIdArchivedOrganizationDoesNotRestoreByOwnerIdWhenMemberRoleIsEmployee()
    {
        await using var context = fixture.CreateContext();
        var owner = CreateUser("query-restore-owner-role");
        var employee = CreateUser("query-restore-owner-id");
        var organization = CreateOrganization(owner, "Архив с устаревшей ролью", "detailing");
        organization.Members.Add(new OrganizationMember { User = employee, RoleCode = OrganizationRoleCodes.Employee });
        organization.Archive(DateTime.UtcNow);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        organization.OwnerUserId = employee.Id;
        await context.SaveChangesAsync();
        var service = CreateService(context, CreatePermissions().Object);

        var result = await service.GetByIdAsync(employee.Id, organization.Id);

        Assert.False(result.CanRestore);
    }

    [Fact]
    public async Task GetOverviewFiltersAndOrdersActiveAndArchivedOrganizations()
    {
        await using var context = fixture.CreateContext();
        var owner = CreateUser("query-overview-owner");
        var alpha = CreateOrganization(owner, "Альфа", "detailing");
        var zulu = CreateOrganization(owner, "Янтарь", "detailing");
        var inactive = CreateOrganization(owner, "Неактивная", "detailing");
        inactive.IsActive = false;
        var olderArchive = CreateOrganization(owner, "Старый архив", "detailing");
        olderArchive.Archive(DateTime.UtcNow.AddDays(-5).AddMinutes(-1));
        olderArchive.Invitations.Add(CreateInvitation(olderArchive, owner, "archived@example.test", "ARCHIVED@EXAMPLE.TEST", OrganizationInvitationStatusCodes.Pending));
        var newerArchive = CreateOrganization(owner, "Новый архив", "detailing");
        newerArchive.Archive(DateTime.UtcNow.AddDays(-2));
        var deleted = CreateOrganization(owner, "Удалённая", "detailing");
        deleted.Delete(DateTime.UtcNow);
        context.Organizations.AddRange(alpha, zulu, inactive, olderArchive, newerArchive, deleted);
        await context.SaveChangesAsync();
        var permissions = CreatePermissions(OrganizationPermissionCodes.MembersRolesManage);
        var service = CreateService(context, permissions.Object);

        var result = await service.GetOverviewAsync(owner.Id);

        Assert.Equal(["Альфа", "Янтарь"], result.Organizations.Select(organization => organization.Name));
        Assert.Equal(["Новый архив", "Старый архив"], result.ArchivedOrganizations.Select(organization => organization.Name));
        Assert.True(result.HasOrganizations);
        Assert.True(result.HasArchivedOrganizations);
        Assert.True(result.Organizations.All(organization => organization.CanManageMembers));
        Assert.All(result.ArchivedOrganizations, organization =>
        {
            Assert.False(organization.CanManageMembers);
            Assert.False(organization.CanManageSettings);
            Assert.False(organization.CanManageBilling);
            Assert.False(organization.CanArchive);
            Assert.True(organization.CanRestore);
            Assert.Empty(organization.PendingInvitations);
            Assert.Empty(organization.Invitations);
        });
        Assert.Equal(25, result.ArchivedOrganizations.Single(organization => organization.Name == "Старый архив").DaysLeft);
    }

    [Fact]
    public async Task GetByIdArchivedOrganizationUsesArchivedPermissionRulesAndExpiredRetention()
    {
        await using var context = fixture.CreateContext();
        var owner = CreateUser("query-archived-owner");
        var organization = CreateOrganization(owner, "Архив", "detailing");
        organization.Archive(DateTime.UtcNow.AddDays(-40));
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var permissions = CreatePermissions(
            OrganizationPermissionCodes.MembersInvite,
            OrganizationPermissionCodes.WorkspaceProfileEdit,
            OrganizationPermissionCodes.BillingManage,
            OrganizationPermissionCodes.WorkspaceArchive);
        var service = CreateService(context, permissions.Object);

        var result = await service.GetByIdAsync(owner.Id, organization.Id);

        Assert.Equal(0, result.DaysLeft);
        Assert.True(result.CanRestore);
        Assert.False(result.CanManageMembers);
        Assert.False(result.CanManageSettings);
        Assert.False(result.CanManageBilling);
        Assert.False(result.CanArchive);
        Assert.Empty(permissions.Invocations);
    }

    [Fact]
    public async Task GetOverviewReturnsEmptyFlagsWhenUserHasNoOrganizations()
    {
        await using var context = fixture.CreateContext();
        var user = CreateUser("query-empty-user");
        context.ApplicationUsers.Add(user);
        await context.SaveChangesAsync();
        var service = CreateService(context, CreatePermissions().Object);

        var result = await service.GetOverviewAsync(user.Id);

        Assert.Empty(result.Organizations);
        Assert.Empty(result.ArchivedOrganizations);
        Assert.False(result.HasOrganizations);
        Assert.False(result.HasArchivedOrganizations);
    }

    private static OrganizationQueryService CreateService(OpenCRMContext context, IOrganizationPermissionService permissions) =>
        new(context, permissions, new OrganizationScenarioReader(context), new ConfigurationBuilder().Build());

    private static Mock<IOrganizationPermissionService> CreatePermissions(params string[] grantedCodes)
    {
        var granted = grantedCodes.ToHashSet(StringComparer.Ordinal);
        var mock = new Mock<IOrganizationPermissionService>(MockBehavior.Strict);
        mock.Setup(service => service.HasPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((string _, Guid _, string permissionCode) => granted.Contains(permissionCode));
        return mock;
    }

    private static ApplicationUser CreateUser(string prefix)
    {
        var unique = Guid.NewGuid().ToString("N");
        return new ApplicationUser
        {
            Email = $"{prefix}-{unique}@example.test",
            UserName = $"{prefix}-{unique}"
        };
    }

    private static Organization CreateOrganization(ApplicationUser owner, string name, string scenarioCode)
    {
        return new Organization
        {
            Name = name,
            PrimaryScenarioCode = scenarioCode,
            OwnerUser = owner,
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
    }

    private static OrganizationInvitation CreateInvitation(
        Organization organization,
        ApplicationUser invitedBy,
        string email,
        string normalizedEmail,
        string statusCode,
        DateTime? createdAt = null,
        OrganizationEmployee? employee = null)
    {
        return new OrganizationInvitation
        {
            Organization = organization,
            InvitedByUser = invitedBy,
            Email = email,
            NormalizedEmail = normalizedEmail,
            StatusCode = statusCode,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Employee = employee
        };
    }
}
