using OpenCRM.Services.Interfaces.Features.Dtos;

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
    public static string FormatFeature(FeatureValueDto feature)
    {
        return feature.Code switch
        {
            ExportDocumentsPresentationCode => "Экспорт и документы",
            FeatureCodes.VehiclesModule => "Техника",
            FeatureCodes.ExportWord => "Экспорт Word",
            FeatureCodes.ExportWordWithPhotos => "Экспорт Word с фото",
            FeatureCodes.ExportPdf => "Экспорт PDF",
            FeatureCodes.ExportPdfWithPhotos => "Экспорт PDF с фото",
            FeatureCodes.VehiclePhotoAlbums => "Альбомы фото техники",
            FeatureCodes.VehiclesLimit => $"Лимит техники: {feature.IntValue}",
            FeatureCodes.WorkspaceVehiclesLimit => $"Лимит техники: {feature.IntValue}",
            FeatureCodes.WorkspaceMembersLimit => $"Лимит участников: {feature.IntValue}",
            FeatureCodes.ClientsModule => "Клиенты",
            FeatureCodes.DefectsModule => "Неисправности",
            FeatureCodes.CountersModule => "Пробег и наработка",
            FeatureCodes.MaintenanceModule => "Обслуживание",
            FeatureCodes.SchedulerModule => "Планирование",
            FeatureCodes.RemindersModule => "Напоминания",
            FeatureCodes.DocumentsModule => "Документы",
            FeatureCodes.MaterialsModule => "Материалы и запчасти",
            FeatureCodes.WorkJournalModule => "Журнал работ",
            FeatureCodes.FinanceModule => "Финансы",
            FeatureCodes.AnalyticsModule => "Статистика",
            FeatureCodes.WikiModule => "Вики организации",
            FeatureCodes.MembersModule => "Участники",
            FeatureCodes.BillingModule => "Тарифы и оплата",
            FeatureCodes.SettingsModule => "Настройки организации",
            _ when feature.IntValue.HasValue => $"{feature.Name}: {feature.IntValue}",
            _ => feature.Name
        };
    }

    /// <summary>
    /// Возвращает короткое описание фичи для карточек тарифов.
    /// </summary>
    public static string? GetFeatureDescription(FeatureValueDto feature)
    {
        return feature.Code switch
        {
            ExportDocumentsPresentationCode => "PDF и Word: отчеты, акты, документы и фотофиксация для клиента или печати.",
            FeatureCodes.VehiclesModule => "Единая база техники организации с карточками, владельцами и привязками.",
            FeatureCodes.ExportWord => "Выгрузка отчетов и документов в Word для передачи клиенту или печати.",
            FeatureCodes.ExportWordWithPhotos => "Word-документы с фотографиями неисправностей, работ и состояния техники.",
            FeatureCodes.ExportPdf => "PDF-отчеты, акты и документы в удобном формате для клиента.",
            FeatureCodes.ExportPdfWithPhotos => "PDF-документы с фотофиксацией, чек-листами и результатами работ.",
            FeatureCodes.VehiclePhotoAlbums => "Галерея техники и альбомы с фото, доступные на всех тарифах.",
            FeatureCodes.VehiclesLimit => "Сколько единиц техники можно вести в личном журнале.",
            FeatureCodes.WorkspaceVehiclesLimit => "Сколько единиц техники можно вести внутри организации.",
            FeatureCodes.WorkspaceMembersLimit => "Сколько сотрудников можно подключить к рабочему пространству.",
            FeatureCodes.ClientsModule => "CRM-клиенты, история обращений, техника клиента и быстрые действия.",
            FeatureCodes.DefectsModule => "Фиксация проблем, статусы, фото и связь неисправностей с обслуживанием.",
            FeatureCodes.CountersModule => "Пробег, моточасы, история показаний и контроль регламентов.",
            FeatureCodes.MaintenanceModule => "Работы, ТО, ремонты, история обслуживания и расход материалов.",
            FeatureCodes.SchedulerModule => "График ТО, ремонтов, CRM-задач, переносов и загрузки команды.",
            FeatureCodes.RemindersModule => "Напоминания по задачам, обслуживанию, срокам и ручным событиям.",
            FeatureCodes.DocumentsModule => "Акты, заказ-наряды, чек-листы, отчеты и экспорт под сценарий организации.",
            FeatureCodes.MaterialsModule => "Склад материалов и запчастей: остатки, приход, списание и история движения.",
            FeatureCodes.WorkJournalModule => "Единый журнал работ, событий обслуживания и изменений по технике.",
            FeatureCodes.FinanceModule => "Финансовая сводка: выручка, долги, себестоимость, прибыль, графики и заказы за период.",
            FeatureCodes.AnalyticsModule => "Отдельный раздел с KPI, графиками, отчетами, выручкой и сводными показателями.",
            FeatureCodes.WikiModule => "Встроенная справка по рабочему пространству и процессам организации.",
            FeatureCodes.MembersModule => "Команда, роли, права и доступ сотрудников к разделам организации.",
            FeatureCodes.BillingModule => "Тарифы, подписка, оплата и состояние доступа рабочего пространства.",
            FeatureCodes.SettingsModule => "Профиль организации, реквизиты, справочники сценариев и история изменений.",
            _ => null
        };
    }

    /// <summary>
    /// Возвращает резервные описания возможностей продукта, если тариф пока содержит мало настроенных фич.
    /// </summary>
    public static IReadOnlyList<(string Title, string Description)> GetProductHighlights(bool business)
    {
        if (!business)
        {
            return
            [
                ("Журнал техники", "Карточки автомобилей, история обслуживания, пробег и документы."),
                ("Экспорт", "PDF и Word-отчеты для хранения, печати и передачи."),
                ("Напоминания", "Контроль сроков, обслуживания и важных событий по технике.")
            ];
        }

        return
        [
            ("Клиенты", "CRM-карточки, история, техника клиента и быстрый переход к работам."),
            ("Планирование", "График ТО, ремонтов, CRM-задач, ответственные и сроки."),
            ("Неисправности", "Регистрация дефектов, статусы, фото и связь с обслуживанием."),
            ("Документы", "Акты, заказ-наряды, чек-листы, отчеты и экспорт под сценарий организации."),
            ("Материалы", "Склад, остатки, приход, списание и расход в обслуживании."),
            ("Команда", "Участники, роли, права и доступность модулей рабочего пространства.")
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
