using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles.Contracts.Photos;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Media.Dto;

namespace OpenCRM.Modules.Vehicles.Photos;

/// <summary> Сервис галереи фотографий техники. </summary>
internal sealed class VehicleGalleryService : IVehicleGalleryService
{
    /// <summary> Максимальное число фотографий в галерее техники. </summary>
    private const int MaxVehicleGalleryPhotos = 10;

    /// <summary> Тип события истории о загрузке фото техники. </summary>
    private const string EventVehiclePhotoUploaded = "vehicle_photo_uploaded";

    /// <summary> Тип события истории об удалении фото техники. </summary>
    private const string EventVehiclePhotoDeleted = "vehicle_photo_deleted";

    /// <summary> Репозиторий фотографий техники. </summary>
    private readonly IVehiclePhotoRepository _vehiclePhotoRepository;

    /// <summary> Сервис обработки изображений. </summary>
    private readonly IImageProcessingService _imageProcessingService;

    /// <summary> Репозиторий техники. </summary>
    private readonly IVehicleRepository _vehicleRepository;

    /// <summary> Кэш в памяти для данных фотографий. </summary>
    private readonly IMemoryCache _cache;

    /// <summary> Длительность хранения кэша фотографий. </summary>
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);

    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Записывает события в историю техники. </summary>
    private readonly VehicleHistoryWriter _historyWriter;

    /// <summary> Доступ к медиа-хранилищу рабочего пространства. </summary>
    private readonly IMediaWorkspaceAccess _workspaceAccess;

    /// <summary> Создаёт сервис галереи фотографий с зависимостями. </summary>
    public VehicleGalleryService(
        IVehiclePhotoRepository vehiclePhotoRepository,
        IImageProcessingService imageProcessingService,
        IVehicleRepository vehicleRepository,
        IMemoryCache cache,
        OpenCRMContext context,
        VehicleHistoryWriter historyWriter,
        IMediaWorkspaceAccess workspaceAccess)
    {
        this._vehiclePhotoRepository = vehiclePhotoRepository;
        this._imageProcessingService = imageProcessingService;
        this._vehicleRepository = vehicleRepository;
        this._cache = cache;
        this._context = context;
        this._historyWriter = historyWriter;
        this._workspaceAccess = workspaceAccess;
    }

    /// <summary> Загружает фотографию техники пользователя. </summary>
    public async Task<Guid> UploadVehiclePhotoAsync(string userId, Guid vehicleId,
        Stream imageStream, string fileName)
    {
        return await this.UploadVehiclePhotoAsync(userId, null, vehicleId, imageStream, fileName);
    }

    /// <summary> Загружает фотографию техники с учётом организации. </summary>
    public async Task<Guid> UploadVehiclePhotoAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Stream imageStream,
        string fileName)
    {
        var photoIds = await this.UploadVehiclePhotosAsync(userId, organizationId, vehicleId, [(imageStream, fileName)]);

        return photoIds.First();
    }

    /// <summary> Загружает несколько фотографий техники за раз. </summary>
    public async Task<IReadOnlyList<Guid>> UploadVehiclePhotosAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        IEnumerable<(Stream Stream, string FileName)> files)
    {
        await this.EnsureCanChangePhotosAsync(userId, organizationId);
        _ = await this._vehicleRepository.Get(userId, organizationId, vehicleId) ?? throw new ArgumentException($"Vehicle not found: userId={userId}, vehicleId={vehicleId}");

        var fileList = files.ToList();
        if (fileList.Count == 0)
        {
            throw new InvalidOperationException("Выберите фотографии для загрузки.");
        }

        var currentPhotos = await this._vehiclePhotoRepository.GetGalleryByVehicleIdAsync(userId, organizationId, vehicleId);
        if (currentPhotos.Count + fileList.Count > VehicleGalleryService.MaxVehicleGalleryPhotos)
        {
            throw new InvalidOperationException($"Для техники можно загрузить максимум {VehicleGalleryService.MaxVehicleGalleryPhotos} фото.");
        }

        var hasPrimary = currentPhotos.Any(x => x.IsPrimary);
        var photosToAdd = new List<VehiclePhoto>();
        var options = new ImageProcessingOptions
        {
            TargetFolder = VehiclePhotoWorkspace.BuildTargetFolder(userId, organizationId),
            MaxWidth = 1200,
            MaxHeight = 800,
            Quality = 70
        };

        foreach (var (imageStream, fileName) in fileList)
        {
            var imagePath = await this._imageProcessingService.ProcessAndSaveImageAsync(imageStream, options);
            var contentType = await this._imageProcessingService.GetImageFormatAsync(imageStream);
            var photo = new VehiclePhoto
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Path = imagePath,
                Type = MediaType.VehiclePhoto,
                CreatedAt = DateTime.UtcNow,
                VehicleId = vehicleId,
                Size = imageStream.Length,
                ContentType = contentType,
                OriginalFileName = fileName,
                IsPrimary = !hasPrimary && photosToAdd.Count == 0
            };

            photosToAdd.Add(photo);
        }

        await this._context.VehiclePhotos.AddRangeAsync(photosToAdd);
        await this._context.SaveChangesAsync();

        await this._historyWriter.AddPhotoEventAsync(
            userId,
            organizationId,
            vehicleId,
            VehicleGalleryService.EventVehiclePhotoUploaded,
            photosToAdd.Count == 1 ? "Фотография техники загружена" : "Фотографии техники загружены",
            MediaPhotoDescription.Build("Файлы", photosToAdd.Select(x => x.OriginalFileName)));

        this.InvalidateVehiclePhotoCache(userId, vehicleId);
        return photosToAdd.Select(x => x.Id).ToList();
    }

    /// <summary> Возвращает список фотографий техники. </summary>
    public async Task<IReadOnlyList<VehiclePhotoDto>> GetVehiclePhotosAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId)
    {
        await this._workspaceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);
        _ = await this._vehicleRepository.Get(userId, organizationId, vehicleId) ?? throw new ArgumentException($"Vehicle not found: userId={userId}, vehicleId={vehicleId}");

        var photos = await this._vehiclePhotoRepository.GetGalleryByVehicleIdAsync(userId, organizationId, vehicleId);

        return photos.Select(VehiclePhotoMapper.MapPhoto).ToList();
    }

    /// <summary> Возвращает фотографии нескольких техник. </summary>
    public async Task<IReadOnlyList<VehiclePhotoDto>> GetVehiclePhotosAsync(
        string userId,
        Guid? organizationId,
        IEnumerable<Guid> vehicleIds)
    {
        await this._workspaceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);
        var ids = vehicleIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var photos = await this._vehiclePhotoRepository.GetByVehicleIdsAsync(userId, organizationId, ids);

        return photos.Select(VehiclePhotoMapper.MapPhoto).ToList();
    }

    /// <summary> Удаляет фотографию техники пользователя. </summary>
    public async Task DeleteVehiclePhotoAsync(string userId, Guid vehicleId)
    {
        await this.DeleteVehiclePhotoAsync(userId, null, vehicleId);
    }

    /// <summary> Удаляет фотографию техники с учётом организации. </summary>
    public async Task DeleteVehiclePhotoAsync(string userId, Guid? organizationId, Guid vehicleId)
    {
        await this.EnsureCanChangePhotosAsync(userId, organizationId);
        var photos = await this._vehiclePhotoRepository.GetGalleryByVehicleIdAsync(userId, organizationId, vehicleId);
        var photo = photos.FirstOrDefault();
        if (photo == null)
        {
            throw new ArgumentException($"Vehicle photo not found: userId={userId}, vehicleId={vehicleId}");
        }

        await this.DeleteVehiclePhotoCoreAsync(userId, organizationId, vehicleId, photo);
    }

    /// <summary> Удаляет указанную фотографию техники. </summary>
    public async Task DeleteVehiclePhotoAsync(string userId, Guid? organizationId, Guid vehicleId, Guid photoId)
    {
        await this.EnsureCanChangePhotosAsync(userId, organizationId);

        var photo = await this._context.VehiclePhotos
            .Include(x => x.Vehicle)
            .FirstOrDefaultAsync(x => x.Id == photoId && x.VehicleId == vehicleId && x.AlbumId == null && !x.IsDeleted);

        if (photo == null)
        {
            throw new ArgumentException($"Vehicle photo not found: userId={userId}, photoId={photoId}");
        }

        VehiclePhotoWorkspace.EnsureAccess(userId, organizationId, photo.Vehicle);

        await this.DeleteVehiclePhotoCoreAsync(userId, organizationId, vehicleId, photo);
    }

    /// <summary> Назначает фотографию основной для техники. </summary>
    public async Task SetPrimaryVehiclePhotoAsync(string userId, Guid? organizationId, Guid vehicleId, Guid photoId)
    {
        await this.EnsureCanChangePhotosAsync(userId, organizationId);
        var photos = await this._vehiclePhotoRepository.GetGalleryByVehicleIdAsync(userId, organizationId, vehicleId);
        if (photos.All(x => x.Id != photoId))
        {
            throw new ArgumentException($"Vehicle photo not found: userId={userId}, photoId={photoId}");
        }

        foreach (var photo in photos)
        {
            photo.IsPrimary = photo.Id == photoId;
        }

        await this._context.SaveChangesAsync();
        this.InvalidateVehiclePhotoCache(userId, vehicleId);
    }

    /// <summary> Возвращает путь к основной фотографии техники. </summary>
    public Task<string?> GetVehiclePhotoPathAsync(string userId, Guid vehicleId)
    {
        var cacheKey = $"vehicle_photo_path_{userId}_{vehicleId}";

        return this._cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = this._cacheDuration;
            var photo = await this._vehiclePhotoRepository.GetByVehicleIdAsync(userId, vehicleId);

            return photo?.Path;
        });
    }

    /// <summary> Выполняет удаление фотографии техники. </summary>
    private async Task DeleteVehiclePhotoCoreAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        VehiclePhoto photo)
    {
        await this._vehiclePhotoRepository.DeleteAsync(photo.Id);
        if (photo.IsPrimary)
        {
            var nextPhoto = await this._context.VehiclePhotos
                .Where(x => x.VehicleId == vehicleId && x.AlbumId == null && !x.IsDeleted && x.Id != photo.Id)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync();
            if (nextPhoto != null)
            {
                nextPhoto.IsPrimary = true;
                await this._context.SaveChangesAsync();
            }
        }

        await this._historyWriter.AddPhotoEventAsync(
            userId,
            organizationId,
            vehicleId,
            VehicleGalleryService.EventVehiclePhotoDeleted,
            "Фотография техники удалена",
            MediaPhotoDescription.Build("Файл", [photo.OriginalFileName]));

        this.InvalidateVehiclePhotoCache(userId, vehicleId);
    }

    /// <summary> Проверяет право изменять фотографии рабочего пространства. </summary>
    private async Task EnsureCanChangePhotosAsync(string userId, Guid? organizationId)
    {
        await this._workspaceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId);
    }

    /// <summary> Сбрасывает кэш фотографий техники. </summary>
    private void InvalidateVehiclePhotoCache(string userId, Guid vehicleId) => _cache.Remove($"vehicle_photo_path_{userId}_{vehicleId}");
}
