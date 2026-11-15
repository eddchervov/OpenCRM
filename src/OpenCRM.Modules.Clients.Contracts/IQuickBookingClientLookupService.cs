namespace OpenCRM.Modules.Clients.Contracts;

/// <summary>
/// Выполняет поиск клиента для быстрой записи после проверки права на создание обслуживания.
/// Поиск не изменяет данные и не управляет транзакцией.
/// </summary>
public interface IQuickBookingClientLookupService
{
    /// <summary>Проверяет, что клиент не удалён и принадлежит указанной организации.</summary>
    Task<bool> ExistsAsync(string userId, Guid organizationId, Guid clientId);

    /// <summary>Ищет клиента сначала по нормализованному телефону, затем по имени.</summary>
    Task<Guid?> FindExistingAsync(string userId, Guid organizationId, string? phone, string? name);

    /// <summary>Ищет возможный дубль при явном создании клиента.</summary>
    Task<Guid?> FindPotentialDuplicateAsync(string userId, Guid organizationId, string? phone, string? name);
}
