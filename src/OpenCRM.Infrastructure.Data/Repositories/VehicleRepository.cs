using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces.Vehicles;
using System.Linq.Expressions;

namespace OpenCRM.Infrastructure.Data.Repositories;

/// <summary> Репозиторий техники. </summary>
public class VehicleRepository(OpenCRMContext opencrmContext) : IVehicleRepository
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает проекцию запроса техники пользователя с признаком архива. </summary>
    public IQueryable<T> Get<T>(string userId, bool isArchived, Expression<Func<Vehicle, T>> mapExpr) where T : class
    {
        return this.Get(userId, null, isArchived, mapExpr);
    }

    /// <summary> Возвращает проекцию запроса техники в рабочем пространстве с признаком архива. </summary>
    public IQueryable<T> Get<T>(string userId, Guid? organizationId, bool isArchived, Expression<Func<Vehicle, T>> mapExpr) where T : class
    {
        return this.BuildQuery(userId, organizationId)
            .Where(x => x.IsArchived == isArchived)
            .OrderByDescending(x => x.DateTimeCreation)
            .Select(mapExpr);
    }

    /// <summary> Возвращает проекцию запроса техники пользователя. </summary>
    public IQueryable<T> Get<T>(string userId, Expression<Func<Vehicle, T>> mapExpr) where T : class
    {
        return this.Get(userId, null, mapExpr);
    }

    /// <summary> Возвращает проекцию запроса техники в рабочем пространстве. </summary>
    public IQueryable<T> Get<T>(string userId, Guid? organizationId, Expression<Func<Vehicle, T>> mapExpr) where T : class
    {
        return this.BuildQuery(userId, organizationId)
            .OrderByDescending(x => x.DateTimeCreation)
            .Select(mapExpr);
    }

    /// <summary> Возвращает всю архивную технику. </summary>
    public Task<List<Vehicle>> GetArchived()
    {
        return this._opencrmContext.Vehicles.Where(x => x.IsArchived).ToListAsync();
    }

    /// <summary> Возвращает технику пользователя по идентификатору. </summary>
    public Task<Vehicle?> Get(string userId, Guid vehicleId)
    {
        return this.Get(userId, null, vehicleId);
    }

    /// <summary> Возвращает технику в рабочем пространстве по идентификатору. </summary>
    public Task<Vehicle?> Get(string userId, Guid? organizationId, Guid vehicleId)
    {
        return this.BuildQuery(userId, organizationId).FirstOrDefaultAsync(x => x.Id == vehicleId);
    }

    /// <summary> Возвращает количество техники пользователя с фильтром по архивности. </summary>
    public async Task<int> CountVehicles(string userId, bool? isArchived)
    {
        return await this.CountVehicles(userId, null, isArchived);
    }

    /// <summary> Возвращает количество техники пользователя или организации с фильтром по архивности. </summary>
    public async Task<int> CountVehicles(string userId, Guid? organizationId, bool? isArchived)
    {
        var query = this.BuildQuery(userId, organizationId);

        if (isArchived == null)
        {
            return await query.CountAsync();
        }

        return isArchived.Value
            ? await query.Where(x => x.IsArchived).CountAsync()
            : await query.Where(x => !x.IsArchived).CountAsync();
    }

    /// <summary> Отмечает сущность техники как изменённую. </summary>
    public void Update(Vehicle entity)
    {
        this._opencrmContext.Vehicles.Update(entity);
    }

    /// <summary> Добавляет новую запись техники. </summary>
    public Task Add(Vehicle entity)
    {
        return this._opencrmContext.Vehicles.AddAsync(entity).AsTask();
    }

    /// <summary> Сохраняет изменения в базе данных. </summary>
    public Task Save()
    {
        return this._opencrmContext.SaveChangesAsync();
    }

    /// <summary> Формирует базовый запрос техники с фильтрацией по пользователю и организации. </summary>
    private IQueryable<Vehicle> BuildQuery(string userId, Guid? organizationId) =>
        this._opencrmContext.Vehicles
            .Where(vehicle => !vehicle.IsDeleted)
            .Where(vehicle => organizationId.HasValue
                ? vehicle.OrganizationId == organizationId.Value
                : vehicle.UserId == userId && vehicle.OrganizationId == null);
}
