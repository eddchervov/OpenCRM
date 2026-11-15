namespace OpenCRM.Domain.Interfaces.Maintenances;

/// <summary> Репозиторий обслуживаний. </summary>
public interface IMaintenanceRepository
{
    /// <summary> Получает обслуживания техники пользователя. </summary>
    Task<IEnumerable<Maintenance>> GetByVehicleId(string userId, Guid vehicleId);

    /// <summary> Получает обслуживания техники с учётом организации. </summary>
    Task<IEnumerable<Maintenance>> GetByVehicleId(string userId, Guid? organizationId, Guid vehicleId);

    /// <summary> Получает страницу обслуживаний техники с сортировкой и фильтрами. </summary>
    Task<(IEnumerable<Maintenance> Items, int TotalCount)> GetByVehicleId(string userId, Guid vehicleId, int page, int pageSize, string? sort = null, int? type = null, int? status = null);

    /// <summary> Получает страницу обслуживаний техники с учётом организации. </summary>
    Task<(IEnumerable<Maintenance> Items, int TotalCount)> GetByVehicleId(string userId, Guid? organizationId, Guid vehicleId, int page, int pageSize, string? sort = null, int? type = null, int? status = null);

    /// <summary> Получает страницу удалённых обслуживаний техники. </summary>
    Task<(IEnumerable<Maintenance> Items, int TotalCount)> GetDeletedByVehicleId(string userId, Guid vehicleId, int page, int pageSize);

    /// <summary> Получает страницу удалённых обслуживаний с учётом организации. </summary>
    Task<(IEnumerable<Maintenance> Items, int TotalCount)> GetDeletedByVehicleId(string userId, Guid? organizationId, Guid vehicleId, int page, int pageSize);

    /// <summary> Находит обслуживание по идентификатору для пользователя. </summary>
    Task<Maintenance?> GetById(string userId, Guid id);

    /// <summary> Находит обслуживание по идентификатору с учётом организации. </summary>
    Task<Maintenance?> GetById(string userId, Guid? organizationId, Guid id);

    /// <summary> Проверяет существование обслуживания с заданным идентификатором. </summary>
    Task<bool> ExistsAsync(string userId, Guid? organizationId, Guid id);

    /// <summary> Помечает обслуживание как изменённое. </summary>
    void Update(Maintenance entity);

    /// <summary> Добавляет новое обслуживание. </summary>
    Task Add(Maintenance entity);

    /// <summary> Сохраняет изменения репозитория обслуживаний. </summary>
    Task Save();
}


