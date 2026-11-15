using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces.Models;
using System.Linq.Expressions;

namespace OpenCRM.Infrastructure.Data.Repositories;

/// <summary> Репозиторий моделей техники. </summary>
public class ModelRepository(OpenCRMContext opencrmContext) : IModelRepository
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает проекцию запроса моделей по марке. </summary>
    public IQueryable<T> Get<T>(int brandId, Expression<Func<Model, T>> mapExpr) where T : class
    {
        return _opencrmContext.Models
            .Where(x => x.IsDeleted == false && x.BrandId == brandId)
            .Select(mapExpr);
    }

    /// <summary> Возвращает проекцию запроса моделей по марке и виду техники. </summary>
    public IQueryable<T> Get<T>(int brandId, OpenCRM.Domain.Enums.VehicleKind? kind, Expression<Func<Model, T>> mapExpr) where T : class
    {
        var query = _opencrmContext.Models
            .Where(x => x.IsDeleted == false && x.BrandId == brandId);

        if (kind.HasValue)
        {
            query = query.Where(x => x.Kind == kind.Value);
        }

        return query.Select(mapExpr);
    }

    /// <summary> Возвращает модель по идентификатору. </summary>
    public async Task<Model?> Get(int id)
    {
        return await _opencrmContext.Models
            .Include(x => x.Brand)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted && !x.Brand.IsDeleted);
    }
}

