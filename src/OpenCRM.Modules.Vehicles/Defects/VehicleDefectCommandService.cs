using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.History;
using OpenCRM.Services.Interfaces.Defects.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Vehicles.Defects;

/// <summary> Сервис команд управления дефектами техники. </summary>
internal sealed class VehicleDefectCommandService(
    OpenCRMContext context,
    IOrganizationSubscriptionService organizationSubscriptionService)
{
    /// <summary> Тип события истории о создании дефекта. </summary>
    private const string EventDefectCreated = "defect_created";

    /// <summary> Тип события истории об изменении дефекта. </summary>
    private const string EventDefectUpdated = "defect_updated";

    /// <summary> Тип события истории об удалении дефекта. </summary>
    private const string EventDefectDeleted = "defect_deleted";

    /// <summary> Тип события истории о смене статуса дефекта. </summary>
    private const string EventDefectStatusChanged = "defect_status_changed";

    /// <summary> Тип события истории о связи дефекта с обслуживанием. </summary>
    private const string EventDefectMaintenanceLinked = "defect_maintenance_linked";

    /// <summary> Тип события истории об отвязке дефекта от обслуживания. </summary>
    private const string EventDefectMaintenanceUnlinked = "defect_maintenance_unlinked";

    /// <summary> Создаёт дефект техники и возвращает идентификатор. </summary>
    internal async Task<Guid> CreateAsync(string userId, Guid organizationId, CreateVehicleDefectDto dto)
    {
        await VehicleDefectRules.EnsureDefectsModuleEnabledAsync(organizationSubscriptionService, userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.DefectsCreate);
        await VehicleDefectRules.EnsureVehicleBelongsToOrganizationAsync(context, organizationId, dto.VehicleId);

        var now = DateTime.UtcNow;
        var statusCode = VehicleDefectRules.NormalizeStatusCode(dto.StatusCode, VehicleDefectStatusCodes.New);
        var defect = new VehicleDefect
        {
            OrganizationId = organizationId,
            VehicleId = dto.VehicleId,
            UserId = userId,
            Title = VehicleDefectRules.NormalizeRequired(dto.Title, "Укажите название неисправности."),
            Description = VehicleDefectRules.NormalizeNullable(dto.Description),
            SeverityCode = VehicleDefectRules.NormalizeSeverityCode(dto.SeverityCode),
            StatusCode = statusCode,
            ClosedAt = VehicleDefectStatusCodes.IsClosed(statusCode) ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.VehicleDefects.Add(defect);
        await context.SaveChangesAsync();
        await AddVehicleHistoryEventAsync(
            userId,
            organizationId,
            defect.VehicleId,
            EventDefectCreated,
            "Создана неисправность",
            defect.Title,
            "defect",
            defect.Id);
        return defect.Id;
    }

    /// <summary> Обновляет данные дефекта техники. </summary>
    internal async Task UpdateAsync(string userId, Guid organizationId, Guid defectId, UpdateVehicleDefectDto dto)
    {
        await VehicleDefectRules.EnsureDefectsModuleEnabledAsync(organizationSubscriptionService, userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.DefectsEdit);
        await VehicleDefectRules.EnsureVehicleBelongsToOrganizationAsync(context, organizationId, dto.VehicleId);

        var defect = await context.VehicleDefects
            .FirstOrDefaultAsync(x => x.Id == defectId && x.OrganizationId == organizationId && !x.IsDeleted)
            ?? throw new InvalidOperationException("Неисправность не найдена.");

        var previous = VehicleDefectRules.VehicleDefectSnapshot.From(defect);
        var statusCode = VehicleDefectRules.NormalizeStatusCode(dto.StatusCode, defect.StatusCode);
        defect.VehicleId = dto.VehicleId;
        defect.Title = VehicleDefectRules.NormalizeRequired(dto.Title, "Укажите название неисправности.");
        defect.Description = VehicleDefectRules.NormalizeNullable(dto.Description);
        defect.SeverityCode = VehicleDefectRules.NormalizeSeverityCode(dto.SeverityCode);
        defect.StatusCode = statusCode;
        defect.ClosedAt = VehicleDefectStatusCodes.IsClosed(statusCode) ? (defect.ClosedAt ?? DateTime.UtcNow) : null;
        defect.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        var changes = VehicleDefectRules.BuildDefectChanges(previous, defect);
        if (changes.Count > 0)
        {
            await AddVehicleHistoryEventAsync(
                userId,
                organizationId,
                defect.VehicleId,
                EventDefectUpdated,
                "Изменена неисправность",
                string.Join("\n", changes),
                "defect",
                defect.Id);
        }
    }

    /// <summary> Удаляет дефект техники. </summary>
    internal async Task DeleteAsync(string userId, Guid organizationId, Guid defectId)
    {
        await VehicleDefectRules.EnsureDefectsModuleEnabledAsync(organizationSubscriptionService, userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.DefectsDelete);

        var defect = await context.VehicleDefects
            .FirstOrDefaultAsync(x => x.Id == defectId && x.OrganizationId == organizationId && !x.IsDeleted)
            ?? throw new InvalidOperationException("Неисправность не найдена.");

        var maintenanceLinks = await context.MaintenanceDefects
            .Where(x => x.VehicleDefectId == defectId && x.OrganizationId == organizationId)
            .ToListAsync();

        if (maintenanceLinks.Count > 0)
        {
            context.MaintenanceDefects.RemoveRange(maintenanceLinks);
        }

        defect.Delete(DateTime.UtcNow);
        await context.SaveChangesAsync();
        await AddVehicleHistoryEventAsync(
            userId,
            organizationId,
            defect.VehicleId,
            EventDefectDeleted,
            "Удалена неисправность",
            defect.Title,
            "defect",
            defect.Id);
    }

    /// <summary> Обновляет статус дефекта техники. </summary>
    internal async Task UpdateStatusAsync(string userId, Guid organizationId, Guid defectId, string statusCode)
    {
        await VehicleDefectRules.EnsureDefectsModuleEnabledAsync(organizationSubscriptionService, userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.DefectsStatusManage);

        var defect = await context.VehicleDefects
            .FirstOrDefaultAsync(x => x.Id == defectId && x.OrganizationId == organizationId && !x.IsDeleted)
            ?? throw new InvalidOperationException("Неисправность не найдена.");

        var normalizedStatus = VehicleDefectRules.NormalizeStatusCode(statusCode, defect.StatusCode);
        if (string.Equals(defect.StatusCode, normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previousStatusName = VehicleDefectStatusCodes.GetName(defect.StatusCode);
        defect.StatusCode = normalizedStatus;
        defect.ClosedAt = VehicleDefectStatusCodes.IsClosed(normalizedStatus) ? DateTime.UtcNow : null;
        defect.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        await AddVehicleHistoryEventAsync(
            userId,
            organizationId,
            defect.VehicleId,
            EventDefectStatusChanged,
            "Изменено состояние неисправности",
            $"Состояние: {previousStatusName} -> {VehicleDefectStatusCodes.GetName(normalizedStatus)}",
            "defect",
            defect.Id);
    }

    /// <summary> Связывает дефект с обслуживанием. </summary>
    internal async Task LinkMaintenanceAsync(string userId, Guid organizationId, Guid defectId, Guid maintenanceId)
    {
        await VehicleDefectRules.EnsureDefectsModuleEnabledAsync(organizationSubscriptionService, userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.DefectsMaintenanceLink);

        var defect = await context.VehicleDefects
            .FirstOrDefaultAsync(x => x.Id == defectId && x.OrganizationId == organizationId && !x.IsDeleted)
            ?? throw new InvalidOperationException("Неисправность не найдена.");

        var maintenance = await context.Maintenance
            .FirstOrDefaultAsync(x => x.Id == maintenanceId
                && x.VehicleId == defect.VehicleId
                && x.Vehicle.OrganizationId == organizationId
                && !x.IsDeleted)
            ?? throw new InvalidOperationException("Обслуживание не найдено для этой техники.");

        var linkExists = await context.MaintenanceDefects
            .AnyAsync(x => x.MaintenanceId == maintenanceId && x.VehicleDefectId == defectId);

        if (linkExists)
        {
            return;
        }

        await context.MaintenanceDefects.AddAsync(new MaintenanceDefect
        {
            MaintenanceId = maintenanceId,
            VehicleDefectId = defectId,
            OrganizationId = organizationId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        defect.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        await AddVehicleHistoryEventAsync(
            userId,
            organizationId,
            defect.VehicleId,
            EventDefectMaintenanceLinked,
            "Привязано обслуживание к неисправности",
            $"Обслуживание: {VehicleDefectRules.GetMaintenanceTypeText(maintenance.Type)} от {VehicleDefectRules.GetMaintenanceDisplayDate(maintenance):dd.MM.yyyy}",
            "defect",
            defect.Id);
    }

    /// <summary> Отвязывает дефект от обслуживания. </summary>
    internal async Task UnlinkMaintenanceAsync(string userId, Guid organizationId, Guid defectId, Guid maintenanceId)
    {
        await VehicleDefectRules.EnsureDefectsModuleEnabledAsync(organizationSubscriptionService, userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.DefectsMaintenanceLink);

        var link = await context.MaintenanceDefects
            .Include(x => x.VehicleDefect)
            .Include(x => x.Maintenance)
            .FirstOrDefaultAsync(x => x.MaintenanceId == maintenanceId
                && x.VehicleDefectId == defectId
                && x.OrganizationId == organizationId
                && !x.VehicleDefect.IsDeleted
                && !x.Maintenance.IsDeleted)
            ?? throw new InvalidOperationException("Связь обслуживания с неисправностью не найдена.");

        var defect = link.VehicleDefect;
        var maintenance = link.Maintenance;

        context.MaintenanceDefects.Remove(link);
        defect.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        await AddVehicleHistoryEventAsync(
            userId,
            organizationId,
            defect.VehicleId,
            EventDefectMaintenanceUnlinked,
            "Связь обслуживания с неисправностью удалена",
            $"Обслуживание: {VehicleDefectRules.GetMaintenanceTypeText(maintenance.Type)} от {VehicleDefectRules.GetMaintenanceDisplayDate(maintenance):dd.MM.yyyy}",
            "defect",
            defect.Id);
    }

    /// <summary> Добавляет событие в историю техники. </summary>
    private async Task AddVehicleHistoryEventAsync(
        string userId,
        Guid organizationId,
        Guid vehicleId,
        string eventType,
        string title,
        string? description,
        string? relatedEntityType,
        Guid? relatedEntityId)
    {
        var actorDisplayName = await ResolveActorDisplayNameAsync(userId);
        context.VehicleHistoryEvents.Add(new VehicleHistoryEvent
        {
            VehicleId = vehicleId,
            OrganizationId = organizationId,
            UserId = userId,
            ActorDisplayName = actorDisplayName,
            EventType = eventType,
            Title = title,
            Description = VehicleDefectRules.Truncate(description, 1000),
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
    }

    /// <summary> Определяет отображаемое имя автора действия. </summary>
    private async Task<string> ResolveActorDisplayNameAsync(string userId)
    {
        return await HistoryActorDisplayNameQuery.ResolveAsync(context, userId);
    }

}
