using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Проверяет допустимость изменений при выполняемых работах. </summary>
internal sealed class MaintenanceWorkGuard(
    OpenCRMContext context)
{
    internal static void EnsurePricedServicesForWorkspaceCompletion(MaintenanceEntity maintenance)
    {
        var services = maintenance.Details
            .Where(detail => !detail.MaterialCatalogItemId.HasValue
                && string.IsNullOrWhiteSpace(detail.MaterialName)
                && !detail.MaterialQuantity.HasValue)
            .ToList();
        if (services.Count == 0)
        {
            throw new InvalidOperationException("Добавьте хотя бы одну выполненную работу перед завершением обслуживания.");
        }

        if (services.Any(detail => !detail.Price.HasValue))
        {
            throw new InvalidOperationException("Перед завершением укажите цену каждой работы. Для бесплатной услуги укажите 0.");
        }
    }

    /// <summary> Проверяет завершение отслеживаемых работ перед изменениями. </summary>
    internal async Task EnsureTrackedWorkCompletedAsync(MaintenanceEntity maintenance, bool allowUntrackedAssignedWork = false)
    {
        if (await context.MaintenanceWorkReports.AsNoTracking()
            .AnyAsync(report => report.MaintenanceId == maintenance.Id
                && report.Kind == "extra" && report.Decision == null))
        {
            throw new InvalidOperationException("Рассмотрите запросы дополнительных работ до проверки и выдачи техники.");
        }

        var statuses = await context.MaintenanceWorkOperations.AsNoTracking()
            .Where(operation => operation.MaintenanceId == maintenance.Id)
            .ToDictionaryAsync(operation => operation.Id, operation => operation.Status);
        var approvedDetailIds = await context.MaintenanceWorkReports.AsNoTracking()
            .Where(report => report.MaintenanceId == maintenance.Id
                && report.Kind == "extra" && report.Decision == "approved")
            .Select(report => report.ApprovedDetailId)
            .ToListAsync();
        var serviceIds = maintenance.Details
            .Where(detail => detail.MaterialCatalogItemId == null && string.IsNullOrWhiteSpace(detail.MaterialName))
            .Select(detail => detail.DetailId)
            .ToArray();
        var hasAssignedWork = maintenance.Details.Any(detail =>
            detail.MaterialCatalogItemId == null && string.IsNullOrWhiteSpace(detail.MaterialName)
            && (detail.EmployeeId.HasValue || maintenance.EmployeeId.HasValue));
        if (serviceIds.Length == 0)
        {
            return;
        }

        if (statuses.Count == 0 && approvedDetailIds.Count == 0 && (!hasAssignedWork || allowUntrackedAssignedWork))
        {
            var workflow = await (from vehicle in context.Vehicles.AsNoTracking()
                join organization in context.Organizations.AsNoTracking()
                    on vehicle.OrganizationId equals organization.Id
                where vehicle.Id == maintenance.VehicleId
                select new { organization.MaintenanceWorkflowMode, organization.PrimaryScenarioCode })
                .SingleAsync();
            if (workflow.MaintenanceWorkflowMode == "simple" || workflow.PrimaryScenarioCode == "detailing")
            {
                return;
            }
        }

        if (serviceIds.Any(detailId => !statuses.TryGetValue(detailId, out var status)
            || status != MaintenanceWorkStatus.Completed))
        {
            throw new InvalidOperationException("Все работы должны быть завершены до проверки и выдачи техники.");
        }
    }

    /// <summary> Проверяет, что состояние работ допускает смену статуса. </summary>
    internal async Task EnsureWorkAllowsStatusChangeAsync(MaintenanceEntity maintenance, MaintenanceStatus nextStatus)
    {
        if (nextStatus == maintenance.Status || nextStatus is not (MaintenanceStatus.Planned or MaintenanceStatus.Cancelled))
        {
            return;
        }

        if (await context.MaintenanceWorkOperations.AsNoTracking()
            .AnyAsync(operation => operation.MaintenanceId == maintenance.Id))
        {
            throw new InvalidOperationException("Начатую работу нельзя отменить или вернуть в запланированные.");
        }
    }

    /// <summary> Проверяет корректность изменений при начатых работах. </summary>
    internal async Task ValidateStartedWorkAsync(
        MaintenanceEntity maintenance,
        IReadOnlyList<MaintenanceDetail> nextDetails,
        Guid? nextMaintenanceEmployeeId)
    {
        var startedIds = await context.MaintenanceWorkOperations.AsNoTracking()
            .Where(operation => operation.MaintenanceId == maintenance.Id)
            .Select(operation => operation.Id)
            .ToListAsync();
        var approvedIds = await context.MaintenanceWorkReports.AsNoTracking()
            .Where(report => report.MaintenanceId == maintenance.Id
                && report.Kind == "extra" && report.Decision == "approved")
            .Select(report => report.ApprovedDetailId)
            .ToListAsync();
        foreach (var protectedId in startedIds.Concat(approvedIds.OfType<Guid>()).Distinct())
        {
            var previous = maintenance.Details.Single(detail => detail.DetailId == protectedId);
            var next = nextDetails.SingleOrDefault(detail => detail.DetailId == protectedId);
            if (next == null
                || next.CatalogItemId != previous.CatalogItemId
                || next.EmployeeId != previous.EmployeeId
                || next.Name != previous.Name
                || next.Quantity != previous.Quantity
                || approvedIds.Contains(protectedId) && next.Price != previous.Price
                || !next.EmployeeId.HasValue && nextMaintenanceEmployeeId != maintenance.EmployeeId)
            {
                throw new InvalidOperationException(
                    "Начатую или согласованную работу нельзя удалить или изменить её состав и исполнителя.");
            }
        }
    }
}
