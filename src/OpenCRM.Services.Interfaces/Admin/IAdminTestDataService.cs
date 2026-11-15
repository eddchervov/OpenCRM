using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Генерация тестовых данных для организаций. </summary>
public interface IAdminTestDataService
{
    /// <summary> Ищет организации для работы с тестовыми данными. </summary>
    Task<IReadOnlyList<AdminTestDataOrganizationDto>> SearchOrganizationsAsync(string? search);

    /// <summary> Возвращает страницу организаций для работы с тестовыми данными. </summary>
    Task<AdminTestDataOrganizationsPageDto> SearchOrganizationsPageAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary> Возвращает организацию для работы с тестовыми данными. </summary>
    Task<AdminTestDataOrganizationDto?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary> Возвращает технику организации для тестовых данных. </summary>
    Task<AdminTestDataVehiclesResultDto> GetVehiclesAsync(Guid organizationId);

    /// <summary> Генерирует тестовые записи техники для организации. </summary>
    Task<AdminTestDataGenerationResultDto> GenerateVehiclesAsync(Guid organizationId, int count);

    /// <summary> Генерирует тестовые записи об обслуживании для техники. </summary>
    Task<AdminTestDataGenerationResultDto> GenerateMaintenancesAsync(
        Guid organizationId,
        Guid vehicleId,
        int count,
        string? mode);

    /// <summary> Генерирует тестовые дефекты для техники. </summary>
    Task<AdminTestDataGenerationResultDto> GenerateDefectsAsync(
        Guid organizationId,
        Guid vehicleId,
        int count);
}
