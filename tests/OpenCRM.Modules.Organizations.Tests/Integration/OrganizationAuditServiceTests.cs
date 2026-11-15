using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Modules.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationAuditServiceTests(OrganizationsDatabaseFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeferredAuditSaveUsesCallerTransaction(bool commit)
    {
        Guid organizationId;
        var eventId = Guid.NewGuid();
        await using (var context = fixture.CreateContext())
        {
            var organization = await fixture.CreateOrganizationAsync(context);
            organizationId = organization.Id;
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                var service = new OrganizationAuditService(context);
                await service.AddAsync(new OrganizationAuditEventCreateDto
                {
                    EventId = eventId,
                    OrganizationId = organizationId,
                    ActorUserId = organization.OwnerUserId,
                    EventType = "transaction.test",
                    Title = "Аудит внутри транзакции"
                }, saveChanges: false);

                Assert.Single(context.ChangeTracker.Entries<OrganizationAuditEvent>());
                Assert.False(await context.OrganizationAuditEvents.AsNoTracking().AnyAsync(value => value.Id == eventId));
                await context.SaveChangesAsync();
                if (commit)
                {
                    await transaction.CommitAsync();
                }
                else
                {
                    await transaction.RollbackAsync();
                }
            });
        }

        await using var verificationContext = fixture.CreateContext();
        Assert.Equal(commit, await verificationContext.OrganizationAuditEvents.AsNoTracking().AnyAsync(value => value.Id == eventId));
    }

    [Fact]
    public async Task RecentAuditNormalizesFieldsFallsBackForMissingActorAndRejectsNonmember()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = new OrganizationAuditService(context);
        await service.AddAsync(new OrganizationAuditEventCreateDto
        {
            OrganizationId = organization.Id,
            ActorUserId = "deleted-user",
            EventType = "  PROFILE.UPDATED ",
            Title = "  Название изменено  ",
            Description = "   ",
            TargetType = " ORGANIZATION ",
            TargetId = "  profile-1  "
        });

        var recent = Assert.Single(await service.GetRecentAsync(organization.OwnerUserId, organization.Id));
        Assert.Equal("profile.updated", recent.EventType);
        Assert.Equal("Название изменено", recent.Title);
        Assert.Null(recent.Description);
        Assert.Equal("organization", recent.TargetType);
        Assert.Equal("profile-1", recent.TargetId);
        Assert.Equal("Пользователь удален", recent.ActorDisplayName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetRecentAsync("outside-user", organization.Id));
    }
}
