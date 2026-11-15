using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Contracts;

/// <summary> Контракт изменения клиентов организации. </summary>
public interface IOrganizationClientCommandService
{
    /// <summary> Атомарно находит или создаёт клиента быстрой записи в общей транзакции сценария. </summary>
    Task<(Guid? ClientId, bool Created)> ResolveQuickBookingClientAsync(
        string userId,
        Guid organizationId,
        string? phone,
        string? name,
        string createdComment);

    /// <summary> Создаёт клиента организации. </summary>
    Task<Guid> CreateAsync(string userId, Guid organizationId, CreateOrganizationClientDto dto);

    /// <summary> Явно создаёт клиента для конвертации обращения, отклоняя совпадение телефона. </summary>
    Task<Guid> CreateForInquiryBookingAsync(string userId, Guid organizationId, CreateOrganizationClientDto dto);

    /// <summary> Изменяет клиента организации. </summary>
    Task UpdateAsync(string userId, Guid organizationId, Guid clientId, UpdateOrganizationClientDto dto);

    /// <summary> Изменяет только комментарий клиента организации. </summary>
    Task UpdateCommentAsync(string userId, Guid organizationId, Guid clientId, UpdateOrganizationClientCommentDto dto);

    /// <summary> Удаляет клиента организации. </summary>
    Task DeleteAsync(string userId, Guid organizationId, Guid clientId);

    /// <summary> Помещает клиента в архив. </summary>
    Task ArchiveAsync(string userId, Guid organizationId, Guid clientId);

    /// <summary> Возвращает клиента из архива. </summary>
    Task RestoreAsync(string userId, Guid organizationId, Guid clientId);

    /// <summary> Добавляет запись в историю клиента. </summary>
    Task AddHistoryEntryAsync(string userId, Guid organizationId, Guid clientId, string text, DateTime? occurredAt);
}
