using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Rules;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Queries;

/// <summary> Сервис запросов данных организаций. </summary>
internal sealed class OrganizationQueryService(
    OpenCRMContext context,
    IOrganizationPermissionService permissions,
    OrganizationScenarioReader scenarios,
    IConfiguration configuration) : IOrganizationQueryService
{
    /// <summary> Срок хранения организации в архиве в днях. </summary>
    private const int ArchiveRetentionDays = 30;

    /// <summary> Зависимость доступа к данным приложения. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Зависимость проверки разрешений организации. </summary>
    private readonly IOrganizationPermissionService _permissions = permissions;

    /// <summary> Зависимость чтения сценариев организации. </summary>
    private readonly OrganizationScenarioReader _scenarios = scenarios;

    /// <summary> Конфигурация доступности архивирования. </summary>
    private readonly IConfiguration _configuration = configuration;

    /// <summary> Возвращает организацию по идентификатору для пользователя. </summary>
    public async Task<OrganizationDto> GetByIdAsync(string userId, Guid organizationId)
    {
        var scenarios = await this._scenarios.LoadNamesAsync();
        var organization = await this._context.Organizations
            .AsNoTracking()
            .Include(x => x.Members)
                .ThenInclude(x => x.User)
            .Include(x => x.Invitations)
                .ThenInclude(x => x.Employee)
            .FirstOrDefaultAsync(x => x.Id == organizationId
                && !x.IsDeleted
                && x.Members.Any(member => member.UserId == userId))
            ?? throw new InvalidOperationException("Организация не найдена.");

        var employeeNames = await this.LoadEmployeeNamesAsync([organizationId]);
        var dto = OrganizationQueryService.MapOrganization(organization, userId, scenarios, employeeNames);
        await this.ApplyPermissionFlagsAsync(userId, dto);

        return dto;
    }

    /// <summary> Возвращает обзор организаций пользователя. </summary>
    public async Task<TeamOverviewDto> GetOverviewAsync(string userId)
    {
        var scenarios = await this._scenarios.LoadNamesAsync();

        var organizations = await this._context.Organizations
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsArchived && x.IsActive && x.Members.Any(member => member.UserId == userId))
            .Include(x => x.Members)
                .ThenInclude(x => x.User)
            .Include(x => x.Invitations)
                .ThenInclude(x => x.Employee)
            .OrderBy(x => x.Name)
            .ToListAsync();

        var archivedOrganizations = await this._context.Organizations
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsArchived && x.Members.Any(member => member.UserId == userId))
            .Include(x => x.Members)
                .ThenInclude(x => x.User)
            .OrderByDescending(x => x.ArchiveDate)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var organizationIds = organizations.Concat(archivedOrganizations).Select(organization => organization.Id).ToList();
        var employeeNames = await this.LoadEmployeeNamesAsync(organizationIds);

        var result = new TeamOverviewDto
        {
            Organizations = organizations
                .Select(x => OrganizationQueryService.MapOrganization(x, userId, scenarios, employeeNames))
                .ToList(),
            ArchivedOrganizations = archivedOrganizations
                .Select(x => OrganizationQueryService.MapOrganization(x, userId, scenarios, employeeNames))
                .ToList()
        };

        foreach (var organization in result.Organizations)
        {
            await this.ApplyPermissionFlagsAsync(userId, organization);
        }

        foreach (var organization in result.ArchivedOrganizations)
        {
            OrganizationQueryService.ApplyArchivedPermissionFlags(organization);
        }

        return result;
    }

    /// <summary> Преобразует организацию в DTO с названиями сценариев. </summary>
    private static OrganizationDto MapOrganization(
        Organization organization,
        string currentUserId,
        IReadOnlyDictionary<string, string> scenarioNames,
        IReadOnlyDictionary<Guid, string> employeeNames)
    {
        var currentMember = organization.Members.First(x => x.UserId == currentUserId);

        return new OrganizationDto
        {
            Id = organization.Id,
            Name = organization.Name,
            Description = organization.Description,
            LegalName = organization.LegalName,
            Inn = organization.Inn,
            Kpp = organization.Kpp,
            Ogrn = organization.Ogrn,
            LegalAddress = organization.LegalAddress,
            ActualAddress = organization.ActualAddress,
            Phone = organization.Phone,
            Email = organization.Email,
            SignatoryPosition = organization.SignatoryPosition,
            SignatoryName = organization.SignatoryName,
            SignatoryBasis = organization.SignatoryBasis,
            DocumentStyleCode = organization.DocumentStyleCode,
            MaintenanceWorkflowMode = OrganizationRules.NormalizeMaintenanceWorkflowMode(organization.MaintenanceWorkflowMode),
            TimeZoneId = organization.ShiftTimeZoneId,
            CurrencyCode = organization.CurrencyCode,
            AllowWorkSelfAssignment = organization.AllowWorkSelfAssignment,
            PrimaryScenarioCode = organization.PrimaryScenarioCode,
            PrimaryScenarioName = scenarioNames.TryGetValue(organization.PrimaryScenarioCode, out var scenarioName)
                ? scenarioName
                : organization.PrimaryScenarioCode,
            CurrentUserRoleCode = currentMember.RoleCode,
            CurrentUserRoleName = OrganizationRules.GetRoleName(currentMember.RoleCode),
            CanRestore = (currentMember.RoleCode == OrganizationRoleCodes.Owner || organization.OwnerUserId == currentUserId) && organization.IsArchived,
            MembersCount = organization.Members.Count,
            CreatedAt = organization.CreatedAt,
            IsArchived = organization.IsArchived,
            ArchiveDate = organization.ArchiveDate,
            DaysLeft = organization.ArchiveDate.HasValue
                ? Math.Max(0, ArchiveRetentionDays - (int)(DateTime.UtcNow - organization.ArchiveDate.Value).TotalDays)
                : (int?)null,
            Members = organization.Members
                .OrderBy(x => OrganizationRules.RoleSortOrder(x.RoleCode))
                .ThenBy(x => x.User.Email)
                .Select(x => new OrganizationMemberDto
                {
                    Id = x.Id,
                    UserId = x.UserId,
                    Email = x.User.Email,
                    UserName = x.User.UserName,
                    EmployeeName = x.User.IsDeleted ? null : employeeNames.GetValueOrDefault(x.Id),
                    RoleCode = x.RoleCode,
                    RoleName = OrganizationRules.GetRoleName(x.RoleCode),
                    JoinedAt = x.JoinedAt,
                    IsOwner = x.RoleCode == OrganizationRoleCodes.Owner
                })
                .ToList(),
            PendingInvitations = organization.IsArchived
                ? []
                : organization.Invitations
                    .Where(x => x.StatusCode == OrganizationInvitationStatusCodes.Pending)
                    .OrderBy(x => x.Email)
                    .Select(x => new OrganizationInvitationDto
                    {
                        Id = x.Id,
                        Email = x.Email,
                        EmployeeId = x.EmployeeId,
                        EmployeeName = x.Employee?.Name,
                        RoleCode = x.RoleCode,
                        RoleName = OrganizationRules.GetRoleName(x.RoleCode),
                        StatusCode = x.StatusCode,
                        StatusName = OrganizationRules.GetInvitationStatusName(x.StatusCode),
                        CreatedAt = x.CreatedAt
                    })
                    .ToList(),
            Invitations = organization.IsArchived
                ? []
                : organization.Invitations
                    .GroupBy(x => x.NormalizedEmail)
                    .Select(group => group.OrderByDescending(x => x.CreatedAt).First())
                    .OrderBy(x => x.Email)
                    .Select(x => new OrganizationInvitationDto
                    {
                        Id = x.Id,
                        Email = x.Email,
                        EmployeeId = x.EmployeeId,
                        EmployeeName = x.Employee?.Name,
                        RoleCode = x.RoleCode,
                        RoleName = OrganizationRules.GetRoleName(x.RoleCode),
                        StatusCode = x.StatusCode,
                        StatusName = OrganizationRules.GetInvitationStatusName(x.StatusCode),
                        CreatedAt = x.CreatedAt
                    })
                    .ToList()
        };
    }

    /// <summary> Загружает имена сотрудников, связанных с участниками выбранных организаций. </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> LoadEmployeeNamesAsync(IReadOnlyCollection<Guid> organizationIds)
    {
        if (organizationIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await this._context.OrganizationEmployees
            .AsNoTracking()
            .Where(employee => organizationIds.Contains(employee.OrganizationId) && employee.MemberId.HasValue)
            .ToDictionaryAsync(employee => employee.MemberId!.Value, employee => employee.Name);
    }

    /// <summary> Устанавливает флаги разрешений пользователя в DTO организации. </summary>
    private async Task ApplyPermissionFlagsAsync(string userId, OrganizationDto organization)
    {
        if (organization.IsArchived)
        {
            OrganizationQueryService.ApplyArchivedPermissionFlags(organization);
            return;
        }

        organization.CanManageMembers =
            await this._permissions.HasPermissionAsync(userId, organization.Id, OrganizationPermissionCodes.MembersInvite)
            || await this._permissions.HasPermissionAsync(userId, organization.Id, OrganizationPermissionCodes.MembersRolesManage)
            || await this._permissions.HasPermissionAsync(userId, organization.Id, OrganizationPermissionCodes.MembersRemove);

        organization.CanManageSettings =
            await this._permissions.HasPermissionAsync(userId, organization.Id, OrganizationPermissionCodes.WorkspaceProfileEdit)
            || await this._permissions.HasPermissionAsync(userId, organization.Id, OrganizationPermissionCodes.WorkspaceRequisitesEdit);

        organization.CanManageBilling =
            await this._permissions.HasPermissionAsync(userId, organization.Id, OrganizationPermissionCodes.BillingManage);

        organization.CanArchive =
            string.Equals(this._configuration["Organizations:ArchivingEnabled"], bool.TrueString, StringComparison.OrdinalIgnoreCase)
            && await this._permissions.HasPermissionAsync(userId, organization.Id, OrganizationPermissionCodes.WorkspaceArchive);
    }

    /// <summary> Устанавливает ограниченные флаги разрешений для архивной организации. </summary>
    private static void ApplyArchivedPermissionFlags(OrganizationDto organization)
    {
        organization.CanManageMembers = false;
        organization.CanManageSettings = false;
        organization.CanManageBilling = false;
        organization.CanArchive = false;
        organization.CanRestore = organization.CurrentUserRoleCode == OrganizationRoleCodes.Owner;
    }
}
