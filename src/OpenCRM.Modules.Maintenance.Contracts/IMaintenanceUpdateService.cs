using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт обновления обслуживаний и их строк. </summary>
public interface IMaintenanceUpdateService
{
    /// <summary> Обновляет список работ и услуг обслуживания. </summary>
    Task UpdateServices(string userId, Guid organizationId, Guid maintenanceId, UpdateMaintenanceServicesDto dto);

    /// <summary> Обновляет ставки НДС по строкам обслуживания. </summary>
    Task UpdateVatRates(string userId, Guid organizationId, Guid maintenanceId, UpdateMaintenanceVatRatesDto dto);

    /// <summary> Обновляет данные обслуживания. </summary>
    Task Update(string userId, Guid maintenanceId, UpdateMaintenanceDto dto);

    /// <summary> Обновляет данные обслуживания в контексте организации. </summary>
    Task Update(string userId, Guid? organizationId, Guid maintenanceId, UpdateMaintenanceDto dto);
}
