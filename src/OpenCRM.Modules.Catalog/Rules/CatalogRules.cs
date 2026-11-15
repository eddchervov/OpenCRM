using OpenCRM.Domain;

namespace OpenCRM.Modules.Catalog.Rules;

/// <summary> Правила нормализации и проверки данных каталога. </summary>
internal static class CatalogRules
{
    /// <summary> Нормализует обязательное название. </summary>
    internal static string NormalizeName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Укажите название элемента справочника.");
        }

        return name.Length > 160 ? name[..160] : name;
    }

    /// <summary> Нормализует необязательное строковое значение. </summary>
    internal static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length > 1000 ? normalized[..1000] : normalized;
    }

    /// <summary> Определяет код вида документа по названию. </summary>
    internal static string InferDocumentKindCode(string? name)
    {
        var text = name?.Trim().ToLowerInvariant() ?? string.Empty;
        if (text.Contains("заказ") || text.Contains("наряд") || text.Contains("заявка"))
        {
            return OrganizationScenarioDocumentKindCodes.WorkOrder;
        }

        if (text.Contains("акт приема") || text.Contains("акт приёма") || text.Contains("осмотр") || text.Contains("диагност"))
        {
            return OrganizationScenarioDocumentKindCodes.InspectionAct;
        }

        if (text.Contains("акт"))
        {
            return OrganizationScenarioDocumentKindCodes.WorkAct;
        }

        if (text.Contains("фото"))
        {
            return OrganizationScenarioDocumentKindCodes.PhotoReport;
        }

        if (text.Contains("рекоменд") || text.Contains("памят"))
        {
            return OrganizationScenarioDocumentKindCodes.Recommendations;
        }

        if (text.Contains("материал") || text.Contains("спецификац") || text.Contains("затрат"))
        {
            return OrganizationScenarioDocumentKindCodes.MaterialsSpec;
        }

        if (text.Contains("чек") || text.Contains("квитанц"))
        {
            return OrganizationScenarioDocumentKindCodes.Receipt;
        }

        if (text.Contains("готов"))
        {
            return OrganizationScenarioDocumentKindCodes.ReadinessReport;
        }

        if (text.Contains("журнал") || text.Contains("лист"))
        {
            return OrganizationScenarioDocumentKindCodes.Checklist;
        }

        return OrganizationScenarioDocumentKindCodes.MaintenanceReport;
    }

    /// <summary> Проверяет, является ли устаревший документ неподдерживаемым. </summary>
    internal static bool IsUnsupportedLegacyDocument(OrganizationScenarioCatalogItem item)
    {
        var text = $"{item.Code} {item.Name}".ToLowerInvariant();
        return text.Contains("до/после", StringComparison.OrdinalIgnoreCase)
            || text.Contains("до-после", StringComparison.OrdinalIgnoreCase)
            || text.Contains("before-after", StringComparison.OrdinalIgnoreCase)
            || text.Contains("before/after", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Формирует слаг из строки. </summary>
    internal static string Slug(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
