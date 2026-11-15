using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Запросы по подпискам пользователя для админки. </summary>
internal sealed class AdminUserSubscriptionQueries(OpenCRMContext opencrmContext)
{
    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Обновляет доступ пользователя к тестовым подпискам. </summary>
    public async Task<bool> UpdateTestSubscriptionsAccessAsync(string userId, bool canUseTestSubscriptions)
    {
        var user = await this._opencrmContext.ApplicationUsers.FirstOrDefaultAsync(x => x.Id == userId);
        if (user == null)
        {
            return false;
        }

        user.CanUseTestSubscriptions = canUseTestSubscriptions;
        await this._opencrmContext.SaveChangesAsync();

        return true;
    }

    /// <summary> Возвращает длительность тарифного плана в днях. </summary>
    public async Task<int?> GetSubscriptionPlanDurationDaysAsync(int planId)
    {
        return await this._opencrmContext.SubscriptionPlans
            .Where(plan => plan.Id == planId)
            .Select(plan => (int?)plan.DurationDays)
            .FirstOrDefaultAsync();
    }

    /// <summary> Возвращает активную подписку пользователя. </summary>
    public async Task<AdminActiveUserSubscriptionDto?> GetActiveUserSubscriptionAsync(string userId)
    {
        var activeSubscription = await this._opencrmContext.UserSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId && item.EndDate >= DateTime.UtcNow);
        if (activeSubscription == null)
        {
            return null;
        }

        return new AdminActiveUserSubscriptionDto(activeSubscription.Id, activeSubscription.EndDate);
    }
}
