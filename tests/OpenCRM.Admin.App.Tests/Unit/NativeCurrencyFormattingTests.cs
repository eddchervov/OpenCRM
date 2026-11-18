using System.Globalization;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Unit;

[Collection("Admin localization")]
public sealed class NativeCurrencyFormattingTests
{
    [Theory]
    [InlineData("en-GB")]
    [InlineData("zh-CN")]
    [InlineData("ru-RU")]
    public void OrganizationMetricsKeepEveryCurrencyAndRegionalDecimalFormat(string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var culture = CultureInfo.GetCultureInfo(cultureName);
        using var http = new HttpClient { BaseAddress = new Uri("https://example.invalid/") };
        using var session = new AdminSessionClient(http);
        try
        {
            CultureInfo.CurrentCulture = culture;
            var model = new OrganizationsViewModel(session)
            {
                Organization = new AdminOrganization
                {
                    CurrencyCode = "CNY",
                    WorkOrdersTotal = 50.12m,
                    PaymentsTotal = 30.34m,
                    InventoryValue = 20.56m,
                    OtherCurrencyTotals = [new AdminOrganizationCurrencyTotal
                    {
                        CurrencyCode = "RUB", WorkOrdersTotal = 900m, PaymentsTotal = 700m, InventoryValue = 400m
                    }]
                }
            };

            Assert.Equal($"{50.12m.ToString("N2", culture)} CNY{Environment.NewLine}{900m.ToString("N2", culture)} RUB", model.WorkOrdersTotalLabel);
            Assert.Equal($"{30.34m.ToString("N2", culture)} CNY{Environment.NewLine}{700m.ToString("N2", culture)} RUB", model.PaymentsTotalLabel);
            Assert.Equal($"{20.56m.ToString("N2", culture)} CNY{Environment.NewLine}{400m.ToString("N2", culture)} RUB", model.InventoryValueLabel);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("zh-CN")]
    public void UserCardKeepsMaintenanceSnapshotCurrencyAndCommercialRubles(string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var culture = CultureInfo.GetCultureInfo(cultureName);
        try
        {
            CultureInfo.CurrentCulture = culture;
            var vehicleId = Guid.NewGuid();
            var date = new DateTime(2026, 11, 16, 23, 30, 0, DateTimeKind.Utc);
            var model = new UserCardViewModel(new AdminUserOverview
            {
                Vehicles = [new AdminUserOverviewVehicle { Id = vehicleId, LastMaintenanceAt = date }],
                Maintenances = [new AdminUserOverviewMaintenance
                {
                    VehicleId = vehicleId, Date = date, PaidAmount = 1234.56m, CurrencyCode = "CNY"
                }],
                Payments = [new AdminUserOverviewPayment { Amount = 10.25m }]
            }, true);
            var fields = model.Tabs.SelectMany(tab => tab.Items).SelectMany(item => item.Fields).ToArray();

            Assert.Contains(fields, field => field.Value == $"{1234.56m.ToString("N2", culture)} CNY");
            Assert.Contains(fields, field => field.Value == $"{10.25m.ToString("N2", culture)} RUB");
            Assert.Contains(fields, field => field.Value == date.ToString("d", culture));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
