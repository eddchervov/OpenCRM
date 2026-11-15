using Microsoft.EntityFrameworkCore;
using Helpers;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Planning;

/// <summary> Доступ к возможностям планировщика с проверками и нормализацией данных. </summary>
internal sealed class SchedulerAccess(
    OpenCRMContext context,
    IOrganizationSubscriptionService organizationSubscriptionService,
    ICatalogInitializationService catalogInitializationService)
{
    /// <summary> Проверяет, что модуль планировщика включён для организации. </summary>
    internal async Task EnsureSchedulerModuleEnabledAsync(string userId, Guid organizationId)
    {
        var hasSchedulerModule = await organizationSubscriptionService.HasFeatureAsync(userId, organizationId, FeatureCodes.SchedulerModule);
        if (!hasSchedulerModule)
        {
            throw new InvalidOperationException("Раздел планирования недоступен для рабочего пространства.");
        }
    }

    /// <summary> Проверяет право пользователя на чтение планировщика. </summary>
    internal Task EnsureCanReadAsync(string userId, Guid organizationId) =>
        organizationSubscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.SchedulerView);

    /// <summary> Проверяет право пользователя на запись в планировщик. </summary>
    internal Task EnsureCanWriteAsync(string userId, Guid organizationId) =>
        organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.SchedulerManage);

    /// <summary> Проверяет наличие стандартных записей каталога организации. </summary>
    internal Task EnsureCatalogDefaultsAsync(Guid organizationId) => catalogInitializationService.EnsureDefaultsAsync(organizationId);

    /// <summary> Загружает ручную задачу планировщика организации. </summary>
    internal async Task<OrganizationSchedulerTask> LoadManualTaskAsync(Guid organizationId, Guid taskId)
    {
        var trackedTask = context.OrganizationSchedulerTasks.Local.FirstOrDefault(task => task.Id == taskId);
        if (trackedTask != null)
        {
            context.Entry(trackedTask).State = EntityState.Detached;
        }

        var task = await context.OrganizationSchedulerTasks
            .FirstOrDefaultAsync(value => value.OrganizationId == organizationId && value.Id == taskId && !value.IsDeleted);

        return task ?? throw new InvalidOperationException("Задача планировщика не найдена.");
    }

    /// <summary> Нормализует идентификатор техники для организации. </summary>
    internal async Task<Guid?> NormalizeVehicleIdAsync(Guid organizationId, Guid? vehicleId)
    {
        if (!vehicleId.HasValue)
        {
            return null;
        }

        var exists = await context.Vehicles.AsNoTracking()
            .AnyAsync(value => value.OrganizationId == organizationId && value.Id == vehicleId.Value && !value.IsDeleted && !value.IsArchived);
        if (!exists)
        {
            throw new InvalidOperationException("Выбранная техника не найдена в рабочем пространстве.");
        }

        return vehicleId.Value;
    }

    /// <summary> Нормализует идентификатор клиента организации. </summary>
    internal async Task<Guid?> NormalizeClientIdAsync(Guid organizationId, Guid? clientId)
    {
        if (!clientId.HasValue)
        {
            return null;
        }

        var exists = await context.OrganizationClients.AsNoTracking()
            .AnyAsync(value => value.OrganizationId == organizationId && value.Id == clientId.Value && !value.IsDeleted);
        if (!exists)
        {
            throw new InvalidOperationException("Выбранный клиент не найден в рабочем пространстве.");
        }

        return clientId.Value;
    }

    /// <summary> Нормализует идентификатор ответственного пользователя. </summary>
    internal async Task<string?> NormalizeResponsibleUserIdAsync(Guid organizationId, string? responsibleUserId)
    {
        var normalized = SchedulerRules.NormalizeNullable(responsibleUserId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var exists = await context.OrganizationMembers.AsNoTracking()
            .AnyAsync(value => value.OrganizationId == organizationId && value.UserId == normalized);
        if (!exists)
        {
            throw new InvalidOperationException("Выбранный ответственный не найден в рабочем пространстве.");
        }

        return normalized;
    }

    /// <summary> Нормализует идентификатор дефекта для организации. </summary>
    internal async Task<Guid?> NormalizeDefectIdAsync(Guid organizationId, Guid? defectId)
    {
        if (!defectId.HasValue)
        {
            return null;
        }

        var exists = await context.VehicleDefects.AsNoTracking()
            .AnyAsync(value => value.OrganizationId == organizationId && value.Id == defectId.Value && !value.IsDeleted);
        if (!exists)
        {
            throw new InvalidOperationException("Выбранная неисправность не найдена.");
        }

        return defectId.Value;
    }

    /// <summary> Нормализует идентификатор обслуживания для организации. </summary>
    internal async Task<Guid?> NormalizeMaintenanceIdAsync(Guid organizationId, Guid? maintenanceId)
    {
        if (!maintenanceId.HasValue)
        {
            return null;
        }

        var exists = await context.Maintenance.AsNoTracking()
            .AnyAsync(value => value.Id == maintenanceId.Value && !value.IsDeleted && value.Vehicle.OrganizationId == organizationId);
        if (!exists)
        {
            throw new InvalidOperationException("Выбранное обслуживание не найдено.");
        }

        return maintenanceId.Value;
    }

    /// <summary> Определяет сценарный процесс организации. </summary>
    internal async Task<OrganizationScenarioProcessDefinition> ResolveProcessAsync(Guid organizationId)
    {
        var scenarioCode = await context.Organizations.AsNoTracking()
            .Where(value => value.Id == organizationId)
            .Select(value => value.PrimaryScenarioCode)
            .FirstOrDefaultAsync();

        return OrganizationScenarioProcessCatalog.Resolve(scenarioCode);
    }
}
