using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Planning;

/// <summary> Синхронизирует обслуживания с задачами планировщика организации. </summary>
internal sealed class MaintenanceSchedulerSyncService(OpenCRMContext context, SchedulerAccess access) : IMaintenanceSchedulerSyncService
{
    /// <summary> Связывает обслуживание с задачей планировщика по источнику и триггеру. </summary>
    public async Task LinkMaintenanceTaskAsync(
        string userId, Guid organizationId, Guid vehicleId, Guid maintenanceId, string sourceCode,
        Guid? sourceEntityId, string triggerKey, int maintenanceStatus)
    {
        this.EnsureAmbientTransaction();
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        var normalizedTriggerKey = SchedulerRules.NormalizeRequired(triggerKey, "Не указан ключ задачи планировщика.");
        var taskState = await this.LoadTaskStateAsync(organizationId, normalizedTriggerKey);
        if (taskState != null && taskState.VehicleId != vehicleId)
        {
            throw new InvalidOperationException("Ключ задачи планировщика уже связан с другой техникой.");
        }
        var process = await access.ResolveProcessAsync(organizationId);
        taskState ??= new OrganizationSchedulerTaskState
        {
            OrganizationId = organizationId,
            VehicleId = vehicleId,
            TriggerKey = normalizedTriggerKey
        };
        if (context.Entry(taskState).State == EntityState.Detached)
        {
            context.OrganizationSchedulerTaskStates.Add(taskState);
        }
        var status = (MaintenanceStatus)maintenanceStatus;
        var now = DateTime.UtcNow;
        taskState.SourceCode = SchedulerRules.NormalizeFilter(sourceCode);
        taskState.SourceEntityId = sourceEntityId;
        taskState.MaintenanceId = maintenanceId;
        taskState.StatusCode = SchedulerRules.MapSchedulerTaskStatusCode(status);
        taskState.ProcessCode = process.Code;
        taskState.WorkflowStageCode = OrganizationScenarioProcessCatalog.ResolveStageCode(process.Code, taskState.StatusCode, taskState.SourceCode);
        taskState.DeferredUntil = null;
        taskState.ClosedAt = status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled ? now : null;
        taskState.UserId = userId;
        taskState.UpdatedAt = now;
        await context.SaveChangesAsync();
    }

    /// <summary> Синхронизирует состояние задачи планировщика со статусом обслуживания. </summary>
    public async Task SyncMaintenanceWorkflowAsync(string userId, Guid organizationId, Guid maintenanceId, int maintenanceStatus)
    {
        this.EnsureAmbientTransaction();
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        var linkedTaskStates = await this.LoadMaintenanceStatesAsync(organizationId, maintenanceId);
        if (linkedTaskStates.Count == 0)
        {
            return;
        }
        var status = (MaintenanceStatus)maintenanceStatus;
        var process = await access.ResolveProcessAsync(organizationId);
        var now = DateTime.UtcNow;
        foreach (var taskState in linkedTaskStates)
        {
            taskState.StatusCode = SchedulerRules.MapSchedulerTaskStatusCode(status);
            taskState.ProcessCode = process.Code;
            taskState.WorkflowStageCode = OrganizationScenarioProcessCatalog.ResolveStageCode(process.Code, taskState.StatusCode, taskState.SourceCode);
            taskState.DeferredUntil = null;
            taskState.ClosedAt = status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled ? now : null;
            taskState.UserId = userId;
            taskState.UpdatedAt = now;
        }
        await context.SaveChangesAsync();
    }

    /// <summary> Сбрасывает состояние задачи планировщика для обслуживания. </summary>
    public async Task ResetMaintenanceWorkflowAsync(string userId, Guid organizationId, Guid maintenanceId)
    {
        this.EnsureAmbientTransaction();
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        var linkedTaskStates = await this.LoadMaintenanceStatesAsync(organizationId, maintenanceId);
        if (linkedTaskStates.Count == 0)
        {
            return;
        }
        var process = await access.ResolveProcessAsync(organizationId);
        foreach (var taskState in linkedTaskStates)
        {
            taskState.MaintenanceId = null;
            taskState.StatusCode = OrganizationSchedulerTaskStatusCodes.New;
            taskState.ProcessCode = process.Code;
            taskState.WorkflowStageCode = OrganizationScenarioProcessCatalog.ResolveStageCode(process.Code, taskState.StatusCode, taskState.SourceCode);
            taskState.DeferredUntil = null;
            taskState.ClosedAt = null;
            taskState.UserId = userId;
            taskState.UpdatedAt = DateTime.UtcNow;
        }
        await context.SaveChangesAsync();
    }

    /// <summary> Проверяет наличие открытой внешней транзакции. </summary>
    private void EnsureAmbientTransaction()
    {
        if (context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("Синхронизация планировщика должна выполняться в транзакции обслуживания.");
        }
    }

    /// <summary> Загружает состояние задачи планировщика по триггеру организации. </summary>
    private async Task<OrganizationSchedulerTaskState?> LoadTaskStateAsync(Guid organizationId, string triggerKey)
    {
        var tracked = context.OrganizationSchedulerTaskStates.Local
            .FirstOrDefault(value => value.OrganizationId == organizationId && value.TriggerKey == triggerKey);
        if (tracked != null)
        {
            context.Entry(tracked).State = EntityState.Detached;
        }

        return await context.OrganizationSchedulerTaskStates
            .FirstOrDefaultAsync(value => value.OrganizationId == organizationId && value.TriggerKey == triggerKey);
    }

    /// <summary> Загружает все состояния задач планировщика для обслуживания. </summary>
    private async Task<List<OrganizationSchedulerTaskState>> LoadMaintenanceStatesAsync(Guid organizationId, Guid maintenanceId)
    {
        var trackedStates = context.OrganizationSchedulerTaskStates.Local
            .Where(value => value.OrganizationId == organizationId && value.MaintenanceId == maintenanceId)
            .ToList();
        foreach (var taskState in trackedStates)
        {
            context.Entry(taskState).State = EntityState.Detached;
        }

        return await context.OrganizationSchedulerTaskStates
            .Where(value => value.OrganizationId == organizationId && value.MaintenanceId == maintenanceId)
            .ToListAsync();
    }
}
