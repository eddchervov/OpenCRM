using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Clients.Rules;

namespace OpenCRM.Modules.Clients.Queries;

/// <summary> Читатель вспомогательных данных о клиентах организации. </summary>
internal sealed class ClientReadData(OpenCRMContext context)
{
    /// <summary> Контекст базы данных для чтения данных клиентов. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Загружает варианты тегов клиентов организации. </summary>
    internal async Task<IReadOnlyList<OrganizationClientTagDto>> LoadTagOptionsAsync(Guid organizationId)
    {
        return await this._context.OrganizationClientTags
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Name)
            .Select(x => new OrganizationClientTagDto
            {
                Id = x.Id,
                Name = x.Name
            })
            .ToListAsync();
    }

    /// <summary> Загружает теги по каждому клиенту. </summary>
    internal async Task<IReadOnlyDictionary<Guid, IReadOnlyList<OrganizationClientTagDto>>> LoadTagsByClientAsync(IReadOnlyCollection<Guid> clientIds)
    {
        if (clientIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<OrganizationClientTagDto>>();
        }

        var rows = await this._context.OrganizationClientTagAssignments
            .AsNoTracking()
            .Where(x => clientIds.Contains(x.OrganizationClientId))
            .Select(x => new
            {
                x.OrganizationClientId,
                x.OrganizationClientTag.Id,
                x.OrganizationClientTag.Name
            })
            .OrderBy(x => x.Name)
            .ToListAsync();

        return rows
            .GroupBy(x => x.OrganizationClientId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<OrganizationClientTagDto>)x.Select(tag => new OrganizationClientTagDto
                {
                    Id = tag.Id,
                    Name = tag.Name
                }).ToList());
    }

    /// <summary> Загружает отображаемые имена ответственных пользователей. </summary>
    internal async Task<IReadOnlyDictionary<string, string>> LoadResponsibleDisplayNamesAsync(
        Guid organizationId,
        IEnumerable<string?> responsibleUserIds)
    {
        var ids = responsibleUserIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var users = await this._context.Users
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Email,
                x.UserName,
                x.OriginalEmail,
                x.IsDeleted,
                EmployeeName = this._context.OrganizationEmployees
                    .Where(employee => employee.OrganizationId == organizationId
                        && employee.MemberId.HasValue && employee.Member!.UserId == x.Id)
                    .Select(employee => employee.Name)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return users.ToDictionary(
            x => x.Id,
            x => ClientRules.ResolveActorName(x.IsDeleted ? null : x.EmployeeName, x.Email, x.UserName, x.OriginalEmail, x.IsDeleted) ?? x.Id,
            StringComparer.Ordinal);
    }
}
