using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Planning.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Planning;

/// <summary> Сервис команд управления ручными задачами планировщика. </summary>
internal sealed class SchedulerTaskCommandService(
    OpenCRMContext context,
    SchedulerAccess access,
    SchedulerTransaction transaction,
    SchedulerRecurrenceEngine recurrenceEngine) : ISchedulerTaskCommandService
{
    /// <summary> Создаёт ручную задачу планировщика. </summary>
    public Task<Guid> CreateTaskAsync(string userId, Guid organizationId, CreateOrganizationSchedulerTaskDto dto) =>
        transaction.ExecuteAuditedAsync(eventId => this.CreateCoreAsync(eventId, userId, organizationId, dto));

    /// <summary> Обновляет данные ручной задачи планировщика. </summary>
    public Task UpdateManualTaskAsync(string userId, Guid organizationId, Guid taskId, UpdateOrganizationSchedulerTaskDto dto) =>
        transaction.ExecuteAuditedAsync(eventId => this.UpdateCoreAsync(eventId, userId, organizationId, taskId, dto));

    /// <summary> Обновляет статус ручной задачи планировщика. </summary>
    public Task UpdateManualTaskStatusAsync(string userId, Guid organizationId, Guid taskId, string statusCode) =>
        transaction.ExecuteAuditedAsync(eventId => this.UpdateStatusCoreAsync(eventId, userId, organizationId, taskId, statusCode));

    /// <summary> Обновляет расписание ручной задачи планировщика. </summary>
    public Task UpdateManualTaskScheduleAsync(string userId, Guid organizationId, Guid taskId, UpdateOrganizationSchedulerTaskScheduleDto dto) =>
        transaction.ExecuteAuditedAsync(eventId => this.UpdateScheduleCoreAsync(eventId, userId, organizationId, taskId, dto));

    /// <summary> Удаляет ручную задачу планировщика. </summary>
    public Task DeleteManualTaskAsync(string userId, Guid organizationId, Guid taskId) =>
        transaction.ExecuteAuditedAsync(eventId => this.DeleteCoreAsync(eventId, userId, organizationId, taskId));

    /// <summary> Отмечает напоминание задачи отправленным. </summary>
    public Task MarkReminderSentAsync(string userId, Guid organizationId, Guid taskId, DateTime sentAt) =>
        transaction.ExecuteAuditedAsync(eventId => this.MarkReminderSentCoreAsync(eventId, userId, organizationId, taskId, sentAt));

    /// <summary> Выполняет создание задачи с аудитом. </summary>
    private async Task<Guid> CreateCoreAsync(Guid eventId, string userId, Guid organizationId, CreateOrganizationSchedulerTaskDto dto)
    {
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        await access.EnsureCanWriteAsync(userId, organizationId);
        await transaction.LockOrganizationAsync(organizationId);
        var process = await access.ResolveProcessAsync(organizationId);
        await access.EnsureCatalogDefaultsAsync(organizationId);
        var task = new OrganizationSchedulerTask
        {
            OrganizationId = organizationId,
            VehicleId = await access.NormalizeVehicleIdAsync(organizationId, dto.VehicleId),
            OrganizationClientId = await access.NormalizeClientIdAsync(organizationId, dto.OrganizationClientId),
            ResponsibleUserId = await access.NormalizeResponsibleUserIdAsync(organizationId, dto.ResponsibleUserId),
            CreatedByUserId = userId,
            Title = SchedulerRules.NormalizeRequired(dto.Title, "Укажите название задачи."),
            Comment = SchedulerRules.NormalizeNullable(dto.Comment),
            TypeCode = OrganizationSchedulerTaskTypeCodes.Normalize(dto.TypeCode),
            PriorityCode = OrganizationSchedulerTaskPriorityCodes.Normalize(dto.PriorityCode),
            StatusCode = OrganizationSchedulerTaskStatusCodes.New,
            ProcessCode = process.Code,
            LinkedDefectId = await access.NormalizeDefectIdAsync(organizationId, dto.LinkedDefectId),
            LinkedMaintenanceId = await access.NormalizeMaintenanceIdAsync(organizationId, dto.LinkedMaintenanceId),
            StartAt = SchedulerRules.NormalizeDateTime(dto.StartAt),
            EndAt = SchedulerRules.NormalizeDateTime(dto.EndAt),
            RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Normalize(dto.RecurrenceCode),
            RecurrenceInterval = SchedulerRules.NormalizeRecurrenceInterval(dto.RecurrenceInterval),
            RecurrenceUntil = SchedulerRules.NormalizeDateTime(dto.RecurrenceUntil),
            ReminderAt = SchedulerRules.NormalizeDateTime(dto.ReminderAt),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        SchedulerRules.NormalizeTaskDates(task);
        SchedulerRules.NormalizeTaskRecurrence(task);
        task.WorkflowStageCode = OrganizationScenarioProcessCatalog.ResolveStageCode(process.Code, task.StatusCode, "task");
        context.OrganizationSchedulerTasks.Add(task);
        SchedulerAudit.Add(context, eventId, organizationId, userId, "scheduler.task.created", "Задача планировщика создана", task.Title, task.Id);
        await context.SaveChangesAsync();
        await recurrenceEngine.GenerateForTaskAsync(task.Id, DateTime.UtcNow.Date.AddDays(180));

        return task.Id;
    }

    /// <summary> Выполняет обновление задачи с аудитом. </summary>
    private async Task UpdateCoreAsync(Guid eventId, string userId, Guid organizationId, Guid taskId, UpdateOrganizationSchedulerTaskDto dto)
    {
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        await access.EnsureCanWriteAsync(userId, organizationId);
        await transaction.LockOrganizationAsync(organizationId);
        var task = await access.LoadManualTaskAsync(organizationId, taskId);
        task.VehicleId = await access.NormalizeVehicleIdAsync(organizationId, dto.VehicleId);
        task.OrganizationClientId = await access.NormalizeClientIdAsync(organizationId, dto.OrganizationClientId);
        task.ResponsibleUserId = await access.NormalizeResponsibleUserIdAsync(organizationId, dto.ResponsibleUserId);
        task.Title = SchedulerRules.NormalizeRequired(dto.Title, "Укажите название задачи.");
        task.Comment = SchedulerRules.NormalizeNullable(dto.Comment);
        task.TypeCode = OrganizationSchedulerTaskTypeCodes.Normalize(dto.TypeCode);
        task.PriorityCode = OrganizationSchedulerTaskPriorityCodes.Normalize(dto.PriorityCode);
        task.LinkedDefectId = await access.NormalizeDefectIdAsync(organizationId, dto.LinkedDefectId);
        task.LinkedMaintenanceId = await access.NormalizeMaintenanceIdAsync(organizationId, dto.LinkedMaintenanceId);
        task.StartAt = SchedulerRules.NormalizeDateTime(dto.StartAt);
        task.EndAt = SchedulerRules.NormalizeDateTime(dto.EndAt);
        task.RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Normalize(dto.RecurrenceCode);
        task.RecurrenceInterval = SchedulerRules.NormalizeRecurrenceInterval(dto.RecurrenceInterval);
        task.RecurrenceUntil = SchedulerRules.NormalizeDateTime(dto.RecurrenceUntil);
        task.ReminderAt = SchedulerRules.NormalizeDateTime(dto.ReminderAt);
        task.UpdatedAt = DateTime.UtcNow;
        SchedulerRules.NormalizeTaskDates(task);
        SchedulerRules.NormalizeTaskRecurrence(task);
        SchedulerAudit.Add(context, eventId, organizationId, userId, "scheduler.task.updated", "Задача планировщика обновлена", task.Title, task.Id);
        await context.SaveChangesAsync();
        await recurrenceEngine.RefreshAsync(task.Id, DateTime.UtcNow.Date.AddDays(180));
    }

    /// <summary> Выполняет смену статуса задачи с аудитом. </summary>
    private async Task UpdateStatusCoreAsync(Guid eventId, string userId, Guid organizationId, Guid taskId, string statusCode)
    {
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        await access.EnsureCanWriteAsync(userId, organizationId);
        await transaction.LockOrganizationAsync(organizationId);
        var process = await access.ResolveProcessAsync(organizationId);
        var task = await access.LoadManualTaskAsync(organizationId, taskId);
        var normalizedStatus = OrganizationSchedulerTaskStatusCodes.Normalize(statusCode);
        task.StatusCode = normalizedStatus;
        task.ProcessCode = process.Code;
        task.WorkflowStageCode = OrganizationScenarioProcessCatalog.ResolveStageCode(process.Code, normalizedStatus, "task");
        task.ClosedAt = normalizedStatus == OrganizationSchedulerTaskStatusCodes.Closed ? DateTime.UtcNow : null;
        task.UpdatedAt = DateTime.UtcNow;
        SchedulerAudit.Add(context, eventId, organizationId, userId, "scheduler.task.status", "Статус задачи планировщика изменен", OrganizationSchedulerTaskStatusCodes.GetName(normalizedStatus), task.Id);
        await context.SaveChangesAsync();
    }

    /// <summary> Выполняет обновление расписания задачи с аудитом. </summary>
    private async Task UpdateScheduleCoreAsync(Guid eventId, string userId, Guid organizationId, Guid taskId, UpdateOrganizationSchedulerTaskScheduleDto dto)
    {
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        await access.EnsureCanWriteAsync(userId, organizationId);
        await transaction.LockOrganizationAsync(organizationId);
        var task = await access.LoadManualTaskAsync(organizationId, taskId);
        task.StartAt = SchedulerRules.NormalizeDateTime(dto.StartAt);
        task.EndAt = SchedulerRules.NormalizeDateTime(dto.EndAt);
        task.UpdatedAt = DateTime.UtcNow;
        SchedulerRules.NormalizeTaskDates(task);
        SchedulerAudit.Add(context, eventId, organizationId, userId, "scheduler.task.schedule", "Задача планировщика перенесена", task.Title, task.Id);
        await context.SaveChangesAsync();
    }

    /// <summary> Выполняет удаление задачи с аудитом. </summary>
    private async Task DeleteCoreAsync(Guid eventId, string userId, Guid organizationId, Guid taskId)
    {
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        await access.EnsureCanWriteAsync(userId, organizationId);
        await transaction.LockOrganizationAsync(organizationId);
        var task = await access.LoadManualTaskAsync(organizationId, taskId);
        task.IsDeleted = true;
        task.DeletedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        var generatedChildren = await context.OrganizationSchedulerTasks
            .Where(value => value.OrganizationId == organizationId && value.RecurrenceParentTaskId == task.Id && !value.IsDeleted)
            .ToListAsync();
        foreach (var child in generatedChildren)
        {
            child.IsDeleted = true;
            child.DeletedAt = DateTime.UtcNow;
            child.UpdatedAt = DateTime.UtcNow;
        }
        SchedulerAudit.Add(context, eventId, organizationId, userId, "scheduler.task.deleted", "Задача планировщика удалена", task.Title, task.Id);
        await context.SaveChangesAsync();
    }

    /// <summary> Выполняет отметку отправленного напоминания с аудитом. </summary>
    private async Task MarkReminderSentCoreAsync(Guid eventId, string userId, Guid organizationId, Guid taskId, DateTime sentAt)
    {
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        await access.EnsureCanWriteAsync(userId, organizationId);
        await transaction.LockOrganizationAsync(organizationId);
        var task = await access.LoadManualTaskAsync(organizationId, taskId);
        if (!task.ReminderAt.HasValue)
        {
            throw new InvalidOperationException("У задачи нет напоминания.");
        }
        task.ReminderSentAt = sentAt.ToUniversalTime();
        task.UpdatedAt = DateTime.UtcNow;
        SchedulerAudit.Add(context, eventId, organizationId, userId, "scheduler.task.reminder.sent", "Напоминание задачи обработано", task.Title, task.Id);
        await context.SaveChangesAsync();
    }

}
