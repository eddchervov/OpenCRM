using System.Text.Json;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api.Tests.Unit;

public sealed class AdminOrganizationJsonTests
{
    [Fact]
    public void LegacyOrganizationJsonUsesRubAndEmptyOtherCurrencyTotals()
    {
        var organization = JsonSerializer.Deserialize<AdminOrganization>("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(organization);
        Assert.Equal("RUB", organization.CurrencyCode);
        Assert.Empty(organization.OtherCurrencyTotals);
    }

    [Fact]
    public void LegacyMaintenanceJsonUsesRub()
    {
        var maintenance = JsonSerializer.Deserialize<AdminUserOverviewMaintenance>("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(maintenance);
        Assert.Equal("RUB", maintenance.CurrencyCode);
    }
}
