using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces.Catalog;

namespace OpenCRM.Infrastructure.Data.Repositories;

/// <summary>
/// Репозиторий предложений в каталог техники.
/// </summary>
public class VehicleCatalogSuggestionRepository(OpenCRMContext opencrmContext) : IVehicleCatalogSuggestionRepository
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary>
    /// Возвращает открытое дублирующее предложение.
    /// </summary>
    public async Task<VehicleCatalogSuggestion?> FindOpenDuplicateAsync(VehicleKind kind, string brandName, string modelName)
    {
        var normalizedBrandName = brandName.Trim().ToUpperInvariant();
        var normalizedModelName = modelName.Trim().ToUpperInvariant();

        return await _opencrmContext.VehicleCatalogSuggestions
            .FirstOrDefaultAsync(x =>
                x.Kind == kind &&
                x.Status == VehicleCatalogSuggestionStatus.Pending &&
                x.BrandName.ToUpper() == normalizedBrandName &&
                x.ModelName.ToUpper() == normalizedModelName);
    }

    /// <summary>
    /// Проверяет наличие модели в каталоге.
    /// </summary>
    public async Task<bool> ExistsInCatalogAsync(VehicleKind kind, string brandName, string modelName)
    {
        var normalizedBrandName = brandName.Trim().ToUpperInvariant();
        var normalizedModelName = modelName.Trim().ToUpperInvariant();

        return await _opencrmContext.Models
            .Where(x => !x.IsDeleted)
            .AnyAsync(x =>
                x.Kind == kind &&
                x.Name.ToUpper() == normalizedModelName &&
                x.Brand != null &&
                !x.Brand.IsDeleted &&
                x.Brand.Kind == kind &&
                x.Brand.Name.ToUpper() == normalizedBrandName);
    }

    /// <summary>
    /// Добавляет предложение в каталог.
    /// </summary>
    public async Task AddAsync(VehicleCatalogSuggestion entity)
    {
        await _opencrmContext.VehicleCatalogSuggestions.AddAsync(entity);
    }

    /// <summary>
    /// Сохраняет изменения.
    /// </summary>
    public async Task SaveAsync()
    {
        await _opencrmContext.SaveChangesAsync();
    }
}

