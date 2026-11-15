using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Rules;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Проверяет допустимость назначения постов обслуживания. </summary>
internal sealed class MaintenanceSlotGuard(
    OpenCRMContext context)
{
    /// <summary> Проверяет допустимость назначения локации обслуживания. </summary>
    internal async Task EnsureCanAssignLocationAsync(
        Guid? organizationId,
        string? location,
        string? previousLocation = null,
        bool requireActiveSameLocation = false)
    {
        if (!organizationId.HasValue || location == null)
        {
            return;
        }

        var normalizedLocation = MaintenanceSlotRules.NormalizeSlotLocation(location);
        if (normalizedLocation == MaintenanceSlotRules.NormalizeSlotLocation(previousLocation) && !requireActiveSameLocation)
        {
            return;
        }

        var matchingPosts = await context.OrganizationScenarioCatalogItems.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId.Value
                && item.KindCode == OrganizationScenarioCatalogKindCodes.WorkPost
                && item.Name.Trim().ToUpper() == normalizedLocation)
            .Select(item => item.IsActive)
            .ToListAsync();

        if (matchingPosts.Count > 0 && !matchingPosts.Contains(true))
        {
            throw new InvalidOperationException("Выбранный пост в архиве. Для новой записи выберите действующий пост.");
        }
    }

    /// <summary> Определяет, требует ли изменённая запись проверки активного поста. </summary>
    internal static bool RequiresActivePost(MaintenanceSnapshot previous, MaintenanceEntity maintenance)
    {
        if (!MaintenanceSlotGuard.IsOccupyingSlot(maintenance.Status))
        {
            return false;
        }

        if (!MaintenanceSlotGuard.IsOccupyingSlot(previous.Status))
        {
            return true;
        }

        return previous.PlannedDate != MaintenanceDateRules.NormalizeMaintenanceDate(maintenance.PlannedDate)
            || (previous.PlannedEndDate ?? previous.PlannedDate)
                != (MaintenanceDateRules.NormalizeMaintenanceDate(maintenance.PlannedEndDate)
                    ?? MaintenanceDateRules.NormalizeMaintenanceDate(maintenance.PlannedDate))
            || previous.PlannedStartTime != maintenance.PlannedStartTime
            || previous.PlannedEndTime != maintenance.PlannedEndTime;
    }

    private static bool IsOccupyingSlot(MaintenanceStatus status)
    {
        return status is MaintenanceStatus.Planned or MaintenanceStatus.InProgress;
    }

    /// <summary> Проверяет отсутствие конфликта записи с занятыми постами. </summary>
    internal async Task EnsureNoWorkspaceSlotConflictAsync(Guid? organizationId, MaintenanceEntity maintenance)
    {
        if (!organizationId.HasValue || maintenance.Status == MaintenanceStatus.Cancelled
            || !maintenance.PlannedStartTime.HasValue || string.IsNullOrWhiteSpace(maintenance.Location))
        {
            return;
        }

        var location = MaintenanceSlotRules.NormalizeSlotLocation(maintenance.Location);
        if (context.Database.CurrentTransaction != null)
        {
            // Проверка и сохранение на одном посту выполняются последовательно, включая разные дни.
            var lockKey = $"maintenance-slot:{organizationId.Value:D}:{location}";
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");
        }

        var start = MaintenanceSlotRules.GetPlannedStart(maintenance);
        var end = MaintenanceSlotRules.GetPlannedEnd(maintenance);
        var items = await MaintenanceSlotRules.QueryOccupiedSlots(context, organizationId.Value, start.Date, end.Date.AddDays(1), maintenance.Id)
            .ToListAsync();
        var hasConflict = items.Any(x => MaintenanceSlotRules.NormalizeSlotLocation(x.Location) == location
            && start < MaintenanceSlotRules.GetPlannedEnd(x) && MaintenanceSlotRules.GetPlannedStart(x) < end);
        if (!hasConflict)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Пост \"{maintenance.Location.Trim()}\" уже занят на этот период. Выберите другой пост или свободный интервал.");
    }
}
