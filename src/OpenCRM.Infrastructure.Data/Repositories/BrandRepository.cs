using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces.Brands;
using System.Linq.Expressions;

namespace OpenCRM.Infrastructure.Data.Repositories;

/// <summary> Репозиторий марок техники. </summary>
public class BrandRepository(OpenCRMContext opencrmContext) : IBrandRepository
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает проекцию запроса по маркам. </summary>
    public IQueryable<T> Get<T>(Expression<Func<Brand, T>> mapExpr) where T : class
    {
        return _opencrmContext.Brands
            .Where(x => x.IsDeleted == false)
            .Select(mapExpr);
    }

    /// <summary> Возвращает проекцию запроса по маркам с фильтром. </summary>
    public IQueryable<T> Get<T>(BrandFilter filter, Expression<Func<Brand, T>> mapExpr) where T : class
    {
        var query = _opencrmContext.Brands.Where(x => x.IsDeleted == false);

        if (filter.Kind.HasValue)
        {
            query = query.Where(x => x.Kind == filter.Kind.Value);
        }

        return query.Select(mapExpr);
    }
}

