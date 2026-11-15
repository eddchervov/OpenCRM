using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Domain.Interfaces.Models
{
    /// <summary> Контракт репозитория моделей техники справочника. </summary>
    public interface IModelRepository
    {
        /// <summary> Проекция запроса моделей выбранного бренда. </summary>
        public IQueryable<T> Get<T>(int brandId, Expression<Func<Model, T>> mapExpr) where T : class;

        /// <summary> Проекция запроса моделей бренда с фильтром по виду техники. </summary>
        public IQueryable<T> Get<T>(int brandId, OpenCRM.Domain.Enums.VehicleKind? kind, Expression<Func<Model, T>> mapExpr) where T : class;

       /// <summary> Находит модель по идентификатору. </summary>
       Task<Model?> Get(int id);
    }
}

