using Helpers;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Ограничивает назначения сотрудников возможностью команды в тарифе. </summary>
internal sealed class MaintenancePlanAccess(IOrganizationSubscriptionService subscriptions)
{
    internal async Task EnsureCanCreateAsync(
        string userId,
        Guid? organizationId,
        Guid? employeeId,
        IReadOnlyCollection<MaintenanceDetail> details)
    {
        if (!await this.IsTeamRestrictedAsync(userId, organizationId))
        {
            return;
        }

        if (employeeId.HasValue || details.Any(detail => detail.EmployeeId.HasValue))
        {
            throw new InvalidOperationException("Назначение сотрудников недоступно на текущем тарифе.");
        }
    }

    internal async Task<Guid?> PreserveRestrictedTeamFieldsAsync(
        string userId,
        Guid? organizationId,
        MaintenanceEntity maintenance,
        List<MaintenanceDetail> details,
        Guid? employeeId)
    {
        if (!await this.IsTeamRestrictedAsync(userId, organizationId))
        {
            return employeeId;
        }

        if (employeeId.HasValue && employeeId != maintenance.EmployeeId)
        {
            throw new InvalidOperationException("Назначение сотрудников недоступно на текущем тарифе.");
        }

        var previousById = maintenance.Details.ToDictionary(detail => detail.DetailId);

        foreach (var detail in details)
        {
            previousById.TryGetValue(detail.DetailId, out var previous);
            if (detail.EmployeeId.HasValue && detail.EmployeeId != previous?.EmployeeId)
            {
                throw new InvalidOperationException("Назначение сотрудников недоступно на текущем тарифе.");
            }

            if (previous?.EmployeeId is Guid previousEmployeeId)
            {
                detail.EmployeeId = previousEmployeeId;
                detail.EmployeeName = previous.EmployeeName;
            }
        }

        return maintenance.EmployeeId;
    }

    internal async Task EnsureCanAssignEmployeeAsync(
        string userId,
        Guid organizationId,
        Guid? currentEmployeeId,
        Guid? requestedEmployeeId)
    {
        if (await this.IsTeamRestrictedAsync(userId, organizationId)
            && requestedEmployeeId != currentEmployeeId)
        {
            throw new InvalidOperationException("Назначение сотрудников недоступно на текущем тарифе.");
        }
    }

    private async Task<bool> IsTeamRestrictedAsync(string userId, Guid? organizationId)
    {
        return organizationId.HasValue
            && !await subscriptions.HasFeatureAsync(userId, organizationId.Value, FeatureCodes.MembersModule);
    }
}
