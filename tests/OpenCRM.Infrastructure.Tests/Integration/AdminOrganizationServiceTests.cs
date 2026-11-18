using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Testing;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class AdminOrganizationServiceTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing)]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    public async Task ActiveFreeOrganizationShowsItsPlanWithoutAnEndDate(string scenario)
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка отображает тариф организации, поэтому коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var organization = new Organization
        {
            Name = "Организация на Free",
            OwnerUser = new ApplicationUser(),
            PrimaryScenarioCode = scenario
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var result = await new AdminOrganizationService(context, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PlatformPolicy:Commercial:Enabled"] = "true" }).Build()).GetByIdAsync(organization.Id);

        Assert.NotNull(result);
        Assert.Equal("Бесплатный", result.ActiveSubscriptionName);
        Assert.Null(result.ActiveSubscriptionEndDate);

        organization.IsArchived = true;
        await context.SaveChangesAsync();
        var archivedResult = await new AdminOrganizationService(context, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PlatformPolicy:Commercial:Enabled"] = "true" }).Build()).GetByIdAsync(organization.Id);
        Assert.NotNull(archivedResult);
        Assert.Null(archivedResult.ActiveSubscriptionName);
    }

    [Fact]
    public async Task FiltersAllMembersAndPaginatesWithStableSorting()
    {
        await using var context = fixture.CreateContext();
        var marker = Guid.NewGuid().ToString("N");
        var owner = new ApplicationUser { Email = $"owner-{marker}@example.test" };
        var member = new ApplicationUser { Email = $"member-{marker}@example.test", PhoneNumber = marker };
        var organizations = Enumerable.Range(0, 3).Select(index => new Organization
        {
            Name = $"{marker}-{index}",
            OwnerUser = owner,
            Members = [new OrganizationMember { User = member, RoleCode = OrganizationRoleCodes.Admin }]
        }).ToArray();

        context.Organizations.AddRange(organizations);
        context.Organizations.Add(new Organization { Name = $"{marker}-foreign", OwnerUser = new ApplicationUser() });
        await context.SaveChangesAsync();
        var service = new AdminOrganizationService(context);

        var first = await service.GetAsync(marker, member.Id, "name", false, 1, 2);
        var second = await service.GetAsync(marker, member.Email, "name", false, 2, 2);
        var descending = await service.GetAsync(marker, member.PhoneNumber, "name", true, 1, 2);
        var byOwner = await service.GetAsync(marker, owner.Id, "name", false, 1, 10);

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, first.AllCount);
        Assert.Equal(organizations.Take(2).Select(item => item.Id), first.Organizations.Select(item => item.Id));
        Assert.Equal(organizations[2].Id, Assert.Single(second.Organizations).Id);
        Assert.Equal(organizations[2].Id, descending.Organizations[0].Id);
        Assert.Equal(3, byOwner.TotalCount);
        Assert.Equal(1, first.Page);
        Assert.Equal(2, first.PageSize);

        foreach (var sort in new[] { "owner", "scenario", "status", "subscription", "created", "members", "clients", "vehicles", "orders" })
        {
            var result = await service.GetAsync(marker, member.Id, sort, false, 1, 2);
            Assert.Equal(2, result.Organizations.Count);
        }
    }

    [Fact]
    public async Task CardReturnsOnlyRequestedOrganizationStatisticsAndAllMembers()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Статистика организации",
            OwnerUser = owner,
            Members = Enumerable.Range(0, 10).Select(_ => new OrganizationMember
            {
                User = new ApplicationUser(), RoleCode = OrganizationRoleCodes.Admin
            }).ToList()
        };

        var vehicle = new Vehicle { Organization = organization, User = owner };
        var foreignVehicle = new Vehicle
        {
            Organization = new Organization { Name = "Другая организация", OwnerUser = owner }, User = owner
        };

        context.Maintenance.AddRange(
            new Maintenance
            {
                Vehicle = vehicle, User = owner, Status = MaintenanceStatus.Completed,
                DiscountAmount = 100m, PaidAmount = 500m,
                Details = [new MaintenanceDetail { Name = "Работа", Price = 1000 }]
            },
            new Maintenance
            {
                Vehicle = vehicle, User = owner, Status = MaintenanceStatus.Planned,
                Details = [new MaintenanceDetail { Name = "Работа", Price = 200 }]
            },
            new Maintenance
            {
                Vehicle = vehicle, User = owner, IsDeleted = true, PaidAmount = 9999m,
                Details = [new MaintenanceDetail { Name = "Удалённая работа", Price = 9999 }]
            },
            new Maintenance
            {
                Vehicle = foreignVehicle, User = owner, PaidAmount = 9999m,
                Details = [new MaintenanceDetail { Name = "Чужая работа", Price = 9999 }]
            });

        var plan = new SubscriptionPlan { Name = "Будущий тариф" };
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Organization = organization, SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddDays(1), EndDate = DateTime.UtcNow.AddDays(31)
        });

        await context.SaveChangesAsync();
        var service = new AdminOrganizationService(context);
        var card = await service.GetByIdAsync(organization.Id);

        Assert.NotNull(card);
        Assert.Equal(10, card.Members.Count);
        Assert.Equal(2, card.WorkOrdersCount);
        Assert.Equal(1, card.CompletedWorkOrdersCount);
        Assert.Equal(1100m, card.WorkOrdersTotal);
        Assert.Equal(500m, card.PaymentsTotal);
        Assert.Null(card.ActiveSubscriptionName);
        Assert.Null(await service.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task OrganizationCardKeepsMaintenanceAndStockTotalsSeparatedByCurrency()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var search = Guid.NewGuid().ToString("N");
        var organization = new Organization { Name = $"{search} A Организация CNY", OwnerUser = owner, CurrencyCode = "CNY" };
        var vehicle = new Vehicle { Organization = organization, User = owner };
        context.Maintenance.AddRange(
            new Maintenance
            {
                Vehicle = vehicle,
                User = owner,
                CurrencyCode = "CNY",
                PaidAmount = 30m,
                Details = [new MaintenanceDetail { Name = "Заказ CNY", Price = 50 }]
            },
            new Maintenance
            {
                Vehicle = vehicle,
                User = owner,
                CurrencyCode = "RUB",
                PaidAmount = 700m,
                DiscountAmount = 100m,
                Details = [new MaintenanceDetail { Name = "Исторический заказ RUB", Price = 1000 }]
            },
            new Maintenance
            {
                Vehicle = vehicle,
                User = owner,
                CurrencyCode = "RUB",
                IsDeleted = true,
                PaidAmount = 9000m,
                Details = [new MaintenanceDetail { Name = "Удалённый заказ", Price = 9000 }]
            });
        context.OrganizationMaterialStocks.AddRange(
            new OrganizationMaterialStock
            {
                Organization = organization,
                CurrencyCode = "CNY",
                Name = "Материал CNY",
                Unit = "шт",
                QuantityOnHand = 2m,
                AverageUnitCost = 10m,
                InventoryValue = 20m
            },
            new OrganizationMaterialStock
            {
                Organization = organization,
                CurrencyCode = "RUB",
                Name = "Исторический материал RUB",
                Unit = "шт",
                QuantityOnHand = 2m,
                AverageUnitCost = 200m,
                InventoryValue = 400m
            });
        var nextOrganization = new Organization { Name = $"{search} B Другая организация", OwnerUser = owner };
        context.Maintenance.Add(new Maintenance
        {
            Vehicle = new Vehicle { Organization = nextOrganization, User = owner },
            User = owner,
            CurrencyCode = "USD",
            PaidAmount = 10000m
        });
        await context.SaveChangesAsync();

        var page = await new AdminOrganizationService(context).GetAsync(search, null, "name", false, 1, 1);

        var card = Assert.Single(page.Organizations);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(organization.Id, card.Id);
        Assert.Equal("CNY", card.CurrencyCode);
        Assert.Equal(50m, card.WorkOrdersTotal);
        Assert.Equal(30m, card.PaymentsTotal);
        Assert.Equal(20m, card.InventoryValue);
        var otherCurrency = Assert.Single(card.OtherCurrencyTotals);
        Assert.Equal("RUB", otherCurrency.CurrencyCode);
        Assert.Equal(900m, otherCurrency.WorkOrdersTotal);
        Assert.Equal(700m, otherCurrency.PaymentsTotal);
        Assert.Equal(400m, otherCurrency.InventoryValue);
    }
}
