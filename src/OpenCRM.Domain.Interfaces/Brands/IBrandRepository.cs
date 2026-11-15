using System.Linq.Expressions;

namespace OpenCRM.Domain.Interfaces.Brands;

/// <summary> Контракт репозитория справочника брендов. </summary>
public interface IBrandRepository
{
    /// <summary> Проекция запроса по всем брендам справочника. </summary>
    IQueryable<T> Get<T>(Expression<Func<Brand, T>> mapExpr) where T : class;

    /// <summary> Проекция запроса брендов с применением фильтра. </summary>
    IQueryable<T> Get<T>(BrandFilter filter, Expression<Func<Brand, T>> mapExpr) where T : class;
}

/// <summary> Фильтр брендов справочника. </summary>
public sealed class BrandFilter
{
    /// <summary> Тип техники. </summary>
    public OpenCRM.Domain.Enums.VehicleKind? Kind { get; set; }
}

