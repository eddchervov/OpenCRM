using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces.Models;
using OpenCRM.Services.Interfaces.Models;
using OpenCRM.Services.Interfaces.Models.Dtos;
using System.Linq.Expressions;

namespace OpenCRM.Modules.Catalog.Models;

/// <summary> Сервис получения справочника моделей техники. </summary>
public class ModelService(IModelRepository modelRepository) : IModelService
{
    /// <summary> Зависимость репозитория моделей. </summary>
    private readonly IModelRepository _modelRepository = modelRepository;

    /// <summary> Возвращает модели выбранного бренда. </summary>
    public async Task<IEnumerable<ModelOptionDto>> GetOptions(int brandId)
    {
        return await _modelRepository.Get(brandId, MapToDtoExpr).ToListAsync();
    }

    /// <summary> Возвращает модели бренда с фильтром по виду техники. </summary>
    public async Task<IEnumerable<ModelOptionDto>> GetOptions(int brandId, OpenCRM.Domain.Enums.VehicleKind? kind)
    {
        return await _modelRepository.Get(brandId, kind, MapToDtoExpr).ToListAsync();
    }

    /// <summary> Выражение проекции модели в DTO. </summary>
    private readonly static Expression<Func<Model, ModelOptionDto>> MapToDtoExpr = model => new ModelOptionDto(model.Id, model.Name, model.Kind);
}
