using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;

namespace OpenCRM.Modules.Catalog;

/// <summary> Сервис массового сохранения элементов каталога при настройке. </summary>
internal sealed class CatalogSetupService(OpenCRMContext context) : ICatalogSetupService
{
    /// <summary> Сохраняет список элементов каталога указанного вида. </summary>
    public async Task SaveItemsAsync(Guid organizationId, string kindCode, IReadOnlyList<CatalogSetupItemDto> requestedItems)
    {
        if (requestedItems.Any(item => item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Name)
            || item.Name.Trim().Length > 160 || item.UnitPrice < 0 || item.UnitPrice > 999999999
            || (item.UnitPrice.HasValue && decimal.Round(item.UnitPrice.Value, 2) != item.UnitPrice.Value))
            || requestedItems.Select(item => item.Id).Distinct().Count() != requestedItems.Count)
        {
            throw new InvalidOperationException(
                "Проверьте названия и цены: цена не может быть отрицательной и содержать больше двух знаков после запятой.");
        }

        if (requestedItems.Any(item => item.DurationMinutes is < 1 or > 525600))
        {
            throw new InvalidOperationException("Длительность услуги должна быть от 1 до 525600 минут.");
        }

        var scenarioCode = await context.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.PrimaryScenarioCode)
            .SingleAsync();
        var items = await context.OrganizationScenarioCatalogItems
            .Where(item => item.OrganizationId == organizationId && item.KindCode == kindCode)
            .ToListAsync();
        var existingById = items.ToDictionary(item => item.Id);
        var requestedIds = requestedItems.Select(item => item.Id).ToArray();
        if (await context.OrganizationScenarioCatalogItems.AnyAsync(item => requestedIds.Contains(item.Id)
            && (item.OrganizationId != organizationId || item.KindCode != kindCode)))
        {
            throw new InvalidOperationException("Выбрана позиция из другого справочника.");
        }

        var usedItemIds = await context.OrganizationScenarioCatalogItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.KindCode == kindCode && item.HasBeenUsed)
            .Select(item => item.Id)
            .ToListAsync();

        if (kindCode == OrganizationScenarioCatalogKindCodes.WorkPost)
        {
            var usedPosts = items.Where(item => usedItemIds.Contains(item.Id)).ToArray();
            var hasUsedNameConflict = requestedItems
                .Where(item => item.IsActive)
                .Any(requested => usedPosts.Any(used => used.Id != requested.Id
                    && string.Equals(used.Name.Trim(), requested.Name.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (hasUsedNameConflict)
            {
                throw new InvalidOperationException("Название уже использованного поста нельзя повторно присвоить другой позиции.");
            }
        }

        var names = items
            .Where(item => item.IsActive && !requestedIds.Contains(item.Id))
            .Select(item => item.Name.Trim())
            .Concat(requestedItems.Where(item => item.IsActive).Select(item => item.Name.Trim()))
            .ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
        {
            throw new InvalidOperationException("Названия активных позиций не должны повторяться.");
        }

        var usedServiceIds = new HashSet<Guid>();
        if (kindCode == OrganizationScenarioCatalogKindCodes.Service)
        {
            var serviceIds = requestedItems
                .Where(value => !value.IsActive)
                .Where(value => existingById.TryGetValue(value.Id, out var item) && item.IsActive)
                .Select(value => value.Id)
                .Intersect(usedItemIds)
                .ToArray();

            if (serviceIds.Length > 0)
            {
                var historicalServiceIds = await context.MaintenanceDetails
                    .Where(detail => detail.CatalogItemId.HasValue && serviceIds.Contains(detail.CatalogItemId.Value)
                        && context.Maintenance.Any(order => order.Id == detail.MaintenanceId
                            && order.Vehicle.OrganizationId == organizationId))
                    .Select(detail => detail.CatalogItemId!.Value)
                    .Distinct()
                    .ToListAsync();

                var bundleServiceIds = await context.OrganizationServiceBundleItems
                    .Where(link => link.OrganizationId == organizationId && serviceIds.Contains(link.ServiceId)
                        && link.Bundle.IsActive)
                    .Select(link => link.ServiceId)
                    .Distinct()
                    .ToListAsync();

                usedServiceIds.UnionWith(historicalServiceIds);
                usedServiceIds.UnionWith(bundleServiceIds);
            }
        }

        foreach (var value in requestedItems)
        {
            if (!existingById.TryGetValue(value.Id, out var item))
            {
                if (!value.IsActive)
                {
                    continue;
                }

                item = new OrganizationScenarioCatalogItem
                {
                    Id = value.Id,
                    OrganizationId = organizationId,
                    ScenarioCode = scenarioCode,
                    KindCode = kindCode,
                    Code = "setup-" + value.Id.ToString("N"),
                    SortOrder = (items.Count + 1) * 10
                };
                context.OrganizationScenarioCatalogItems.Add(item);
                items.Add(item);
            }
            else
            {
                var used = kindCode == OrganizationScenarioCatalogKindCodes.Service
                    ? usedServiceIds.Contains(item.Id)
                    : usedItemIds.Contains(item.Id);
                if (used && kindCode == OrganizationScenarioCatalogKindCodes.WorkPost && value.Name.Trim() != item.Name)
                {
                    throw new InvalidOperationException(
                        $"«{item.Name}» уже используется. Сохраните название поста без изменений.");
                }

                if (used && kindCode == OrganizationScenarioCatalogKindCodes.WorkPost && value.IsActive != item.IsActive)
                {
                    throw new InvalidOperationException(
                        "Изменяйте доступность использованного поста в справочнике «Посты / боксы».");
                }

                if (used && kindCode != OrganizationScenarioCatalogKindCodes.WorkPost && !value.IsActive && item.IsActive)
                {
                    throw new InvalidOperationException(
                        $"«{item.Name}» уже используется. Эту позицию нельзя отключить.");
                }
            }

            item.Name = value.Name.Trim();
            item.IsActive = value.IsActive;
            item.UnitPrice = kindCode == OrganizationScenarioCatalogKindCodes.Service ? value.UnitPrice : null;
            item.DurationMinutes = kindCode == OrganizationScenarioCatalogKindCodes.Service ? value.DurationMinutes : null;
            item.UpdatedAt = DateTime.UtcNow;
        }
    }
}
