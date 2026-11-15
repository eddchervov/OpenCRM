using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Planning;

/// <summary> Движок генерации экземпляров повторяющихся задач. </summary>
internal sealed class SchedulerRecurrenceEngine(OpenCRMContext context)
{
    /// <summary> Генерирует экземпляры повторяющихся задач организации до горизонта. </summary>
    internal async Task<int> GenerateForOrganizationAsync(Guid organizationId, DateTime horizonDate)
    {
        var horizon = horizonDate.ToUniversalTime().Date;
        var templateIds = await context.OrganizationSchedulerTasks.AsNoTracking()
            .Where(value => value.OrganizationId == organizationId && !value.IsDeleted
                && value.RecurrenceParentTaskId == null && value.StartAt.HasValue
                && value.RecurrenceCode != OrganizationSchedulerTaskRecurrenceCodes.None)
            .Select(value => value.Id)
            .ToListAsync();
        var created = 0;
        foreach (var templateId in templateIds)
        {
            created += await this.GenerateForTaskAsync(templateId, horizon);
        }

        return created;
    }

    /// <summary> Обновляет экземпляры повторяющейся задачи по шаблону. </summary>
    internal async Task RefreshAsync(Guid templateTaskId, DateTime horizonDate)
    {
        var now = DateTime.UtcNow;
        var futureGeneratedTasks = await context.OrganizationSchedulerTasks
            .Where(value => value.RecurrenceParentTaskId == templateTaskId && !value.IsDeleted
                && value.StatusCode == OrganizationSchedulerTaskStatusCodes.New
                && value.UpdatedAt == value.CreatedAt && (!value.StartAt.HasValue || value.StartAt.Value >= now))
            .ToListAsync();
        if (futureGeneratedTasks.Count > 0)
        {
            context.OrganizationSchedulerTasks.RemoveRange(futureGeneratedTasks);
            await context.SaveChangesAsync();
        }
        await this.GenerateForTaskAsync(templateTaskId, horizonDate);
    }

    /// <summary> Генерирует экземпляры повторяющейся задачи до горизонта. </summary>
    internal async Task<int> GenerateForTaskAsync(Guid templateTaskId, DateTime horizonDate)
    {
        var trackedTemplate = context.OrganizationSchedulerTasks.Local.FirstOrDefault(value => value.Id == templateTaskId);
        if (trackedTemplate != null)
        {
            context.Entry(trackedTemplate).State = EntityState.Detached;
        }

        var template = await context.OrganizationSchedulerTasks.FirstOrDefaultAsync(value => value.Id == templateTaskId && !value.IsDeleted);
        if (template == null || template.RecurrenceParentTaskId.HasValue || !template.StartAt.HasValue
            || OrganizationSchedulerTaskRecurrenceCodes.Normalize(template.RecurrenceCode) == OrganizationSchedulerTaskRecurrenceCodes.None)
        {
            return 0;
        }
        SchedulerRules.NormalizeTaskRecurrence(template);
        var horizon = horizonDate.ToUniversalTime().Date.AddDays(1).AddTicks(-1);
        var recurrenceUntil = template.RecurrenceUntil?.ToUniversalTime();
        var generationUntil = recurrenceUntil.HasValue && recurrenceUntil.Value < horizon ? recurrenceUntil.Value : horizon;
        if (generationUntil <= template.StartAt.Value)
        {
            template.RecurrenceGeneratedUntil = generationUntil;
            await context.SaveChangesAsync();

            return 0;
        }
        var existingStartSet = (await context.OrganizationSchedulerTasks.AsNoTracking()
                .Where(value => value.RecurrenceParentTaskId == template.Id && !value.IsDeleted && value.StartAt.HasValue)
                .Select(value => value.StartAt!.Value)
                .ToListAsync())
            .Select(value => value.ToUniversalTime()).ToHashSet();
        var duration = template.EndAt.HasValue && template.EndAt.Value > template.StartAt.Value
            ? template.EndAt.Value - template.StartAt.Value
            : TimeSpan.FromHours(1);
        var reminderOffset = template.ReminderAt.HasValue
            ? template.ReminderAt.Value - template.StartAt.Value
            : (TimeSpan?)null;
        var createdCount = 0;
        var nextStart = SchedulerRules.NextRecurrenceStart(template.StartAt.Value, template.RecurrenceCode, template.RecurrenceInterval ?? 1);
        while (nextStart <= generationUntil && createdCount < 366)
        {
            if (existingStartSet.Add(nextStart))
            {
                var createdAt = DateTime.UtcNow;
                context.OrganizationSchedulerTasks.Add(new OrganizationSchedulerTask
                {
                    OrganizationId = template.OrganizationId,
                    VehicleId = template.VehicleId,
                    OrganizationClientId = template.OrganizationClientId,
                    ResponsibleUserId = template.ResponsibleUserId,
                    CreatedByUserId = template.CreatedByUserId,
                    Title = template.Title,
                    Comment = template.Comment,
                    TypeCode = template.TypeCode,
                    PriorityCode = template.PriorityCode,
                    StatusCode = OrganizationSchedulerTaskStatusCodes.New,
                    ProcessCode = template.ProcessCode,
                    WorkflowStageCode = template.WorkflowStageCode,
                    LinkedDefectId = template.LinkedDefectId,
                    LinkedMaintenanceId = template.LinkedMaintenanceId,
                    StartAt = nextStart,
                    EndAt = nextStart.Add(duration),
                    RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.None,
                    RecurrenceParentTaskId = template.Id,
                    ReminderAt = reminderOffset.HasValue ? nextStart.Add(reminderOffset.Value) : null,
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt
                });
                createdCount++;
            }
            nextStart = SchedulerRules.NextRecurrenceStart(nextStart, template.RecurrenceCode, template.RecurrenceInterval ?? 1);
        }
        template.RecurrenceGeneratedUntil = generationUntil;
        template.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        return createdCount;
    }
}
