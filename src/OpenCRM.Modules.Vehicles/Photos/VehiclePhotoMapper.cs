using OpenCRM.Domain;
using OpenCRM.Modules.Vehicles.Contracts.Photos;

namespace OpenCRM.Modules.Vehicles.Photos;

/// <summary> Преобразование фотографий и альбомов в DTO. </summary>
internal static class VehiclePhotoMapper
{
    /// <summary> Преобразует фотографию техники в DTO. </summary>
    internal static VehiclePhotoDto MapPhoto(VehiclePhoto photo)
    {
        return new VehiclePhotoDto
        {
            PhotoId = photo.Id,
            VehicleId = photo.VehicleId,
            AlbumId = photo.AlbumId,
            IsPrimary = photo.IsPrimary,
            OriginalFileName = photo.OriginalFileName,
            Size = photo.Size,
            ContentType = photo.ContentType,
            CreatedAt = photo.CreatedAt
        };
    }

    /// <summary> Преобразует фотоальбом в DTO. </summary>
    internal static VehiclePhotoAlbumDto MapAlbum(VehiclePhotoAlbum album)
    {
        var photos = album.Photos
            .Where(photo => !photo.IsDeleted)
            .OrderByDescending(photo => photo.IsPrimary)
            .ThenBy(photo => photo.CreatedAt)
            .Select(VehiclePhotoMapper.MapPhoto)
            .ToList();

        return new VehiclePhotoAlbumDto
        {
            AlbumId = album.Id,
            VehicleId = album.VehicleId,
            Title = album.Title,
            CreatedAt = album.CreatedAt,
            PhotoCount = photos.Count,
            PrimaryPhotoId = photos.FirstOrDefault(photo => photo.IsPrimary)?.PhotoId ?? photos.FirstOrDefault()?.PhotoId,
            Photos = photos
        };
    }
}
