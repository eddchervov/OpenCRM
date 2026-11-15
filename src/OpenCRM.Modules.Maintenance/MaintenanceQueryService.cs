using Microsoft.EntityFrameworkCore;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Читает историю и карточки обслуживания. </summary>
internal sealed class MaintenanceQueryService : IMaintenanceQueryService
{
    /// <summary> Репозиторий записей обслуживания. </summary>
    private readonly IMaintenanceRepository _maintenanceRepository;

    /// <summary> Репозиторий фотографий обслуживания. </summary>
    private readonly IMaintenancePhotoRepository _maintenancePhotoRepository;

    /// <summary> Поиск техники для записей обслуживания. </summary>
    private readonly IVehicleMaintenanceLookupService _vehicleLookup;

    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Проверка подписки организации. </summary>
    private readonly IOrganizationSubscriptionService _organizationSubscriptionService;

    /// <summary> Чтение обращения, из которого создано обслуживание организации. </summary>
    private readonly IOrganizationInquiryOriginReader _inquiryOriginReader;

    /// <summary> Создаёт сервис чтения с репозиториями и контекстом. </summary>
    public MaintenanceQueryService(
        IMaintenanceRepository maintenanceRepository,
        IMaintenancePhotoRepository maintenancePhotoRepository,
        IVehicleMaintenanceLookupService vehicleLookup,
        OpenCRMContext context,
        IOrganizationSubscriptionService organizationSubscriptionService,
        IOrganizationInquiryOriginReader inquiryOriginReader)
    {
        this._maintenanceRepository = maintenanceRepository;
        this._maintenancePhotoRepository = maintenancePhotoRepository;
        this._vehicleLookup = vehicleLookup;
        this._context = context;
        this._organizationSubscriptionService = organizationSubscriptionService;
        this._inquiryOriginReader = inquiryOriginReader;
    }

    /// <summary> Возвращает обслуживание по идентификатору. </summary>
    public async Task<GetMaintenanceByIdDto> GetById(string userId, Guid maintenanceId)
    {
        return await this.GetById(userId, null, maintenanceId);
    }

    /// <summary> Возвращает обслуживание с учётом организации. </summary>
    public async Task<GetMaintenanceByIdDto> GetById(string userId, Guid? organizationId, Guid maintenanceId)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId);

        var maintenance = await this._maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new Exception($"У пользователя userId={userId}, не найдено обслуживание с maintenanceId={maintenanceId}");

        var maintenancePhotos = await this._maintenancePhotoRepository.GetByMaintenanceIdAsync(userId, organizationId, maintenanceId);
        var linkedDefects = await this.LoadLinkedDefectsAsync([maintenance.Id]);

        return MaintenanceQueryService.MapToDto(maintenance, maintenancePhotos, linkedDefects);
    }

    /// <summary> Возвращает страницу списка обслуживаний техники. </summary>
    public async Task<MaintenanceListResponse> Get(string userId, Guid vehicleId, int page = 1, int pageSize = 10, string? sort = null, int? type = null, int? status = null)
    {
        return await this.Get(userId, null, vehicleId, page, pageSize, sort, type, status);
    }

    /// <summary> Возвращает страницу списка обслуживаний с учётом организации. </summary>
    public async Task<MaintenanceListResponse> Get(string userId, Guid? organizationId, Guid vehicleId, int page = 1, int pageSize = 10, string? sort = null, int? type = null, int? status = null)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId);
        _ = await this._vehicleLookup.FindAsync(userId, organizationId, vehicleId)
            ?? throw new Exception($"У пользователя userId={userId}, не найдена техника с vehicleId={vehicleId}");

        var (maintenance, totalCount) = await this._maintenanceRepository.GetByVehicleId(userId, organizationId, vehicleId, page, pageSize, sort, type, status);
        var maintenanceIds = maintenance.Select(x => x.Id);
        var maintenancePhotos = await this._maintenancePhotoRepository.GetByMaintenanceIdsAsync(userId, organizationId, maintenanceIds);
        var linkedDefects = await this.LoadLinkedDefectsAsync(maintenanceIds);

        return new MaintenanceListResponse
        {
            Items = maintenance.Select(x => MaintenanceQueryService.MapToMaintenanceDto(x, maintenancePhotos, linkedDefects)).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasMore = (page * pageSize) < totalCount
        };
    }

    /// <summary> Возвращает страницу удалённых обслуживаний техники. </summary>
    public async Task<MaintenanceListResponse> GetDeleted(string userId, Guid vehicleId, int page = 1, int pageSize = 10)
    {
        return await this.GetDeleted(userId, null, vehicleId, page, pageSize);
    }

    /// <summary> Возвращает страницу удалённых обслуживаний с учётом организации. </summary>
    public async Task<MaintenanceListResponse> GetDeleted(string userId, Guid? organizationId, Guid vehicleId, int page = 1, int pageSize = 10)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId);
        _ = await this._vehicleLookup.FindAsync(userId, organizationId, vehicleId)
            ?? throw new Exception($"У пользователя userId={userId}, не найдена техника с vehicleId={vehicleId}");

        var (maintenance, totalCount) = await this._maintenanceRepository.GetDeletedByVehicleId(userId, organizationId, vehicleId, page, pageSize);
        var maintenanceIds = maintenance.Select(x => x.Id);
        var maintenancePhotos = await this._maintenancePhotoRepository.GetByMaintenanceIdsAsync(userId, organizationId, maintenanceIds, includeDeleted: true);
        var linkedDefects = await this.LoadLinkedDefectsAsync(maintenanceIds);

        return new MaintenanceListResponse
        {
            Items = maintenance.Select(x => MaintenanceQueryService.MapToMaintenanceDto(x, maintenancePhotos, linkedDefects)).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasMore = (page * pageSize) < totalCount
        };
    }

    /// <summary> Возвращает список обслуживаний техники. </summary>
    public async Task<IEnumerable<GetMaintenanceDto>> Get(string userId, Guid vehicleId)
    {
        return await this.Get(userId, null, vehicleId);
    }

    /// <summary> Возвращает список обслуживаний с учётом организации. </summary>
    public async Task<IEnumerable<GetMaintenanceDto>> Get(string userId, Guid? organizationId, Guid vehicleId)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId);
        _ = await this._vehicleLookup.FindAsync(userId, organizationId, vehicleId)
            ?? throw new Exception($"У пользователя userId={userId}, не найдена техника с vehicleId={vehicleId}");

        var maintenance = await this._maintenanceRepository.GetByVehicleId(userId, organizationId, vehicleId);
        var maintenanceIds = maintenance.Select(x => x.Id);
        var maintenancePhotos = await this._maintenancePhotoRepository.GetByMaintenanceIdsAsync(userId, organizationId, maintenanceIds);
        var linkedDefects = await this.LoadLinkedDefectsAsync(maintenanceIds);

        return maintenance.Select(x => MaintenanceQueryService.MapToMaintenanceDto(x, maintenancePhotos, linkedDefects)).ToList();
    }

    /// <summary> Возвращает историю событий обслуживания. </summary>
    public async Task<IReadOnlyList<MaintenanceHistoryEventDto>> GetHistory(string userId, Guid maintenanceId)
    {
        return await this.GetHistory(userId, null, maintenanceId);
    }

    /// <summary> Возвращает историю событий с учётом организации. </summary>
    public async Task<IReadOnlyList<MaintenanceHistoryEventDto>> GetHistory(string userId, Guid? organizationId, Guid maintenanceId)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId);

        var maintenanceExists = await this._maintenanceRepository.ExistsAsync(userId, organizationId, maintenanceId);

        if (!maintenanceExists)
        {
            throw new Exception($"Не найдено обслуживание с maintenanceId={maintenanceId} и userId={userId}");
        }

        var timeZoneId = organizationId.HasValue
            ? await this._context.Organizations.AsNoTracking()
                .Where(item => item.Id == organizationId.Value)
                .Select(item => item.ShiftTimeZoneId)
                .FirstOrDefaultAsync()
            : null;
        var history = await this._context.MaintenanceHistoryEvents
            .AsNoTracking()
            .Where(x => x.MaintenanceId == maintenanceId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new MaintenanceHistoryEventDto
            {
                Id = x.Id,
                EventType = x.EventType,
                Title = x.Title,
                Description = x.Description,
                ActorDisplayName = x.ActorDisplayName,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        foreach (var historyEvent in history)
        {
            historyEvent.TimeZoneId = timeZoneId;
        }

        if (organizationId.HasValue && history.Any(item => item.EventType == "created"))
        {
            var source = await this._inquiryOriginReader.FindSourceAsync(userId, organizationId.Value, maintenanceId);
            if (source is not null)
            {
                var createdEvent = history.First(item => item.EventType == "created");
                createdEvent.Title = "Запись создана из обращения";
                createdEvent.SourceInquiryId = source.InquiryId;
            }
        }

        return history;
    }

    /// <summary> Преобразует запись обслуживания в DTO списка. </summary>
    private static GetMaintenanceDto MapToMaintenanceDto(
        MaintenanceEntity maintenance,
        IEnumerable<MaintenancePhoto> maintenancePhotos,
        IReadOnlyDictionary<Guid, List<MaintenanceLinkedDefectDto>> linkedDefects)
    {
        var localMaintenancePhotos = maintenancePhotos.Where(x => x.MaintenanceId == maintenance.Id);
        linkedDefects.TryGetValue(maintenance.Id, out var localLinkedDefects);
        var finance = MaintenanceReadModelRules.BuildFinanceSummary(maintenance);
        return new GetMaintenanceDto
        {
            Id = maintenance.Id,
            Comment = maintenance.Comment,
            Date = MaintenanceReadModelRules.NormalizeMaintenanceDate(maintenance.Date),
            PlannedDate = MaintenanceReadModelRules.NormalizeMaintenanceDate(maintenance.PlannedDate),
            PlannedStartTime = maintenance.PlannedStartTime,
            PlannedEndDate = maintenance.PlannedEndDate,
            PlannedEndTime = maintenance.PlannedEndTime,
            CompletedDate = MaintenanceReadModelRules.NormalizeMaintenanceDate(maintenance.CompletedDate),
            ReadyForHandoverAt = maintenance.ReadyForHandoverAt,
            HandedOverAt = maintenance.HandedOverAt,
            Location = maintenance.Location,
            EmployeeId = maintenance.EmployeeId,
            Performer = maintenance.Performer,
            Mileage = maintenance.Mileage,
            Status = maintenance.Status,
            Type = maintenance.Type,
            Price = finance.GrossAmount,
            GrossAmount = finance.GrossAmount,
            DiscountAmount = maintenance.DiscountAmount,
            PaidAmount = maintenance.PaidAmount,
            DebtAmount = finance.DebtAmount,
            MaterialCostAmount = maintenance.MaterialCostAmount,
            LaborCostAmount = maintenance.LaborCostAmount,
            ProfitAmount = finance.ProfitAmount,
            PaymentStatus = finance.PaymentStatus,
            PhotoIds = localMaintenancePhotos.Select(x => x.Id),
            MaintenanceDetails = maintenance.Details.Select(x => new MaintenanceDetailDto
            {
                DetailId = x.DetailId,
                Description = x.Description,
                Name = x.Name,
                Price = x.Price,
                VatRateCode = x.VatRateCode,
                Quantity = x.Quantity,
                EmployeeId = x.EmployeeId,
                EmployeeName = x.EmployeeName,
                CatalogItemId = x.CatalogItemId,
                MaterialCatalogItemId = x.MaterialCatalogItemId,
                MaterialName = x.MaterialName,
                MaterialQuantity = x.MaterialQuantity,
                MaterialUnit = x.MaterialUnit,
                MaterialUnitCost = x.MaterialUnitCost,
                MaterialTotalCost = x.MaterialTotalCost
            }),
            LinkedDefects = localLinkedDefects ?? []
        };
    }

    /// <summary> Преобразует запись обслуживания в DTO карточки. </summary>
    private static GetMaintenanceByIdDto MapToDto(
        MaintenanceEntity maintenance,
        IEnumerable<MaintenancePhoto> maintenancePhotos,
        IReadOnlyDictionary<Guid, List<MaintenanceLinkedDefectDto>> linkedDefects)
    {
        var localMaintenancePhotos = maintenancePhotos.Where(x => x.MaintenanceId == maintenance.Id);
        linkedDefects.TryGetValue(maintenance.Id, out var localLinkedDefects);
        var finance = MaintenanceReadModelRules.BuildFinanceSummary(maintenance);
        return new GetMaintenanceByIdDto
        {
            Version = maintenance.Version,
            IntakeNotes = maintenance.IntakeNotes,
            IntakeMileage = maintenance.IntakeMileage,
            IntakeCompleteness = maintenance.IntakeCompleteness,
            IntakeVisibleDamage = maintenance.IntakeVisibleDamage,
            IntakeRecordedAt = maintenance.IntakeRecordedAt,
            Id = maintenance.Id,
            Comment = maintenance.Comment,
            Date = MaintenanceReadModelRules.NormalizeMaintenanceDate(maintenance.Date),
            PlannedDate = MaintenanceReadModelRules.NormalizeMaintenanceDate(maintenance.PlannedDate),
            PlannedStartTime = maintenance.PlannedStartTime,
            PlannedEndDate = maintenance.PlannedEndDate,
            PlannedEndTime = maintenance.PlannedEndTime,
            CompletedDate = MaintenanceReadModelRules.NormalizeMaintenanceDate(maintenance.CompletedDate),
            ReadyForHandoverAt = maintenance.ReadyForHandoverAt,
            HandedOverAt = maintenance.HandedOverAt,
            Location = maintenance.Location,
            EmployeeId = maintenance.EmployeeId,
            Performer = maintenance.Performer,
            Mileage = maintenance.Mileage,
            Status = maintenance.Status,
            Type = maintenance.Type,
            VehicleId = maintenance.VehicleId,
            BuyerClientId = maintenance.BuyerClientId,
            BuyerName = maintenance.BuyerName,
            BuyerPartyTypeCode = maintenance.BuyerPartyTypeCode,
            BuyerInn = maintenance.BuyerInn,
            BuyerKpp = maintenance.BuyerKpp,
            BuyerLegalAddress = maintenance.BuyerLegalAddress,
            Price = finance.GrossAmount,
            GrossAmount = finance.GrossAmount,
            DiscountAmount = maintenance.DiscountAmount,
            PaidAmount = maintenance.PaidAmount,
            DebtAmount = finance.DebtAmount,
            MaterialCostAmount = maintenance.MaterialCostAmount,
            LaborCostAmount = maintenance.LaborCostAmount,
            ProfitAmount = finance.ProfitAmount,
            PaymentStatus = finance.PaymentStatus,
            PhotoIds = localMaintenancePhotos.Select(x => x.Id),
            MaintenanceDetails = maintenance.Details.Select(x => new MaintenanceDetailDto
            {
                DetailId = x.DetailId,
                Description = x.Description,
                Name = x.Name,
                Price = x.Price,
                VatRateCode = x.VatRateCode,
                Quantity = x.Quantity,
                EmployeeId = x.EmployeeId,
                EmployeeName = x.EmployeeName,
                CatalogItemId = x.CatalogItemId,
                MaterialCatalogItemId = x.MaterialCatalogItemId,
                MaterialName = x.MaterialName,
                MaterialQuantity = x.MaterialQuantity,
                MaterialUnit = x.MaterialUnit,
                MaterialUnitCost = x.MaterialUnitCost,
                MaterialTotalCost = x.MaterialTotalCost
            }),
            LinkedDefects = localLinkedDefects ?? []
        };
    }

    /// <summary> Загружает связанные дефекты для записей обслуживания. </summary>
    private async Task<Dictionary<Guid, List<MaintenanceLinkedDefectDto>>> LoadLinkedDefectsAsync(IEnumerable<Guid> maintenanceIds)
    {
        var ids = maintenanceIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await this._context.MaintenanceDefects
            .AsNoTracking()
            .Where(x => ids.Contains(x.MaintenanceId) && !x.VehicleDefect.IsDeleted)
            .Select(x => new
            {
                x.MaintenanceId,
                Defect = new MaintenanceLinkedDefectDto
                {
                    Id = x.VehicleDefect.Id,
                    Title = x.VehicleDefect.Title,
                    Description = x.VehicleDefect.Description,
                    SeverityCode = x.VehicleDefect.SeverityCode,
                    StatusCode = x.VehicleDefect.StatusCode,
                    CreatedAt = x.VehicleDefect.CreatedAt,
                    ClosedAt = x.VehicleDefect.ClosedAt
                }
            })
            .ToListAsync();

        return rows
            .GroupBy(x => x.MaintenanceId)
            .ToDictionary(x => x.Key, x => x.Select(row => row.Defect).ToList());
    }

    /// <summary> Проверяет право пользователя читать рабочее пространство. </summary>
    private async Task EnsureCanReadWorkspaceAsync(string userId, Guid? organizationId)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        await this._organizationSubscriptionService.EnsureWorkspaceCanReadAsync(
            userId,
            organizationId.Value,
            OrganizationPermissionCodes.MaintenanceView);
    }
}
