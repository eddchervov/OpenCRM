using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Синхронизирует связи обслуживания с дефектами техники. </summary>
internal sealed class MaintenanceDefectLinks(
    ISystemClock systemClock,
    OpenCRMContext context,
    MaintenanceHistoryWriter maintenanceHistoryWriter)
{
    /// <summary> Запись событий в историю обслуживания. </summary>
    private readonly MaintenanceHistoryWriter _maintenanceHistoryWriter = maintenanceHistoryWriter;

    /// <summary> Синхронизирует связанные дефекты по обслуживанию. </summary>
    internal async Task SyncLinkedDefectsAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid maintenanceId,
        MaintenanceStatus status,
        IEnumerable<Guid>? defectIds)
    {
        if (!organizationId.HasValue || defectIds == null)
        {
            return;
        }

        var targetIds = defectIds.Distinct().ToList();
        var actorDisplayName = await this._maintenanceHistoryWriter.ResolveActorDisplayNameAsync(userId);
        var existingLinks = await context.MaintenanceDefects
            .Where(x => x.MaintenanceId == maintenanceId)
            .ToListAsync();

        var linksToRemove = existingLinks
            .Where(x => !targetIds.Contains(x.VehicleDefectId))
            .ToList();

        if (linksToRemove.Count > 0)
        {
            var removedDefectIds = linksToRemove.Select(x => x.VehicleDefectId).ToList();
            var removedDefects = await context.VehicleDefects
                .AsNoTracking()
                .Where(x => removedDefectIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Title })
                .ToListAsync();

            context.MaintenanceDefects.RemoveRange(linksToRemove);

            foreach (var removedDefect in removedDefects)
            {
                this._maintenanceHistoryWriter.AddHistoryEvent(
                    userId,
                    organizationId,
                    vehicleId,
                    maintenanceId,
                    MaintenanceHistoryEventCodes.EventDefectUnlinked,
                    "Связь с неисправностью удалена",
                    $"Неисправность: {removedDefect.Title}",
                    actorDisplayName);

                context.VehicleHistoryEvents.Add(new VehicleHistoryEvent
                {
                    VehicleId = vehicleId,
                    OrganizationId = organizationId,
                    UserId = userId,
                    ActorDisplayName = actorDisplayName,
                    EventType = MaintenanceHistoryEventCodes.EventDefectUnlinked,
                    Title = "Связь обслуживания с неисправностью удалена",
                    Description = $"Обслуживание: {maintenanceId}",
                    RelatedEntityType = "defect",
                    RelatedEntityId = removedDefect.Id,
                    CreatedAt = systemClock.UtcNow
                });
            }
        }

        if (targetIds.Count == 0)
        {
            await context.SaveChangesAsync();
            return;
        }

        var defects = await context.VehicleDefects
            .Where(x => targetIds.Contains(x.Id)
                && x.OrganizationId == organizationId.Value
                && x.VehicleId == vehicleId
                && !x.IsDeleted)
            .ToListAsync();

        if (defects.Count != targetIds.Count)
        {
            throw new InvalidOperationException("Одна или несколько связанных неисправностей не найдены для этой техники.");
        }

        var existingDefectIds = existingLinks.Select(x => x.VehicleDefectId).ToHashSet();
        foreach (var defect in defects.Where(x => !existingDefectIds.Contains(x.Id)))
        {
            await context.MaintenanceDefects.AddAsync(new MaintenanceDefect
            {
                MaintenanceId = maintenanceId,
                VehicleDefectId = defect.Id,
                OrganizationId = organizationId.Value,
                UserId = userId,
                CreatedAt = systemClock.UtcNow
            });

            this._maintenanceHistoryWriter.AddHistoryEvent(
                userId,
                organizationId,
                vehicleId,
                maintenanceId,
                MaintenanceHistoryEventCodes.EventDefectLinked,
                "Привязана неисправность",
                $"Неисправность: {defect.Title}",
                actorDisplayName);

            context.VehicleHistoryEvents.Add(new VehicleHistoryEvent
            {
                VehicleId = vehicleId,
                OrganizationId = organizationId,
                UserId = userId,
                ActorDisplayName = actorDisplayName,
                EventType = MaintenanceHistoryEventCodes.EventDefectLinked,
                Title = "Привязано обслуживание к неисправности",
                Description = $"Обслуживание: {maintenanceId}",
                RelatedEntityType = "defect",
                RelatedEntityId = defect.Id,
                CreatedAt = systemClock.UtcNow
            });
        }

        if (status == MaintenanceStatus.Completed)
        {
            this.CloseDefects(defects);
        }

        await context.SaveChangesAsync();
    }

    /// <summary> Проверяет, что дефекты принадлежат указанной технике. </summary>
    internal async Task EnsureLinkedDefectsBelongToVehicleAsync(Guid? organizationId, Guid vehicleId, IEnumerable<Guid>? defectIds)
    {
        if (!organizationId.HasValue || defectIds == null)
        {
            return;
        }

        var targetIds = defectIds.Distinct().ToList();
        if (targetIds.Count == 0)
        {
            return;
        }

        var existingCount = await context.VehicleDefects
            .CountAsync(x => targetIds.Contains(x.Id)
                && x.OrganizationId == organizationId.Value
                && x.VehicleId == vehicleId
                && !x.IsDeleted);

        if (existingCount != targetIds.Count)
        {
            throw new InvalidOperationException("Одна или несколько связанных неисправностей не найдены для этой техники.");
        }
    }

    /// <summary> Закрывает дефекты, связанные с обслуживанием. </summary>
    internal async Task CloseLinkedDefectsAsync(Guid maintenanceId)
    {
        var defects = await context.MaintenanceDefects
            .Where(x => x.MaintenanceId == maintenanceId && !x.VehicleDefect.IsDeleted)
            .Select(x => x.VehicleDefect)
            .ToListAsync();

        this.CloseDefects(defects);
        await context.SaveChangesAsync();
    }

    /// <summary> Закрывает переданные дефекты техники. </summary>
    internal void CloseDefects(IEnumerable<VehicleDefect> defects)
    {
        foreach (var defect in defects.Where(x => x.StatusCode != VehicleDefectStatusCodes.Resolved))
        {
            defect.StatusCode = VehicleDefectStatusCodes.Resolved;
            defect.ClosedAt = systemClock.UtcNow;
            defect.UpdatedAt = systemClock.UtcNow;
        }
    }
}
