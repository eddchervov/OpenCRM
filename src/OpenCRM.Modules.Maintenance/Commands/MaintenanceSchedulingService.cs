using OpenCRM.Modules.Planning.Contracts;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Loading;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Commands;

/// <summary> Управляет плановыми датами обслуживания. </summary>
internal sealed class MaintenanceSchedulingService(
    IMaintenanceRepository maintenanceRepository,
    OpenCRMContext context,
    MaintenanceAccess maintenanceAccess,
    MaintenanceSlotGuard maintenanceSlotGuard,
    MaintenanceHistoryWriter maintenanceHistoryWriter,
    MaintenanceNotificationOutbox maintenanceNotificationOutbox,
    IMaintenanceSchedulerSyncService organizationSchedulerService,
    MaintenanceTransaction transaction) : IMaintenanceSchedulingService
{
    /// <summary> Проверка доступа к обслуживанию. </summary>
    private readonly MaintenanceAccess _maintenanceAccess = maintenanceAccess;

    /// <summary> Проверка конфликтов постов при назначении. </summary>
    private readonly MaintenanceSlotGuard _maintenanceSlotGuard = maintenanceSlotGuard;

    /// <summary> Запись событий в историю обслуживания. </summary>
    private readonly MaintenanceHistoryWriter _maintenanceHistoryWriter = maintenanceHistoryWriter;

    /// <summary> Постановка уведомлений в очередь. </summary>
    private readonly MaintenanceNotificationOutbox _maintenanceNotificationOutbox = maintenanceNotificationOutbox;

    /// <summary> Синхронизация с планировщиком организации. </summary>
    private readonly IMaintenanceSchedulerSyncService _organizationSchedulerService = organizationSchedulerService;

    /// <summary> Транзакция сохранения изменений. </summary>
    private readonly MaintenanceTransaction _transaction = transaction;

    /// <summary> Обновляет плановую дату обслуживания и возвращает статус. </summary>
    public Task<MaintenanceStatus> UpdatePlannedDate(
        string userId,
        Guid? organizationId,
        Guid maintenanceId,
        UpdateMaintenancePlannedDateDto dto)
    {
        uint? savedVersion = null;
        return this._transaction.ExecuteAsync(async () =>
        {
            var status = await this.UpdatePlannedDateCoreAsync(userId, organizationId, maintenanceId, dto);
            if (organizationId.HasValue)
            {
                await this._organizationSchedulerService.SyncMaintenanceWorkflowAsync(
                    userId, organizationId.Value, maintenanceId, (int)status);
            }

            savedVersion = context.Maintenance.Local.Single(maintenance => maintenance.Id == maintenanceId).Version;

            return status;
        }, () => context.Maintenance.AsNoTracking().AnyAsync(maintenance =>
            maintenance.Id == maintenanceId && savedVersion.HasValue && maintenance.Version == savedVersion.Value));
    }

    /// <summary> Выполняет обновление плановой даты обслуживания. </summary>
    private async Task<MaintenanceStatus> UpdatePlannedDateCoreAsync(
        string userId,
        Guid? organizationId,
        Guid maintenanceId,
        UpdateMaintenancePlannedDateDto dto)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._maintenanceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);
        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);

        var maintenance = await maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new KeyNotFoundException($"Не найдено обслуживание с maintenanceId={maintenanceId} и userId={userId}");

        await context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenance.Id} FOR UPDATE""");
        await context.Entry(maintenance).ReloadAsync();
        await MaintenanceDetailsLoader.ReloadAsync(context, maintenance);

        if (maintenance.IsDeleted)
        {
            throw new KeyNotFoundException("Обслуживание не найдено.");
        }

        if (maintenance.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
        {
            throw new InvalidOperationException("Нельзя переносить выполненное или отмененное обслуживание.");
        }

        var previous = MaintenanceSnapshot.From(maintenance);

        var periodDays = ((maintenance.PlannedEndDate ?? maintenance.PlannedDate ?? maintenance.Date).Date
            - (maintenance.PlannedDate ?? maintenance.Date).Date).Days;

        var plannedDate = MaintenanceDateRules.ToStoredMaintenanceDate(dto.PlannedDate);
        var plannedEndDate = dto.PlannedEndDate ?? plannedDate.AddDays(periodDays);
        var plannedStartTime = dto.PlannedStartTime ?? maintenance.PlannedStartTime ?? new TimeSpan(9, 0, 0);
        var plannedEndTime = dto.PlannedEndTime ?? maintenance.PlannedEndTime ?? new TimeSpan(10, 0, 0);

        maintenance.PlannedDate = plannedDate;
        maintenance.Date = plannedDate;
        MaintenanceDateRules.ApplyPlannedTime(maintenance, plannedStartTime, plannedEndTime, plannedEndDate);
        await this._maintenanceSlotGuard.EnsureCanAssignLocationAsync(
            organizationId,
            maintenance.Location,
            previous.Location,
            requireActiveSameLocation: MaintenanceSlotGuard.RequiresActivePost(previous, maintenance));
        await this._maintenanceSlotGuard.EnsureNoWorkspaceSlotConflictAsync(organizationId, maintenance);

        if (!MaintenanceChangeRules.HasMaintenanceChanges(previous, maintenance))
        {
            return maintenance.Status;
        }

        // Сохраняются только изменённые даты и время, без повторного обновления строк заказа.
        await context.SaveChangesAsync();
        await this._maintenanceHistoryWriter.AddUpdateHistoryEventsAsync(userId, organizationId, maintenance, previous);
        if (organizationId.HasValue)
        {
            await this._maintenanceNotificationOutbox.QueueMaxChangeAsync(organizationId.Value, maintenance, previous);
            await this._maintenanceNotificationOutbox.QueueTelegramChangeAsync(organizationId.Value, maintenance, previous);
        }

        return maintenance.Status;
    }
}
