using Helpers;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Queries;

/// <summary> Маппер данных обслуживания рабочего пространства. </summary>
internal static class OrganizationWorkspaceMaintenanceMapper
{
    /// <summary> Преобразует сущность обслуживания в DTO недавнего обслуживания. </summary>
    internal static OrganizationRecentMaintenanceDto Map(MaintenanceEntity maintenance, DateTime? displayDate = null) =>
        new()
        {
            Id = maintenance.Id,
            Version = maintenance.Version,
            VehicleId = maintenance.VehicleId,
            VehicleDisplayName = OrganizationWorkspaceMaintenanceMapper.GetVehicleDisplayName(maintenance.Vehicle),
            VehicleSubtitle = OrganizationWorkspaceMaintenanceMapper.BuildVehicleSubtitle(
                maintenance.Vehicle.Name,
                maintenance.Vehicle.YearIssue,
                maintenance.Vehicle.OrganizationClient != null ? maintenance.Vehicle.OrganizationClient.Name : null,
                maintenance.Vehicle.RegistrationPlate,
                !(maintenance.Vehicle.BrandId.HasValue || maintenance.Vehicle.ModelId.HasValue)),
            RegistrationPlate = maintenance.Vehicle.RegistrationPlate,
            BrandLogoKey = maintenance.Vehicle.BrandId.HasValue ? maintenance.Vehicle.Brand?.LogoKey : null,
            ServiceNames = maintenance.Details
                .Where(detail => !OrganizationWorkspaceMaintenanceMapper.IsMaterial(detail) && !string.IsNullOrWhiteSpace(detail.Name))
                .Select(detail => detail.Name)
                .ToArray(),
            EmployeeNames = maintenance.Details
                .Where(detail => !OrganizationWorkspaceMaintenanceMapper.IsMaterial(detail) && !string.IsNullOrWhiteSpace(detail.EmployeeName ?? maintenance.Performer))
                .Select(detail => (detail.EmployeeName ?? maintenance.Performer)!)
                .Distinct()
                .ToArray(),
            ReadyForHandoverAt = maintenance.ReadyForHandoverAt,
            HandedOverAt = maintenance.HandedOverAt,
            TypeName = OrganizationWorkspaceMaintenanceMapper.GetMaintenanceTypeName(maintenance.Type),
            Status = maintenance.Status,
            StatusName = maintenance.HandedOverAt.HasValue
                ? "Выдан"
                : maintenance.ReadyForHandoverAt.HasValue
                    ? "Готов к выдаче"
                    : OrganizationWorkspaceMaintenanceMapper.GetMaintenanceStatusName(maintenance.Status),
            IsPlannedDate = maintenance.PlannedDate.HasValue,
            Date = displayDate ?? maintenance.PlannedDate ?? maintenance.Date,
            PlannedStartTime = maintenance.PlannedStartTime,
            PlannedEndDate = maintenance.PlannedEndDate,
            PlannedEndTime = maintenance.PlannedEndTime,
            Location = maintenance.Location,
            Mileage = maintenance.Mileage,
            MileageUnit = maintenance.Vehicle.MileageUnit,
            Comment = maintenance.Comment,
            CurrencyCode = maintenance.CurrencyCode,
            Price = Math.Max(0m, Math.Round(
                maintenance.Details.Sum(detail => (decimal)(detail.Price ?? 0)),
                2,
                MidpointRounding.AwayFromZero) - maintenance.DiscountAmount),
            DetailUrl = $"#maintenance/{maintenance.Id}"
        };

    /// <summary> Определяет, является ли строка заказа материалом. </summary>
    private static bool IsMaterial(MaintenanceDetail detail) =>
        detail.MaterialCatalogItemId.HasValue || detail.MaterialName != null || detail.MaterialQuantity.HasValue;

    /// <summary> Определяет отображаемое название транспортного средства. </summary>
    internal static string GetVehicleDisplayName(Vehicle vehicle) =>
        VehicleDisplayName.Format(
            vehicle.BrandId.HasValue
                ? (vehicle.Brand != null ? vehicle.Brand.Name : string.Empty)
                : (vehicle.ManualBrandName ?? string.Empty),
            vehicle.ModelId.HasValue
                ? (vehicle.Model != null ? vehicle.Model.Name : string.Empty)
                : (vehicle.ManualModelName ?? string.Empty),
            vehicle.RegistrationPlate);

    /// <summary> Формирует дополнительное описание транспортного средства из доступных данных. </summary>
    internal static string? BuildVehicleSubtitle(
        string? name,
        int? yearIssue,
        string? clientName,
        string? registrationPlate = null,
        bool includeVehicleName = true) =>
        VehicleDisplayName.FormatSubtitle(name, yearIssue, clientName, registrationPlate, includeVehicleName);

    /// <summary> Определяет отображаемое название типа обслуживания. </summary>
    internal static string GetMaintenanceTypeName(MaintenanceType type, string? scenarioCode = null)
    {
        if (OrganizationWorkspaceMaintenanceMapper.IsDetailingScenario(scenarioCode))
        {
            return type switch
            {
                MaintenanceType.Maintenance => "Уход / мойка",
                MaintenanceType.Repair => "Ремонт",
                MaintenanceType.Refill => "Заправка",
                _ => "Другое"
            };
        }

        return type switch
        {
            MaintenanceType.Maintenance => "Ремонт и обслуживание",
            MaintenanceType.Repair => "Плановое ТО",
            MaintenanceType.Refill => "Заправка топливом",
            _ => "Другое"
        };
    }

    /// <summary> Определяет отображаемое название статуса обслуживания. </summary>
    internal static string GetMaintenanceStatusName(MaintenanceStatus status) => status switch
    {
        MaintenanceStatus.Planned => "Запланировано",
        MaintenanceStatus.InProgress => "В работе",
        MaintenanceStatus.Completed => "Выполнено",
        MaintenanceStatus.Cancelled => "Отменено",
        _ => "Неизвестно"
    };

    /// <summary> Проверяет, что код сценария относится к детейлингу. </summary>
    private static bool IsDetailingScenario(string? scenarioCode)
    {
        var normalized = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(scenarioCode);

        return normalized == OrganizationTypeCodes.Detailing;
    }
}
