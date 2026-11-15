using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Testing;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class AdminUsersServiceTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task FullOverviewLoadsOnlyTheRequestedUsersVehiclesAndOrganizations()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = $"overview-{Guid.NewGuid():N}@example.test" };
        var foreignUser = new ApplicationUser { Email = $"foreign-{Guid.NewGuid():N}@example.test" };
        var organization = new Organization
        {
            Name = "Организация пользователя", OwnerUser = owner,
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        var foreignOrganization = new Organization
        {
            Name = "Чужая организация", OwnerUser = foreignUser,
            Members = [new OrganizationMember { User = foreignUser, RoleCode = OrganizationRoleCodes.Owner }]
        };
        var vehicle = new Vehicle { User = owner, Organization = organization, Name = "Свой автомобиль" };
        var foreignVehicle = new Vehicle { User = foreignUser, Organization = foreignOrganization };
        context.Vehicles.AddRange(vehicle, foreignVehicle);
        await context.SaveChangesAsync();

        var result = await new AdminUsersService(context).GetOverviewAsync(owner.Id);

        Assert.NotNull(result);
        Assert.Equal(owner.Id, result.Profile.Id);
        Assert.Equal(vehicle.Id, Assert.Single(result.Vehicles).Id);
        Assert.Contains(result.Organizations, item => item.OrganizationId == organization.Id);
        Assert.DoesNotContain(result.Organizations, item => item.OrganizationId == foreignOrganization.Id);
        Assert.Null(await new AdminUsersService(context).GetOverviewAsync("missing-user"));
    }

    [Fact]
    public async Task OrganizationContentsAreReturnedOnlyForOrganizationMembers()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var otherOwner = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Доступная организация",
            OwnerUser = owner,
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        var otherOrganization = new Organization
        {
            Name = "Чужая организация",
            OwnerUser = otherOwner,
            Members = [new OrganizationMember { User = otherOwner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.AddRange(organization, otherOrganization);
        await context.SaveChangesAsync();
        var service = new AdminUsersService(context);

        var ownContents = await service.GetOrganizationContentsAsync(owner.Id, organization.Id);
        var foreignContents = await service.GetOrganizationContentsAsync(owner.Id, otherOrganization.Id);

        Assert.NotNull(ownContents);
        Assert.Equal(organization.Name, ownContents.Name);
        Assert.Single(ownContents.Members);
        Assert.Null(foreignContents);
    }

    [Fact]
    public async Task TestSubscriptionAccessChangesOnlyRequestedUser()
    {
        await using var context = fixture.CreateContext();
        var targetUser = new ApplicationUser();
        var otherUser = new ApplicationUser();
        context.ApplicationUsers.AddRange(targetUser, otherUser);
        await context.SaveChangesAsync();
        var service = new AdminUsersService(context);

        var updated = await service.UpdateTestSubscriptionsAccessAsync(targetUser.Id, true);
        var userFlags = await context.ApplicationUsers
            .AsNoTracking()
            .Where(user => user.Id == targetUser.Id || user.Id == otherUser.Id)
            .ToDictionaryAsync(user => user.Id, user => user.CanUseTestSubscriptions);

        Assert.True(updated);
        Assert.True(userFlags[targetUser.Id]);
        Assert.False(userFlags[otherUser.Id]);
        Assert.False(await service.UpdateTestSubscriptionsAccessAsync("missing-user", true));
    }
}
