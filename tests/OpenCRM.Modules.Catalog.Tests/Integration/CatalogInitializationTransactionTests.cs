using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogInitializationTransactionTests(CatalogDatabaseFixture fixture)
{
    [Fact]
    public async Task InitializationJoinsExternalTransactionAndDoesNotCommitIt()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<ICatalogInitializationService>().EnsureDefaultsAsync(organization.Id);
            Assert.Same(transaction, context.Database.CurrentTransaction);
            Assert.True(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.OrganizationId == organization.Id));
            await transaction.RollbackAsync();
        });

        await using var verification = fixture.CreateContext();
        Assert.False(await verification.OrganizationScenarioCatalogItems.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task LegacyDocumentWithoutKindUsesNameFallbackWhenUpdated()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var document = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            KindCode = OrganizationScenarioCatalogKindCodes.Document,
            ScenarioCode = "detailing",
            Code = "legacy-kind",
            Name = "Старый документ"
        };
        context.OrganizationScenarioCatalogItems.Add(document);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var updated = await scope.ServiceProvider.GetRequiredService<ICatalogCommandService>()
            .UpdateAsync(organization.OwnerUserId, organization.Id, document.Id, new UpdateOrganizationScenarioCatalogItemDto
            {
                Name = "Акт выполненных работ",
                DocumentKindCode = "unknown",
                IsActive = true
            });

        Assert.Equal(OrganizationScenarioDocumentKindCodes.WorkAct, updated.DocumentKindCode);
    }

    [Fact]
    public async Task FailedCommandRollsBackDefaultsAndDoesNotLeaveThemForLaterSave()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ICatalogCommandService>()
            .CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto { Name = " " }));
        await context.SaveChangesAsync();

        await using var verification = fixture.CreateContext();
        Assert.False(await verification.OrganizationScenarioCatalogItems.AnyAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await verification.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }
}
