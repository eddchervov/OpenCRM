using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceDetailRulesIntegrationTests(MaintenanceDatabaseFixture fixture)
    : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task ServiceValidationAllowsOneCatalogLineWithoutPreviousDetails()
    {
        await using var context = fixture.CreateContext();
        var service = await MaintenanceDetailRulesIntegrationTests.CreateServiceItemAsync(context);
        var rules = MaintenanceDetailRulesIntegrationTests.CreateRules(context);
        var details = new[] { MaintenanceDetailRulesIntegrationTests.CreateServiceLine(service.Id, "Устаревшее название") };

        await rules.ValidateServiceDetailsAsync(service.OrganizationId, details);

        Assert.Equal(service.Name, Assert.Single(details).Name);
    }

    [Fact]
    public async Task ServiceValidationAllowsDuplicateCatalogLinesPresentInPreviousSnapshot()
    {
        await using var context = fixture.CreateContext();
        var service = await MaintenanceDetailRulesIntegrationTests.CreateServiceItemAsync(context);
        var rules = MaintenanceDetailRulesIntegrationTests.CreateRules(context);
        var details = new[]
        {
            MaintenanceDetailRulesIntegrationTests.CreateServiceLine(service.Id, service.Name),
            MaintenanceDetailRulesIntegrationTests.CreateServiceLine(service.Id, service.Name)
        };
        var previousDetails = new[]
        {
            MaintenanceDetailRulesIntegrationTests.CreateServiceLine(service.Id, service.Name),
            MaintenanceDetailRulesIntegrationTests.CreateServiceLine(service.Id, service.Name)
        };

        await rules.ValidateServiceDetailsAsync(service.OrganizationId, details, previousDetails);

        Assert.All(details, detail => Assert.Equal(service.Name, detail.Name));
    }

    [Fact]
    public async Task ServiceValidationRestoresHistoricalNameForRenamedCatalogLine()
    {
        await using var context = fixture.CreateContext();
        var service = await MaintenanceDetailRulesIntegrationTests.CreateServiceItemAsync(context);
        var rules = MaintenanceDetailRulesIntegrationTests.CreateRules(context);
        var details = new[] { MaintenanceDetailRulesIntegrationTests.CreateServiceLine(service.Id, "Новое название") };
        var previousDetails = new[] { MaintenanceDetailRulesIntegrationTests.CreateServiceLine(service.Id, "Историческое название") };

        await rules.ValidateServiceDetailsAsync(service.OrganizationId, details, previousDetails);

        Assert.Equal("Историческое название", Assert.Single(details).Name);
    }

    [Fact]
    public async Task ServiceValidationDoesNotMatchHistoricalRowWithoutCatalogId()
    {
        await using var context = fixture.CreateContext();
        var service = await MaintenanceDetailRulesIntegrationTests.CreateServiceItemAsync(context);
        var rules = MaintenanceDetailRulesIntegrationTests.CreateRules(context);
        var details = new[] { MaintenanceDetailRulesIntegrationTests.CreateServiceLine(service.Id, "Подменённое название") };
        var previousDetails = new[] { new MaintenanceDetail { Name = "Несвязанная историческая работа" } };

        await rules.ValidateServiceDetailsAsync(service.OrganizationId, details, previousDetails);

        Assert.Equal(service.Name, Assert.Single(details).Name);
    }

    private static MaintenanceDetailRules CreateRules(OpenCRMContext context) =>
        new(context, new MaintenanceEmployeeResolver(context));

    private static async Task<OrganizationScenarioCatalogItem> CreateServiceItemAsync(OpenCRMContext context)
    {
        var user = new ApplicationUser
        {
            Email = $"maintenance-detail-rules-{Guid.NewGuid():N}@example.test",
            UserName = $"maintenance-detail-rules-{Guid.NewGuid():N}"
        };
        var organization = new Organization
        {
            Name = "Организация проверки правил строк",
            PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation,
            OwnerUser = user,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };
        var service = new OrganizationScenarioCatalogItem
        {
            Organization = organization,
            ScenarioCode = OrganizationTypeCodes.ServiceStation,
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Code = $"service-{Guid.NewGuid():N}",
            Name = "Полировка кузова"
        };
        context.OrganizationScenarioCatalogItems.Add(service);
        await context.SaveChangesAsync();

        return service;
    }

    private static MaintenanceDetail CreateServiceLine(Guid catalogItemId, string name) => new()
    {
        CatalogItemId = catalogItemId,
        Name = name,
        Price = 1000
    };
}
