using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary>
/// Читает сводные показатели обслуживания для модуля клиентов.
/// Вызывающий модуль проверяет функцию ClientsModule и разрешение ClientsView до вызова этих методов.
/// </summary>
public interface IMaintenanceClientSummaryQueryService
{
    /// <summary>
    /// Возвращает количество обслуживаний и дату последнего завершённого обслуживания по покупателю заказа.
    /// Заказ без BuyerClientId не относится к клиенту, независимо от владельца техники.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, MaintenanceClientSummaryDto>> GetByClientIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> clientIds);

    /// <summary>Возвращает общее количество обслуживаний организации, с указанным покупателем.</summary>
    Task<int> GetLinkedCountAsync(Guid organizationId);
}
