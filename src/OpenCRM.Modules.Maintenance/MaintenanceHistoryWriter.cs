using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.History;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Записывает события в историю обслуживания. </summary>
internal sealed class MaintenanceHistoryWriter(
    ISystemClock systemClock,
    OpenCRMContext context)
{
    /// <summary> Добавляет события истории при обновлении обслуживания. </summary>
    internal async Task AddUpdateHistoryEventsAsync(
        string userId,
        Guid? organizationId,
        MaintenanceEntity maintenance,
        MaintenanceSnapshot previous)
    {
        var actorDisplayName = await this.ResolveActorDisplayNameAsync(userId);
        var hasDetailedEvents = false;

        if (previous.Status != maintenance.Status)
        {
            this.AddHistoryEvent(
                userId,
                organizationId,
                maintenance.VehicleId,
                maintenance.Id,
                MaintenanceHistoryEventCodes.EventStatusChanged,
                "Изменено состояние обслуживания",
                $"{MaintenanceChangeRules.GetMaintenanceStatusText(previous.Status)} -> {MaintenanceChangeRules.GetMaintenanceStatusText(maintenance.Status)}",
                actorDisplayName);
            hasDetailedEvents = true;
        }

        var fieldChanges = MaintenanceChangeRules.BuildFieldChanges(previous, maintenance);
        if (fieldChanges.Count > 0)
        {
            this.AddHistoryEvent(
                userId,
                organizationId,
                maintenance.VehicleId,
                maintenance.Id,
                MaintenanceHistoryEventCodes.EventFieldsChanged,
                "Изменены сведения обслуживания",
                string.Join("\n", fieldChanges),
                actorDisplayName);
            hasDetailedEvents = true;
        }

        var detailChanges = MaintenanceChangeRules.BuildDetailChanges(previous.Details, maintenance.Details);
        if (detailChanges.Count > 0)
        {
            this.AddHistoryEvent(
                userId,
                organizationId,
                maintenance.VehicleId,
                maintenance.Id,
                MaintenanceHistoryEventCodes.EventDetailsChanged,
                "Изменены услуги и работы",
                string.Join("\n", detailChanges),
                actorDisplayName);
            hasDetailedEvents = true;
        }

        if (!hasDetailedEvents)
        {
            this.AddHistoryEvent(
                userId,
                organizationId,
                maintenance.VehicleId,
                maintenance.Id,
                MaintenanceHistoryEventCodes.EventUpdated,
                "Обслуживание обновлено",
                MaintenanceChangeRules.BuildMaintenanceSummary(maintenance),
                actorDisplayName);
        }

        await context.SaveChangesAsync();
    }

    /// <summary> Определяет, изменился ли список связанных дефектов. </summary>
    internal async Task<bool> HasLinkedDefectChangesAsync(
        Guid? organizationId,
        Guid maintenanceId,
        IEnumerable<Guid>? defectIds)
    {
        if (!organizationId.HasValue || defectIds == null)
        {
            return false;
        }

        var targetIds = defectIds.Distinct().OrderBy(x => x).ToList();
        var existingIds = await context.MaintenanceDefects
            .AsNoTracking()
            .Where(x => x.MaintenanceId == maintenanceId)
            .Select(x => x.VehicleDefectId)
            .OrderBy(x => x)
            .ToListAsync();

        return !targetIds.SequenceEqual(existingIds);
    }

    /// <summary> Добавляет событие истории обслуживания асинхронно. </summary>
    internal async Task AddHistoryEventAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid maintenanceId,
        string eventType,
        string title,
        string? description = null,
        string? payloadJson = null)
    {
        var actorDisplayName = await this.ResolveActorDisplayNameAsync(userId);
        this.AddHistoryEvent(userId, organizationId, vehicleId, maintenanceId, eventType, title, description, actorDisplayName, payloadJson);
        await context.SaveChangesAsync();
    }

    /// <summary> Добавляет событие истории обслуживания. </summary>
    internal void AddHistoryEvent(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid maintenanceId,
        string eventType,
        string title,
        string? description = null,
        string? actorDisplayName = null,
        string? payloadJson = null)
    {
        context.MaintenanceHistoryEvents.Add(new MaintenanceHistoryEvent
        {
            MaintenanceId = maintenanceId,
            VehicleId = vehicleId,
            OrganizationId = organizationId,
            UserId = userId,
            ActorDisplayName = actorDisplayName,
            EventType = eventType,
            Title = title,
            Description = description,
            PayloadJson = payloadJson,
            CreatedAt = systemClock.UtcNow
        });
    }

    /// <summary> Возвращает отображаемое имя пользователя-инициатора. </summary>
    internal async Task<string> ResolveActorDisplayNameAsync(string userId)
    {
        return await HistoryActorDisplayNameQuery.ResolveAsync(context, userId);
    }
}
