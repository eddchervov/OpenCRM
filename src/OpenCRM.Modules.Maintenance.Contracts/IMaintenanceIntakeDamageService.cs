using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Осмотр кузова в существующей приёмке заказа. </summary>
public interface IMaintenanceIntakeDamageService
{
    Task<MaintenanceIntakeDamagesDto> GetDamages(string userId, Guid organizationId, Guid maintenanceId);

    Task SaveDamage(string userId, Guid organizationId, Guid maintenanceId, Guid damageId, SaveMaintenanceIntakeDamageDto dto);

    Task DeleteDamage(string userId, Guid organizationId, Guid maintenanceId, Guid damageId, uint version);
}
