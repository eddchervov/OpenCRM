using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Planning.Contracts;

namespace OpenCRM.Modules.Planning;

/// <summary> Сервис управления генерацией повторяющихся задач. </summary>
internal sealed class SchedulerRecurrenceService(
    OpenCRMContext context,
    SchedulerAccess access,
    SchedulerTransaction transaction,
    SchedulerRecurrenceEngine engine) : ISchedulerRecurrenceService
{
    /// <summary> Генерирует экземпляры повторяющихся задач организации до указанной даты. </summary>
    public Task<int> GenerateRecurringTaskInstancesAsync(string userId, Guid organizationId, DateTime horizonDate) =>
        transaction.ExecuteAuditedAsync(eventId => this.GenerateCoreAsync(eventId, userId, organizationId, horizonDate));

    /// <summary> Выполняет генерацию повторяющихся задач с аудитом. </summary>
    private async Task<int> GenerateCoreAsync(Guid eventId, string userId, Guid organizationId, DateTime horizonDate)
    {
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        await access.EnsureCanReadAsync(userId, organizationId);
        await transaction.LockOrganizationAsync(organizationId);
        var created = await engine.GenerateForOrganizationAsync(organizationId, horizonDate);

        if (created == 0)
        {
            return 0;
        }

        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = eventId,
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = "scheduler.recurrence.generated",
            Title = "Повторы задач планировщика обновлены",
            Description = $"Создано экземпляров: {created}.",
            TargetType = "scheduler_recurrence",
            TargetId = organizationId.ToString("D")
        });
        await context.SaveChangesAsync();

        return created;
    }
}
