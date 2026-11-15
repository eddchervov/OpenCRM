using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using Npgsql;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationTransactionTests(OrganizationsDatabaseFixture fixture)
{
    [Theory]
    [InlineData("create", false)]
    [InlineData("create", true)]
    [InlineData("profile", false)]
    [InlineData("profile", true)]
    [InlineData("requisites", false)]
    [InlineData("requisites", true)]
    [InlineData("update", false)]
    [InlineData("update", true)]
    [InlineData("archive", false)]
    [InlineData("archive", true)]
    [InlineData("restore", false)]
    [InlineData("restore", true)]
    [InlineData("transfer", false)]
    [InlineData("transfer", true)]
    [InlineData("role", false)]
    [InlineData("role", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    [InlineData("leave", false)]
    [InlineData("leave", true)]
    [InlineData("open", false)]
    [InlineData("open", true)]
    public async Task CommitRetryKeepsExactlyOneCommandResult(string operation, bool failAfterCommit)
    {
        var failure = new OrganizationCommitFailureInterceptor(failAfterCommit);
        await using var context = fixture.CreateContext(failure);
        var organization = await fixture.CreateOrganizationAsync(context);
        var member = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        var ownerId = organization.OwnerUserId;
        if (operation == "restore")
        {
            organization.Archive(DateTime.UtcNow);
            await context.SaveChangesAsync();
        }

        if (operation == "open")
        {
            organization.SetupAutoOpenPending = true;
            await context.SaveChangesAsync();
        }

        failure.Arm();
        using var services = OrganizationCommandHarness.CreateServices(context, archivingEnabled: true);
        var profile = services.GetRequiredService<IOrganizationProfileCommandService>();
        var membership = services.GetRequiredService<IOrganizationMembershipService>();
        var archive = services.GetRequiredService<IOrganizationArchiveService>();
        Guid? createdId = null;
        switch (operation)
        {
            case "open":
                Assert.True(await services.GetRequiredService<IOrganizationSetupService>().TryOpenSetupAsync(ownerId, organization.Id));
                break;
            case "create":
                createdId = await profile.CreateAsync(ownerId, new CreateOrganizationDto { Name = "Команда создания" });
                break;
            case "profile":
                await profile.UpdateProfileAsync(ownerId, organization.Id, new UpdateOrganizationProfileDto { Name = "Изменено" });
                break;
            case "requisites":
                await profile.UpdateRequisitesAsync(ownerId, organization.Id, new UpdateOrganizationRequisitesDto { Inn = "1234567890" });
                break;
            case "update":
                await profile.UpdateAsync(ownerId, organization.Id, new UpdateOrganizationDto { Name = "Изменено" });
                break;
            case "archive":
                await archive.ArchiveAsync(ownerId, organization.Id);
                break;
            case "restore":
                await archive.RestoreAsync(ownerId, organization.Id);
                break;
            case "transfer":
                await membership.TransferOwnershipAsync(ownerId, organization.Id, member.Id);
                break;
            case "role":
                await membership.UpdateMemberRoleAsync(ownerId, organization.Id, member.Id, OrganizationRoleCodes.Viewer);
                break;
            case "remove":
                await membership.RemoveMemberAsync(ownerId, organization.Id, member.Id);
                break;
            case "leave":
                await membership.LeaveOrganizationAsync(member.UserId, organization.Id);
                break;
        }

        Assert.Equal(1, failure.Failures);
        Assert.Equal(failAfterCommit ? 1 : 2, failure.CommitAttempts);
        var saved = await OrganizationCommandHarness.ReadAsync(context, createdId ?? organization.Id);
        Assert.Equal(operation == "create" ? 0 : 1,
            await context.OrganizationAuditEvents.CountAsync(value => value.OrganizationId == saved.Id));
        switch (operation)
        {
            case "open":
                Assert.False(saved.SetupAutoOpenPending);
                break;
            case "create":
                Assert.NotEqual(Guid.Empty, createdId);
                Assert.Equal("Команда создания", saved.Name);
                Assert.Single(saved.Members);
                break;
            case "profile":
            case "update":
                Assert.Equal("Изменено", saved.Name);
                break;
            case "requisites":
                Assert.Equal("1234567890", saved.Inn);
                break;
            case "archive":
                Assert.True(saved.IsArchived);
                break;
            case "restore":
                Assert.False(saved.IsArchived);
                Assert.True(saved.IsActive);
                break;
            case "transfer":
                Assert.Equal(member.UserId, saved.OwnerUserId);
                Assert.Single(saved.Members, value => value.RoleCode == OrganizationRoleCodes.Owner);
                break;
            case "role":
                Assert.Equal(OrganizationRoleCodes.Viewer, saved.Members.Single(value => value.Id == member.Id).RoleCode);
                break;
            case "leave":
            case "remove":
                Assert.DoesNotContain(saved.Members, value => value.Id == member.Id);
                break;
        }
    }

    [Fact]
    public async Task ExistingTransactionOwnsCommitAndRollback()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            using var services = OrganizationCommandHarness.CreateServices(context);
            await services.GetRequiredService<IOrganizationProfileCommandService>().UpdateProfileAsync(organization.OwnerUserId,
                organization.Id, new UpdateOrganizationProfileDto { Name = "Внешняя транзакция" });
            Assert.Same(transaction, context.Database.CurrentTransaction);
            await transaction.RollbackAsync();
        });

        Assert.NotEqual("Внешняя транзакция", (await OrganizationCommandHarness.ReadAsync(context, organization.Id)).Name);
        Assert.False(await context.OrganizationAuditEvents.AnyAsync(value => value.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task InvitationCommitRetryPreservesResultAndLaterResend(
        bool createEmployee,
        bool failAfterCommit,
        bool resendAfterCommit)
    {
        Guid organizationId = default;
        string ownerId = string.Empty;
        var invitee = new OpenCRM.Domain.Identity.ApplicationUser
        {
            Email = $"retry-{Guid.NewGuid():N}@example.test",
            EmailConfirmed = true
        };
        string? laterToken = null;
        var failure = new OrganizationCommitFailureInterceptor(failAfterCommit, async () =>
        {
            if (!resendAfterCommit)
            {
                return;
            }

            await using var laterContext = fixture.CreateContext();
            var invitation = await laterContext.OrganizationInvitations.AsNoTracking()
                .SingleAsync(value => value.OrganizationId == organizationId);
            await fixture.CreateInvitationCommandService(laterContext).InviteMemberAsync(ownerId, organizationId,
                new InviteOrganizationMemberDto
                {
                    Email = invitee.Email!,
                    RoleCode = OrganizationRoleCodes.Viewer,
                    EmployeeId = invitation.EmployeeId
                });
            laterToken = await laterContext.OrganizationInvitations.AsNoTracking()
                .Where(value => value.Id == invitation.Id)
                .Select(value => value.Token)
                .SingleAsync();
        });
        await using var context = fixture.CreateContext(failure);
        var organization = await fixture.CreateOrganizationAsync(context);
        organizationId = organization.Id;
        ownerId = organization.OwnerUserId;
        context.ApplicationUsers.Add(invitee);
        await context.SaveChangesAsync();
        failure.Arm();
        var service = fixture.CreateInvitationCommandService(context);
        CreateOrganizationEmployeeInvitationResultDto? result = null;
        if (createEmployee)
        {
            result = await service.CreateEmployeeAndInviteAsync(ownerId, organizationId,
                new CreateOrganizationEmployeeInvitationDto
                {
                    Name = "Повтор команды",
                    Email = invitee.Email!,
                    RoleCode = OrganizationRoleCodes.Performer
                });
        }
        else
        {
            await service.InviteMemberAsync(ownerId, organizationId, new InviteOrganizationMemberDto
            {
                Email = invitee.Email!,
                RoleCode = OrganizationRoleCodes.Performer
            });
        }

        Assert.Equal(1, failure.Failures);
        Assert.Equal(failAfterCommit ? 1 : 2, failure.CommitAttempts);
        await using var savedContext = fixture.CreateContext();
        var saved = await savedContext.OrganizationInvitations.AsNoTracking()
            .SingleAsync(value => value.OrganizationId == organizationId);
        Assert.Equal(resendAfterCommit ? OrganizationRoleCodes.Viewer : OrganizationRoleCodes.Performer, saved.RoleCode);
        Assert.Equal(OrganizationInvitationStatusCodes.Pending, saved.StatusCode);
        if (resendAfterCommit)
        {
            Assert.NotNull(laterToken);
            Assert.Equal(laterToken, saved.Token);
        }

        if (createEmployee)
        {
            Assert.Equal(result!.InvitationId, saved.Id);
            Assert.Equal(result.EmployeeId, saved.EmployeeId);
            var employee = await savedContext.OrganizationEmployees.AsNoTracking()
                .SingleAsync(value => value.OrganizationId == organizationId);
            Assert.Equal(result.EmployeeId, employee.Id);
            Assert.Equal("Повтор команды", employee.Name);
        }

        Assert.Equal((createEmployee ? 2 : 1) + (resendAfterCommit ? 1 : 0),
            await savedContext.OrganizationAuditEvents.CountAsync(value => value.OrganizationId == organizationId));
        Assert.Equal(1, await savedContext.SiteNotifications.CountAsync(value =>
            value.RecipientUserId == invitee.Id && !value.IsDeleted));
    }
}
