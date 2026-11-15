using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Defects.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Vehicles.Defects;

/// <summary> Сервис чтения дефектов техники организации. </summary>
internal sealed class VehicleDefectQueryService(
    OpenCRMContext context,
    IOrganizationSubscriptionService organizationSubscriptionService)
{
    /// <summary> Возвращает постраничный список дефектов с фильтрами. </summary>
    internal async Task<VehicleDefectListResponse> GetAsync(
        string userId,
        Guid organizationId,
        int page = 1,
        int pageSize = 12,
        string? searchQuery = null,
        string? statusCode = null,
        Guid? vehicleId = null,
        string? sort = null)
    {
        await VehicleDefectRules.EnsureDefectsModuleEnabledAsync(organizationSubscriptionService, userId, organizationId);

        var normalizedSearch = VehicleDefectRules.NormalizeNullable(searchQuery);
        var normalizedStatusCode = VehicleDefectRules.NormalizeOptionalStatusCode(statusCode);
        var normalizedSort = VehicleDefectRules.NormalizeSort(sort);
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 12 : pageSize;

        var baseQuery = context.VehicleDefects
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && !x.IsDeleted);

        var allCount = await baseQuery.CountAsync();
        var openCount = await baseQuery.CountAsync(x => x.StatusCode != VehicleDefectStatusCodes.Resolved);
        var resolvedCount = await baseQuery.CountAsync(x => x.StatusCode == VehicleDefectStatusCodes.Resolved);

        var query = baseQuery;

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var searchPattern = $"%{normalizedSearch}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Title, searchPattern)
                || (!string.IsNullOrEmpty(x.Description) && EF.Functions.ILike(x.Description, searchPattern)));
        }

        if (!string.IsNullOrWhiteSpace(normalizedStatusCode))
        {
            query = query.Where(x => x.StatusCode == normalizedStatusCode);
        }

        if (vehicleId.HasValue)
        {
            query = query.Where(x => x.VehicleId == vehicleId.Value);
        }

        var totalCount = await query.CountAsync();

        var sortedQuery = VehicleDefectRules.ApplySort(query, normalizedSort);

        var items = await sortedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new VehicleDefectDto
            {
                Id = x.Id,
                OrganizationId = x.OrganizationId,
                VehicleId = x.VehicleId,
                VehicleDisplayName = Helpers.VehicleDisplayName.Format(
                    x.Vehicle.BrandId.HasValue ? (x.Vehicle.Brand != null ? x.Vehicle.Brand.Name : string.Empty) : (x.Vehicle.ManualBrandName ?? string.Empty),
                    x.Vehicle.ModelId.HasValue ? (x.Vehicle.Model != null ? x.Vehicle.Model.Name : string.Empty) : (x.Vehicle.ManualModelName ?? string.Empty), x.Vehicle.RegistrationPlate),
                VehicleSubtitle = VehicleDefectRules.BuildVehicleSubtitle(
                    x.Vehicle.Name,
                    x.Vehicle.YearIssue,
                    x.Vehicle.OrganizationClient != null ? x.Vehicle.OrganizationClient.Name : null),
                Title = x.Title,
                Description = x.Description,
                SeverityCode = x.SeverityCode,
                SeverityName = VehicleDefectSeverityCodes.GetName(x.SeverityCode),
                StatusCode = x.StatusCode,
                StatusName = VehicleDefectStatusCodes.GetName(x.StatusCode),
                IsClosed = VehicleDefectStatusCodes.IsClosed(x.StatusCode),
                LinkedMaintenanceCount = x.MaintenanceLinks.Count(link => !link.Maintenance.IsDeleted),
                LinkedMaintenance = x.MaintenanceLinks
                    .Where(link => !link.Maintenance.IsDeleted)
                    .OrderByDescending(link => link.CreatedAt)
                    .Select(link => new VehicleDefectLinkedMaintenanceDto
                    {
                        Id = link.MaintenanceId,
                        Date = link.Maintenance.Date,
                        PlannedDate = link.Maintenance.PlannedDate,
                        CompletedDate = link.Maintenance.CompletedDate,
                        Status = (int)link.Maintenance.Status,
                        TypeName = link.Maintenance.Type == MaintenanceType.Maintenance
                            ? "Обслуживание"
                            : link.Maintenance.Type == MaintenanceType.Repair
                                ? "Ремонт"
                                : link.Maintenance.Type == MaintenanceType.Refill
                                    ? "Заправка"
                                    : "Другое",
                        Comment = link.Maintenance.Comment,
                        Mileage = link.Maintenance.Mileage
                    })
                    .FirstOrDefault(),
                LinkedMaintenances = x.MaintenanceLinks
                    .Where(link => !link.Maintenance.IsDeleted)
                    .OrderByDescending(link => link.CreatedAt)
                    .Select(link => new VehicleDefectLinkedMaintenanceDto
                    {
                        Id = link.MaintenanceId,
                        Date = link.Maintenance.Date,
                        PlannedDate = link.Maintenance.PlannedDate,
                        CompletedDate = link.Maintenance.CompletedDate,
                        Status = (int)link.Maintenance.Status,
                        TypeName = link.Maintenance.Type == MaintenanceType.Maintenance
                            ? "Обслуживание"
                            : link.Maintenance.Type == MaintenanceType.Repair
                                ? "Ремонт"
                                : link.Maintenance.Type == MaintenanceType.Refill
                                    ? "Заправка"
                                    : "Другое",
                        Comment = link.Maintenance.Comment,
                        Mileage = link.Maintenance.Mileage
                    })
                    .ToList(),
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                ClosedAt = x.ClosedAt
            })
            .ToListAsync();

        await HydrateLinkedMaintenancePricesAsync(items);

        return new VehicleDefectListResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            AllCount = allCount,
            OpenCount = openCount,
            ResolvedCount = resolvedCount,
            HasMore = page * pageSize < totalCount,
            SelectedVehicleId = vehicleId
        };
    }

    /// <summary> Возвращает дефект по идентификатору. </summary>
    internal async Task<VehicleDefectDto?> GetByIdAsync(string userId, Guid organizationId, Guid defectId)
    {
        await VehicleDefectRules.EnsureDefectsModuleEnabledAsync(organizationSubscriptionService, userId, organizationId);

        var defect = await context.VehicleDefects
            .AsNoTracking()
            .Where(x => x.Id == defectId && x.OrganizationId == organizationId && !x.IsDeleted)
            .Select(x => new VehicleDefectDto
            {
                Id = x.Id,
                OrganizationId = x.OrganizationId,
                VehicleId = x.VehicleId,
                VehicleDisplayName = Helpers.VehicleDisplayName.Format(
                    x.Vehicle.BrandId.HasValue ? (x.Vehicle.Brand != null ? x.Vehicle.Brand.Name : string.Empty) : (x.Vehicle.ManualBrandName ?? string.Empty),
                    x.Vehicle.ModelId.HasValue ? (x.Vehicle.Model != null ? x.Vehicle.Model.Name : string.Empty) : (x.Vehicle.ManualModelName ?? string.Empty), x.Vehicle.RegistrationPlate),
                VehicleSubtitle = VehicleDefectRules.BuildVehicleSubtitle(
                    x.Vehicle.Name,
                    x.Vehicle.YearIssue,
                    x.Vehicle.OrganizationClient != null ? x.Vehicle.OrganizationClient.Name : null),
                Title = x.Title,
                Description = x.Description,
                SeverityCode = x.SeverityCode,
                SeverityName = VehicleDefectSeverityCodes.GetName(x.SeverityCode),
                StatusCode = x.StatusCode,
                StatusName = VehicleDefectStatusCodes.GetName(x.StatusCode),
                IsClosed = VehicleDefectStatusCodes.IsClosed(x.StatusCode),
                LinkedMaintenanceCount = x.MaintenanceLinks.Count(link => !link.Maintenance.IsDeleted),
                LinkedMaintenance = x.MaintenanceLinks
                    .Where(link => !link.Maintenance.IsDeleted)
                    .OrderByDescending(link => link.CreatedAt)
                    .Select(link => new VehicleDefectLinkedMaintenanceDto
                    {
                        Id = link.MaintenanceId,
                        Date = link.Maintenance.Date,
                        PlannedDate = link.Maintenance.PlannedDate,
                        CompletedDate = link.Maintenance.CompletedDate,
                        Status = (int)link.Maintenance.Status,
                        TypeName = link.Maintenance.Type == MaintenanceType.Maintenance
                            ? "Обслуживание"
                            : link.Maintenance.Type == MaintenanceType.Repair
                                ? "Ремонт"
                                : link.Maintenance.Type == MaintenanceType.Refill
                                    ? "Заправка"
                                    : "Другое",
                        Comment = link.Maintenance.Comment,
                        Mileage = link.Maintenance.Mileage
                    })
                    .FirstOrDefault(),
                LinkedMaintenances = x.MaintenanceLinks
                    .Where(link => !link.Maintenance.IsDeleted)
                    .OrderByDescending(link => link.CreatedAt)
                    .Select(link => new VehicleDefectLinkedMaintenanceDto
                    {
                        Id = link.MaintenanceId,
                        Date = link.Maintenance.Date,
                        PlannedDate = link.Maintenance.PlannedDate,
                        CompletedDate = link.Maintenance.CompletedDate,
                        Status = (int)link.Maintenance.Status,
                        TypeName = link.Maintenance.Type == MaintenanceType.Maintenance
                            ? "Обслуживание"
                            : link.Maintenance.Type == MaintenanceType.Repair
                                ? "Ремонт"
                                : link.Maintenance.Type == MaintenanceType.Refill
                                    ? "Заправка"
                                    : "Другое",
                        Comment = link.Maintenance.Comment,
                        Mileage = link.Maintenance.Mileage
                    })
                    .ToList(),
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                ClosedAt = x.ClosedAt
            })
            .FirstOrDefaultAsync();

        if (defect != null)
        {
            await HydrateLinkedMaintenancePricesAsync([defect]);
        }

        return defect;
    }

    /// <summary> Возвращает историю изменений дефекта. </summary>
    internal async Task<IReadOnlyList<VehicleDefectHistoryItemDto>> GetHistoryAsync(string userId, Guid organizationId, Guid defectId)
    {
        await VehicleDefectRules.EnsureDefectsModuleEnabledAsync(organizationSubscriptionService, userId, organizationId);

        return await context.VehicleHistoryEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.RelatedEntityType == "defect"
                && x.RelatedEntityId == defectId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new VehicleDefectHistoryItemDto
            {
                CreatedAt = x.CreatedAt,
                Title = x.Title,
                Description = x.Description,
                ActorDisplayName = x.ActorDisplayName ?? x.User.Email ?? x.User.UserName
            })
            .ToListAsync();
    }

    /// <summary> Возвращает варианты обслуживания для привязки к дефекту. </summary>
    internal async Task<IReadOnlyList<VehicleDefectMaintenanceOptionDto>> GetMaintenanceOptionsAsync(string userId, Guid organizationId, Guid defectId)
    {
        await VehicleDefectRules.EnsureDefectsModuleEnabledAsync(organizationSubscriptionService, userId, organizationId);

        var defect = await GetByIdAsync(userId, organizationId, defectId)
            ?? throw new InvalidOperationException("Неисправность не найдена.");

        var linkedIds = defect.LinkedMaintenances.Select(x => x.Id).ToList();
        var rows = await context.Maintenance
            .AsNoTracking()
            .Where(x => x.VehicleId == defect.VehicleId
                && x.Vehicle.OrganizationId == organizationId
                && !x.IsDeleted
                && !linkedIds.Contains(x.Id))
            .OrderByDescending(x => x.Date)
            .Take(50)
            .Select(x => new
            {
                x.Id,
                x.Type,
                x.Status,
                x.Date,
                x.PlannedDate,
                x.CompletedDate,
                x.Comment
            })
            .ToListAsync();

        return rows.Select(x => new VehicleDefectMaintenanceOptionDto
            {
                Id = x.Id,
                Text = $"{VehicleDefectRules.GetMaintenanceTypeName(x.Type)} от {VehicleDefectRules.GetMaintenanceDisplayDate(x.Status, x.Date, x.PlannedDate, x.CompletedDate):dd.MM.yyyy} · {VehicleDefectRules.GetMaintenanceStatusName(x.Status)}"
                    + (string.IsNullOrWhiteSpace(x.Comment) ? string.Empty : $" · {x.Comment}")
            })
            .ToList();
    }

    /// <summary> Возвращает варианты дефектов для привязки к обслуживанию. </summary>
    internal async Task<IReadOnlyList<VehicleDefectOptionDto>> GetOptionsForMaintenanceAsync(string userId, Guid organizationId, Guid maintenanceId)
    {
        await VehicleDefectRules.EnsureDefectsModuleEnabledAsync(organizationSubscriptionService, userId, organizationId);

        var maintenance = await context.Maintenance
            .AsNoTracking()
            .Where(x => x.Id == maintenanceId
                && !x.IsDeleted
                && !x.Vehicle.IsDeleted
                && x.Vehicle.OrganizationId == organizationId)
            .Select(x => new
            {
                x.VehicleId,
                LinkedDefectIds = x.LinkedDefects
                    .Where(link => !link.VehicleDefect.IsDeleted)
                    .Select(link => link.VehicleDefectId)
                    .ToList()
            })
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Обслуживание не найдено.");

        return await context.VehicleDefects
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.VehicleId == maintenance.VehicleId
                && !x.IsDeleted
                && (x.StatusCode != VehicleDefectStatusCodes.Resolved || maintenance.LinkedDefectIds.Contains(x.Id)))
            .OrderBy(x => x.StatusCode == VehicleDefectStatusCodes.Resolved)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => new VehicleDefectOptionDto
            {
                Id = x.Id,
                Title = x.Title,
                Description = x.Description,
                SeverityCode = x.SeverityCode,
                StatusCode = x.StatusCode,
                IsLinked = maintenance.LinkedDefectIds.Contains(x.Id)
            })
            .ToListAsync();
    }

    /// <summary> Заполняет цены связанных обслуживаний у дефектов. </summary>
    private async Task HydrateLinkedMaintenancePricesAsync(IReadOnlyCollection<VehicleDefectDto> defects)
    {
        var maintenanceIds = defects
            .SelectMany(defect => defect.LinkedMaintenances.Select(maintenance => maintenance.Id)
                .Concat(defect.LinkedMaintenance != null ? [defect.LinkedMaintenance.Id] : []))
            .Distinct()
            .ToList();

        if (maintenanceIds.Count == 0)
        {
            return;
        }

        var prices = await context.MaintenanceDetails
            .AsNoTracking()
            .Where(detail => maintenanceIds.Contains(detail.MaintenanceId))
            .GroupBy(detail => detail.MaintenanceId)
            .Select(details => new { MaintenanceId = details.Key, Price = details.Sum(detail => detail.Price ?? 0) })
            .ToListAsync();

        var priceByMaintenanceId = prices.ToDictionary(item => item.MaintenanceId, item => item.Price);

        foreach (var defect in defects)
        {
            VehicleDefectRules.ApplyPrice(defect.LinkedMaintenance, priceByMaintenanceId);

            foreach (var maintenance in defect.LinkedMaintenances)
            {
                VehicleDefectRules.ApplyPrice(maintenance, priceByMaintenanceId);
            }
        }
    }

}
