using OpenCRM.Admin.App.Localization;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Unit;

[Collection("Admin localization")]
public sealed class AdminLocalizedOptionsTests
{
    [Fact]
    public void LocalizedOptionsChangeLabelsWithoutChangingTheirCodes()
    {
        var localization = AdminLocalization.Instance;
        var previousLanguage = localization.Language;
        var preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-admin-options-{Guid.NewGuid():N}.txt");

        using var preferenceScope = localization.UsePreferencePath(preferencePath);
        try
        {
            var communicationOption = CommunicationOption.Localized("active", "native.billing.aktivnye");
            var promoOption = PromoCodeOption.Localized("percentage", "native.promoCodes.skidka.v.protsentakh");

            localization.Language = "en";
            communicationOption.RefreshLocalizedValues();
            promoOption.RefreshLocalizedValues();
            Assert.Equal("active", communicationOption.Code);
            Assert.Equal("Active", communicationOption.Name);
            Assert.Equal("percentage", promoOption.Code);
            Assert.Equal("Percentage discount", promoOption.Name);

            localization.Language = "zh-Hans";
            communicationOption.RefreshLocalizedValues();
            promoOption.RefreshLocalizedValues();
            Assert.Equal("active", communicationOption.Code);
            Assert.Equal("percentage", promoOption.Code);
            Assert.NotEqual("Active", communicationOption.Name);
            Assert.NotEqual("Percentage discount", promoOption.Name);
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
