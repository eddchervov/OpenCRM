using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Rules;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Queries;

/// <summary> Сервис поиска клиентов для быстрой записи. </summary>
public sealed class QuickBookingClientLookupService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptions) : IQuickBookingClientLookupService
{
    /// <summary> Контекст базы данных для поиска клиентов. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Зависимость сервиса подписок организаций. </summary>
    private readonly IOrganizationSubscriptionService _subscriptions = subscriptions;

    /// <summary> Проверяет существование клиента в организации. </summary>
    public async Task<bool> ExistsAsync(string userId, Guid organizationId, Guid clientId)
    {
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.MaintenanceCreate);

        return await this._context.OrganizationClients
            .AsNoTracking()
            .AnyAsync(client => client.Id == clientId
                && client.OrganizationId == organizationId
                && !client.IsDeleted);
    }

    /// <summary> Идентифицирует клиента только по единственному совпадению телефона. </summary>
    public async Task<Guid?> FindExistingAsync(string userId, Guid organizationId, string? phone, string? name)
    {
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.MaintenanceCreate);

        var matches = await this.FindPhoneMatchesAsync(organizationId, phone);
        if (matches.Length > 1)
        {
            throw new InvalidOperationException("Найдено несколько клиентов с этим телефоном. Выберите клиента из списка.");
        }

        return matches.SingleOrDefault();
    }

    /// <summary> Ищет возможный дубль при явном создании клиента. </summary>
    public async Task<Guid?> FindPotentialDuplicateAsync(string userId, Guid organizationId, string? phone, string? name)
    {
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.ClientsCreate);

        var matches = await this.FindPhoneMatchesAsync(organizationId, phone);
        if (matches.Length > 0)
        {
            return matches[0];
        }

        var normalizedName = name?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        return await this._context.OrganizationClients.AsNoTracking()
            .Where(client => client.OrganizationId == organizationId && !client.IsDeleted
                && client.Name.Trim().ToUpper() == normalizedName)
            .OrderBy(client => client.Id)
            .Select(client => (Guid?)client.Id)
            .FirstOrDefaultAsync();
    }

    private async Task<Guid?[]> FindPhoneMatchesAsync(Guid organizationId, string? phone)
    {
        var normalizedPhone = ClientPhoneSearch.Normalize(phone);
        if (normalizedPhone.Length == 0)
        {
            return [];
        }

        var clients = this._context.OrganizationClients.AsNoTracking()
            .Where(client => client.OrganizationId == organizationId && !client.IsDeleted);

        return await ClientPhoneSearch.GetCanonicalPhones(clients)
            .Where(client => client.Number == normalizedPhone)
            .OrderBy(client => client.Id)
            .Select(client => (Guid?)client.Id)
            .Take(2)
            .ToArrayAsync();
    }
}
