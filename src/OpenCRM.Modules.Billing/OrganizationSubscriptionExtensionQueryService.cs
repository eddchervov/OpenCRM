using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис запросов о возможности продления подписки организации. </summary>
internal sealed class OrganizationSubscriptionExtensionQueryService(
    OpenCRMContext context,
    IOrganizationPermissionService organizationPermissionService) : IOrganizationSubscriptionExtensionQueryService
{
    /// <summary> Код источника промо-подписки. </summary>
    private const string PromoSourceCode = "promo";

    /// <summary> Проверяет, может ли пользователь продлить подписку организации. </summary>
    public async Task<bool> CanExtendSubscriptionAsync(string userId, Guid organizationId)
    {
        await this.EnsureCanManageBillingAsync(userId, organizationId);

        var currentSubscription = await this.LoadCurrentSubscriptionAsync(organizationId);
        if (currentSubscription is null
            || currentSubscription.Value.IsDemoPlan
            || currentSubscription.Value.IsDefaultPlan
            || currentSubscription.Value.SourceCode == PromoSourceCode)
        {
            return false;
        }

        return (currentSubscription.Value.EndDate - DateTime.UtcNow).TotalDays <= 7;
    }

    /// <summary> Возвращает информацию о продлении подписки и дни до её окончания. </summary>
    public async Task<(bool CanExtend, int DaysUntilExpiry, string Message)> GetSubscriptionExtensionInfoAsync(
        string userId,
        Guid organizationId)
    {
        await this.EnsureCanManageBillingAsync(userId, organizationId);

        var currentSubscription = await this.LoadCurrentSubscriptionAsync(organizationId);
        if (currentSubscription is null)
        {
            return (false, 0, "У рабочего пространства нет активного тарифа для продления.");
        }

        if (currentSubscription.Value.IsDemoPlan)
        {
            var demoDays = (int)Math.Ceiling((currentSubscription.Value.EndDate - DateTime.UtcNow).TotalDays);
            return (false, demoDays, "Демо-тариф нельзя продлить.");
        }

        if (currentSubscription.Value.IsDefaultPlan)
        {
            return (false, 0, "Базовый тариф не продлевается. Для этого рабочего пространства можно подключить платный тариф.");
        }

        if (currentSubscription.Value.SourceCode == PromoSourceCode)
        {
            var promoDays = (int)Math.Ceiling((currentSubscription.Value.EndDate - DateTime.UtcNow).TotalDays);
            return (false, promoDays, "Можно купить тариф: оставшиеся подарочные дни добавятся к оплаченному сроку.");
        }

        var daysUntilExpiry = (int)Math.Ceiling((currentSubscription.Value.EndDate - DateTime.UtcNow).TotalDays);
        var canExtend = daysUntilExpiry <= 7;
        var message = canExtend
            ? $"Тариф можно продлить. Осталось дней: {daysUntilExpiry}."
            : $"Продление откроется за 7 дней до окончания. Осталось дней: {daysUntilExpiry}.";

        return (canExtend, daysUntilExpiry, message);
    }

    /// <summary> Проверяет право пользователя управлять биллингом организации. </summary>
    private async Task EnsureCanManageBillingAsync(string userId, Guid organizationId)
    {
        var organization = await context.Organizations
            .AsNoTracking()
            .Where(item => item.Id == organizationId && !item.IsDeleted && !item.IsArchived && item.IsActive)
            .Select(item => new
            {
                RoleCode = item.Members
                    .Where(member => member.UserId == userId)
                    .Select(member => member.RoleCode)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Организация не найдена.");

        if (organization.RoleCode is null)
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }

        if (!await organizationPermissionService.HasPermissionAsync(
                userId,
                organizationId,
                OrganizationPermissionCodes.BillingManage))
        {
            throw new InvalidOperationException(
                OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.BillingManage));
        }
    }

    /// <summary> Загружает данные текущей подписки организации или null. </summary>
    private async Task<(DateTime EndDate, bool IsDemoPlan, bool IsDefaultPlan, string SourceCode)?> LoadCurrentSubscriptionAsync(Guid organizationId)
    {
        var subscription = await context.OrganizationSubscriptions
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.EndDate >= DateTime.UtcNow)
            .OrderByDescending(item => item.EndDate)
            .Select(item => new
            {
                item.EndDate,
                item.SubscriptionPlan.IsDemoPlan,
                item.SubscriptionPlan.IsDefaultPlan,
                item.SourceCode
            })
            .FirstOrDefaultAsync();

        return subscription is null
            ? null
            : (subscription.EndDate, subscription.IsDemoPlan, subscription.IsDefaultPlan, subscription.SourceCode);
    }
}
