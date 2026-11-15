using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт управления платежами по обслуживанию. </summary>
public interface IMaintenancePaymentService
{
    /// <summary> Возвращает платежи обслуживания. </summary>
    Task<IReadOnlyList<MaintenancePaymentDto>> GetPayments(string userId, Guid organizationId, Guid maintenanceId);

    /// <summary> Добавляет платеж по обслуживанию. </summary>
    Task AddPayment(string userId, Guid organizationId, Guid maintenanceId, AddMaintenancePaymentDto dto);

    /// <summary> Отменяет платеж обслуживания с указанием причины. </summary>
    Task CancelPayment(string userId, Guid organizationId, Guid maintenanceId, Guid paymentId, string reason);
}
