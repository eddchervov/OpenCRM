using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Contracts;

/// <summary> Контракт чтения клиентов организации. </summary>
public interface IOrganizationClientQueryService
{
    /// <summary> Возвращает список клиентов организации с поиском. </summary>
    Task<IReadOnlyList<OrganizationClientDto>> GetAsync(string userId, Guid organizationId, string? searchQuery = null);

    /// <summary> Возвращает страницу клиентов с фильтрами и сортировкой. </summary>
    Task<OrganizationClientsPageDto> GetPageAsync(string userId, Guid organizationId, string? searchQuery, string? sort, int page, int pageSize, bool isArchived = false, string? statusCode = null, string? responsibleUserId = null, Guid? tagId = null, string? segmentCode = null, string? partyTypeCode = null);

    /// <summary> Возвращает страницу кратких вариантов клиентов для поиска при выборе. </summary>
    Task<OrganizationClientLookupPageDto> LookupAsync(string userId, Guid organizationId, string? searchQuery, int page, CancellationToken cancellationToken = default);

    /// <summary> Возвращает краткие варианты клиентов для выбора. </summary>
    Task<IReadOnlyList<OrganizationClientOptionDto>> GetOptionsAsync(string userId, Guid organizationId);

    /// <summary> Возвращает клиента по идентификатору. </summary>
    Task<OrganizationClientDto?> GetByIdAsync(string userId, Guid organizationId, Guid clientId);
}
