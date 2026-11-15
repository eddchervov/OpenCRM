using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Organizations.Contracts;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class AdminOrganizationArchiveTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task AdminCanArchiveAndRestoreAnyOrganizationWithReasonWhenUserArchivingIsDisabled()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = OrganizationCommandHarness.CreateServices(context);
        var archive = services.GetRequiredService<IOrganizationArchiveService>();

        await archive.ArchiveByAdminAsync("admin-user", organization.Id, "  Решение поддержки  ");

        var archived = await OrganizationCommandHarness.ReadAsync(context, organization.Id);
        Assert.True(archived.IsArchived);
        Assert.False(archived.IsActive);
        var archivedEvent = await context.OrganizationAuditEvents.SingleAsync(item => item.OrganizationId == organization.Id);
        Assert.Equal("admin-user", archivedEvent.ActorUserId);
        Assert.Equal("organization.admin_archived", archivedEvent.EventType);
        Assert.Equal("Причина: Решение поддержки", archivedEvent.Description);

        await archive.RestoreByAdminAsync("another-admin", organization.Id, "Исправлено");

        var restored = await OrganizationCommandHarness.ReadAsync(context, organization.Id);
        Assert.False(restored.IsArchived);
        Assert.True(restored.IsActive);
        Assert.Equal(2, await context.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task AdminArchiveRequiresReasonAndCannotRestoreDeletedOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = OrganizationCommandHarness.CreateServices(context);
        var archive = services.GetRequiredService<IOrganizationArchiveService>();

        await Assert.ThrowsAsync<ArgumentException>(() => archive.ArchiveByAdminAsync("admin-user", organization.Id, " "));
        await archive.ArchiveByAdminAsync("admin-user", organization.Id, "Причина");
        var archivedOrganization = await context.Organizations.SingleAsync(item => item.Id == organization.Id);
        archivedOrganization.Delete(DateTime.UtcNow);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            archive.RestoreByAdminAsync("admin-user", organization.Id, "Восстановление не требуется"));
        Assert.Equal(1, await context.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id));
    }
}
