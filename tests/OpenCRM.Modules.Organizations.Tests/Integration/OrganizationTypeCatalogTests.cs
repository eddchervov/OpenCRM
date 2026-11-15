using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationTypeCatalogTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task UserCanCreateAndReadVisibleOrganizationTypesWithoutAccountProfile()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = $"multi-type-{Guid.NewGuid():N}@example.test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);
        var commands = services.GetRequiredService<IOrganizationProfileCommandService>();
        var codes = new[] { OrganizationTypeCodes.Detailing, OrganizationTypeCodes.ServiceStation };

        foreach (var code in codes)
        {
            await commands.CreateAsync(user.Id, new CreateOrganizationDto { Name = code, PrimaryScenarioCode = code });
        }

        var overview = await services.GetRequiredService<IOrganizationQueryService>().GetOverviewAsync(user.Id);

        Assert.Equal(codes.Order(), overview.Organizations.Select(x => x.PrimaryScenarioCode).Order());
        Assert.All(overview.Organizations, organization => Assert.False(string.IsNullOrWhiteSpace(organization.PrimaryScenarioName)));
    }

    [Fact]
    public async Task CatalogUsesSupportedTypesAndPreservesConfiguredNamesAndOrder()
    {
        await using var context = fixture.CreateContext();
        var detailing = await context.OrganizationTypeDefinitions.SingleAsync(x => x.Code == OrganizationTypeCodes.Detailing);
        var originalName = detailing.Name;
        var originalOrder = detailing.SortOrder;

        try
        {
            detailing.Name = "Студия детейлинга";
            detailing.SortOrder = -1;
            await context.SaveChangesAsync();
            var catalog = new OrganizationTypeCatalog(context);

            var options = await catalog.GetOrganizationTypeOptionsAsync();

            Assert.Equal(2, options.Count);
            Assert.DoesNotContain(options, option => option.Code == OrganizationTypeCodes.Fleet);
            Assert.Equal("Автосервис", options.Single(option => option.Code == OrganizationTypeCodes.ServiceStation).Name);
            Assert.Equal(OrganizationTypeCodes.Detailing, options[0].Code);
            Assert.Equal("Студия детейлинга", options[0].Name);
            Assert.All(options, option => Assert.Equal(option.Code, Services.Interfaces.Organizations.OrganizationScenarioProcessCatalog.Resolve(option.Code).Code));
        }
        finally
        {
            detailing.Name = originalName;
            detailing.SortOrder = originalOrder;
            await context.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData("fleet")]
    [InlineData(" FLEET ")]
    public async Task HiddenFleetCannotBeCreatedAndDoesNotFallBackToAnotherType(string scenario)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = $"hidden-fleet-{Guid.NewGuid():N}@example.test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var count = await context.Organizations.CountAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);
        var commands = services.GetRequiredService<IOrganizationProfileCommandService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.CreateAsync(user.Id,
            new CreateOrganizationDto { Name = "Скрытый сценарий", PrimaryScenarioCode = scenario }));

        Assert.Equal(count, await context.Organizations.CountAsync());
        Assert.False(await context.Organizations.AnyAsync(organization => organization.OwnerUserId == user.Id));
    }

    [Fact]
    public async Task ExistingFleetRemainsVisibleToItsOwnerAndRetainsItsType()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.PrimaryScenarioCode = OrganizationTypeCodes.Fleet;
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationQueryService>();

        var saved = await query.GetByIdAsync(organization.OwnerUserId, organization.Id);
        var overview = await query.GetOverviewAsync(organization.OwnerUserId);

        Assert.Equal(OrganizationTypeCodes.Fleet, saved.PrimaryScenarioCode);
        Assert.Equal("Автопарк", saved.PrimaryScenarioName);
        Assert.Contains(overview.Organizations, item => item.Id == organization.Id);
        await new OrganizationScenarioReader(context).EnsureUnchangedAsync(OrganizationTypeCodes.Fleet, " FLEET ");
    }

    [Fact]
    public async Task InactiveTypeCannotBeSelectedForCreationButExistingTypeCanBeRetained()
    {
        await using var context = fixture.CreateContext();
        var fleet = await context.OrganizationTypeDefinitions.SingleAsync(x => x.Code == OrganizationTypeCodes.Fleet);
        var wasActive = fleet.IsActive;

        try
        {
            fleet.IsActive = false;
            await context.SaveChangesAsync();
            var reader = new OrganizationScenarioReader(context);

            var options = await new OrganizationTypeCatalog(context).GetOrganizationTypeOptionsAsync();

            Assert.DoesNotContain(options, x => x.Code == OrganizationTypeCodes.Fleet);
            await reader.EnsureUnchangedAsync(OrganizationTypeCodes.Fleet, " FLEET ");
            await reader.EnsureUnchangedAsync("historical-type", "historical-type");
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.EnsureUnchangedAsync("historical-type", "another-type"));
        }
        finally
        {
            fleet.IsActive = wasActive;
            await context.SaveChangesAsync();
        }
    }
}
