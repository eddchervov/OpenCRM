using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations;

/// <summary> Сервис аудита событий организации. </summary>
public sealed class OrganizationAuditService(OpenCRMContext context) : IOrganizationAuditService
{
    /// <summary> Добавляет событие аудита организации. </summary>
    public async Task AddAsync(OrganizationAuditEventCreateDto dto, bool saveChanges = true)
    {
        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = dto.EventId ?? Guid.NewGuid(),
            OrganizationId = dto.OrganizationId,
            ActorUserId = dto.ActorUserId,
            EventType = Normalize(dto.EventType),
            Title = dto.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            TargetType = string.IsNullOrWhiteSpace(dto.TargetType) ? null : dto.TargetType.Trim().ToLowerInvariant(),
            TargetId = string.IsNullOrWhiteSpace(dto.TargetId) ? null : dto.TargetId.Trim(),
            CreatedAt = DateTime.UtcNow
        });

        if (saveChanges)
        {
            await context.SaveChangesAsync();
        }
    }

    /// <summary> Возвращает последние события аудита организации. </summary>
    public async Task<IReadOnlyList<OrganizationAuditEventDto>> GetRecentAsync(string userId, Guid organizationId, int take = 20)
    {
        var canRead = await context.Organizations
            .AsNoTracking()
            .AnyAsync(x => x.Id == organizationId
                && !x.IsDeleted
                && x.Members.Any(member => member.UserId == userId));

        if (!canRead)
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }

        var normalizedTake = Math.Clamp(take, 1, 50);
        var events = await context.OrganizationAuditEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(normalizedTake)
            .GroupJoin(
                context.ApplicationUsers.AsNoTracking(),
                auditEvent => auditEvent.ActorUserId,
                user => user.Id,
                (auditEvent, users) => new { AuditEvent = auditEvent, User = users.FirstOrDefault() })
            .ToListAsync();

        return events
            .Select(x => new OrganizationAuditEventDto
            {
                Id = x.AuditEvent.Id,
                OrganizationId = x.AuditEvent.OrganizationId,
                ActorUserId = x.AuditEvent.ActorUserId,
                ActorDisplayName = ResolveActorName(x.User, x.AuditEvent.ActorUserId),
                EventType = x.AuditEvent.EventType,
                Title = x.AuditEvent.Title,
                Description = x.AuditEvent.Description,
                TargetType = x.AuditEvent.TargetType,
                TargetId = x.AuditEvent.TargetId,
                CreatedAt = x.AuditEvent.CreatedAt
            })
            .ToList();
    }

    /// <summary> Определяет имя автора события аудита. </summary>
    private static string ResolveActorName(Domain.Identity.ApplicationUser? user, string actorUserId)
    {
        if (user == null)
        {
            return "Пользователь удален";
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email;
        }

        return !string.IsNullOrWhiteSpace(user.UserName) ? user.UserName : actorUserId;
    }

    /// <summary> Нормализует текст для события аудита. </summary>
    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "organization.changed"
            : value.Trim().ToLowerInvariant();
}
