using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationInvitationEmployeeLinkTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task InvitingMemberCreatesAndUpdatesPendingInvitationAndRefreshesNotification()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var invitee = new ApplicationUser { Email = "performer-invite-create@example.test", EmailConfirmed = true };
        context.ApplicationUsers.Add(invitee);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);
        var request = new InviteOrganizationMemberDto
        {
            Email = invitee.Email!,
            RoleCode = OrganizationRoleCodes.Performer
        };

        await service.InviteMemberAsync(organization.OwnerUserId, organization.Id, request);
        var invitation = await context.OrganizationInvitations.SingleAsync(item => item.OrganizationId == organization.Id);
        var firstNotification = await context.SiteNotifications.SingleAsync(item =>
            item.TypeCode == SiteNotificationTypes.OrganizationInvitation && item.RecipientUserId == invitee.Id);
        var previousToken = invitation.Token;

        await service.InviteMemberAsync(organization.OwnerUserId, organization.Id, request);
        invitation = await context.OrganizationInvitations.SingleAsync(item => item.Id == invitation.Id);

        Assert.NotEqual(previousToken, invitation.Token);
        Assert.Equal(OrganizationRoleCodes.Performer, invitation.RoleCode);
        Assert.True(await context.SiteNotifications.AnyAsync(item => item.Id == firstNotification.Id && item.IsDeleted));
        var currentNotification = await context.SiteNotifications.SingleAsync(item =>
            item.TypeCode == SiteNotificationTypes.OrganizationInvitation
            && item.RecipientUserId == invitee.Id
            && !item.IsDeleted);
        Assert.Equal(SiteNotificationAudiences.All, currentNotification.AudienceMask);

        await service.RevokeInvitationAsync(organization.OwnerUserId, organization.Id, invitation.Id);

        Assert.True(await context.SiteNotificationClosures.AnyAsync(item =>
            item.NotificationId == currentNotification.Id && item.UserId == invitee.Id));
        Assert.Equal(OrganizationInvitationStatusCodes.Revoked, await context.OrganizationInvitations
            .Where(item => item.Id == invitation.Id)
            .Select(item => item.StatusCode)
            .SingleAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreatingEmployeeAndInvitationPersistsAndLinksSelectedEmployeeAfterAcceptance(bool isActive)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, "Студия");
        var invitee = new ApplicationUser
        {
            Email = $"new-performer-{Guid.NewGuid():N}@example.test",
            EmailConfirmed = true
        };
        context.ApplicationUsers.Add(invitee);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);

        var invitationDto = new CreateOrganizationEmployeeInvitationDto
        {
            Name = "Иван Мастер",
            Phone = "+7 900 000-00-00",
            Email = invitee.Email!,
            RoleCode = OrganizationRoleCodes.Performer
        };
        if (!isActive)
        {
            invitationDto.IsActive = false;
        }

        var result = await service.CreateEmployeeAndInviteAsync(
            organization.OwnerUserId,
            organization.Id,
            invitationDto);

        var employee = await context.OrganizationEmployees.SingleAsync(item => item.Id == result.EmployeeId);
        var invitation = await context.OrganizationInvitations.SingleAsync(item => item.Id == result.InvitationId);
        Assert.Equal("Иван Мастер", employee.Name);
        Assert.Equal("+7 900 000-00-00", employee.Phone);
        Assert.Equal(isActive, employee.IsActive);
        Assert.Equal(employee.Id, invitation.EmployeeId);
        Assert.Equal(OrganizationInvitationStatusCodes.Pending, invitation.StatusCode);
        Assert.Equal(OrganizationRoleCodes.Performer, invitation.RoleCode);
        await service.AcceptInvitationAsync(invitee.Id, invitation.Id);

        var member = await context.OrganizationMembers.SingleAsync(item =>
            item.OrganizationId == organization.Id && item.UserId == invitee.Id);
        Assert.Equal(member.Id, await context.OrganizationEmployees
            .Where(item => item.Id == employee.Id)
            .Select(item => item.MemberId)
            .SingleAsync());
        Assert.Equal(isActive, await context.OrganizationEmployees
            .Where(item => item.Id == employee.Id)
            .Select(item => item.IsActive)
            .SingleAsync());
        Assert.Equal(invitee.Id, await context.OrganizationInvitations
            .Where(item => item.Id == invitation.Id)
            .Select(item => item.AcceptedByUserId)
            .SingleAsync());
        Assert.True(await context.SiteNotifications.AnyAsync(item =>
            item.TypeCode == SiteNotificationTypes.InvitationAccepted
            && item.RecipientUserId == organization.OwnerUserId));
    }

    [Fact]
    public async Task InvalidInvitationRollsBackNewEmployeeAndAudit()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = fixture.CreateInvitationCommandService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateEmployeeAndInviteAsync(
            organization.OwnerUserId,
            organization.Id,
            new CreateOrganizationEmployeeInvitationDto
            {
                Name = "Мастер",
                Email = "owner-role@example.test",
                RoleCode = OrganizationRoleCodes.Owner
            }));

        await using var verificationContext = fixture.CreateContext();
        Assert.Empty(await verificationContext.OrganizationEmployees
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync());
        Assert.Empty(await verificationContext.OrganizationInvitations
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync());
        Assert.Empty(await verificationContext.OrganizationAuditEvents
            .Where(item => item.OrganizationId == organization.Id && item.EventType == "employees.created")
            .ToListAsync());
    }

    [Fact]
    public async Task CreatingEmployeeRejectsAnEmailWithAnExistingPendingInvitation()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var existingInvitation = new OrganizationInvitation
        {
            OrganizationId = organization.Id,
            Email = "already-invited@example.test",
            NormalizedEmail = "ALREADY-INVITED@EXAMPLE.TEST",
            InvitedByUserId = organization.OwnerUserId
        };
        context.OrganizationInvitations.Add(existingInvitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateEmployeeAndInviteAsync(
            organization.OwnerUserId,
            organization.Id,
            new CreateOrganizationEmployeeInvitationDto
            {
                Name = "Мастер",
                Email = existingInvitation.Email,
                RoleCode = OrganizationRoleCodes.Employee
            }));

        Assert.False(await context.OrganizationEmployees.AnyAsync(item => item.OrganizationId == organization.Id));
    }
}
