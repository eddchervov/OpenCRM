using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationInvitationQueryTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task InvitationUsesCatalogScenarioName()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var user = new ApplicationUser { Email = $"scenario-invitee-{Guid.NewGuid():N}@example.test" };
        context.ApplicationUsers.Add(user);
        context.OrganizationInvitations.Add(new OrganizationInvitation
        {
            OrganizationId = organization.Id, InvitedByUserId = organization.OwnerUserId,
            Email = user.Email, NormalizedEmail = user.Email.ToUpperInvariant()
        });
        await context.SaveChangesAsync();
        var result = await fixture.CreateInvitationQueryService(context).GetInvitationsAsync(user.Id);

        Assert.Equal("Детейлинг", Assert.Single(result).OrganizationScenarioName);
    }

    [Fact]
    public async Task QueriesFilterPendingAndReturnLatestInvitationPerOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.PrimaryScenarioCode = "custom-scenario";
        var invitee = new ApplicationUser { Email = "invitee-query@example.test", EmailConfirmed = true };
        var employee = new OrganizationEmployee
        {
            OrganizationId = organization.Id,
            Name = "Мастер приглашённый"
        };
        var pendingInvitation = new OrganizationInvitation
        {
            OrganizationId = organization.Id,
            Employee = employee,
            Email = invitee.Email,
            NormalizedEmail = invitee.Email!.ToUpperInvariant(),
            RoleCode = OrganizationRoleCodes.Performer,
            InvitedByUserId = organization.OwnerUserId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };
        var acceptedInvitation = new OrganizationInvitation
        {
            OrganizationId = organization.Id,
            Email = "previous-email@example.test",
            NormalizedEmail = "PREVIOUS-EMAIL@EXAMPLE.TEST",
            RoleCode = OrganizationRoleCodes.Employee,
            StatusCode = OrganizationInvitationStatusCodes.Accepted,
            InvitedByUserId = organization.OwnerUserId,
            AcceptedByUser = invitee,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        context.ApplicationUsers.Add(invitee);
        context.OrganizationEmployees.Add(employee);
        context.OrganizationInvitations.AddRange(pendingInvitation, acceptedInvitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationQueryService(context);

        var pending = await service.GetPendingInvitationsAsync(invitee.Id);
        var all = await service.GetInvitationsAsync(invitee.Id);

        var pendingItem = Assert.Single(pending);
        Assert.Equal(pendingInvitation.Id, pendingItem.InvitationId);
        Assert.Equal("Мастер приглашённый", pendingItem.EmployeeName);
        Assert.Equal(OrganizationRoleCodes.Performer, pendingItem.RoleCode);
        Assert.True(pendingItem.CanAccept);
        Assert.Equal("custom-scenario", pendingItem.OrganizationScenarioName);
        var allItem = Assert.Single(all);
        Assert.Equal(acceptedInvitation.Id, allItem.InvitationId);
        Assert.Equal(OrganizationInvitationStatusCodes.Accepted, allItem.StatusCode);
    }

    [Fact]
    public async Task QueriesIgnoreUsersWithoutEmailAndUnavailableOrganizations()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var noEmailUser = new ApplicationUser();
        var invitee = new ApplicationUser { Email = "inactive-org@example.test" };
        organization.IsActive = false;
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organization.Id,
            Email = invitee.Email!,
            NormalizedEmail = invitee.Email!.ToUpperInvariant(),
            InvitedByUserId = organization.OwnerUserId
        };
        context.ApplicationUsers.AddRange(noEmailUser, invitee);
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationQueryService(context);

        Assert.Empty(await service.GetPendingInvitationsAsync("missing-user"));
        Assert.Empty(await service.GetInvitationsAsync(noEmailUser.Id));
        Assert.Empty(await service.GetPendingInvitationsAsync(invitee.Id));
    }
}
