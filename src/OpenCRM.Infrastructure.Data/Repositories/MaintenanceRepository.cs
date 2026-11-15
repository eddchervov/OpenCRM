using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces.Maintenances;

namespace OpenCRM.Infrastructure.Data.Repositories;

/// <summary> Репозиторий обслуживаний. </summary>
public class MaintenanceRepository(OpenCRMContext opencrmContext) : IMaintenanceRepository
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает обслуживания техники пользователя. </summary>
    public async Task<IEnumerable<Maintenance>> GetByVehicleId(string userId, Guid vehicleId)
    {
        return await GetByVehicleId(userId, null, vehicleId);
    }

    /// <summary> Возвращает обслуживания техники в рабочем пространстве. </summary>
    public async Task<IEnumerable<Maintenance>> GetByVehicleId(string userId, Guid? organizationId, Guid vehicleId)
    {
        return await BuildVehicleQuery(userId, organizationId, vehicleId)
            .OrderByDescending(x => x.Date)
            .ToListAsync();
    }

    /// <summary> Возвращает страницу обслуживаний техники с фильтрами и сортировкой. </summary>
    public async Task<(IEnumerable<Maintenance> Items, int TotalCount)> GetByVehicleId(string userId, Guid vehicleId, int page, int pageSize, string? sort = null, int? type = null, int? status = null)
    {
        return await GetByVehicleId(userId, null, vehicleId, page, pageSize, sort, type, status);
    }

    /// <summary> Возвращает страницу обслуживаний в рабочем пространстве с фильтрами. </summary>
    public async Task<(IEnumerable<Maintenance> Items, int TotalCount)> GetByVehicleId(string userId, Guid? organizationId, Guid vehicleId, int page, int pageSize, string? sort = null, int? type = null, int? status = null)
    {
        var query = BuildVehicleQuery(userId, organizationId, vehicleId);

        if (type.HasValue)
        {
            query = query.Where(x => (int)x.Type == type.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => (int)x.Status == status.Value);
        }

        var totalCount = await query.CountAsync();

        query = sort switch
        {
            "mileage_asc" => query.OrderByDescending(x => x.Mileage.HasValue).ThenBy(x => x.Mileage).ThenByDescending(x => x.Date),
            "mileage_desc" => query.OrderByDescending(x => x.Mileage.HasValue).ThenByDescending(x => x.Mileage).ThenByDescending(x => x.Date),
            "date_asc" => query.OrderBy(x => x.Date).ThenByDescending(x => x.Mileage),
            "date_desc" => query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Mileage),
            _ => query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Mileage)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <summary> Возвращает страницу удалённых обслуживаний техники. </summary>
    public async Task<(IEnumerable<Maintenance> Items, int TotalCount)> GetDeletedByVehicleId(string userId, Guid vehicleId, int page, int pageSize)
    {
        return await GetDeletedByVehicleId(userId, null, vehicleId, page, pageSize);
    }

    /// <summary> Возвращает страницу удалённых обслуживаний в рабочем пространстве. </summary>
    public async Task<(IEnumerable<Maintenance> Items, int TotalCount)> GetDeletedByVehicleId(string userId, Guid? organizationId, Guid vehicleId, int page, int pageSize)
    {
        var query = BuildVehicleQuery(userId, organizationId, vehicleId, includeDeleted: true)
            .Where(x => x.IsDeleted)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Mileage);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <summary> Возвращает обслуживание пользователя по идентификатору. </summary>
    public Task<Maintenance?> GetById(string userId, Guid id)
    {
        return GetById(userId, null, id);
    }

    /// <summary> Возвращает обслуживание в рабочем пространстве по идентификатору. </summary>
    public Task<Maintenance?> GetById(string userId, Guid? organizationId, Guid id)
    {
        return BuildByIdQuery(userId, organizationId, id)
            .Include(x => x.Vehicle)
            .FirstOrDefaultAsync();
    }

    /// <summary> Проверяет существование обслуживания. </summary>
    public Task<bool> ExistsAsync(string userId, Guid? organizationId, Guid id) =>
        this.BuildByIdQuery(userId, organizationId, id).AsNoTracking().AnyAsync();

    /// <summary> Помечает обслуживание как изменённое. </summary>
    public void Update(Maintenance entity)
    {
        _opencrmContext.Update(entity);
    }

    /// <summary> Добавляет обслуживание. </summary>
    public Task Add(Maintenance entity)
    {
        return _opencrmContext.Maintenance.AddAsync(entity).AsTask();
    }

    /// <summary> Сохраняет изменения в базе данных. </summary>
    public Task Save()
    {
        return _opencrmContext.SaveChangesAsync();
    }

    /// <summary> Строит запрос обслуживаний техники с учётом удалённых. </summary>
    private IQueryable<Maintenance> BuildVehicleQuery(string userId, Guid? organizationId, Guid vehicleId, bool includeDeleted = false) =>
        this.BuildQuery(userId, organizationId, includeDeleted).Where(maintenance => maintenance.VehicleId == vehicleId);

    /// <summary> Строит запрос обслуживания по идентификатору. </summary>
    private IQueryable<Maintenance> BuildByIdQuery(string userId, Guid? organizationId, Guid maintenanceId) =>
        this.BuildQuery(userId, organizationId).Where(maintenance => maintenance.Id == maintenanceId);

    /// <summary> Строит запрос обслуживаний пользователя или организации. </summary>
    private IQueryable<Maintenance> BuildQuery(string userId, Guid? organizationId, bool includeDeleted = false)
    {
        var query = this._opencrmContext.Maintenance;

        var scopedQuery = organizationId.HasValue
            ? query.Where(maintenance => maintenance.Vehicle.OrganizationId == organizationId.Value)
            : query.Where(maintenance => maintenance.UserId == userId && maintenance.Vehicle.OrganizationId == null);

        return includeDeleted ? scopedQuery : scopedQuery.Where(maintenance => !maintenance.IsDeleted);
    }
}
