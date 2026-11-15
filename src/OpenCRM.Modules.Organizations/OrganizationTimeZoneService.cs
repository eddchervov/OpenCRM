using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations;

/// <summary> Управление глобальным часовым поясом организации. </summary>
internal sealed class OrganizationTimeZoneService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptionService,
    IOrganizationPermissionService permissionService,
    IOrganizationAuditService auditService,
    OrganizationTransaction transaction) : IOrganizationTimeZoneService
{
    /// <inheritdoc />
    public async Task<OrganizationTimeZoneDto> GetAsync(string userId, Guid organizationId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.WorkspaceView);

        var organization = await context.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId
                && organization.IsActive
                && !organization.IsArchived
                && !organization.IsDeleted
                && organization.Members.Any(member => member.UserId == userId))
            .Select(organization => new { organization.ShiftTimeZoneId })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Организация не найдена.");

        var canManage = await permissionService.HasPermissionAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.WorkspaceProfileEdit);
        if (canManage)
        {
            try
            {
                await subscriptionService.EnsureWorkspaceCanWriteAsync(
                    userId,
                    organizationId,
                    OrganizationPermissionCodes.WorkspaceProfileEdit);
            }
            catch (UnauthorizedAccessException)
            {
                canManage = false;
            }
            catch (InvalidOperationException)
            {
                canManage = false;
            }
        }

        return new OrganizationTimeZoneDto
        {
            TimeZoneId = organization.ShiftTimeZoneId,
            CanManage = canManage
        };
    }

    /// <inheritdoc />
    public async Task<OrganizationTimeZoneDto> UpdateAsync(
        string userId,
        Guid organizationId,
        UpdateOrganizationTimeZoneDto dto)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.WorkspaceProfileEdit);

        var normalizedTimeZoneId = NormalizeTimeZoneId(dto.TimeZoneId);
        await transaction.ExecuteAuditedAsync(async eventId =>
        {
            var organizationQuery = context.Organizations
                .Where(organization => organization.Id == organizationId
                    && organization.IsActive
                    && !organization.IsArchived
                    && !organization.IsDeleted);
            organizationQuery = dto.ExpectedTimeZoneId == null
                ? organizationQuery.Where(organization => organization.ShiftTimeZoneId == null)
                : organizationQuery.Where(organization => organization.ShiftTimeZoneId == dto.ExpectedTimeZoneId);

            var updatedCount = await organizationQuery.ExecuteUpdateAsync(setters => setters
                .SetProperty(organization => organization.ShiftTimeZoneId, normalizedTimeZoneId)
                .SetProperty(organization => organization.UpdatedAt, DateTime.UtcNow));
            if (updatedCount != 1)
            {
                throw new DbUpdateConcurrencyException("Часовой пояс уже изменён. Обновите настройки и повторите попытку.");
            }

            await auditService.AddAsync(new OrganizationAuditEventCreateDto
            {
                EventId = eventId,
                OrganizationId = organizationId,
                ActorUserId = userId,
                EventType = "organization.timezone.changed",
                Title = "Часовой пояс организации изменён",
                Description = $"{dto.ExpectedTimeZoneId ?? "не задан"} → {normalizedTimeZoneId}",
                TargetType = "organization",
                TargetId = organizationId.ToString("D")
            }, saveChanges: false);
            await context.SaveChangesAsync();
        });

        return new OrganizationTimeZoneDto
        {
            TimeZoneId = normalizedTimeZoneId,
            CanManage = true
        };
    }

    private static string NormalizeTimeZoneId(string? timeZoneId)
    {
        var normalizedTimeZoneId = timeZoneId?.Trim() ?? string.Empty;
        if (string.Equals(normalizedTimeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return "Etc/UTC";
        }

        if (normalizedTimeZoneId.Length is < 1 or > 100 || !normalizedTimeZoneId.Contains('/'))
        {
            throw new InvalidOperationException("Выберите часовой пояс IANA.");
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(normalizedTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException("Часовой пояс не найден.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new InvalidOperationException("Часовой пояс недоступен.");
        }

        return normalizedTimeZoneId;
    }
}
