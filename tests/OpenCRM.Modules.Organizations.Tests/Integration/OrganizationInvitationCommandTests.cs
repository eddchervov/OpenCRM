using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationInvitationCommandTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task AcceptRejectsMissingUserEmailAndUnverifiedAddress()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var invitee = new ApplicationUser { Email = "accept-validation@example.test" };
        var invitation = CreateInvitation(organization, invitee.Email!);
        context.ApplicationUsers.Add(invitee);
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptInvitationAsync("missing-user", invitation.Id));
        invitee = await context.ApplicationUsers.SingleAsync(value => value.Id == invitee.Id);
        invitee.Email = null;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptInvitationAsync(invitee.Id, invitation.Id));
        invitee = await context.ApplicationUsers.SingleAsync(value => value.Id == invitee.Id);
        invitee.Email = "accept-validation@example.test";
        await context.SaveChangesAsync();
        var unverified = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptInvitationAsync(invitee.Id, invitation.Id));

        Assert.Contains("Подтвердите email", unverified.Message);
        Assert.False(await context.OrganizationMembers.AnyAsync(member => member.UserId == invitee.Id));
    }

    [Fact]
    public async Task AcceptRejectsInvitationForAnotherEmailAndArchivedOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var invitee = new ApplicationUser { Email = "different-email@example.test", EmailConfirmed = true };
        var invitation = CreateInvitation(organization, "somebody-else@example.test");
        context.ApplicationUsers.Add(invitee);
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptInvitationAsync(invitee.Id, invitation.Id));
        invitation = await context.OrganizationInvitations.SingleAsync(value => value.Id == invitation.Id);
        organization = await context.Organizations.SingleAsync(value => value.Id == organization.Id);
        invitation.NormalizedEmail = invitee.Email!.ToUpperInvariant();
        organization.IsArchived = true;
        organization.IsActive = false;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptInvitationAsync(invitee.Id, invitation.Id));
        Assert.False(await context.OrganizationMembers.AnyAsync(member => member.UserId == invitee.Id));
    }

    [Fact]
    public async Task AcceptKeepsExistingMemberAndCreatesAcceptedNotification()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var invitee = new ApplicationUser { Email = "existing-member@example.test", EmailConfirmed = true };
        var member = new OrganizationMember
        {
            OrganizationId = organization.Id,
            User = invitee,
            RoleCode = OrganizationRoleCodes.Viewer
        };
        var invitation = CreateInvitation(organization, invitee.Email!, OrganizationRoleCodes.Performer);
        context.ApplicationUsers.Add(invitee);
        context.OrganizationMembers.Add(member);
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);

        await service.AcceptInvitationAsync(invitee.Id, invitation.Id);

        Assert.Equal(1, await context.OrganizationMembers.CountAsync(value => value.UserId == invitee.Id));
        Assert.Equal(OrganizationRoleCodes.Viewer, await context.OrganizationMembers
            .Where(value => value.UserId == invitee.Id)
            .Select(value => value.RoleCode)
            .SingleAsync());
        Assert.True(await context.SiteNotifications.AnyAsync(value =>
            value.TypeCode == SiteNotificationTypes.InvitationAccepted
            && value.RecipientUserId == organization.OwnerUserId
            && value.EventKey == $"invitation-accepted:{invitation.Id}"));
    }

    [Fact]
    public async Task AcceptRejectsForeignAndAlreadyLinkedEmployeeCards()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var invitee = new ApplicationUser { Email = "employee-unavailable@example.test", EmailConfirmed = true };
        var missingEmployeeInvitation = CreateInvitation(organization, invitee.Email!);
        var foreignEmployee = new OrganizationEmployee
        {
            OrganizationId = foreignOrganization.Id,
            Name = "Чужая карточка"
        };
        missingEmployeeInvitation.Employee = foreignEmployee;
        var inviteeId = invitee.Id;
        var invitationId = missingEmployeeInvitation.Id;
        var organizationId = organization.Id;
        context.ApplicationUsers.Add(invitee);
        context.OrganizationEmployees.Add(foreignEmployee);
        context.OrganizationInvitations.Add(missingEmployeeInvitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptInvitationAsync(inviteeId, invitationId));
        context.ChangeTracker.Clear();
        missingEmployeeInvitation = await context.OrganizationInvitations.SingleAsync(value => value.Id == invitationId);
        missingEmployeeInvitation.EmployeeId = null;
        await context.SaveChangesAsync();

        var otherMember = new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = "another-member",
            RoleCode = OrganizationRoleCodes.Employee
        };
        var employee = new OrganizationEmployee
        {
            OrganizationId = organization.Id,
            Name = "Занятая карточка",
            IsActive = false,
            Member = otherMember
        };
        var otherUser = new ApplicationUser { Email = "another-member@example.test" };
        otherMember.User = otherUser;
        employee.OrganizationId = organizationId;
        context.OrganizationMembers.Add(otherMember);
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        missingEmployeeInvitation.EmployeeId = employee.Id;
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptInvitationAsync(inviteeId, invitationId));
        Assert.False(await context.OrganizationMembers.AnyAsync(value => value.UserId == inviteeId));
    }

    [Fact]
    public async Task AcceptRejectsEmployeeAlreadyLinkedToAnotherMember()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var invitee = new ApplicationUser { Email = "employee-linked@example.test", EmailConfirmed = true };
        var otherUser = new ApplicationUser { Email = "linked-user@example.test" };
        var otherMember = new OrganizationMember
        {
            OrganizationId = organization.Id,
            User = otherUser,
            RoleCode = OrganizationRoleCodes.Employee
        };
        var employee = new OrganizationEmployee
        {
            OrganizationId = organization.Id,
            Name = "Карточка",
            Member = otherMember
        };
        var invitation = CreateInvitation(organization, invitee.Email!);
        invitation.Employee = employee;
        context.ApplicationUsers.Add(invitee);
        context.ApplicationUsers.Add(otherUser);
        context.OrganizationMembers.Add(otherMember);
        context.OrganizationEmployees.Add(employee);
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptInvitationAsync(invitee.Id, invitation.Id));
        Assert.False(await context.OrganizationMembers.AnyAsync(value => value.UserId == invitee.Id));
    }

    [Fact]
    public async Task DeclineRequiresVerifiedMatchingEmailAndClosesNotifications()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var invitee = new ApplicationUser { Email = "decline@example.test" };
        var invitation = CreateInvitation(organization, invitee.Email!);
        context.ApplicationUsers.Add(invitee);
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeclineInvitationAsync("missing-user", invitation.Id));
        var unconfirmed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeclineInvitationAsync(invitee.Id, invitation.Id));
        Assert.Contains("Подтвердите email", unconfirmed.Message);
        invitee = await context.ApplicationUsers.SingleAsync(value => value.Id == invitee.Id);
        invitee.EmailConfirmed = true;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeclineInvitationAsync(
            invitee.Id,
            Guid.NewGuid()));

        await service.InviteMemberAsync(organization.OwnerUserId, organization.Id, new InviteOrganizationMemberDto
        {
            Email = invitee.Email!,
            RoleCode = OrganizationRoleCodes.Employee
        });
        var currentInvitation = await context.OrganizationInvitations.SingleAsync(value => value.Id == invitation.Id);
        var notification = await context.SiteNotifications.SingleAsync(value =>
            value.TypeCode == SiteNotificationTypes.OrganizationInvitation && value.RecipientUserId == invitee.Id);

        await service.DeclineInvitationAsync(invitee.Id, currentInvitation.Id);

        Assert.Equal(OrganizationInvitationStatusCodes.Declined, await context.OrganizationInvitations
            .Where(value => value.Id == currentInvitation.Id)
            .Select(value => value.StatusCode)
            .SingleAsync());
        Assert.NotNull(await context.OrganizationInvitations
            .Where(value => value.Id == currentInvitation.Id)
            .Select(value => value.RevokedAt)
            .SingleAsync());
        Assert.True(await context.SiteNotificationClosures.AnyAsync(value =>
            value.NotificationId == notification.Id && value.UserId == invitee.Id));
    }

    [Fact]
    public async Task InviteRejectsOwnerRoleExistingMemberAndFullWorkspace()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var member = new ApplicationUser { Email = "already-member@example.test" };
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            User = member,
            RoleCode = OrganizationRoleCodes.Employee
        });
        await context.SaveChangesAsync();
        var subscriptions = OrganizationInvitationTestServices.CreateSubscription();
        var service = fixture.CreateInvitationCommandService(context, subscriptions.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InviteMemberAsync(
            organization.OwnerUserId,
            organization.Id,
            new InviteOrganizationMemberDto { Email = "owner-role@example.test", RoleCode = OrganizationRoleCodes.Owner }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InviteMemberAsync(
            organization.OwnerUserId,
            organization.Id,
            new InviteOrganizationMemberDto { Email = member.Email!, RoleCode = OrganizationRoleCodes.Employee }));
        subscriptions.Setup(value => value.GetMembersLimitAsync(organization.OwnerUserId, organization.Id, It.IsAny<int>()))
            .ReturnsAsync(2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InviteMemberAsync(
            organization.OwnerUserId,
            organization.Id,
            new InviteOrganizationMemberDto { Email = "capacity@example.test", RoleCode = OrganizationRoleCodes.Employee }));

        Assert.Empty(await context.OrganizationInvitations.Where(value => value.OrganizationId == organization.Id).ToListAsync());
    }

    [Fact]
    public async Task InviteAllowsInactiveEmployeeCardAndRejectsDuplicatePendingEmployeeInvitation()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = fixture.CreateInvitationCommandService(context);
        var request = new InviteOrganizationMemberDto
        {
            Email = "employee-invite@example.test",
            RoleCode = OrganizationRoleCodes.Employee,
            EmployeeId = Guid.NewGuid()
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InviteMemberAsync(
            organization.OwnerUserId,
            organization.Id,
            request));

        var employee = new OrganizationEmployee
        {
            OrganizationId = organization.Id,
            Name = "Мастер",
            IsActive = false
        };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        request.EmployeeId = employee.Id;
        await service.InviteMemberAsync(organization.OwnerUserId, organization.Id, request);
        request.Email = "different-email@example.test";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InviteMemberAsync(
            organization.OwnerUserId,
            organization.Id,
            request));
        Assert.Equal(1, await context.OrganizationInvitations.CountAsync(value => value.EmployeeId == employee.Id));
    }

    [Fact]
    public async Task RevokeRejectsMissingOrAlreadyChangedInvitation()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var invitation = CreateInvitation(organization, "revoke@example.test");
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevokeInvitationAsync(
            organization.OwnerUserId,
            organization.Id,
            Guid.NewGuid()));
        await context.OrganizationInvitations
            .Where(value => value.Id == invitation.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.StatusCode, OrganizationInvitationStatusCodes.Accepted));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevokeInvitationAsync(
            organization.OwnerUserId,
            organization.Id,
            invitation.Id));
    }

    [Fact]
    public async Task InviteRequiresWorkspaceWritePermission()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var subscriptions = OrganizationInvitationTestServices.CreateSubscription();
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId,
                organization.Id,
                It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Нет права приглашать участников."));
        var service = fixture.CreateInvitationCommandService(context, subscriptions.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InviteMemberAsync(
            organization.OwnerUserId,
            organization.Id,
            new InviteOrganizationMemberDto { Email = "denied@example.test", RoleCode = OrganizationRoleCodes.Employee }));
        Assert.Empty(await context.OrganizationInvitations.Where(value => value.OrganizationId == organization.Id).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentInvitesRespectOrganizationMemberLimit()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var lockBarrier = new TwoOrganizationInvitationLockBarrier();
        Mock<IOrganizationSubscriptionService> CreateSynchronizedSubscription()
        {
            var subscription = OrganizationInvitationTestServices.CreateSubscription();
            subscription.Setup(value => value.GetMembersLimitAsync(
                    organization.OwnerUserId,
                    organization.Id,
                    It.IsAny<int>()))
                .ReturnsAsync(2);

            return subscription;
        }

        async Task<Exception?> TryInviteAsync(string email)
        {
            await using var context = fixture.CreateContext(lockBarrier);
            var service = fixture.CreateInvitationCommandService(context, CreateSynchronizedSubscription().Object);
            try
            {
                await service.InviteMemberAsync(organization.OwnerUserId, organization.Id, new InviteOrganizationMemberDto
                {
                    Email = email,
                    RoleCode = OrganizationRoleCodes.Employee
                });
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var firstInvite = TryInviteAsync("concurrent-one@example.test");
        var secondInvite = TryInviteAsync("concurrent-two@example.test");
        try
        {
            await lockBarrier.WaitForBothCommandsAsync();
        }
        finally
        {
            lockBarrier.Release();
        }

        var results = await Task.WhenAll(firstInvite, secondInvite);

        Assert.Single(results, result => result == null);
        Assert.IsType<InvalidOperationException>(Assert.Single(results, result => result != null));
        await using var verifyContext = fixture.CreateContext();
        Assert.Equal(1, await verifyContext.OrganizationInvitations.CountAsync(value => value.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task InviteChangesAreRolledBackWithCallerTransaction()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await fixture.CreateInvitationCommandService(context).InviteMemberAsync(
                organization.OwnerUserId,
                organization.Id,
                new InviteOrganizationMemberDto
                {
                    Email = "rollback-invite@example.test",
                    RoleCode = OrganizationRoleCodes.Employee
                });
            await transaction.RollbackAsync();
        });

        await using var verificationContext = fixture.CreateContext();
        Assert.Empty(await verificationContext.OrganizationInvitations
            .Where(value => value.OrganizationId == organization.Id)
            .ToListAsync());
        Assert.Empty(await verificationContext.EmailMessages
            .Where(value => value.ToEmail == "rollback-invite@example.test")
            .ToListAsync());
        Assert.Empty(await verificationContext.OrganizationAuditEvents
            .Where(value => value.OrganizationId == organization.Id && value.EventType.StartsWith("members.invitation."))
            .ToListAsync());
    }

    private static OrganizationInvitation CreateInvitation(
        Organization organization,
        string email,
        string roleCode = OrganizationRoleCodes.Employee) => new()
        {
            OrganizationId = organization.Id,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            RoleCode = roleCode,
            InvitedByUserId = organization.OwnerUserId
        };
}

internal sealed class TwoOrganizationInvitationLockBarrier : DbCommandInterceptor
{
    private readonly TaskCompletionSource _bothCommands = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _commandCount;

    internal Task WaitForBothCommandsAsync() => this._bothCommands.Task.WaitAsync(TimeSpan.FromSeconds(15));

    internal void Release() => this._release.TrySetResult();

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal))
        {
            if (Interlocked.Increment(ref this._commandCount) == 2)
            {
                this._bothCommands.TrySetResult();
            }

            await this._release.Task.WaitAsync(cancellationToken);
        }

        return result;
    }
}
