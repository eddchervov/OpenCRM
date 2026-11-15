using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Определяет сотрудников для записей обслуживания. </summary>
internal sealed class MaintenanceEmployeeResolver(
    OpenCRMContext context)
{
    /// <summary> Возвращает имя сотрудника организации по идентификатору. </summary>
    internal async Task<string?> ResolveEmployeeNameAsync(Guid organizationId, Guid? employeeId, Guid? previousId = null, string? previousName = null)
    {
        if (!employeeId.HasValue)
        {
            return null;
        }
        var employee = await context.OrganizationEmployees.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == employeeId.Value && item.OrganizationId == organizationId);
        if (employee == null || (!employee.IsActive && previousId != employeeId.Value))
        {
            throw new InvalidOperationException("Сотрудник недоступен. Выберите действующего сотрудника этой организации.");
        }

        return previousId == employeeId.Value && previousName != null ? previousName : employee.Name;
    }

    /// <summary> Проверяет сотрудников, назначенных на строки работ. </summary>
    internal async Task ValidateDetailEmployeesAsync(Guid? organizationId, IReadOnlyList<MaintenanceDetail> details, IReadOnlyList<MaintenanceDetail>? previousDetails)
    {
        var previousRows = previousDetails?.ToList() ?? [];
        foreach (var detail in details)
        {
            if (!organizationId.HasValue || MaintenanceDetailRules.IsMaterialDetail(detail))
            {
                detail.EmployeeId = null;
                detail.EmployeeName = null;
                continue;
            }
            var previous = previousRows.FirstOrDefault(item => item.DetailId == detail.DetailId && item.EmployeeId == detail.EmployeeId)
                ?? previousRows.FirstOrDefault(item => item.CatalogItemId == detail.CatalogItemId && item.EmployeeId == detail.EmployeeId);
            detail.EmployeeName = await this.ResolveEmployeeNameAsync(organizationId.Value, detail.EmployeeId, previous?.EmployeeId, previous?.EmployeeName);
            if (previous != null)
            {
                previousRows.Remove(previous);
            }
        }
    }
}
