using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Unit;

[Collection("Admin localization")]
public sealed class BillingRowTests
{
    [Fact]
    public void BuiltInPaymentLabelsRefreshByCodeAndUnknownLabelsStayRaw()
    {
        var localization = AdminLocalization.Instance;
        var previousLanguage = localization.Language;
        var preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-billing-row-{Guid.NewGuid():N}.txt");
        using var preferenceScope = localization.UsePreferencePath(preferencePath);

        try
        {
            localization.Language = "ru";
            var row = new BillingRow(new AdminBillingPayment
            {
                PaymentStatus = "completed",
                StatusName = "Оплачено",
                PaymentMethod = "yookassa_extension",
                PaymentMethodName = "YooKassa: продление"
            });

            localization.Language = "en";
            row.RefreshLocalizedValues();
            Assert.Equal("Paid", row.Status);
            Assert.Equal("YooKassa: extension", row.Source);

            localization.Language = "zh-Hans";
            row.RefreshLocalizedValues();
            Assert.Equal("已付款", row.Status);
            Assert.Equal("YooKassa：续订", row.Source);

            var unknown = new BillingRow(new AdminBillingPayment
            {
                PaymentStatus = "provider_custom_status",
                StatusName = "Provider status",
                PaymentMethod = "custom_method",
                PaymentMethodName = "Provider method"
            });
            unknown.RefreshLocalizedValues();
            Assert.Equal("Provider status", unknown.Status);
            Assert.Equal("Provider method", unknown.Source);
        }
        finally
        {
            localization.Language = previousLanguage;
            if (File.Exists(preferencePath))
            {
                File.Delete(preferencePath);
            }
        }
    }

    [Fact]
    public void FutureSubscriptionHasItsOwnStatus()
    {
        var localization = AdminLocalization.Instance;
        var previousLanguage = localization.Language;
        var preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-billing-row-{Guid.NewGuid():N}.txt");
        using var preferenceScope = localization.UsePreferencePath(preferencePath);

        try
        {
            localization.Language = "ru";
            var subscription = new AdminBillingOrganizationSubscription
            {
                StartDate = DateTime.UtcNow.AddDays(1),
                EndDate = DateTime.UtcNow.AddDays(31)
            };

            Assert.Equal("Ещё не началась", new BillingRow(subscription).Status);
        }
        finally
        {
            localization.Language = previousLanguage;
            if (File.Exists(preferencePath))
            {
                File.Delete(preferencePath);
            }
        }
    }
}
