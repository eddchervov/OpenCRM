using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Commands;

/// <summary> Управляет назначением сотрудника на обслуживание. </summary>
internal sealed class MaintenanceAssignmentService(
    OpenCRMContext context,
    MaintenanceOrderLoader maintenanceOrderLoader,
    MaintenanceAccess maintenanceAccess,
    MaintenancePlanAccess maintenancePlanAccess,
    MaintenanceEmployeeResolver maintenanceEmployeeResolver,
    MaintenanceWorkGuard maintenanceWorkGuard,
    MaintenanceHistoryWriter maintenanceHistoryWriter,
    MaintenanceTransaction transaction) : IMaintenanceAssignmentService
{
    /// <summary> Загрузчик записи обслуживания с блокировкой. </summary>
    private readonly MaintenanceOrderLoader _maintenanceOrderLoader = maintenanceOrderLoader;

    /// <summary> Проверка доступа к обслуживанию. </summary>
    private readonly MaintenanceAccess _maintenanceAccess = maintenanceAccess;

    /// <summary> Проверка платных полей тарифа обслуживания. </summary>
    private readonly MaintenancePlanAccess _maintenancePlanAccess = maintenancePlanAccess;

    /// <summary> Определение сотрудника для обслуживания. </summary>
    private readonly MaintenanceEmployeeResolver _maintenanceEmployeeResolver = maintenanceEmployeeResolver;

    /// <summary> Проверки выполнения работ при смене состояния. </summary>
    private readonly MaintenanceWorkGuard _maintenanceWorkGuard = maintenanceWorkGuard;

    /// <summary> Запись событий в историю обслуживания. </summary>
    private readonly MaintenanceHistoryWriter _maintenanceHistoryWriter = maintenanceHistoryWriter;

    /// <summary> Транзакция сохранения изменений. </summary>
    private readonly MaintenanceTransaction _transaction = transaction;

    /// <summary> Назначает сотрудника на обслуживание. </summary>
    public Task AssignEmployee(string userId, Guid organizationId, Guid maintenanceId, AssignMaintenanceEmployeeDto dto)
    {
        uint? savedVersion = null;

        return this._transaction.ExecuteAsync(
            async () =>
            {
                savedVersion = await this.AssignEmployeeCoreAsync(userId, organizationId, maintenanceId, dto);
            },
            () => context.Maintenance.AsNoTracking().AnyAsync(maintenance =>
                maintenance.Id == maintenanceId && savedVersion.HasValue && maintenance.Version == savedVersion.Value));
    }

    /// <summary> Выполняет назначение сотрудника на обслуживание. </summary>
    private async Task<uint?> AssignEmployeeCoreAsync(string userId, Guid organizationId, Guid maintenanceId, AssignMaintenanceEmployeeDto dto)
    {
        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);
        var maintenance = await this._maintenanceOrderLoader.LockMaintenanceAsync(userId, organizationId, maintenanceId);
        if (maintenance.Version != dto.Version)
        {
            throw new InvalidOperationException("Запись уже изменена. Обновите карточку перед назначением сотрудника.");
        }
        await this._maintenancePlanAccess.EnsureCanAssignEmployeeAsync(
            userId, organizationId, maintenance.EmployeeId, dto.EmployeeId);
        await this._maintenanceWorkGuard.ValidateStartedWorkAsync(maintenance, maintenance.Details, dto.EmployeeId);
        var previous = MaintenanceSnapshot.From(maintenance);
        maintenance.Performer = await this._maintenanceEmployeeResolver.ResolveEmployeeNameAsync(organizationId, dto.EmployeeId, maintenance.EmployeeId, maintenance.Performer);
        maintenance.EmployeeId = dto.EmployeeId;
        if (!MaintenanceChangeRules.HasMaintenanceChanges(previous, maintenance))
        {
            return null;
        }

        await context.SaveChangesAsync();
        await this._maintenanceHistoryWriter.AddUpdateHistoryEventsAsync(userId, organizationId, maintenance, previous);

        return maintenance.Version;
    }
}
