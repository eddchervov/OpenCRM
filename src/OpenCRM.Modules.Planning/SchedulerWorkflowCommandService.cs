using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Planning;

/// <summary> Сервис команд жизненного цикла задач планировщика по источникам. </summary>
internal sealed class SchedulerWorkflowCommandService(
    OpenCRMContext context,
    SchedulerAccess access,
    SchedulerTransaction transaction) : ISchedulerWorkflowCommandService
{
    /// <summary> Обновляет состояние задачи планировщика по коду статуса источника. </summary>
    public Task UpdateTaskWorkflowAsync(
        string userId, Guid organizationId, Guid vehicleId, string sourceCode, Guid? sourceEntityId, string triggerKey, string statusCode) =>
        transaction.ExecuteAuditedAsync(eventId => this.UpdateCoreAsync(
            eventId, userId, organizationId, vehicleId, sourceCode, sourceEntityId, triggerKey, statusCode));

    /// <summary> Откладывает задачу планировщика до указанной даты и возвращает её. </summary>
    public Task<DateTime?> DeferTaskAsync(
        string userId, Guid organizationId, Guid vehicleId, string sourceCode, Guid? sourceEntityId,
        string triggerKey, DateTime? deferredUntil, DateTime today) =>
        transaction.ExecuteAuditedAsync(eventId => this.DeferCoreAsync(
            eventId, userId, organizationId, vehicleId, sourceCode, sourceEntityId, triggerKey, deferredUntil, today));

    /// <summary> Выполняет обновление состояния задачи с аудитом. </summary>
    private async Task UpdateCoreAsync(
        Guid eventId, string userId, Guid organizationId, Guid vehicleId, string sourceCode,
        Guid? sourceEntityId, string triggerKey, string statusCode)
    {
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        await access.EnsureCanWriteAsync(userId, organizationId);
        await transaction.LockOrganizationAsync(organizationId);
        var normalizedStatus = OrganizationSchedulerTaskStatusCodes.Normalize(statusCode);
        var process = await access.ResolveProcessAsync(organizationId);
        var normalizedTriggerKey = SchedulerRules.NormalizeRequired(triggerKey, "Не указан ключ задачи планировщика.");
        var stateId = await this.UpsertTaskStateAsync(organizationId, vehicleId, SchedulerRules.NormalizeFilter(sourceCode),
            sourceEntityId, normalizedTriggerKey, normalizedStatus, process, null, userId);
        this.AddWorkflowAudit(eventId, organizationId, userId, "scheduler.workflow.updated", "Этап рабочего процесса планировщика изменен", normalizedStatus, stateId);
        await context.SaveChangesAsync();
    }

    /// <summary> Выполняет отложение задачи с аудитом. </summary>
    private async Task<DateTime?> DeferCoreAsync(
        Guid eventId, string userId, Guid organizationId, Guid vehicleId, string sourceCode,
        Guid? sourceEntityId, string triggerKey, DateTime? deferredUntil, DateTime today)
    {
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        await access.EnsureCanWriteAsync(userId, organizationId);
        await transaction.LockOrganizationAsync(organizationId);
        var process = await access.ResolveProcessAsync(organizationId);
        var normalizedTriggerKey = SchedulerRules.NormalizeRequired(triggerKey, "Не указан ключ задачи планировщика.");
        DateTime? result = deferredUntil?.Date;
        if (result.HasValue && result.Value < today.Date)
        {
            throw new InvalidOperationException("Нельзя отложить задачу на прошедшую дату.");
        }
        var stateId = await this.UpsertTaskStateAsync(organizationId, vehicleId, SchedulerRules.NormalizeFilter(sourceCode), sourceEntityId,
            normalizedTriggerKey, result.HasValue ? OrganizationSchedulerTaskStatusCodes.Deferred : OrganizationSchedulerTaskStatusCodes.New,
            process, result, userId);
        this.AddWorkflowAudit(eventId, organizationId, userId, "scheduler.workflow.deferred", "Срок задачи рабочего процесса обновлен",
            result?.ToString("yyyy-MM-dd") ?? "Без отложенной даты", stateId);
        await context.SaveChangesAsync();

        return result;
    }

    /// <summary> Создаёт или обновляет состояние задачи планировщика по триггеру. </summary>
    private async Task<Guid> UpsertTaskStateAsync(
        Guid organizationId, Guid vehicleId, string sourceCode, Guid? sourceEntityId, string triggerKey,
        string statusCode, OrganizationScenarioProcessDefinition process, DateTime? deferredUntil, string userId)
    {
        var existing = await this.LoadTaskStateAsync(organizationId, triggerKey);
        if (existing != null && existing.VehicleId != vehicleId)
        {
            throw new InvalidOperationException("Ключ задачи планировщика уже связан с другой техникой.");
        }

        if (existing == null)
        {
            await access.NormalizeVehicleIdAsync(organizationId, vehicleId);
        }

        var now = DateTime.UtcNow;
        var normalizedStatus = OrganizationSchedulerTaskStatusCodes.Normalize(statusCode);
        var taskState = existing ?? new OrganizationSchedulerTaskState
        {
            OrganizationId = organizationId,
            VehicleId = vehicleId,
            TriggerKey = triggerKey
        };
        if (existing == null)
        {
            context.OrganizationSchedulerTaskStates.Add(taskState);
        }
        taskState.VehicleId = vehicleId;
        taskState.SourceCode = sourceCode;
        taskState.SourceEntityId = sourceEntityId;
        taskState.StatusCode = normalizedStatus;
        taskState.ProcessCode = process.Code;
        taskState.WorkflowStageCode = OrganizationScenarioProcessCatalog.ResolveStageCode(process.Code, normalizedStatus, sourceCode);
        taskState.DeferredUntil = normalizedStatus == OrganizationSchedulerTaskStatusCodes.Deferred ? deferredUntil?.Date : null;
        taskState.ClosedAt = normalizedStatus == OrganizationSchedulerTaskStatusCodes.Closed ? now : null;
        taskState.UserId = userId;
        taskState.UpdatedAt = now;

        return taskState.Id;
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

    /// <summary> Добавляет запись аудита изменения workflow. </summary>
    private void AddWorkflowAudit(Guid eventId, Guid organizationId, string userId, string eventType, string title, string description, Guid stateId)
    {
        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = eventId,
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = eventType,
            Title = title,
            Description = description,
            TargetType = "scheduler_workflow",
            TargetId = stateId.ToString("D")
        });
    }
}
