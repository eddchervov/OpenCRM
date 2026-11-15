using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Rules;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Invitations;

/// <summary> Сервис команд управления приглашениями в организацию. </summary>
internal sealed class OrganizationInvitationCommandService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptions,
    IOrganizationAuditService auditService,
    OrganizationAccess access,
    OrganizationTransaction transaction,
    OrganizationInvitationNotifications notifications,
    OrganizationInvitationEmail email) : IOrganizationInvitationCommandService
{
    /// <summary> Принимает приглашение в организацию. </summary>
    public Task AcceptInvitationAsync(string userId, Guid invitationId) =>
        transaction.ExecuteAsync(
            () => this.AcceptInvitationCoreAsync(userId, invitationId),
            () => context.OrganizationInvitations.AsNoTracking().AnyAsync(invitation =>
                invitation.Id == invitationId
                && invitation.StatusCode == OrganizationInvitationStatusCodes.Accepted
                && invitation.AcceptedByUserId == userId));

    /// <summary> Внутренняя реализация принятия приглашения. </summary>
    private async Task AcceptInvitationCoreAsync(string userId, Guid invitationId)
    {
        var organizationId = await this.GetInvitationOrganizationIdAsync(invitationId);
        await transaction.LockOrganizationAsync(organizationId);

        var user = await context.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == userId)
            ?? throw new InvalidOperationException("Пользователь не найден.");
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException("Для принятия приглашения у аккаунта должен быть указан email.");
        }

        if (!user.EmailConfirmed)
        {
            throw new InvalidOperationException("Подтвердите email аккаунта, прежде чем принимать приглашение.");
        }

        var normalizedEmail = OrganizationRules.NormalizeEmail(user.Email);
        var invitation = await context.OrganizationInvitations
            .Include(value => value.Organization)
            .FirstOrDefaultAsync(value => value.Id == invitationId && value.OrganizationId == organizationId)
            ?? throw new InvalidOperationException("Приглашение не найдено или уже недоступно.");
        await this.LockInvitationAsync(invitation.Id);
        await context.Entry(invitation).ReloadAsync();
        var organization = await this.GetCurrentInvitationOrganizationAsync(invitation.Organization);
        if (invitation.StatusCode != OrganizationInvitationStatusCodes.Pending
            || invitation.NormalizedEmail != normalizedEmail
            || organization.IsDeleted
            || !organization.IsActive
            || organization.IsArchived)
        {
            throw new InvalidOperationException("Приглашение не найдено или уже недоступно.");
        }

        var member = await context.OrganizationMembers
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId && value.UserId == userId);
        var now = DateTime.UtcNow;
        if (member == null)
        {
            member = new OrganizationMember
            {
                OrganizationId = organizationId,
                UserId = userId,
                RoleCode = OrganizationRules.NormalizeRole(invitation.RoleCode),
                InvitedByUserId = invitation.InvitedByUserId,
                CreatedAt = now,
                JoinedAt = now
            };
            context.OrganizationMembers.Add(member);
        }

        if (invitation.EmployeeId.HasValue)
        {
            var employee = await context.OrganizationEmployees
                .SingleOrDefaultAsync(value => value.Id == invitation.EmployeeId.Value
                    && value.OrganizationId == organizationId)
                ?? throw new InvalidOperationException("Карточка сотрудника из приглашения не найдена.");
            await this.LockEmployeeAsync(employee.Id);
            await context.Entry(employee).ReloadAsync();

            if (employee.MemberId.HasValue && employee.MemberId.Value != member.Id)
            {
                throw new InvalidOperationException("Карточка сотрудника уже связана с другим участником.");
            }

            if (await context.OrganizationEmployees.AnyAsync(value =>
                value.OrganizationId == organizationId
                && value.Id != employee.Id
                && value.MemberId == member.Id))
            {
                throw new InvalidOperationException("Аккаунт уже связан с другой карточкой сотрудника.");
            }

            employee.MemberId = member.Id;
        }

        invitation.StatusCode = OrganizationInvitationStatusCodes.Accepted;
        invitation.AcceptedByUserId = userId;
        invitation.AcceptedAt = now;
        organization.UpdatedAt = now;

        await notifications.AddAcceptedAsync(invitation.InvitedByUserId, invitation.Id, organization.Name, user.Email);
        await notifications.CloseAsync(invitation.Id);
        await context.SaveChangesAsync();
    }

    /// <summary> Отклоняет приглашение в организацию. </summary>
    public Task DeclineInvitationAsync(string userId, Guid invitationId) =>
        transaction.ExecuteAsync(
            () => this.DeclineInvitationCoreAsync(userId, invitationId),
            () => context.OrganizationInvitations.AsNoTracking().AnyAsync(invitation =>
                invitation.Id == invitationId && invitation.StatusCode == OrganizationInvitationStatusCodes.Declined));

    /// <summary> Внутренняя реализация отклонения приглашения. </summary>
    private async Task DeclineInvitationCoreAsync(string userId, Guid invitationId)
    {
        var organizationId = await this.GetInvitationOrganizationIdAsync(invitationId);
        await transaction.LockOrganizationAsync(organizationId);

        var user = await context.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == userId)
            ?? throw new InvalidOperationException("Пользователь не найден.");
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException("Для отклонения приглашения у аккаунта должен быть указан email.");
        }

        if (!user.EmailConfirmed)
        {
            throw new InvalidOperationException("Подтвердите email аккаунта, прежде чем отклонять приглашение.");
        }

        var normalizedEmail = OrganizationRules.NormalizeEmail(user.Email);
        var invitation = await context.OrganizationInvitations
            .Include(value => value.Organization)
            .FirstOrDefaultAsync(value => value.Id == invitationId && value.OrganizationId == organizationId)
            ?? throw new InvalidOperationException("Приглашение не найдено или уже недоступно.");
        await this.LockInvitationAsync(invitation.Id);
        await context.Entry(invitation).ReloadAsync();
        var organization = await this.GetCurrentInvitationOrganizationAsync(invitation.Organization);
        if (invitation.StatusCode != OrganizationInvitationStatusCodes.Pending
            || invitation.NormalizedEmail != normalizedEmail
            || organization.IsDeleted
            || !organization.IsActive
            || organization.IsArchived)
        {
            throw new InvalidOperationException("Приглашение не найдено или уже недоступно.");
        }

        var now = DateTime.UtcNow;
        invitation.StatusCode = OrganizationInvitationStatusCodes.Declined;
        invitation.RevokedAt = now;
        organization.UpdatedAt = now;
        await notifications.CloseAsync(invitation.Id);
        await context.SaveChangesAsync();
    }

    /// <summary> Приглашает участника в организацию. </summary>
    public Task InviteMemberAsync(string currentUserId, Guid organizationId, InviteOrganizationMemberDto dto)
    {
        var invitationToken = Guid.NewGuid().ToString("N");

        return transaction.ExecuteAuditedAsync(eventId =>
            this.InviteMemberCoreAsync(currentUserId, organizationId, dto, invitationToken, eventId));
    }

    /// <summary> Внутренняя реализация приглашения участника. </summary>
    private async Task InviteMemberCoreAsync(
        string currentUserId,
        Guid organizationId,
        InviteOrganizationMemberDto dto,
        string invitationToken,
        Guid eventId,
        Guid? newInvitationId = null)
    {
        await transaction.LockOrganizationAsync(organizationId);
        await subscriptions.EnsureWorkspaceCanWriteAsync(
            currentUserId,
            organizationId,
            OrganizationPermissionCodes.MembersInvite);
        var organization = await access.GetForManageAsync(currentUserId, organizationId);
        var roleCode = OrganizationRules.NormalizeRole(dto.RoleCode);
        if (roleCode == OrganizationRoleCodes.Owner)
        {
            throw new InvalidOperationException("Роль владельца нельзя выдавать через приглашение.");
        }

        var emailAddress = OrganizationRules.NormalizeRequired(dto.Email, "Email участника обязателен.");
        var normalizedEmail = OrganizationRules.NormalizeEmail(emailAddress);
        var existingUser = await context.ApplicationUsers
            .FirstOrDefaultAsync(value => value.NormalizedEmail == normalizedEmail
                || value.Email!.ToUpper() == normalizedEmail);
        if (existingUser != null && await context.OrganizationMembers
            .AnyAsync(value => value.OrganizationId == organization.Id && value.UserId == existingUser.Id))
        {
            throw new InvalidOperationException("Этот пользователь уже есть в организации.");
        }

        var pendingInvitation = await context.OrganizationInvitations
            .FirstOrDefaultAsync(value => value.OrganizationId == organization.Id
                && value.NormalizedEmail == normalizedEmail
                && value.StatusCode == OrganizationInvitationStatusCodes.Pending);
        if (pendingInvitation != null)
        {
            await this.LockInvitationAsync(pendingInvitation.Id);
            await context.Entry(pendingInvitation).ReloadAsync();
            if (pendingInvitation.StatusCode != OrganizationInvitationStatusCodes.Pending)
            {
                throw new InvalidOperationException("Приглашение уже изменено. Обновите список.");
            }
        }

        if (dto.EmployeeId.HasValue)
        {
            var employee = await context.OrganizationEmployees
                .SingleOrDefaultAsync(value => value.Id == dto.EmployeeId.Value
                    && value.OrganizationId == organization.Id)
                ?? throw new InvalidOperationException("Карточка сотрудника не найдена в этой организации.");
            await this.LockEmployeeAsync(employee.Id);
            await context.Entry(employee).ReloadAsync();
            if (employee.MemberId.HasValue)
            {
                throw new InvalidOperationException("Для приглашения выберите карточку без связанного аккаунта.");
            }
        }

        var pendingInvitationId = pendingInvitation?.Id ?? Guid.Empty;
        var invitationId = pendingInvitation?.Id ?? newInvitationId ?? Guid.NewGuid();
        if (dto.EmployeeId.HasValue && await context.OrganizationInvitations.AnyAsync(value =>
            value.OrganizationId == organization.Id
            && value.EmployeeId == dto.EmployeeId
            && value.StatusCode == OrganizationInvitationStatusCodes.Pending
            && value.Id != pendingInvitationId))
        {
            throw new InvalidOperationException("Для этой карточки уже ожидается приглашение.");
        }

        if (pendingInvitation != null)
        {
            pendingInvitation.RoleCode = roleCode;
            pendingInvitation.Email = emailAddress;
            pendingInvitation.InvitedByUserId = currentUserId;
            pendingInvitation.EmployeeId = dto.EmployeeId;
            pendingInvitation.Token = invitationToken;
        }
        else
        {
            var membersLimit = await subscriptions.GetMembersLimitAsync(currentUserId, organizationId, Helpers.PlatformDefaults.WorkspaceMembersLimit);

            // Нулевой лимит означает отсутствие ограничения на число участников.
            if (membersLimit <= 0)
            {
                membersLimit = int.MaxValue;
            }
            var plannedMembersCount = organization.Members.Count
                + organization.Invitations.Count(value => value.StatusCode == OrganizationInvitationStatusCodes.Pending);
            if (plannedMembersCount >= membersLimit)
            {
                throw new InvalidOperationException($"Лимит участников workspace исчерпан. Доступно: {membersLimit}.");
            }

            context.OrganizationInvitations.Add(new OrganizationInvitation
            {
                Id = invitationId,
                OrganizationId = organization.Id,
                Email = emailAddress,
                NormalizedEmail = normalizedEmail,
                RoleCode = roleCode,
                EmployeeId = dto.EmployeeId,
                Token = invitationToken,
                StatusCode = OrganizationInvitationStatusCodes.Pending,
                InvitedByUserId = currentUserId,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (existingUser != null)
        {
            var invitationNotificationKey = $"invitation:{invitationId}:{invitationToken}";
            await context.SiteNotifications
                .Where(notification => notification.TypeCode == SiteNotificationTypes.OrganizationInvitation
                    && notification.RecipientUserId == existingUser.Id
                    && notification.EventKey != invitationNotificationKey
                    && notification.EventKey != null
                    && notification.EventKey.StartsWith($"invitation:{invitationId}:")
                    && !notification.IsDeleted)
                .ExecuteUpdateAsync(update => update.SetProperty(notification => notification.IsDeleted, true));

            await notifications.AddInvitationAsync(
                existingUser.Id,
                invitationId,
                invitationToken,
                organization.Name);
        }
        else
        {
            await email.QueueAsync(emailAddress, organization.Name, invitationId, invitationToken);
        }

        organization.UpdatedAt = DateTime.UtcNow;
        await auditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            EventId = eventId,
            OrganizationId = organization.Id,
            ActorUserId = currentUserId,
            EventType = pendingInvitation != null ? "members.invitation.updated" : "members.invitation.created",
            Title = pendingInvitation != null ? "Приглашение обновлено" : "Участник приглашен",
            Description = $"{emailAddress}, роль: {OrganizationRules.GetRoleName(roleCode)}",
            TargetType = "invitation",
            TargetId = normalizedEmail
        }, saveChanges: false);
        await context.SaveChangesAsync();
    }

    /// <summary> Создаёт сотрудника и приглашает его в организацию. </summary>
    public async Task<CreateOrganizationEmployeeInvitationResultDto> CreateEmployeeAndInviteAsync(
        string currentUserId,
        Guid organizationId,
        CreateOrganizationEmployeeInvitationDto dto)
    {
        var employeeId = Guid.NewGuid();
        var invitationToken = Guid.NewGuid().ToString("N");
        var invitationId = Guid.NewGuid();
        await transaction.ExecuteAuditedAsync(
            async eventId =>
            {
                await transaction.LockOrganizationAsync(organizationId);
                await subscriptions.EnsureWorkspaceCanWriteAsync(
                    currentUserId,
                    organizationId,
                    OrganizationPermissionCodes.EmployeesManage);
                await access.GetForManageAsync(currentUserId, organizationId);

                var name = OrganizationRules.NormalizeRequired(dto.Name, "Укажите имя сотрудника.");
                var phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
                if (name.Length > 250 || phone?.Length > 50)
                {
                    throw new InvalidOperationException(
                        "Имя сотрудника должно быть не длиннее 250 символов, телефон — 50 символов.");
                }

                var emailAddress = OrganizationRules.NormalizeRequired(dto.Email, "Email участника обязателен.");
                var normalizedEmail = OrganizationRules.NormalizeEmail(emailAddress);
                if (await context.OrganizationInvitations.AnyAsync(invitation =>
                    invitation.OrganizationId == organizationId
                    && invitation.NormalizedEmail == normalizedEmail
                    && invitation.StatusCode == OrganizationInvitationStatusCodes.Pending))
                {
                    throw new InvalidOperationException("Для этого email уже ожидается приглашение.");
                }

                var employee = new OrganizationEmployee
                {
                    Id = employeeId,
                    OrganizationId = organizationId,
                    Name = name,
                    Phone = phone,
                    IsActive = dto.IsActive
                };
                context.OrganizationEmployees.Add(employee);
                context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
                {
                    OrganizationId = organizationId,
                    ActorUserId = currentUserId,
                    EventType = "employees.created",
                    Title = "Сотрудник добавлен",
                    Description = name,
                    TargetType = "employee",
                    TargetId = employeeId.ToString("D")
                });
                await context.SaveChangesAsync();

                await this.InviteMemberCoreAsync(
                    currentUserId,
                    organizationId,
                    new InviteOrganizationMemberDto
                    {
                        Email = emailAddress,
                        RoleCode = dto.RoleCode,
                        EmployeeId = employeeId
                    },
                    invitationToken,
                    eventId,
                    invitationId);
            });

        return new CreateOrganizationEmployeeInvitationResultDto(employeeId, invitationId);
    }

    /// <summary> Отзывает приглашение в организацию. </summary>
    public Task RevokeInvitationAsync(string currentUserId, Guid organizationId, Guid invitationId) =>
        transaction.ExecuteAuditedAsync(eventId =>
            this.RevokeInvitationCoreAsync(currentUserId, organizationId, invitationId, eventId));

    /// <summary> Внутренняя реализация отзыва приглашения. </summary>
    private async Task RevokeInvitationCoreAsync(
        string currentUserId,
        Guid organizationId,
        Guid invitationId,
        Guid auditEventId)
    {
        await transaction.LockOrganizationAsync(organizationId);
        await subscriptions.EnsureWorkspaceCanWriteAsync(
            currentUserId,
            organizationId,
            OrganizationPermissionCodes.MembersInvite);
        var organization = await access.GetForManageAsync(currentUserId, organizationId);
        var invitation = await context.OrganizationInvitations
            .FirstOrDefaultAsync(value => value.OrganizationId == organizationId && value.Id == invitationId)
            ?? throw new InvalidOperationException("Ожидающее приглашение не найдено.");
        await this.LockInvitationAsync(invitation.Id);
        await context.Entry(invitation).ReloadAsync();
        if (invitation.StatusCode != OrganizationInvitationStatusCodes.Pending)
        {
            throw new InvalidOperationException("Приглашение уже изменено. Обновите список.");
        }

        invitation.StatusCode = OrganizationInvitationStatusCodes.Revoked;
        invitation.RevokedAt = DateTime.UtcNow;
        organization.UpdatedAt = DateTime.UtcNow;
        await auditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            EventId = auditEventId,
            OrganizationId = organization.Id,
            ActorUserId = currentUserId,
            EventType = "members.invitation.revoked",
            Title = "Приглашение отозвано",
            Description = invitation.Email,
            TargetType = "invitation",
            TargetId = invitation.Id.ToString("D")
        }, saveChanges: false);
        await notifications.CloseAsync(invitation.Id);
        await context.SaveChangesAsync();
    }

    /// <summary> Возвращает идентификатор организации приглашения. </summary>
    private async Task<Guid> GetInvitationOrganizationIdAsync(Guid invitationId)
    {
        var organizationId = await context.OrganizationInvitations
            .AsNoTracking()
            .Where(invitation => invitation.Id == invitationId)
            .Select(invitation => invitation.OrganizationId)
            .FirstOrDefaultAsync();
        if (organizationId == Guid.Empty)
        {
            throw new InvalidOperationException("Приглашение не найдено или уже недоступно.");
        }

        return organizationId;
    }

    /// <summary> Возвращает актуальную организацию приглашения. </summary>
    private async Task<Organization> GetCurrentInvitationOrganizationAsync(Organization organization)
    {
        await context.Entry(organization).ReloadAsync();

        return organization;
    }

    /// <summary> Блокирует приглашение для конкурентных операций. </summary>
    private Task LockInvitationAsync(Guid invitationId) => context.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT 1 FROM \"OrganizationInvitations\" WHERE \"Id\" = {invitationId} FOR UPDATE");

    /// <summary> Блокирует сотрудника для конкурентных операций. </summary>
    private Task LockEmployeeAsync(Guid employeeId) => context.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT 1 FROM \"OrganizationEmployees\" WHERE \"Id\" = {employeeId} FOR UPDATE");
}
