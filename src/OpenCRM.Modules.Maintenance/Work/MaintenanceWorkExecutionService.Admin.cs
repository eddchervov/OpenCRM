using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Loading;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Work;

internal sealed partial class MaintenanceWorkExecutionService
{
    /// <summary> Завершает назначенную работу от имени администратора. </summary>
    public async Task<MaintenanceWorkItemDto> CompleteForEmployeeAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        MaintenanceWorkCommandDto command)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);
        var reason = command.Reason?.Trim();
        if (detailId == Guid.Empty || command.CommandId == Guid.Empty
            || command.Action?.Trim().ToLowerInvariant() != "complete-for-employee"
            || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
        {
            throw new InvalidOperationException("Укажите причину завершения не длиннее 1000 символов.");
        }

        return await this._transaction.ExecuteAsync(
            () => this.CompleteForEmployeeCoreAsync(
                userId, organizationId, maintenanceId, detailId, command, reason),
            () => context.MaintenanceWorkEvents.AsNoTracking()
                .AnyAsync(item => item.CommandId == command.CommandId));
    }

    /// <summary> Внутренняя реализация административного завершения работы. </summary>
    private async Task<MaintenanceWorkItemDto> CompleteForEmployeeCoreAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        MaintenanceWorkCommandDto command,
        string reason)
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
        var previousCommand = await context.MaintenanceWorkEvents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CommandId == command.CommandId);
        if (previousCommand != null)
        {
            if (previousCommand.OperationId != detailId || previousCommand.OrganizationId != organizationId
                || previousCommand.MaintenanceId != maintenanceId || previousCommand.ActorUserId != userId
                || previousCommand.Status != MaintenanceWorkStatus.Completed
                || previousCommand.PreviousStatus is not (MaintenanceWorkStatus.Assigned
                    or MaintenanceWorkStatus.InProgress or MaintenanceWorkStatus.Paused)
                || previousCommand.PreviousVersion != command.Version || previousCommand.Reason != reason)
            {
                throw new InvalidOperationException("Идентификатор запроса уже использован для другого действия.");
            }

            var priorOperation = await context.MaintenanceWorkOperations.AsNoTracking()
                .SingleAsync(item => item.Id == detailId && item.MaintenanceId == maintenanceId);
            var priorIntervals = await context.MaintenanceWorkIntervals.AsNoTracking()
                .Where(item => item.OperationId == detailId)
                .ToListAsync();
            var priorAdjustmentMinutes = await MaintenanceWorkTimeService.GetAdjustmentMinutesAsync(context, detailId);

            return MaintenanceWorkMapping.MapItem(
                maintenance, maintenance.Details.Single(item => item.DetailId == detailId), priorOperation,
                priorIntervals, previousCommand.CreatedAt, previousCommand.Status, previousCommand.ResultVersion,
                priorAdjustmentMinutes);
        }

        if (maintenance.Status is not (MaintenanceStatus.InProgress or MaintenanceStatus.Completed)
            || maintenance.HandedOverAt.HasValue)
        {
            throw new InvalidOperationException("Работу можно завершить только до выдачи автомобиля.");
        }

        var detail = maintenance.Details.SingleOrDefault(item => item.DetailId == detailId
            && !MaintenanceWorkMapping.IsMaterial(item))
            ?? throw new KeyNotFoundException("Работа не найдена.");
        var assignedEmployeeId = detail.EmployeeId ?? maintenance.EmployeeId;
        if (!assignedEmployeeId.HasValue)
        {
            throw new InvalidOperationException("У работы не указан исполнитель.");
        }

        var employee = await context.OrganizationEmployees.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == assignedEmployeeId && item.OrganizationId == organizationId)
            ?? throw new InvalidOperationException("Назначенный исполнитель не найден в организации.");
        var operation = await context.MaintenanceWorkOperations
            .SingleOrDefaultAsync(item => item.Id == detailId && item.MaintenanceId == maintenanceId);
        if (operation != null && operation.EmployeeId != assignedEmployeeId)
        {
            throw new DbUpdateConcurrencyException("Исполнитель работы уже изменён. Обновите карточку.");
        }

        var previousStatus = operation?.Status ?? MaintenanceWorkStatus.Assigned;
        var previousVersion = operation?.Version ?? 0;
        if (command.Version != previousVersion)
        {
            throw new DbUpdateConcurrencyException("Работа уже изменена. Обновите карточку.");
        }
        if (previousStatus is not (MaintenanceWorkStatus.Assigned or MaintenanceWorkStatus.InProgress
            or MaintenanceWorkStatus.Paused))
        {
            throw new InvalidOperationException("Работу можно завершить только до её выполнения.");
        }

        var now = systemClock.UtcNow;
        if (previousStatus == MaintenanceWorkStatus.InProgress)
        {
            var activeInterval = await context.MaintenanceWorkIntervals
                .SingleOrDefaultAsync(item => item.OperationId == detailId && item.EndedAt == null)
                ?? throw new InvalidOperationException("Открытый интервал работы не найден.");
            activeInterval.EndedAt = now;
        }

        var memberId = await context.OrganizationMembers.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.UserId == userId)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync()
            ?? throw new UnauthorizedAccessException("Участник организации не найден.");
        if (operation == null)
        {
            operation = new MaintenanceWorkOperation
            {
                Id = detailId,
                MaintenanceId = maintenanceId,
                OrganizationId = organizationId,
                EmployeeId = assignedEmployeeId.Value
            };
            context.MaintenanceWorkOperations.Add(operation);
        }

        operation.Status = MaintenanceWorkStatus.Completed;
        operation.EmployeeId = assignedEmployeeId.Value;
        operation.CompletedAt = now;
        var workEvent = new MaintenanceWorkEvent
        {
            OperationId = detailId,
            CommandId = command.CommandId,
            OrganizationId = organizationId,
            MaintenanceId = maintenanceId,
            EmployeeId = assignedEmployeeId.Value,
            MemberId = memberId,
            ActorUserId = userId,
            PreviousStatus = previousStatus,
            Status = MaintenanceWorkStatus.Completed,
            CreatedAt = now,
            Reason = reason,
            PreviousVersion = previousVersion
        };
        context.MaintenanceWorkEvents.Add(workEvent);
        await context.SaveChangesAsync();
        workEvent.ResultVersion = operation.Version;
        await context.SaveChangesAsync();

        var intervals = await context.MaintenanceWorkIntervals.AsNoTracking()
            .Where(item => item.OperationId == detailId)
            .ToListAsync();
        var adjustmentMinutes = await MaintenanceWorkTimeService.GetAdjustmentMinutesAsync(context, detailId);

        return MaintenanceWorkMapping.MapItem(
            maintenance, detail, operation, intervals, now, adjustmentMinutes: adjustmentMinutes);
    }
}
