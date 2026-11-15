using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Maintenance.Rules;

/// <summary> Общие правила поиска и расчета занятости постов. </summary>
internal static class MaintenanceSlotRules
{
    /// <summary> Возвращает запрос занятых слотов обслуживания за период. </summary>
    internal static IQueryable<MaintenanceEntity> QueryOccupiedSlots(OpenCRMContext context, Guid organizationId, DateTime from, DateTime to, Guid? excludeMaintenanceId)
    {
        return context.Maintenance
            .AsNoTracking()
            .Where(x => (!excludeMaintenanceId.HasValue || x.Id != excludeMaintenanceId.Value)
                && !x.IsDeleted && !x.Vehicle.IsDeleted
                && x.Status != MaintenanceStatus.Cancelled
                && x.Status != MaintenanceStatus.Completed
                && x.Vehicle.OrganizationId == organizationId
                && (x.PlannedDate ?? x.Date).Date < to
                && (x.PlannedEndDate ?? x.PlannedDate ?? x.Date).Date >= from.Date
                && x.PlannedStartTime.HasValue
                && x.Location != null);
    }

    /// <summary> Возвращает планируемое время начала обслуживания. </summary>
    internal static DateTime GetPlannedStart(MaintenanceEntity maintenance) =>
        (maintenance.PlannedDate ?? maintenance.Date).Date.Add(maintenance.PlannedStartTime ?? TimeSpan.Zero);

    /// <summary> Возвращает планируемое время окончания обслуживания. </summary>
    internal static DateTime GetPlannedEnd(MaintenanceEntity maintenance)
    {
        var start = MaintenanceSlotRules.GetPlannedStart(maintenance);
        var end = (maintenance.PlannedEndDate ?? maintenance.PlannedDate ?? maintenance.Date).Date
            .Add(maintenance.PlannedEndTime ?? start.TimeOfDay.Add(TimeSpan.FromHours(1)));

        // Старые записи без корректного окончания сохраняют прежний часовой интервал.
        return end > start ? end : start.AddHours(1);
    }

    /// <summary> Нормализует название места слота обслуживания. </summary>
    internal static string NormalizeSlotLocation(string? location)
    {
        return string.IsNullOrWhiteSpace(location)
            ? string.Empty
            : location.Trim().ToUpperInvariant();
    }
}