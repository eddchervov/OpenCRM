using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations;

/// <summary> Сервис управления сотрудниками организации. </summary>
public class OrganizationEmployeeService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptionService,
    IOrganizationPermissionService permissionService) : IOrganizationEmployeeService
{
    /// <summary> Возвращает варианты участников для привязки к сотрудникам. </summary>
    public async Task<IReadOnlyList<OrganizationEmployeeMemberOptionDto>> GetMemberOptionsAsync(string userId, Guid organizationId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.EmployeesManage);
        await subscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.MembersRolesManage);

        return await context.OrganizationMembers.AsNoTracking()
            .Where(member => member.OrganizationId == organizationId)
            .OrderBy(member => member.User.UserName)
            .Select(member => new OrganizationEmployeeMemberOptionDto(member.Id, member.User.Email, member.User.UserName))
            .ToListAsync();
    }

    /// <summary> Возвращает список сотрудников организации. </summary>
    public async Task<IReadOnlyList<OrganizationEmployeeDto>> GetAsync(string userId, Guid organizationId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceView);
        var canManageEmployees = await permissionService.HasPermissionAsync(
            userId, organizationId, OrganizationPermissionCodes.EmployeesManage);
        var canEditMaintenance = await permissionService.HasPermissionAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);
        if (!canManageEmployees && !canEditMaintenance)
        {
            throw new UnauthorizedAccessException("Нет доступа к сотрудникам организации.");
        }

        var canSeeMemberLinks = await permissionService.HasPermissionAsync(
            userId, organizationId, OrganizationPermissionCodes.MembersView);

        return await context.OrganizationEmployees.AsNoTracking()
            .Where(employee => employee.OrganizationId == organizationId)
            .OrderByDescending(employee => employee.IsActive).ThenBy(employee => employee.Name).ThenBy(employee => employee.Id)
            .Select(employee => new OrganizationEmployeeDto
            {
                Id = employee.Id, Name = employee.Name,
                Phone = canManageEmployees ? employee.Phone : null,
                MemberId = canSeeMemberLinks ? employee.MemberId : null,
                IsActive = employee.IsActive, Version = employee.Version
            }).ToListAsync();
    }

    /// <summary> Сохраняет сотрудника организации или создаёт нового. </summary>
    public async Task<Guid> SaveAsync(string userId, Guid organizationId, Guid? employeeId, OrganizationEmployeeDto dto)
    {
        var savedEmployeeId = employeeId ?? Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        await this.ExecuteEmployeeTransactionAsync(
            () => this.SaveEmployeeAsync(userId, organizationId, employeeId, savedEmployeeId, dto, auditEventId),
            () => context.OrganizationAuditEvents.AsNoTracking().AnyAsync(item => item.Id == auditEventId));

        return savedEmployeeId;
    }

    /// <summary> Внутренняя реализация сохранения сотрудника. </summary>
    private async Task SaveEmployeeAsync(
        string userId,
        Guid organizationId,
        Guid? employeeId,
        Guid savedEmployeeId,
        OrganizationEmployeeDto dto,
        Guid auditEventId)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.EmployeesManage);
        var name = dto.Name?.Trim() ?? string.Empty;
        var phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
        if (name.Length == 0 || name.Length > 250 || phone?.Length > 50)
        {
            throw new InvalidOperationException("Укажите имя сотрудника до 250 символов и телефон до 50 символов.");
        }

        var employee = employeeId.HasValue
            ? await context.OrganizationEmployees.SingleOrDefaultAsync(item => item.Id == employeeId && item.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Сотрудник не найден.")
            : new OrganizationEmployee { Id = savedEmployeeId, OrganizationId = organizationId };

        if (employeeId.HasValue)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "OrganizationEmployees" WHERE "Id" = {employeeId.Value} FOR UPDATE""");
            await context.Entry(employee).ReloadAsync();
        }

        if (employeeId.HasValue && employee.Version != dto.Version)
        {
            throw new InvalidOperationException("Сотрудник уже изменён. Обновите список перед сохранением.");
        }

        if (employeeId.HasValue && employee.IsActive && !dto.IsActive
            && await context.MaintenanceWorkOperations.AsNoTracking()
                .AnyAsync(operation => operation.OrganizationId == organizationId
                    && operation.EmployeeId == employeeId.Value
                    && (operation.Status == MaintenanceWorkStatus.InProgress
                        || operation.Status == MaintenanceWorkStatus.Paused)))
        {
            throw new InvalidOperationException(
                "Сначала завершите активные работы сотрудника, затем отключите назначение новых работ.");
        }

        var description = employeeId.HasValue
            ? $"Имя: {employee.Name} → {name}; телефон: {employee.Phone ?? "—"} → {phone ?? "—"}; активен: {employee.IsActive} → {dto.IsActive}."
            : name;
        employee.Name = name;
        employee.Phone = phone;
        employee.IsActive = dto.IsActive;
        if (!employeeId.HasValue)
        {
            context.OrganizationEmployees.Add(employee);
        }
        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = auditEventId,
            OrganizationId = organizationId, ActorUserId = userId,
            EventType = employeeId.HasValue ? "employees.updated" : "employees.created",
            Title = employeeId.HasValue ? "Сотрудник изменён" : "Сотрудник добавлен",
            Description = description, TargetType = "employee", TargetId = employee.Id.ToString()
        });
        await context.SaveChangesAsync();
        if (dto.MemberIdToLink.HasValue)
        {
            await this.LinkEmployeeMemberAsync(
                userId, organizationId, employee.Id, dto.MemberIdToLink.Value, employee.Version, Guid.NewGuid());
        }
    }

    /// <summary> Создаёт карточку сотрудника для участника организации. </summary>
    public async Task<Guid> CreateForMemberAsync(
        string userId,
        Guid organizationId,
        Guid memberId,
        string name,
        string? phone,
        bool isActive = true)
    {
        var employeeId = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        await this.ExecuteEmployeeTransactionAsync(
            () => this.CreateEmployeeForMemberAsync(
                userId, organizationId, memberId, employeeId, name, phone, isActive, auditEventId),
            () => context.OrganizationAuditEvents.AsNoTracking().AnyAsync(item => item.Id == auditEventId));

        return employeeId;
    }

    /// <summary> Внутренняя реализация создания сотрудника для участника. </summary>
    private async Task CreateEmployeeForMemberAsync(
        string userId,
        Guid organizationId,
        Guid memberId,
        Guid employeeId,
        string name,
        string? phone,
        bool isActive,
        Guid auditEventId)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.EmployeesManage);
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MembersRolesManage);

        name = name?.Trim() ?? string.Empty;
        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (name.Length == 0 || name.Length > 250 || phone?.Length > 50)
        {
            throw new InvalidOperationException("Укажите имя сотрудника до 250 символов и телефон до 50 символов.");
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationMembers" WHERE "Id" = {memberId} AND "OrganizationId" = {organizationId} FOR UPDATE""");
        var memberExists = await context.OrganizationMembers.AsNoTracking()
            .AnyAsync(member => member.Id == memberId && member.OrganizationId == organizationId);
        if (!memberExists)
        {
            throw new KeyNotFoundException("Участник этой организации не найден.");
        }

        if (await context.OrganizationEmployees.AnyAsync(employee => employee.MemberId == memberId))
        {
            throw new DbUpdateConcurrencyException("Участник уже связан с карточкой сотрудника.");
        }

        context.OrganizationEmployees.Add(new OrganizationEmployee
        {
            Id = employeeId,
            OrganizationId = organizationId,
            MemberId = memberId,
            Name = name,
            Phone = phone,
            IsActive = isActive
        });
        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = auditEventId,
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = "employees.created_from_member",
            Title = "Карточка сотрудника создана для участника",
            Description = $"Участник: {memberId:D}; имя: {name}.",
            TargetType = "employee",
            TargetId = employeeId.ToString("D")
        });
        await context.SaveChangesAsync();
    }

    /// <summary> Привязывает участника к сотруднику организации. </summary>
    public async Task LinkMemberAsync(string userId, Guid organizationId, Guid employeeId, Guid? memberId, uint version)
    {
        var auditEventId = Guid.NewGuid();
        var isUnchanged = false;
        await this.ExecuteEmployeeTransactionAsync(
            async () => isUnchanged = await this.LinkEmployeeMemberAsync(userId, organizationId, employeeId, memberId, version, auditEventId),
            () => isUnchanged
                ? Task.FromResult(true)
                : context.OrganizationAuditEvents.AsNoTracking().AnyAsync(item => item.Id == auditEventId));
    }

    /// <summary> Внутренняя реализация привязки участника к сотруднику. </summary>
    private async Task<bool> LinkEmployeeMemberAsync(
        string userId,
        Guid organizationId,
        Guid employeeId,
        Guid? memberId,
        uint version,
        Guid auditEventId)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.EmployeesManage);
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MembersRolesManage);

        var employee = await context.OrganizationEmployees
            .SingleOrDefaultAsync(item => item.Id == employeeId && item.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("Сотрудник не найден.");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationEmployees" WHERE "Id" = {employeeId} FOR UPDATE""");
        await context.Entry(employee).ReloadAsync();

        if (employee.Version != version)
        {
            throw new DbUpdateConcurrencyException("Сотрудник уже изменён. Обновите список перед сохранением.");
        }

        if (memberId.HasValue)
        {
            if (await context.OrganizationInvitations.AnyAsync(invitation =>
                invitation.OrganizationId == organizationId
                && invitation.EmployeeId == employeeId
                && invitation.StatusCode == OrganizationInvitationStatusCodes.Pending))
            {
                throw new InvalidOperationException("Для карточки уже ожидается приглашение. Сначала отмените его.");
            }

            var memberUserId = await context.OrganizationMembers.AsNoTracking()
                .Where(item => item.Id == memberId && item.OrganizationId == organizationId)
                .Select(item => item.UserId)
                .SingleOrDefaultAsync();
            if (memberUserId is null)
            {
                throw new InvalidOperationException("Участник этой организации не найден.");
            }

            var alreadyLinked = await context.OrganizationEmployees
                .AnyAsync(item => item.MemberId == memberId && item.Id != employeeId);
            if (alreadyLinked)
            {
                throw new InvalidOperationException("Участник уже связан с другим сотрудником.");
            }
        }

        if (employee.MemberId == memberId)
        {
            return true;
        }

        if (employee.MemberId.HasValue && await context.MaintenanceWorkOperations.AsNoTracking()
            .AnyAsync(operation => operation.OrganizationId == organizationId
                && operation.EmployeeId == employeeId
                && (operation.Status == MaintenanceWorkStatus.InProgress
                    || operation.Status == MaintenanceWorkStatus.Paused)))
        {
            throw new InvalidOperationException("Сначала завершите активные работы сотрудника, затем измените привязку аккаунта.");
        }

        var previousMemberId = employee.MemberId;
        employee.MemberId = memberId;
        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = auditEventId,
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = memberId.HasValue ? "employees.member.linked" : "employees.member.unlinked",
            Title = memberId.HasValue ? "Доступ сотрудника связан с участником" : "Связь сотрудника с участником снята",
            Description = $"Участник: {previousMemberId?.ToString() ?? "—"} → {memberId?.ToString() ?? "—"}.",
            TargetType = "employee",
            TargetId = employee.Id.ToString("D")
        });
        await context.SaveChangesAsync();

        return false;
    }

    /// <summary> Выполняет операцию в транзакции с проверкой результата. </summary>
    private async Task ExecuteEmployeeTransactionAsync(Func<Task> action, Func<Task<bool>> verifySucceeded)
    {
        if (context.Database.CurrentTransaction != null)
        {
            await action();

            return;
        }

        var isRetry = false;
        await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(async () =>
        {
            if (isRetry)
            {
                context.ChangeTracker.Clear();
            }

            isRetry = true;
            await action();
        }, verifySucceeded);
    }
}
