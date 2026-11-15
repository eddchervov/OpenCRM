using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Catalog;

/// <summary> Сервис запросов каталога организации. </summary>
internal sealed class CatalogQueryService(
    OpenCRMContext context,
    IOrganizationSubscriptionService organizationSubscriptionService,
    ICatalogInitializationService initialization) : ICatalogQueryService
{
    /// <summary> Возвращает активную услугу каталога организации. </summary>
    public async Task<OrganizationScenarioCatalogItemDto> GetActiveServiceAsync(string userId, Guid organizationId, Guid serviceId)
    {
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceCreate);
        var item = await context.OrganizationScenarioCatalogItems.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == serviceId
                && value.OrganizationId == organizationId
                && value.KindCode == OrganizationScenarioCatalogKindCodes.Service
                && value.IsActive)
            ?? throw new InvalidOperationException("Выберите действующую услугу из справочника этой организации.");

        return CatalogItemMapper.Map(item, item.HasBeenUsed);
    }

    /// <summary> Возвращает незавершённые записи, назначенные на рабочий пост. </summary>
    public async Task<WorkPostAffectedMaintenancesDto> GetAffectedWorkPostMaintenancesAsync(
        string userId,
        Guid organizationId,
        Guid workPostId)
    {
        await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceView);

        var workPostName = await context.OrganizationScenarioCatalogItems
            .AsNoTracking()
            .Where(item => item.Id == workPostId && item.OrganizationId == organizationId
                && item.KindCode == OrganizationScenarioCatalogKindCodes.WorkPost && item.HasBeenUsed)
            .Select(item => item.Name)
            .SingleOrDefaultAsync() ?? throw new KeyNotFoundException("Используемый пост не найден.");
        var normalizedName = workPostName.Trim().ToLower();
        var affectedMaintenances = context.Maintenance
            .AsNoTracking()
            .Where(item => item.Vehicle.OrganizationId == organizationId && !item.IsDeleted
                && item.Status != MaintenanceStatus.Completed && item.Status != MaintenanceStatus.Cancelled
                && item.Location != null && item.Location.Trim().ToLower() == normalizedName);
        var totalCount = await affectedMaintenances.CountAsync();
        var items = await affectedMaintenances
            .OrderBy(item => item.PlannedDate ?? item.Date)
            .ThenBy(item => item.PlannedStartTime)
            .ThenBy(item => item.Id)
            .Take(20)
            .Select(item => new WorkPostAffectedMaintenanceDto
            {
                MaintenanceId = item.Id,
                Date = item.PlannedDate ?? item.Date,
                VehicleName = item.Vehicle.Name,
                RegistrationPlate = item.Vehicle.RegistrationPlate,
                Status = (int)item.Status
            })
            .ToListAsync();

        return new WorkPostAffectedMaintenancesDto
        {
            TotalCount = totalCount,
            Items = items
        };
    }

    /// <summary> Возвращает полный каталог организации. </summary>
    public async Task<OrganizationScenarioCatalogDto> GetAsync(string userId, Guid organizationId)
    {
        await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceView);
        await initialization.EnsureDefaultsAsync(organizationId);

        var organization = await context.Organizations
            .AsNoTracking()
            .Where(x => x.Id == organizationId)
            .Select(x => new { x.Id, x.PrimaryScenarioCode })
            .FirstOrDefaultAsync() ?? throw new InvalidOperationException("Организация не найдена.");

        var entities = await context.OrganizationScenarioCatalogItems
            .Include(item => item.BundleItems)
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.KindCode)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var items = entities.Select(item => CatalogItemMapper.Map(item, item.HasBeenUsed)).ToList();

        return new OrganizationScenarioCatalogDto
        {
            OrganizationId = organization.Id,
            ScenarioCode = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(organization.PrimaryScenarioCode),
            WorkTypes = items.Where(x => x.KindCode == OrganizationScenarioCatalogKindCodes.WorkType).ToList(),
            Services = items.Where(x => x.KindCode == OrganizationScenarioCatalogKindCodes.Service).ToList(),
            ServiceBundles = items.Where(x => x.KindCode == OrganizationScenarioCatalogKindCodes.ServiceBundle).ToList(),
            WorkPosts = items.Where(x => x.KindCode == OrganizationScenarioCatalogKindCodes.WorkPost).ToList(),
            Documents = items.Where(x => x.KindCode == OrganizationScenarioCatalogKindCodes.Document).ToList(),
            PlanningRules = items.Where(x => x.KindCode == OrganizationScenarioCatalogKindCodes.PlanningRule).ToList(),
            Materials = items.Where(x => x.KindCode == OrganizationScenarioCatalogKindCodes.Material).ToList()
        };
    }
}
