using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceResolverEdgeTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task EmployeeResolverRetainsInactiveEmployeeSnapshotOnlyForUnchangedAssignment()
    {
        await using var context = fixture.CreateContext();
        var organization = MaintenanceResolverEdgeTests.CreateOrganization();
        var employee = new OrganizationEmployee { Organization = organization, Name = "Новое имя", IsActive = false };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        var resolver = new MaintenanceEmployeeResolver(context);

        Assert.Equal("Историческое имя", await resolver.ResolveEmployeeNameAsync(
            organization.Id, employee.Id, employee.Id, "Историческое имя"));
        Assert.Equal("Новое имя", await resolver.ResolveEmployeeNameAsync(organization.Id, employee.Id, employee.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveEmployeeNameAsync(organization.Id, employee.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveEmployeeNameAsync(Guid.NewGuid(), employee.Id));
        Assert.Null(await resolver.ResolveEmployeeNameAsync(organization.Id, null));

        employee.IsActive = true;
        await context.SaveChangesAsync();
        Assert.Equal("Новое имя", await resolver.ResolveEmployeeNameAsync(organization.Id, employee.Id, Guid.NewGuid(), "Другое имя"));
    }

    [Fact]
    public async Task DetailEmployeesReuseMatchingSnapshotsAndClearMaterialOrPersonalAssignments()
    {
        await using var context = fixture.CreateContext();
        var organization = MaintenanceResolverEdgeTests.CreateOrganization();
        var employee = new OrganizationEmployee { Organization = organization, Name = "Текущее имя" };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        var catalogId = Guid.NewGuid();
        var previous = new MaintenanceDetail { CatalogItemId = catalogId, EmployeeId = employee.Id, EmployeeName = "Старое имя" };
        var matched = new MaintenanceDetail { CatalogItemId = catalogId, EmployeeId = employee.Id };
        var unmatched = new MaintenanceDetail { CatalogItemId = Guid.NewGuid(), EmployeeId = employee.Id };
        var material = new MaintenanceDetail { MaterialName = "Состав", EmployeeId = employee.Id, EmployeeName = "Удалить" };
        var resolver = new MaintenanceEmployeeResolver(context);

        await resolver.ValidateDetailEmployeesAsync(organization.Id, [matched, unmatched, material], [previous]);

        Assert.Equal("Старое имя", matched.EmployeeName);
        Assert.Equal("Текущее имя", unmatched.EmployeeName);
        Assert.Null(material.EmployeeId);
        Assert.Null(material.EmployeeName);

        await resolver.ValidateDetailEmployeesAsync(null, [matched], null);

        Assert.Null(matched.EmployeeId);
        Assert.Null(matched.EmployeeName);
        await resolver.ValidateDetailEmployeesAsync(organization.Id, [new MaintenanceDetail()], null);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OptionalCatalogAndEmployeeIdsMatchOnlyTheCorrespondingPreviousRow(bool hasCatalog, bool hasEmployee)
    {
        await using var context = fixture.CreateContext();
        var organization = MaintenanceResolverEdgeTests.CreateOrganization();
        var employee = new OrganizationEmployee { Organization = organization, Name = "Текущее имя" };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        Guid? catalogId = hasCatalog ? Guid.NewGuid() : null;
        Guid? employeeId = hasEmployee ? employee.Id : null;
        var detail = new MaintenanceDetail { CatalogItemId = catalogId, EmployeeId = employeeId };
        var previous = new MaintenanceDetail { CatalogItemId = catalogId, EmployeeId = employeeId, EmployeeName = "Историческое имя" };
        var unrelatedCatalog = new MaintenanceDetail { CatalogItemId = Guid.NewGuid(), EmployeeId = employeeId };
        var unrelatedEmployee = new MaintenanceDetail { CatalogItemId = catalogId, EmployeeId = hasEmployee ? null : employee.Id };
        var resolver = new MaintenanceEmployeeResolver(context);

        await resolver.ValidateDetailEmployeesAsync(organization.Id, [detail], [unrelatedCatalog, unrelatedEmployee, previous]);

        Assert.Equal(employeeId, detail.EmployeeId);
        Assert.Equal(hasEmployee ? "Историческое имя" : null, detail.EmployeeName);
    }

    [Fact]
    public async Task PayerUsesBuyerSnapshotWhenExplicitlySelectingTheSameClient()
    {
        await using var context = fixture.CreateContext();
        var resolver = new MaintenanceBuyerResolver(context);
        var buyerId = Guid.NewGuid();
        var work = new MaintenanceEntity
        {
            BuyerClientId = buyerId,
            BuyerName = "Исторический покупатель",
            BuyerPartyTypeCode = "company",
            BuyerInn = "1234567890",
            BuyerKpp = "123456789",
            BuyerLegalAddress = "Исторический адрес"
        };
        var payment = new MaintenancePayment();

        await resolver.SetPayerAsync(payment, work, Guid.NewGuid(), buyerId);

        Assert.Equal(buyerId, payment.PayerClientId);
        Assert.Equal(work.BuyerName, payment.PayerName);
        Assert.Equal(work.BuyerInn, payment.PayerInn);
        Assert.Equal(work.BuyerKpp, payment.PayerKpp);
        Assert.Equal(work.BuyerLegalAddress, payment.PayerLegalAddress);
        Assert.Equal(work.BuyerPartyTypeCode, payment.PayerPartyTypeCode);
    }

    [Fact]
    public async Task DefaultPayerIsEmptyWhenOrderHasNoBuyerDespiteStoredSnapshot()
    {
        await using var context = fixture.CreateContext();
        var order = new MaintenanceEntity
        {
            BuyerName = "Сохранённое имя",
            BuyerPartyTypeCode = "company",
            BuyerInn = "1234567890",
            BuyerKpp = "123456789",
            BuyerLegalAddress = "Сохранённый адрес"
        };
        var payment = new MaintenancePayment();

        await new MaintenanceBuyerResolver(context).SetPayerAsync(payment, order, Guid.NewGuid(), null);

        Assert.Null(payment.PayerClientId);
        Assert.Null(payment.PayerName);
        Assert.Null(payment.PayerPartyTypeCode);
        Assert.Null(payment.PayerInn);
        Assert.Null(payment.PayerKpp);
        Assert.Null(payment.PayerLegalAddress);
        Assert.Equal("Сохранённое имя", order.BuyerName);
    }

    [Fact]
    public async Task LockedOrderReloadRejectsDeletionAfterTheRepositoryRead()
    {
        await using var context = fixture.CreateContext();
        var organization = MaintenanceResolverEdgeTests.CreateOrganization();
        var work = new MaintenanceEntity
        {
            Vehicle = new Vehicle { Organization = organization, User = organization.OwnerUser },
            User = organization.OwnerUser,
            Date = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned
        };
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        await context.Maintenance.Where(value => value.Id == work.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsDeleted, true));
        Assert.False(work.IsDeleted);
        var repository = new Mock<IMaintenanceRepository>(MockBehavior.Strict);
        repository.Setup(value => value.GetById(work.User.Id, organization.Id, work.Id)).ReturnsAsync(work);
        var loader = new MaintenanceOrderLoader(repository.Object, context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            loader.LockMaintenanceAsync(work.User.Id, organization.Id, work.Id));

        Assert.Equal("Обслуживание не найдено.", error.Message);
        Assert.True(work.IsDeleted);
    }

    [Fact]
    public async Task ExplicitPayerCanBeSelectedForAnOrderWithoutABuyer()
    {
        await using var context = fixture.CreateContext();
        var organization = MaintenanceResolverEdgeTests.CreateOrganization();
        var payer = new OrganizationClient
        {
            Organization = organization,
            Name = "Самостоятельный плательщик",
            PartyTypeCode = "person"
        };
        context.OrganizationClients.Add(payer);
        await context.SaveChangesAsync();
        var payment = new MaintenancePayment();

        await new MaintenanceBuyerResolver(context).SetPayerAsync(
            payment, new MaintenanceEntity(), organization.Id, payer.Id);

        Assert.Equal(payer.Id, payment.PayerClientId);
        Assert.Equal(payer.Name, payment.PayerName);
        Assert.Equal(payer.PartyTypeCode, payment.PayerPartyTypeCode);
    }

    [Fact]
    public async Task DefectLinksRejectMissingTargetsBeforeAddingLinks()
    {
        await using var context = fixture.CreateContext();
        var organization = MaintenanceResolverEdgeTests.CreateOrganization();
        var work = new MaintenanceEntity
        {
            Vehicle = new Vehicle { Organization = organization, User = organization.OwnerUser },
            User = organization.OwnerUser,
            Date = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned
        };
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var clock = Mock.Of<ISystemClock>(value => value.UtcNow == new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc));
        var links = new MaintenanceDefectLinks(clock, context, new MaintenanceHistoryWriter(clock, context));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => links.SyncLinkedDefectsAsync(
            work.User.Id, organization.Id, work.Vehicle.Id, work.Id, work.Status, [Guid.NewGuid()]));

        Assert.Contains("не найдены", error.Message);
        Assert.False(await context.MaintenanceDefects.AnyAsync(value => value.MaintenanceId == work.Id));
        await links.SyncLinkedDefectsAsync(work.User.Id, organization.Id, work.Vehicle.Id, work.Id, work.Status, []);
        await links.SyncLinkedDefectsAsync(work.User.Id, null, work.Vehicle.Id, work.Id, work.Status, []);
        await links.SyncLinkedDefectsAsync(work.User.Id, organization.Id, work.Vehicle.Id, work.Id, work.Status, null);

        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task ServiceValidationPreservesHistoricalDuplicateNamesButRejectsNewDuplicates()
    {
        await using var context = fixture.CreateContext();
        var organization = MaintenanceResolverEdgeTests.CreateOrganization();
        var service = new OrganizationScenarioCatalogItem
        {
            Organization = organization,
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Code = "historical-service",
            Name = "Текущее название услуги",
            IsActive = false
        };
        context.OrganizationScenarioCatalogItems.Add(service);
        await context.SaveChangesAsync();
        var rules = new MaintenanceDetailRules(context, new MaintenanceEmployeeResolver(context));
        var details = new[]
        {
            new MaintenanceDetail { CatalogItemId = service.Id, Name = "Переименовано в запросе" },
            new MaintenanceDetail { CatalogItemId = service.Id, Name = "Второе историческое название" }
        };
        var previous = new[]
        {
            new MaintenanceDetail { Name = "Произвольная работа без справочника" },
            new MaintenanceDetail { CatalogItemId = service.Id, Name = "Первое историческое название" },
            new MaintenanceDetail { CatalogItemId = service.Id, Name = "Второе историческое название" }
        };

        await rules.ValidateServiceDetailsAsync(organization.Id, details, previous);

        Assert.Equal("Первое историческое название", details[0].Name);
        Assert.Equal("Второе историческое название", details[1].Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rules.ValidateServiceDetailsAsync(organization.Id, details));

        service.IsActive = true;
        await context.SaveChangesAsync();
        var newDetail = new MaintenanceDetail { CatalogItemId = service.Id, Name = "Подмена названия" };
        await rules.ValidateServiceDetailsAsync(organization.Id, [newDetail], [previous[0]]);

        Assert.Equal(service.Name, newDetail.Name);
    }

    private static Organization CreateOrganization()
    {
        var user = new ApplicationUser { UserName = $"resolver-{Guid.NewGuid():N}" };

        return new Organization { Name = "Проверка назначения", OwnerUser = user };
    }
}
