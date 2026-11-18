using Microsoft.EntityFrameworkCore;
using Helpers;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Workspace.Queries;

/// <summary> Сервис журнала обслуживания организации. </summary>
public sealed class OrganizationMaintenanceJournalQueryService(OpenCRMContext opencrmContext) : IOrganizationMaintenanceJournalQueryService
{
    /// <summary> Возвращает журнал обслуживания организации с фильтрами и пагинацией. </summary>
    public async Task<OrganizationMaintenanceJournalResult> GetMaintenanceJournalAsync(
        Guid organizationId,
        MaintenanceStatus? status,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? search,
        int page,
        int pageSize,
        bool canViewClients,
        CancellationToken cancellationToken = default,
        string? handover = null,
        Guid? employeeId = null,
        bool canViewPayments = false)
    {
        if (handover is not (null or "ready" or "handedOver"))
        {
            throw new ArgumentException("Неизвестный фильтр выдачи.", nameof(handover));
        }

        if (status.HasValue && !Enum.IsDefined(status.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (dateFrom > dateTo || dateTo == DateOnly.MaxValue)
        {
            throw new ArgumentException("Проверьте период журнала.");
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = opencrmContext.Maintenance.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            // Ищем названия работ до пагинации, чтобы поиск охватывал весь журнал.
            query = opencrmContext.Maintenance.FromSqlInterpolated($"""
                SELECT m.*, m.xmin FROM "Maintenance" m
                JOIN "Vehicles" v ON v."Id" = m."VehicleId"
                LEFT JOIN "Brands" b ON b."Id" = v."BrandId"
                LEFT JOIN "Models" model ON model."Id" = v."ModelId"
                LEFT JOIN "OrganizationClients" c ON c."Id" = v."OrganizationClientId"
                WHERE concat_ws(' ', COALESCE(b."Name", v."ManualBrandName"),
                    COALESCE(model."Name", v."ManualModelName"), v."RegistrationPlate",
                    CASE WHEN {canViewClients} THEN c."Name" END,
                    CASE WHEN {canViewClients} AND m."BuyerClientId" IS NOT NULL THEN m."BuyerName" END) ILIKE {pattern}
                OR EXISTS (
                    SELECT 1 FROM "MaintenanceDetails" detail
                    WHERE detail."MaintenanceId" = m."Id"
                      AND detail."MaterialCatalogItemId" IS NULL
                      AND NULLIF(btrim(detail."MaterialName"), '') IS NULL
                      AND detail."MaterialQuantity" IS NULL
                      AND detail."Name" ILIKE {pattern})
                """);
        }

        query = query.AsNoTracking()
            .Where(item => item.Vehicle.OrganizationId == organizationId && !item.IsDeleted && !item.Vehicle.IsDeleted);
        if (employeeId.HasValue)
        {
            var selectedEmployeeId = employeeId.Value;
            var employeeBelongsToOrganization = await opencrmContext.OrganizationEmployees
                .AsNoTracking()
                .AnyAsync(employee => employee.Id == selectedEmployeeId && employee.OrganizationId == organizationId, cancellationToken);

            query = query.Where(item => employeeBelongsToOrganization
                && (item.EmployeeId == selectedEmployeeId || item.Details.Any(detail => detail.EmployeeId == selectedEmployeeId)));
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        if (handover == "ready")
        {
            query = query.Where(item => item.Status == MaintenanceStatus.Completed && item.ReadyForHandoverAt != null && item.HandedOverAt == null);
        }

        if (handover == "handedOver")
        {
            query = query.Where(item => item.Status == MaintenanceStatus.Completed && item.HandedOverAt != null);
        }

        if (dateFrom.HasValue)
        {
            var from = dateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(item => (item.Status == MaintenanceStatus.Completed
                ? item.CompletedDate ?? item.Date : item.PlannedDate ?? item.Date) >= from);
        }

        if (dateTo.HasValue)
        {
            var toExclusive = dateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(item => (item.Status == MaintenanceStatus.Completed
                ? item.CompletedDate ?? item.Date : item.PlannedDate ?? item.Date) < toExclusive);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize)));
        var rows = await query
            .OrderByDescending(item => item.Status == MaintenanceStatus.Completed
                ? item.CompletedDate ?? item.Date : item.PlannedDate ?? item.Date)
            .ThenByDescending(item => item.Status == MaintenanceStatus.Completed ? null : item.PlannedStartTime)
            .ThenByDescending(item => item.DateTimeCreation)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(item => item.Vehicle).ThenInclude(vehicle => vehicle.Brand)
            .Include(item => item.Vehicle).ThenInclude(vehicle => vehicle.Model)
            .Include(item => item.Vehicle).ThenInclude(vehicle => vehicle.OrganizationClient)
            .ToListAsync(cancellationToken);
        var performers = await opencrmContext.OrganizationEmployees
            .AsNoTracking()
            .Where(employee => employee.OrganizationId == organizationId)
            .OrderBy(employee => employee.Name)
            .ThenBy(employee => employee.Id)
            .Select(employee => new OrganizationMaintenanceJournalPerformerDto
            {
                Id = employee.Id,
                Name = employee.Name
            })
            .ToListAsync(cancellationToken);
        var performerNamesById = performers.ToDictionary(performer => performer.Id, performer => performer.Name);

        return new OrganizationMaintenanceJournalResult
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Performers = performers,
            Items = rows.Select(item =>
            {
                var serviceDetails = item.Details
                    .Where(detail => !detail.MaterialCatalogItemId.HasValue && string.IsNullOrWhiteSpace(detail.MaterialName)
                        && !detail.MaterialQuantity.HasValue && !string.IsNullOrWhiteSpace(detail.Name))
                    .OrderBy(detail => detail.Position)
                    .ToList();
                var performerNames = serviceDetails
                    .Select(detail => !string.IsNullOrWhiteSpace(detail.EmployeeName)
                        ? detail.EmployeeName
                        : detail.EmployeeId.HasValue && performerNamesById.TryGetValue(detail.EmployeeId.Value, out var employeeName)
                            ? employeeName
                            : null)
                    .Append(!string.IsNullOrWhiteSpace(item.Performer)
                        ? item.Performer
                        : item.EmployeeId.HasValue && performerNamesById.TryGetValue(item.EmployeeId.Value, out var generalEmployeeName)
                            ? generalEmployeeName
                            : null)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var isCancelled = item.Status == MaintenanceStatus.Cancelled;
                var amount = isCancelled ? 0m : Math.Max(0m, OrganizationFinanceMapper.RoundMoney(
                    item.Details.Sum(detail => (decimal)(detail.Price ?? 0)))
                    - OrganizationFinanceMapper.RoundMoney(item.DiscountAmount));
                var paymentStatus = canViewPayments
                    ? MaintenancePaymentStatus.Resolve(
                        item.PaidAmount,
                        amount,
                        isCancelled || item.Details.Any(detail => detail.Price.HasValue),
                        isCancelled || item.Details.All(detail => detail.Price.HasValue))
                    : null;

                return new OrganizationMaintenanceJournalItemDto
                {
                    Id = item.Id,
                    VehicleDisplayName = VehicleDisplayName.Format(
                        item.Vehicle.Brand?.Name ?? item.Vehicle.ManualBrandName,
                        item.Vehicle.Model?.Name ?? item.Vehicle.ManualModelName, item.Vehicle.RegistrationPlate),
                    RegistrationPlate = item.Vehicle.RegistrationPlate,
                    ClientName = canViewClients && item.BuyerClientId.HasValue ? item.BuyerName : null,
                    Services = serviceDetails.Select(detail => detail.Name).ToList(),
                    ServiceLines = serviceDetails.Select(detail => new OrganizationMaintenanceJournalServiceLineDto
                    {
                        Name = detail.Name,
                        EmployeeName = !string.IsNullOrWhiteSpace(detail.EmployeeName)
                            ? detail.EmployeeName
                            : detail.EmployeeId.HasValue && performerNamesById.TryGetValue(detail.EmployeeId.Value, out var employeeName)
                                ? employeeName
                                : null,
                        Amount = detail.Price.HasValue
                            ? OrganizationFinanceMapper.RoundMoney((decimal)detail.Price.Value)
                            : null
                    }).ToList(),
                    PerformerNames = performerNames,
                    Status = item.Status,
                    StatusName = item.HandedOverAt.HasValue ? "Выдан" : item.ReadyForHandoverAt.HasValue ? "Готов к выдаче" : OrganizationWorkspaceMaintenanceMapper.GetMaintenanceStatusName(item.Status),
                    PlannedDate = item.PlannedDate,
                    PlannedStartTime = item.PlannedStartTime,
                    CompletedDate = item.Status == MaintenanceStatus.Completed ? item.CompletedDate : null,
                    Date = item.Status == MaintenanceStatus.Completed
                        ? item.CompletedDate ?? item.Date
                        : item.PlannedDate ?? item.Date,
                    WorkActNumber = item.WorkActNumber,
                    CurrencyCode = item.CurrencyCode,
                    Amount = amount,
                    PaidAmount = canViewPayments ? item.PaidAmount : null,
                    PaymentStatus = paymentStatus
                };
            }).ToList()
        };
    }
}
