using System.Linq.Expressions;

namespace OpenCRM.Domain.Interfaces.Vehicles;

/// <summary> Репозиторий техники. </summary>
public interface IVehicleRepository
{
    /// <summary> Проекция запроса техники пользователя с фильтром архивности. </summary>
    IQueryable<T> Get<T>(string userId, bool isArchived, Expression<Func<Vehicle, T>> mapExpr) where T : class;

    /// <summary> Проекция запроса техники пользователя с учётом организации. </summary>
    IQueryable<T> Get<T>(string userId, Guid? organizationId, bool isArchived, Expression<Func<Vehicle, T>> mapExpr) where T : class;

    /// <summary> Проекция запроса всей техники пользователя. </summary>
    IQueryable<T> Get<T>(string userId, Expression<Func<Vehicle, T>> mapExpr) where T : class;

    /// <summary> Проекция запроса техники пользователя с учётом организации. </summary>
    IQueryable<T> Get<T>(string userId, Guid? organizationId, Expression<Func<Vehicle, T>> mapExpr) where T : class;

    /// <summary> Находит технику по идентификатору для пользователя. </summary>
    Task<Vehicle?> Get(string userId, Guid vehicleId);

    /// <summary> Находит технику по идентификатору с учётом организации. </summary>
    Task<Vehicle?> Get(string userId, Guid? organizationId, Guid vehicleId);

    /// <summary> Получает всю архивную технику. </summary>
    Task<List<Vehicle>> GetArchived();

    /// <summary> Считает технику пользователя с учётом архивности. </summary>
    Task<int> CountVehicles(string userId, bool? isArchived);

    /// <summary> Считает технику пользователя с учётом организации и архивности. </summary>
    Task<int> CountVehicles(string userId, Guid? organizationId, bool? isArchived);

    /// <summary> Помечает технику как изменённую. </summary>
    void Update(Vehicle entity);

    /// <summary> Добавляет новую единицу техники. </summary>
    Task Add(Vehicle entity);

    /// <summary> Сохраняет изменения репозитория техники. </summary>
    Task Save();
}
