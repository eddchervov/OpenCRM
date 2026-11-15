using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Workspace.Queries;

/// <summary> Сервис сводки планирования рабочего пространства. </summary>
public sealed class OrganizationWorkspacePlanningQueryService(
    OpenCRMContext opencrmContext) : IOrganizationWorkspacePlanningQueryService
{
    /// <summary> Возвращает сводку планирования организации с учётом доступных модулей. </summary>
    public async Task<OrganizationWorkspacePlanningSummary> GetPlanningSummaryAsync(
        Guid organizationId,
        bool hasDefectsModule,
        bool hasCountersModule,
        bool hasSchedulerModule,
        DateTime today,
        TimeZoneInfo? timeZone = null)
    {
        if (!hasSchedulerModule)
        {
            return new OrganizationWorkspacePlanningSummary();
        }

        var scenarioCode = await opencrmContext.Organizations
            .AsNoTracking()
            .Where(x => x.Id == organizationId)
            .Select(x => x.PrimaryScenarioCode)
            .FirstOrDefaultAsync();
        var process = OrganizationScenarioProcessCatalog.Resolve(scenarioCode);
        var planningPolicy = OrganizationPlanningPolicyFactory.Build(process, await this.LoadPlanningRulesAsync(organizationId));
        var organizationTimeZone = timeZone ?? TimeZoneInfo.Utc;

        var vehicles = await opencrmContext.Vehicles
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && !x.IsDeleted && !x.IsArchived)
            .Select(x => new WorkspaceVehiclePlannerRow
            {
                Id = x.Id,
                MileageUnit = x.MileageUnit,
                CurrentMileage = x.CurrentMileage,
                CurrentMileageUpdatedAt = x.CurrentMileageUpdatedAt,
                CreatedAt = x.DateTimeCreation
            })
            .ToListAsync();

        var vehicleIds = vehicles.Select(x => x.Id).ToList();
        if (vehicleIds.Count == 0)
        {
            return new OrganizationWorkspacePlanningSummary();
        }

        var maintenances = await opencrmContext.Maintenance
            .AsNoTracking()
            .Where(x => vehicleIds.Contains(x.VehicleId) && !x.IsDeleted)
            .Select(x => new WorkspaceMaintenancePlannerRow
            {
                VehicleId = x.VehicleId,
                Date = x.Date,
                Mileage = x.Mileage
            })
            .ToListAsync();

        var defectsCount = hasDefectsModule && planningPolicy.EnableDefectPlanning
            ? await opencrmContext.VehicleDefects
                .AsNoTracking()
                .CountAsync(x => x.OrganizationId == organizationId
                    && !x.IsDeleted
                    && x.StatusCode != VehicleDefectStatusCodes.Resolved)
            : 0;

        var maintenanceLookup = maintenances
            .GroupBy(x => x.VehicleId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(item => item.Date).ToList());

        var overdueCount = defectsCount;
        var soonCount = 0;
        var attentionCount = 0;
        var firstServiceCount = 0;
        var counterUpdateCount = 0;

        foreach (var vehicle in vehicles)
        {
            maintenanceLookup.TryGetValue(vehicle.Id, out var vehicleMaintenances);
            var lastMaintenance = vehicleMaintenances?.FirstOrDefault();

            if (lastMaintenance == null)
            {
                if (planningPolicy.EnableFirstService
                    && ToOrganizationDate(vehicle.CreatedAt, organizationTimeZone) <= today.AddDays(-planningPolicy.FirstServiceGraceDays))
                {
                    attentionCount++;
                    firstServiceCount++;
                }
            }
            else
            {
                if (planningPolicy.EnableDatePlanning)
                {
                    var dueDate = lastMaintenance.Date.Date.AddDays(planningPolicy.PlannedServiceIntervalDays);
                    if (dueDate <= today.AddDays(planningPolicy.SoonServiceWindowDays))
                    {
                        if (dueDate <= today)
                        {
                            overdueCount++;
                        }
                        else
                        {
                            soonCount++;
                        }
                    }
                }

                if (planningPolicy.EnableMileagePlanning && vehicle.CurrentMileage.HasValue && lastMaintenance.Mileage.HasValue)
                {
                    var interval = planningPolicy.GetMileageInterval(vehicle.MileageUnit);
                    var soonThreshold = planningPolicy.GetMileageSoonThreshold(vehicle.MileageUnit);
                    var dueMileage = lastMaintenance.Mileage.Value + interval;
                    var remaining = dueMileage - vehicle.CurrentMileage.Value;

                    if (remaining <= soonThreshold)
                    {
                        if (remaining <= 0)
                        {
                            overdueCount++;
                        }
                        else
                        {
                            soonCount++;
                        }
                    }
                }
            }

            if (hasCountersModule
                && planningPolicy.EnableCounterFreshness
                && vehicle.CurrentMileageUpdatedAt.HasValue
                && ToOrganizationDate(vehicle.CurrentMileageUpdatedAt.Value, organizationTimeZone)
                    <= today.AddDays(-planningPolicy.CounterStaleWindowDays))
            {
                attentionCount++;
                counterUpdateCount++;
            }
        }

        return new OrganizationWorkspacePlanningSummary
        {
            OverdueCount = overdueCount,
            SoonCount = soonCount,
            AttentionCount = attentionCount,
            DefectsCount = defectsCount,
            FirstServiceCount = firstServiceCount,
            CounterUpdateCount = counterUpdateCount
        };
    }

    private static DateTime ToOrganizationDate(DateTime utcDateTime, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), timeZone).Date;

    /// <summary> Загружает снимок правил планирования организации. </summary>
    private async Task<IReadOnlyList<OrganizationPlanningRuleSnapshot>> LoadPlanningRulesAsync(Guid organizationId)
    {
        return await opencrmContext.OrganizationScenarioCatalogItems
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.KindCode == OrganizationScenarioCatalogKindCodes.PlanningRule)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new OrganizationPlanningRuleSnapshot(x.Code, x.Name, x.Description, x.IsActive))
            .ToListAsync();
    }

    /// <summary> Строка транспортного средства для планирования. </summary>
    private sealed class WorkspaceVehiclePlannerRow
    {
        /// <summary> Идентификатор транспортного средства. </summary>
        public Guid Id { get; init; }

        /// <summary> Единица измерения пробега техники. </summary>
        public VehicleMileageUnit MileageUnit { get; init; }

        /// <summary> Текущий пробег. </summary>
        public int? CurrentMileage { get; init; }

        /// <summary> Время последнего обновления пробега. </summary>
        public DateTime? CurrentMileageUpdatedAt { get; init; }

        /// <summary> Время создания записи. </summary>
        public DateTime CreatedAt { get; init; }
    }

    /// <summary> Строка обслуживания для планирования. </summary>
    private sealed class WorkspaceMaintenancePlannerRow
    {
        /// <summary> Идентификатор транспортного средства. </summary>
        public Guid VehicleId { get; init; }

        /// <summary> Дата обслуживания. </summary>
        public DateTime Date { get; init; }

        /// <summary> Пробег на момент обслуживания. </summary>
        public int? Mileage { get; init; }
    }
}
