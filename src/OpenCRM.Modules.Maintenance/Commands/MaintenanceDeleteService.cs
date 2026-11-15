using OpenCRM.Modules.Planning.Contracts;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Commands;

/// <summary> Удаляет записи об обслуживании. </summary>
internal sealed class MaintenanceDeleteService(
    IMaintenanceRepository maintenanceRepository,
    IVehicleCounterCommandService vehicleService,
    OpenCRMContext context,
    IMaintenanceConsumptionService maintenanceConsumptionService,
    IMaintenanceSchedulerSyncService organizationSchedulerService,
    MaintenanceAccess maintenanceAccess,
    MaintenanceHistoryWriter maintenanceHistoryWriter,
    MaintenanceTransaction transaction) : IMaintenanceDeleteService
{
    /// <summary> Проверка доступа к обслуживанию. </summary>
    private readonly MaintenanceAccess _maintenanceAccess = maintenanceAccess;

    /// <summary> Запись событий в историю обслуживания. </summary>
    private readonly MaintenanceHistoryWriter _maintenanceHistoryWriter = maintenanceHistoryWriter;

    /// <summary> Синхронизация с планировщиком организации. </summary>
    private readonly IMaintenanceSchedulerSyncService _organizationSchedulerService = organizationSchedulerService;

    /// <summary> Транзакция сохранения изменений. </summary>
    private readonly MaintenanceTransaction _transaction = transaction;

    /// <summary> Удаляет обслуживание пользователя. </summary>
    public async Task Delete(string userId, Guid maintenanceId)
    {
        await this.Delete(userId, null, maintenanceId);
    }

    /// <summary> Удаляет обслуживание с учётом организации. </summary>
    public async Task Delete(string userId, Guid? organizationId, Guid maintenanceId)
    {
        await this._transaction.ExecuteAsync(
            () => this.DeleteCoreAsync(userId, organizationId, maintenanceId),
            () => context.Maintenance.AsNoTracking()
                .AnyAsync(maintenance => maintenance.Id == maintenanceId && maintenance.IsDeleted));
    }

    /// <summary> Выполняет удаление записи обслуживания. </summary>
    private async Task DeleteCoreAsync(string userId, Guid? organizationId, Guid maintenanceId)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceDelete);

        var maintenance = await maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new Exception($"Не найдено обслуживание с maintenanceId={maintenanceId} и userId={userId}");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenanceId} FOR UPDATE""");
        await context.Entry(maintenance).ReloadAsync();
        if (maintenance.IsDeleted)
        {
            throw new InvalidOperationException("Обслуживание не найдено.");
        }

        if (organizationId.HasValue && await context.MaintenanceWorkOperations.AsNoTracking()
            .AnyAsync(operation => operation.MaintenanceId == maintenanceId))
        {
            throw new InvalidOperationException("Обслуживание с начатыми работами нельзя удалить.");
        }

        await this._maintenanceHistoryWriter.AddHistoryEventAsync(
            userId,
            organizationId,
            maintenance.VehicleId,
            maintenance.Id,
            MaintenanceHistoryEventCodes.EventDeleted,
            "Обслуживание удалено",
            "Запись скрыта из основной истории техники.");

        maintenance.IsDeleted = true;
        maintenanceRepository.Update(maintenance);
        await maintenanceRepository.Save();
        if (organizationId.HasValue)
        {
            var consumption = await maintenanceConsumptionService.SynchronizeAsync(userId, organizationId.Value, maintenance.Id, []);
            maintenance.MaterialCostAmount = consumption.TotalCost;
            maintenanceRepository.Update(maintenance);
            await maintenanceRepository.Save();
        }

        await vehicleService.RemoveCounterSyncFromMaintenanceAsync(userId, organizationId, maintenance.VehicleId, maintenance.Id);
        if (organizationId.HasValue)
        {
            await this._organizationSchedulerService.ResetMaintenanceWorkflowAsync(userId, organizationId.Value, maintenanceId);
        }
    }
}
