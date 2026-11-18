using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис работы с организациями в админке. </summary>
public sealed class AdminOrganizationService(OpenCRMContext opencrmContext, IConfiguration? configuration = null) : IAdminOrganizationService
{
    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает страницу со всеми организациями. </summary>
    public Task<AdminOrganizationsPageDto> GetAllAsync() =>
        this.LoadAsync(null, null, null, "created", true, 1, null, CancellationToken.None);

    /// <inheritdoc />
    public Task<AdminOrganizationsPageDto> GetAsync(
        string? search, string? user, string sort, bool descending, int page, int pageSize,
        CancellationToken cancellationToken = default) =>
        this.LoadAsync(null, search, user, sort, descending, Math.Clamp(page, 1, 1_000_000),
            Math.Clamp(pageSize, 1, 100), cancellationToken);

    /// <inheritdoc />
    public async Task<AdminOrganizationDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await this.LoadAsync(id, null, null, "created", true, 1, 1, cancellationToken);

        return result.Organizations.FirstOrDefault();
    }

    /// <summary> Загружает выбранную страницу, сохраняя совместимость полного списка веб-админки. </summary>
    private async Task<AdminOrganizationsPageDto> LoadAsync(
        Guid? id, string? search, string? user, string sort, bool descending, int page, int? pageSize,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var commercialEnabled = configuration?.GetValue("PlatformPolicy:Commercial:Enabled", false) ?? false;
        var freePlanName = commercialEnabled ? await this._opencrmContext.SubscriptionPlans
            .AsNoTracking()
            .Where(plan => plan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup)
            .Select(plan => plan.Name)
            .FirstOrDefaultAsync(cancellationToken) : null;
        var scenarioNames = await this._opencrmContext.OrganizationTypeDefinitions
            .AsNoTracking()
            .ToDictionaryAsync(definition => definition.Code, definition => definition.Name, cancellationToken);

        var query = this._opencrmContext.Organizations.AsNoTracking();
        if (id.HasValue)
        {
            query = query.Where(organization => organization.Id == id.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(organization => organization.Name.ToLower().Contains(term)
                || (organization.LegalName != null && organization.LegalName.ToLower().Contains(term))
                || (organization.Inn != null && organization.Inn.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(user))
        {
            var term = user.Trim().ToLower();
            query = query.Where(organization => organization.OwnerUserId.ToLower() == term
                || (organization.OwnerUser.Email != null && organization.OwnerUser.Email.ToLower().Contains(term))
                || (organization.OwnerUser.PhoneNumber != null && organization.OwnerUser.PhoneNumber.Contains(term))
                || organization.Members.Any(member => member.UserId.ToLower() == term
                    || (member.User.Email != null && member.User.Email.ToLower().Contains(term))
                    || (member.User.PhoneNumber != null && member.User.PhoneNumber.Contains(term))));
        }

        var counts = await query.GroupBy(organization => 1)
            .Select(group => new
            {
                All = group.Count(),
                Active = group.Count(organization => !organization.IsDeleted && !organization.IsArchived && organization.IsActive),
                Archived = group.Count(organization => !organization.IsDeleted && organization.IsArchived),
                Deleted = group.Count(organization => organization.IsDeleted)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var totalCount = counts?.All ?? 0;
        var projected = query
            .Select(organization => new
            {
                organization.Id,
                organization.Name,
                organization.CurrencyCode,
                organization.Description,
                organization.LegalName,
                organization.Inn,
                organization.Phone,
                organization.Email,
                organization.PrimaryScenarioCode,
                organization.DocumentStyleCode,
                organization.OwnerUserId,
                Owner = organization.OwnerUser.Email ?? organization.OwnerUser.PhoneNumber ?? organization.OwnerUserId,
                OwnerEmail = organization.OwnerUser.Email,
                OwnerPhone = organization.OwnerUser.PhoneNumber,
                organization.IsActive,
                organization.IsDeleted,
                organization.IsArchived,
                organization.CreatedAt,
                organization.UpdatedAt,
                organization.ArchiveDate,
                organization.DeletionDate,
                MembersCount = organization.Members.Count,
                AdminsCount = organization.Members.Count(member =>
                    member.RoleCode == OrganizationRoleCodes.Admin ||
                    member.RoleCode == OrganizationRoleCodes.Owner),
                PendingInvitationsCount = organization.Invitations.Count(invitation =>
                    invitation.StatusCode == OrganizationInvitationStatusCodes.Pending),
                ClientsCount = organization.Clients.Count(client => !client.IsDeleted),
                VehiclesCount = this._opencrmContext.Vehicles.Count(vehicle =>
                    vehicle.OrganizationId == organization.Id &&
                    !vehicle.IsDeleted),
                MaintenancesCount = this._opencrmContext.Maintenance.Count(maintenance =>
                    maintenance.Vehicle.OrganizationId == organization.Id &&
                    !maintenance.IsDeleted),
                CompletedWorkOrdersCount = this._opencrmContext.Maintenance.Count(maintenance =>
                    maintenance.Vehicle.OrganizationId == organization.Id && !maintenance.IsDeleted
                    && maintenance.Status == MaintenanceStatus.Completed),
                WorkOrdersTotal = this._opencrmContext.Maintenance
                    .Where(maintenance => maintenance.Vehicle.OrganizationId == organization.Id
                        && !maintenance.IsDeleted
                        && maintenance.CurrencyCode == organization.CurrencyCode)
                    .Sum(maintenance => (decimal?)Math.Max(0m,
                        maintenance.Details.Sum(detail => (decimal)(detail.Price ?? 0)) - maintenance.DiscountAmount)) ?? 0m,
                PaymentsTotal = this._opencrmContext.Maintenance
                    .Where(maintenance => maintenance.Vehicle.OrganizationId == organization.Id
                        && !maintenance.IsDeleted
                        && maintenance.CurrencyCode == organization.CurrencyCode)
                    .Sum(maintenance => (decimal?)maintenance.PaidAmount) ?? 0m,
                OpenDefectsCount = organization.Defects.Count(defect =>
                    !defect.IsDeleted &&
                    defect.StatusCode != VehicleDefectStatusCodes.Resolved),
                SchedulerTasksCount = this._opencrmContext.OrganizationSchedulerTasks.Count(task =>
                    task.OrganizationId == organization.Id &&
                    !task.IsDeleted),
                OverdueTasksCount = this._opencrmContext.OrganizationSchedulerTasks.Count(task =>
                    task.OrganizationId == organization.Id &&
                    !task.IsDeleted &&
                    task.StartAt.HasValue &&
                    task.StartAt.Value < now &&
                    task.ClosedAt == null),
                StockItemsCount = this._opencrmContext.OrganizationMaterialStocks.Count(stock =>
                    stock.OrganizationId == organization.Id),
                LowStockItemsCount = this._opencrmContext.OrganizationMaterialStocks.Count(stock =>
                    stock.OrganizationId == organization.Id &&
                    stock.QuantityOnHand <= stock.MinQuantity),
                InventoryValue = this._opencrmContext.OrganizationMaterialStocks
                    .Where(stock => stock.OrganizationId == organization.Id
                        && stock.CurrencyCode == organization.CurrencyCode)
                    .Sum(stock => (decimal?)stock.InventoryValue) ?? 0m,
                ActiveSubscriptionName = commercialEnabled ? this._opencrmContext.OrganizationSubscriptions
                    .Where(subscription => subscription.OrganizationId == organization.Id
                        && subscription.StartDate <= now && subscription.EndDate >= now)
                    .OrderByDescending(subscription => subscription.EndDate)
                    .Select(subscription => subscription.SubscriptionPlan.Name)
                    .FirstOrDefault()
                    ?? (organization.IsActive && !organization.IsArchived && !organization.IsDeleted
                        && (organization.PrimaryScenarioCode == OrganizationTypeCodes.Detailing
                            || organization.PrimaryScenarioCode == OrganizationTypeCodes.ServiceStation)
                            ? freePlanName
                            : null) : null,
                ActiveSubscriptionEndDate = commercialEnabled ? this._opencrmContext.OrganizationSubscriptions
                    .Where(subscription => subscription.OrganizationId == organization.Id
                        && subscription.StartDate <= now && subscription.EndDate >= now)
                    .OrderByDescending(subscription => subscription.EndDate)
                    .Select(subscription => (DateTime?)subscription.EndDate)
                    .FirstOrDefault() : null,
                LastAuditAt = organization.AuditEvents
                    .OrderByDescending(audit => audit.CreatedAt)
                    .Select(audit => (DateTime?)audit.CreatedAt)
                    .FirstOrDefault(),
                LastAuditTitle = organization.AuditEvents
                    .OrderByDescending(audit => audit.CreatedAt)
                    .Select(audit => audit.Title)
                    .FirstOrDefault(),
                Members = organization.Members
                    .OrderBy(member => member.RoleCode)
                    .ThenBy(member => member.User.Email)
                    .Select(member => new AdminOrganizationMemberDto
                    {
                        UserId = member.UserId,
                        User = member.User.Email ?? member.User.PhoneNumber ?? member.UserId,
                        Email = member.User.Email,
                        Phone = member.User.PhoneNumber,
                        RoleCode = member.RoleCode,
                        JoinedAt = member.JoinedAt
                    })
                    .Take(id.HasValue ? int.MaxValue : 8)
                    .ToList(),
                PendingInvitations = organization.Invitations
                    .Where(invitation => invitation.StatusCode == OrganizationInvitationStatusCodes.Pending)
                    .OrderByDescending(invitation => invitation.CreatedAt)
                    .Select(invitation => new AdminOrganizationInvitationDto
                    {
                        Email = invitation.Email,
                        RoleCode = invitation.RoleCode,
                        CreatedAt = invitation.CreatedAt,
                        InvitedBy = invitation.InvitedByUser.Email ??
                            invitation.InvitedByUser.PhoneNumber ??
                            invitation.InvitedByUserId
                    })
                    .Take(id.HasValue ? int.MaxValue : 6)
                    .ToList(),
                RecentAudit = organization.AuditEvents
                    .OrderByDescending(audit => audit.CreatedAt)
                    .Select(audit => new AdminOrganizationAuditDto
                    {
                        EventType = audit.EventType,
                        Title = audit.Title,
                        Description = audit.Description,
                        CreatedAt = audit.CreatedAt
                    })
                    .Take(6)
                    .ToList()
            });

        // Вторичная сортировка обеспечивает стабильный состав соседних страниц.
        var ordered = (sort, descending) switch
        {
            ("name", false) => projected.OrderBy(item => item.Name),
            ("name", true) => projected.OrderByDescending(item => item.Name),
            ("owner", false) => projected.OrderBy(item => item.Owner),
            ("owner", true) => projected.OrderByDescending(item => item.Owner),
            ("scenario", false) => projected.OrderBy(item => item.PrimaryScenarioCode),
            ("scenario", true) => projected.OrderByDescending(item => item.PrimaryScenarioCode),
            ("status", false) => projected.OrderBy(item => item.IsDeleted ? 3 : item.IsArchived ? 2 : item.IsActive ? 0 : 1),
            ("status", true) => projected.OrderByDescending(item => item.IsDeleted ? 3 : item.IsArchived ? 2 : item.IsActive ? 0 : 1),
            ("subscription", false) => projected.OrderBy(item => item.ActiveSubscriptionName),
            ("subscription", true) => projected.OrderByDescending(item => item.ActiveSubscriptionName),
            ("members", false) => projected.OrderBy(item => item.MembersCount),
            ("members", true) => projected.OrderByDescending(item => item.MembersCount),
            ("clients", false) => projected.OrderBy(item => item.ClientsCount),
            ("clients", true) => projected.OrderByDescending(item => item.ClientsCount),
            ("vehicles", false) => projected.OrderBy(item => item.VehiclesCount),
            ("vehicles", true) => projected.OrderByDescending(item => item.VehiclesCount),
            ("orders", false) => projected.OrderBy(item => item.MaintenancesCount),
            ("orders", true) => projected.OrderByDescending(item => item.MaintenancesCount),
            (_, false) => projected.OrderBy(item => item.CreatedAt),
            _ => projected.OrderByDescending(item => item.CreatedAt)
        };

        var pageQuery = ordered.ThenBy(item => item.Id).AsQueryable();
        if (pageSize.HasValue)
        {
            pageQuery = pageQuery.Skip((page - 1) * pageSize.Value).Take(pageSize.Value);
        }

        var attentionCount = await projected.CountAsync(organization =>
            organization.PendingInvitationsCount > 0 || organization.OverdueTasksCount > 0
            || organization.LowStockItemsCount > 0 || organization.OpenDefectsCount > 0, cancellationToken);

        var organizations = await pageQuery.ToListAsync(cancellationToken);

        var organizationCurrencies = organizations.ToDictionary(organization => organization.Id, organization => organization.CurrencyCode);
        var organizationIds = organizationCurrencies.Keys.ToArray();
        var otherMaintenanceTotals = await this._opencrmContext.Maintenance
            .AsNoTracking()
            .Where(maintenance => maintenance.Vehicle.OrganizationId.HasValue
                && organizationIds.Contains(maintenance.Vehicle.OrganizationId.Value)
                && !maintenance.IsDeleted)
            .GroupBy(maintenance => new { OrganizationId = maintenance.Vehicle.OrganizationId!.Value, maintenance.CurrencyCode })
            .Select(group => new
            {
                OrganizationId = group.Key.OrganizationId,
                group.Key.CurrencyCode,
                WorkOrdersTotal = group.Sum(maintenance => Math.Max(0m,
                    maintenance.Details.Sum(detail => (decimal)(detail.Price ?? 0)) - maintenance.DiscountAmount)),
                PaymentsTotal = group.Sum(maintenance => maintenance.PaidAmount)
            })
            .ToListAsync(cancellationToken);
        var otherInventoryTotals = await this._opencrmContext.OrganizationMaterialStocks
            .AsNoTracking()
            .Where(stock => organizationIds.Contains(stock.OrganizationId))
            .GroupBy(stock => new { stock.OrganizationId, stock.CurrencyCode })
            .Select(group => new
            {
                OrganizationId = group.Key.OrganizationId,
                group.Key.CurrencyCode,
                InventoryValue = group.Sum(stock => stock.InventoryValue)
            })
            .ToListAsync(cancellationToken);
        var maintenanceTotalsByCurrency = otherMaintenanceTotals
            .Where(total => total.CurrencyCode != organizationCurrencies[total.OrganizationId])
            .ToDictionary(total => (total.OrganizationId, total.CurrencyCode));
        var inventoryTotalsByCurrency = otherInventoryTotals
            .Where(total => total.CurrencyCode != organizationCurrencies[total.OrganizationId])
            .ToDictionary(total => (total.OrganizationId, total.CurrencyCode));
        var otherCurrencyKeys = maintenanceTotalsByCurrency.Keys
            .Concat(inventoryTotalsByCurrency.Keys)
            .Distinct()
            .ToArray();
        var otherCurrencyTotals = new Dictionary<(Guid OrganizationId, string CurrencyCode), AdminOrganizationCurrencyTotalDto>();
        foreach (var key in otherCurrencyKeys)
        {
            maintenanceTotalsByCurrency.TryGetValue(key, out var maintenanceTotal);
            inventoryTotalsByCurrency.TryGetValue(key, out var inventoryTotal);
            otherCurrencyTotals.Add(key, new AdminOrganizationCurrencyTotalDto
            {
                CurrencyCode = key.CurrencyCode,
                WorkOrdersTotal = maintenanceTotal?.WorkOrdersTotal ?? 0m,
                PaymentsTotal = maintenanceTotal?.PaymentsTotal ?? 0m,
                InventoryValue = inventoryTotal?.InventoryValue ?? 0m
            });
        }

        var items = organizations
            .Select(organization => new AdminOrganizationDto
            {
                Id = organization.Id,
                Name = organization.Name,
                Description = organization.Description,
                LegalName = organization.LegalName,
                Inn = organization.Inn,
                Phone = organization.Phone,
                Email = organization.Email,
                PrimaryScenarioCode = organization.PrimaryScenarioCode,
                CurrencyCode = organization.CurrencyCode,
                ScenarioName = scenarioNames.TryGetValue(organization.PrimaryScenarioCode, out var scenarioName)
                    ? scenarioName
                    : organization.PrimaryScenarioCode,
                DocumentStyleCode = organization.DocumentStyleCode,
                OwnerUserId = organization.OwnerUserId,
                Owner = organization.Owner,
                OwnerEmail = organization.OwnerEmail,
                OwnerPhone = organization.OwnerPhone,
                IsActive = organization.IsActive,
                IsDeleted = organization.IsDeleted,
                IsArchived = organization.IsArchived,
                StatusCode = ResolveStatusCode(organization.IsDeleted, organization.IsArchived, organization.IsActive),
                StatusName = ResolveStatusName(organization.IsDeleted, organization.IsArchived, organization.IsActive),
                CreatedAt = organization.CreatedAt,
                UpdatedAt = organization.UpdatedAt,
                ArchiveDate = organization.ArchiveDate,
                DeletionDate = organization.DeletionDate,
                MembersCount = organization.MembersCount,
                AdminsCount = organization.AdminsCount,
                PendingInvitationsCount = organization.PendingInvitationsCount,
                ClientsCount = organization.ClientsCount,
                VehiclesCount = organization.VehiclesCount,
                MaintenancesCount = organization.MaintenancesCount,
                WorkOrdersCount = organization.MaintenancesCount,
                CompletedWorkOrdersCount = organization.CompletedWorkOrdersCount,
                WorkOrdersTotal = organization.WorkOrdersTotal,
                PaymentsTotal = organization.PaymentsTotal,
                OpenDefectsCount = organization.OpenDefectsCount,
                SchedulerTasksCount = organization.SchedulerTasksCount,
                OverdueTasksCount = organization.OverdueTasksCount,
                StockItemsCount = organization.StockItemsCount,
                LowStockItemsCount = organization.LowStockItemsCount,
                InventoryValue = organization.InventoryValue,
                OtherCurrencyTotals = otherCurrencyTotals
                    .Where(total => total.Key.OrganizationId == organization.Id)
                    .Select(total => total.Value)
                    .ToArray(),
                ActiveSubscriptionName = organization.ActiveSubscriptionName,
                ActiveSubscriptionEndDate = organization.ActiveSubscriptionEndDate,
                LastAuditAt = organization.LastAuditAt,
                LastAuditTitle = organization.LastAuditTitle,
                Members = organization.Members,
                PendingInvitations = organization.PendingInvitations,
                RecentAudit = organization.RecentAudit
            })
            .ToList();

        return new AdminOrganizationsPageDto
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize ?? totalCount,
            AllCount = totalCount,
            ActiveCount = counts?.Active ?? 0,
            ArchivedCount = counts?.Archived ?? 0,
            DeletedCount = counts?.Deleted ?? 0,
            AttentionCount = attentionCount,
            Organizations = items
        };
    }

    /// <summary> Определяет код статуса организации по признакам. </summary>
    private static string ResolveStatusCode(bool isDeleted, bool isArchived, bool isActive)
    {
        if (isDeleted)
        {
            return "deleted";
        }

        if (isArchived)
        {
            return "archived";
        }

        return isActive ? "active" : "inactive";
    }

    /// <summary> Определяет название статуса организации по признакам. </summary>
    private static string ResolveStatusName(bool isDeleted, bool isArchived, bool isActive)
    {
        if (isDeleted)
        {
            return "Удалена";
        }

        if (isArchived)
        {
            return "Архив";
        }

        return isActive ? "Активна" : "Неактивна";
    }
}
