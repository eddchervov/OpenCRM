using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Organizations.Contracts;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationArchiveTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task ArchiveRestoreAndCleanupPreserveRecentAndActiveOrganizations()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = OrganizationCommandHarness.CreateServices(context, archivingEnabled: true);
        var service = services.GetRequiredService<IOrganizationArchiveService>();
        await service.ArchiveAsync(organization.OwnerUserId, organization.Id);
        var archived = await OrganizationCommandHarness.ReadAsync(context, organization.Id);
        Assert.True(archived.IsArchived);
        Assert.False(archived.IsActive);
        Assert.NotNull(archived.ArchiveDate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync("foreign", organization.Id));
        await service.RestoreAsync(organization.OwnerUserId, organization.Id);
        var restored = await OrganizationCommandHarness.ReadAsync(context, organization.Id);
        Assert.True(restored.IsActive);
        Assert.False(restored.IsArchived);
        Assert.Null(restored.ArchiveDate);
        Assert.Null(restored.DeletionDate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(organization.OwnerUserId, organization.Id));

        var old = await fixture.CreateOrganizationAsync(context);
        old.Archive(DateTime.UtcNow.AddDays(-31));
        var recent = await fixture.CreateOrganizationAsync(context);
        recent.Archive(DateTime.UtcNow.AddDays(-2));
        var undated = await fixture.CreateOrganizationAsync(context);
        undated.Archive(DateTime.UtcNow);
        undated.ArchiveDate = null;
        await context.SaveChangesAsync();
        await service.CleanUpArchiveAsync();
        var deleted = await OrganizationCommandHarness.ReadAsync(context, old.Id);
        Assert.True(deleted.IsDeleted);
        Assert.False(deleted.IsArchived);
        Assert.False(deleted.IsActive);
        Assert.NotNull(deleted.DeletionDate);
        Assert.False((await OrganizationCommandHarness.ReadAsync(context, recent.Id)).IsDeleted);
        Assert.False((await OrganizationCommandHarness.ReadAsync(context, undated.Id)).IsDeleted);
        Assert.False((await OrganizationCommandHarness.ReadAsync(context, organization.Id)).IsDeleted);
        await service.CleanUpArchiveAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(old.OwnerUserId, old.Id));
        Assert.Equal(2, await context.OrganizationAuditEvents.CountAsync(value => value.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing)]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    [InlineData(OrganizationTypeCodes.Fleet)]
    public async Task ArchivingDisabledByDefaultButExistingArchiveCanBeRestored(string scenario)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.PrimaryScenarioCode = scenario;
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);
        var archive = services.GetRequiredService<IOrganizationArchiveService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => archive.ArchiveAsync(organization.OwnerUserId, organization.Id));

        Assert.Contains("временно отключено", exception.Message);
        Assert.False((await OrganizationCommandHarness.ReadAsync(context, organization.Id)).IsArchived);
        Assert.False(await context.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
        var existing = await context.Organizations.SingleAsync(item => item.Id == organization.Id);
        existing.Archive(DateTime.UtcNow.AddDays(-1));
        await context.SaveChangesAsync();
        await archive.RestoreAsync(organization.OwnerUserId, organization.Id);
        var restored = await OrganizationCommandHarness.ReadAsync(context, organization.Id);
        Assert.False(restored.IsArchived);
        Assert.True(restored.IsActive);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("archived")]
    [InlineData("inactive")]
    [InlineData("foreign")]
    public async Task ArchiveRejectsUnavailableOrganization(string state)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.IsDeleted = state == "deleted";
        organization.IsArchived = state == "archived";
        organization.IsActive = state != "inactive";
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context, archivingEnabled: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IOrganizationArchiveService>()
            .ArchiveAsync(state == "foreign" ? "foreign" : organization.OwnerUserId,
                state == "missing" ? Guid.NewGuid() : organization.Id));
    }
}
