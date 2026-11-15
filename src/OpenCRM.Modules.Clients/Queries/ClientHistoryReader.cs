using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Clients.Rules;

namespace OpenCRM.Modules.Clients.Queries;

/// <summary> Читатель истории клиентов организации. </summary>
internal sealed class ClientHistoryReader(OpenCRMContext context)
{
    /// <summary> Контекст базы данных для чтения истории. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Загружает события истории клиента с недавними обслуживаниями и дефектами. </summary>
    internal async Task<IReadOnlyList<OrganizationClientHistoryEventDto>> LoadAsync(
        Guid organizationId,
        Guid clientId,
        IReadOnlyList<OrganizationClientMaintenanceItemDto> recentMaintenances,
        IReadOnlyList<OrganizationClientDefectItemDto> defects)
    {
        var rows = await this._context.OrganizationClientHistoryEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.OrganizationClientId == clientId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(30)
            .Select(x => new
            {
                x.Id,
                x.OrganizationClientId,
                x.EventType,
                x.Title,
                x.Description,
                x.ActorDisplayName,
                x.RelatedEntityType,
                x.RelatedEntityId,
                x.CreatedAt,
                x.User.Email,
                x.User.UserName,
                x.User.OriginalEmail,
                x.User.IsDeleted
            })
            .ToListAsync();

        var historyItems = rows.Select(x => new OrganizationClientHistoryEventDto
        {
            Id = x.Id,
            OrganizationClientId = x.OrganizationClientId,
            EventType = x.EventType,
            Title = x.Title,
            Description = x.Description,
            ActorDisplayName = ClientRules.ResolveActorName(x.ActorDisplayName, x.Email, x.UserName, x.OriginalEmail, x.IsDeleted),
            RelatedEntityType = x.RelatedEntityType,
            RelatedEntityId = x.RelatedEntityId,
            CreatedAt = x.CreatedAt
        }).ToList();

        historyItems.AddRange(recentMaintenances.Select(x => new OrganizationClientHistoryEventDto
        {
            Id = x.Id,
            OrganizationClientId = clientId,
            EventType = "maintenance",
            Title = $"{ClientRules.MaintenanceTypeName(x.Type)}: {ClientRules.MaintenanceStatusName(x.Status)}",
            Description = ClientRules.BuildHistoryDescription(x.VehicleDisplayName, x.Comment),
            RelatedEntityType = "maintenance",
            RelatedEntityId = x.Id,
            CreatedAt = ClientRules.ResolveMaintenanceHistoryDate(x)
        }));

        historyItems.AddRange(defects.Select(x => new OrganizationClientHistoryEventDto
        {
            Id = x.Id,
            OrganizationClientId = clientId,
            EventType = x.StatusCode == VehicleDefectStatusCodes.Resolved ? "defect_resolved" : "defect_created",
            Title = x.StatusCode == VehicleDefectStatusCodes.Resolved ? "Неисправность устранена" : "Неисправность добавлена",
            Description = ClientRules.BuildHistoryDescription(x.VehicleDisplayName, x.Title),
            RelatedEntityType = "defect",
            RelatedEntityId = x.Id,
            CreatedAt = x.ClosedAt ?? x.CreatedAt
        }));

        return historyItems
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Title)
            .Take(40)
            .ToList();
    }
}
