using OpenCRM.Domain;
using OpenCRM.Domain.Enums;

namespace OpenCRM.Domain.Interfaces.Catalog;

/// <summary> Репозиторий предложений в каталог техники. </summary>
public interface IVehicleCatalogSuggestionRepository
{
    /// <summary> Возвращает открытое дублирующее предложение. </summary>
    Task<VehicleCatalogSuggestion?> FindOpenDuplicateAsync(VehicleKind kind, string brandName, string modelName);

    /// <summary> Проверяет наличие модели в каталоге. </summary>
    Task<bool> ExistsInCatalogAsync(VehicleKind kind, string brandName, string modelName);

    /// <summary> Добавляет предложение в каталог. </summary>
    Task AddAsync(VehicleCatalogSuggestion entity);

    /// <summary> Сохраняет изменения. </summary>
    Task SaveAsync();
}
