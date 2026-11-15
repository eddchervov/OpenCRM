using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Workday;

/// <summary> Сервис данных рабочего дня организации. </summary>
public sealed class OrganizationWorkdayQueryService(OpenCRMContext opencrmContext) : IOrganizationWorkdayQueryService
{
    /// <summary> Возвращает данные рабочего дня организации. </summary>
    public Task<OrganizationWorkdayDto> GetWorkdayAsync(Guid organizationId, DateTime date, bool canViewClients, TimeZoneInfo? timeZone = null)
    {
        return this.LoadWorkdayAsync(organizationId, date, canViewClients, timeZone, false);
    }

    /// <summary> Возвращает визуализацию рабочего дня организации. </summary>
    public Task<OrganizationWorkdayDto> GetVisualizationAsync(
        Guid organizationId,
        DateTime date,
        bool canViewClients,
        TimeZoneInfo? timeZone = null)
    {
        return this.LoadWorkdayAsync(organizationId, date, canViewClients, timeZone, true);
    }

    /// <summary> Загружает данные рабочего дня организации. </summary>
    private async Task<OrganizationWorkdayDto> LoadWorkdayAsync(
        Guid organizationId,
        DateTime date,
        bool canViewClients,
        TimeZoneInfo? timeZone,
        bool isVisualization)
    {
        var from = date.Date;
        var to = from.AddDays(1);
        var zone = timeZone ?? TimeZoneInfo.Utc;
        var issuedFrom = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(from, DateTimeKind.Unspecified), zone);
        var issuedTo = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(to, DateTimeKind.Unspecified), zone);
        var query = opencrmContext.Maintenance
            .AsNoTracking()
            .Where(item => !item.IsDeleted && !item.Vehicle.IsDeleted && item.Vehicle.OrganizationId == organizationId);
        var planned = query.Where(item => item.Status == MaintenanceStatus.Planned && (item.PlannedDate ?? item.Date) < to);
        var inProgress = query.Where(item => item.Status == MaintenanceStatus.InProgress);
        var ready = query.Where(item => item.Status == MaintenanceStatus.Completed
            && item.ReadyForHandoverAt != null && item.HandedOverAt == null);
        var handedOver = query.Where(item => item.Status == MaintenanceStatus.Completed
            && item.HandedOverAt >= issuedFrom && item.HandedOverAt < issuedTo);

        return new OrganizationWorkdayDto
        {
            HasAnyWork = await query.AnyAsync(),
            Planned = await this.LoadWorkdayGroupAsync(planned, canViewClients, isVisualization),
            InProgress = await this.LoadWorkdayGroupAsync(inProgress, canViewClients, isVisualization),
            Ready = await this.LoadWorkdayGroupAsync(ready, canViewClients, isVisualization),
            HandedOver = isVisualization
                ? new OrganizationWorkdayGroupDto()
                : await this.LoadWorkdayGroupAsync(handedOver, canViewClients, false)
        };
    }

    /// <summary> Загружает группу обслуживаний рабочего дня из запроса. </summary>
    private async Task<OrganizationWorkdayGroupDto> LoadWorkdayGroupAsync(
        IQueryable<MaintenanceEntity> query,
        bool canViewClients,
        bool shouldIncludeAll)
    {
        var totalCount = shouldIncludeAll ? 0 : await query.CountAsync();
        var ordered = query
            .OrderByDescending(item => item.Status == MaintenanceStatus.Completed
                ? item.HandedOverAt ?? item.ReadyForHandoverAt ?? item.CompletedDate ?? item.Date
                : item.PlannedDate ?? item.Date)
            .ThenByDescending(item => item.PlannedStartTime ?? TimeSpan.Zero)
            .ThenBy(item => item.Id);

        var selected = shouldIncludeAll ? ordered : ordered.Take(50);

        var rows = await selected
            .Select(item => new
            {
                item.Id,
                Brand = item.Vehicle.Brand != null ? item.Vehicle.Brand.Name : item.Vehicle.ManualBrandName,
                VehicleBrandLogoKey = item.Vehicle.Brand != null ? item.Vehicle.Brand.LogoKey : null,
                Model = item.Vehicle.Model != null ? item.Vehicle.Model.Name : item.Vehicle.ManualModelName,
                item.Vehicle.Name,
                item.Vehicle.RegistrationPlate,
                ClientName = canViewClients && item.Vehicle.OrganizationClient != null && !item.Vehicle.OrganizationClient.IsDeleted ? item.Vehicle.OrganizationClient.Name : null,
                ClientPhone = canViewClients && item.Vehicle.OrganizationClient != null && !item.Vehicle.OrganizationClient.IsDeleted ? item.Vehicle.OrganizationClient.Phone : null,
                Date = item.Status == MaintenanceStatus.Completed ? item.CompletedDate ?? item.Date : item.PlannedDate ?? item.Date,
                item.ReadyForHandoverAt,
                item.HandedOverAt,
                item.PlannedStartTime,
                item.PlannedEndDate,
                item.PlannedEndTime,
                item.Location,
                item.Performer,
                item.EmployeeId
            })
            .ToListAsync();

        var maintenanceIds = rows.Select(item => item.Id).ToArray();

        var details = await opencrmContext.MaintenanceDetails.AsNoTracking()
            .Where(detail => maintenanceIds.Contains(detail.MaintenanceId))
            .OrderBy(detail => detail.Position)
            .ToListAsync();

        var detailsByMaintenance = details.ToLookup(detail => detail.MaintenanceId);

        var operationStatuses = await opencrmContext.MaintenanceWorkOperations.AsNoTracking()
            .Where(operation => maintenanceIds.Contains(operation.MaintenanceId))
            .Select(operation => new { operation.MaintenanceId, operation.Id, operation.Status })
            .ToListAsync();
        var statusesByMaintenance = operationStatuses.ToLookup(operation => operation.MaintenanceId);

        return new OrganizationWorkdayGroupDto
        {
            TotalCount = shouldIncludeAll ? rows.Count : totalCount,
            Items = rows.Select(item => new OrganizationWorkdayItemDto
            {
                Id = item.Id,
                VehicleName = Helpers.VehicleDisplayName.Format(item.Brand, item.Model, item.RegistrationPlate),
                VehicleBrandLogoKey = item.VehicleBrandLogoKey,
                RegistrationPlate = item.RegistrationPlate,
                ClientName = item.ClientName,
                ClientPhone = item.ClientPhone,
                Services = detailsByMaintenance[item.Id]
                    .Where(detail => detail.MaterialCatalogItemId == null && string.IsNullOrWhiteSpace(detail.MaterialName))
                    .Select(detail => detail.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct()
                    .ToArray(),
                WorkTotalCount = detailsByMaintenance[item.Id].Count(detail =>
                    detail.MaterialCatalogItemId == null && string.IsNullOrWhiteSpace(detail.MaterialName)),
                HasTrackedWork = statusesByMaintenance[item.Id].Any()
                    || detailsByMaintenance[item.Id].Any(detail => detail.MaterialCatalogItemId == null
                        && string.IsNullOrWhiteSpace(detail.MaterialName)
                        && (detail.EmployeeId.HasValue || item.EmployeeId.HasValue)),
                WorkCompletedCount = statusesByMaintenance[item.Id].Count(operation =>
                    operation.Status == MaintenanceWorkStatus.Completed),
                WorkPausedCount = statusesByMaintenance[item.Id].Count(operation =>
                    operation.Status == MaintenanceWorkStatus.Paused),
                Date = item.Date,
                ReadyForHandoverAt = item.ReadyForHandoverAt,
                HandedOverAt = item.HandedOverAt,
                StartTime = item.PlannedStartTime,
                EndDate = item.PlannedEndDate,
                EndTime = item.PlannedEndTime,
                Location = item.Location,
                Performer = item.Performer
            }).ToList()
        };
    }
}
