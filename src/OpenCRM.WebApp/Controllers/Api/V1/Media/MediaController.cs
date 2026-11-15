using OpenCRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Modules.Vehicles.Contracts.Photos;
using OpenCRM.WebApp.Controllers.Api.V1;

namespace OpenCRM.WebApp.Controllers.Api.V1.Media;

/// <summary> API-контроллер медиафайлов: фото автомобилей, альбомов и обслуживаний. </summary>
[ApiController]
[Route("api/v1/media")]
[Authorize]
public class MediaController(
    IMediaAccessService mediaAccessService,
    IMaintenancePhotoService maintenancePhotoService,
    IVehicleGalleryService vehicleGalleryService,
    IVehiclePhotoAlbumService vehiclePhotoAlbumService,
    IFileStorageService fileStorageService) : ControllerBase
{
    /// <summary> Максимальный размер одного фото автомобиля в байтах. </summary>
    private const long MaxVehiclePhotoSizeBytes = 30 * 1024 * 1024;

    /// <summary> Максимальный общий размер пакета фото автомобиля в байтах. </summary>
    private const long MaxVehiclePhotoBatchSizeBytes = 100 * 1024 * 1024;

    /// <summary> Максимальное количество фото автомобиля в одной загрузке. </summary>
    private const int MaxVehiclePhotoCount = 10;

    /// <summary> Максимальное количество автомобилей в массовом запросе фото. </summary>
    private const int MaxVehiclePhotoBulkVehicleCount = 100;

    /// <summary> Максимальный общий размер пакета фото обслуживания в байтах. </summary>
    private const long MaxMaintenancePhotoBatchSizeBytes = 100 * 1024 * 1024;

    /// <summary> Максимальное количество фото обслуживания в одной загрузке. </summary>
    private const int MaxMaintenancePhotoCount = 10;

    /// <summary> Определяет, является ли исключение ошибкой отказа в доступе. </summary>
    private static bool IsAccessDeniedError(Exception exception)
    {
        return exception.Message.Contains("нет доступа", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("только на просмотр", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("только для чтения", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Определяет, является ли исключение ошибкой отсутствия сущности. </summary>
    private static bool IsNotFoundError(Exception exception)
    {
        return exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("не найден", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("does not belong", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("не принадлежит", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Получает идентификатор текущего пользователя или ошибку. </summary>
    private ActionResult<string> GetCurrentUserId()
    {
        var userId = this.GetApiUserId();
        return string.IsNullOrWhiteSpace(userId) ? Unauthorized() : userId;
    }

    /// <summary> Загружает одиночное фото автомобиля. </summary>
    [HttpPost("vehicles/{vehicleId:guid}/photo")]
    [HttpPost("vehicles/{vehicleId:guid}/upload-photo")]
    [RequestSizeLimit(MaxVehiclePhotoSizeBytes)]
    public async Task<ActionResult<Guid>> UploadVehiclePhoto(Guid vehicleId, [FromForm] IFormFile formFile, [FromQuery] Guid? organizationId = null)
    {
        var validationError = ValidateSingleImage(formFile, MaxVehiclePhotoSizeBytes);
        if (validationError != null) return validationError;

        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await using var stream = formFile!.OpenReadStream();
            var photoId = await vehicleGalleryService.UploadVehiclePhotoAsync(userId, organizationId, vehicleId, stream, formFile.FileName);
            return Ok(photoId);
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Возвращает список фото указанного автомобиля. </summary>
    [HttpGet("vehicles/{vehicleId:guid}/photos")]
    public async Task<ActionResult<IEnumerable<VehiclePhotoResponse>>> GetVehiclePhotos(Guid vehicleId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            var photos = await vehicleGalleryService.GetVehiclePhotosAsync(userId, organizationId, vehicleId);
            return Ok(await BuildVehiclePhotoResponsesAsync(userId, photos));
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Возвращает фото сразу по нескольким автомобилям. </summary>
    [HttpGet("vehicles/photos")]
    public async Task<ActionResult<IEnumerable<VehiclePhotosResponse>>> GetVehiclePhotos([FromQuery] Guid[] vehicleIds, [FromQuery] Guid? organizationId = null)
    {
        if (vehicleIds == null || vehicleIds.Length == 0)
        {
            return BadRequest(new { message = "Передайте хотя бы одну технику." });
        }

        var distinctVehicleIds = vehicleIds.Distinct().ToList();
        if (distinctVehicleIds.Count > MaxVehiclePhotoBulkVehicleCount)
        {
            return BadRequest(new { message = $"За один запрос можно передать максимум {MaxVehiclePhotoBulkVehicleCount} единиц техники." });
        }

        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            var photos = await vehicleGalleryService.GetVehiclePhotosAsync(userId, organizationId, distinctVehicleIds);
            return Ok(await BuildVehiclePhotoGroupsResponsesAsync(userId, distinctVehicleIds, photos));
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Загружает пакет фото для автомобиля. </summary>
    [HttpPost("vehicles/{vehicleId:guid}/photos")]
    [RequestSizeLimit(MaxVehiclePhotoBatchSizeBytes)]
    public async Task<ActionResult<IEnumerable<Guid>>> UploadVehiclePhotos(Guid vehicleId, [FromForm] IFormFileCollection formFiles, [FromQuery] Guid? organizationId = null)
    {
        var validationError = ValidateImageCollection(formFiles, MaxVehiclePhotoCount, MaxVehiclePhotoBatchSizeBytes, "Фото техники: максимум 10 штук всего и до 100 МБ за одну загрузку.");
        if (validationError != null) return validationError;

        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        return await UploadPhotoBatchAsync(
            formFiles!,
            files => vehicleGalleryService.UploadVehiclePhotosAsync(userId, organizationId, vehicleId, files));
    }

    /// <summary> Удаляет основное фото автомобиля без указания идентификатора. </summary>
    [HttpDelete("vehicles/{vehicleId:guid}/photo")]
    [HttpDelete("vehicles/{vehicleId:guid}/delete-photo")]
    public async Task<IActionResult> DeleteVehiclePhoto(Guid vehicleId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await vehicleGalleryService.DeleteVehiclePhotoAsync(userId, organizationId, vehicleId);
            return NoContent();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Удаляет фото автомобиля по идентификатору. </summary>
    [HttpDelete("vehicles/{vehicleId:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> DeleteVehiclePhoto(Guid vehicleId, Guid photoId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await vehicleGalleryService.DeleteVehiclePhotoAsync(userId, organizationId, vehicleId, photoId);
            return NoContent();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Назначает фото автомобиля основным. </summary>
    [HttpPost("vehicles/{vehicleId:guid}/photos/{photoId:guid}/primary")]
    public async Task<IActionResult> SetPrimaryVehiclePhoto(Guid vehicleId, Guid photoId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await vehicleGalleryService.SetPrimaryVehiclePhotoAsync(userId, organizationId, vehicleId, photoId);
            return NoContent();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Возвращает фотоальбомы автомобиля с информацией об ограничениях. </summary>
    [HttpGet("vehicles/{vehicleId:guid}/albums")]
    public async Task<ActionResult<VehicleAlbumsResponse>> GetVehicleAlbums(Guid vehicleId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            var albums = await vehiclePhotoAlbumService.GetVehiclePhotoAlbumsAsync(userId, organizationId, vehicleId);
            var limit = await vehiclePhotoAlbumService.GetVehiclePhotoAlbumLimitAsync(userId, organizationId, vehicleId);
            return Ok(new VehicleAlbumsResponse
            {
                Limit = limit,
                Albums = await BuildVehicleAlbumResponsesAsync(userId, albums)
            });
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Возвращает фотоальбом автомобиля по идентификатору. </summary>
    [HttpGet("vehicles/{vehicleId:guid}/albums/{albumId:guid}")]
    public async Task<ActionResult<VehicleAlbumResponse>> GetVehicleAlbum(Guid vehicleId, Guid albumId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            var album = await vehiclePhotoAlbumService.GetVehiclePhotoAlbumAsync(userId, organizationId, vehicleId, albumId);
            return Ok((await BuildVehicleAlbumResponsesAsync(userId, [album])).First());
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Создаёт фотоальбом для автомобиля. </summary>
    [HttpPost("vehicles/{vehicleId:guid}/albums")]
    public async Task<ActionResult<VehicleAlbumResponse>> CreateVehicleAlbum(Guid vehicleId, [FromBody] CreateVehicleAlbumRequest request, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            var album = await vehiclePhotoAlbumService.CreateVehiclePhotoAlbumAsync(userId, organizationId, vehicleId, request.Title);
            return Ok((await BuildVehicleAlbumResponsesAsync(userId, [album])).First());
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Удаляет фотоальбом автомобиля. </summary>
    [HttpDelete("vehicles/{vehicleId:guid}/albums/{albumId:guid}")]
    public async Task<IActionResult> DeleteVehicleAlbum(Guid vehicleId, Guid albumId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await vehiclePhotoAlbumService.DeleteVehiclePhotoAlbumAsync(userId, organizationId, vehicleId, albumId);
            return NoContent();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Загружает фотографии в фотоальбом автомобиля. </summary>
    [HttpPost("vehicles/{vehicleId:guid}/albums/{albumId:guid}/photos")]
    [RequestSizeLimit(MaxVehiclePhotoBatchSizeBytes)]
    public async Task<ActionResult<IEnumerable<Guid>>> UploadVehicleAlbumPhotos(Guid vehicleId, Guid albumId, [FromForm] IFormFileCollection formFiles, [FromQuery] Guid? organizationId = null)
    {
        var validationError = ValidateImageCollection(formFiles, int.MaxValue, MaxVehiclePhotoBatchSizeBytes, "Фото альбома: до 100 МБ за одну загрузку.");
        if (validationError != null) return validationError;

        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        return await UploadPhotoBatchAsync(
            formFiles!,
            files => vehiclePhotoAlbumService.UploadVehicleAlbumPhotosAsync(userId, organizationId, vehicleId, albumId, files));
    }

    /// <summary> Удаляет фотографию из фотоальбома автомобиля. </summary>
    [HttpDelete("vehicles/{vehicleId:guid}/albums/{albumId:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> DeleteVehicleAlbumPhoto(Guid vehicleId, Guid albumId, Guid photoId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await vehiclePhotoAlbumService.DeleteVehicleAlbumPhotoAsync(userId, organizationId, vehicleId, albumId, photoId);
            return NoContent();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Назначает фотографию альбома основной. </summary>
    [HttpPost("vehicles/{vehicleId:guid}/albums/{albumId:guid}/photos/{photoId:guid}/primary")]
    public async Task<IActionResult> SetPrimaryVehicleAlbumPhoto(Guid vehicleId, Guid albumId, Guid photoId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await vehiclePhotoAlbumService.SetPrimaryVehicleAlbumPhotoAsync(userId, organizationId, vehicleId, albumId, photoId);
            return NoContent();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Возвращает список фотографий по обслуживанию. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}/photos")]
    public async Task<ActionResult<IEnumerable<MaintenancePhotoResponse>>> GetMaintenancePhotos(Guid maintenanceId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            var photos = await maintenancePhotoService.GetMaintenancePhotosAsync(userId, organizationId, maintenanceId);
            var photosWithUrls = new List<MaintenancePhotoResponse>();

            foreach (var photo in photos)
            {
                photosWithUrls.Add(new MaintenancePhotoResponse
                {
                    PhotoId = photo.PhotoId,
                    Stage = photo.Stage,
                    CreatedAt = photo.CreatedAt,
                    Url = await fileStorageService.GeneratePresignedUrlAsync(photo.Path)
                });
            }

            return Ok(photosWithUrls);
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Возвращает содержимое файла фотографии обслуживания. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}/photos/{photoId:guid}/content")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetMaintenancePhotoContent(
        Guid maintenanceId,
        Guid photoId,
        [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = this.GetCurrentUserId();
        if (userIdResult.Result != null)
        {
            return userIdResult.Result;
        }

        try
        {
            var photos = await maintenancePhotoService.GetMaintenancePhotosAsync(userIdResult.Value!, organizationId, maintenanceId);
            var photo = photos.FirstOrDefault(item => item.PhotoId == photoId);
            if (photo == null)
            {
                return this.NotFound();
            }

            // Хранилище содержит обработанные JPEG; поток освобождает FileStreamResult.
            var stream = await fileStorageService.GetFileAsync(photo.Path);

            return this.File(stream, "image/jpeg");
        }
        catch (Exception ex) when (MediaController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (MediaController.IsNotFoundError(ex) || ex is FileNotFoundException)
        {
            return this.NotFound();
        }
    }

    /// <summary> Загружает пакет фотографий для обслуживания. </summary>
    [HttpPost("maintenance/{maintenanceId:guid}/photos")]
    [RequestSizeLimit(MaxMaintenancePhotoBatchSizeBytes)]
    public async Task<IActionResult> UploadMaintenancePhotos(Guid maintenanceId, [FromForm] IFormFileCollection formFiles,
        [FromQuery] Guid? organizationId = null, [FromQuery] MaintenancePhotoStage stage = MaintenancePhotoStage.Unspecified)
    {
        if (!Enum.IsDefined(stage))
        {
            return this.BadRequest(new { message = "Неизвестный этап съёмки." });
        }

        var validationError = ValidateImageCollection(formFiles, MaxMaintenancePhotoCount, MaxMaintenancePhotoBatchSizeBytes, "Фото обслуживания: максимум 10 файлов и до 100 МБ за одну загрузку.");
        if (validationError != null) return validationError;

        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        var openedStreams = new List<Stream>();
        try
        {
            var files = new List<(Stream Stream, string FileName)>();
            foreach (var file in formFiles!)
            {
                var stream = file.OpenReadStream();
                openedStreams.Add(stream);
                files.Add((stream, file.FileName));
            }

            await maintenancePhotoService.UploadMaintenancePhotosAsync(userId, organizationId, maintenanceId, files, stage);
            return Ok();
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
        finally
        {
            foreach (var stream in openedStreams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    /// <summary> Устанавливает этап обслуживания для фотографии. </summary>
    [HttpPut("maintenance/{maintenanceId:guid}/photos/{photoId:guid}/stage")]
    public async Task<IActionResult> SetMaintenancePhotoStage(
        Guid maintenanceId, Guid photoId, [FromQuery] MaintenancePhotoStage stage, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = this.GetCurrentUserId();
        if (userIdResult.Result != null)
        {
            return userIdResult.Result;
        }

        try
        {
            await maintenancePhotoService.SetMaintenancePhotoStageAsync(userIdResult.Value!, organizationId, maintenanceId, photoId, stage);

            return this.NoContent();
        }
        catch (Exception ex) when (MediaController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (MediaController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return this.BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Удаляет фотографию обслуживания по идентификатору. </summary>
    [HttpDelete("maintenance/{maintenanceId:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> DeleteMaintenancePhoto(Guid maintenanceId, Guid photoId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await maintenancePhotoService.DeleteMaintenancePhotoAsync(userId, organizationId, maintenanceId, photoId);
            return NoContent();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Возвращает путь медиафайла по идентификатору. </summary>
    [HttpGet("{mediaId:guid}/path")]
    public async Task<ActionResult<string>> GetMediaPath(Guid mediaId)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        var hasAccess = await mediaAccessService.UserHasAccessToMediaAsync(userId, mediaId);
        if (!hasAccess) return Forbid();

        var mediaPath = await mediaAccessService.GetMediaPathAsync(userId, mediaId);
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return NotFound();
        }

        return Ok(await fileStorageService.GeneratePresignedUrlAsync(mediaPath));
    }

    /// <summary> Проверяет одиночное изображение на корректность и размер. </summary>
    private ActionResult? ValidateSingleImage(IFormFile? file, long maxSizeBytes)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "Выберите фото для загрузки." });
        if (!IsImage(file)) return BadRequest(new { message = "Можно загружать только изображения." });
        if (file.Length > maxSizeBytes) return BadRequest(new { message = $"Фото превышает лимит {FormatMegabytes(maxSizeBytes)} МБ." });
        return null;
    }

    /// <summary> Проверяет пакет изображений на количество и суммарный размер. </summary>
    private ActionResult? ValidateImageCollection(IFormFileCollection? files, int maxCount, long maxTotalSizeBytes, string limitText)
    {
        if (files == null || files.Count == 0) return BadRequest(new { message = "Выберите фото для загрузки." });
        if (files.Count > maxCount) return BadRequest(new { message = limitText });
        if (files.Any(file => !IsImage(file))) return BadRequest(new { message = "Можно загружать только изображения." });
        if (files.Sum(file => file.Length) > maxTotalSizeBytes) return BadRequest(new { message = $"Превышен лимит загрузки: {limitText}" });
        return null;
    }

    /// <summary> Определяет, является ли файл изображением. </summary>
    private static bool IsImage(IFormFile file)
    {
        return !string.IsNullOrWhiteSpace(file.ContentType)
            && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Формирует модели ответа с данными фото автомобилей. </summary>
    private async Task<List<VehiclePhotoResponse>> BuildVehiclePhotoResponsesAsync(string userId, IEnumerable<VehiclePhotoDto> photos)
    {
        var result = new List<VehiclePhotoResponse>();
        foreach (var photo in photos)
        {
            var mediaPath = await mediaAccessService.GetMediaPathAsync(userId, photo.PhotoId);
            if (string.IsNullOrWhiteSpace(mediaPath)) continue;
            result.Add(new VehiclePhotoResponse
            {
                PhotoId = photo.PhotoId,
                AlbumId = photo.AlbumId,
                Url = await fileStorageService.GeneratePresignedUrlAsync(mediaPath),
                IsPrimary = photo.IsPrimary,
                OriginalFileName = photo.OriginalFileName,
                Size = photo.Size,
                CreatedAt = photo.CreatedAt
            });
        }

        return result;
    }

    /// <summary> Формирует модели ответа с фото, сгруппированными по автомобилям. </summary>
    private async Task<List<VehiclePhotosResponse>> BuildVehiclePhotoGroupsResponsesAsync(string userId, IEnumerable<Guid> vehicleIds, IEnumerable<VehiclePhotoDto> photos)
    {
        var responsesByVehicle = new Dictionary<Guid, List<VehiclePhotoResponse>>();
        foreach (var photoGroup in photos.GroupBy(x => x.VehicleId))
        {
            responsesByVehicle[photoGroup.Key] = await BuildVehiclePhotoResponsesAsync(userId, photoGroup);
        }

        return vehicleIds
            .Select(vehicleId => new VehiclePhotosResponse
            {
                VehicleId = vehicleId,
                Photos = responsesByVehicle.GetValueOrDefault(vehicleId) ?? []
            })
            .ToList();
    }

    /// <summary> Формирует модели ответа с данными фотоальбомов. </summary>
    private async Task<List<VehicleAlbumResponse>> BuildVehicleAlbumResponsesAsync(string userId, IEnumerable<VehiclePhotoAlbumDto> albums)
    {
        var result = new List<VehicleAlbumResponse>();
        foreach (var album in albums)
        {
            result.Add(new VehicleAlbumResponse
            {
                AlbumId = album.AlbumId,
                VehicleId = album.VehicleId,
                Title = album.Title,
                CreatedAt = album.CreatedAt,
                PhotoCount = album.PhotoCount,
                PrimaryPhotoId = album.PrimaryPhotoId,
                Photos = await BuildVehiclePhotoResponsesAsync(userId, album.Photos)
            });
        }

        return result;
    }

    /// <summary> Выполняет пакетную загрузку изображений с общей валидацией. </summary>
    private async Task<ActionResult<IEnumerable<Guid>>> UploadPhotoBatchAsync(
        IFormFileCollection formFiles,
        Func<IEnumerable<(Stream Stream, string FileName)>, Task<IReadOnlyList<Guid>>> upload)
    {
        var openedStreams = new List<Stream>();
        try
        {
            var files = new List<(Stream Stream, string FileName)>();
            foreach (var file in formFiles)
            {
                var stream = file.OpenReadStream();
                openedStreams.Add(stream);
                files.Add((stream, file.FileName));
            }

            var ids = await upload(files);
            return Ok(ids);
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
        finally
        {
            foreach (var stream in openedStreams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    /// <summary> Переводит размер в байтах в мегабайты. </summary>
    private static long FormatMegabytes(long bytes) => bytes / 1024 / 1024;

    /// <summary> Модель ответа с данными фотографии обслуживания. </summary>
    public sealed class MaintenancePhotoResponse
    {
        /// <summary> Этап обслуживания, к которому относится фотография. </summary>
        public MaintenancePhotoStage Stage { get; set; }

        /// <summary> Дата и время создания фотографии. </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary> Идентификатор фотографии. </summary>
        public Guid PhotoId { get; set; }

        /// <summary> Ссылка на фотографию. </summary>
        public string Url { get; set; } = string.Empty;
    }

    /// <summary> Модель ответа с данными фотографии автомобиля. </summary>
    public sealed class VehiclePhotoResponse
    {
        /// <summary> Идентификатор фотографии. </summary>
        public Guid PhotoId { get; set; }

        /// <summary> Идентификатор альбома, если фотография в нём находится. </summary>
        public Guid? AlbumId { get; set; }

        /// <summary> Ссылка на фотографию. </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary> Признак основного фото автомобиля. </summary>
        public bool IsPrimary { get; set; }

        /// <summary> Исходное имя загруженного файла. </summary>
        public string? OriginalFileName { get; set; }

        /// <summary> Размер файла фотографии. </summary>
        public long Size { get; set; }

        /// <summary> Дата и время создания фотографии. </summary>
        public DateTime CreatedAt { get; set; }
    }

    /// <summary> Модель ответа со списком фото одного автомобиля. </summary>
    public sealed class VehiclePhotosResponse
    {
        /// <summary> Идентификатор автомобиля. </summary>
        public Guid VehicleId { get; set; }

        /// <summary> Список фотографий автомобиля. </summary>
        public List<VehiclePhotoResponse> Photos { get; set; } = [];
    }

    /// <summary> Модель ответа с данными фотоальбома автомобиля. </summary>
    public sealed class VehicleAlbumResponse
    {
        /// <summary> Идентификатор фотоальбома. </summary>
        public Guid AlbumId { get; set; }

        /// <summary> Идентификатор автомобиля. </summary>
        public Guid VehicleId { get; set; }

        /// <summary> Название фотоальбома. </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary> Дата и время создания альбома. </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary> Количество фотографий в альбоме. </summary>
        public int PhotoCount { get; set; }

        /// <summary> Идентификатор основной фотографии альбома, если задана. </summary>
        public Guid? PrimaryPhotoId { get; set; }

        /// <summary> Список фотографий альбома. </summary>
        public List<VehiclePhotoResponse> Photos { get; set; } = [];
    }

    /// <summary> Модель ответа со списком фотоальбомов автомобиля. </summary>
    public sealed class VehicleAlbumsResponse
    {
        /// <summary> Ограничения на количество альбомов и фотографий. </summary>
        public VehiclePhotoAlbumLimitDto Limit { get; set; } = new();

        /// <summary> Список фотоальбомов автомобиля. </summary>
        public List<VehicleAlbumResponse> Albums { get; set; } = [];
    }

    /// <summary> Запрос на создание фотоальбома автомобиля. </summary>
    public sealed class CreateVehicleAlbumRequest
    {
        /// <summary> Название создаваемого фотоальбома. </summary>
        public string Title { get; set; } = string.Empty;
    }
}



