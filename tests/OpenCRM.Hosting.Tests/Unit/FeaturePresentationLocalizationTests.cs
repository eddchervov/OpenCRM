using System.Globalization;
using Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.WebApp.Infrastructure.Localization;

namespace OpenCRM.Hosting.Tests.Unit;

public sealed class FeaturePresentationLocalizationTests
{
    [Theory]
    [InlineData("en-US", "Vehicle limit: 300", "Exports and documents")]
    [InlineData("zh-Hans", "车辆数量上限：300", "导出与文档")]
    public void KnownFeaturesUseReaderLanguageAndPreserveCustomData(
        string cultureName,
        string expectedLimit,
        string expectedExport)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(options => options.ResourcesPath = "Resources")
            .BuildServiceProvider();
        var text = services.GetRequiredService<IStringLocalizer<LocalizationStrings>>();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var limit = new FeatureValueDto { Code = FeatureCodes.WorkspaceVehiclesLimit, IntValue = 300 };
            var export = Assert.Single(FeaturePresentationHelper.GetVisiblePresentationFeatures(
                [new FeatureValueDto { Code = FeatureCodes.ExportPdf, BoolValue = true }]));
            var custom = new FeatureValueDto { Code = "custom", Name = "自定义 возможность", IntValue = 42 };

            Assert.Equal(expectedLimit, FeaturePresentationHelper.FormatFeature(limit, text));
            Assert.Equal(expectedExport, FeaturePresentationHelper.FormatFeature(export, text));
            Assert.Equal("自定义 возможность: 42", FeaturePresentationHelper.FormatFeature(custom, text));
            Assert.Null(FeaturePresentationHelper.GetFeatureDescription(custom, text));
            Assert.DoesNotContain("featurePresentation.", FeaturePresentationHelper.GetFeatureDescription(export, text));
            Assert.All(FeaturePresentationHelper.GetProductHighlights(true, text), highlight =>
            {
                Assert.DoesNotContain("featurePresentation.", highlight.Title);
                Assert.DoesNotContain("featurePresentation.", highlight.Description);
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
