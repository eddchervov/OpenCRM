using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Clients.Rules;
using OpenCRM.Modules.Maintenance.Contracts;

namespace OpenCRM.Modules.Clients.Queries;

/// <summary> Сервис запросов клиентов организации. </summary>
internal sealed class OrganizationClientQueryService(
    OpenCRMContext context,
    ClientsAccess access,
    ClientReadData readData,
    ClientVehicleReader vehicles,
    OrganizationClientPageReader pages,
    IMaintenanceClientSummaryQueryService maintenance) : IOrganizationClientQueryService
{
    private const int LookupPageSize = 8;

    /// <summary> Контекст базы данных для запросов клиентов. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Зависимость проверки доступа к клиентам. </summary>
    private readonly ClientsAccess _access = access;

    /// <summary> Зависимость читателя вспомогательных данных клиентов. </summary>
    private readonly ClientReadData _readData = readData;

    /// <summary> Зависимость читателя техники клиентов. </summary>
    private readonly ClientVehicleReader _vehicles = vehicles;

    /// <summary> Зависимость читателя страниц клиентов. </summary>
    private readonly OrganizationClientPageReader _pages = pages;

    /// <summary> Зависимость сервиса сводок обслуживаний клиентов. </summary>
    private readonly IMaintenanceClientSummaryQueryService _maintenance = maintenance;

    /// <summary> Возвращает список клиентов организации. </summary>
    public Task<IReadOnlyList<OrganizationClientDto>> GetAsync(string userId, Guid organizationId, string? searchQuery = null) =>
        this.GetClientsAsync(userId, organizationId, searchQuery, null);

    /// <summary> Возвращает страницу списка клиентов организации. </summary>
    public Task<OrganizationClientsPageDto> GetPageAsync(
        string userId, Guid organizationId, string? searchQuery, string? sort, int page, int pageSize,
        bool isArchived = false, string? statusCode = null, string? responsibleUserId = null,
        Guid? tagId = null, string? segmentCode = null, string? partyTypeCode = null) =>
        this._pages.GetPageAsync(userId, organizationId, searchQuery, sort, page, pageSize,
            isArchived, statusCode, responsibleUserId, tagId, segmentCode, partyTypeCode);

    /// <summary> Возвращает страницу кратких вариантов клиентов для поиска при выборе. </summary>
    public async Task<OrganizationClientLookupPageDto> LookupAsync(
        string userId,
        Guid organizationId,
        string? searchQuery,
        int page,
        CancellationToken cancellationToken = default)
    {
        await this._access.EnsureEnabledAsync(userId, organizationId);

        var normalizedSearch = ClientRules.NormalizeNullable(searchQuery);
        if (normalizedSearch?.Length > 100)
        {
            throw new ArgumentException("Поисковый запрос не должен превышать 100 символов.", nameof(searchQuery));
        }

        if (normalizedSearch is null || normalizedSearch.Length < 2)
        {
            return new OrganizationClientLookupPageDto();
        }

        var isPhoneSearch = normalizedSearch.All(character =>
            char.IsDigit(character) || character is ' ' or '+' or '-' or '(' or ')' or '.');
        var phonePatterns = isPhoneSearch ? ClientPhoneSearch.GetSearchPatterns(normalizedSearch) : [];
        var clientsQuery = this._context.OrganizationClients
            .AsNoTracking()
            .Where(client => client.OrganizationId == organizationId && !client.IsDeleted);

        var matchingPhoneIds = ClientPhoneSearch.GetCanonicalPhones(clientsQuery)
            .Where(phone => phonePatterns.Any(pattern => EF.Functions.ILike(phone.Number, pattern)))
            .Select(phone => phone.Id);
        var searchPattern = $"%{OrganizationClientQueryService.EscapeLikePattern(normalizedSearch)}%";
        clientsQuery = clientsQuery.Where(client =>
            EF.Functions.ILike(client.Name, searchPattern, "\\")
            || (!isPhoneSearch && client.Phone != null && EF.Functions.ILike(client.Phone, searchPattern, "\\"))
            || matchingPhoneIds.Contains(client.Id));

        var totalCount = await clientsQuery.CountAsync(cancellationToken);
        var offset = (int)Math.Min(((long)Math.Max(page, 1) - 1) * LookupPageSize, int.MaxValue);
        var items = await clientsQuery
            .OrderBy(client => client.Name)
            .ThenBy(client => client.Id)
            .Skip(offset)
            .Take(LookupPageSize)
            .Select(client => new OrganizationClientLookupItemDto
            {
                Id = client.Id,
                Name = client.Name,
                Phone = client.Phone,
                PartyTypeCode = client.PartyTypeCode
            })
            .ToListAsync(cancellationToken);

        return new OrganizationClientLookupPageDto
        {
            Items = items,
            TotalCount = totalCount
        };
    }

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary> Возвращает список клиентов с фильтрами. </summary>
    private async Task<IReadOnlyList<OrganizationClientDto>> GetClientsAsync(string userId, Guid organizationId, string? searchQuery, Guid? clientId)
    {
        await this._access.EnsureEnabledAsync(userId, organizationId);

        var normalizedSearch = ClientRules.NormalizeNullable(searchQuery);

        var clientsQuery = this._context.OrganizationClients
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && !x.IsDeleted);

        if (clientId.HasValue)
        {
            clientsQuery = clientsQuery.Where(client => client.Id == clientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var searchPattern = $"%{normalizedSearch}%";
            clientsQuery = clientsQuery.Where(x =>
                EF.Functions.ILike(x.Name, searchPattern)
                || (!string.IsNullOrEmpty(x.Phone) && EF.Functions.ILike(x.Phone, searchPattern))
                || (!string.IsNullOrEmpty(x.Comment) && EF.Functions.ILike(x.Comment, searchPattern)));
        }

        var clients = await clientsQuery
            .OrderBy(x => x.Name)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync();

        if (clients.Count == 0)
        {
            return [];
        }

        var clientIds = clients.Select(x => x.Id).ToList();
        var maintenanceStatsByClient = await this._maintenance.GetByClientIdsAsync(organizationId, clientIds);
        var vehiclesByClient = await this._vehicles.LoadAsync(organizationId, clientIds, sortByName: false);

        var responsibleDisplayNames = await this._readData.LoadResponsibleDisplayNamesAsync(
            organizationId, clients.Select(x => x.ResponsibleUserId));

        var tagsByClient = await this._readData.LoadTagsByClientAsync(clientIds);

        return clients.Select(client =>
        {
            vehiclesByClient.TryGetValue(client.Id, out var vehicles);
            vehicles ??= Array.Empty<OrganizationClientVehicleDto>();
            tagsByClient.TryGetValue(client.Id, out var tags);
            tags ??= Array.Empty<OrganizationClientTagDto>();
            maintenanceStatsByClient.TryGetValue(client.Id, out var maintenanceStats);
            var statusCode = OrganizationClientStatusCodes.Normalize(client.StatusCode);

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
                VehiclesCount = vehicles.Count,
                MaintenanceCount = maintenanceStats?.Count ?? 0,
                LastMaintenanceDate = maintenanceStats?.LastDate
            };
        }).ToList();
    }

    /// <summary> Возвращает варианты выбора клиентов организации. </summary>
    public async Task<IReadOnlyList<OrganizationClientOptionDto>> GetOptionsAsync(string userId, Guid organizationId)
    {
        await this._access.EnsureEnabledAsync(userId, organizationId);

        return await this._context.OrganizationClients
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && !x.IsDeleted)
            .OrderBy(x => x.Name)
            .Select(x => new OrganizationClientOptionDto
            {
                Id = x.Id,
                Name = x.Name,
                Phone = x.Phone,
                StatusName = OrganizationClientStatusCodes.GetName(x.StatusCode),
                VehiclesCount = x.Vehicles.Count(v => !v.IsDeleted),
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();
    }

    /// <summary> Возвращает клиента по идентификатору. </summary>
    public async Task<OrganizationClientDto?> GetByIdAsync(string userId, Guid organizationId, Guid clientId)
    {
        var clients = await this.GetClientsAsync(userId, organizationId, null, clientId);

        return clients.FirstOrDefault(x => x.Id == clientId);
    }
}
