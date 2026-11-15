using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Modules.Organizations;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationPermissionServiceTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task RoleMatrixUsesDefaultsAndKeepsCustomOverridesWithinOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var otherOrganization = await fixture.CreateOrganizationAsync(context);
        var employee = await fixture.AddMemberAsync(context, organization,
            new OpenCRM.Domain.Identity.ApplicationUser { Email = $"employee-{Guid.NewGuid():N}@example.test" });
        var otherEmployee = await fixture.AddMemberAsync(context, otherOrganization,
            new OpenCRM.Domain.Identity.ApplicationUser { Email = $"employee-{Guid.NewGuid():N}@example.test" });
        var service = new OrganizationPermissionService(context, new OrganizationAuditService(context));

        var defaults = service.GetDefaultRoleMatrix();
        var initialMatrix = await service.GetRoleMatrixAsync(organization.OwnerUserId, organization.Id);
        Assert.Equal(defaults[OrganizationRoleCodes.Employee].Order(StringComparer.Ordinal),
            GetRole(initialMatrix, OrganizationRoleCodes.Employee).Permissions
                .Where(permission => permission.IsEnabled).Select(permission => permission.Code).Order(StringComparer.Ordinal));
        Assert.Equal(defaults[OrganizationRoleCodes.Viewer].Order(StringComparer.Ordinal),
            GetRole(initialMatrix, OrganizationRoleCodes.Viewer).Permissions
                .Where(permission => permission.IsEnabled).Select(permission => permission.Code).Order(StringComparer.Ordinal));

        await service.UpdateRolePermissionsAsync(organization.OwnerUserId, organization.Id, new UpdateOrganizationRolePermissionsDto
        {
            RoleCode = " EMPLOYEE ",
            EnabledPermissionCodes = [" WORKSPACE.VIEW ", OrganizationPermissionCodes.WorkspaceProfileEdit, "unknown.permission"]
        });

        Assert.True(await service.HasPermissionAsync(employee.UserId, organization.Id, OrganizationPermissionCodes.WorkspaceProfileEdit));
        Assert.True(await service.HasPermissionAsync(employee.UserId, organization.Id, OrganizationPermissionCodes.WorkspaceView));
        Assert.False(await service.HasPermissionAsync(otherEmployee.UserId, otherOrganization.Id, OrganizationPermissionCodes.WorkspaceProfileEdit));
        var overrides = await context.OrganizationRolePermissionOverrides.AsNoTracking()
            .Where(value => value.OrganizationId == organization.Id)
            .ToListAsync();
        Assert.Contains(overrides, value => value.RoleCode == OrganizationRoleCodes.Employee
            && value.PermissionCode == OrganizationPermissionCodes.WorkspaceProfileEdit
            && value.IsEnabled
            && value.UpdatedByUserId == organization.OwnerUserId);
        Assert.Empty(await context.OrganizationRolePermissionOverrides.AsNoTracking()
            .Where(value => value.OrganizationId == otherOrganization.Id)
            .ToListAsync());
        Assert.Equal("permissions.role.updated", await context.OrganizationAuditEvents.AsNoTracking()
            .Where(value => value.OrganizationId == organization.Id)
            .Select(value => value.EventType).SingleAsync());
    }

    [Fact]
    public async Task RolePermissionEditingProtectsOwnerAndViewerAndRequiresManagerMembership()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var viewer = await fixture.AddMemberAsync(context, organization,
            new OpenCRM.Domain.Identity.ApplicationUser { Email = $"viewer-{Guid.NewGuid():N}@example.test" }, OrganizationRoleCodes.Viewer);
        var service = new OrganizationPermissionService(context, new OrganizationAuditService(context));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateRolePermissionsAsync(organization.OwnerUserId,
            organization.Id, UpdatePermissions(OrganizationRoleCodes.Owner)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateRolePermissionsAsync(organization.OwnerUserId,
            organization.Id, UpdatePermissions(OrganizationRoleCodes.Viewer)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateRolePermissionsAsync(viewer.UserId,
            organization.Id, UpdatePermissions(OrganizationRoleCodes.Employee)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetRoleMatrixAsync("outside-user", organization.Id));

        Assert.Empty(await context.OrganizationRolePermissionOverrides.Where(item => item.OrganizationId == organization.Id).ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.Where(item => item.OrganizationId == organization.Id).ToListAsync());
    }

    [Fact]
    public async Task ManagingInquiriesRequiresInquiryReadPermission()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = new OrganizationPermissionService(context, new OrganizationAuditService(context));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateRolePermissionsAsync(
            organization.OwnerUserId,
            organization.Id,
            new UpdateOrganizationRolePermissionsDto
            {
                RoleCode = OrganizationRoleCodes.Employee,
                EnabledPermissionCodes =
                [
                    OrganizationPermissionCodes.WorkspaceView,
                    OrganizationPermissionCodes.InquiriesManage
                ]
            }));

        Assert.Empty(await context.OrganizationRolePermissionOverrides.AsNoTracking()
            .Where(item => item.OrganizationId == organization.Id).ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking()
            .Where(item => item.OrganizationId == organization.Id).ToListAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("archived")]
    [InlineData("inactive")]
    [InlineData("nonmember")]
    public async Task PermissionChecksDenyUnavailableOrganizationsAndNonmembers(string state)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        if (state == "deleted")
        {
            organization.IsDeleted = true;
        }
        else if (state == "archived")
        {
            organization.IsArchived = true;
        }
        else if (state == "inactive")
        {
            organization.IsActive = false;
        }

        if (state != "missing" && state != "nonmember")
        {
            await context.SaveChangesAsync();
        }

        var userId = state == "nonmember" ? "outside-user" : organization.OwnerUserId;
        var service = new OrganizationPermissionService(context, new OrganizationAuditService(context));
        var organizationId = state == "missing" ? Guid.NewGuid() : organization.Id;

        Assert.False(await service.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceView));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetRoleMatrixAsync(userId, organizationId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsurePermissionAsync(userId, organizationId,
            OrganizationPermissionCodes.WorkspaceView));
    }

    private static OrganizationRolePermissionMatrixDto GetRole(
        IReadOnlyList<OrganizationRolePermissionMatrixDto> matrix,
        string roleCode) => matrix.Single(role => role.RoleCode == roleCode);

    private static UpdateOrganizationRolePermissionsDto UpdatePermissions(string roleCode) => new()
    {
        RoleCode = roleCode,
        EnabledPermissionCodes = [OrganizationPermissionCodes.WorkspaceView]
    };
}
