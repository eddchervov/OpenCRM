using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations;

/// <summary> Запись событий аудита организации. </summary>
internal sealed class OrganizationAuditWriter(IOrganizationAuditService auditService)
{
    /// <summary> Добавляет событие аудита организации с заданными данными. </summary>
    internal Task AddAsync(
        Guid organizationId,
        string userId,
        string eventType,
        string title,
        string? description,
        bool saveChanges,
        Guid eventId) =>
        auditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            EventId = eventId,
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = eventType,
            Title = title,
            Description = description,
            TargetType = "organization",
            TargetId = organizationId.ToString("D")
        }, saveChanges);
}
