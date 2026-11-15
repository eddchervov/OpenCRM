using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Rules;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Membership;

/// <summary> Сервис управления участием сотрудников в организации. </summary>
internal sealed class OrganizationMembershipService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptions,
    IOrganizationAuditService auditService,
    OrganizationAccess access,
    OrganizationTransaction transaction) : IOrganizationMembershipService
{
    /// <summary> Зависимость доступа к данным приложения. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Зависимость сервиса подписок организации. </summary>
    private readonly IOrganizationSubscriptionService _subscriptions = subscriptions;

    /// <summary> Зависимость сервиса аудита организации. </summary>
    private readonly IOrganizationAuditService _auditService = auditService;

    /// <summary> Зависимость доступа к организации. </summary>
    private readonly OrganizationAccess _access = access;

    /// <summary> Зависимость выполнения операций в транзакции. </summary>
    private readonly OrganizationTransaction _transaction = transaction;

    /// <summary> Передаёт владение организацией участнику. </summary>
    public Task TransferOwnershipAsync(string currentUserId, Guid organizationId, Guid memberId) =>
        this._transaction.ExecuteAuditedAsync(eventId => this.TransferOwnershipCoreAsync(currentUserId, organizationId, memberId, eventId));

    /// <summary> Внутренняя реализация передачи владения организацией. </summary>
    private async Task TransferOwnershipCoreAsync(string currentUserId, Guid organizationId, Guid memberId, Guid eventId)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(currentUserId, organizationId, OrganizationPermissionCodes.MembersRolesManage);
        var organization = await this._access.GetActiveAsync(organizationId);

        OrganizationRules.EnsureOwner(currentUserId, organization);

        var currentOwnerMember = organization.Members.FirstOrDefault(x => x.UserId == currentUserId)
            ?? throw new InvalidOperationException("Текущий владелец не найден в составе участников.");

        var nextOwnerMember = organization.Members.FirstOrDefault(x => x.Id == memberId)
            ?? throw new InvalidOperationException("Новый владелец не найден среди участников.");

        if (nextOwnerMember.UserId == currentUserId)
        {
            throw new InvalidOperationException("Нельзя передать владение самому себе.");
        }

        currentOwnerMember.RoleCode = OrganizationRoleCodes.Admin;
        nextOwnerMember.RoleCode = OrganizationRoleCodes.Owner;
        organization.OwnerUserId = nextOwnerMember.UserId;
        organization.UpdatedAt = DateTime.UtcNow;

        await this._auditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            EventId = eventId,
            OrganizationId = organization.Id,
            ActorUserId = currentUserId,
            EventType = "members.owner.transferred",
            Title = "Владение передано",
            Description = $"Новый владелец: {nextOwnerMember.UserId}",
            TargetType = "member",
            TargetId = nextOwnerMember.Id.ToString("D")
        }, saveChanges: false);
        await this._context.SaveChangesAsync();
    }

    /// <summary> Выполняет выход участника из организации. </summary>
    public Task LeaveOrganizationAsync(string currentUserId, Guid organizationId) =>
        this._transaction.ExecuteAuditedAsync(eventId => this.LeaveOrganizationCoreAsync(currentUserId, organizationId, eventId));

    /// <summary> Внутренняя реализация выхода из организации. </summary>
    private async Task LeaveOrganizationCoreAsync(string currentUserId, Guid organizationId, Guid eventId)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        var organization = await this._access.GetActiveAsync(organizationId);

        var member = organization.Members.FirstOrDefault(x => x.UserId == currentUserId)
            ?? throw new InvalidOperationException("Вы не состоите в этой организации.");

        if (member.RoleCode == OrganizationRoleCodes.Owner || organization.OwnerUserId == currentUserId)
        {
            throw new InvalidOperationException("Владелец не может выйти из организации. Сначала нужно передать владение или удалить организацию отдельно.");
        }

        await this.UnlinkEmployeeAsync(organizationId, member, currentUserId);
        this._context.OrganizationMembers.Remove(member);
        organization.UpdatedAt = DateTime.UtcNow;
        await this._auditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            EventId = eventId,
            OrganizationId = organization.Id,
            ActorUserId = currentUserId,
            EventType = "members.left",
            Title = "Участник вышел",
            Description = currentUserId,
            TargetType = "member",
            TargetId = member.Id.ToString("D")
        }, saveChanges: false);
        await this._context.SaveChangesAsync();
    }

    /// <summary> Изменяет роль участника организации. </summary>
    public Task UpdateMemberRoleAsync(string currentUserId, Guid organizationId, Guid memberId, string roleCode) =>
        this._transaction.ExecuteAuditedAsync(eventId => this.UpdateMemberRoleCoreAsync(currentUserId, organizationId, memberId, roleCode, eventId));

    /// <summary> Внутренняя реализация изменения роли участника. </summary>
    private async Task UpdateMemberRoleCoreAsync(string currentUserId, Guid organizationId, Guid memberId, string roleCode, Guid eventId)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(currentUserId, organizationId, OrganizationPermissionCodes.MembersRolesManage);
        var organization = await this._access.GetForManageAsync(currentUserId, organizationId);

        var member = await this._context.OrganizationMembers
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == memberId)
            ?? throw new InvalidOperationException("Участник не найден.");

        if (member.RoleCode == OrganizationRoleCodes.Owner)
        {
            throw new InvalidOperationException("Роль владельца нельзя изменить.");
        }

        var normalizedRole = OrganizationRules.NormalizeRole(roleCode);
        if (normalizedRole == OrganizationRoleCodes.Owner)
        {
            throw new InvalidOperationException("Передача владения будет отдельным действием.");
        }

        if (member.RoleCode == normalizedRole)
        {
            return;
        }

        await this._context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationEmployees" WHERE "OrganizationId" = {organizationId} AND "MemberId" = {memberId} FOR UPDATE""");

        if (await this._context.MaintenanceWorkOperations.AsNoTracking()
                .AnyAsync(operation => operation.OrganizationId == organizationId
                    && this._context.OrganizationEmployees.Any(employee => employee.Id == operation.EmployeeId
                        && employee.MemberId == memberId)
                    && (operation.Status == MaintenanceWorkStatus.InProgress
                        || operation.Status == MaintenanceWorkStatus.Paused)))
        {
            throw new InvalidOperationException("Сначала завершите активные работы сотрудника, затем измените роль.");
        }

        var previousRole = member.RoleCode;
        member.RoleCode = normalizedRole;
        organization.UpdatedAt = DateTime.UtcNow;
        await this._auditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            EventId = eventId,
            OrganizationId = organization.Id,
            ActorUserId = currentUserId,
            EventType = "members.role.updated",
            Title = "Роль участника изменена",
            Description = $"{OrganizationRules.GetRoleName(previousRole)} -> {OrganizationRules.GetRoleName(normalizedRole)}",
            TargetType = "member",
            TargetId = member.Id.ToString("D")
        }, saveChanges: false);
        await this._context.SaveChangesAsync();
    }

    /// <summary> Удаляет участника из организации. </summary>
    public Task RemoveMemberAsync(string currentUserId, Guid organizationId, Guid memberId) =>
        this._transaction.ExecuteAuditedAsync(eventId => this.RemoveMemberCoreAsync(currentUserId, organizationId, memberId, eventId));

    /// <summary> Внутренняя реализация удаления участника. </summary>
    private async Task RemoveMemberCoreAsync(string currentUserId, Guid organizationId, Guid memberId, Guid eventId)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(currentUserId, organizationId, OrganizationPermissionCodes.MembersRemove);
        var organization = await this._access.GetForManageAsync(currentUserId, organizationId);
        var member = await this._context.OrganizationMembers
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == memberId)
            ?? throw new InvalidOperationException("Участник не найден.");

        if (member.RoleCode == OrganizationRoleCodes.Owner || member.UserId == organization.OwnerUserId)
        {
            throw new InvalidOperationException("Владельца нельзя удалить из организации.");
        }

        await this.UnlinkEmployeeAsync(organizationId, member, currentUserId);
        this._context.OrganizationMembers.Remove(member);
        organization.UpdatedAt = DateTime.UtcNow;
        await this._auditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            EventId = eventId,
            OrganizationId = organization.Id,
            ActorUserId = currentUserId,
            EventType = "members.removed",
            Title = "Участник удален",
            Description = $"{member.UserId}, роль: {OrganizationRules.GetRoleName(member.RoleCode)}",
            TargetType = "member",
            TargetId = member.Id.ToString("D")
        }, saveChanges: false);
        await this._context.SaveChangesAsync();
    }

    /// <summary> Отвязывает сотрудника от участника организации. </summary>
    private async Task UnlinkEmployeeAsync(Guid organizationId, OrganizationMember member, string actorUserId)
    {
        var employee = await this._context.OrganizationEmployees
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.MemberId == member.Id);
        if (employee == null)
        {
            return;
        }

        await this._context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationEmployees" WHERE "Id" = {employee.Id} FOR UPDATE""");
        await this._context.Entry(employee).ReloadAsync();

        if (this._context.Entry(employee).State == EntityState.Detached || employee.MemberId != member.Id)
        {
            return;
        }

        if (await this._context.MaintenanceWorkOperations.AsNoTracking()
            .AnyAsync(operation => operation.OrganizationId == organizationId
                && operation.EmployeeId == employee.Id
                && (operation.Status == MaintenanceWorkStatus.InProgress
                    || operation.Status == MaintenanceWorkStatus.Paused)))
        {
            throw new InvalidOperationException("Сначала завершите активные работы сотрудника, затем удалите участника.");
        }

        employee.MemberId = null;
        await this._auditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            EventType = "employees.member.unlinked",
            Title = "Связь сотрудника с участником снята",
            Description = $"Участник {member.Id:D} удалён из организации.",
            TargetType = "employee",
            TargetId = employee.Id.ToString("D")
        }, saveChanges: false);
    }
}
