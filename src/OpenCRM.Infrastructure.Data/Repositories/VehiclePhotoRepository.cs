using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces.Media;

namespace OpenCRM.Infrastructure.Data.Repositories;

/// <summary> Репозиторий фотографий техники. </summary>
public class VehiclePhotoRepository : IVehiclePhotoRepository
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Логгер для репозитория. </summary>
    private readonly ILogger<VehiclePhotoRepository> _logger;

    /// <summary> Создаёт репозиторий с контекстом и логгером. </summary>
    public VehiclePhotoRepository(OpenCRMContext context, ILogger<VehiclePhotoRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary> Возвращает фотографию техники пользователя по идентификатору. </summary>
    public async Task<VehiclePhoto?> GetByIdAsync(string? userId, Guid photoId, bool includeDeleted = false)
    {
        var query = _context.VehiclePhotos.AsQueryable();
        query = userId != null ? query.Where(p => p.UserId == userId) : query.Where(p => p.UserId == null);
        query = query.Where(p => p.Id == photoId);

        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return await query.Include(p => p.Vehicle).FirstOrDefaultAsync();
    }

    /// <summary> Возвращает системную фотографию по идентификатору. </summary>
    public Task<VehiclePhoto?> GetSystemPhotoByIdAsync(Guid photoId, bool includeDeleted = false)
    {
        return GetByIdAsync(null, photoId, includeDeleted);
    }

    /// <summary> Возвращает системные фотографии техники. </summary>
    public async Task<IEnumerable<VehiclePhoto>> GetSystemPhotosAsync(bool includeDeleted = false)
    {
        var query = _context.VehiclePhotos.Where(p => p.UserId == null);
        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return await query.ToListAsync();
    }

    /// <summary> Проверяет существование фотографии техники. </summary>
    public async Task<bool> ExistsAsync(string? userId, Guid photoId, bool includeDeleted = false)
    {
        var query = _context.VehiclePhotos.Where(p => p.Id == photoId);
        query = userId != null ? query.Where(p => p.UserId == userId) : query.Where(p => p.UserId == null);

        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return await query.AnyAsync();
    }

    /// <summary> Проверяет принадлежность фотографии пользователю. </summary>
    public async Task<bool> IsPhotoBelongsToUserAsync(string? userId, Guid photoId)
    {
        if (userId == null)
        {
            return await _context.VehiclePhotos.AnyAsync(p => p.Id == photoId && p.UserId == null && !p.IsDeleted);
        }

        return await _context.VehiclePhotos.AnyAsync(p => p.UserId == userId && p.Id == photoId && !p.IsDeleted);
    }

    /// <summary> Возвращает фотографию техники по идентификатору техники. </summary>
    public Task<VehiclePhoto?> GetByVehicleIdAsync(string userId, Guid vehicleId, bool includeDeleted = false)
    {
        return GetByVehicleIdAsync(userId, null, vehicleId, includeDeleted);
    }

    /// <summary> Возвращает фотографию техники в рабочем пространстве. </summary>
    public Task<VehiclePhoto?> GetByVehicleIdAsync(string userId, Guid? organizationId, Guid vehicleId, bool includeDeleted = false)
    {
        var query = BuildWorkspaceVehiclePhotosQuery(userId, organizationId)
            .Where(p => p.VehicleId == vehicleId);

        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return query
            .OrderByDescending(p => p.IsPrimary)
            .ThenBy(p => p.CreatedAt)
            .FirstOrDefaultAsync();
    }

    /// <summary> Возвращает галерею фотографий техники. </summary>
    public Task<List<VehiclePhoto>> GetGalleryByVehicleIdAsync(string userId, Guid? organizationId, Guid vehicleId, bool includeDeleted = false)
    {
        var query = BuildWorkspaceVehiclePhotosQuery(userId, organizationId)
            .Where(p => p.VehicleId == vehicleId && p.AlbumId == null);

        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return query
            .OrderByDescending(p => p.IsPrimary)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync();
    }

    /// <summary> Возвращает фотографии по нескольким единицам техники. </summary>
    public Task<List<VehiclePhoto>> GetByVehicleIdsAsync(string userId, IEnumerable<Guid> vehicleIds, bool includeDeleted = false)
    {
        return GetByVehicleIdsAsync(userId, null, vehicleIds, includeDeleted);
    }

    /// <summary> Возвращает фотографии по нескольким единицам техники в рабочем пространстве. </summary>
    public Task<List<VehiclePhoto>> GetByVehicleIdsAsync(string userId, Guid? organizationId, IEnumerable<Guid> vehicleIds, bool includeDeleted = false)
    {
        var query = BuildWorkspaceVehiclePhotosQuery(userId, organizationId)
            .Where(p => vehicleIds.Contains(p.VehicleId) && p.AlbumId == null);

        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return query
            .OrderByDescending(p => p.IsPrimary)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync();
    }

    /// <summary> Возвращает фотографии техники пользователя. </summary>
    public async Task<IEnumerable<VehiclePhoto>> GetByUserAsync(string userId, bool includeDeleted = false)
    {
        var query = _context.VehiclePhotos.Where(p => p.UserId == userId);
        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return await query.Include(p => p.Vehicle).ToListAsync();
    }

    /// <summary> Добавляет фотографию техники. </summary>
    public async Task AddAsync(VehiclePhoto photo)
    {
        await _context.VehiclePhotos.AddAsync(photo);
        await _context.SaveChangesAsync();
    }

    /// <summary> Обновляет фотографию техники. </summary>
    public async Task UpdateAsync(VehiclePhoto photo)
    {
        _context.VehiclePhotos.Update(photo);
        await _context.SaveChangesAsync();
    }

    /// <summary> Удаляет фотографию техники по идентификатору. </summary>
    public async Task DeleteAsync(Guid photoId)
    {
        var photo = await _context.VehiclePhotos.FindAsync(photoId);
        if (photo != null)
        {
            photo.IsDeleted = true;
            _context.VehiclePhotos.Update(photo);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary> Проверяет наличие фотографии у техники. </summary>
    public async Task<bool> HasVehiclePhotoAsync(string? userId, Guid vehicleId, bool includeDeleted = false)
    {
        var query = _context.VehiclePhotos.Where(p => p.UserId == userId && p.VehicleId == vehicleId);
        if (!includeDeleted)
        {
            query = query.Where(p => !p.IsDeleted);
        }

        return await query.AnyAsync();
    }

    /// <summary> Строит базовый запрос фотографий техники рабочего пространства. </summary>
    private IQueryable<VehiclePhoto> BuildWorkspaceVehiclePhotosQuery(string userId, Guid? organizationId)
    {
        var query = _context.VehiclePhotos.AsQueryable();

        return organizationId.HasValue
            ? query.Where(p => p.Vehicle.OrganizationId == organizationId.Value)
            : query.Where(p => p.Vehicle.UserId == userId && p.Vehicle.OrganizationId == null);
    }
}



