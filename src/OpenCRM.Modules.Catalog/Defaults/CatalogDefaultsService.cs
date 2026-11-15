using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Catalog.Rules;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Catalog.Defaults;

/// <summary> Сервис создания каталога по умолчанию. </summary>
internal sealed class CatalogDefaultsService(OpenCRMContext context)
{
    /// <summary> Контекст базы данных для работы с каталогом. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Создаёт элементы каталога по умолчанию при их отсутствии. </summary>
    internal async Task EnsureDefaultsAsync(Guid organizationId)
    {
        var organization = await this._context.Organizations
            .Where(value => value.Id == organizationId)
            .Select(value => new { value.Id, value.PrimaryScenarioCode })
            .FirstOrDefaultAsync();

        if (organization == null)
        {
            return;
        }

        var process = OrganizationScenarioProcessCatalog.Resolve(organization.PrimaryScenarioCode);
        var existingKeys = await this._context.OrganizationScenarioCatalogItems
            .Where(value => value.OrganizationId == organizationId)
            .Select(value => value.KindCode + ":" + value.Code)
            .ToListAsync();
        var existing = existingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var documentItemsWithoutKind = await this._context.OrganizationScenarioCatalogItems
            .Where(value =>
                value.OrganizationId == organizationId
                && value.KindCode == OrganizationScenarioCatalogKindCodes.Document
                && string.IsNullOrWhiteSpace(value.DocumentKindCode))
            .ToListAsync();

        foreach (var documentItem in documentItemsWithoutKind)
        {
            documentItem.DocumentKindCode = CatalogRules.InferDocumentKindCode(documentItem.Name);
            documentItem.UpdatedAt = DateTime.UtcNow;
        }

        var unsupportedLegacyDocuments = await this._context.OrganizationScenarioCatalogItems
            .Where(value =>
                value.OrganizationId == organizationId
                && value.KindCode == OrganizationScenarioCatalogKindCodes.Document
                && value.IsSystem
                && value.IsActive)
            .ToListAsync();

        foreach (var documentItem in unsupportedLegacyDocuments.Where(CatalogRules.IsUnsupportedLegacyDocument))
        {
            documentItem.IsActive = false;
            documentItem.UpdatedAt = DateTime.UtcNow;
        }

        var defaults = CatalogDefaults.Build(process);
        foreach (var item in defaults)
        {
            var key = item.KindCode + ":" + item.Code;
            if (existing.Contains(key))
            {
                continue;
            }

            item.OrganizationId = organizationId;
            item.ScenarioCode = process.Code;
            this._context.OrganizationScenarioCatalogItems.Add(item);
        }

        await this._context.SaveChangesAsync();
    }
}
