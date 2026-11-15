using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces.Media;

namespace OpenCRM.Infrastructure.Data.Repositories;

/// <summary> Репозиторий фотографий обслуживания. </summary>
public class MaintenancePhotoRepository : IMaintenancePhotoRepository
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Логгер для репозитория. </summary>
    private readonly ILogger<MaintenancePhotoRepository> _logger;

    /// <summary> Создаёт репозиторий с контекстом и логгером. </summary>
    public MaintenancePhotoRepository(OpenCRMContext context, ILogger<MaintenancePhotoRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary> Возвращает фотографию обслуживания пользователя по идентификатору. </summary>
    public async Task<MaintenancePhoto> GetByIdAsync(string userId, Guid photoId, bool includeDeleted = false)
    {
        return await GetByIdAsync(userId, null, photoId, includeDeleted);
    }

    /// <summary> Возвращает фотографию обслуживания в рабочем пространстве. </summary>
    public async Task<MaintenancePhoto> GetByIdAsync(string userId, Guid? organizationId, Guid photoId, bool includeDeleted = false)
    {
        var query = BuildWorkspacePhotosQuery(userId, organizationId).Where(p => p.Id == photoId);
        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return await query.Include(p => p.Maintenance).Include(p => p.Vehicle).FirstOrDefaultAsync();
    }

    /// <summary> Возвращает фотографии обслуживания по идентификатору обслуживания. </summary>
    public async Task<IEnumerable<MaintenancePhoto>> GetByMaintenanceIdAsync(string userId, Guid maintenanceId, bool includeDeleted = false)
    {
        return await GetByMaintenanceIdAsync(userId, null, maintenanceId, includeDeleted);
    }

    /// <summary> Возвращает фотографии обслуживания в рабочем пространстве. </summary>
    public async Task<IEnumerable<MaintenancePhoto>> GetByMaintenanceIdAsync(string userId, Guid? organizationId, Guid maintenanceId, bool includeDeleted = false)
    {
        var query = BuildWorkspacePhotosQuery(userId, organizationId).Where(p => p.MaintenanceId == maintenanceId);
        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return await query.Include(p => p.Maintenance).Include(p => p.Vehicle).ToListAsync();
    }

    /// <summary> Возвращает фотографии по нескольким обслуживаниям. </summary>
    public async Task<IEnumerable<MaintenancePhoto>> GetByMaintenanceIdsAsync(string userId, IEnumerable<Guid> maintenanceIds, bool includeDeleted = false)
    {
        return await GetByMaintenanceIdsAsync(userId, null, maintenanceIds, includeDeleted);
    }

    /// <summary> Возвращает фотографии по нескольким обслуживаниям в рабочем пространстве. </summary>
    public async Task<IEnumerable<MaintenancePhoto>> GetByMaintenanceIdsAsync(string userId, Guid? organizationId, IEnumerable<Guid> maintenanceIds, bool includeDeleted = false)
    {
        var query = BuildWorkspacePhotosQuery(userId, organizationId).Where(p => maintenanceIds.Contains(p.MaintenanceId));
        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return await query.ToListAsync();
    }

    /// <summary> Возвращает фотографии обслуживания по технике. </summary>
    public async Task<IEnumerable<MaintenancePhoto>> GetByVehicleIdAsync(string userId, Guid vehicleId, bool includeDeleted = false)
    {
        var query = _context.MaintenancePhotos.Where(p => p.UserId == userId && p.VehicleId == vehicleId);
        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return await query.Include(p => p.Maintenance).Include(p => p.Vehicle).ToListAsync();
    }

    /// <summary> Возвращает фотографии обслуживаний пользователя. </summary>
    public async Task<IEnumerable<MaintenancePhoto>> GetByUserAsync(string userId, bool includeDeleted = false)
    {
        var query = _context.MaintenancePhotos.Where(p => p.UserId == userId);
        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return await query.Include(p => p.Maintenance).Include(p => p.Vehicle).ToListAsync();
    }

    /// <summary> Добавляет фотографию обслуживания. </summary>
    public async Task AddAsync(MaintenancePhoto photo)
    {
        await _context.MaintenancePhotos.AddAsync(photo);
        await _context.SaveChangesAsync();
    }

    /// <summary> Добавляет несколько фотографий обслуживания. </summary>
    public async Task AddRangeAsync(IEnumerable<MaintenancePhoto> photos)
    {
        await _context.MaintenancePhotos.AddRangeAsync(photos);
        await _context.SaveChangesAsync();
    }

    /// <summary> Обновляет фотографию обслуживания. </summary>
    public async Task UpdateAsync(MaintenancePhoto photo)
    {
        _context.MaintenancePhotos.Update(photo);
        await _context.SaveChangesAsync();
    }

    /// <summary> Удаляет фотографию обслуживания по идентификатору. </summary>
    public async Task DeleteAsync(Guid photoId)
    {
        var photo = await _context.MaintenancePhotos.FindAsync(photoId);
        if (photo != null)
        {
            photo.IsDeleted = true;
            _context.MaintenancePhotos.Update(photo);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary> Проверяет существование фотографии у пользователя. </summary>
    public Task<bool> ExistsAsync(string userId, Guid photoId, bool includeDeleted = false)
    {
        var query = _context.MaintenancePhotos.Where(p => p.UserId == userId && p.Id == photoId);
        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return query.AnyAsync();
    }

    /// <summary> Проверяет принадлежность фотографии пользователю. </summary>
    public Task<bool> IsPhotoBelongsToUserAsync(string userId, Guid photoId)
    {
        return _context.MaintenancePhotos.AnyAsync(p => p.UserId == userId && p.Id == photoId && !p.IsDeleted);
    }

    /// <summary> Возвращает количество фотографий обслуживания. </summary>
    public Task<int> GetCountByMaintenanceAsync(string userId, Guid maintenanceId, bool includeDeleted = false)
    {
        var query = _context.MaintenancePhotos.Where(p => p.UserId == userId && p.MaintenanceId == maintenanceId);
        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return query.CountAsync();
    }

    /// <summary> Строит базовый запрос фотографий рабочего пространства. </summary>
    private IQueryable<MaintenancePhoto> BuildWorkspacePhotosQuery(string userId, Guid? organizationId)
    {
        var query = _context.MaintenancePhotos.AsQueryable();

        return organizationId.HasValue
            ? query.Where(p => p.Vehicle.OrganizationId == organizationId.Value)
            : query.Where(p => p.Vehicle.UserId == userId && p.Vehicle.OrganizationId == null);
    }
}
