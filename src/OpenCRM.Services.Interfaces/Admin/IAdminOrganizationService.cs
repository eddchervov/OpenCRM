using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Управление организациями в административной панели. </summary>
public interface IAdminOrganizationService
{
    /// <summary> Возвращает все организации для административной панели. </summary>
    Task<AdminOrganizationsPageDto> GetAllAsync();

    /// <summary> Возвращает страницу организаций с поиском, фильтром участника и сортировкой. </summary>
    Task<AdminOrganizationsPageDto> GetAsync(
        string? search, string? user, string sort, bool descending, int page, int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary> Возвращает сведения и статистику выбранной организации. </summary>
    Task<AdminOrganizationDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
