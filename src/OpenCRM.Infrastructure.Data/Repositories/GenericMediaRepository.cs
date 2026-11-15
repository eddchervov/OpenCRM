using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Infrastructure.Data.Repositories;

/// <summary> Репозиторий доступа к медиафайлам разных типов. </summary>
public class GenericMediaRepository : IGenericMediaRepository
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Логгер для репозитория. </summary>
    private readonly ILogger<GenericMediaRepository> _logger;

    /// <summary> Создаёт репозиторий с контекстом и логгером. </summary>
    public GenericMediaRepository(OpenCRMContext context, ILogger<GenericMediaRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary> Проверяет доступ пользователя к медиафайлу. </summary>
    public async Task<bool> UserHasAccessToMediaAsync(string? userId, Guid mediaId)
    {
        // Системные файлы доступны всем
        var isSystemFile = await _context.VehiclePhotos
            .AnyAsync(p => p.Id == mediaId && p.UserId == null && !p.IsDeleted) ||
            await _context.MaintenancePhotos
            .AnyAsync(p => p.Id == mediaId && p.UserId == null && !p.IsDeleted);

        if (isSystemFile) return true;

        // Для пользовательских файлов проверяем принадлежность
        if (userId == null) return false;

        var hasAccessToVehiclePhoto = await _context.VehiclePhotos
            .AnyAsync(p =>
                p.Id == mediaId
                && !p.IsDeleted
                && (
                    (p.Vehicle.OrganizationId == null && p.Vehicle.UserId == userId)
                    || (p.Vehicle.OrganizationId != null && _context.Organizations.Any(o =>
                        o.Id == p.Vehicle.OrganizationId
                        && !o.IsDeleted
                        && o.IsActive
                        && !o.IsArchived
                        && o.Members.Any(m => m.UserId == userId)))
                ));

        if (hasAccessToVehiclePhoto) return true;

        var hasAccessToMaintenancePhoto = await _context.MaintenancePhotos
            .AnyAsync(p =>
                p.Id == mediaId
                && !p.IsDeleted
                && (
                    (p.Vehicle.OrganizationId == null && p.Vehicle.UserId == userId)
                    || (p.Vehicle.OrganizationId != null && _context.Organizations.Any(o =>
                        o.Id == p.Vehicle.OrganizationId
                        && !o.IsDeleted
                        && o.IsActive
                        && !o.IsArchived
                        && o.Members.Any(m => m.UserId == userId)))
                ));

        return hasAccessToMaintenancePhoto;
    }

    /// <summary> Возвращает системные медиафайлы заданного типа. </summary>
    public async Task<IEnumerable<T>> GetSystemMediaAsync<T>(MediaType? type = null, bool includeDeleted = false) where T : BaseMedia
    {
        var query = _context.Set<T>()
            .Where(m => m.UserId == null);

        if (type.HasValue)
        {
            query = query.Where(m => m.Type == type.Value);
        }

        if (!includeDeleted)
        {
            query = query.Where(m => !m.IsDeleted);
        }

        return await query.ToListAsync();
    }

    /// <summary> Возвращает медиафайл пользователя по идентификатору. </summary>
    public async Task<T> GetByIdAsync<T>(string userId, Guid mediaId, bool includeDeleted = false) where T : BaseMedia
    {
        var query = _context.Set<T>()
            .Where(m => m.UserId == userId && m.Id == mediaId);

        if (!includeDeleted)
        {
            query = query.Where(m => !m.IsDeleted);
        }

        return await query.FirstOrDefaultAsync();
    }

    /// <summary> Возвращает путь к медиафайлу по идентификатору. </summary>
    public async Task<string?> GetMediaPathAsync(string? userId, Guid mediaId, bool includeDeleted = false)
    {
        // Поиск во всех таблицах медиа
        var vehiclePhoto = await _context.VehiclePhotos
            .Where(p =>
                p.Id == mediaId
                && (includeDeleted || !p.IsDeleted)
                && (
                    (p.Vehicle.OrganizationId == null && p.Vehicle.UserId == userId)
                    || (p.Vehicle.OrganizationId != null && _context.Organizations.Any(o =>
                        o.Id == p.Vehicle.OrganizationId
                        && !o.IsDeleted
                        && o.IsActive
                        && !o.IsArchived
                        && o.Members.Any(m => m.UserId == userId)))
                ))
            .Select(p => p.Path)
            .FirstOrDefaultAsync();

        if (vehiclePhoto != null) return vehiclePhoto;

        var maintenancePhoto = await _context.MaintenancePhotos
            .Where(p =>
                p.Id == mediaId
                && (includeDeleted || !p.IsDeleted)
                && (
                    (p.Vehicle.OrganizationId == null && p.Vehicle.UserId == userId)
                    || (p.Vehicle.OrganizationId != null && _context.Organizations.Any(o =>
                        o.Id == p.Vehicle.OrganizationId
                        && !o.IsDeleted
                        && o.IsActive
                        && !o.IsArchived
                        && o.Members.Any(m => m.UserId == userId)))
                ))
            .Select(p => p.Path)
            .FirstOrDefaultAsync();

        return maintenancePhoto;
    }

    /// <summary> Возвращает идентификаторы медиафайлов пользователя. </summary>
    public async Task<IEnumerable<Guid>> GetMediaIdsByUserAsync(string userId, MediaType? type = null, bool includeDeleted = false)
    {
        var result = new List<Guid>();

        if (!type.HasValue || type == MediaType.VehiclePhoto)
        {
            var vehiclePhotosQuery = _context.VehiclePhotos
                .Where(p => p.UserId == userId);

            if (!includeDeleted)
            {
                vehiclePhotosQuery = vehiclePhotosQuery.Where(p => !p.IsDeleted);
            }

            var vehiclePhotoIds = await vehiclePhotosQuery
                .Select(p => p.Id)
                .ToListAsync();

            result.AddRange(vehiclePhotoIds);
        }

        if (!type.HasValue || type == MediaType.MaintenancePhoto)
        {
            var maintenancePhotosQuery = _context.MaintenancePhotos
                .Where(p => p.UserId == userId);

            if (!includeDeleted)
            {
                maintenancePhotosQuery = maintenancePhotosQuery.Where(p => !p.IsDeleted);
            }

            var maintenancePhotoIds = await maintenancePhotosQuery
                .Select(p => p.Id)
                .ToListAsync();

            result.AddRange(maintenancePhotoIds);
        }

        return result;
    }
}

