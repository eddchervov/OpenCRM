using OpenCRM.Modules.Planning.Contracts;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Loading;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Commands;

/// <summary> Сервис управления рабочим процессом записей об обслуживании. </summary>
internal sealed class MaintenanceWorkflowService(
    IMaintenanceRepository maintenanceRepository,
    IVehicleCounterCommandService vehicleService,
    OpenCRMContext context,
    MaintenanceAccess maintenanceAccess,
    MaintenanceWorkGuard maintenanceWorkGuard,
    MaintenanceSlotGuard maintenanceSlotGuard,
    MaintenanceWorkflowRules maintenanceWorkflowRules,
    MaintenanceHistoryWriter maintenanceHistoryWriter,
    MaintenanceDefectLinks maintenanceDefectLinks,
    MaintenanceNotificationOutbox maintenanceNotificationOutbox,
    IMaintenanceSchedulerSyncService organizationSchedulerService,
    MaintenanceTransaction transaction) : IMaintenanceWorkflowService
{
    /// <summary> Проверка прав доступа пользователя к записям об обслуживании. </summary>
    private readonly MaintenanceAccess _maintenanceAccess = maintenanceAccess;
    /// <summary> Проверки работ исполнителей перед сменой статуса записи. </summary>
    private readonly MaintenanceWorkGuard _maintenanceWorkGuard = maintenanceWorkGuard;
    /// <summary> Проверка отсутствия конфликтов слотов при изменении записи. </summary>
    private readonly MaintenanceSlotGuard _maintenanceSlotGuard = maintenanceSlotGuard;
    /// <summary> Правила применения действий рабочего процесса к записи. </summary>
    private readonly MaintenanceWorkflowRules _maintenanceWorkflowRules = maintenanceWorkflowRules;
    /// <summary> Записывает события истории записи об обслуживании. </summary>
    private readonly MaintenanceHistoryWriter _maintenanceHistoryWriter = maintenanceHistoryWriter;
    /// <summary> Управление связями записи с неисправностями. </summary>
    private readonly MaintenanceDefectLinks _maintenanceDefectLinks = maintenanceDefectLinks;
    /// <summary> Постановка уведомлений об изменении записи в исходящую очередь. </summary>
    private readonly MaintenanceNotificationOutbox _maintenanceNotificationOutbox = maintenanceNotificationOutbox;
    /// <summary> Синхронизация записи с планировщиком организации. </summary>
    private readonly IMaintenanceSchedulerSyncService _organizationSchedulerService = organizationSchedulerService;
    /// <summary> Транзакция для атомарного изменения записи. </summary>
    private readonly MaintenanceTransaction _transaction = transaction;

    /// <summary> Изменяет рабочий процесс записи без указания организации. </summary>
    public async Task ChangeWorkflow(string userId, Guid maintenanceId, MaintenanceWorkflowActionDto dto)
    {
        await this.ChangeWorkflow(userId, null, maintenanceId, dto);
    }

    /// <summary> Изменяет рабочий процесс записи в транзакции с синхронизацией планировщика. </summary>
    public Task ChangeWorkflow(string userId, Guid? organizationId, Guid maintenanceId, MaintenanceWorkflowActionDto dto)
    {
        var action = MaintenanceWorkflowRules.NormalizeWorkflowAction(dto.Action);
        uint? savedVersion = null;
        return this._transaction.ExecuteAsync(async () =>
        {
            await this.ChangeWorkflowCoreAsync(userId, organizationId, maintenanceId, dto);
            if (organizationId.HasValue && action is not ("mark_ready" or "handover" or "undo_handover"))
            {
                var maintenance = context.Maintenance.Local.Single(item => item.Id == maintenanceId);
                await this._organizationSchedulerService.SyncMaintenanceWorkflowAsync(
                    userId, organizationId.Value, maintenanceId, (int)maintenance.Status);
            }

            // Сохранённая версия позволяет проверить результат при неопределённом исходе фиксации транзакции.
            savedVersion = context.Maintenance.Local.Single(item => item.Id == maintenanceId).Version;
        }, () => context.Maintenance.AsNoTracking().AnyAsync(maintenance =>
            maintenance.Id == maintenanceId && savedVersion.HasValue && maintenance.Version == savedVersion.Value));
    }

    /// <summary> Выполняет смену статуса записи с проверками доступа, конфликтов и побочными эффектами. </summary>
    private async Task ChangeWorkflowCoreAsync(string userId, Guid? organizationId, Guid maintenanceId, MaintenanceWorkflowActionDto dto)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);

        var maintenance = await maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new Exception($"Не найдено обслуживание с maintenanceId={maintenanceId} и userId={userId}");

        await context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenance.Id} FOR UPDATE""");
        // После блокировки перечитываем запись: до её получения другая транзакция могла изменить данные.
        await context.Entry(maintenance).ReloadAsync();
        if (maintenance.IsDeleted)
        {
            throw new InvalidOperationException("Обслуживание не найдено.");
        }

        await MaintenanceDetailsLoader.ReloadAsync(context, maintenance);

        if (dto.Version.HasValue && dto.Version.Value != maintenance.Version)
        {
            throw new InvalidOperationException("Запись уже изменена. Обновите календарь и повторите перенос.");
        }

        var previous = MaintenanceSnapshot.From(maintenance);
        var action = MaintenanceWorkflowRules.NormalizeWorkflowAction(dto.Action);
        var completesWork = action == "complete"
            || action == "simple_handover" && maintenance.Status != MaintenanceStatus.Completed;

        if (organizationId.HasValue && completesWork && await this.RequiresPricedServicesAsync(organizationId.Value))
        {
            MaintenanceWorkGuard.EnsurePricedServicesForWorkspaceCompletion(maintenance);
        }

        if (organizationId.HasValue && action is ("cancel" or "return_to_planned"))
        {
            await this._maintenanceWorkGuard.EnsureWorkAllowsStatusChangeAsync(maintenance,
                action == "cancel" ? MaintenanceStatus.Cancelled : MaintenanceStatus.Planned);
        }

        var isSimpleWorkflow = false;
        if (action == "simple_handover")
        {
            isSimpleWorkflow = await this.IsSimpleHandoverAllowedAsync(organizationId, maintenance.VehicleId);
            await this._maintenanceWorkGuard.EnsureTrackedWorkCompletedAsync(maintenance, allowUntrackedAssignedWork: true);
        }

        if (organizationId.HasValue && action is ("complete" or "mark_ready" or "handover"))
        {
            await this._maintenanceWorkGuard.EnsureTrackedWorkCompletedAsync(maintenance);
        }

        if (action == "reschedule" && dto.Location != null)
        {
            var location = MaintenanceTextRules.NormalizeOptional(dto.Location);
            if (organizationId.HasValue && location != null && location != maintenance.Location
                && !await context.OrganizationScenarioCatalogItems.AnyAsync(item =>
                    item.OrganizationId == organizationId.Value
                    && item.KindCode == OrganizationScenarioCatalogKindCodes.WorkPost
                    && item.IsActive && item.Name == location))
            {
                throw new InvalidOperationException("Выбранный пост недоступен. Обновите календарь и выберите другой пост.");
            }

            maintenance.Location = location;
        }

        if ((action is "reschedule" or "reopen") && !dto.PlannedEndDate.HasValue && dto.PlannedDate.HasValue)
        {
            // При переносе без новой даты окончания сохраняем прежнюю длительность записи в днях.
            var days = ((maintenance.PlannedEndDate ?? maintenance.PlannedDate ?? maintenance.Date).Date
                - (maintenance.PlannedDate ?? maintenance.Date).Date).Days;
            dto.PlannedEndDate = (MaintenanceDateRules.ParseMaintenanceDateText(dto.PlannedDateText) ?? dto.PlannedDate.Value).Date.AddDays(days);
        }
        TimeZoneInfo? organizationTimeZone = null;
        if (organizationId.HasValue && action is ("complete" or "simple_handover"))
        {
            var timeZoneId = await context.Organizations
                .AsNoTracking()
                .Where(organization => organization.Id == organizationId.Value)
                .Select(organization => organization.ShiftTimeZoneId)
                .SingleOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                organizationTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
        }

        var title = this._maintenanceWorkflowRules.ApplyWorkflowAction(
            maintenance,
            action,
            dto,
            organizationId.HasValue,
            isSimpleWorkflow,
            organizationTimeZone);
        if (action is "reschedule" or "reopen" or "return_to_work")
        {
            await this._maintenanceSlotGuard.EnsureCanAssignLocationAsync(
                organizationId,
                maintenance.Location,
                previous.Location,
                requireActiveSameLocation: MaintenanceSlotGuard.RequiresActivePost(previous, maintenance));
        }

        if (action is "start" or "reschedule" or "reopen" or "return_to_planned" or "return_to_work")
        {
            await this._maintenanceSlotGuard.EnsureNoWorkspaceSlotConflictAsync(organizationId, maintenance);
        }

        if (!MaintenanceChangeRules.HasMaintenanceChanges(previous, maintenance) && string.IsNullOrWhiteSpace(dto.Reason))
        {
            return;
        }

        maintenanceRepository.Update(maintenance);
        await maintenanceRepository.Save();

        await this._maintenanceHistoryWriter.AddHistoryEventAsync(
            userId,
            organizationId,
            maintenance.VehicleId,
            maintenance.Id,
            MaintenanceHistoryEventCodes.EventStatusChanged,
            title,
            MaintenanceWorkflowRules.BuildWorkflowHistoryDescription(previous, maintenance, dto.Reason));

        if (previous.Status != MaintenanceStatus.Completed && maintenance.Status == MaintenanceStatus.Completed)
        {
            await this._maintenanceDefectLinks.CloseLinkedDefectsAsync(maintenance.Id);
        }

        // При завершении учитываем пробег, а при выходе из завершённого состояния отменяем связанное показание.
        if ((previous.Status == MaintenanceStatus.Completed || maintenance.Status == MaintenanceStatus.Completed)
            && previous.Status != maintenance.Status)
        {
            await vehicleService.SyncCounterFromMaintenanceAsync(
                userId,
                organizationId,
                maintenance.VehicleId,
                maintenance.Id,
                maintenance.Status == MaintenanceStatus.Completed ? maintenance.Mileage : null,
                maintenance.Date);
        }

        if (organizationId.HasValue && action != "simple_handover")
        {
            await this._maintenanceNotificationOutbox.QueueMaxChangeAsync(organizationId.Value, maintenance, previous);
            await this._maintenanceNotificationOutbox.QueueTelegramChangeAsync(organizationId.Value, maintenance, previous);
        }
    }

    private async Task<bool> RequiresPricedServicesAsync(Guid organizationId)
    {
        var scenarioCode = await context.Organizations.AsNoTracking().Where(item => item.Id == organizationId)
            .Select(item => item.PrimaryScenarioCode).SingleAsync();

        return OrganizationScenarioProcessCatalog.NormalizeScenarioCode(scenarioCode)
            is OrganizationTypeCodes.Detailing or OrganizationTypeCodes.ServiceStation;
    }

    private async Task<bool> IsSimpleHandoverAllowedAsync(Guid? organizationId, Guid vehicleId)
    {
        if (!organizationId.HasValue)
        {
            throw new InvalidOperationException("Выдача доступна только в рабочем пространстве организации.");
        }

        var workflow = await (from vehicle in context.Vehicles.AsNoTracking()
            join organization in context.Organizations.AsNoTracking()
                on vehicle.OrganizationId equals organization.Id
            where vehicle.Id == vehicleId && vehicle.OrganizationId == organizationId.Value
            select new { organization.MaintenanceWorkflowMode, organization.PrimaryScenarioCode })
            .SingleOrDefaultAsync();
        var scenarioCode = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(workflow?.PrimaryScenarioCode);
        if (!string.Equals(workflow?.MaintenanceWorkflowMode, "simple", StringComparison.OrdinalIgnoreCase)
            || scenarioCode is not (OrganizationTypeCodes.Detailing or OrganizationTypeCodes.ServiceStation))
        {
            throw new InvalidOperationException("Выдача одним действием доступна только в простом режиме детейлинга и СТО.");
        }

        return true;
    }
}
