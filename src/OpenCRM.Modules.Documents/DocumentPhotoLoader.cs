using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Modules.Documents;

/// <summary> Загрузчик фотографий для документов обслуживания. </summary>
public sealed class DocumentPhotoLoader(
    IMaintenancePhotoService maintenancePhotoService,
    IMediaAccessService mediaAccessService,
    IFileStorageService fileStorageService)
{
    /// <summary> Возвращает данные фотографии по идентификатору. </summary>
    public async Task<byte[]?> GetPhotoDataAsync(string userId, Guid photoId)
    {
        try
        {
            if (!await mediaAccessService.UserHasAccessToMediaAsync(userId, photoId))
            {
                return null;
            }

            var filePath = await mediaAccessService.GetMediaPathAsync(userId, photoId);
            if (string.IsNullOrEmpty(filePath) || !await fileStorageService.FileExistsAsync(filePath))
            {
                return null;
            }

            using var fileStream = await fileStorageService.GetFileAsync(filePath);
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);

            return memoryStream.ToArray();
        }
        catch (Exception exception)
        {
            // Ошибка чтения фотографии не должна прерывать экспорт документа.
            Console.WriteLine($"Error getting photo data: {exception.Message}");

            return null;
        }
    }

    /// <summary> Загружает фотографии обслуживания для PDF-документа. </summary>
    public async Task<List<PhotoInfo>> LoadPdfMaintenancePhotosAsync(string userId, Guid? organizationId, Guid maintenanceId)
    {
        var result = new List<PhotoInfo>();
        var photos = await maintenancePhotoService.GetMaintenancePhotosAsync(userId, organizationId, maintenanceId);
        var orderedPhotos = photos.OrderBy(photo => photo.Stage).ThenBy(photo => photo.CreatedAt);

        foreach (var photo in orderedPhotos)
        {
            var imageData = await this.GetPhotoDataAsync(userId, photo.PhotoId);
            if (imageData?.Length > 0)
            {
                result.Add(new PhotoInfo
                {
                    ImageData = imageData,
                    Caption = photo.Stage switch
                    {
                        MaintenancePhotoStage.Before => "До работы",
                        MaintenancePhotoStage.After => "После работы",
                        _ => null
                    }
                });
            }
        }

        return result;
    }

    /// <summary> Загружает фотографии для Word-документа. </summary>
    public async Task<List<WordPhotoInfo>> LoadWordPhotosAsync(string userId, IEnumerable<Guid> photoIds)
    {
        var result = new List<WordPhotoInfo>();

        foreach (var photoId in photoIds.Take(12))
        {
            var imageData = await this.GetPhotoDataAsync(userId, photoId);
            if (imageData?.Length > 0)
            {
                result.Add(new WordPhotoInfo { ImageData = imageData });
            }
        }

        return result;
    }

    /// <summary> Загружает фотографии приёма до начала обслуживания. </summary>
    public async Task<List<PhotoInfo>> LoadIntakeBeforePhotosAsync(string userId, Guid? organizationId, Guid maintenanceId)
    {
        var result = new List<PhotoInfo>();
        var photos = await maintenancePhotoService.GetMaintenancePhotosAsync(userId, organizationId, maintenanceId);
        var beforePhotos = photos
            .Where(photo => photo.Stage == MaintenancePhotoStage.Before)
            .OrderBy(photo => photo.CreatedAt);

        foreach (var photo in beforePhotos)
        {
            var imageData = await this.GetPhotoDataAsync(userId, photo.PhotoId);
            if (imageData?.Length > 0)
            {
                result.Add(new PhotoInfo { ImageData = imageData, Caption = "До обслуживания", PhotoId = photo.PhotoId });
            }
        }

        return result;
    }
}
