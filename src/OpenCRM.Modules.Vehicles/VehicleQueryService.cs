using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles;

/// <summary> Сервис чтения данных техники. </summary>
public sealed class VehicleQueryService : IVehicleQueryService
{
    /// <summary> Префикс фильтра по марке из каталога. </summary>
    private const string CatalogBrandFilterPrefix = "catalog:";

    /// <summary> Префикс фильтра по марке из ручного ввода. </summary>
    private const string ManualBrandFilterPrefix = "manual:";

    /// <summary> Репозиторий техники. </summary>
    private readonly IVehicleRepository _vehicleRepository;

    /// <summary> Репозиторий фотографий техники. </summary>
    private readonly IVehiclePhotoRepository _vehiclePhotoRepository;

    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Сервис подписки организации для проверок. </summary>
    private readonly IOrganizationSubscriptionService _organizationSubscriptionService;

    /// <summary> Системные часы для расчётов по времени. </summary>
    private readonly ISystemClock _systemClock;

    /// <summary> Политика вариантов регистрационного номера для поиска. </summary>
    private readonly IVehicleRegistrationPlatePolicy? _registrationPlatePolicy;

    /// <summary> Создаёт сервис чтения техники с зависимостями. </summary>
    public VehicleQueryService(
        IVehicleRepository vehicleRepository,
        IVehiclePhotoRepository vehiclePhotoRepository,
        OpenCRMContext context,
        IOrganizationSubscriptionService organizationSubscriptionService,
        ISystemClock systemClock,
        IVehicleRegistrationPlatePolicy? registrationPlatePolicy = null)
    {
        this._vehicleRepository = vehicleRepository;
        this._vehiclePhotoRepository = vehiclePhotoRepository;
        this._context = context;
        this._organizationSubscriptionService = organizationSubscriptionService;
        this._systemClock = systemClock;
        this._registrationPlatePolicy = registrationPlatePolicy;
    }

    /// <summary> Возвращает технику пользователя по идентификатору. </summary>
    public async Task<GetVehicleDto> GetById(string userId, Guid vehicleId)
    {
        return await this.GetById(userId, null, vehicleId);
    }

    /// <summary> Возвращает технику с учётом организации по идентификатору. </summary>
    public async Task<GetVehicleDto> GetById(string userId, Guid? organizationId, Guid vehicleId)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId);

        var mapToDto = VehicleQueryService.CreateMapToDtoExpression(this._systemClock.UtcNow);
        var vehicle = await this._vehicleRepository.Get(userId, organizationId, mapToDto)
            .FirstOrDefaultAsync(x => x.Id == vehicleId)
            ?? throw new Exception($"Не найдена техника с id={vehicleId}");

        var vehiclePhoto = await this._vehiclePhotoRepository.GetByVehicleIdAsync(userId, organizationId, vehicleId);
        vehicle.PhotoId = vehiclePhoto?.Id;

        return vehicle;
    }

    /// <summary> Возвращает постраничный список техники пользователя с фильтрами. </summary>
    public async Task<GetVehiclesResponse> Get(string userId, int page, int pageSize, bool isArchived, string? brandFilter = null, int? kind = null, string? searchQuery = null, Guid? organizationClientId = null, bool canViewMaintenance = false)
    {
        return await this.Get(userId, null, page, pageSize, isArchived, brandFilter, kind, searchQuery, organizationClientId, canViewMaintenance);
    }

    /// <summary> Возвращает постраничный список техники с учётом организации и фильтрами. </summary>
    public async Task<GetVehiclesResponse> Get(string userId, Guid? organizationId, int page, int pageSize, bool isArchived, string? brandFilter = null, int? kind = null, string? searchQuery = null, Guid? organizationClientId = null, bool canViewMaintenance = false)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId);

        var mapToDto = VehicleQueryService.CreateMapToDtoExpression(this._systemClock.UtcNow);
        var userBrandRows = await this._vehicleRepository.Get(userId, organizationId, isArchived, mapToDto)
            .Select(x => new { x.BrandId, x.BrandName })
            .ToListAsync();

        var userBrands = userBrandRows
            .Select(x => new
            {
                Id = x.BrandId.HasValue
                    ? VehicleQueryService.CreateCatalogBrandFilter(x.BrandId.Value)
                    : VehicleQueryService.CreateManualBrandFilter(x.BrandName),
                Name = VehicleQueryService.NormalizeManualValue(x.BrandName)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Id!, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var name = x.First().Name!;
                return new VehicleBrandFilterOptionDto
                {
                    Id = x.First().Id!,
                    Name = $"{name} ({x.Count()})"
                };
            })
            .OrderBy(x => x.Name)
            .ToList();

        var userKinds = await this._vehicleRepository.Get(userId, organizationId, isArchived, mapToDto)
            .Select(x => (int)x.Kind)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var filteredQuery = this._vehicleRepository.Get(userId, organizationId, isArchived, mapToDto);

        var catalogBrandId = VehicleQueryService.TryGetCatalogBrandId(brandFilter);
        if (catalogBrandId.HasValue)
        {
            filteredQuery = filteredQuery.Where(x => x.BrandId == catalogBrandId.Value);
        }
        else
        {
            var manualBrandName = VehicleQueryService.TryGetManualBrandName(brandFilter);
            if (!string.IsNullOrWhiteSpace(manualBrandName))
            {
                filteredQuery = filteredQuery.Where(x => !x.BrandId.HasValue && x.BrandName == manualBrandName);
            }
        }

        if (kind.HasValue)
        {
            var vehicleKind = (VehicleKind)kind.Value;
            filteredQuery = filteredQuery.Where(x => x.Kind == vehicleKind);
        }

        if (organizationId.HasValue && organizationClientId.HasValue)
        {
            filteredQuery = filteredQuery.Where(x => x.OrganizationClientId == organizationClientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var searchTokens = searchQuery
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();

            foreach (var token in searchTokens)
            {
                var searchPattern = $"%{token}%";
                var registrationPlateSearchPatterns = this._registrationPlatePolicy?.GetSearchVariants(token)
                    .Select(variant => $"%{variant}%")
                    .ToArray() ?? [searchPattern];

                filteredQuery = filteredQuery.Where(x =>
                    EF.Functions.ILike(x.BrandName, searchPattern) ||
                    EF.Functions.ILike(x.ModelName, searchPattern) ||
                    (!string.IsNullOrEmpty(x.Name) && EF.Functions.ILike(x.Name, searchPattern)) ||
                    (!string.IsNullOrEmpty(x.RegistrationPlate) && registrationPlateSearchPatterns.Any(platePattern =>
                        EF.Functions.ILike(x.RegistrationPlate.Replace(" ", string.Empty).Replace("-", string.Empty), platePattern))) ||
                    (!string.IsNullOrEmpty(x.Vin) && EF.Functions.ILike(x.Vin, searchPattern)) ||
                    (!string.IsNullOrEmpty(x.BodyNumber) && EF.Functions.ILike(x.BodyNumber, searchPattern)) ||
                    (!string.IsNullOrEmpty(x.EngineNumber) && EF.Functions.ILike(x.EngineNumber, searchPattern)) ||
                    (!string.IsNullOrEmpty(x.OrganizationClientName) && EF.Functions.ILike(x.OrganizationClientName, searchPattern)) ||
                    (x.YearIssue.HasValue && EF.Functions.ILike(x.YearIssue.Value.ToString(), searchPattern)));
            }
        }

        var allCount = await filteredQuery.CountAsync();
        var totalCount = await filteredQuery.CountAsync();
        var vehicles = await filteredQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var vehicleIds = vehicles.Select(x => x.Id).ToList();
        var currentMaintenanceByVehicle = new Dictionary<Guid,
            (Guid? MaintenanceId, MaintenanceStatus? Status, DateTime? Date)>();
        var workDetailsByMaintenance = new Dictionary<Guid, (string Name, int Count)>();
        var completedWorkCounts = new Dictionary<Guid, int>();
        var lastMaintenanceDates = new Dictionary<Guid, DateTime?>();

        if (canViewMaintenance)
        {
            var currentMaintenance = await this._context.Maintenance
                .AsNoTracking()
                .Where(x => vehicleIds.Contains(x.VehicleId)
                    && !x.IsDeleted
                    && (x.Status == MaintenanceStatus.Planned
                        || x.Status == MaintenanceStatus.InProgress
                        || (x.Status == MaintenanceStatus.Completed && x.ReadyForHandoverAt != null && x.HandedOverAt == null)))
                .GroupBy(x => x.VehicleId)
                .Select(group => new
                {
                    VehicleId = group.Key,
                    MaintenanceId = group
                        .OrderByDescending(x => x.Status == MaintenanceStatus.InProgress)
                        .ThenByDescending(x => x.Status == MaintenanceStatus.Completed
                            && x.ReadyForHandoverAt != null && x.HandedOverAt == null)
                        .ThenBy(x => x.PlannedDate ?? x.Date)
                        .Select(x => (Guid?)x.Id)
                        .FirstOrDefault(),
                    Status = group
                        .OrderByDescending(x => x.Status == MaintenanceStatus.InProgress)
                        .ThenByDescending(x => x.Status == MaintenanceStatus.Completed
                            && x.ReadyForHandoverAt != null && x.HandedOverAt == null)
                        .ThenBy(x => x.PlannedDate ?? x.Date)
                        .Select(x => (MaintenanceStatus?)x.Status)
                        .FirstOrDefault(),
                    Date = group
                        .OrderByDescending(x => x.Status == MaintenanceStatus.InProgress)
                        .ThenByDescending(x => x.Status == MaintenanceStatus.Completed
                            && x.ReadyForHandoverAt != null && x.HandedOverAt == null)
                        .ThenBy(x => x.PlannedDate ?? x.Date)
                        .Select(x => (DateTime?)(x.Status == MaintenanceStatus.Completed
                            ? x.ReadyForHandoverAt ?? x.CompletedDate ?? x.Date
                            : x.PlannedDate ?? x.Date))
                        .FirstOrDefault()
                })
                .ToListAsync();
            var currentMaintenanceIds = currentMaintenance
                .Where(x => x.MaintenanceId.HasValue)
                .Select(x => x.MaintenanceId!.Value)
                .ToArray();
            var currentWorkDetails = currentMaintenanceIds.Length == 0
                ? []
                : await this._context.Maintenance
                    .AsNoTracking()
                    .Where(x => currentMaintenanceIds.Contains(x.Id))
                    .SelectMany(x => x.Details
                        .Where(detail => detail.MaterialCatalogItemId == null
                            && detail.MaterialQuantity == null
                            && string.IsNullOrWhiteSpace(detail.MaterialName)
                            && !string.IsNullOrWhiteSpace(detail.Name))
                        .Select(detail => new { x.Id, detail.DetailId, detail.Position, detail.Name }))
                    .OrderBy(x => x.Id)
                    .ThenBy(x => x.Position)
                    .ToListAsync();
            workDetailsByMaintenance = currentWorkDetails
                .GroupBy(x => x.Id)
                .ToDictionary(group => group.Key, group => (group.First().Name, group.Count()));
            var currentWorkDetailIds = currentWorkDetails.Select(detail => detail.DetailId).ToArray();
            completedWorkCounts = currentMaintenanceIds.Length == 0 || currentWorkDetailIds.Length == 0
                ? new Dictionary<Guid, int>()
                : await this._context.MaintenanceWorkOperations
                    .AsNoTracking()
                    .Where(operation => currentMaintenanceIds.Contains(operation.MaintenanceId)
                        && currentWorkDetailIds.Contains(operation.Id))
                    .GroupBy(operation => operation.MaintenanceId)
                    .Select(group => new
                    {
                        MaintenanceId = group.Key,
                        Count = group.Count(operation => operation.Status == MaintenanceWorkStatus.Completed)
                    })
                    .ToDictionaryAsync(x => x.MaintenanceId, x => x.Count);
            lastMaintenanceDates = await this._context.Maintenance
                .AsNoTracking()
                .Where(x => vehicleIds.Contains(x.VehicleId) && !x.IsDeleted && x.Status == MaintenanceStatus.Completed)
                .GroupBy(x => x.VehicleId)
                .Select(group => new
                {
                    VehicleId = group.Key,
                    Date = group.Max(x => (DateTime?)(x.CompletedDate ?? x.Date))
                })
                .ToDictionaryAsync(x => x.VehicleId, x => x.Date);
            currentMaintenanceByVehicle = currentMaintenance.ToDictionary(
                x => x.VehicleId,
                x => (x.MaintenanceId, x.Status, x.Date));
        }

        var vehiclePhotos = await this._vehiclePhotoRepository.GetByVehicleIdsAsync(userId, organizationId, vehicleIds);

        foreach (var vehicle in vehicles)
        {
            vehicle.PhotoId = vehiclePhotos.FirstOrDefault(x => x.VehicleId == vehicle.Id)?.Id;
            if (canViewMaintenance)
            {
                vehicle.LastMaintenanceDate = lastMaintenanceDates.GetValueOrDefault(vehicle.Id);
                if (currentMaintenanceByVehicle.TryGetValue(vehicle.Id, out var maintenance))
                {
                    vehicle.CurrentMaintenanceStatus = maintenance.Status;
                    vehicle.CurrentMaintenanceDate = maintenance.Date;
                    vehicle.CurrentMaintenanceId = maintenance.MaintenanceId;
                    if (maintenance.MaintenanceId is Guid maintenanceId)
                    {
                        if (workDetailsByMaintenance.TryGetValue(maintenanceId, out var workDetails))
                        {
                            vehicle.CurrentMaintenanceName = workDetails.Name;
                            vehicle.TotalWorkCount = workDetails.Count;
                        }

                        vehicle.CompletedWorkCount = completedWorkCounts.GetValueOrDefault(maintenanceId);
                    }
                }
            }
        }

        return new GetVehiclesResponse
        {
            CanViewMaintenance = canViewMaintenance,
            Items = vehicles,
            AllCount = allCount,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            UserBrands = userBrands,
            UserKinds = userKinds
        };
    }

    /// <summary> Возвращает историю событий техники пользователя. </summary>
    public async Task<IReadOnlyList<VehicleHistoryEventDto>> GetHistory(string userId, Guid vehicleId, int take = 100)
    {
        return await this.GetHistory(userId, null, vehicleId, take);
    }

    /// <summary> Возвращает историю событий техники с учётом организации. </summary>
    public async Task<IReadOnlyList<VehicleHistoryEventDto>> GetHistory(string userId, Guid? organizationId, Guid vehicleId, int take = 100)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId);

        _ = await this._vehicleRepository.Get(userId, organizationId, vehicleId)
            ?? throw new Exception($"Не найдена техника с vehicleId={vehicleId} и userId={userId}");

        take = Math.Clamp(take, 20, 300);

        var vehicleEvents = await this._context.VehicleHistoryEvents
            .AsNoTracking()
            .Where(x => x.VehicleId == vehicleId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new VehicleHistoryEventDto
            {
                Id = x.Id,
                EventType = x.EventType,
                Title = x.Title,
                Description = x.Description,
                ActorDisplayName = x.ActorDisplayName,
                RelatedEntityType = x.RelatedEntityType,
                RelatedEntityId = x.RelatedEntityId,
                RelatedUrl = VehicleQueryService.BuildRelatedUrl(x.RelatedEntityType, x.RelatedEntityId, organizationId),
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        var maintenanceEvents = await this._context.MaintenanceHistoryEvents
            .AsNoTracking()
            .Where(x => x.VehicleId == vehicleId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new VehicleHistoryEventDto
            {
                Id = x.Id,
                EventType = "maintenance_" + x.EventType,
                Title = x.Title,
                Description = x.Description,
                ActorDisplayName = x.ActorDisplayName,
                RelatedEntityType = "maintenance",
                RelatedEntityId = x.MaintenanceId,
                RelatedUrl = VehicleQueryService.BuildRelatedUrl("maintenance", x.MaintenanceId, organizationId),
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        return vehicleEvents
            .Concat(maintenanceEvents)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToList();
    }

    /// <summary> Возвращает количество техники пользователя. </summary>
    public Task<int> GetVehiclesCountAsync(string userId) => this.GetVehiclesCountAsync(userId, null);

    /// <summary> Возвращает количество техники с учётом организации. </summary>
    public Task<int> GetVehiclesCountAsync(string userId, Guid? organizationId) => this._vehicleRepository.CountVehicles(userId, organizationId, false);

    /// <summary> Возвращает количество техники пользователя в архиве. </summary>
    public Task<int> GetArchivedVehiclesCountAsync(string userId) => this.GetArchivedVehiclesCountAsync(userId, null);

    /// <summary> Возвращает количество техники в архиве с учётом организации. </summary>
    public Task<int> GetArchivedVehiclesCountAsync(string userId, Guid? organizationId) => this._vehicleRepository.CountVehicles(userId, organizationId, true);

    /// <summary> Строит ссылку на связанную сущность события истории. </summary>
    private static string? BuildRelatedUrl(string? relatedEntityType, Guid? relatedEntityId, Guid? organizationId)
    {
        if (!relatedEntityId.HasValue)
        {
            return null;
        }

        var url = relatedEntityType switch
        {
            "maintenance" => $"/maintenance/{relatedEntityId.Value}",
            _ => null
        };

        if (url == null || !organizationId.HasValue)
        {
            return url;
        }

        return $"{url}?organizationId={organizationId.Value:D}";
    }

    /// <summary> Нормализует значение ручного ввода. </summary>
    private static string? NormalizeManualValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : VehicleQueryService.CollapseWhitespace(value.Trim());

    /// <summary> Схлопывает повторяющиеся пробелы в строке. </summary>
    private static string CollapseWhitespace(string value) => Regex.Replace(value, @"\s+", " ");

    /// <summary> Формирует фильтр по марке из каталога. </summary>
    private static string CreateCatalogBrandFilter(int brandId) => $"{VehicleQueryService.CatalogBrandFilterPrefix}{brandId}";

    /// <summary> Формирует фильтр по марке из ручного ввода. </summary>
    private static string? CreateManualBrandFilter(string? brandName)
    {
        var normalizedBrandName = VehicleQueryService.NormalizeManualValue(brandName);
        return string.IsNullOrWhiteSpace(normalizedBrandName)
            ? null
            : $"{VehicleQueryService.ManualBrandFilterPrefix}{normalizedBrandName}";
    }

    /// <summary> Извлекает идентификатор марки каталога из фильтра. </summary>
    private static int? TryGetCatalogBrandId(string? brandFilter)
    {
        if (string.IsNullOrWhiteSpace(brandFilter)
            || !brandFilter.StartsWith(VehicleQueryService.CatalogBrandFilterPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(brandFilter[VehicleQueryService.CatalogBrandFilterPrefix.Length..], out var brandId)
            ? brandId
            : null;
    }

    /// <summary> Извлекает название марки ручного ввода из фильтра. </summary>
    private static string? TryGetManualBrandName(string? brandFilter)
    {
        if (string.IsNullOrWhiteSpace(brandFilter)
            || !brandFilter.StartsWith(VehicleQueryService.ManualBrandFilterPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return VehicleQueryService.NormalizeManualValue(brandFilter[VehicleQueryService.ManualBrandFilterPrefix.Length..]);
    }

    /// <summary> Проверяет право пользователя читать данные рабочего пространства. </summary>
    private async Task EnsureCanReadWorkspaceAsync(
        string userId,
        Guid? organizationId,
        string permissionCode = OrganizationPermissionCodes.VehiclesView)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        await this._organizationSubscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId.Value, permissionCode);
    }

    /// <summary> Строит выражение преобразования техники в DTO. </summary>
    private static Expression<Func<Vehicle, GetVehicleDto>> CreateMapToDtoExpression(DateTime now) => vehicle => new GetVehicleDto
    {
        Id = vehicle.Id,
        Kind = vehicle.Kind,
        MileageUnit = vehicle.MileageUnit,
        BrandId = vehicle.BrandId,
        BrandLogoKey = vehicle.Brand != null ? vehicle.Brand.LogoKey : null,
        BrandName = vehicle.BrandId.HasValue
            ? (vehicle.Brand != null ? vehicle.Brand.Name : string.Empty)
            : (vehicle.ManualBrandName ?? string.Empty),
        ModelId = vehicle.ModelId,
        ModelName = vehicle.ModelId.HasValue
            ? (vehicle.Model != null ? vehicle.Model.Name : string.Empty)
            : (vehicle.ManualModelName ?? string.Empty),
        Description = vehicle.Description,
        CurrentMileage = vehicle.CurrentMileage,
        CurrentMileageUpdatedAt = vehicle.CurrentMileageUpdatedAt,
        Name = vehicle.Name,
        Vin = vehicle.Vin,
        RegistrationPlate = vehicle.RegistrationPlate,
        RegistrationCountryCode = vehicle.RegistrationCountryCode,
        BodyNumber = vehicle.BodyNumber,
        EngineNumber = vehicle.EngineNumber,
        Color = vehicle.Color,
        RegistrationCertificate = vehicle.RegistrationCertificate,
        CommissioningDate = vehicle.CommissioningDate,
        YearIssue = vehicle.YearIssue,
        PhotoId = null,
        MaintenanceCount = vehicle.Maintenance.Count(x => !x.IsDeleted),
        IsArchived = vehicle.IsArchived,
        ArchiveDate = vehicle.ArchiveDate,
        DaysLeft = VehicleArchivePolicy.GetDaysLeft(vehicle.ArchiveDate, now),
        DisplayName = Helpers.VehicleDisplayName.Format(
            vehicle.BrandId.HasValue ? vehicle.Brand!.Name : vehicle.ManualBrandName,
            vehicle.ModelId.HasValue ? vehicle.Model!.Name : vehicle.ManualModelName,
            vehicle.RegistrationPlate),
        ManualBrandName = vehicle.ManualBrandName,
        ManualModelName = vehicle.ManualModelName,
        IsManualEntry = !vehicle.ModelId.HasValue,
        DateTimeCreation = vehicle.DateTimeCreation,
        OrganizationId = vehicle.OrganizationId,
        OrganizationClientId = vehicle.OrganizationClientId,
        OrganizationClientName = vehicle.OrganizationClient != null ? vehicle.OrganizationClient.Name : null
    };
}
