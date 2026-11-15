using OpenCRM.Modules.Organizations.Contracts.Dtos;
using Helpers;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Documents;

/// <summary> Вспомогательные факты для документов обслуживания. </summary>
internal static class MaintenanceDocumentFacts
{
    /// <summary> Определяет дату акта выполненных работ. </summary>
    public static DateTime GetWorkActDate(GetMaintenanceByIdDto maintenance) =>
        maintenance.CompletedDate ?? maintenance.Date;

    /// <summary> Определяет дату заказа-наряда. </summary>
    public static DateTime GetWorkOrderDate(GetMaintenanceByIdDto maintenance) =>
        maintenance.PlannedDate ?? maintenance.Date;

    /// <summary> Определяет дату документа сценария обслуживания. </summary>
    public static DateTime GetScenarioDocumentDate(GetMaintenanceByIdDto maintenance) =>
        maintenance.CompletedDate ?? maintenance.PlannedDate ?? maintenance.Date;

    /// <summary> Формирует строку запланированного периода обслуживания. </summary>
    public static string? FormatPlannedPeriod(GetMaintenanceByIdDto maintenance)
    {
        if (!maintenance.PlannedDate.HasValue)
        {
            return null;
        }

        var start = maintenance.PlannedDate.Value.Date.Add(maintenance.PlannedStartTime ?? TimeSpan.Zero);
        var end = (maintenance.PlannedEndDate ?? maintenance.PlannedDate.Value).Date.Add(maintenance.PlannedEndTime ?? TimeSpan.Zero);
        if (!maintenance.PlannedStartTime.HasValue && !maintenance.PlannedEndTime.HasValue)
        {
            return start.ToString("dd.MM.yyyy");
        }

        return $"{start:dd.MM.yyyy HH:mm} — {end:dd.MM.yyyy HH:mm}";
    }

    /// <summary> Строит название техники для документа. </summary>
    public static string GetVehicleTitle(string? brandName, string? modelName, string? name, string? registrationPlate)
    {
        var title = VehicleDisplayName.Format(brandName, modelName, registrationPlate);

        return string.IsNullOrWhiteSpace(name) ? title : $"{title} · {name.Trim()}";
    }

    /// <summary> Возвращает подпись единицы пробега. </summary>
    public static string GetMileageLabel(VehicleMileageUnit mileageUnit) =>
        mileageUnit == VehicleMileageUnit.EngineHours ? "Моточасы" : "Пробег";

    /// <summary> Возвращает единицу пробега. </summary>
    public static string GetMileageUnit(VehicleMileageUnit mileageUnit) =>
        mileageUnit == VehicleMileageUnit.EngineHours ? "м/ч" : "км";

    /// <summary> Возвращает имя исполнителя из данных организации. </summary>
    public static string GetExecutorName(OrganizationDto organization) =>
        !string.IsNullOrWhiteSpace(organization.LegalName) ? organization.LegalName : organization.Name;

    /// <summary> Строит номер заказа-наряда. </summary>
    public static string BuildWorkOrderNumber(Guid maintenanceId, DateTime orderDate) =>
        $"{orderDate:yyyyMMdd}-{maintenanceId.ToString("N")[..8].ToUpperInvariant()}";

    /// <summary> Строит номер документа сценария. </summary>
    public static string BuildScenarioDocumentNumber(Guid maintenanceId, DateTime documentDate) =>
        $"{documentDate:yyyyMMdd}-{maintenanceId.ToString("N")[..6].ToUpperInvariant()}";

    /// <summary> Проверяет, нужно ли включать фотографии в документ вида. </summary>
    public static bool ShouldIncludePhotos(string documentKindCode) =>
        documentKindCode is OrganizationScenarioDocumentKindCodes.PhotoReport
            or OrganizationScenarioDocumentKindCodes.InspectionAct;

    /// <summary> Возвращает текстовое описание типа обслуживания. </summary>
    public static string GetMaintenanceTypeText(int type) => type switch
    {
        1 => "Ремонт и обслуживание",
        2 => "Плановое ТО",
        3 => "Заправка топливом",
        100 => "Другое",
        _ => "Обслуживание"
    };

    /// <summary> Формирует описание экспорта документа для истории. </summary>
    public static string BuildDocumentHistoryDescription(string format, bool includePhotos) =>
        $"Формат: {format}\nФотографии: {(includePhotos ? "включены" : "не включены")}";

    /// <summary> Очищает строку для использования в имени файла. </summary>
    public static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Select(character => invalidChars.Contains(character) || char.IsWhiteSpace(character) ? '_' : character).ToArray();
        var fileName = new string(chars).Trim('_');

        return string.IsNullOrWhiteSpace(fileName) ? "document" : fileName;
    }
}
