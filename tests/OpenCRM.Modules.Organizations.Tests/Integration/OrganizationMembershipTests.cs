using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Organizations.Contracts;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationMembershipTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task OwnershipTransferChangesBothRolesWithoutAccountProfile()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var member = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        var previousOwner = organization.OwnerUserId;
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);
        await services.GetRequiredService<IOrganizationMembershipService>().TransferOwnershipAsync(previousOwner, organization.Id, member.Id);
        var saved = await OrganizationCommandHarness.ReadAsync(context, organization.Id);

        Assert.Equal(member.UserId, saved.OwnerUserId);
        Assert.Equal(OrganizationRoleCodes.Admin, saved.Members.Single(value => value.UserId == previousOwner).RoleCode);
        Assert.Equal(OrganizationRoleCodes.Owner, saved.Members.Single(value => value.Id == member.Id).RoleCode);
        var audit = await context.OrganizationAuditEvents.AsNoTracking().SingleAsync(value => value.OrganizationId == organization.Id);
        Assert.Equal("members.owner.transferred", audit.EventType);
        Assert.Equal(member.Id.ToString("D"), audit.TargetId);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing_owner")]
    [InlineData("missing_member")]
    [InlineData("self")]
    public async Task OwnershipFailurePreservesOwnerAndRoles(string failure)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var member = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        var previousOwner = organization.OwnerUserId;
        var ownerMemberId = organization.Members.Single(value => value.UserId == previousOwner).Id;
        if (failure == "missing_owner")
        {
            context.OrganizationMembers.Remove(organization.Members.Single(value => value.Id == ownerMemberId));
            await context.SaveChangesAsync();
        }

        using var services = OrganizationCommandHarness.CreateServices(context);
        var targetId = failure == "self" ? ownerMemberId : failure == "missing_member" ? Guid.NewGuid() : member.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IOrganizationMembershipService>()
            .TransferOwnershipAsync(failure == "foreign" ? member.UserId : previousOwner, organization.Id, targetId));
        var saved = await OrganizationCommandHarness.ReadAsync(context, organization.Id);

        Assert.Equal(previousOwner, saved.OwnerUserId);
        Assert.Equal(OrganizationRoleCodes.Employee, saved.Members.Single(value => value.Id == member.Id).RoleCode);
        Assert.False(await context.OrganizationAuditEvents.AsNoTracking().AnyAsync(value => value.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task ConcurrentTransfersKeepExactlyOneOwner()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var first = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        var second = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        var ownerId = organization.OwnerUserId;
        var results = await Task.WhenAll(AttemptAsync(first.Id), AttemptAsync(second.Id));
        var saved = await OrganizationCommandHarness.ReadAsync(context, organization.Id);

        Assert.Single(results, value => value);
        var owner = Assert.Single(saved.Members, value => value.RoleCode == OrganizationRoleCodes.Owner);
        Assert.Equal(owner.UserId, saved.OwnerUserId);
        Assert.Equal(1, await context.OrganizationAuditEvents.CountAsync(value => value.OrganizationId == organization.Id));

        async Task<bool> AttemptAsync(Guid memberId)
        {
            await using var attempt = fixture.CreateContext();
            using var services = OrganizationCommandHarness.CreateServices(attempt);
            try
            {
                await services.GetRequiredService<IOrganizationMembershipService>().TransferOwnershipAsync(ownerId, organization.Id, memberId);

                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    [Theory]
    [InlineData("leave", false)]
    [InlineData("leave", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    public async Task LeavingOrRemovingMemberUnlinksEmployeeWithoutDeletingCard(string operation, bool linkedEmployee)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var member = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        OrganizationEmployee? employee = null;
        if (linkedEmployee)
        {
            employee = new OrganizationEmployee { OrganizationId = organization.Id, Name = "Сотрудник", MemberId = member.Id };
            context.OrganizationEmployees.Add(employee);
            await context.SaveChangesAsync();
        }

        using var services = OrganizationCommandHarness.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationMembershipService>();
        if (operation == "leave")
        {
            await service.LeaveOrganizationAsync(member.UserId, organization.Id);
        }
        else
        {
            await service.RemoveMemberAsync(organization.OwnerUserId, organization.Id, member.Id);
        }

        Assert.False(await context.OrganizationMembers.AsNoTracking().AnyAsync(value => value.Id == member.Id));
        Assert.Equal(linkedEmployee ? 2 : 1, await context.OrganizationAuditEvents.CountAsync(value => value.OrganizationId == organization.Id));
        if (employee != null)
        {
            var saved = await context.OrganizationEmployees.AsNoTracking().SingleAsync(value => value.Id == employee.Id);
            Assert.Null(saved.MemberId);
            Assert.True(saved.IsActive);
        }
    }

    [Theory]
    [InlineData("leave", "owner_role")]
    [InlineData("leave", "owner_id")]
    [InlineData("leave", "missing")]
    [InlineData("remove", "owner_role")]
    [InlineData("remove", "owner_id")]
    [InlineData("remove", "missing")]
    [InlineData("role", "owner_role")]
    [InlineData("role", "new_owner")]
    [InlineData("role", "missing")]
    public async Task CommandsRejectInvalidMemberActions(string operation, string state)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var member = await OrganizationCommandHarness.AddMemberAsync(context, organization,
            state == "owner_role" ? OrganizationRoleCodes.Owner : OrganizationRoleCodes.Employee);
        if (state == "owner_id")
        {
            organization.OwnerUserId = member.UserId;
            await context.SaveChangesAsync();
        }

        using var services = OrganizationCommandHarness.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationMembershipService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => operation switch
        {
            "leave" => service.LeaveOrganizationAsync(state == "missing" ? "missing" : member.UserId, organization.Id),
            "remove" => service.RemoveMemberAsync(organization.OwnerUserId, organization.Id, state == "missing" ? Guid.NewGuid() : member.Id),
            _ => service.UpdateMemberRoleAsync(organization.OwnerUserId, organization.Id,
                state == "missing" ? Guid.NewGuid() : member.Id, state == "new_owner" ? OrganizationRoleCodes.Owner : OrganizationRoleCodes.Admin)
        });
        Assert.True(await context.OrganizationMembers.AsNoTracking().AnyAsync(value => value.Id == member.Id));
        Assert.False(await context.OrganizationAuditEvents.AsNoTracking().AnyAsync(value => value.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoleUpdateNormalizesUnknownRolesAndSkipsUnchangedRole(bool unchanged)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var member = await OrganizationCommandHarness.AddMemberAsync(context, organization, OrganizationRoleCodes.Viewer);
        using var services = OrganizationCommandHarness.CreateServices(context);
        await services.GetRequiredService<IOrganizationMembershipService>().UpdateMemberRoleAsync(organization.OwnerUserId,
            organization.Id, member.Id, unchanged ? " VIEWER " : "unknown");
        var saved = await context.OrganizationMembers.AsNoTracking().SingleAsync(value => value.Id == member.Id);

        Assert.Equal(unchanged ? OrganizationRoleCodes.Viewer : OrganizationRoleCodes.Employee, saved.RoleCode);
        Assert.Equal(unchanged ? 0 : 1, await context.OrganizationAuditEvents.CountAsync(value => value.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData("role", MaintenanceWorkStatus.InProgress)]
    [InlineData("role", MaintenanceWorkStatus.Paused)]
    [InlineData("remove", MaintenanceWorkStatus.InProgress)]
    [InlineData("leave", MaintenanceWorkStatus.Paused)]
    public async Task ActiveEmployeeWorkPreventsRoleChangeAndRemoval(string operation, MaintenanceWorkStatus status)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var member = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        var employee = new OrganizationEmployee { OrganizationId = organization.Id, Name = "Сотрудник", MemberId = member.Id };
        var vehicle = new Vehicle { OrganizationId = organization.Id, UserId = organization.OwnerUserId };
        var maintenance = new OpenCRM.Domain.Maintenance { Vehicle = vehicle, UserId = organization.OwnerUserId };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = Guid.NewGuid(), Maintenance = maintenance, OrganizationId = organization.Id, EmployeeId = employee.Id,
            Status = status
        });
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationMembershipService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => operation switch
        {
            "role" => service.UpdateMemberRoleAsync(organization.OwnerUserId, organization.Id, member.Id, OrganizationRoleCodes.Viewer),
            "remove" => service.RemoveMemberAsync(organization.OwnerUserId, organization.Id, member.Id),
            _ => service.LeaveOrganizationAsync(member.UserId, organization.Id)
        });

        Assert.Equal(member.Id, await context.OrganizationEmployees.AsNoTracking().Where(value => value.Id == employee.Id)
            .Select(value => value.MemberId).SingleAsync());
        Assert.Equal(OrganizationRoleCodes.Employee, await context.OrganizationMembers.AsNoTracking().Where(value => value.Id == member.Id)
            .Select(value => value.RoleCode).SingleAsync());
    }
}
