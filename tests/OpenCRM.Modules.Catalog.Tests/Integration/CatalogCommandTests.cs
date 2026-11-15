using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogCommandTests(CatalogDatabaseFixture fixture)
{
    [Fact]
    public async Task CreateAndUpdateNormalizeFieldsGenerateUniqueCodesAndKeepServiceSettings()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();

        var created = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Name = "  Экспресс-мойка  ",
            Description = "  Кузов и салон  ",
            UnitPrice = 1250.50m,
            DurationMinutes = 45
        });
        var duplicate = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Name = "Экспресс-мойка"
        });
        var ordered = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Name = "Услуга с заданным порядком",
            SortOrder = 4
        });
        var punctuationName = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Name = "!!!"
        });

        Assert.Equal("Экспресс-мойка", created.Name);
        Assert.Equal("экспресс-мойка-2", created.Code);
        Assert.Equal("Кузов и салон", created.Description);
        Assert.Equal(1250.50m, created.UnitPrice);
        Assert.Equal(45, created.DurationMinutes);
        Assert.True(created.IsActive);
        Assert.False(created.IsSystem);
        Assert.Equal("экспресс-мойка-3", duplicate.Code);
        Assert.Equal(created.SortOrder + 10, duplicate.SortOrder);
        Assert.Equal(4, ordered.SortOrder);
        Assert.Equal("item", punctuationName.Code);

        var longName = new string('д', 160);
        var longNameFirst = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Name = longName
        });
        var longNameSecond = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Name = longName
        });
        Assert.True(longNameFirst.Code.Length <= 120);
        Assert.True(longNameSecond.Code.Length <= 120);
        Assert.NotEqual(longNameFirst.Code, longNameSecond.Code);

        var updated = await command.UpdateAsync(organization.OwnerUserId, organization.Id, created.Id, new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = "  Мойка кузова  ",
            Description = "  ",
            UnitPrice = 999999999m,
            DurationMinutes = 525600,
            SortOrder = 7,
            IsActive = false
        });

        Assert.Equal("Мойка кузова", updated.Name);
        Assert.Equal(created.Code, updated.Code);
        Assert.Null(updated.Description);
        Assert.Equal(999999999m, updated.UnitPrice);
        Assert.Equal(525600, updated.DurationMinutes);
        Assert.Equal(7, updated.SortOrder);
        Assert.False(updated.IsActive);
        Assert.Null(updated.DocumentKindCode);

        var unchangedOrder = await command.UpdateAsync(organization.OwnerUserId, organization.Id, duplicate.Id, new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = duplicate.Name,
            IsActive = true
        });
        Assert.Equal(duplicate.SortOrder, unchangedOrder.SortOrder);
    }

    [Fact]
    public async Task CreateAndUpdateDocumentInferAndPreserveItsStableGeneratorKind()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();

        var created = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.Document,
            Name = "  Заказ на работу  ",
            DocumentKindCode = "unknown-generator",
            UnitPrice = 500m
        });
        var updated = await command.UpdateAsync(organization.OwnerUserId, organization.Id, created.Id, new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = "Переименованный документ",
            IsActive = true
        });
        var changedKind = await command.UpdateAsync(organization.OwnerUserId, organization.Id, created.Id, new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = updated.Name,
            DocumentKindCode = "receipt",
            IsActive = true
        });

        Assert.Equal(OrganizationScenarioDocumentKindCodes.WorkOrder, created.DocumentKindCode);
        Assert.Null(created.UnitPrice);
        Assert.Equal(created.Code, updated.Code);
        Assert.Equal(OrganizationScenarioDocumentKindCodes.WorkOrder, updated.DocumentKindCode);
        Assert.Equal(OrganizationScenarioDocumentKindCodes.Receipt, changedKind.DocumentKindCode);
    }

    [Theory]
    [InlineData("-0.01", null, "Укажите цену от 0 до 999999999 рублей, не более двух знаков после запятой.")]
    [InlineData("1000000000", null, "Укажите цену от 0 до 999999999 рублей, не более двух знаков после запятой.")]
    [InlineData("1.001", null, "Укажите цену от 0 до 999999999 рублей, не более двух знаков после запятой.")]
    [InlineData(null, 0, "Длительность услуги или набора должна быть от 1 до 525600 минут.")]
    [InlineData(null, 525601, "Длительность услуги или набора должна быть от 1 до 525600 минут.")]
    public async Task CreateRejectsInvalidPriceAndDuration(string? priceValue, int? duration, string expectedMessage)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.CreateAsync(
            organization.OwnerUserId,
            organization.Id,
            new CreateOrganizationScenarioCatalogItemDto
            {
                KindCode = OrganizationScenarioCatalogKindCodes.Service,
                Name = "Невалидная услуга",
                UnitPrice = priceValue is null ? null : decimal.Parse(priceValue, CultureInfo.InvariantCulture),
                DurationMinutes = duration
            }));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item =>
            item.OrganizationId == organization.Id && item.Name == "Невалидная услуга"));
        Assert.False(await context.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(525601)]
    public async Task CreateRejectsDurationForUnsupportedCatalogKind(int duration)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.CreateAsync(
            organization.OwnerUserId,
            organization.Id,
            new CreateOrganizationScenarioCatalogItemDto
            {
                KindCode = OrganizationScenarioCatalogKindCodes.Material,
                Name = "Материал с длительностью",
                DurationMinutes = duration
            }));

        Assert.Equal("Длительность услуги или набора должна быть от 1 до 525600 минут.", exception.Message);
    }

    [Fact]
    public async Task BundleCreationAndUpdateKeepServiceOrderAndReplaceRemovedLinks()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        await scope.ServiceProvider.GetRequiredService<ICatalogInitializationService>().EnsureDefaultsAsync(organization.Id);
        var serviceIds = await CatalogCommandTests.GetServiceIdsAsync(context, organization.Id);
        var bundle = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.ServiceBundle,
            Name = "Комплекс услуг",
            UnitPrice = 3000m,
            DurationMinutes = 90,
            ServiceIds = [serviceIds[0], serviceIds[1]]
        });

        Assert.Equal([serviceIds[0], serviceIds[1]], bundle.ServiceIds);
        var createdPositions = await context.OrganizationServiceBundleItems.AsNoTracking()
            .Where(item => item.BundleId == bundle.Id)
            .OrderBy(item => item.Position)
            .Select(item => item.Position)
            .ToArrayAsync();
        Assert.Equal(new[] { 0, 1 }, createdPositions);

        var updated = await command.UpdateAsync(organization.OwnerUserId, organization.Id, bundle.Id, new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = "Комплекс услуг",
            UnitPrice = 3100m,
            DurationMinutes = 100,
            IsActive = true,
            ServiceIds = [serviceIds[1], serviceIds[2]]
        });

        Assert.Equal([serviceIds[1], serviceIds[2]], updated.ServiceIds);
        var savedLinks = await context.OrganizationServiceBundleItems.AsNoTracking()
            .Where(item => item.BundleId == bundle.Id)
            .OrderBy(item => item.Position)
            .ToListAsync();
        Assert.Equal([serviceIds[1], serviceIds[2]], savedLinks.Select(item => item.ServiceId));
        Assert.Equal([0, 1], savedLinks.Select(item => item.Position));
        Assert.Null(updated.UnitPrice);
        Assert.Equal(100, updated.DurationMinutes);
        var catalog = await scope.ServiceProvider.GetRequiredService<ICatalogQueryService>()
            .GetAsync(organization.OwnerUserId, organization.Id);
        Assert.Equal([serviceIds[1], serviceIds[2]], catalog.ServiceBundles.Single(item => item.Id == bundle.Id).ServiceIds);

        var invalidUpdate = await Assert.ThrowsAsync<InvalidOperationException>(() => command.UpdateAsync(
            organization.OwnerUserId,
            organization.Id,
            bundle.Id,
            new UpdateOrganizationScenarioCatalogItemDto
            {
                Name = bundle.Name,
                IsActive = true,
                ServiceIds = [serviceIds[0], serviceIds[0]]
            }));
        Assert.Equal("Выберите от 1 до 100 разных услуг для набора.", invalidUpdate.Message);
    }

    [Fact]
    public async Task BundleCreationRejectsEmptyDuplicateForeignInactiveAndOversizedServiceLists()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        await scope.ServiceProvider.GetRequiredService<ICatalogInitializationService>().EnsureDefaultsAsync(organization.Id);
        await scope.ServiceProvider.GetRequiredService<ICatalogInitializationService>().EnsureDefaultsAsync(foreignOrganization.Id);
        var serviceIds = await CatalogCommandTests.GetServiceIdsAsync(context, organization.Id);
        var foreignService = CatalogCommandTests.CreateCatalogItem(
            foreignOrganization.Id,
            foreignOrganization.PrimaryScenarioCode,
            "foreign-service");
        context.OrganizationScenarioCatalogItems.Add(foreignService);
        await context.SaveChangesAsync();
        var inactiveServiceId = serviceIds[2];
        var inactiveService = await context.OrganizationScenarioCatalogItems.SingleAsync(item => item.Id == inactiveServiceId);
        inactiveService.IsActive = false;
        await context.SaveChangesAsync();
        var overLimitServiceIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();

        var nullServicesException = await Assert.ThrowsAsync<InvalidOperationException>(() => command.CreateAsync(
            organization.OwnerUserId,
            organization.Id,
            new CreateOrganizationScenarioCatalogItemDto
            {
                KindCode = OrganizationScenarioCatalogKindCodes.ServiceBundle,
                Name = "Набор без списка",
                ServiceIds = null!
            }));
        Assert.Equal("Выберите от 1 до 100 разных услуг для набора.", nullServicesException.Message);

        var cases = new (Guid[] ServiceIds, string ExpectedMessage)[]
        {
            ([], "Выберите от 1 до 100 разных услуг для набора."),
            ([serviceIds[0], serviceIds[0]], "Выберите от 1 до 100 разных услуг для набора."),
            (overLimitServiceIds, "Выберите от 1 до 100 разных услуг для набора."),
            ([foreignService.Id], "В набор можно включить только действующие услуги этой организации."),
            ([inactiveServiceId], "В набор можно включить только действующие услуги этой организации.")
        };

        foreach (var testCase in cases)
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.CreateAsync(
                organization.OwnerUserId,
                organization.Id,
                new CreateOrganizationScenarioCatalogItemDto
                {
                    KindCode = OrganizationScenarioCatalogKindCodes.ServiceBundle,
                    Name = $"Невалидный набор {Guid.NewGuid():N}",
                    ServiceIds = testCase.ServiceIds
                }));

            Assert.Equal(testCase.ExpectedMessage, exception.Message);
        }

        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item =>
            item.OrganizationId == organization.Id && item.KindCode == OrganizationScenarioCatalogKindCodes.ServiceBundle));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task DeleteArchivesSystemAndServiceItemsButRemovesUnusedCustomItems(bool isSystem, bool shouldRemain)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var item = CatalogCommandTests.CreateCatalogItem(
            organization.Id,
            organization.PrimaryScenarioCode,
            $"delete-{Guid.NewGuid():N}",
            isSystem,
            OrganizationScenarioCatalogKindCodes.WorkType);
        context.OrganizationScenarioCatalogItems.Add(item);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ICatalogCommandService>()
            .DeleteAsync(organization.OwnerUserId, organization.Id, item.Id);

        if (shouldRemain)
        {
            var archived = await context.OrganizationScenarioCatalogItems.AsNoTracking().SingleAsync(value => value.Id == item.Id);
            Assert.False(archived.IsActive);
        }
        else
        {
            Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(value => value.Id == item.Id));
        }
    }

    [Fact]
    public async Task DeleteArchivesBundlesAndRejectsUsedNonServiceCatalogItems()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        await scope.ServiceProvider.GetRequiredService<ICatalogInitializationService>().EnsureDefaultsAsync(organization.Id);
        var serviceIds = await CatalogCommandTests.GetServiceIdsAsync(context, organization.Id);
        var bundle = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.ServiceBundle,
            Name = "Набор для удаления",
            ServiceIds = [serviceIds[0]]
        });
        await command.DeleteAsync(organization.OwnerUserId, organization.Id, bundle.Id);
        Assert.False((await context.OrganizationScenarioCatalogItems.AsNoTracking().SingleAsync(item => item.Id == bundle.Id)).IsActive);

        var usedItem = CatalogCommandTests.CreateCatalogItem(organization.Id, organization.PrimaryScenarioCode, "used-material", kindCode: OrganizationScenarioCatalogKindCodes.Material);
        context.OrganizationScenarioCatalogItems.Add(usedItem);
        await context.SaveChangesAsync();
        context.OrganizationMaterialStocks.Add(new OrganizationMaterialStock
        {
            OrganizationId = organization.Id,
            CatalogItemId = usedItem.Id,
            Name = "Масло",
            Unit = "л"
        });
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.DeleteAsync(
            organization.OwnerUserId, organization.Id, usedItem.Id));
        var updateException = await Assert.ThrowsAsync<InvalidOperationException>(() => command.UpdateAsync(
            organization.OwnerUserId,
            organization.Id,
            usedItem.Id,
            new UpdateOrganizationScenarioCatalogItemDto { Name = "Измененный материал", IsActive = true }));

        Assert.Equal("Элемент справочника уже используется. Его нельзя удалить.", exception.Message);
        Assert.Equal("Элемент справочника уже используется. Его нельзя редактировать, чтобы не сломать связанные записи.", updateException.Message);
        Assert.True((await context.OrganizationScenarioCatalogItems.AsNoTracking().SingleAsync(item => item.Id == usedItem.Id)).IsActive);
    }

    [Fact]
    public async Task ActivePostNameCannotBeDuplicatedRenamedIntoOrReactivatedTwice()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        var name = $"QA Бокс {Guid.NewGuid():N}";
        var first = await command.CreateAsync(organization.OwnerUserId, organization.Id,
            new CreateOrganizationScenarioCatalogItemDto { KindCode = OrganizationScenarioCatalogKindCodes.WorkPost, Name = name });
        var second = await command.CreateAsync(organization.OwnerUserId, organization.Id,
            new CreateOrganizationScenarioCatalogItemDto { KindCode = OrganizationScenarioCatalogKindCodes.WorkPost, Name = name + " другой" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.CreateAsync(organization.OwnerUserId, organization.Id,
            new CreateOrganizationScenarioCatalogItemDto { KindCode = OrganizationScenarioCatalogKindCodes.WorkPost, Name = $" {name.ToUpperInvariant()} " }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.UpdateAsync(organization.OwnerUserId, organization.Id, second.Id,
            new UpdateOrganizationScenarioCatalogItemDto { Name = name, IsActive = true }));
        await using var verify = fixture.CreateContext();
        Assert.Equal(name + " другой", await verify.OrganizationScenarioCatalogItems.Where(item => item.Id == second.Id).Select(item => item.Name).SingleAsync());

        var archived = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.WorkPost,
            Code = $"legacy-{Guid.NewGuid():N}",
            Name = name,
            IsActive = false
        };
        context.OrganizationScenarioCatalogItems.Add(archived);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.UpdateAsync(organization.OwnerUserId, organization.Id, archived.Id,
            new UpdateOrganizationScenarioCatalogItemDto { Name = name, IsActive = true }));
        Assert.False(await verify.OrganizationScenarioCatalogItems.Where(item => item.Id == archived.Id).Select(item => item.IsActive).SingleAsync());
        await command.DeleteAsync(organization.OwnerUserId, organization.Id, first.Id);
        await command.UpdateAsync(organization.OwnerUserId, organization.Id, archived.Id,
            new UpdateOrganizationScenarioCatalogItemDto { Name = name, IsActive = true });
        Assert.True(await verify.OrganizationScenarioCatalogItems.Where(item => item.Id == archived.Id).Select(item => item.IsActive).SingleAsync());
        var renamed = await command.UpdateAsync(organization.OwnerUserId, organization.Id, second.Id,
            new UpdateOrganizationScenarioCatalogItemDto { Name = name + " новое имя", IsActive = true });
        Assert.Equal(name + " новое имя", renamed.Name);
        await command.DeleteAsync(organization.OwnerUserId, organization.Id, second.Id);
        Assert.False(await verify.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == second.Id));
    }

    [Fact]
    public async Task DeleteArchivesUsedPostAndAllowsReactivationWithoutChangingItsIdentity()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var name = $"Закрытый пост {Guid.NewGuid():N}";
        var post = CatalogCommandTests.CreateCatalogItem(
            organization.Id,
            organization.PrimaryScenarioCode,
            $"post-{Guid.NewGuid():N}",
            kindCode: OrganizationScenarioCatalogKindCodes.WorkPost);
        post.Name = name;
        var vehicle = new Vehicle { User = organization.OwnerUser, Organization = organization };
        context.OrganizationScenarioCatalogItems.Add(post);
        context.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            User = organization.OwnerUser,
            Vehicle = vehicle,
            Location = $" {name} ",
            Date = DateTime.UtcNow,
            Details = []
        });
        await context.SaveChangesAsync();
        await using (var verificationContext = fixture.CreateContext())
        {
            Assert.True(await verificationContext.OrganizationScenarioCatalogItems
                .Where(item => item.Id == post.Id).Select(item => item.HasBeenUsed).SingleAsync());
        }

        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();

        await command.DeleteAsync(organization.OwnerUserId, organization.Id, post.Id);

        var archived = await context.OrganizationScenarioCatalogItems.AsNoTracking().SingleAsync(item => item.Id == post.Id);
        Assert.False(archived.IsActive);
        Assert.Equal(name, archived.Name);
        Assert.True(archived.HasBeenUsed);

        var renameError = await Assert.ThrowsAsync<InvalidOperationException>(() => command.UpdateAsync(
            organization.OwnerUserId,
            organization.Id,
            post.Id,
            new UpdateOrganizationScenarioCatalogItemDto { Name = "Другой пост", IsActive = true }));
        Assert.Contains("только вывести из работы или вернуть в работу", renameError.Message);

        var duplicateNameError = await Assert.ThrowsAsync<InvalidOperationException>(() => command.CreateAsync(
            organization.OwnerUserId,
            organization.Id,
            new CreateOrganizationScenarioCatalogItemDto
            {
                KindCode = OrganizationScenarioCatalogKindCodes.WorkPost,
                Name = $"  {name.ToUpperInvariant()}  "
            }));
        Assert.Contains("Название уже использованного поста", duplicateNameError.Message);

        await command.UpdateAsync(organization.OwnerUserId, organization.Id, post.Id,
            new UpdateOrganizationScenarioCatalogItemDto { Name = name, IsActive = true });

        var reactivated = await context.OrganizationScenarioCatalogItems.AsNoTracking().SingleAsync(item => item.Id == post.Id);
        var historicalLocation = await context.Maintenance.AsNoTracking().Where(item => item.Vehicle.OrganizationId == organization.Id)
            .Select(item => item.Location).SingleAsync();
        Assert.True(reactivated.IsActive);
        Assert.Equal(name, reactivated.Name);
        Assert.True(reactivated.HasBeenUsed);
        Assert.Equal($" {name} ", historicalLocation);
    }

    [Fact]
    public async Task DeleteRemovesUnusedCustomWorkPost()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var post = CatalogCommandTests.CreateCatalogItem(
            organization.Id,
            organization.PrimaryScenarioCode,
            $"unused-post-{Guid.NewGuid():N}",
            kindCode: OrganizationScenarioCatalogKindCodes.WorkPost);
        context.OrganizationScenarioCatalogItems.Add(post);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ICatalogCommandService>()
            .DeleteAsync(organization.OwnerUserId, organization.Id, post.Id);

        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == post.Id));
    }

    [Fact]
    public async Task DeleteArchivesCustomServiceInsteadOfRemovingItsIdentity()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = CatalogCommandTests.CreateCatalogItem(
            organization.Id,
            organization.PrimaryScenarioCode,
            "custom-service",
            kindCode: OrganizationScenarioCatalogKindCodes.Service);
        context.OrganizationScenarioCatalogItems.Add(service);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ICatalogCommandService>()
            .DeleteAsync(organization.OwnerUserId, organization.Id, service.Id);

        var saved = await context.OrganizationScenarioCatalogItems.AsNoTracking().SingleAsync(item => item.Id == service.Id);
        Assert.False(saved.IsActive);
        Assert.False(saved.IsSystem);
    }

    [Fact]
    public async Task UpdateAndDeleteRejectItemsOwnedByAnotherOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var foreignItem = CatalogCommandTests.CreateCatalogItem(foreignOrganization.Id, foreignOrganization.PrimaryScenarioCode, "foreign-service");
        context.OrganizationScenarioCatalogItems.Add(foreignItem);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();

        var updateError = await Assert.ThrowsAsync<InvalidOperationException>(() => command.UpdateAsync(
            organization.OwnerUserId,
            organization.Id,
            foreignItem.Id,
            new UpdateOrganizationScenarioCatalogItemDto { Name = "Не должно примениться", IsActive = true }));
        var deleteError = await Assert.ThrowsAsync<InvalidOperationException>(() => command.DeleteAsync(
            organization.OwnerUserId, organization.Id, foreignItem.Id));

        Assert.Equal("Элемент справочника не найден.", updateError.Message);
        Assert.Equal("Элемент справочника не найден.", deleteError.Message);
        Assert.Equal("foreign-service", (await context.OrganizationScenarioCatalogItems.AsNoTracking()
            .SingleAsync(item => item.Id == foreignItem.Id)).Name);
    }

    [Fact]
    public async Task WritePermissionFailurePreventsCatalogMutation()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var permission = new Mock<IOrganizationSubscriptionService>();
        permission.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .ThrowsAsync(new InvalidOperationException("Нет права на изменение справочников."));
        using var services = fixture.CreateServices(context, permission.Object);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.CreateAsync(
            organization.OwnerUserId,
            organization.Id,
            new CreateOrganizationScenarioCatalogItemDto
            {
                KindCode = OrganizationScenarioCatalogKindCodes.Service,
                Name = "Запрещенная услуга"
            }));

        Assert.Equal("Нет права на изменение справочников.", exception.Message);
        permission.Verify(service => service.EnsureWorkspaceCanWriteAsync(
            organization.OwnerUserId,
            organization.Id,
            OrganizationPermissionCodes.WorkspaceProfileEdit), Times.Once);
        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task CreateRejectsMissingOrganizationAfterPermissionCheck()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var initialCatalogCount = await context.OrganizationScenarioCatalogItems.CountAsync();
        var initialAuditCount = await context.OrganizationAuditEvents.CountAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<ICatalogCommandService>().CreateAsync(
                organization.OwnerUserId,
                Guid.NewGuid(),
                new CreateOrganizationScenarioCatalogItemDto
                {
                    KindCode = OrganizationScenarioCatalogKindCodes.Service,
                    Name = "Нет организации"
                }));

        Assert.Equal("Организация не найдена.", exception.Message);
        Assert.Equal(initialCatalogCount, await context.OrganizationScenarioCatalogItems.CountAsync());
        Assert.Equal(initialAuditCount, await context.OrganizationAuditEvents.CountAsync());
    }

    private static async Task<Guid[]> GetServiceIdsAsync(OpenCRM.Infrastructure.Data.OpenCRMContext context, Guid organizationId) =>
        await context.OrganizationScenarioCatalogItems
            .Where(item => item.OrganizationId == organizationId
                && item.KindCode == OrganizationScenarioCatalogKindCodes.Service
                && item.IsActive)
            .OrderBy(item => item.SortOrder)
            .Select(item => item.Id)
            .Take(3)
            .ToArrayAsync();

    private static OrganizationScenarioCatalogItem CreateCatalogItem(
        Guid organizationId,
        string scenarioCode,
        string code,
        bool isSystem = false,
        string kindCode = OrganizationScenarioCatalogKindCodes.Service) =>
        new()
        {
            OrganizationId = organizationId,
            ScenarioCode = scenarioCode,
            KindCode = kindCode,
            Code = code,
            Name = code,
            IsSystem = isSystem,
            IsActive = true
        };
}
