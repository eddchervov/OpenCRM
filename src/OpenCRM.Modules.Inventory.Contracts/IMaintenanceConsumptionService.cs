namespace OpenCRM.Modules.Inventory.Contracts;

/// <summary>
/// Пересчитывает расход материалов как внутренний участник изменения обслуживания.
/// Владеющий сценарий проверяет право изменения заказа и новые ссылки на справочник;
/// исторический расход допускает ранее сохранённые архивные материалы.
/// </summary>
public interface IMaintenanceConsumptionService
{
    /// <summary> Синхронизирует расход материалов по обслуживанию. </summary>
    Task<MaintenanceConsumptionResult> SynchronizeAsync(string userId, Guid organizationId, Guid maintenanceId, IReadOnlyCollection<MaintenanceMaterialConsumptionLine> details);
}
