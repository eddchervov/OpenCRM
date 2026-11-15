using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Counters;

/// <summary> Читает показания счетчиков техники. </summary>
public class VehicleCounterQueryService : IVehicleCounterQueryService
{
    /// <summary> Репозиторий техники для чтения счётчиков. </summary>
    private readonly IVehicleRepository _vehicleRepository;

    /// <summary> Контекст базы данных для запросов счётчиков. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Сервис подписки организации для проверок доступа. </summary>
    private readonly IOrganizationSubscriptionService _organizationSubscriptionService;

    /// <summary> Создаёт сервис чтения счётчиков техники с зависимостями. </summary>
    public VehicleCounterQueryService(
        IVehicleRepository vehicleRepository,
        OpenCRMContext context,
        IOrganizationSubscriptionService organizationSubscriptionService)
    {
        this._vehicleRepository = vehicleRepository;
        this._context = context;
        this._organizationSubscriptionService = organizationSubscriptionService;
    }

    /// <summary> Возвращает технику с показаниями счётчиков. </summary>
    public async Task<IReadOnlyList<VehicleCounterVehicleDto>> GetCounterVehiclesAsync(string userId, Guid? organizationId)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.CountersView);

        return await this._vehicleRepository.Get(userId, organizationId, false, vehicle => new VehicleCounterVehicleDto
        {
            VehicleId = vehicle.Id,
            OrganizationClientId = vehicle.OrganizationClientId,
            OrganizationClientName = vehicle.OrganizationClient != null ? (vehicle.OrganizationClient.Name ?? string.Empty) : null,
            VehicleDisplayName = Helpers.VehicleDisplayName.Format(
                vehicle.BrandId.HasValue ? vehicle.Brand!.Name : vehicle.ManualBrandName,
                vehicle.ModelId.HasValue ? vehicle.Model!.Name : vehicle.ManualModelName,
                vehicle.RegistrationPlate),
            VehicleSubtitle = (
                (((vehicle.Name ?? string.Empty) + " • ")
                + (vehicle.YearIssue.HasValue ? (vehicle.YearIssue.Value.ToString() + " г.") : string.Empty)
                + ((vehicle.YearIssue.HasValue && vehicle.OrganizationClient != null) ? " • " : string.Empty)
                + (vehicle.OrganizationClient != null ? (vehicle.OrganizationClient.Name ?? string.Empty) : string.Empty)
                + (vehicle.RegistrationPlate != null && vehicle.RegistrationPlate != string.Empty
                    ? (((vehicle.Name != null || vehicle.YearIssue.HasValue || vehicle.OrganizationClient != null) ? " • " : string.Empty) + vehicle.RegistrationPlate)
                    : string.Empty))
            ).Trim(' ', '•'),
            MileageUnit = vehicle.MileageUnit,
            CurrentValue = vehicle.CurrentMileage,
            UpdatedAt = vehicle.CurrentMileageUpdatedAt
        }).ToListAsync();
    }

    /// <summary> Возвращает историю показаний счётчиков с фильтрами. </summary>
    public async Task<IReadOnlyList<VehicleCounterEntryDto>> GetCounterHistoryAsync(string userId, Guid? organizationId, Guid? vehicleId = null, int take = 100)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.CountersView);

        var query = this._context.VehicleCounterEntries
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                && (organizationId.HasValue
                    ? x.Vehicle.OrganizationId == organizationId.Value
                    : x.Vehicle.UserId == userId && x.Vehicle.OrganizationId == null)
                && !x.Vehicle.IsDeleted);

        if (vehicleId.HasValue)
        {
            query = query.Where(x => x.VehicleId == vehicleId.Value);
        }

        return await query
            .OrderByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .Select(x => new VehicleCounterEntryDto
            {
                Id = x.Id,
                VehicleId = x.VehicleId,
                OrganizationClientId = x.Vehicle.OrganizationClientId,
                OrganizationClientName = x.Vehicle.OrganizationClient != null ? (x.Vehicle.OrganizationClient.Name ?? string.Empty) : null,
                VehicleDisplayName = Helpers.VehicleDisplayName.Format(
                    x.Vehicle.BrandId.HasValue ? x.Vehicle.Brand!.Name : x.Vehicle.ManualBrandName,
                    x.Vehicle.ModelId.HasValue ? x.Vehicle.Model!.Name : x.Vehicle.ManualModelName,
                    x.Vehicle.RegistrationPlate),
                VehicleSubtitle = (
                    (((x.Vehicle.Name ?? string.Empty) + " • ")
                    + (x.Vehicle.YearIssue.HasValue ? (x.Vehicle.YearIssue.Value.ToString() + " г.") : string.Empty)
                    + ((x.Vehicle.YearIssue.HasValue && x.Vehicle.OrganizationClient != null) ? " • " : string.Empty)
                    + (x.Vehicle.OrganizationClient != null ? (x.Vehicle.OrganizationClient.Name ?? string.Empty) : string.Empty))
                ).Trim(' ', '•'),
                MileageUnit = x.Vehicle.MileageUnit,
                Value = x.Value,
                RecordedAt = x.RecordedAt,
                Comment = x.Comment,
                SourceType = x.SourceType,
                SourceName = VehicleCounterSourceTypes.GetName(x.SourceType),
                TimelineType = "counter",
                CanManage = x.SourceType == VehicleCounterSourceTypes.Manual
            })
            .ToListAsync();
    }

    /// <summary> Возвращает хронологию показаний счётчика техники. </summary>
    public async Task<IReadOnlyList<VehicleCounterEntryDto>> GetCounterTimelineAsync(string userId, Guid? organizationId, Guid vehicleId, int take = 120)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.CountersView);

        var vehicle = await this._vehicleRepository.Get(userId, organizationId, vehicleId)
            ?? throw new InvalidOperationException("Техника не найдена.");

        var counterItems = await this._context.VehicleCounterEntries
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.VehicleId == vehicleId)
            .OrderByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .Select(x => new VehicleCounterEntryDto
            {
                Id = x.Id,
                VehicleId = x.VehicleId,
                OrganizationClientId = vehicle.OrganizationClientId,
                OrganizationClientName = vehicle.OrganizationClient != null ? (vehicle.OrganizationClient.Name ?? string.Empty) : null,
                VehicleDisplayName = Helpers.VehicleDisplayName.Format(
                    vehicle.BrandId.HasValue ? vehicle.Brand!.Name : vehicle.ManualBrandName,
                    vehicle.ModelId.HasValue ? vehicle.Model!.Name : vehicle.ManualModelName,
                    vehicle.RegistrationPlate),
                VehicleSubtitle = (
                    (((vehicle.Name ?? string.Empty) + " • ")
                    + (vehicle.YearIssue.HasValue ? (vehicle.YearIssue.Value.ToString() + " г.") : string.Empty)
                    + ((vehicle.YearIssue.HasValue && vehicle.OrganizationClient != null) ? " • " : string.Empty)
                    + (vehicle.OrganizationClient != null ? (vehicle.OrganizationClient.Name ?? string.Empty) : string.Empty))
                ).Trim(' ', '•'),
                MileageUnit = vehicle.MileageUnit,
                Value = x.Value,
                RecordedAt = x.RecordedAt,
                Comment = x.Comment,
                SourceType = x.SourceType,
                SourceName = VehicleCounterSourceTypes.GetName(x.SourceType),
                TimelineType = "counter",
                CanManage = x.SourceType == VehicleCounterSourceTypes.Manual
            })
            .ToListAsync();

        var maintenanceItems = await this._context.Maintenance
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.VehicleId == vehicleId && x.Mileage.HasValue)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .Select(x => new VehicleCounterEntryDto
            {
                Id = x.Id,
                VehicleId = x.VehicleId,
                OrganizationClientId = vehicle.OrganizationClientId,
                OrganizationClientName = vehicle.OrganizationClient != null ? (vehicle.OrganizationClient.Name ?? string.Empty) : null,
                VehicleDisplayName = Helpers.VehicleDisplayName.Format(
                    vehicle.BrandId.HasValue ? vehicle.Brand!.Name : vehicle.ManualBrandName,
                    vehicle.ModelId.HasValue ? vehicle.Model!.Name : vehicle.ManualModelName,
                    vehicle.RegistrationPlate),
                VehicleSubtitle = (
                    (((vehicle.Name ?? string.Empty) + " • ")
                    + (vehicle.YearIssue.HasValue ? (vehicle.YearIssue.Value.ToString() + " г.") : string.Empty)
                    + ((vehicle.YearIssue.HasValue && vehicle.OrganizationClient != null) ? " • " : string.Empty)
                    + (vehicle.OrganizationClient != null ? (vehicle.OrganizationClient.Name ?? string.Empty) : string.Empty))
                ).Trim(' ', '•'),
                MileageUnit = vehicle.MileageUnit,
                Value = x.Mileage!.Value,
                RecordedAt = x.Date,
                Comment = x.Comment,
                SourceType = VehicleCounterSourceTypes.Maintenance,
                SourceName = VehicleCounterSourceTypes.GetName(VehicleCounterSourceTypes.Maintenance),
                TimelineType = "maintenance",
                MaintenanceType = (int)x.Type,
                CanManage = false
            })
            .ToListAsync();

        return counterItems
            .Concat(maintenanceItems)
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.TimelineType == "counter" ? 0 : 1)
            .ThenBy(x => x.Id)
            .ToList();
    }

    /// <summary> Возвращает постраничный список техники со счётчиками. </summary>
    public async Task<VehicleCounterVehiclesResponse> GetCounterVehiclesPageAsync(
        string userId,
        Guid? organizationId,
        int page = 1,
        int pageSize = 12,
        string? searchQuery = null,
        Guid? vehicleId = null,
        Guid? organizationClientId = null,
        string? sort = null)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.CountersView);

        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 12 : pageSize;

        var normalizedSearch = VehicleCounterQueryService.NormalizeNullable(searchQuery);
        var normalizedSort = VehicleCounterQueryService.NormalizeCounterVehiclesSort(sort);

        var baseQuery = this._vehicleRepository.Get(userId, organizationId, false, vehicle => vehicle).AsNoTracking();

        var allCount = await baseQuery.CountAsync();
        var vehiclesWithCountersCount = await baseQuery.CountAsync(vehicle => vehicle.CurrentMileage.HasValue);

        var query = baseQuery;

        if (vehicleId.HasValue)
        {
            query = query.Where(vehicle => vehicle.Id == vehicleId.Value);
        }

        if (organizationClientId.HasValue)
        {
            query = query.Where(vehicle => vehicle.OrganizationClientId == organizationClientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var searchPattern = $"%{normalizedSearch}%";
            query = query.Where(vehicle =>
                EF.Functions.ILike(
                    ((vehicle.BrandId.HasValue ? vehicle.Brand!.Name : vehicle.ManualBrandName) ?? string.Empty) + " "
                    + ((vehicle.ModelId.HasValue ? vehicle.Model!.Name : vehicle.ManualModelName) ?? string.Empty), searchPattern)
                || EF.Functions.ILike(vehicle.RegistrationPlate ?? string.Empty, searchPattern)
                || EF.Functions.ILike(vehicle.Name ?? string.Empty, searchPattern)
                || (vehicle.OrganizationClient != null
                    && EF.Functions.ILike(vehicle.OrganizationClient.Name ?? string.Empty, searchPattern)));
        }

        var totalCount = await query.CountAsync();
        var items = await VehicleCounterQueryService.ApplyCounterVehiclesSort(query, normalizedSort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(vehicle => new VehicleCounterVehicleDto
            {
                VehicleId = vehicle.Id,
                OrganizationClientId = vehicle.OrganizationClientId,
                OrganizationClientName = vehicle.OrganizationClient != null ? (vehicle.OrganizationClient.Name ?? string.Empty) : null,
                VehicleDisplayName = Helpers.VehicleDisplayName.Format(
                    vehicle.BrandId.HasValue ? vehicle.Brand!.Name : vehicle.ManualBrandName,
                    vehicle.ModelId.HasValue ? vehicle.Model!.Name : vehicle.ManualModelName,
                    vehicle.RegistrationPlate),
                VehicleSubtitle = (
                    (((vehicle.Name ?? string.Empty) + " • ")
                    + (vehicle.YearIssue.HasValue ? (vehicle.YearIssue.Value.ToString() + " г.") : string.Empty)
                    + ((vehicle.YearIssue.HasValue && vehicle.OrganizationClient != null) ? " • " : string.Empty)
                    + (vehicle.OrganizationClient != null ? (vehicle.OrganizationClient.Name ?? string.Empty) : string.Empty)
                    + (vehicle.RegistrationPlate != null && vehicle.RegistrationPlate != string.Empty
                        ? (((vehicle.Name != null || vehicle.YearIssue.HasValue || vehicle.OrganizationClient != null) ? " • " : string.Empty) + vehicle.RegistrationPlate)
                        : string.Empty))
                ).Trim(' ', '•'),
                MileageUnit = vehicle.MileageUnit,
                CurrentValue = vehicle.CurrentMileage,
                UpdatedAt = vehicle.CurrentMileageUpdatedAt
            })
            .ToListAsync();

        return new VehicleCounterVehiclesResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            AllCount = allCount,
            VehiclesWithCountersCount = vehiclesWithCountersCount,
            HasMore = page * pageSize < totalCount
        };
    }

    /// <summary> Возвращает постраничную историю показаний счётчиков. </summary>
    public async Task<VehicleCounterHistoryResponse> GetCounterHistoryPageAsync(
        string userId,
        Guid? organizationId,
        int page = 1,
        int pageSize = 12,
        string? searchQuery = null,
        Guid? vehicleId = null,
        Guid? organizationClientId = null,
        string? sort = null)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.CountersView);

        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 12 : pageSize;

        var normalizedSearch = VehicleCounterQueryService.NormalizeNullable(searchQuery);
        var normalizedSort = VehicleCounterQueryService.NormalizeCounterHistorySort(sort);

        if (vehicleId.HasValue)
        {
            var timelineItems = await this.GetCounterTimelineAsync(userId, organizationId, vehicleId.Value, 500);

            if (organizationClientId.HasValue)
            {
                timelineItems = timelineItems
                    .Where(x => x.OrganizationClientId == organizationClientId.Value)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                timelineItems = timelineItems
                    .Where(x =>
                        VehicleCounterQueryService.ContainsCounterHistoryText(x.VehicleDisplayName, normalizedSearch)
                        || VehicleCounterQueryService.ContainsCounterHistoryText(x.VehicleSubtitle, normalizedSearch)
                        || VehicleCounterQueryService.ContainsCounterHistoryText(x.Comment, normalizedSearch)
                        || VehicleCounterQueryService.ContainsCounterHistoryText(x.OrganizationClientName, normalizedSearch))
                    .ToList();
            }

            var sortedTimelineItems = VehicleCounterQueryService.ApplyCounterHistorySort(timelineItems, normalizedSort).ToList();
            var pagedTimelineItems = sortedTimelineItems
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return new VehicleCounterHistoryResponse
            {
                Items = pagedTimelineItems,
                TotalCount = sortedTimelineItems.Count,
                Page = page,
                PageSize = pageSize,
                AllCount = sortedTimelineItems.Count,
                HasMore = page * pageSize < sortedTimelineItems.Count
            };
        }

        var baseQuery = this._context.VehicleCounterEntries
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                && (organizationId.HasValue
                    ? x.Vehicle.OrganizationId == organizationId.Value
                    : x.Vehicle.UserId == userId && x.Vehicle.OrganizationId == null)
                && !x.Vehicle.IsDeleted);

        var allCount = await baseQuery.CountAsync();

        if (organizationClientId.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Vehicle.OrganizationClientId == organizationClientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var searchPattern = $"%{normalizedSearch}%";
            baseQuery = baseQuery.Where(x =>
                ((x.Vehicle.BrandId.HasValue
                    ? (x.Vehicle.Brand != null ? x.Vehicle.Brand.Name : string.Empty)
                    : (x.Vehicle.ManualBrandName ?? string.Empty)) != string.Empty
                    && EF.Functions.ILike(
                        (x.Vehicle.BrandId.HasValue
                            ? (x.Vehicle.Brand != null ? x.Vehicle.Brand.Name : string.Empty)
                            : (x.Vehicle.ManualBrandName ?? string.Empty)),
                        searchPattern))
                || ((x.Vehicle.ModelId.HasValue
                    ? (x.Vehicle.Model != null ? x.Vehicle.Model.Name : string.Empty)
                    : (x.Vehicle.ManualModelName ?? string.Empty)) != string.Empty
                    && EF.Functions.ILike(
                        (x.Vehicle.ModelId.HasValue
                            ? (x.Vehicle.Model != null ? x.Vehicle.Model.Name : string.Empty)
                            : (x.Vehicle.ManualModelName ?? string.Empty)),
                        searchPattern))
                || (!string.IsNullOrEmpty(x.Vehicle.Name) && EF.Functions.ILike(x.Vehicle.Name, searchPattern))
                || (!string.IsNullOrEmpty(x.Comment) && EF.Functions.ILike(x.Comment, searchPattern))
                || (x.Vehicle.OrganizationClient != null && EF.Functions.ILike(x.Vehicle.OrganizationClient.Name, searchPattern)));
        }

        var totalCount = await baseQuery.CountAsync();

        var sortedQuery = VehicleCounterQueryService.ApplyCounterHistorySort(baseQuery, normalizedSort);
        var items = await sortedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new VehicleCounterEntryDto
            {
                Id = x.Id,
                VehicleId = x.VehicleId,
                OrganizationClientId = x.Vehicle.OrganizationClientId,
                OrganizationClientName = x.Vehicle.OrganizationClient != null ? (x.Vehicle.OrganizationClient.Name ?? string.Empty) : null,
                VehicleDisplayName = Helpers.VehicleDisplayName.Format(
                    x.Vehicle.BrandId.HasValue ? x.Vehicle.Brand!.Name : x.Vehicle.ManualBrandName,
                    x.Vehicle.ModelId.HasValue ? x.Vehicle.Model!.Name : x.Vehicle.ManualModelName,
                    x.Vehicle.RegistrationPlate),
                VehicleSubtitle = (
                    (((x.Vehicle.Name ?? string.Empty) + " • ")
                    + (x.Vehicle.YearIssue.HasValue ? (x.Vehicle.YearIssue.Value.ToString() + " г.") : string.Empty)
                    + ((x.Vehicle.YearIssue.HasValue && x.Vehicle.OrganizationClient != null) ? " • " : string.Empty)
                    + (x.Vehicle.OrganizationClient != null ? (x.Vehicle.OrganizationClient.Name ?? string.Empty) : string.Empty))
                ).Trim(' ', '•'),
                MileageUnit = x.Vehicle.MileageUnit,
                Value = x.Value,
                RecordedAt = x.RecordedAt,
                Comment = x.Comment,
                SourceType = x.SourceType,
                SourceName = VehicleCounterSourceTypes.GetName(x.SourceType),
                TimelineType = "counter",
                CanManage = x.SourceType == VehicleCounterSourceTypes.Manual
            })
            .ToListAsync();

        return new VehicleCounterHistoryResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            AllCount = allCount,
            HasMore = page * pageSize < totalCount
        };
    }

    /// <summary> Возвращает значение счётчика на указанную дату. </summary>
    public async Task<VehicleCounterLookupDto> GetCounterEntryForDateAsync(string userId, Guid? organizationId, Guid vehicleId, DateTime recordedAt)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.CountersView);

        var vehicle = await this._vehicleRepository.Get(userId, organizationId, item => new
        {
            item.Id,
            item.Name,
            item.YearIssue,
            item.RegistrationPlate,
            item.MileageUnit,
            item.OrganizationClientId,
            OrganizationClientName = item.OrganizationClient != null ? item.OrganizationClient.Name : null,
            BrandName = item.BrandId.HasValue ? item.Brand!.Name : item.ManualBrandName,
            ModelName = item.ModelId.HasValue ? item.Model!.Name : item.ManualModelName
        }).SingleOrDefaultAsync(item => item.Id == vehicleId)
            ?? throw new InvalidOperationException("Техника не найдена.");

        var startOfDay = recordedAt.Date;
        var endOfDay = startOfDay.AddDays(1);

        var entry = await this._context.VehicleCounterEntries
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                && x.VehicleId == vehicleId
                && x.RecordedAt >= startOfDay
                && x.RecordedAt < endOfDay)
            .OrderBy(x => x.SourceType == VehicleCounterSourceTypes.Manual ? 0 : 1)
            .ThenByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        return new VehicleCounterLookupDto
        {
            HasEntry = entry != null,
            EntryId = entry?.Id,
            VehicleId = vehicle.Id,
            OrganizationClientId = vehicle.OrganizationClientId,
            OrganizationClientName = vehicle.OrganizationClientName,
            VehicleDisplayName = Helpers.VehicleDisplayName.Format(vehicle.BrandName, vehicle.ModelName, vehicle.RegistrationPlate),
            VehicleSubtitle = VehicleCounterQueryService.BuildCounterVehicleSubtitle(
                vehicle.Name, vehicle.YearIssue, vehicle.OrganizationClientName, vehicle.RegistrationPlate),
            MileageUnit = vehicle.MileageUnit,
            Value = entry?.Value,
            RecordedAt = startOfDay,
            Comment = entry?.Comment,
            SourceType = entry?.SourceType ?? VehicleCounterSourceTypes.Manual,
            SourceName = entry != null ? VehicleCounterSourceTypes.GetName(entry.SourceType) : string.Empty
        };
    }

    /// <summary> Обрезает и очищает необязательную строку. </summary>
    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Regex.Replace(value.Trim(), @"\s+", " ");
    }

    /// <summary> Строит подзаголовок техники из названия, года и клиента. </summary>
    private static string? BuildCounterVehicleSubtitle(string? name, int? yearIssue, string? organizationClientName, string? registrationPlate = null)
    {
        var parts = new[]
        {
            string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            yearIssue.HasValue ? $"{yearIssue.Value} г." : null,
            string.IsNullOrWhiteSpace(organizationClientName) ? null : organizationClientName.Trim(),
            string.IsNullOrWhiteSpace(registrationPlate) ? null : registrationPlate.Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        return parts.Count > 0 ? string.Join(" • ", parts) : null;
    }

    /// <summary> Нормализует параметр сортировки техники со счётчиками. </summary>
    private static string NormalizeCounterVehiclesSort(string? sort)
    {
        return sort switch
        {
            "name_desc" => "name_desc",
            "updated_asc" => "updated_asc",
            "updated_desc" => "updated_desc",
            _ => "name_asc"
        };
    }

    /// <summary> Применяет сортировку к запросу техники. </summary>
    private static IQueryable<Vehicle> ApplyCounterVehiclesSort(IQueryable<Vehicle> query, string sort)
    {
        return sort switch
        {
            "name_desc" => query.OrderByDescending(vehicle => vehicle.BrandId.HasValue ? vehicle.Brand!.Name : vehicle.ManualBrandName)
                .ThenByDescending(vehicle => vehicle.ModelId.HasValue ? vehicle.Model!.Name : vehicle.ManualModelName)
                .ThenByDescending(vehicle => vehicle.RegistrationPlate)
                .ThenBy(vehicle => vehicle.Id),
            "updated_asc" => query.OrderBy(vehicle => vehicle.CurrentMileageUpdatedAt.HasValue ? 0 : 1)
                .ThenBy(vehicle => vehicle.CurrentMileageUpdatedAt)
                .ThenBy(vehicle => vehicle.Id),
            "updated_desc" => query.OrderBy(vehicle => vehicle.CurrentMileageUpdatedAt.HasValue ? 0 : 1)
                .ThenByDescending(vehicle => vehicle.CurrentMileageUpdatedAt)
                .ThenBy(vehicle => vehicle.Id),
            _ => query.OrderBy(vehicle => vehicle.BrandId.HasValue ? vehicle.Brand!.Name : vehicle.ManualBrandName)
                .ThenBy(vehicle => vehicle.ModelId.HasValue ? vehicle.Model!.Name : vehicle.ManualModelName)
                .ThenBy(vehicle => vehicle.RegistrationPlate)
                .ThenBy(vehicle => vehicle.Id)
        };
    }

    /// <summary> Нормализует параметр сортировки истории счётчиков. </summary>
    private static string NormalizeCounterHistorySort(string? sort)
    {
        return sort switch
        {
            "recorded_asc" => "recorded_asc",
            "value_desc" => "value_desc",
            "value_asc" => "value_asc",
            _ => "recorded_desc"
        };
    }

    /// <summary> Применяет сортировку к запросу записей счётчиков. </summary>
    private static IQueryable<VehicleCounterEntry> ApplyCounterHistorySort(IQueryable<VehicleCounterEntry> query, string sort)
    {
        return sort switch
        {
            "recorded_asc" => query.OrderBy(x => x.RecordedAt).ThenBy(x => x.Id),
            "value_desc" => query.OrderByDescending(x => x.Value).ThenByDescending(x => x.RecordedAt),
            "value_asc" => query.OrderBy(x => x.Value).ThenByDescending(x => x.RecordedAt),
            _ => query.OrderByDescending(x => x.RecordedAt).ThenByDescending(x => x.Id)
        };
    }

    /// <summary> Применяет сортировку к списку DTO записей счётчиков. </summary>
    private static IEnumerable<VehicleCounterEntryDto> ApplyCounterHistorySort(IEnumerable<VehicleCounterEntryDto> items, string sort)
    {
        return sort switch
        {
            "recorded_asc" => items.OrderBy(x => x.RecordedAt).ThenBy(x => x.TimelineType == "counter" ? 0 : 1).ThenBy(x => x.Id),
            "value_desc" => items.OrderByDescending(x => x.Value).ThenByDescending(x => x.RecordedAt).ThenBy(x => x.TimelineType == "counter" ? 0 : 1),
            "value_asc" => items.OrderBy(x => x.Value).ThenByDescending(x => x.RecordedAt).ThenBy(x => x.TimelineType == "counter" ? 0 : 1),
            _ => items.OrderByDescending(x => x.RecordedAt).ThenBy(x => x.TimelineType == "counter" ? 0 : 1).ThenByDescending(x => x.Id)
        };
    }

    /// <summary> Проверяет вхождение поисковой строки в текст записи. </summary>
    private static bool ContainsCounterHistoryText(string? source, string search)
    {
        return !string.IsNullOrWhiteSpace(source)
            && source.Contains(search, StringComparison.OrdinalIgnoreCase);
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
}
