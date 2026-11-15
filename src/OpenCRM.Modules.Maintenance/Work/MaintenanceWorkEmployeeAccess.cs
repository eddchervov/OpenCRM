using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Maintenance.Work;

/// <summary> Доступ к данным сотрудника организации для работ обслуживания. </summary>
internal sealed class MaintenanceWorkEmployeeAccess(OpenCRMContext context)
{
    /// <summary> Возвращает сотрудника организации по идентификатору пользователя. </summary>
    public async Task<OrganizationEmployee> GetEmployeeAsync(string userId, Guid organizationId)
    {
        var memberId = await context.OrganizationMembers.AsNoTracking()
            .Where(member => member.OrganizationId == organizationId && member.UserId == userId)
            .Select(member => (Guid?)member.Id)
            .SingleOrDefaultAsync();
        if (!memberId.HasValue)
        {
            throw new UnauthorizedAccessException("Участник организации не найден.");
        }

        return await context.OrganizationEmployees.AsNoTracking()
            .SingleOrDefaultAsync(employee => employee.OrganizationId == organizationId
                && employee.MemberId == memberId && employee.IsActive)
            ?? throw new UnauthorizedAccessException("Аккаунт не связан с сотрудником.");
    }
}