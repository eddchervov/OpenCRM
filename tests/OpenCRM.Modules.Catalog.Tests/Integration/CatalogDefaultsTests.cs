using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Defaults;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogDefaultsTests(CatalogDatabaseFixture fixture)
{
    [Theory]
    [InlineData("CNY")]
    [InlineData("USD")]
    public async Task NewDefaultsUseOrganizationCurrencyWithoutInventingPrices(string currencyCode)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, OrganizationTypeCodes.Detailing);
        organization.CurrencyCode = currencyCode;
        await context.SaveChangesAsync();
        var service = new CatalogDefaultsService(context);

        await service.EnsureDefaultsAsync(organization.Id);
        var items = await context.OrganizationScenarioCatalogItems.AsNoTracking()
            .Where(item => item.OrganizationId == organization.Id).ToListAsync();

        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.Equal(currencyCode, item.CurrencyCode));
        Assert.All(items, item => Assert.Null(item.UnitPrice));
    }

    [Fact]
    public async Task EnsureDefaultsCreatesCatalogAndIsIdempotent()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, OrganizationTypeCodes.Detailing);
        var service = new CatalogDefaultsService(context);

        await service.EnsureDefaultsAsync(organization.Id);
        var initialItems = await context.OrganizationScenarioCatalogItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == organization.Id)
            .OrderBy(item => item.KindCode)
            .ThenBy(item => item.Code)
            .Select(item => new { item.Id, item.KindCode, item.Code, item.Name, item.SortOrder })
            .ToListAsync();

        Assert.NotEmpty(initialItems);
        Assert.Contains(initialItems, item => item.KindCode == OrganizationScenarioCatalogKindCodes.Service && item.Name == "Экспресс-мойка");
        Assert.Contains(initialItems, item => item.KindCode == OrganizationScenarioCatalogKindCodes.Document && item.Name == "Акт приема автомобиля");

        await service.EnsureDefaultsAsync(organization.Id);
        var repeatedItems = await context.OrganizationScenarioCatalogItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == organization.Id)
            .OrderBy(item => item.KindCode)
            .ThenBy(item => item.Code)
            .Select(item => new { item.Id, item.KindCode, item.Code, item.Name, item.SortOrder })
            .ToListAsync();

        Assert.Equal(initialItems, repeatedItems);
    }

    [Fact]
    public async Task EnsureDefaultsDoesNotReactivateArchivedDefaultItem()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, OrganizationTypeCodes.Detailing);
        var service = new CatalogDefaultsService(context);
        await service.EnsureDefaultsAsync(organization.Id);

        var defaultItem = await context.OrganizationScenarioCatalogItems.SingleAsync(item =>
            item.OrganizationId == organization.Id
            && item.KindCode == OrganizationScenarioCatalogKindCodes.Service
            && item.Code == "экспресс-мойка");
        defaultItem.IsActive = false;
        await context.SaveChangesAsync();

        await service.EnsureDefaultsAsync(organization.Id);

        var archivedItem = await context.OrganizationScenarioCatalogItems.SingleAsync(item => item.Id == defaultItem.Id);
        Assert.False(archivedItem.IsActive);
        Assert.Equal(1, await context.OrganizationScenarioCatalogItems.CountAsync(item =>
            item.OrganizationId == organization.Id
            && item.KindCode == OrganizationScenarioCatalogKindCodes.Service
            && item.Code == "экспресс-мойка"));
    }

    [Fact]
    public async Task EnsureDefaultsRepairsLegacyDocumentsAndDisablesUnsupportedSystemTemplates()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, OrganizationTypeCodes.Detailing);
        var legacyDocument = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.Document,
            Code = "legacy-order",
            Name = "Заказ-наряд старого образца",
            IsSystem = true,
            IsActive = true,
            DocumentKindCode = null
        };
        var unsupportedSystemDocument = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.Document,
            Code = "before-after",
            Name = "Фотоотчет до/после",
            IsSystem = true,
            IsActive = true,
            DocumentKindCode = null
        };
        var unsupportedCustomDocument = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.Document,
            Code = "custom-before-after",
            Name = "Мой пользовательский шаблон до/после",
            IsSystem = false,
            IsActive = true,
            DocumentKindCode = null
        };
        var legacyWhitespaceDocumentKind = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.Document,
            Code = "legacy-inspection",
            Name = "Акт приема старого образца",
            IsSystem = true,
            IsActive = true,
            DocumentKindCode = "  "
        };
        context.OrganizationScenarioCatalogItems.AddRange(
            legacyDocument,
            unsupportedSystemDocument,
            unsupportedCustomDocument,
            legacyWhitespaceDocumentKind);
        await context.SaveChangesAsync();

        await new CatalogDefaultsService(context).EnsureDefaultsAsync(organization.Id);

        Assert.Equal(OrganizationScenarioDocumentKindCodes.WorkOrder,
            (await context.OrganizationScenarioCatalogItems.SingleAsync(item => item.Id == legacyDocument.Id)).DocumentKindCode);
        Assert.Equal(OrganizationScenarioDocumentKindCodes.InspectionAct,
            (await context.OrganizationScenarioCatalogItems.SingleAsync(item => item.Id == legacyWhitespaceDocumentKind.Id)).DocumentKindCode);
        var repairedUnsupported = await context.OrganizationScenarioCatalogItems.SingleAsync(item => item.Id == unsupportedSystemDocument.Id);
        Assert.Equal(OrganizationScenarioDocumentKindCodes.PhotoReport, repairedUnsupported.DocumentKindCode);
        Assert.False(repairedUnsupported.IsActive);
        Assert.True((await context.OrganizationScenarioCatalogItems.SingleAsync(item => item.Id == unsupportedCustomDocument.Id)).IsActive);
    }

    [Fact]
    public async Task EnsureDefaultsForMissingOrganizationDoesNothing()
    {
        await using var context = fixture.CreateContext();
        var initialCount = await context.OrganizationScenarioCatalogItems.CountAsync();

        await new CatalogDefaultsService(context).EnsureDefaultsAsync(Guid.NewGuid());

        Assert.Equal(initialCount, await context.OrganizationScenarioCatalogItems.CountAsync());
    }
}
