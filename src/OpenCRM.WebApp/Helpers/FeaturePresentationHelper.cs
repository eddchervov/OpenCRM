using Microsoft.Extensions.Localization;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.WebApp.Infrastructure.Localization;

namespace Helpers;

/// <summary>
/// Форматирует и фильтрует фичи для отображения в интерфейсе.
/// </summary>
public static class FeaturePresentationHelper
{
    /// <summary> Код функции экспорта документов. </summary>
    private const string ExportDocumentsPresentationCode = "presentation.export.documents";

    /// <summary>
    /// Возвращает список видимых фич.
    /// </summary>
    public static IEnumerable<FeatureValueDto> GetVisibleFeatures(
        IEnumerable<FeatureValueDto>? features,
        bool includeVehiclesLimit = false)
    {
        if (features == null)
        {
            return Enumerable.Empty<FeatureValueDto>();
        }

        return features.Where(feature =>
            (includeVehiclesLimit || (feature.Code != FeatureCodes.VehiclesLimit && feature.Code != FeatureCodes.WorkspaceVehiclesLimit)) &&
            feature.AudienceCode != OpenCRM.Domain.FeatureCatalogCodes.Audiences.Admin &&
            (feature.BoolValue == true || feature.IntValue.HasValue));
    }

    /// <summary>
    /// Возвращает список фич для карточек тарифов с объединением близких пунктов.
    /// </summary>
    public static IEnumerable<FeatureValueDto> GetVisiblePresentationFeatures(
        IEnumerable<FeatureValueDto>? features,
        bool includeVehiclesLimit = false)
    {
        var exportAdded = false;

        foreach (var feature in GetVisibleFeatures(features, includeVehiclesLimit))
        {
            if (IsExportFeature(feature.Code))
            {
                if (exportAdded)
                {
                    continue;
                }

                exportAdded = true;
                yield return new FeatureValueDto
                {
                    Code = ExportDocumentsPresentationCode,
                    Name = "Экспорт и документы",
                    BoolValue = true,
                    AudienceCode = feature.AudienceCode,
                    AudienceName = feature.AudienceName,
                    ScopeCode = feature.ScopeCode,
                    ScopeName = feature.ScopeName,
                    ValueType = feature.ValueType
                };

                continue;
            }

            yield return feature;
        }
    }

    /// <summary>
    /// Возвращает лимит техники из списка фич.
    /// </summary>
    public static int? GetVehicleLimit(IEnumerable<FeatureValueDto>? features, int? fallback = null)
    {
        var values = features?.ToList();
        var value = values?.FirstOrDefault(feature => feature.Code == FeatureCodes.WorkspaceVehiclesLimit)?.IntValue
            ?? values?.FirstOrDefault(feature => feature.Code == FeatureCodes.VehiclesLimit)?.IntValue;

        return value ?? fallback;
    }

    /// <summary>
    /// Совместимый алиас для получения лимита техники.
    /// </summary>
    public static int? GetVehiclesLimit(IEnumerable<FeatureValueDto>? features, int? fallback = null)
    {
        return GetVehicleLimit(features, fallback);
    }

    /// <summary>
    /// Возвращает человекочитаемое название фичи.
    /// </summary>
    public static string FormatFeature(FeatureValueDto feature, IStringLocalizer<LocalizationStrings> text)
    {
        return feature.Code switch
        {
            ExportDocumentsPresentationCode => text["featurePresentation.exports"].Value,
            FeatureCodes.VehiclesModule => text["featurePresentation.vehicles"].Value,
            FeatureCodes.ExportWord => text["featurePresentation.word"].Value,
            FeatureCodes.ExportWordWithPhotos => text["featurePresentation.wordPhotos"].Value,
            FeatureCodes.ExportPdf => text["featurePresentation.pdf"].Value,
            FeatureCodes.ExportPdfWithPhotos => text["featurePresentation.pdfPhotos"].Value,
            FeatureCodes.VehiclePhotoAlbums => text["featurePresentation.albums"].Value,
            FeatureCodes.VehiclesLimit => text["featurePresentation.vehicleLimit", feature.IntValue!].Value,
            FeatureCodes.WorkspaceVehiclesLimit => text["featurePresentation.vehicleLimit", feature.IntValue!].Value,
            FeatureCodes.WorkspaceClientsLimit => text["featurePresentation.clientLimit", feature.IntValue!].Value,
            FeatureCodes.WorkspaceMembersLimit => text["featurePresentation.memberLimit", feature.IntValue!].Value,
            FeatureCodes.ClientsModule => text["featurePresentation.clients"].Value,
            FeatureCodes.DefectsModule => text["featurePresentation.defects"].Value,
            FeatureCodes.CountersModule => text["featurePresentation.counters"].Value,
            FeatureCodes.MaintenanceModule => text["featurePresentation.maintenance"].Value,
            FeatureCodes.SchedulerModule => text["featurePresentation.scheduler"].Value,
            FeatureCodes.RemindersModule => text["featurePresentation.reminders"].Value,
            FeatureCodes.DocumentsModule => text["featurePresentation.documents"].Value,
            FeatureCodes.MaterialsModule => text["featurePresentation.materials"].Value,
            FeatureCodes.WorkJournalModule => text["featurePresentation.workJournal"].Value,
            FeatureCodes.FinanceModule => text["featurePresentation.finance"].Value,
            FeatureCodes.AnalyticsModule => text["featurePresentation.analytics"].Value,
            FeatureCodes.WikiModule => text["featurePresentation.wiki"].Value,
            FeatureCodes.MembersModule => text["featurePresentation.members"].Value,
            FeatureCodes.BillingModule => text["featurePresentation.billing"].Value,
            FeatureCodes.SettingsModule => text["featurePresentation.settings"].Value,
            _ when feature.IntValue.HasValue => $"{feature.Name}: {feature.IntValue}",
            _ => feature.Name
        };
    }

    /// <summary>
    /// Возвращает короткое описание фичи для карточек тарифов.
    /// </summary>
    public static string? GetFeatureDescription(FeatureValueDto feature, IStringLocalizer<LocalizationStrings> text)
    {
        return feature.Code switch
        {
            ExportDocumentsPresentationCode => text["featurePresentation.exportsHelp"].Value,
            FeatureCodes.VehiclesModule => text["featurePresentation.vehiclesHelp"].Value,
            FeatureCodes.ExportWord => text["featurePresentation.wordHelp"].Value,
            FeatureCodes.ExportWordWithPhotos => text["featurePresentation.wordPhotosHelp"].Value,
            FeatureCodes.ExportPdf => text["featurePresentation.pdfHelp"].Value,
            FeatureCodes.ExportPdfWithPhotos => text["featurePresentation.pdfPhotosHelp"].Value,
            FeatureCodes.VehiclePhotoAlbums => text["featurePresentation.albumsHelp"].Value,
            FeatureCodes.VehiclesLimit => text["featurePresentation.personalVehicleLimitHelp"].Value,
            FeatureCodes.WorkspaceVehiclesLimit => text["featurePresentation.vehicleLimitHelp"].Value,
            FeatureCodes.WorkspaceMembersLimit => text["featurePresentation.memberLimitHelp"].Value,
            FeatureCodes.ClientsModule => text["featurePresentation.clientsHelp"].Value,
            FeatureCodes.DefectsModule => text["featurePresentation.defectsHelp"].Value,
            FeatureCodes.CountersModule => text["featurePresentation.countersHelp"].Value,
            FeatureCodes.MaintenanceModule => text["featurePresentation.maintenanceHelp"].Value,
            FeatureCodes.SchedulerModule => text["featurePresentation.schedulerHelp"].Value,
            FeatureCodes.RemindersModule => text["featurePresentation.remindersHelp"].Value,
            FeatureCodes.DocumentsModule => text["featurePresentation.documentsHelp"].Value,
            FeatureCodes.MaterialsModule => text["featurePresentation.materialsHelp"].Value,
            FeatureCodes.WorkJournalModule => text["featurePresentation.workJournalHelp"].Value,
            FeatureCodes.FinanceModule => text["featurePresentation.financeHelp"].Value,
            FeatureCodes.AnalyticsModule => text["featurePresentation.analyticsHelp"].Value,
            FeatureCodes.WikiModule => text["featurePresentation.wikiHelp"].Value,
            FeatureCodes.MembersModule => text["featurePresentation.membersHelp"].Value,
            FeatureCodes.BillingModule => text["featurePresentation.billingHelp"].Value,
            FeatureCodes.SettingsModule => text["featurePresentation.settingsHelp"].Value,
            _ => null
        };
    }

    /// <summary>
    /// Возвращает резервные описания возможностей продукта, если тариф пока содержит мало настроенных фич.
    /// </summary>
    public static IReadOnlyList<(string Title, string Description)> GetProductHighlights(bool business, IStringLocalizer<LocalizationStrings> text)
    {
        if (!business)
        {
            return
            [
                (text["featurePresentation.personalJournal"].Value, text["featurePresentation.personalJournalHelp"].Value),
                (text["featurePresentation.export"].Value, text["featurePresentation.exportHelp"].Value),
                (text["featurePresentation.reminders"].Value, text["featurePresentation.personalRemindersHelp"].Value)
            ];
        }

        return
        [
            (text["featurePresentation.clients"].Value, text["featurePresentation.businessClientsHelp"].Value),
            (text["featurePresentation.scheduler"].Value, text["featurePresentation.businessSchedulerHelp"].Value),
            (text["featurePresentation.defects"].Value, text["featurePresentation.businessDefectsHelp"].Value),
            (text["featurePresentation.documents"].Value, text["featurePresentation.documentsHelp"].Value),
            (text["featurePresentation.materialsShort"].Value, text["featurePresentation.businessMaterialsHelp"].Value),
            (text["featurePresentation.team"].Value, text["featurePresentation.teamHelp"].Value)
        ];
    }

    /// <summary> Определяет, относится ли код функции к экспорту. </summary>
    private static bool IsExportFeature(string code)
    {
        return code is FeatureCodes.ExportWord
            or FeatureCodes.ExportWordWithPhotos
            or FeatureCodes.ExportPdf
            or FeatureCodes.ExportPdfWithPhotos;
    }
}
