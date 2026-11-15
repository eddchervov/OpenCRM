using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogSetupTests(CatalogDatabaseFixture fixture)
{
    [Fact]
    public async Task SaveItemsCreatesOnlyActiveItemsAndLeavesCommitToCaller()
    {
        var itemId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        Guid organizationId;
        await using (var context = fixture.CreateContext())
        {
            var organization = await fixture.CreateOrganizationAsync(context);
            organizationId = organization.Id;
            using var services = fixture.CreateServices(context);
            using var scope = services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ICatalogSetupService>();

            await service.SaveItemsAsync(organization.Id, OrganizationScenarioCatalogKindCodes.Service,
            [
                Item(itemId, "  Полировка  ", unitPrice: 1200, durationMinutes: 90),
                Item(inactiveId, "Архивная работа", isActive: false)
            ]);

            await using var verificationContext = fixture.CreateContext();
            Assert.False(await verificationContext.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == itemId));
            await context.SaveChangesAsync();
        }

        await using var savedContext = fixture.CreateContext();
        var created = await savedContext.OrganizationScenarioCatalogItems.SingleAsync(item => item.Id == itemId);
        Assert.Equal(organizationId, created.OrganizationId);
        Assert.Equal("detailing", created.ScenarioCode);
        Assert.Equal("setup-" + itemId.ToString("N"), created.Code);
        Assert.Equal("Полировка", created.Name);
        Assert.Equal(10, created.SortOrder);
        Assert.Equal(1200, created.UnitPrice);
        Assert.Equal(90, created.DurationMinutes);
        Assert.False(await savedContext.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == inactiveId));
    }

    [Fact]
    public async Task SaveItemsAcceptsAnEmptyList()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogSetupService>();

        await service.SaveItemsAsync(organization.Id, OrganizationScenarioCatalogKindCodes.Service, []);

        Assert.Empty(context.ChangeTracker.Entries<OrganizationScenarioCatalogItem>());
    }

    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task SaveItemsRejectsInvalidNamesIdsAndPrices(IReadOnlyList<CatalogSetupItemDto> items)
    {
        await using var context = fixture.CreateContext();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogSetupService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveItemsAsync(Guid.NewGuid(), OrganizationScenarioCatalogKindCodes.Service, items));
    }

    public static IEnumerable<object[]> InvalidItems()
    {
        var validId = Guid.NewGuid();
        yield return [new[] { Item(Guid.Empty, "Работа") }];
        yield return [new[] { Item(validId, null!) }];
        yield return [new[] { Item(validId, "   ") }];
        yield return [new[] { Item(validId, new string('я', 161)) }];
        yield return [new[] { Item(validId, "Работа", unitPrice: -1) }];
        yield return [new[] { Item(validId, "Работа", unitPrice: 1_000_000_000) }];
        yield return [new[] { Item(validId, "Работа", unitPrice: 1.001m) }];
        yield return new object[] { new[] { Item(validId, "Работа"), Item(validId, "Дубль") } };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(525601)]
    public async Task SaveItemsRejectsInvalidDuration(int durationMinutes)
    {
        await using var context = fixture.CreateContext();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogSetupService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveItemsAsync(
            Guid.NewGuid(), OrganizationScenarioCatalogKindCodes.Service, [Item(Guid.NewGuid(), "Работа", durationMinutes: durationMinutes)]));
    }

    [Fact]
    public async Task SaveItemsRejectsAnItemFromAnotherOrganizationOrCatalog()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var foreignService = CatalogItem(foreignOrganization.Id, OrganizationScenarioCatalogKindCodes.Service, "Чужая услуга");
        context.OrganizationScenarioCatalogItems.Add(foreignService);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogSetupService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveItemsAsync(
            organization.Id, OrganizationScenarioCatalogKindCodes.Service, [Item(foreignService.Id, foreignService.Name)]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveItemsAsync(
            foreignOrganization.Id, OrganizationScenarioCatalogKindCodes.WorkPost, [Item(foreignService.Id, foreignService.Name)]));
    }

    [Fact]
    public async Task SaveItemsRejectsDuplicateActiveNamesIgnoringCase()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var existing = CatalogItem(organization.Id, OrganizationScenarioCatalogKindCodes.WorkPost, "Бокс 1");
        context.OrganizationScenarioCatalogItems.Add(existing);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogSetupService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveItemsAsync(
            organization.Id, OrganizationScenarioCatalogKindCodes.WorkPost, [Item(Guid.NewGuid(), "  бокс 1 ")]));
    }

    [Fact]
    public async Task SaveItemsProtectsServicesUsedByMaintenanceAndActiveBundles()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var serviceItem = CatalogItem(organization.Id, OrganizationScenarioCatalogKindCodes.Service, "Полировка");
        var bundledService = CatalogItem(organization.Id, OrganizationScenarioCatalogKindCodes.Service, "Мойка");
        var activeBundle = CatalogItem(organization.Id, OrganizationScenarioCatalogKindCodes.ServiceBundle, "Комплекс");
        activeBundle.BundleItems.Add(new OrganizationServiceBundleItem
        {
            BundleId = activeBundle.Id,
            ServiceId = bundledService.Id,
            OrganizationId = organization.Id,
            Position = 0
        });
        var vehicle = new Vehicle { User = organization.OwnerUser, Organization = organization };
        context.OrganizationScenarioCatalogItems.AddRange(serviceItem, bundledService, activeBundle);
        context.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            User = organization.OwnerUser,
            Vehicle = vehicle,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { Name = "Полировка", CatalogItemId = serviceItem.Id }]
        });
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogSetupService>();

        await service.SaveItemsAsync(organization.Id, OrganizationScenarioCatalogKindCodes.Service,
            [Item(serviceItem.Id, "Полировка новая", unitPrice: 1500)]);
        Assert.Equal("Полировка новая", serviceItem.Name);
        Assert.Equal(1500, serviceItem.UnitPrice);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveItemsAsync(
            organization.Id, OrganizationScenarioCatalogKindCodes.Service, [Item(serviceItem.Id, "Полировка новая", isActive: false)]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveItemsAsync(
            organization.Id, OrganizationScenarioCatalogKindCodes.Service, [Item(bundledService.Id, "Мойка", isActive: false)]));
    }

    [Fact]
    public async Task SaveItemsIgnoresArchivedBundleWhenCheckingServiceUsage()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var serviceItem = CatalogItem(organization.Id, OrganizationScenarioCatalogKindCodes.Service, "Мойка");
        var bundle = CatalogItem(organization.Id, OrganizationScenarioCatalogKindCodes.ServiceBundle, "Архивный комплекс");
        bundle.IsActive = false;
        bundle.BundleItems.Add(new OrganizationServiceBundleItem
        {
            BundleId = bundle.Id,
            ServiceId = serviceItem.Id,
            OrganizationId = organization.Id,
            Position = 0
        });
        context.OrganizationScenarioCatalogItems.AddRange(serviceItem, bundle);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogSetupService>();

        await service.SaveItemsAsync(organization.Id, OrganizationScenarioCatalogKindCodes.Service,
            [Item(serviceItem.Id, "Мойка", isActive: false)]);

        Assert.False(serviceItem.IsActive);
    }

    [Fact]
    public async Task SaveItemsPreventsUsedPostActivityChangesButAllowsUnusedPostActivityChanges()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var post = CatalogItem(organization.Id, OrganizationScenarioCatalogKindCodes.WorkPost, "Пост 1");
        var serviceItem = CatalogItem(organization.Id, OrganizationScenarioCatalogKindCodes.Service, "Работа", price: 200, durationMinutes: 30);
        var vehicle = new Vehicle { User = organization.OwnerUser, Organization = organization };
        context.OrganizationScenarioCatalogItems.AddRange(post, serviceItem);
        context.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            User = organization.OwnerUser,
            Vehicle = vehicle,
            Location = " пост 1 ",
            Date = DateTime.UtcNow,
            Details = []
        });
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogSetupService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveItemsAsync(
            organization.Id, OrganizationScenarioCatalogKindCodes.WorkPost, [Item(post.Id, "Пост 2")]));
        var activityError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveItemsAsync(
            organization.Id, OrganizationScenarioCatalogKindCodes.WorkPost, [Item(post.Id, "Пост 1", isActive: false)]));
        Assert.Contains("справочнике «Посты / боксы»", activityError.Message);
        Assert.True(post.IsActive);

        var unusedPost = CatalogItem(organization.Id, OrganizationScenarioCatalogKindCodes.WorkPost, "Пост без назначений");
        context.OrganizationScenarioCatalogItems.Add(unusedPost);
        await context.SaveChangesAsync();
        await service.SaveItemsAsync(organization.Id, OrganizationScenarioCatalogKindCodes.WorkPost,
            [Item(unusedPost.Id, "Пост без назначений", isActive: false)]);
        Assert.False(unusedPost.IsActive);
        await service.SaveItemsAsync(organization.Id, OrganizationScenarioCatalogKindCodes.WorkPost,
            [Item(unusedPost.Id, "Пост без назначений")]);
        Assert.True(unusedPost.IsActive);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveItemsAsync(
            organization.Id,
            OrganizationScenarioCatalogKindCodes.WorkPost,
            [Item(post.Id, "Пост 1", isActive: false), Item(Guid.NewGuid(), " пост 1 ")]));

        await service.SaveItemsAsync(organization.Id, OrganizationScenarioCatalogKindCodes.Service,
            [Item(serviceItem.Id, "Работа", unitPrice: 300, durationMinutes: 45)]);
        Assert.Null(post.UnitPrice);
        Assert.Null(post.DurationMinutes);
        Assert.Equal(300, serviceItem.UnitPrice);
        Assert.Equal(45, serviceItem.DurationMinutes);
    }

    private static CatalogSetupItemDto Item(
        Guid id,
        string name,
        decimal? unitPrice = null,
        int? durationMinutes = null,
        bool isActive = true) => new()
    {
        Id = id,
        Name = name,
        UnitPrice = unitPrice,
        DurationMinutes = durationMinutes,
        IsActive = isActive
    };

    private static OrganizationScenarioCatalogItem CatalogItem(
        Guid organizationId,
        string kindCode,
        string name,
        decimal? price = null,
        int? durationMinutes = null) => new()
    {
        OrganizationId = organizationId,
        ScenarioCode = "detailing",
        KindCode = kindCode,
        Code = "test-" + Guid.NewGuid().ToString("N"),
        Name = name,
        UnitPrice = price,
        DurationMinutes = durationMinutes
    };
}
