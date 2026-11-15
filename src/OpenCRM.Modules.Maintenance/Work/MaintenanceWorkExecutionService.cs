using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Loading;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Work;

/// <summary> Сервис выполнения работ по строкам обслуживания. </summary>
internal sealed partial class MaintenanceWorkExecutionService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptionService,
    ISystemClock systemClock) : IMaintenanceWorkExecutionService
{
    /// <summary> Зависимость для получения сотрудника организации. </summary>
    private readonly MaintenanceWorkEmployeeAccess _employeeAccess = new(context);

    /// <summary> Зависимость для выполнения операций в транзакции. </summary>
    private readonly MaintenanceTransaction _transaction = new(context);

    /// <summary> Возвращает строку работы на доработку. </summary>
    public async Task ReturnForReworkAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        MaintenanceWorkCommandDto command)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);
        var reason = command.Reason?.Trim();
        if (command.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
        {
            throw new InvalidOperationException("Укажите причину возврата работы не длиннее 1000 символов.");
        }

        await this._transaction.ExecuteAsync(
            () => this.ReturnForReworkCoreAsync(userId, organizationId, maintenanceId, detailId, command, reason),
            () => context.MaintenanceWorkEvents.AsNoTracking().AnyAsync(item => item.CommandId == command.CommandId));
    }

    /// <summary> Внутренняя реализация возврата работы на доработку. </summary>
    private async Task ReturnForReworkCoreAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        MaintenanceWorkCommandDto command,
        string reason
    )
    {
        var maintenance = await context.Maintenance
            .SingleOrDefaultAsync(item => item.Id == maintenanceId
                && item.Vehicle.OrganizationId == organizationId && !item.IsDeleted)
            ?? throw new KeyNotFoundException("Обслуживание не найдено.");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenanceId} FOR UPDATE""");
        await context.Entry(maintenance).ReloadAsync();
        if (maintenance.IsDeleted)
        {
            throw new KeyNotFoundException("Обслуживание не найдено.");
        }

        await MaintenanceDetailsLoader.ReloadAsync(context, maintenance);
        if (maintenance.Status != MaintenanceStatus.InProgress)
        {
            throw new InvalidOperationException("Вернуть работу можно только до подтверждения готовности.");
        }

        var operation = await context.MaintenanceWorkOperations
            .SingleOrDefaultAsync(item => item.Id == detailId && item.MaintenanceId == maintenanceId)
            ?? throw new KeyNotFoundException("Работа не найдена.");
        var previousCommand = await context.MaintenanceWorkEvents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CommandId == command.CommandId);
        if (previousCommand != null)
        {
            if (previousCommand.OperationId != detailId || previousCommand.ActorUserId != userId
                || previousCommand.PreviousStatus != MaintenanceWorkStatus.Completed
                || previousCommand.Status != MaintenanceWorkStatus.Assigned
                || previousCommand.PreviousVersion != command.Version
                || previousCommand.Reason != reason)
            {
                throw new InvalidOperationException("Идентификатор запроса уже использован для другого действия.");
            }

            return;
        }

        if (operation.Status != MaintenanceWorkStatus.Completed || operation.Version != command.Version)
        {
            throw new DbUpdateConcurrencyException("Работа уже изменена. Обновите карточку.");
        }

        var memberId = await context.OrganizationMembers.AsNoTracking()
            .Where(member => member.OrganizationId == organizationId && member.UserId == userId)
            .Select(member => member.Id)
            .SingleAsync();
        var previousVersion = operation.Version;
        operation.Status = MaintenanceWorkStatus.Assigned;
        operation.CompletedAt = null;
        var workEvent = new MaintenanceWorkEvent
        {
            OperationId = detailId,
            CommandId = command.CommandId,
            OrganizationId = organizationId,
            MaintenanceId = maintenanceId,
            EmployeeId = operation.EmployeeId,
            MemberId = memberId,
            ActorUserId = userId,
            PreviousStatus = MaintenanceWorkStatus.Completed,
            Status = MaintenanceWorkStatus.Assigned,
            CreatedAt = systemClock.UtcNow,
            Reason = reason,
            PreviousVersion = previousVersion
        };
        context.MaintenanceWorkEvents.Add(workEvent);
        await context.SaveChangesAsync();
        workEvent.ResultVersion = operation.Version;
        await context.SaveChangesAsync();
    }

    /// <summary> Назначает строку работы сотруднику. </summary>
    public async Task<MaintenanceWorkItemDto> ClaimAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkExecute);
        var employee = await this._employeeAccess.GetEmployeeAsync(userId, organizationId);
        var auditEventId = Guid.NewGuid();
        return await this._transaction.ExecuteAsync(
            () => this.ClaimCoreAsync(userId, organizationId, maintenanceId, detailId, employee, auditEventId),
            () => context.OrganizationAuditEvents.AsNoTracking().AnyAsync(item => item.Id == auditEventId));
    }

    /// <summary> Внутренняя реализация назначения строки работы сотруднику. </summary>
    private async Task<MaintenanceWorkItemDto> ClaimCoreAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        OrganizationEmployee employee,
        Guid auditEventId
    )
    {
        var maintenance = await context.Maintenance
            .Include(item => item.Vehicle)
            .SingleOrDefaultAsync(item => item.Id == maintenanceId
                && item.Vehicle.OrganizationId == organizationId && !item.IsDeleted)
            ?? throw new KeyNotFoundException("Обслуживание не найдено.");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenanceId} FOR UPDATE""");
        await context.Entry(maintenance).ReloadAsync();
        if (maintenance.IsDeleted)
        {
            throw new KeyNotFoundException("Обслуживание не найдено.");
        }

        await MaintenanceDetailsLoader.ReloadAsync(context, maintenance);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationEmployees" WHERE "Id" = {employee.Id} FOR UPDATE""");
        employee = await this._employeeAccess.GetEmployeeAsync(userId, organizationId);
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkExecute);
        var organization = await context.Organizations.AsNoTracking()
            .SingleAsync(item => item.Id == organizationId);
        if (!organization.AllowWorkSelfAssignment)
        {
            throw new InvalidOperationException("Самостоятельное назначение работ выключено.");
        }
        if (maintenance.EmployeeId.HasValue || maintenance.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
        {
            throw new DbUpdateConcurrencyException("Работа уже недоступна. Обновите список.");
        }

        var detail = maintenance.Details.SingleOrDefault(item => item.DetailId == detailId
            && !MaintenanceWorkMapping.IsMaterial(item))
            ?? throw new KeyNotFoundException("Работа не найдена.");
        if (detail.EmployeeId.HasValue || await context.MaintenanceWorkOperations.AsNoTracking()
            .AnyAsync(operation => operation.Id == detailId))
        {
            throw new DbUpdateConcurrencyException("Работа уже назначена. Обновите список.");
        }

        detail.EmployeeId = employee.Id;
        detail.EmployeeName = employee.Name;
        // Отмечаем родителя, чтобы проверка версии заказа охватила изменения строк.
        context.Entry(maintenance).Property(item => item.Comment).IsModified = true;
        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = auditEventId,
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = "maintenance.work.claimed",
            Title = "Исполнитель взял работу",
            Description = $"{employee.Name}: {detail.Name}",
            TargetType = "maintenance",
            TargetId = maintenanceId.ToString("D"),
            CreatedAt = systemClock.UtcNow
        });
        await context.SaveChangesAsync();

        return MaintenanceWorkMapping.MapItem(maintenance, detail, null, [], systemClock.UtcNow);
    }

    /// <summary> Выполняет строку работы от имени сотрудника. </summary>
    public async Task<MaintenanceWorkItemDto> ExecuteAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        MaintenanceWorkCommandDto command)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkExecute);
        var employee = await this._employeeAccess.GetEmployeeAsync(userId, organizationId);
        if (command.CommandId == Guid.Empty || detailId == Guid.Empty)
        {
            throw new InvalidOperationException("Укажите идентификаторы операции и запроса.");
        }

        return await this._transaction.ExecuteAsync(
            () => this.ExecuteCoreAsync(userId, organizationId, maintenanceId, detailId, command, employee),
            () => context.MaintenanceWorkEvents.AsNoTracking().AnyAsync(item => item.CommandId == command.CommandId));
    }

    /// <summary> Внутренняя реализация выполнения строки работы. </summary>
    private async Task<MaintenanceWorkItemDto> ExecuteCoreAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        MaintenanceWorkCommandDto command,
        OrganizationEmployee employee
    )
    {
        var maintenance = await context.Maintenance
            .Include(item => item.Vehicle)
            .SingleOrDefaultAsync(item => item.Id == maintenanceId
                && item.Vehicle.OrganizationId == organizationId
                && !item.IsDeleted)
            ?? throw new KeyNotFoundException("Обслуживание не найдено.");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenanceId} FOR UPDATE""");
        await context.Entry(maintenance).ReloadAsync();
        if (maintenance.IsDeleted)
        {
            throw new KeyNotFoundException("Обслуживание не найдено.");
        }

        await MaintenanceDetailsLoader.ReloadAsync(context, maintenance);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationEmployees" WHERE "Id" = {employee.Id} FOR UPDATE""");
        employee = await this._employeeAccess.GetEmployeeAsync(userId, organizationId);
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkExecute);

        var detail = maintenance.Details.SingleOrDefault(item => item.DetailId == detailId && !MaintenanceWorkMapping.IsMaterial(item))
            ?? throw new KeyNotFoundException("Работа не найдена.");
        if (!MaintenanceWorkMapping.IsAssignedTo(maintenance, detail, employee.Id))
        {
            throw new UnauthorizedAccessException("Работа не назначена этому исполнителю.");
        }

        var operation = await context.MaintenanceWorkOperations.SingleOrDefaultAsync(item => item.Id == detailId);
        if (operation != null && operation.MaintenanceId != maintenanceId)
        {
            throw new InvalidOperationException("Работа принадлежит другому обслуживанию.");
        }
        var previousCommand = await context.MaintenanceWorkEvents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CommandId == command.CommandId);
        if (previousCommand != null)
        {
            var expectedStatus = command.Action?.Trim().ToLowerInvariant() switch
            {
                "start" or "resume" => MaintenanceWorkStatus.InProgress,
                "pause" => MaintenanceWorkStatus.Paused,
                "complete" => MaintenanceWorkStatus.Completed,
                _ => (MaintenanceWorkStatus?)null
            };
            var expectedPreviousStatus = command.Action?.Trim().ToLowerInvariant() switch
            {
                "start" => MaintenanceWorkStatus.Assigned,
                "resume" => MaintenanceWorkStatus.Paused,
                "pause" or "complete" => MaintenanceWorkStatus.InProgress,
                _ => (MaintenanceWorkStatus?)null
            };
            if (previousCommand.OperationId != detailId || previousCommand.ActorUserId != userId
                || previousCommand.Status != expectedStatus
                || previousCommand.PreviousStatus != expectedPreviousStatus
                || previousCommand.PreviousVersion != command.Version
                || previousCommand.Reason != (string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim()))
            {
                throw new InvalidOperationException("Идентификатор запроса уже использован для другого действия.");
            }

            var priorIntervals = await context.MaintenanceWorkIntervals.AsNoTracking()
                .Where(interval => interval.OperationId == detailId)
                .ToListAsync();
            var adjustmentMinutes = await MaintenanceWorkTimeService.GetAdjustmentMinutesAsync(context, detailId);

            return MaintenanceWorkMapping.MapItem(
                maintenance, detail, operation, priorIntervals, previousCommand.CreatedAt,
                previousCommand.Status, previousCommand.ResultVersion, adjustmentMinutes);
        }

        if (maintenance.Status is MaintenanceStatus.Cancelled or MaintenanceStatus.Completed)
        {
            throw new InvalidOperationException("Обслуживание недоступно для выполнения.");
        }

        var previousStatus = operation?.Status ?? MaintenanceWorkStatus.Assigned;
        var previousVersion = operation?.Version ?? 0;
        if (command.Version != previousVersion)
        {
            throw new DbUpdateConcurrencyException("Работа уже изменена. Обновите список.");
        }

        var action = command.Action?.Trim().ToLowerInvariant();
        var nextStatus = (action, previousStatus) switch
        {
            ("start", MaintenanceWorkStatus.Assigned) => MaintenanceWorkStatus.InProgress,
            ("pause", MaintenanceWorkStatus.InProgress) => MaintenanceWorkStatus.Paused,
            ("resume", MaintenanceWorkStatus.Paused) => MaintenanceWorkStatus.InProgress,
            ("complete", MaintenanceWorkStatus.InProgress) => MaintenanceWorkStatus.Completed,
            _ => throw new InvalidOperationException("Недопустимый переход состояния работы.")
        };
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
        if (reason?.Length > 1000 || action == "pause" && reason == null)
        {
            throw new InvalidOperationException("Для паузы укажите причину не длиннее 1000 символов.");
        }

        var now = systemClock.UtcNow;
        if (operation == null)
        {
            operation = new MaintenanceWorkOperation
            {
                Id = detailId,
                MaintenanceId = maintenanceId,
                OrganizationId = organizationId,
                EmployeeId = employee.Id,
                StartedAt = now
            };
            context.MaintenanceWorkOperations.Add(operation);
        }

        if (action is "start" or "resume")
        {
            context.MaintenanceWorkIntervals.Add(new MaintenanceWorkInterval
            {
                OperationId = detailId,
                EmployeeId = employee.Id,
                StartedAt = now
            });
        }
        else
        {
            var activeInterval = await context.MaintenanceWorkIntervals
                .SingleOrDefaultAsync(interval => interval.OperationId == detailId && interval.EndedAt == null)
                ?? throw new InvalidOperationException("Открытый интервал работы не найден.");
            activeInterval.EndedAt = now;
        }

        operation.Status = nextStatus;
        operation.EmployeeId = employee.Id;
        if (nextStatus == MaintenanceWorkStatus.Completed)
        {
            operation.CompletedAt = now;
        }
        if (action == "start" && maintenance.Status == MaintenanceStatus.Planned)
        {
            maintenance.Status = MaintenanceStatus.InProgress;
        }

        var workEvent = new MaintenanceWorkEvent
        {
            OperationId = detailId,
            CommandId = command.CommandId,
            OrganizationId = organizationId,
            MaintenanceId = maintenanceId,
            EmployeeId = employee.Id,
            MemberId = employee.MemberId!.Value,
            ActorUserId = userId,
            PreviousStatus = previousStatus,
            Status = nextStatus,
            CreatedAt = now,
            Reason = reason,
            PreviousVersion = previousVersion
        };
        context.MaintenanceWorkEvents.Add(workEvent);
        await context.SaveChangesAsync();
        workEvent.ResultVersion = operation.Version;
        await context.SaveChangesAsync();

        var operationIntervals = await context.MaintenanceWorkIntervals.AsNoTracking()
            .Where(interval => interval.OperationId == detailId)
            .ToListAsync();
        var currentAdjustmentMinutes = await MaintenanceWorkTimeService.GetAdjustmentMinutesAsync(context, detailId);

        return MaintenanceWorkMapping.MapItem(
            maintenance, detail, operation, operationIntervals, now,
            adjustmentMinutes: currentAdjustmentMinutes);
    }
}
