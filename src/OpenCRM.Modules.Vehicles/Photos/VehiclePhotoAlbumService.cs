using OpenCRM.Modules.Billing.Contracts;
using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles.Contracts.Photos;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Media.Dto;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Vehicles.Photos;

/// <summary> Сервис фотоальбомов техники. </summary>
internal sealed class VehiclePhotoAlbumService : IVehiclePhotoAlbumService
{
    /// <summary> Лимит альбомов для бесплатного или демо-тарифа. </summary>
    private const int FreeOrDemoAlbumLimit = 1;

    /// <summary> Лимит альбомов для платного тарифа. </summary>
    private const int PaidAlbumLimit = 100;

    /// <summary> Сервис обработки изображений. </summary>
    private readonly IImageProcessingService _imageProcessingService;

    /// <summary> Репозиторий техники. </summary>
    private readonly IVehicleRepository _vehicleRepository;

    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Сервис подписки организации для проверок тарифа. </summary>
    private readonly IOrganizationSubscriptionService _organizationSubscriptionService;

    /// <summary> Доступ к медиа-хранилищу рабочего пространства. </summary>
    private readonly IMediaWorkspaceAccess _workspaceAccess;

    private readonly IPlatformCommercialPolicy? _platformCommercialPolicy;

    /// <summary> Создаёт сервис фотоальбомов с зависимостями. </summary>
    public VehiclePhotoAlbumService(
        IImageProcessingService imageProcessingService,
        IVehicleRepository vehicleRepository,
        OpenCRMContext context,
        IOrganizationSubscriptionService organizationSubscriptionService,
        IMediaWorkspaceAccess workspaceAccess,
        IPlatformCommercialPolicy? platformCommercialPolicy = null)
    {
        this._imageProcessingService = imageProcessingService;
        this._vehicleRepository = vehicleRepository;
        this._context = context;
        this._organizationSubscriptionService = organizationSubscriptionService;
        this._workspaceAccess = workspaceAccess;
        this._platformCommercialPolicy = platformCommercialPolicy;
    }

    /// <summary> Возвращает фотоальбомы техники. </summary>
    public async Task<IReadOnlyList<VehiclePhotoAlbumDto>> GetVehiclePhotoAlbumsAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId)
    {
        await this._workspaceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);
        _ = await this._vehicleRepository.Get(userId, organizationId, vehicleId) ?? throw new ArgumentException($"Vehicle not found: userId={userId}, vehicleId={vehicleId}");

        var albums = await this._context.VehiclePhotoAlbums
            .AsNoTracking()
            .Include(x => x.Photos.Where(p => !p.IsDeleted))
            .Where(x => x.VehicleId == vehicleId && !x.IsDeleted)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        return albums.Select(VehiclePhotoMapper.MapAlbum).ToList();
    }

    /// <summary> Возвращает фотоальбом техники по идентификатору. </summary>
    public async Task<VehiclePhotoAlbumDto> GetVehiclePhotoAlbumAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid albumId)
    {
        await this._workspaceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);
        _ = await this._vehicleRepository.Get(userId, organizationId, vehicleId) ?? throw new ArgumentException($"Vehicle not found: userId={userId}, vehicleId={vehicleId}");

        var album = await this._context.VehiclePhotoAlbums
            .AsNoTracking()
            .Include(x => x.Photos.Where(p => !p.IsDeleted))
            .Where(x => x.Id == albumId && x.VehicleId == vehicleId && !x.IsDeleted)
            .FirstOrDefaultAsync()
            ?? throw new ArgumentException($"Vehicle photo album not found: albumId={albumId}");

        return VehiclePhotoMapper.MapAlbum(album);
    }

    /// <summary> Возвращает лимит фотоальбомов для техники. </summary>
    public async Task<VehiclePhotoAlbumLimitDto> GetVehiclePhotoAlbumLimitAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId)
    {
        await this._workspaceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);
        _ = await this._vehicleRepository.Get(userId, organizationId, vehicleId) ?? throw new ArgumentException($"Vehicle not found: userId={userId}, vehicleId={vehicleId}");

        var isLimited = await this.IsFreeOrDemoPlanAsync(userId, organizationId);
        var currentCount = await this._context.VehiclePhotoAlbums.CountAsync(x => x.VehicleId == vehicleId && !x.IsDeleted);
        var hasAlbums = !organizationId.HasValue
            || await this._organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, FeatureCodes.VehiclePhotoAlbums);

        return new VehiclePhotoAlbumLimitDto
        {
            CurrentCount = currentCount,
            MaxCount = !hasAlbums ? 0 : isLimited ? VehiclePhotoAlbumService.FreeOrDemoAlbumLimit : VehiclePhotoAlbumService.PaidAlbumLimit,
            IsLimitedByPlan = isLimited
        };
    }

    /// <summary> Создаёт фотоальбом для техники. </summary>
    public async Task<VehiclePhotoAlbumDto> CreateVehiclePhotoAlbumAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        string title)
    {
        await this._workspaceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId);
        _ = await this._vehicleRepository.Get(userId, organizationId, vehicleId) ?? throw new ArgumentException($"Vehicle not found: userId={userId}, vehicleId={vehicleId}");

        var limit = await this.GetVehiclePhotoAlbumLimitAsync(userId, organizationId, vehicleId);
        if (limit.MaxCount == 0)
        {
            throw new InvalidOperationException("Новые фотоальбомы доступны в PRO. Сохранённые фотографии можно просматривать.");
        }

        if (!limit.CanCreateMore)
        {
            throw new InvalidOperationException(limit.IsLimitedByPlan
                ? "На бесплатном и демо-тарифе можно создать только 1 альбом для техники."
                : $"Для техники можно создать максимум {limit.MaxCount} альбомов.");
        }

        var album = new VehiclePhotoAlbum
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            VehicleId = vehicleId,
            Title = VehiclePhotoAlbumService.NormalizeAlbumTitle(title),
            CreatedAt = DateTime.UtcNow
        };

        this._context.VehiclePhotoAlbums.Add(album);
        await this._context.SaveChangesAsync();

        return VehiclePhotoMapper.MapAlbum(album);
    }

    /// <summary> Удаляет фотоальбом техники. </summary>
    public async Task DeleteVehiclePhotoAlbumAsync(string userId, Guid? organizationId, Guid vehicleId, Guid albumId)
    {
        await this._workspaceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId);
        var album = await this.GetAlbumForWriteAsync(userId, organizationId, vehicleId, albumId);
        album.IsDeleted = true;
        foreach (var photo in album.Photos.Where(x => !x.IsDeleted))
        {
            photo.IsDeleted = true;
        }

        await this._context.SaveChangesAsync();
    }

    /// <summary> Загружает фотографии в альбом техники. </summary>
    public async Task<IReadOnlyList<Guid>> UploadVehicleAlbumPhotosAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid albumId,
        IEnumerable<(Stream Stream, string FileName)> files)
    {
        await this._workspaceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId);
        if (organizationId.HasValue
            && !await this._organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, FeatureCodes.VehiclePhotoAlbums))
        {
            throw new InvalidOperationException("Загрузка новых фотографий доступна в PRO.");
        }

        var album = await this.GetAlbumForWriteAsync(userId, organizationId, vehicleId, albumId);
        var fileList = files.ToList();
        if (fileList.Count == 0)
        {
            throw new InvalidOperationException("Выберите фотографии для загрузки.");
        }

        var hasPrimary = album.Photos.Any(x => !x.IsDeleted && x.IsPrimary);
        var photosToAdd = new List<VehiclePhoto>();
        var options = new ImageProcessingOptions
        {
            TargetFolder = $"{VehiclePhotoWorkspace.BuildTargetFolder(userId, organizationId)}/albums/{albumId:D}",
            MaxWidth = 1200,
            MaxHeight = 800,
            Quality = 70
        };

        foreach (var (imageStream, fileName) in fileList)
        {
            var imagePath = await this._imageProcessingService.ProcessAndSaveImageAsync(imageStream, options);
            var contentType = await this._imageProcessingService.GetImageFormatAsync(imageStream);
            photosToAdd.Add(new VehiclePhoto
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Path = imagePath,
                Type = MediaType.VehiclePhoto,
                CreatedAt = DateTime.UtcNow,
                VehicleId = vehicleId,
                AlbumId = albumId,
                Size = imageStream.Length,
                ContentType = contentType,
                OriginalFileName = fileName,
                IsPrimary = !hasPrimary && photosToAdd.Count == 0
            });
        }

        this._context.VehiclePhotos.AddRange(photosToAdd);
        await this._context.SaveChangesAsync();

        return photosToAdd.Select(x => x.Id).ToList();
    }

    /// <summary> Удаляет фотографию из альбома техники. </summary>
    public async Task DeleteVehicleAlbumPhotoAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid albumId,
        Guid photoId)
    {
        await this._workspaceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId);
        var photo = await this.GetAlbumPhotoForWriteAsync(userId, organizationId, vehicleId, albumId, photoId);
        photo.IsDeleted = true;
        if (photo.IsPrimary)
        {
            var nextPhoto = await this._context.VehiclePhotos
                .Where(x => x.AlbumId == albumId && !x.IsDeleted && x.Id != photoId)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync();
            if (nextPhoto != null)
            {
                nextPhoto.IsPrimary = true;
            }
        }

        await this._context.SaveChangesAsync();
    }

    /// <summary> Назначает фотографию альбома основной. </summary>
    public async Task SetPrimaryVehicleAlbumPhotoAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid albumId,
        Guid photoId)
    {
        await this._workspaceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId);
        _ = await this.GetAlbumPhotoForWriteAsync(userId, organizationId, vehicleId, albumId, photoId);
        var photos = await this._context.VehiclePhotos
            .Where(x => x.AlbumId == albumId && !x.IsDeleted)
            .ToListAsync();

        foreach (var photo in photos)
        {
            photo.IsPrimary = photo.Id == photoId;
        }

        await this._context.SaveChangesAsync();
    }

    /// <summary> Возвращает альбом с проверкой прав на изменение. </summary>
    private async Task<VehiclePhotoAlbum> GetAlbumForWriteAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid albumId)
    {
        await this.EnsureCanChangeAlbumsAsync(userId, organizationId);
        var album = await this._context.VehiclePhotoAlbums
            .Include(x => x.Vehicle)
            .Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.Id == albumId && x.VehicleId == vehicleId && !x.IsDeleted)
            ?? throw new ArgumentException($"Vehicle photo album not found: albumId={albumId}");

        VehiclePhotoWorkspace.EnsureAccess(userId, organizationId, album.Vehicle);

        return album;
    }

    /// <summary> Возвращает фотографию альбома с проверкой прав на изменение. </summary>
    private async Task<VehiclePhoto> GetAlbumPhotoForWriteAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid albumId,
        Guid photoId)
    {
        await this.EnsureCanChangeAlbumsAsync(userId, organizationId);
        var photo = await this._context.VehiclePhotos
            .Include(x => x.Vehicle)
            .FirstOrDefaultAsync(x => x.Id == photoId && x.VehicleId == vehicleId && x.AlbumId == albumId && !x.IsDeleted)
            ?? throw new ArgumentException($"Vehicle album photo not found: photoId={photoId}");

        VehiclePhotoWorkspace.EnsureAccess(userId, organizationId, photo.Vehicle);

        return photo;
    }

    /// <summary> Сохраняет фотоисторию доступной для чтения после окончания PRO. </summary>
    private async Task EnsureCanChangeAlbumsAsync(string userId, Guid? organizationId)
    {
        if (organizationId.HasValue
            && !await this._organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, FeatureCodes.VehiclePhotoAlbums))
        {
            throw new InvalidOperationException("Изменение фотоальбомов доступно в PRO.");
        }
    }

    /// <summary> Проверяет, использует ли пользователь бесплатный или демо-тариф. </summary>
    private async Task<bool> IsFreeOrDemoPlanAsync(string userId, Guid? organizationId)
    {
        if (this._platformCommercialPolicy?.CommercialEnabled != true)
        {
            return false;
        }

        if (organizationId.HasValue)
        {
            var plan = await this._organizationSubscriptionService.GetEffectivePlanAsync(userId, organizationId.Value);

            return plan == null || plan.IsDefaultPlan || plan.IsDemoPlan || plan.EffectivePrice <= 0;
        }

        var now = DateTime.UtcNow;
        var currentPlan = await this._context.UserSubscriptions
            .AsNoTracking()
            .Include(x => x.SubscriptionPlan)
            .Where(x => x.UserId == userId && x.EndDate >= now)
            .Select(x => x.SubscriptionPlan)
            .FirstOrDefaultAsync();

        currentPlan ??= await this._context.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsDefaultPlan && x.IsActive && x.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal);

        return currentPlan == null || currentPlan.IsDefaultPlan || currentPlan.IsDemoPlan || currentPlan.EffectivePrice <= 0;
    }

    /// <summary> Нормализует название альбома. </summary>
    private static string NormalizeAlbumTitle(string? title)
    {
        title = string.IsNullOrWhiteSpace(title) ? "Новый альбом" : title.Trim();

        if (title.Contains('<') || title.Contains('>') || title.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Название альбома не должно содержать HTML-теги или скрипты.");
        }

        if (title.Length > 120)
        {
            throw new InvalidOperationException("Название альбома не должно быть длиннее 120 символов.");
        }

        return title;
    }
}
