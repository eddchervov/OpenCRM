using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Defects.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Vehicles.Defects;

/// <summary> Правила нормализации и отображения дефектов техники. </summary>
internal static class VehicleDefectRules
{
    /// <summary> Строит список изменений дефекта по сравнению со снимком. </summary>
    internal static List<string> BuildDefectChanges(VehicleDefectSnapshot previous, VehicleDefect defect)
    {
        var changes = new List<string>();
        AddChange(changes, "Название", previous.Title, defect.Title);
        AddChange(changes, "Описание", previous.Description, defect.Description);
        AddChange(changes, "Критичность", VehicleDefectSeverityCodes.GetName(previous.SeverityCode), VehicleDefectSeverityCodes.GetName(defect.SeverityCode));
        AddChange(changes, "Состояние", VehicleDefectStatusCodes.GetName(previous.StatusCode), VehicleDefectStatusCodes.GetName(defect.StatusCode));
        return changes;
    }

    /// <summary> Добавляет описание изменения поля в список. </summary>
    private static void AddChange(List<string> changes, string fieldName, string? previousValue, string? currentValue)
    {
        var previousDisplay = FormatNullableText(previousValue);
        var currentDisplay = FormatNullableText(currentValue);
        if (string.Equals(previousDisplay, currentDisplay, StringComparison.Ordinal))
        {
            return;
        }

        changes.Add($"{fieldName}: {previousDisplay} -> {currentDisplay}");
    }

    /// <summary> Форматирует необязательный текст для описания изменений. </summary>
    private static string FormatNullableText(string? value) => string.IsNullOrWhiteSpace(value) ? "не указано" : value.Trim();

    /// <summary> Присваивает цену связанного обслуживания дефекту. </summary>
    internal static void ApplyPrice(
        VehicleDefectLinkedMaintenanceDto? maintenance,
        IReadOnlyDictionary<Guid, decimal> priceByMaintenanceId)
    {
        if (maintenance != null && priceByMaintenanceId.TryGetValue(maintenance.Id, out var price))
        {
            maintenance.Price = price;
        }
    }

    /// <summary> Возвращает текст типа обслуживания. </summary>
    internal static string GetMaintenanceTypeText(MaintenanceType type) => type switch
    {
        MaintenanceType.Maintenance => "Обслуживание",
        MaintenanceType.Repair => "Ремонт",
        MaintenanceType.Refill => "Заправка",
        MaintenanceType.Other => "Другое",
        _ => "Обслуживание"
    };

    /// <summary> Возвращает название типа обслуживания. </summary>
    internal static string GetMaintenanceTypeName(MaintenanceType type) => GetMaintenanceTypeText(type);

    /// <summary> Возвращает название статуса обслуживания. </summary>
    internal static string GetMaintenanceStatusName(MaintenanceStatus status) => status switch
    {
        MaintenanceStatus.Planned => "Запланировано",
        MaintenanceStatus.InProgress => "В работе",
        MaintenanceStatus.Completed => "Выполнено",
        MaintenanceStatus.Cancelled => "Отменено",
        _ => "Запланировано"
    };

    /// <summary> Выбирает отображаемую дату обслуживания по статусу. </summary>
    internal static DateTime GetMaintenanceDisplayDate(MaintenanceStatus status, DateTime date, DateTime? plannedDate, DateTime? completedDate)
    {
        return status == MaintenanceStatus.Completed
            ? completedDate ?? date
            : plannedDate ?? date;
    }

    /// <summary> Выбирает отображаемую дату обслуживания. </summary>
    internal static DateTime GetMaintenanceDisplayDate(OpenCRM.Domain.Maintenance maintenance)
    {
        return maintenance.Status == MaintenanceStatus.Completed
            ? maintenance.CompletedDate ?? maintenance.Date
            : maintenance.PlannedDate ?? maintenance.Date;
    }

    /// <summary> Обрезает строку до максимальной длины. </summary>
    internal static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    /// <summary> Снимок основных полей дефекта для фиксации изменений. </summary>
    internal sealed record VehicleDefectSnapshot(string? Title, string? Description, string SeverityCode, string StatusCode)
    {
        /// <summary> Создаёт снимок текущего состояния дефекта. </summary>
        public static VehicleDefectSnapshot From(VehicleDefect defect)
        {
            return new VehicleDefectSnapshot(defect.Title, defect.Description, defect.SeverityCode, defect.StatusCode);
        }
    }

    /// <summary> Проверяет, что модуль дефектов включён для организации. </summary>
    internal static async Task EnsureDefectsModuleEnabledAsync(IOrganizationSubscriptionService organizationSubscriptionService, string userId, Guid organizationId)
    {
        var hasDefectsModule = await organizationSubscriptionService.HasFeatureAsync(userId, organizationId, FeatureCodes.DefectsModule);
        if (!hasDefectsModule)
        {
            throw new InvalidOperationException("Раздел неисправностей недоступен в текущем тарифе рабочего пространства.");
        }

        await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.DefectsView);
    }

    /// <summary> Проверяет принадлежность техники организации. </summary>
    internal static async Task EnsureVehicleBelongsToOrganizationAsync(OpenCRMContext context, Guid organizationId, Guid vehicleId)
    {
        var exists = await context.Vehicles
            .AnyAsync(x => x.Id == vehicleId && x.OrganizationId == organizationId && !x.IsDeleted);

        if (!exists)
        {
            throw new InvalidOperationException("Техника для неисправности не найдена.");
        }
    }

    /// <summary> Строит подзаголовок техники из названия, года и клиента. </summary>
    internal static string? BuildVehicleSubtitle(string? name, int? yearIssue, string? organizationClientName)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(name))
        {
            parts.Add(name.Trim());
        }

        if (yearIssue.HasValue)
        {
            parts.Add($"{yearIssue.Value} г.");
        }

        if (!string.IsNullOrWhiteSpace(organizationClientName))
        {
            parts.Add(organizationClientName.Trim());
        }

        return parts.Count > 0 ? string.Join(" • ", parts) : null;
    }

    /// <summary> Нормализует обязательную строку или выбрасывает ошибку. </summary>
    internal static string NormalizeRequired(string? value, string errorMessage)
    {
        var normalized = NormalizeNullable(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new InvalidOperationException(errorMessage)
            : normalized;
    }

    /// <summary> Нормализует необязательную строку. </summary>
    internal static string? NormalizeNullable(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    /// <summary> Нормализует код статуса дефекта с запасным значением. </summary>
    internal static string NormalizeStatusCode(string? code, string fallback = VehicleDefectStatusCodes.New) =>
        string.IsNullOrWhiteSpace(code) ? fallback : VehicleDefectStatusCodes.Normalize(code);

    /// <summary> Нормализует необязательный код статуса. </summary>
    internal static string? NormalizeOptionalStatusCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : VehicleDefectStatusCodes.Normalize(code);

    /// <summary> Нормализует код критичности дефекта. </summary>
    internal static string NormalizeSeverityCode(string? code) => VehicleDefectSeverityCodes.Normalize(code);

    /// <summary> Нормализует параметр сортировки дефектов. </summary>
    internal static string NormalizeSort(string? sort)
    {
        return sort switch
        {
            "created_asc" => "created_asc",
            "updated_desc" => "updated_desc",
            "updated_asc" => "updated_asc",
            _ => "created_desc"
        };
    }

    /// <summary> Применяет сортировку к запросу дефектов. </summary>
    internal static IQueryable<VehicleDefect> ApplySort(IQueryable<VehicleDefect> query, string sort)
    {
        return sort switch
        {
            "created_asc" => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.UpdatedAt),
            "updated_desc" => query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.CreatedAt),
            "updated_asc" => query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.CreatedAt),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.UpdatedAt)
        };
    }
}
