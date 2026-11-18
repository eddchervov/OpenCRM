using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Clients.Rules;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Clients.Queries;

/// <summary> Общая проекция обслуживания для карточки и истории клиента. </summary>
internal static class ClientMaintenanceMapper
{
    internal static OrganizationClientMaintenanceItemDto Map(
        MaintenanceEntity maintenance,
        decimal? paidAmount,
        bool canViewFinance,
        bool canViewMaintenance)
    {
        decimal? grossAmount = canViewFinance
            ? Math.Round(maintenance.Details.Sum(detail => (decimal)(detail.Price ?? 0)), 2, MidpointRounding.AwayFromZero)
            : null;

        return new OrganizationClientMaintenanceItemDto
        {
            Id = maintenance.Id,
            VehicleId = maintenance.VehicleId,
            VehicleDisplayName = ClientRules.BuildVehicleDisplayName(
                maintenance.Vehicle.BrandId.HasValue
                    ? (maintenance.Vehicle.Brand != null ? maintenance.Vehicle.Brand.Name : string.Empty)
                    : (maintenance.Vehicle.ManualBrandName ?? string.Empty),
                maintenance.Vehicle.ModelId.HasValue
                    ? (maintenance.Vehicle.Model != null ? maintenance.Vehicle.Model.Name : string.Empty)
                    : (maintenance.Vehicle.ManualModelName ?? string.Empty),
                maintenance.Vehicle.YearIssue,
                maintenance.Vehicle.Name,
                maintenance.Vehicle.RegistrationPlate),
            ServiceNames = canViewMaintenance
                ? maintenance.Details
                    .Where(detail => !ClientMaintenanceMapper.IsMaterial(detail)
                        && !string.IsNullOrWhiteSpace(detail.Name))
                    .Select(detail => detail.Name)
                    .ToArray()
                : [],
            GrossAmount = grossAmount,
            CurrencyCode = maintenance.CurrencyCode,
            NetAmount = canViewFinance && maintenance.Status == MaintenanceStatus.Cancelled
                ? 0m
                : grossAmount.HasValue
                ? Math.Max(0m, grossAmount.Value - maintenance.DiscountAmount)
                : null,
            PaidAmount = canViewFinance ? paidAmount : null,
            ReadyForHandoverAt = maintenance.ReadyForHandoverAt,
            HandedOverAt = maintenance.HandedOverAt,
            Type = maintenance.Type,
            Status = maintenance.Status,
            Date = maintenance.Date,
            PlannedDate = maintenance.PlannedDate,
            CompletedDate = maintenance.CompletedDate,
            PlannedStartTime = maintenance.PlannedStartTime,
            PlannedEndTime = maintenance.PlannedEndTime,
            PlannedEndDate = maintenance.PlannedEndDate,
            Mileage = maintenance.Mileage,
            Comment = maintenance.Comment,
            DetailUrl = $"#maintenance/{maintenance.Id}",
            VehicleUrl = $"#vehicles/{maintenance.VehicleId}"
        };
    }

    private static bool IsMaterial(MaintenanceDetail detail) =>
        detail.MaterialCatalogItemId.HasValue || detail.MaterialName != null || detail.MaterialQuantity.HasValue;
}
