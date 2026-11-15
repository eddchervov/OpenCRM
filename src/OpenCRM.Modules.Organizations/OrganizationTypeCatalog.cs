using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations;

/// <summary> Чтение поддерживаемых типов организаций. </summary>
internal sealed class OrganizationTypeCatalog(OpenCRMContext context) : IOrganizationTypeCatalog
{
    public async Task<IReadOnlyList<OrganizationTypeOptionDto>> GetOrganizationTypeOptionsAsync()
    {
        // HIDDEN_FLEET: скрыт только выбор новой организации; возврат согласовать с NormalizeAsync. См. docs/hidden-fleet-scenario.md.
        return await context.OrganizationTypeDefinitions
            .AsNoTracking()
            .Where(x => x.IsSupported && x.IsActive && x.Code != OrganizationTypeCodes.Fleet)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new OrganizationTypeOptionDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Code == OrganizationTypeCodes.ServiceStation ? "Автосервис" : x.Name,
                Description = x.Description,
                IsActive = x.IsActive,
                SortOrder = x.SortOrder
            })
            .ToListAsync();
    }
}
