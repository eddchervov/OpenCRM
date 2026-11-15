using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Clients.Commands;

/// <summary> Писатель истории и аудита клиентов организации. </summary>
internal sealed class OrganizationClientHistoryWriter(
    OpenCRMContext context,
    IOrganizationAuditService organizationAuditService)
{
    /// <summary> Добавляет запись в историю клиента. </summary>
    internal Guid AddHistory(
        Guid clientId,
        Guid organizationId,
        string userId,
        string eventType,
        string title,
        string? description,
        string? relatedEntityType,
        Guid? relatedEntityId,
        DateTime createdAt)
    {
        var history = new OrganizationClientHistoryEvent
        {
            OrganizationClientId = clientId,
            OrganizationId = organizationId,
            UserId = userId,
            ActorDisplayName = null,
            EventType = eventType,
            Title = title,
            Description = description,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = createdAt
        };
        context.OrganizationClientHistoryEvents.Add(history);

        return history.Id;
    }

    /// <summary> Добавляет запись аудита по клиенту. </summary>
    internal Task AddAuditAsync(
        Guid organizationId,
        string userId,
        string eventType,
        string title,
        string? description,
        Guid clientId) => organizationAuditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = eventType,
            Title = title,
            Description = description,
            TargetType = "client",
            TargetId = clientId.ToString("D")
        }, saveChanges: false);
}
