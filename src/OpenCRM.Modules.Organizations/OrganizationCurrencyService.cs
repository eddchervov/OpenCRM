using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations;

/// <summary> Управляет валютой денежных сумм организации. </summary>
internal sealed class OrganizationCurrencyService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptionService,
    IOrganizationPermissionService permissionService,
    IOrganizationAuditService auditService,
    OrganizationTransaction transaction) : IOrganizationCurrencyService
{
    public async Task<CurrencySettingsDto> GetAsync(string userId, Guid organizationId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceView);
        var organization = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId && item.Members.Any(member => member.UserId == userId))
            .Select(item => new { item.CurrencyCode, item.IsActive, item.IsArchived, item.IsDeleted })
            .SingleOrDefaultAsync() ?? throw new KeyNotFoundException("Организация не найдена.");
        var canManage = await permissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);
        if (canManage)
        {
            try
            {
                await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);
            }
            catch (UnauthorizedAccessException)
            {
                canManage = false;
            }
            catch (InvalidOperationException)
            {
                canManage = false;
            }
        }

        return new CurrencySettingsDto
        {
            CurrencyCode = organization.CurrencyCode,
            CanManage = canManage,
            CanChange = canManage && organization.IsActive && !organization.IsArchived && !organization.IsDeleted
                && !await HasDependentDataAsync(organizationId)
        };
    }

    public async Task<CurrencySettingsDto> UpdateAsync(string userId, Guid organizationId, UpdateCurrencySettingsDto dto)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);
        var currencyCode = NormalizeCurrencyCode(dto.CurrencyCode);
        await transaction.ExecuteAuditedAsync(async eventId =>
        {
            await transaction.LockOrganizationAsync(organizationId);
            var organization = await context.Organizations.SingleOrDefaultAsync(item => item.Id == organizationId)
                ?? throw new KeyNotFoundException("Организация не найдена.");
            if (!string.Equals(organization.CurrencyCode, dto.ExpectedCurrencyCode, StringComparison.Ordinal))
            {
                throw new DbUpdateConcurrencyException("Валюта уже изменена. Обновите настройки и повторите попытку.");
            }
            if (!organization.IsActive || organization.IsArchived || organization.IsDeleted)
            {
                throw new InvalidOperationException("Нельзя изменить валюту отключённой организации.");
            }
            if (organization.CurrencyCode != currencyCode && await HasDependentDataAsync(organizationId))
            {
                throw new InvalidOperationException("Валюту нельзя изменить после создания данных организации.");
            }

            organization.CurrencyCode = currencyCode;
            organization.UpdatedAt = DateTime.UtcNow;
            await auditService.AddAsync(new OrganizationAuditEventCreateDto
            {
                EventId = eventId,
                OrganizationId = organizationId,
                ActorUserId = userId,
                EventType = "organization.currency.changed",
                Title = "Валюта организации изменена",
                Description = $"{dto.ExpectedCurrencyCode} → {currencyCode}",
                TargetType = "organization",
                TargetId = organizationId.ToString("D")
            }, saveChanges: false);
            await context.SaveChangesAsync();
        });

        return await this.GetAsync(userId, organizationId);
    }

    private async Task<bool> HasDependentDataAsync(Guid organizationId) =>
        await context.Vehicles.IgnoreQueryFilters().AnyAsync(item => item.OrganizationId == organizationId)
        || await context.Maintenance.IgnoreQueryFilters().AnyAsync(item => item.Vehicle.OrganizationId == organizationId)
        || await context.OrganizationClients.IgnoreQueryFilters().AnyAsync(item => item.OrganizationId == organizationId)
        || await context.OrganizationScenarioCatalogItems.IgnoreQueryFilters().AnyAsync(item => item.OrganizationId == organizationId)
        || await context.OrganizationMaterialStocks.IgnoreQueryFilters().AnyAsync(item => item.OrganizationId == organizationId)
        || await context.OrganizationMaterialMovements.IgnoreQueryFilters().AnyAsync(item => item.OrganizationId == organizationId)
        || await context.MaintenanceDocuments.IgnoreQueryFilters().AnyAsync(item => item.OrganizationId == organizationId)
        || await context.MaintenanceFinancialTotals.IgnoreQueryFilters().AnyAsync(item => item.OrganizationId == organizationId);

    private static string NormalizeCurrencyCode(string? currencyCode)
    {
        var normalized = currencyCode?.Trim().ToUpperInvariant();
        if (!CurrencyCodes.IsSupported(normalized))
        {
            throw new InvalidOperationException("Выберите поддерживаемую валюту.");
        }

        return normalized!;
    }
}
