using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт сохранения данных приёмки обслуживания. </summary>
public interface IMaintenanceIntakeService
{
    /// <summary> Сохраняет данные осмотра при приёмке автомобиля. </summary>
    Task SaveIntake(string userId, Guid organizationId, Guid maintenanceId, SaveMaintenanceIntakeDto dto);
}
