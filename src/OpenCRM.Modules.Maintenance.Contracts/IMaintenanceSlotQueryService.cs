using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Читает занятые интервалы постов. </summary>
public interface IMaintenanceSlotQueryService
{
    /// <summary> Возвращает занятые интервалы постов на дату. </summary>
    Task<IReadOnlyList<OccupiedMaintenanceSlotDto>> GetOccupiedSlots(
        string userId,
        Guid organizationId,
        DateOnly date,
        string location,
        Guid? excludeMaintenanceId = null,
        DateOnly? dateTo = null);
}