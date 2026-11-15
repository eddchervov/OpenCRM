using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles;

/// <summary> Читает варианты выбора техники для организации. </summary>
public sealed class VehicleOptionQueryService(OpenCRMContext context) : IVehicleOptionQueryService
{
    /// <summary> Контекст базы данных для запросов. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Возвращает варианты техники по поиску и клиенту. </summary>
    public async Task<IReadOnlyList<VehicleOptionDto>> GetOptionsAsync(
        Guid organizationId,
        string? searchQuery,
        Guid? organizationClientId,
        int take)
    {
        var normalizedTake = Math.Clamp(take, 5, 30);
        var query = this._context.Vehicles
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && !x.IsDeleted && !x.IsArchived);

        if (organizationClientId.HasValue)
        {
            query = query.Where(x => x.OrganizationClientId == organizationClientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var searchTokens = searchQuery
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.ToLower())
                .Distinct()
                .Take(5)
                .ToArray();

            foreach (var search in searchTokens)
            {
                query = query.Where(x =>
                    (x.Name != null && x.Name.ToLower().Contains(search)) ||
                    (x.ManualBrandName != null && x.ManualBrandName.ToLower().Contains(search)) ||
                    (x.ManualModelName != null && x.ManualModelName.ToLower().Contains(search)) ||
                    (x.Brand != null && x.Brand.Name.ToLower().Contains(search)) ||
                    (x.Model != null && x.Model.Name.ToLower().Contains(search)) ||
                    (x.OrganizationClient != null && x.OrganizationClient.Name.ToLower().Contains(search)) ||
                    (x.RegistrationPlate != null && x.RegistrationPlate.ToLower().Contains(search)) ||
                    (x.Vin != null && x.Vin.ToLower().Contains(search)) ||
                    (x.BodyNumber != null && x.BodyNumber.ToLower().Contains(search)) ||
                    (x.EngineNumber != null && x.EngineNumber.ToLower().Contains(search)) ||
                    (x.RegistrationCertificate != null && x.RegistrationCertificate.ToLower().Contains(search)));
            }
        }

        return await query
            .Include(x => x.Brand)
            .Include(x => x.Model)
            .Include(x => x.OrganizationClient)
            .OrderByDescending(x => x.CurrentMileageUpdatedAt ?? x.DateTimeCreation)
            .ThenByDescending(x => x.DateTimeCreation)
            .Take(normalizedTake)
            .Select(x => new VehicleOptionDto
            {
                Id = x.Id,
                DisplayName = Helpers.VehicleDisplayName.Format(
                    x.BrandId.HasValue ? (x.Brand != null ? x.Brand.Name : string.Empty) : (x.ManualBrandName ?? string.Empty),
                    x.ModelId.HasValue ? (x.Model != null ? x.Model.Name : string.Empty) : (x.ManualModelName ?? string.Empty), x.RegistrationPlate),
                Subtitle = Helpers.VehicleDisplayName.FormatSubtitle(
                    x.Name,
                    x.YearIssue,
                    x.OrganizationClient != null ? x.OrganizationClient.Name : null,
                    x.RegistrationPlate,
                    !(x.BrandId.HasValue || x.ModelId.HasValue)),
                CurrentMileage = x.CurrentMileage,
                MileageUnit = x.MileageUnit == VehicleMileageUnit.EngineHours ? "м/ч" : "км",
                RegistrationPlate = x.RegistrationPlate
            })
            .ToListAsync();
    }
}
