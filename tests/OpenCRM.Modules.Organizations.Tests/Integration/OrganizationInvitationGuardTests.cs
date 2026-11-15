using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationInvitationGuardTests(OrganizationsDatabaseFixture fixture)
{
    [Theory]
    [InlineData("accept", "deleted")]
    [InlineData("accept", "inactive")]
    [InlineData("accept", "archived")]
    [InlineData("accept", "empty_email")]
    [InlineData("accept", "unverified")]
    [InlineData("decline", "missing_user")]
    [InlineData("decline", "empty_email")]
    [InlineData("decline", "unverified")]
    [InlineData("decline", "different_email")]
    [InlineData("decline", "changed_status")]
    [InlineData("decline", "deleted")]
    [InlineData("decline", "inactive")]
    [InlineData("decline", "archived")]
    public async Task InvitationGuardRejectsUnavailableIdentityOrOrganization(string operation, string state)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var user = new ApplicationUser
        {
            Email = state == "empty_email" ? null : $"invitee-{Guid.NewGuid():N}@example.test",
            EmailConfirmed = state != "unverified"
        };
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organization.Id, InvitedByUserId = organization.OwnerUserId,
            Email = user.Email ?? "invitee@example.test",
            NormalizedEmail = state == "different_email" ? "OTHER@EXAMPLE.TEST" : (user.Email ?? "invitee@example.test").ToUpperInvariant(),
            StatusCode = state == "changed_status" ? OrganizationInvitationStatusCodes.Revoked : OrganizationInvitationStatusCodes.Pending
        };
        organization.IsDeleted = state == "deleted";
        organization.IsActive = state != "inactive";
        organization.IsArchived = state == "archived";
        context.ApplicationUsers.Add(user);
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);
        var actor = state == "missing_user" ? "missing-user" : user.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => operation == "accept"
            ? service.AcceptInvitationAsync(actor, invitation.Id)
            : service.DeclineInvitationAsync(actor, invitation.Id));

        Assert.Null(await context.OrganizationInvitations.AsNoTracking().Where(value => value.Id == invitation.Id)
            .Select(value => value.AcceptedByUserId).SingleAsync());
        Assert.False(await context.OrganizationMembers.AsNoTracking().AnyAsync(value => value.UserId == user.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvitationDeletedBeforeOrganizationLockIsRejected(bool accept)
    {
        var interceptor = new BeforeLockInterceptor("FROM \"Organizations\"");
        await using var context = fixture.CreateContext(interceptor);
        var organization = await fixture.CreateOrganizationAsync(context);
        var user = new ApplicationUser { Email = $"removed-{Guid.NewGuid():N}@example.test", EmailConfirmed = true };
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organization.Id, InvitedByUserId = organization.OwnerUserId,
            Email = user.Email, NormalizedEmail = user.Email.ToUpperInvariant()
        };
        context.ApplicationUsers.Add(user);
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        interceptor.Arm(async () =>
        {
            await using var concurrent = fixture.CreateContext();
            await concurrent.OrganizationInvitations.Where(value => value.Id == invitation.Id).ExecuteDeleteAsync();
        });
        var service = fixture.CreateInvitationCommandService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => accept
            ? service.AcceptInvitationAsync(user.Id, invitation.Id)
            : service.DeclineInvitationAsync(user.Id, invitation.Id));

        Assert.Equal(1, interceptor.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmployeeInvitationRejectsOversizedNameAndPhone(bool longName)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = fixture.CreateInvitationCommandService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateEmployeeAndInviteAsync(organization.OwnerUserId,
            organization.Id, new CreateOrganizationEmployeeInvitationDto
            {
                Name = longName ? new string('я', 251) : "Мастер", Phone = longName ? null : new string('1', 51),
                Email = $"employee-{Guid.NewGuid():N}@example.test"
            }));

        Assert.False(await context.OrganizationEmployees.AnyAsync(value => value.OrganizationId == organization.Id));
        Assert.False(await context.OrganizationInvitations.AnyAsync(value => value.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptPreservesExistingEmployeeLinkAndRejectsSecondCard(bool anotherCard)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var member = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        member.User.EmailConfirmed = true;
        var employee = new OrganizationEmployee
        {
            OrganizationId = organization.Id, Name = "Карточка из приглашения", MemberId = anotherCard ? null : member.Id
        };
        context.OrganizationEmployees.Add(employee);
        if (anotherCard)
        {
            context.OrganizationEmployees.Add(new OrganizationEmployee { OrganizationId = organization.Id, Name = "Существующая карточка", MemberId = member.Id });
        }

        var invitation = new OrganizationInvitation
        {
            OrganizationId = organization.Id, InvitedByUserId = organization.OwnerUserId,
            Email = member.User.Email!, NormalizedEmail = member.User.Email!.ToUpperInvariant(), Employee = employee
        };
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        var service = fixture.CreateInvitationCommandService(context);
        if (anotherCard)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptInvitationAsync(member.UserId, invitation.Id));
        }
        else
        {
            await service.AcceptInvitationAsync(member.UserId, invitation.Id);
        }

        Assert.Equal(anotherCard ? (Guid?)null : member.Id, await context.OrganizationEmployees.AsNoTracking()
            .Where(value => value.Id == employee.Id).Select(value => value.MemberId).SingleAsync());
    }

    [Fact]
    public async Task ResendRejectsInvitationChangedBetweenLookupAndRowLock()
    {
        var interceptor = new BeforeLockInterceptor("FROM \"OrganizationInvitations\"");
        await using var context = fixture.CreateContext(interceptor);
        var organization = await fixture.CreateOrganizationAsync(context);
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organization.Id, InvitedByUserId = organization.OwnerUserId,
            Email = "changed@example.test", NormalizedEmail = "CHANGED@EXAMPLE.TEST"
        };
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        interceptor.Arm(async () =>
        {
            await using var concurrent = fixture.CreateContext();
            await concurrent.OrganizationInvitations.Where(value => value.Id == invitation.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.StatusCode, OrganizationInvitationStatusCodes.Declined));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateInvitationCommandService(context)
            .InviteMemberAsync(organization.OwnerUserId, organization.Id, new InviteOrganizationMemberDto { Email = invitation.Email }));

        Assert.Equal(1, interceptor.Calls);
        Assert.Equal(OrganizationInvitationStatusCodes.Declined, await context.OrganizationInvitations.AsNoTracking()
            .Where(value => value.Id == invitation.Id).Select(value => value.StatusCode).SingleAsync());
    }

    private sealed class BeforeLockInterceptor(string table) : DbCommandInterceptor
    {
        private Func<Task>? _change;

        internal int Calls { get; private set; }

        internal void Arm(Func<Task> change) => this._change = change;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (this._change != null && command.CommandText.Contains(table, StringComparison.Ordinal)
                && command.CommandText.Contains("FOR ", StringComparison.Ordinal))
            {
                var change = this._change;
                this._change = null;
                this.Calls++;
                await change();
            }

            return result;
        }
    }
}
