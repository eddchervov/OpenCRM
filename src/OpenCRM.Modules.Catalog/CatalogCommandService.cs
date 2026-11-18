using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Modules.Catalog.Defaults;
using OpenCRM.Modules.Catalog.Rules;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Catalog;

/// <summary> Сервис команд каталога сценариев организации. </summary>
internal sealed class CatalogCommandService(
    OpenCRMContext context,
    IOrganizationSubscriptionService organizationSubscriptionService,
    CatalogDefaultsService defaults,
    CatalogUsageReader usage,
    CatalogTransaction transaction) : ICatalogCommandService
{
    /// <summary> Создаёт элемент каталога организации. </summary>
    public Task<OrganizationScenarioCatalogItemDto> CreateAsync(string userId, Guid organizationId, CreateOrganizationScenarioCatalogItemDto dto) =>
        transaction.ExecuteAuditedAsync(eventId => this.CreateCoreAsync(userId, organizationId, dto, eventId));

    /// <summary> Обновляет элемент каталога организации. </summary>
    public Task<OrganizationScenarioCatalogItemDto> UpdateAsync(string userId, Guid organizationId, Guid itemId, UpdateOrganizationScenarioCatalogItemDto dto) =>
        transaction.ExecuteAuditedAsync(eventId => this.UpdateCoreAsync(userId, organizationId, itemId, dto, eventId));

    /// <summary> Удаляет элемент каталога организации. </summary>
    public Task DeleteAsync(string userId, Guid organizationId, Guid itemId) =>
        transaction.ExecuteAuditedAsync(eventId => this.DeleteCoreAsync(userId, organizationId, itemId, eventId));

    /// <summary> Внутренняя логика создания элемента каталога. </summary>
    private async Task<OrganizationScenarioCatalogItemDto> CreateCoreAsync(string userId, Guid organizationId, CreateOrganizationScenarioCatalogItemDto dto, Guid eventId)
    {
        await transaction.LockOrganizationAsync(organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);
        await defaults.EnsureDefaultsAsync(organizationId);

        var organization = await context.Organizations
            .Where(x => x.Id == organizationId)
            .Select(x => new { x.PrimaryScenarioCode, x.CurrencyCode })
            .FirstOrDefaultAsync() ?? throw new InvalidOperationException("Организация не найдена.");

        var kindCode = OrganizationScenarioCatalogKindCodes.Normalize(dto.KindCode);
        var name = CatalogRules.NormalizeName(dto.Name);
        if (kindCode == OrganizationScenarioCatalogKindCodes.WorkPost)
        {
            await this.EnsureWorkPostNameAvailableAsync(organizationId, Guid.Empty, name);
        }

        await this.ValidateServiceSettingsAsync(organizationId, kindCode, dto.UnitPrice, dto.DurationMinutes, dto.ServiceIds);
        var item = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organizationId,
            CurrencyCode = organization.CurrencyCode,
            ScenarioCode = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(organization.PrimaryScenarioCode),
            KindCode = kindCode,
            Code = await this.BuildUniqueCodeAsync(organizationId, kindCode, CatalogRules.Slug(name)),
            Name = name,
            Description = CatalogRules.NormalizeOptional(dto.Description),
            UnitPrice = kindCode == OrganizationScenarioCatalogKindCodes.Service ? dto.UnitPrice : null,
            DurationMinutes = dto.DurationMinutes,
            DocumentKindCode = kindCode == OrganizationScenarioCatalogKindCodes.Document
                ? OrganizationScenarioDocumentKindCodes.Normalize(dto.DocumentKindCode) ?? CatalogRules.InferDocumentKindCode(name)
                : null,
            SortOrder = dto.SortOrder <= 0 ? await this.ResolveNextSortOrderAsync(organizationId, kindCode) : dto.SortOrder,
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await this.UpdateBundleItemsAsync(item, dto.ServiceIds);
        context.OrganizationScenarioCatalogItems.Add(item);
        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = eventId,
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = "catalog.item.created",
            Title = "Изменение справочника",
            TargetType = "catalog_item",
            TargetId = item.Id.ToString("D")
        });
        await context.SaveChangesAsync();

        return CatalogItemMapper.Map(item, await usage.IsUsedAsync(organizationId, item.Id));
    }

    /// <summary> Внутренняя логика обновления элемента каталога. </summary>
    private async Task<OrganizationScenarioCatalogItemDto> UpdateCoreAsync(string userId, Guid organizationId, Guid itemId, UpdateOrganizationScenarioCatalogItemDto dto, Guid eventId)
    {
        await transaction.LockOrganizationAsync(organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);

        var item = await this.LoadItemAsync(organizationId, itemId);
        var isUsedWorkPost = item.KindCode == OrganizationScenarioCatalogKindCodes.WorkPost
            && await usage.IsUsedAsync(organizationId, itemId);
        var normalizedName = CatalogRules.NormalizeName(dto.Name);

        if (isUsedWorkPost && (normalizedName != item.Name
            || CatalogRules.NormalizeOptional(dto.Description) != item.Description
            || dto.UnitPrice != item.UnitPrice
            || dto.DurationMinutes != item.DurationMinutes
            || (dto.SortOrder > 0 && dto.SortOrder != item.SortOrder)))
        {
            throw new InvalidOperationException("Используемый пост можно только вывести из работы или вернуть в работу без изменения его данных.");
        }

        if (item.KindCode == OrganizationScenarioCatalogKindCodes.WorkPost)
        {
            if (normalizedName != item.Name)
            {
                await this.EnsureWorkPostNameAvailableAsync(organizationId, itemId, normalizedName);
            }

            if (dto.IsActive && !item.IsActive)
            {
                await this.EnsureWorkPostNameAvailableAsync(organizationId, itemId, item.Name);
            }
        }

        if (item.KindCode is not (OrganizationScenarioCatalogKindCodes.Service or OrganizationScenarioCatalogKindCodes.ServiceBundle)
            && !isUsedWorkPost && await usage.IsUsedAsync(organizationId, itemId))
        {
            throw new InvalidOperationException("Элемент справочника уже используется. Его нельзя редактировать, чтобы не сломать связанные записи.");
        }

        await this.ValidateServiceSettingsAsync(organizationId, item.KindCode, dto.UnitPrice, dto.DurationMinutes, dto.ServiceIds);
        item.UnitPrice = item.KindCode == OrganizationScenarioCatalogKindCodes.Service ? dto.UnitPrice : null;
        item.DurationMinutes = dto.DurationMinutes;
        await this.UpdateBundleItemsAsync(item, dto.ServiceIds);
        item.Name = normalizedName;
        item.Description = CatalogRules.NormalizeOptional(dto.Description);
        item.DocumentKindCode = item.KindCode == OrganizationScenarioCatalogKindCodes.Document
            ? OrganizationScenarioDocumentKindCodes.Normalize(dto.DocumentKindCode) ?? item.DocumentKindCode ?? CatalogRules.InferDocumentKindCode(item.Name)
            : null;
        item.SortOrder = dto.SortOrder <= 0 ? item.SortOrder : dto.SortOrder;
        item.IsActive = dto.IsActive;
        item.UpdatedAt = DateTime.UtcNow;

        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = eventId,
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = "catalog.item.updated",
            Title = "Изменение справочника",
            TargetType = "catalog_item",
            TargetId = item.Id.ToString("D")
        });
        await context.SaveChangesAsync();

        return CatalogItemMapper.Map(item, await usage.IsUsedAsync(organizationId, item.Id));
    }

    /// <summary> Внутренняя логика удаления элемента каталога. </summary>
    private async Task<bool> DeleteCoreAsync(string userId, Guid organizationId, Guid itemId, Guid eventId)
    {
        await transaction.LockOrganizationAsync(organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);

        var item = await this.LoadItemAsync(organizationId, itemId);

        var isUsed = await usage.IsUsedAsync(organizationId, itemId);
        if (item.KindCode is not (OrganizationScenarioCatalogKindCodes.Service or OrganizationScenarioCatalogKindCodes.ServiceBundle)
            && isUsed && item.KindCode != OrganizationScenarioCatalogKindCodes.WorkPost)
        {
            throw new InvalidOperationException("Элемент справочника уже используется. Его нельзя удалить.");
        }

        if (isUsed || item.IsSystem || item.KindCode is OrganizationScenarioCatalogKindCodes.Service or OrganizationScenarioCatalogKindCodes.ServiceBundle)
        {
            item.IsActive = false;
            item.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            context.OrganizationScenarioCatalogItems.Remove(item);
        }

        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = eventId,
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = "catalog.item.deleted",
            Title = "Изменение справочника",
            TargetType = "catalog_item",
            TargetId = item.Id.ToString("D")
        });
        await context.SaveChangesAsync();

        return true;
    }

    /// <summary> Загружает элемент каталога организации. </summary>
    private async Task<OrganizationScenarioCatalogItem> LoadItemAsync(Guid organizationId, Guid itemId)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationScenarioCatalogItems" WHERE "Id" = {itemId} AND "OrganizationId" = {organizationId} FOR UPDATE""");
        var trackedItem = context.OrganizationScenarioCatalogItems.Local.FirstOrDefault(item => item.Id == itemId);
        if (trackedItem != null)
        {
            foreach (var link in trackedItem.BundleItems.ToArray())
            {
                context.Entry(link).State = EntityState.Detached;
            }

            context.Entry(trackedItem).State = EntityState.Detached;
        }

        return await context.OrganizationScenarioCatalogItems.Include(item => item.BundleItems)
            .SingleOrDefaultAsync(item => item.Id == itemId && item.OrganizationId == organizationId)
            ?? throw new InvalidOperationException("Элемент справочника не найден.");
    }

    /// <summary> Обновляет состав услуг в комплекте каталога. </summary>
    private async Task UpdateBundleItemsAsync(OrganizationScenarioCatalogItem item, IReadOnlyList<Guid> serviceIds)
    {
        if (item.KindCode != OrganizationScenarioCatalogKindCodes.ServiceBundle)
        {
            return;
        }

        // Удаление старых позиций до вставки новых позволяет переставить строки с уникальным Position.
        await context.OrganizationServiceBundleItems.Where(link => link.BundleId == item.Id).ExecuteDeleteAsync();
        foreach (var link in item.BundleItems.ToArray())
        {
            context.Entry(link).State = EntityState.Detached;
        }

        item.BundleItems.Clear();
        for (var position = 0; position < serviceIds.Count; position++)
        {
            var serviceId = serviceIds[position];
            item.BundleItems.Add(new OrganizationServiceBundleItem
            {
                BundleId = item.Id,
                OrganizationId = item.OrganizationId,
                ServiceId = serviceId,
                Position = position
            });
        }
    }

    /// <summary> Определяет следующий порядок сортировки для элементов каталога. </summary>
    private async Task<int> ResolveNextSortOrderAsync(Guid organizationId, string kindCode)
    {
        var maxSortOrder = await context.OrganizationScenarioCatalogItems
            .Where(x => x.OrganizationId == organizationId && x.KindCode == kindCode)
            .Select(x => (int?)x.SortOrder)
            .MaxAsync();

        return (maxSortOrder ?? 0) + 10;
    }

    /// <summary> Строит уникальный код элемента каталога. </summary>
    private async Task<string> BuildUniqueCodeAsync(Guid organizationId, string kindCode, string baseCode)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseCode) ? "item" : baseCode;
        normalizedBase = normalizedBase[..Math.Min(normalizedBase.Length, 120)];
        var candidate = normalizedBase;
        var index = 2;

        while (await context.OrganizationScenarioCatalogItems.AnyAsync(x =>
            x.OrganizationId == organizationId
            && x.KindCode == kindCode
            && x.Code == candidate))
        {
            var suffix = $"-{index}";
            candidate = $"{normalizedBase[..Math.Min(normalizedBase.Length, 120 - suffix.Length)]}{suffix}";
            index++;
        }

        return candidate;
    }

    /// <summary> Проверяет уникальность названия среди действующих и ранее использованных постов. </summary>
    private async Task EnsureWorkPostNameAvailableAsync(Guid organizationId, Guid itemId, string name)
    {
        var reservedPosts = await context.OrganizationScenarioCatalogItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && item.KindCode == OrganizationScenarioCatalogKindCodes.WorkPost
                && item.Id != itemId
                && (item.HasBeenUsed || item.IsActive))
            .OrderByDescending(item => item.HasBeenUsed)
            .Select(item => new { item.Name, item.HasBeenUsed })
            .ToListAsync();
        var conflictingPost = reservedPosts.FirstOrDefault(post =>
            string.Equals(post.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (conflictingPost == null)
        {
            return;
        }

        throw new InvalidOperationException(conflictingPost.HasBeenUsed
            ? "Название уже использованного поста нельзя повторно присвоить другой позиции."
            : "Пост с таким названием уже есть. Укажите другое название.");
    }

    /// <summary> Проверяет корректность настроек услуги каталога. </summary>
    private async Task ValidateServiceSettingsAsync(Guid organizationId, string kind, decimal? price, int? durationMinutes, Guid[] serviceIds)
    {
        if (durationMinutes.HasValue && (durationMinutes is < 1 or > 525600
            || kind is not (OrganizationScenarioCatalogKindCodes.Service or OrganizationScenarioCatalogKindCodes.ServiceBundle)))
        {
            throw new InvalidOperationException("Длительность услуги или набора должна быть от 1 до 525600 минут.");
        }

        if (price.HasValue && (price < 0 || price > 999999999 || decimal.Round(price.Value, 2) != price))
        {
            throw new InvalidOperationException("Укажите цену от 0 до 999999999, не более двух знаков после запятой.");
        }
        if (kind != OrganizationScenarioCatalogKindCodes.ServiceBundle)
        {
            return;
        }
        if (serviceIds == null || serviceIds.Length == 0 || serviceIds.Length > 100 || serviceIds.Distinct().Count() != serviceIds.Length)
        {
            throw new InvalidOperationException("Выберите от 1 до 100 разных услуг для набора.");
        }

        var count = await context.OrganizationScenarioCatalogItems.CountAsync(item =>
            item.OrganizationId == organizationId && item.KindCode == OrganizationScenarioCatalogKindCodes.Service
            && item.IsActive && serviceIds.Contains(item.Id));
        if (count != serviceIds.Length)
        {
            throw new InvalidOperationException("В набор можно включить только действующие услуги этой организации.");
        }
    }
}
