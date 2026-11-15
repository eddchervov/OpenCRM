using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Media.Dto;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts.Photos;

namespace OpenCRM.Modules.Maintenance.Photos;

/// <summary>Сервис фотографий обслуживания и назначенных работ.</summary>
internal sealed class MaintenancePhotoService : IMaintenancePhotoService
{
    /// <summary> Код события загрузки фотографии. </summary>
    private const string EventPhotoUploaded = "photo_uploaded";

    /// <summary> Код события удаления фотографии. </summary>
    private const string EventPhotoDeleted = "photo_deleted";

    /// <summary> Репозиторий фотографий обслуживания. </summary>
    private readonly IMaintenancePhotoRepository _maintenancePhotoRepository;

    /// <summary> Сервис обработки изображений. </summary>
    private readonly IImageProcessingService _imageProcessingService;

    /// <summary> Репозиторий записей обслуживания. </summary>
    private readonly IMaintenanceRepository _maintenanceRepository;

    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Проверка подписки организации. </summary>
    private readonly IOrganizationSubscriptionService _organizationSubscriptionService;

    /// <summary> Запись событий в историю обслуживания. </summary>
    private readonly MaintenanceHistoryWriter _historyWriter;

    /// <summary> Доступ к медиа-рабочему пространству. </summary>
    private readonly IMediaWorkspaceAccess _workspaceAccess;

    /// <summary> Создаёт сервис фотографий с зависимостями. </summary>
    public MaintenancePhotoService(
        IMaintenancePhotoRepository maintenancePhotoRepository,
        IImageProcessingService imageProcessingService,
        IMaintenanceRepository maintenanceRepository,
        OpenCRMContext context,
        IOrganizationSubscriptionService organizationSubscriptionService,
        MaintenanceHistoryWriter historyWriter,
        IMediaWorkspaceAccess workspaceAccess)
    {
        this._maintenancePhotoRepository = maintenancePhotoRepository;
        this._imageProcessingService = imageProcessingService;
        this._maintenanceRepository = maintenanceRepository;
        this._context = context;
        this._organizationSubscriptionService = organizationSubscriptionService;
        this._historyWriter = historyWriter;
        this._workspaceAccess = workspaceAccess;
    }

    /// <summary> Возвращает фотографии обслуживания. </summary>
    public async Task<IReadOnlyList<MaintenancePhotoDto>> GetMaintenancePhotosAsync(string userId, Guid? organizationId, Guid maintenanceId)
    {
        await this._workspaceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);
        _ = await this._maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new ArgumentException("Maintenance not found.");
        var photos = await this._maintenancePhotoRepository.GetByMaintenanceIdAsync(userId, organizationId, maintenanceId);

        return photos.OrderBy(photo => photo.CreatedAt).ThenBy(photo => photo.Id)
            .Select(photo => new MaintenancePhotoDto(photo.Id, photo.Path, photo.Stage, photo.CreatedAt, photo.DetailId)).ToList();
    }

    /// <summary> Возвращает фотографии строки работы. </summary>
    public async Task<IReadOnlyList<MaintenancePhotoDto>> GetOperationPhotosAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId)
    {
        await this.EnsureOperationAccessAsync(userId, organizationId, maintenanceId, detailId, false);
        var photos = await this._context.MaintenancePhotos.AsNoTracking()
            .Where(photo => photo.MaintenanceId == maintenanceId && !photo.IsDeleted
                && (photo.DetailId == detailId
                    || photo.DetailId == null && photo.Stage == MaintenancePhotoStage.Before))
            .OrderBy(photo => photo.CreatedAt)
            .ToListAsync();

        return photos.Select(photo => new MaintenancePhotoDto(
            photo.Id, photo.Path, photo.Stage, photo.CreatedAt, photo.DetailId)).ToList();
    }

    /// <summary> Загружает фотографии для строки работы. </summary>
    public async Task UploadOperationPhotosAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId,
        IEnumerable<(Stream Stream, string FileName)> files, MaintenancePhotoStage stage)
    {
        await this.EnsurePhotoUploadAvailableAsync(userId, organizationId);
        var maintenance = await this.EnsureOperationAccessAsync(userId, organizationId, maintenanceId, detailId, true);
        if (!Enum.IsDefined(stage))
        {
            throw new InvalidOperationException("Неизвестный этап съёмки.");
        }

        var fileList = files.ToList();
        if (fileList.Count == 0)
        {
            throw new InvalidOperationException("Выберите фотографии для загрузки.");
        }

        var photosToAdd = new List<MaintenancePhoto>();
        foreach (var (stream, fileName) in fileList)
        {
            var imagePath = await this._imageProcessingService.ProcessAndSaveImageAsync(stream, new ImageProcessingOptions
            {
                TargetFolder = MaintenancePhotoService.BuildMaintenancePhotoTargetFolder(userId, organizationId),
                MaxWidth = 1200,
                MaxHeight = 800,
                Quality = 75
            });
            var contentType = await this._imageProcessingService.GetImageFormatAsync(stream);
            photosToAdd.Add(new MaintenancePhoto
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Path = imagePath,
                Stage = stage,
                Type = MediaType.MaintenancePhoto,
                CreatedAt = DateTime.UtcNow,
                MaintenanceId = maintenanceId,
                DetailId = detailId,
                VehicleId = maintenance.VehicleId,
                Size = stream.Length,
                ContentType = contentType,
                OriginalFileName = fileName
            });
        }

        var photoIds = photosToAdd.Select(photo => photo.Id).ToArray();
        var isRetry = false;
        await this._context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(async () =>
        {
            if (isRetry)
            {
                this._context.ChangeTracker.Clear();
            }

            isRetry = true;
            await this._context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenanceId} FOR UPDATE""");
            var memberId = await this._context.OrganizationMembers.AsNoTracking()
                .Where(member => member.OrganizationId == organizationId && member.UserId == userId)
                .Select(member => (Guid?)member.Id)
                .SingleOrDefaultAsync();
            var employeeId = await this._context.OrganizationEmployees.AsNoTracking()
                .Where(employee => employee.OrganizationId == organizationId && employee.MemberId == memberId)
                .Select(employee => (Guid?)employee.Id)
                .SingleOrDefaultAsync()
                ?? throw new UnauthorizedAccessException("Аккаунт не связан с сотрудником.");
            await this._context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "OrganizationEmployees" WHERE "Id" = {employeeId} FOR UPDATE""");
            await this.EnsureOperationAccessAsync(userId, organizationId, maintenanceId, detailId, true);

            await this._maintenancePhotoRepository.AddRangeAsync(photosToAdd);
            await this._historyWriter.AddHistoryEventAsync(
                userId, organizationId, maintenance.VehicleId, maintenanceId,
                EventPhotoUploaded, "Загружены фотографии работы",
                MaintenancePhotoService.TruncateHistoryDescription(MediaPhotoDescription.Build("Файлы", photosToAdd.Select(photo => photo.OriginalFileName))));
        }, async () => await this._context.MaintenancePhotos.AsNoTracking()
            .CountAsync(photo => photoIds.Contains(photo.Id)) == photoIds.Length);
    }

    /// <summary> Устанавливает этап фотографии обслуживания. </summary>
    public async Task SetMaintenancePhotoStageAsync(

        string userId,
        Guid? organizationId,
        Guid maintenanceId,
        Guid photoId,
        MaintenancePhotoStage stage)
    {
        await this._workspaceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);
        await this.EnsurePhotoUploadAvailableAsync(userId, organizationId);
        if (!Enum.IsDefined(stage))
        {
            throw new InvalidOperationException("Неизвестный этап съёмки.");
        }

        _ = await this._maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new ArgumentException("Maintenance not found.");
        var photo = await this._maintenancePhotoRepository.GetByIdAsync(userId, organizationId, photoId)
            ?? throw new ArgumentException("Maintenance photo not found.");
        if (photo.MaintenanceId != maintenanceId)
        {
            throw new ArgumentException("Maintenance photo not found in maintenance.");
        }

        var transaction = new MaintenanceTransaction(this._context);
        await transaction.ExecuteAsync(async () =>
        {
            await this._context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "MaintenancePhotos" WHERE "Id" = {photoId} FOR UPDATE""");
            await this._context.Entry(photo).ReloadAsync();
            if (photo.IsDeleted)
            {
                throw new ArgumentException("Maintenance photo not found.");
            }

            if (photo.Stage == stage)
            {
                return;
            }

            if (stage != MaintenancePhotoStage.Before && await this._context.MaintenanceIntakeDamagePhotos
                .AnyAsync(link => link.PhotoId == photoId && !link.Damage.IsDeleted))
            {
                throw new InvalidOperationException("Фотография связана с повреждением при приёмке. Сначала уберите её из повреждения.");
            }

            photo.Stage = stage;
            var stageName = stage switch
            {
                MaintenancePhotoStage.Before => "До",
                MaintenancePhotoStage.After => "После",
                _ => "Без категории"
            };
            await this._historyWriter.AddHistoryEventAsync(
                userId, organizationId, photo.VehicleId, maintenanceId,
                "photo_stage_changed", "Изменён этап фотографии",
                MaintenancePhotoService.TruncateHistoryDescription($"{photo.OriginalFileName}: {stageName}"));
        }, () => this._context.MaintenancePhotos.AsNoTracking().AnyAsync(value => value.Id == photoId && value.Stage == stage));
    }

    /// <summary> Возвращает идентификаторы фотографий обслуживания пользователя. </summary>
    public async Task<IEnumerable<Guid>> GetMaintenancePhotoIdsAsync(string userId, Guid maintenanceId)
    {
        return await this.GetMaintenancePhotoIdsAsync(userId, null, maintenanceId);
    }

    /// <summary> Возвращает идентификаторы фотографий с учётом организации. </summary>
    public async Task<IEnumerable<Guid>> GetMaintenancePhotoIdsAsync(string userId, Guid? organizationId, Guid maintenanceId)
    {
        await this._workspaceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);
        _ = await this._maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new ArgumentException($"Maintenance not found: userId={userId}, maintenanceId={maintenanceId}");

        var photos = await this._maintenancePhotoRepository.GetByMaintenanceIdAsync(userId, organizationId, maintenanceId);

        return photos.Select(photo => photo.Id);
    }

    /// <summary> Загружает фотографии обслуживания пользователя. </summary>
    public async Task UploadMaintenancePhotosAsync(
        string userId,
        Guid maintenanceId,
        IEnumerable<(Stream Stream, string FileName)> files,
        MaintenancePhotoStage stage = MaintenancePhotoStage.Unspecified)
    {
        await this.UploadMaintenancePhotosAsync(userId, null, maintenanceId, files, stage);
    }

    /// <summary> Загружает фотографии обслуживания с учётом организации. </summary>
    public async Task UploadMaintenancePhotosAsync(
        string userId,
        Guid? organizationId,
        Guid maintenanceId,
        IEnumerable<(Stream Stream, string FileName)> files,
        MaintenancePhotoStage stage = MaintenancePhotoStage.Unspecified)
    {
        await this._workspaceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);
        await this.EnsurePhotoUploadAvailableAsync(userId, organizationId);
        var maintenance = await this._maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new ArgumentException($"Maintenance not found: userId={userId}, maintenanceId={maintenanceId}");

        if (!Enum.IsDefined(stage))
        {
            throw new InvalidOperationException("Неизвестный этап съёмки.");
        }

        var photosToAdd = new List<MaintenancePhoto>();
        foreach (var (stream, fileName) in files)
        {
            var options = new ImageProcessingOptions
            {
                TargetFolder = MaintenancePhotoService.BuildMaintenancePhotoTargetFolder(userId, organizationId),
                MaxWidth = 1200,
                MaxHeight = 800,
                Quality = 75
            };

            var imagePath = await this._imageProcessingService.ProcessAndSaveImageAsync(stream, options);
            var contentType = await this._imageProcessingService.GetImageFormatAsync(stream);

            photosToAdd.Add(new MaintenancePhoto
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Path = imagePath,
                Stage = stage,
                Type = MediaType.MaintenancePhoto,
                CreatedAt = DateTime.UtcNow,
                MaintenanceId = maintenanceId,
                VehicleId = maintenance.VehicleId,
                Size = stream.Length,
                ContentType = contentType,
                OriginalFileName = fileName
            });
        }

        await this._maintenancePhotoRepository.AddRangeAsync(photosToAdd);
        if (photosToAdd.Count > 0)
        {
            await this._historyWriter.AddHistoryEventAsync(
                userId,
                organizationId,
                maintenance.VehicleId,
                maintenanceId,
                EventPhotoUploaded,
                photosToAdd.Count == 1 ? "Загружена фотография" : "Загружены фотографии",
                MaintenancePhotoService.TruncateHistoryDescription(MediaPhotoDescription.Build("Файлы", photosToAdd.Select(photo => photo.OriginalFileName))));
        }
    }

    /// <summary> Удаляет фото обслуживания от имени пользователя. </summary>
    public async Task DeleteMaintenancePhotoAsync(string userId, Guid maintenanceId, Guid photoId)
    {
        await this.DeleteMaintenancePhotoAsync(userId, null, maintenanceId, photoId);
    }

    /// <summary> Удаляет фото обслуживания с учётом организации. </summary>
    public async Task DeleteMaintenancePhotoAsync(string userId, Guid? organizationId, Guid maintenanceId, Guid photoId)
    {
        await this._workspaceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);
        await this.EnsurePhotoUploadAvailableAsync(userId, organizationId);
        var photo = await this._maintenancePhotoRepository.GetByIdAsync(userId, organizationId, photoId)
            ?? throw new ArgumentException($"Maintenance photo not found: userId={userId}, photoId={photoId}");

        if (photo.MaintenanceId != maintenanceId)
        {
            throw new ArgumentException($"Maintenance photo does not belong to maintenance: maintenanceId={maintenanceId}, photoId={photoId}");
        }

        await this._maintenancePhotoRepository.DeleteAsync(photoId);
        await this._historyWriter.AddHistoryEventAsync(
            userId,
            organizationId,
            photo.VehicleId,
            photo.MaintenanceId,
            EventPhotoDeleted,
            "Фотография удалена",
            MaintenancePhotoService.TruncateHistoryDescription(MediaPhotoDescription.Build("Файл", new[] { photo.OriginalFileName })));
    }

    /// <summary> Проверяет тариф перед созданием новых фотографий, сохраняя чтение истории. </summary>
    private async Task EnsurePhotoUploadAvailableAsync(string userId, Guid? organizationId)
    {
        if (organizationId.HasValue
            && !await this._organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, FeatureCodes.VehiclePhotoAlbums))
        {
            throw new InvalidOperationException("Загрузка новых фотографий доступна в PRO.");
        }
    }

    /// <summary> Проверяет доступ пользователя к работе с фото и возвращает обслуживание. </summary>
    private async Task<MaintenanceEntity> EnsureOperationAccessAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId, bool write)
    {
        if (write)
        {
            await this._organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
                userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkExecute);
        }
        else
        {
            await this._organizationSubscriptionService.EnsureWorkspaceCanReadAsync(
                userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkView);
        }

        var maintenance = await this._context.Maintenance.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == maintenanceId
                && item.Vehicle.OrganizationId == organizationId && !item.IsDeleted)
            ?? throw new KeyNotFoundException("Обслуживание не найдено.");
        var memberId = await this._context.OrganizationMembers.AsNoTracking()
            .Where(member => member.OrganizationId == organizationId && member.UserId == userId)
            .Select(member => (Guid?)member.Id)
            .SingleOrDefaultAsync();
        var employeeId = await this._context.OrganizationEmployees.AsNoTracking()
            .Where(employee => employee.OrganizationId == organizationId && employee.MemberId == memberId
                && employee.IsActive)
            .Select(employee => (Guid?)employee.Id)
            .SingleOrDefaultAsync();
        var detail = maintenance.Details.SingleOrDefault(item => item.DetailId == detailId
            && !item.MaterialCatalogItemId.HasValue && item.MaterialName == null
            && !item.MaterialQuantity.HasValue);
        if (detail == null || !employeeId.HasValue
            || (detail.EmployeeId ?? maintenance.EmployeeId) != employeeId.Value)
        {
            throw new UnauthorizedAccessException("Работа не назначена этому исполнителю.");
        }

        if (write && (maintenance.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled
            || maintenance.ReadyForHandoverAt.HasValue || maintenance.HandedOverAt.HasValue))
        {
            throw new InvalidOperationException("Работа недоступна для загрузки фотографии.");
        }

        return maintenance;
    }

    /// <summary> Усекает описание для истории изменений до допустимой длины. </summary>
    private static string TruncateHistoryDescription(string description)
    {
        return description.Length <= 1000 ? description : description[..1000];
    }

    /// <summary> Формирует папку хранения фото обслуживания для пользователя или организации. </summary>
    private static string BuildMaintenancePhotoTargetFolder(string userId, Guid? organizationId)
    {
        return organizationId.HasValue
            ? $"img/organizations/{organizationId.Value:D}/maintenance"
            : $"img/users/{userId}/maintenance";
    }
}
