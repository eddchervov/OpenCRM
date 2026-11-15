using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Clients.Rules;
using OpenCRM.Services.Interfaces.Organizations;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Clients.Queries;

/// <summary> Сервис запросов детальных данных клиента организации. </summary>
internal sealed class OrganizationClientDetailsQueryService(
    OpenCRMContext context,
    ClientsAccess access,
    IOrganizationClientQueryService queries,
    ClientVehiclePhotos photos,
    ClientHistoryReader history,
    IOrganizationPermissionService permissions) : IOrganizationClientDetailsQueryService
{
    /// <summary> Контекст базы данных для деталей клиента. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Зависимость проверки доступа к клиентам. </summary>
    private readonly ClientsAccess _access = access;

    /// <summary> Зависимость сервиса запросов клиентов. </summary>
    private readonly IOrganizationClientQueryService _queries = queries;

    /// <summary> Зависимость сервиса фотографий техники клиентов. </summary>
    private readonly ClientVehiclePhotos _photos = photos;

    /// <summary> Зависимость читателя истории клиентов. </summary>
    private readonly ClientHistoryReader _history = history;

    /// <summary> Зависимость проверки прав в организации. </summary>
    private readonly IOrganizationPermissionService _permissions = permissions;

    /// <summary> Возвращает детальные данные клиента организации. </summary>
    public async Task<OrganizationClientDetailsDto?> GetDetailsAsync(string userId, Guid organizationId, Guid clientId)
    {
        await this._access.EnsureEnabledAsync(userId, organizationId);

        var client = await this._queries.GetByIdAsync(userId, organizationId, clientId);
        if (client == null)
        {
            return null;
        }

        await this._photos.AttachAsync(organizationId, clientId, client.Vehicles);

        var canViewMaintenance = await this._permissions.HasPermissionAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceView);
        var canViewFinance = await this._permissions.HasPermissionAsync(
                userId, organizationId, OrganizationPermissionCodes.FinanceView)
            || await this._permissions.HasPermissionAsync(
                userId, organizationId, OrganizationPermissionCodes.FinanceManage);

        var vehicleIds = client.Vehicles.Select(x => x.Id).ToList();
        var openDefectsCount = vehicleIds.Count == 0
            ? 0
            : await this._context.VehicleDefects
                .AsNoTracking()
                .CountAsync(x => !x.IsDeleted
                    && !x.Vehicle.IsDeleted
                    && x.OrganizationId == organizationId
                    && x.Vehicle.OrganizationClientId == clientId
                    && x.StatusCode != VehicleDefectStatusCodes.Resolved);

        var closedDefectsCount = vehicleIds.Count == 0
            ? 0
            : await this._context.VehicleDefects
                .AsNoTracking()
                .CountAsync(x => !x.IsDeleted
                    && !x.Vehicle.IsDeleted
                    && x.OrganizationId == organizationId
                    && x.Vehicle.OrganizationClientId == clientId
                    && x.StatusCode == VehicleDefectStatusCodes.Resolved);

        var clientMaintenances = this._context.Maintenance
                .AsNoTracking()
                .Where(x => !x.IsDeleted
                    && !x.Vehicle.IsDeleted
                    && x.Vehicle.OrganizationId == organizationId
                    && x.BuyerClientId == clientId);

        var recentMaintenanceRows = await clientMaintenances
                .OrderByDescending(x => x.Status == MaintenanceStatus.Completed
                    ? (x.CompletedDate ?? x.Date)
                    : (x.PlannedDate ?? x.Date))
                .ThenByDescending(x => x.DateTimeCreation)
                .Take(8)
                .Include(x => x.Details)
                .Include(x => x.Vehicle)
                    .ThenInclude(vehicle => vehicle.Brand)
                .Include(x => x.Vehicle)
                    .ThenInclude(vehicle => vehicle.Model)
                .ToListAsync();

        var clientPayments = this._context.MaintenancePayments.AsNoTracking()
            .Where(payment => !payment.CancelledAt.HasValue
                && !payment.Maintenance.IsDeleted
                && !payment.Maintenance.Vehicle.IsDeleted
                && payment.Maintenance.Vehicle.OrganizationId == organizationId
                && payment.Maintenance.BuyerClientId == clientId);
        var recentMaintenanceIds = recentMaintenanceRows.Select(maintenance => maintenance.Id).ToArray();
        var paymentTotals = canViewFinance && recentMaintenanceIds.Length > 0
            ? await clientPayments
                .Where(payment => recentMaintenanceIds.Contains(payment.MaintenanceId))
                .GroupBy(payment => payment.MaintenanceId)
                .Select(group => new { MaintenanceId = group.Key, Amount = group.Sum(payment => payment.Amount) })
                .ToDictionaryAsync(item => item.MaintenanceId, item => item.Amount)
            : new Dictionary<Guid, decimal>();

        var recentMaintenances = recentMaintenanceRows
            .Select(maintenance => ClientMaintenanceMapper.Map(
                maintenance,
                canViewFinance ? paymentTotals.GetValueOrDefault(maintenance.Id) : null,
                canViewFinance,
                canViewMaintenance))
            .ToList();

        var now = DateTime.UtcNow;
        MaintenanceEntity? upcomingMaintenanceRow = null;
        if (canViewMaintenance)
        {
            var timeZoneId = await this._context.Organizations
                .AsNoTracking()
                .Where(organization => organization.Id == organizationId)
                .Select(organization => organization.ShiftTimeZoneId)
                .SingleAsync();
            var timeZone = !string.IsNullOrWhiteSpace(timeZoneId)
                && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var organizationTimeZone)
                    ? organizationTimeZone
                    : TimeZoneInfo.Utc;

            var today = DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(now, timeZone).Date,
                DateTimeKind.Utc);

            var localTime = TimeZoneInfo.ConvertTimeFromUtc(now, timeZone).TimeOfDay;
            upcomingMaintenanceRow = await clientMaintenances
                .Where(maintenance => maintenance.Status == MaintenanceStatus.Planned
                    && (maintenance.PlannedDate ?? maintenance.Date).Date >= today
                    && ((maintenance.PlannedDate ?? maintenance.Date).Date > today
                        || !maintenance.PlannedStartTime.HasValue
                        || maintenance.PlannedStartTime >= localTime))
                .OrderBy(maintenance => maintenance.PlannedDate ?? maintenance.Date)
                .ThenBy(maintenance => maintenance.PlannedStartTime)
                .Include(maintenance => maintenance.Details)
                .Include(maintenance => maintenance.Vehicle)
                    .ThenInclude(vehicle => vehicle.Brand)
                .Include(maintenance => maintenance.Vehicle)
                    .ThenInclude(vehicle => vehicle.Model)
                .FirstOrDefaultAsync();
        }

        var totalPaidAmount = canViewFinance
            ? Math.Round(await clientPayments.SumAsync(payment => payment.Amount), 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        decimal? upcomingPaidAmount = null;
        if (canViewFinance && upcomingMaintenanceRow != null)
        {
            upcomingPaidAmount = paymentTotals.TryGetValue(upcomingMaintenanceRow.Id, out var paidAmount)
                ? paidAmount
                : await clientPayments.Where(payment => payment.MaintenanceId == upcomingMaintenanceRow.Id)
                    .SumAsync(payment => payment.Amount);
        }

        var defects = vehicleIds.Count == 0
            ? new List<OrganizationClientDefectItemDto>()
            : await this._context.VehicleDefects
                .AsNoTracking()
                .Where(x => !x.IsDeleted
                    && !x.Vehicle.IsDeleted
                    && x.OrganizationId == organizationId
                    && x.Vehicle.OrganizationClientId == clientId)
                .OrderBy(x => x.StatusCode == VehicleDefectStatusCodes.Resolved)
                .ThenByDescending(x => x.CreatedAt)
                .Take(10)
                .Select(x => new OrganizationClientDefectItemDto
                {
                    Id = x.Id,
                    VehicleId = x.VehicleId,
                    VehicleDisplayName = ClientRules.BuildVehicleDisplayName(
                        x.Vehicle.BrandId.HasValue ? (x.Vehicle.Brand != null ? x.Vehicle.Brand.Name : string.Empty) : (x.Vehicle.ManualBrandName ?? string.Empty),
                        x.Vehicle.ModelId.HasValue ? (x.Vehicle.Model != null ? x.Vehicle.Model.Name : string.Empty) : (x.Vehicle.ManualModelName ?? string.Empty),
                        x.Vehicle.YearIssue,
                        x.Vehicle.Name,
                        x.Vehicle.RegistrationPlate),
                    Title = x.Title,
                    SeverityCode = x.SeverityCode,
                    SeverityName = VehicleDefectSeverityCodes.GetName(x.SeverityCode),
                    StatusCode = x.StatusCode,
                    StatusName = VehicleDefectStatusCodes.GetName(x.StatusCode),
                    CreatedAt = x.CreatedAt,
                    ClosedAt = x.ClosedAt,
                    DetailUrl = $"#defects/{x.Id}",
                    VehicleUrl = $"#vehicles/{x.VehicleId}"
                })
                .ToListAsync();

        return new OrganizationClientDetailsDto
        {
            Client = client,
            RecentMaintenances = recentMaintenances,
            CanViewMaintenance = canViewMaintenance,
            CanEdit = await this._permissions.HasPermissionAsync(
                userId, organizationId, OrganizationPermissionCodes.ClientsEdit),
            TotalPaidAmount = totalPaidAmount,
            UpcomingMaintenance = !canViewMaintenance || upcomingMaintenanceRow == null
                ? null
                : ClientMaintenanceMapper.Map(
                    upcomingMaintenanceRow,
                    upcomingPaidAmount,
                    canViewFinance,
                    canViewMaintenance),
            Defects = defects,
            HistoryItems = await this._history.LoadAsync(organizationId, clientId, recentMaintenances, defects),
            OpenDefectsCount = openDefectsCount,
            ClosedDefectsCount = closedDefectsCount
        };
    }
}
