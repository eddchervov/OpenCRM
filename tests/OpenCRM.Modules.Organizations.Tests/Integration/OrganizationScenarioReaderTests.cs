using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Modules.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationScenarioReaderTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task LoadNamesReturnsScenarioNamesUsingCaseInsensitiveKeys()
    {
        await using var context = fixture.CreateContext();
        var reader = new OrganizationScenarioReader(context);

        var names = await reader.LoadNamesAsync();

        Assert.Equal("Детейлинг", names["DETAILING"]);
        Assert.Equal("Автосервис", names["service-station"]);
    }

    [Theory]
    [InlineData(" STO ", "service-station")]
    [InlineData("detail", "detailing")]
    [InlineData("unknown", "detailing")]
    [InlineData(null, "service-station")]
    public async Task NormalizeUsesAliasesAndFallsBackToDetailing(string? code, string expected)
    {
        await using var context = fixture.CreateContext();
        var reader = new OrganizationScenarioReader(context);

        Assert.Equal(expected, await reader.NormalizeAsync(code));
    }

    [Fact]
    public async Task NormalizeFallsBackWhenSupportedScenarioIsInactive()
    {
        await using var context = fixture.CreateContext();
        var serviceStation = await context.OrganizationTypeDefinitions.SingleAsync(item => item.Code == OrganizationTypeCodes.ServiceStation);
        serviceStation.IsActive = false;
        await context.SaveChangesAsync();

        try
        {
            var reader = new OrganizationScenarioReader(context);

            Assert.Equal("detailing", await reader.NormalizeAsync(OrganizationTypeCodes.ServiceStation));
        }
        finally
        {
            serviceStation.IsActive = true;
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task EnsureUnchangedAllowsMissingOrEquivalentAliasAndRejectsChange()
    {
        await using var context = fixture.CreateContext();
        var reader = new OrganizationScenarioReader(context);

        await reader.EnsureUnchangedAsync("service-station", " ");
        await reader.EnsureUnchangedAsync("service-station", "sto");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.EnsureUnchangedAsync("service-station", "detailing"));
        Assert.Equal("Тип организации нельзя изменить после создания.", exception.Message);
    }
}
