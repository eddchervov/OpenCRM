using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Rules;

namespace OpenCRM.Modules.Clients.Commands;

/// <summary> Сервис определения ответственного за клиента. </summary>
internal sealed class OrganizationClientResponsibleResolver(OpenCRMContext context)
{
    /// <summary> Определяет ответственного пользователя за клиента. </summary>
    internal async Task<string?> ResolveAsync(Guid organizationId, string? responsibleUserId)
    {
        var normalized = ClientRules.NormalizeNullable(responsibleUserId);
        if (normalized == null)
        {
            return null;
        }

        var exists = await context.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(member => member.OrganizationId == organizationId && member.UserId == normalized);

        return exists
            ? normalized
            : throw new InvalidOperationException("Ответственный сотрудник не найден в организации.");
    }
}
