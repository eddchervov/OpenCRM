using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles;

/// <summary> Читает сводные данные техники по клиентам организации. </summary>
public sealed class VehicleClientSummaryQueryService(OpenCRMContext context) : IVehicleClientSummaryQueryService
{
    /// <summary> Контекст базы данных для запросов. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Возвращает сводные данные техники по списку клиентов. </summary>
    public async Task<IReadOnlyList<VehicleClientSummaryDto>> GetByClientIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> clientIds,
        bool sortByName)
    {
        if (clientIds.Count == 0)
        {
            return [];
        }

        // Агрегаты обслуживания техники читаются вместе с техникой одним SQL, независимо от покупателя обслуживания.
        var query = this._context.Vehicles
            .AsNoTracking()
            .Where(vehicle => !vehicle.IsDeleted
                && vehicle.OrganizationId == organizationId
                && vehicle.OrganizationClientId.HasValue
                && clientIds.Contains(vehicle.OrganizationClientId.Value))
            .Select(vehicle => new VehicleClientSummaryDto
            {
                ClientId = vehicle.OrganizationClientId!.Value,
                Id = vehicle.Id,
                YearIssue = vehicle.YearIssue,
                Name = vehicle.Name,
                RegistrationPlate = vehicle.RegistrationPlate,
                BrandName = vehicle.BrandId.HasValue
                    ? (vehicle.Brand != null ? vehicle.Brand.Name : string.Empty)
                    : (vehicle.ManualBrandName ?? string.Empty),
                BrandLogoKey = vehicle.BrandId.HasValue && vehicle.Brand != null ? vehicle.Brand.LogoKey : null,
                ModelName = vehicle.ModelId.HasValue
                    ? (vehicle.Model != null ? vehicle.Model.Name : string.Empty)
                    : (vehicle.ManualModelName ?? string.Empty),
                MaintenanceCount = vehicle.Maintenance.Count(maintenance => !maintenance.IsDeleted),
                LastMaintenanceDate = vehicle.Maintenance
                    .Where(maintenance => !maintenance.IsDeleted && maintenance.Status == MaintenanceStatus.Completed)
                    .Select(maintenance => maintenance.CompletedDate ?? (DateTime?)maintenance.Date)
                    .Max()
            });

        var sortedQuery = query.OrderBy(vehicle => vehicle.BrandName).ThenBy(vehicle => vehicle.ModelName);

        return await (sortByName ? sortedQuery.ThenBy(vehicle => vehicle.Name) : sortedQuery).ToListAsync();
    }
}
