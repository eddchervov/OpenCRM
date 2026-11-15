using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Clients.Rules;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Queries;

/// <summary> Читатель постраничных списков клиентов организации. </summary>
internal sealed class OrganizationClientPageReader(
    OpenCRMContext context,
    ClientsAccess access,
    ClientReadData readData,
    ClientVehicleReader vehicles,
    IMaintenanceClientSummaryQueryService maintenance,
    IOrganizationPermissionService permissions)
{
    /// <summary> Код сегмента всех клиентов. </summary>
    private const string SegmentAll = "all";

    /// <summary> Код сегмента потенциальных клиентов. </summary>
    private const string SegmentLeads = "leads";

    /// <summary> Код сегмента клиентов, требующих внимания. </summary>
    private const string SegmentAttention = "attention";

    /// <summary> Код сегмента клиентов без ответственного. </summary>
    private const string SegmentNoResponsible = "no_responsible";

    /// <summary> Код сегмента клиентов без техники. </summary>
    private const string SegmentNoVehicles = "no_vehicles";

    /// <summary> Код сегмента клиентов без недавнего обслуживания. </summary>
    private const string SegmentNoRecentService = "no_recent_service";

    /// <summary> Контекст базы данных для чтения страниц клиентов. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Зависимость проверки доступа к клиентам. </summary>
    private readonly ClientsAccess _access = access;

    /// <summary> Зависимость читателя вспомогательных данных клиентов. </summary>
    private readonly ClientReadData _readData = readData;

    /// <summary> Зависимость читателя техники клиентов. </summary>
    private readonly ClientVehicleReader _vehicles = vehicles;

    /// <summary> Зависимость сервиса сводок обслуживаний клиентов. </summary>
    private readonly IMaintenanceClientSummaryQueryService _maintenance = maintenance;

    /// <summary> Зависимость проверки прав пользователя в организации. </summary>
    private readonly IOrganizationPermissionService _permissions = permissions;

    /// <summary> Возвращает страницу списка клиентов организации. </summary>
    public async Task<OrganizationClientsPageDto> GetPageAsync(
        string userId,
        Guid organizationId,
        string? searchQuery,
        string? sort,
        int page,
        int pageSize,
        bool isArchived = false,
        string? statusCode = null,
        string? responsibleUserId = null,
        Guid? tagId = null,
        string? segmentCode = null,
        string? partyTypeCode = null)
    {
        await this._access.EnsureEnabledAsync(userId, organizationId);

        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize switch
        {
            25 => 25,
            50 => 50,
            _ => 10
        };

        var baseQuery = this._context.OrganizationClients
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.IsDeleted == isArchived);

        var allCount = await baseQuery.CountAsync();
        var tagOptions = await this._readData.LoadTagOptionsAsync(organizationId);
        var segmentOptions = await this.LoadSegmentOptionsAsync(baseQuery);
        var totalVehiclesCount = await this._context.Vehicles
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.OrganizationId == organizationId && x.OrganizationClientId.HasValue)
            .CountAsync();
        var totalMaintenanceCount = await this._maintenance.GetLinkedCountAsync(organizationId);

        var normalizedSearch = ClientRules.NormalizeNullable(searchQuery);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var searchPattern = $"%{normalizedSearch}%";
            var isPhoneSearch = normalizedSearch.All(character =>
                char.IsDigit(character) || char.IsWhiteSpace(character) || character is '+' or '-' or '(' or ')' or '.');
            var phonePatterns = isPhoneSearch ? ClientPhoneSearch.GetSearchPatterns(normalizedSearch) : [];
            var matchingPhoneIds = ClientPhoneSearch.GetCanonicalPhones(baseQuery)
                .Where(phone => phonePatterns.Any(pattern => EF.Functions.ILike(phone.Number, pattern)))
                .Select(phone => phone.Id);
            baseQuery = baseQuery.Where(x =>
                EF.Functions.ILike(x.Name, searchPattern)
                || (!string.IsNullOrEmpty(x.Phone) && EF.Functions.ILike(x.Phone, searchPattern))
                || matchingPhoneIds.Contains(x.Id)
                || (!string.IsNullOrEmpty(x.Comment) && EF.Functions.ILike(x.Comment, searchPattern)));
        }

        var normalizedStatusCode = ClientRules.NormalizeNullable(statusCode);
        if (!string.IsNullOrWhiteSpace(normalizedStatusCode))
        {
            var filterStatusCode = OrganizationClientStatusCodes.Normalize(normalizedStatusCode);
            baseQuery = baseQuery.Where(x => x.StatusCode == filterStatusCode);
        }

        var normalizedResponsibleUserId = ClientRules.NormalizeNullable(responsibleUserId);
        if (!string.IsNullOrWhiteSpace(normalizedResponsibleUserId))
        {
            baseQuery = string.Equals(normalizedResponsibleUserId, "__none", StringComparison.Ordinal)
                ? baseQuery.Where(x => x.ResponsibleUserId == null || x.ResponsibleUserId == string.Empty)
                : baseQuery.Where(x => x.ResponsibleUserId == normalizedResponsibleUserId);
        }

        if (tagId.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.TagAssignments.Any(tag => tag.OrganizationClientTagId == tagId.Value));
        }

        var normalizedPartyTypeCode = ClientRules.NormalizeNullable(partyTypeCode)?.ToLowerInvariant();
        if (normalizedPartyTypeCode is "person" or "company")
        {
            baseQuery = baseQuery.Where(x => x.PartyTypeCode == normalizedPartyTypeCode);
        }

        baseQuery = OrganizationClientPageReader.ApplyClientSegment(baseQuery, segmentCode);

        // Составные агрегаты нужны в SQL до сортировки и пагинации; покупатель и владелец техники различаются.
        var clientRowsQuery = baseQuery
            .Select(x => new
            {
                x.Id,
                x.OrganizationId,
                x.Name,
                x.LastName,
                x.FirstName,
                x.MiddleName,
                x.CompanyTypeCode,
                x.Phone,
                x.PartyTypeCode,
                x.Inn,
                x.Kpp,
                x.LegalAddress,
                x.Comment,
                x.StatusCode,
                x.ResponsibleUserId,
                x.CreatedAt,
                x.IsDeleted,
                x.DeletionDate,
                VehiclesCount = x.Vehicles.Count(v => !v.IsDeleted),
                MaintenanceCount = this._context.Maintenance.Count(m => !m.IsDeleted
                    && !m.Vehicle.IsDeleted && m.Vehicle.OrganizationId == organizationId
                    && m.BuyerClientId == x.Id),
                LastMaintenanceDate = this._context.Maintenance
                    .Where(m => !m.IsDeleted && !m.Vehicle.IsDeleted
                        && m.Vehicle.OrganizationId == organizationId && m.Status == MaintenanceStatus.Completed
                        && m.BuyerClientId == x.Id)
                    .Select(m => m.CompletedDate ?? (DateTime?)m.Date)
                    .Max()
            });

        var sortedClientsQuery = OrganizationClientPageReader.ApplyClientSort(clientRowsQuery, sort);
        var totalCount = await sortedClientsQuery.CountAsync();
        var pagedClientRows = await sortedClientsQuery
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        var clientIds = pagedClientRows.Select(x => x.Id).ToList();
        var vehiclesByClient = await this._vehicles.LoadAsync(organizationId, clientIds, sortByName: true);

        var canViewFinance = await this._permissions.HasPermissionAsync(
                userId, organizationId, OrganizationPermissionCodes.FinanceView)
            || await this._permissions.HasPermissionAsync(
                userId, organizationId, OrganizationPermissionCodes.FinanceManage);
        var financeByClient = canViewFinance
            ? await this.LoadFinanceTotalsAsync(organizationId, clientIds)
            : new Dictionary<Guid, (decimal Paid, decimal Debt)>();

        var responsibleDisplayNames = await this._readData.LoadResponsibleDisplayNamesAsync(
            organizationId, pagedClientRows.Select(x => x.ResponsibleUserId));

        var tagsByClient = await this._readData.LoadTagsByClientAsync(clientIds);

        var pagedClients = pagedClientRows.Select(client =>
        {
            vehiclesByClient.TryGetValue(client.Id, out var vehicles);
            vehicles ??= Array.Empty<OrganizationClientVehicleDto>();
            tagsByClient.TryGetValue(client.Id, out var tags);
            tags ??= Array.Empty<OrganizationClientTagDto>();
            var statusCode = OrganizationClientStatusCodes.Normalize(client.StatusCode);
            financeByClient.TryGetValue(client.Id, out var finance);

            return new OrganizationClientDto
            {
                Id = client.Id,
                OrganizationId = client.OrganizationId,
                Name = client.Name,
                LastName = client.LastName,
                FirstName = client.FirstName,
                MiddleName = client.MiddleName,
                CompanyTypeCode = client.CompanyTypeCode,
                Phone = client.Phone,
                PartyTypeCode = client.PartyTypeCode,
                Inn = client.Inn,
                Kpp = client.Kpp,
                LegalAddress = client.LegalAddress,
                Comment = client.Comment,
                StatusCode = statusCode,
                StatusName = OrganizationClientStatusCodes.GetName(statusCode),
                ResponsibleUserId = client.ResponsibleUserId,
                ResponsibleDisplayName = ClientRules.ResolveResponsibleDisplayName(client.ResponsibleUserId, responsibleDisplayNames),
                Tags = tags,
                CreatedAt = client.CreatedAt,
                IsArchived = client.IsDeleted,
                ArchivedAt = client.DeletionDate,
                Vehicles = vehicles,
                VehiclesCount = client.VehiclesCount,
                MaintenanceCount = client.MaintenanceCount,
                LastMaintenanceDate = client.LastMaintenanceDate,
                TotalPaidAmount = canViewFinance ? finance.Paid : null,
                TotalDebtAmount = canViewFinance ? finance.Debt : null
            };
        }).ToList();

        return new OrganizationClientsPageDto
        {
            Items = pagedClients,
            Tags = tagOptions,
            Segments = segmentOptions,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalCount = totalCount,
            AllCount = allCount,
            TotalVehiclesCount = totalVehiclesCount,
            TotalMaintenanceCount = totalMaintenanceCount
        };
    }

    /// <summary> Загружает суммы оплат и долгов для клиентов текущей страницы. </summary>
    private async Task<Dictionary<Guid, (decimal Paid, decimal Debt)>> LoadFinanceTotalsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> clientIds)
    {
        if (clientIds.Count == 0)
        {
            return [];
        }

        var maintenanceRows = await this._context.Maintenance
            .AsNoTracking()
            .Where(maintenance => !maintenance.IsDeleted
                && !maintenance.Vehicle.IsDeleted
                && maintenance.Vehicle.OrganizationId == organizationId
                && maintenance.BuyerClientId.HasValue
                && clientIds.Contains(maintenance.BuyerClientId.Value))
            .Select(maintenance => new
            {
                ClientId = maintenance.BuyerClientId!.Value,
                maintenance.Status,
                maintenance.PaidAmount,
                maintenance.DiscountAmount,
                GrossAmount = maintenance.Details.Sum(detail => (decimal?)(detail.Price ?? 0)) ?? 0
            })
            .ToListAsync();
        var paidByClient = await this._context.MaintenancePayments
            .AsNoTracking()
            .Where(payment => !payment.CancelledAt.HasValue
                && !payment.Maintenance.IsDeleted
                && !payment.Maintenance.Vehicle.IsDeleted
                && payment.Maintenance.Vehicle.OrganizationId == organizationId
                && payment.Maintenance.BuyerClientId.HasValue
                && clientIds.Contains(payment.Maintenance.BuyerClientId.Value))
            .GroupBy(payment => payment.Maintenance.BuyerClientId!.Value)
            .Select(group => new { ClientId = group.Key, Amount = group.Sum(payment => payment.Amount) })
            .ToDictionaryAsync(item => item.ClientId, item => item.Amount);

        return maintenanceRows
            .GroupBy(maintenance => maintenance.ClientId)
            .ToDictionary(
                group => group.Key,
                group => (
                    Paid: Math.Round(paidByClient.GetValueOrDefault(group.Key), 2, MidpointRounding.AwayFromZero),
                    Debt: Math.Round(group.Where(maintenance => maintenance.Status != MaintenanceStatus.Cancelled).Sum(maintenance => Math.Max(
                        0,
                        Math.Round(maintenance.GrossAmount, 2, MidpointRounding.AwayFromZero)
                            - maintenance.DiscountAmount
                            - maintenance.PaidAmount)), 2, MidpointRounding.AwayFromZero)));
    }

    /// <summary> Применяет сортировку клиентов к запросу. </summary>
    private static IQueryable<TClient> ApplyClientSort<TClient>(IQueryable<TClient> clients, string? sort)
        where TClient : class
    {
        return (sort ?? "name_asc") switch
        {
            "name_desc" => clients.OrderByDescending(x => EF.Property<string>(x, "Name")).ThenByDescending(x => EF.Property<DateTime>(x, "CreatedAt")),
            "vehicles_desc" => clients.OrderByDescending(x => EF.Property<int>(x, "VehiclesCount")).ThenBy(x => EF.Property<string>(x, "Name")),
            "maintenance_desc" => clients.OrderByDescending(x => EF.Property<int>(x, "MaintenanceCount")).ThenBy(x => EF.Property<string>(x, "Name")),
            "recent_desc" => clients.OrderByDescending(x => EF.Property<DateTime?>(x, "LastMaintenanceDate")).ThenBy(x => EF.Property<string>(x, "Name")),
            _ => clients.OrderBy(x => EF.Property<string>(x, "Name")).ThenByDescending(x => EF.Property<DateTime>(x, "CreatedAt"))
        };
    }

    /// <summary> Применяет фильтр сегмента к запросу клиентов. </summary>
    private static IQueryable<OrganizationClient> ApplyClientSegment(IQueryable<OrganizationClient> query, string? segmentCode)
    {
        var noRecentServiceThreshold = DateTime.UtcNow.AddDays(-180);
        return OrganizationClientPageReader.NormalizeSegmentCode(segmentCode) switch
        {
            SegmentLeads => query.Where(x => x.StatusCode == OrganizationClientStatusCodes.Lead),
            SegmentAttention => query.Where(x =>
                x.StatusCode == OrganizationClientStatusCodes.Attention
                || x.Vehicles.Any(vehicle => !vehicle.IsDeleted
                    && vehicle.Defects.Any(defect => !defect.IsDeleted && defect.StatusCode != VehicleDefectStatusCodes.Resolved))),
            SegmentNoResponsible => query.Where(x => x.ResponsibleUserId == null || x.ResponsibleUserId == string.Empty),
            SegmentNoVehicles => query.Where(x => !x.Vehicles.Any(vehicle => !vehicle.IsDeleted)),
            SegmentNoRecentService => query.Where(x => !x.Vehicles
                .Where(vehicle => !vehicle.IsDeleted)
                .SelectMany(vehicle => vehicle.Maintenance.Where(maintenance => !maintenance.IsDeleted))
                .Any(maintenance => (maintenance.Status == MaintenanceStatus.Completed
                    ? (maintenance.CompletedDate ?? maintenance.Date)
                    : (maintenance.PlannedDate ?? maintenance.Date)) >= noRecentServiceThreshold)),
            _ => query
        };
    }

    /// <summary> Загружает варианты сегментов клиентов со счётчиками. </summary>
    private async Task<IReadOnlyList<OrganizationClientSegmentDto>> LoadSegmentOptionsAsync(IQueryable<OrganizationClient> baseQuery)
    {
        var allCount = await baseQuery.CountAsync();
        var leadsCount = await OrganizationClientPageReader.ApplyClientSegment(baseQuery, SegmentLeads).CountAsync();
        var attentionCount = await OrganizationClientPageReader.ApplyClientSegment(baseQuery, SegmentAttention).CountAsync();
        var noResponsibleCount = await OrganizationClientPageReader.ApplyClientSegment(baseQuery, SegmentNoResponsible).CountAsync();
        var noVehiclesCount = await OrganizationClientPageReader.ApplyClientSegment(baseQuery, SegmentNoVehicles).CountAsync();
        var noRecentServiceCount = await OrganizationClientPageReader.ApplyClientSegment(baseQuery, SegmentNoRecentService).CountAsync();

        return
        [
            new OrganizationClientSegmentDto { Code = SegmentAll, Name = "Все", Count = allCount },
            new OrganizationClientSegmentDto { Code = SegmentLeads, Name = "Лиды", Count = leadsCount },
            new OrganizationClientSegmentDto { Code = SegmentAttention, Name = "Внимание", Count = attentionCount },
            new OrganizationClientSegmentDto { Code = SegmentNoResponsible, Name = "Без ответственного", Count = noResponsibleCount },
            new OrganizationClientSegmentDto { Code = SegmentNoVehicles, Name = "Без техники", Count = noVehiclesCount },
            new OrganizationClientSegmentDto { Code = SegmentNoRecentService, Name = "Без свежего ТО", Count = noRecentServiceCount }
        ];
    }

    /// <summary> Нормализует код сегмента клиентов. </summary>
    private static string NormalizeSegmentCode(string? segmentCode)
    {
        var normalized = ClientRules.NormalizeNullable(segmentCode)?.ToLowerInvariant();
        return normalized is SegmentLeads or SegmentAttention or SegmentNoResponsible or SegmentNoVehicles or SegmentNoRecentService
            ? normalized
            : SegmentAll;
    }
}
