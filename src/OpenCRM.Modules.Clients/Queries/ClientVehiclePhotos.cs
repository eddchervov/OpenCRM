using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.Modules.Clients.Queries;

/// <summary> Сервис загрузки фотографий техники клиентов. </summary>
internal sealed class ClientVehiclePhotos(OpenCRMContext context, IFileStorageService fileStorageService, ILogger<ClientVehiclePhotos> logger)
{
    /// <summary> Контекст базы данных для загрузки фотографий. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Зависимость сервиса файлового хранилища. </summary>
    private readonly IFileStorageService _fileStorageService = fileStorageService;

    /// <summary> Логгер сервиса фотографий техники. </summary>
    private readonly ILogger<ClientVehiclePhotos> _logger = logger;

    /// <summary> Прикрепляет фотографии к технике клиентов. </summary>
    internal async Task AttachAsync(Guid organizationId, Guid clientId, IReadOnlyList<OrganizationClientVehicleDto> vehicles)
    {
        var vehicleIds = vehicles.Select(x => x.Id).ToList();
        if (vehicleIds.Count == 0)
        {
            return;
        }

        var vehiclePhotoRows = await this._context.VehiclePhotos
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                && vehicleIds.Contains(x.VehicleId)
                && !x.Vehicle.IsDeleted
                && x.Vehicle.OrganizationId == organizationId
                && x.Vehicle.OrganizationClientId == clientId)
            .Select(x => new
            {
                x.VehicleId,
                x.Path,
                x.CreatedAt
            })
            .ToListAsync();

        var photoUrlsByVehicle = new Dictionary<Guid, string>();
        var latestPhotos = vehiclePhotoRows
            .GroupBy(x => x.VehicleId)
            .Select(x => x.OrderByDescending(p => p.CreatedAt).First())
            .Where(x => !string.IsNullOrWhiteSpace(x.Path));

        foreach (var photo in latestPhotos)
        {
            try
            {
                photoUrlsByVehicle[photo.VehicleId] = await this._fileStorageService.GeneratePresignedUrlAsync(photo.Path);
            }
            catch (Exception exception)
            {
                this._logger.LogWarning(exception, "Не удалось получить ссылку на фото техники {VehicleId} в карточке клиента", photo.VehicleId);
            }
        }

        foreach (var vehicle in vehicles)
        {
            if (photoUrlsByVehicle.TryGetValue(vehicle.Id, out var photoUrl))
            {
                vehicle.PhotoUrl = photoUrl;
            }
        }
    }
}
