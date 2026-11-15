using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Planning;

/// <summary> Записи аудита действий планировщика. </summary>
internal static class SchedulerAudit
{
    /// <summary> Добавляет запись аудита по событию планировщика. </summary>
    internal static void Add(
        OpenCRMContext context,
        Guid eventId,
        Guid organizationId,
        string userId,
        string eventType,
        string title,
        string? description,
        Guid taskId)
    {
        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = eventId,
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = eventType,
            Title = title,
            Description = description,
            TargetType = "scheduler_task",
            TargetId = taskId.ToString("D")
        });
    }
}
