using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Rules;

namespace OpenCRM.Modules.Organizations.Invitations;

/// <summary> Сервис запросов приглашений в организацию. </summary>
internal sealed class OrganizationInvitationQueryService(
    OpenCRMContext context,
    OrganizationScenarioReader scenarioReader) : IOrganizationInvitationQueryService
{
    /// <summary> Возвращает ожидающие приглашения пользователя в организации. </summary>
    public Task<IReadOnlyList<PendingOrganizationInvitationDto>> GetPendingInvitationsAsync(string userId) =>
        this.GetInvitationsAsync(userId, pendingOnly: true);

    /// <summary> Возвращает приглашения пользователя в организации. </summary>
    public Task<IReadOnlyList<PendingOrganizationInvitationDto>> GetInvitationsAsync(string userId) =>
        this.GetInvitationsAsync(userId, pendingOnly: false);

    /// <summary> Возвращает приглашения пользователя с фильтром по ожиданию. </summary>
    private async Task<IReadOnlyList<PendingOrganizationInvitationDto>> GetInvitationsAsync(
        string userId,
        bool pendingOnly)
    {
        var user = await context.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == userId);
        if (user == null || string.IsNullOrWhiteSpace(user.Email))
        {
            return [];
        }

        var normalizedEmail = OrganizationRules.NormalizeEmail(user.Email);
        var scenarios = await scenarioReader.LoadNamesAsync();
        var invitations = await context.OrganizationInvitations
            .AsNoTracking()
            .Include(invitation => invitation.Organization)
            .Include(invitation => invitation.Employee)
            .Where(invitation => (invitation.NormalizedEmail == normalizedEmail
                    || invitation.AcceptedByUserId == userId)
                && (!pendingOnly || invitation.StatusCode == OrganizationInvitationStatusCodes.Pending)
                && !invitation.Organization.IsDeleted
                && invitation.Organization.IsActive
                && !invitation.Organization.IsArchived)
            .OrderByDescending(invitation => invitation.CreatedAt)
            .ToListAsync();

        return invitations
            .GroupBy(invitation => invitation.OrganizationId)
            .Select(group => group.First())
            .Select(invitation => new PendingOrganizationInvitationDto
            {
                InvitationId = invitation.Id,
                OrganizationId = invitation.OrganizationId,
                OrganizationName = invitation.Organization.Name,
                OrganizationScenarioCode = invitation.Organization.PrimaryScenarioCode,
                OrganizationScenarioName = scenarios.TryGetValue(
                    invitation.Organization.PrimaryScenarioCode,
                    out var scenarioName)
                    ? scenarioName
                    : invitation.Organization.PrimaryScenarioCode,
                EmployeeName = invitation.Employee?.Name,
                RoleCode = invitation.RoleCode,
                RoleName = OrganizationRules.GetRoleName(invitation.RoleCode),
                StatusCode = invitation.StatusCode,
                StatusName = OrganizationRules.GetInvitationStatusName(invitation.StatusCode),
                CanAccept = user.EmailConfirmed,
                CreatedAt = invitation.CreatedAt
            })
            .ToList();
    }
}
