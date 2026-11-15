using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис административных команд по подпискам пользователей. </summary>
internal sealed class AdminPersonalSubscriptionCommandService(
    OpenCRMContext opencrmContext,
    AdminSubscriptionMutation mutation)
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Зависимость мутации подписок. </summary>
    private readonly AdminSubscriptionMutation _mutation = mutation;

    /// <summary> Выдаёт пользователю тариф на заданное число дней. </summary>
    public Task<AdminBillingSubscriptionOperationDto> GrantUserPlanAsync(
        string userId,
        int planId,
        int days,
        string? reason,
        string adminUserId)
    {
        return this._mutation.ExecuteAsync(() =>
            this.GrantUserPlanCoreAsync(userId, planId, days, reason, adminUserId));
    }

    /// <summary> Основная логика выдачи тарифа пользователю. </summary>
    private async Task<AdminBillingSubscriptionOperationDto> GrantUserPlanCoreAsync(
        string userId,
        int planId,
        int days,
        string? reason,
        string adminUserId)
    {
        var normalizedReason = reason?.Trim();
        if (days is < 1 or > 366 || string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 500)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Укажите срок от 1 до 366 дней и причину длиной до 500 символов.");
        }

        var plan = await this._opencrmContext.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == planId && item.IsActive
                && !item.IsDemoPlan && !item.IsDefaultPlan
                && item.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal);
        if (plan == null)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Активный личный тариф не найден.");
        }

        var userExists = await this._opencrmContext.ApplicationUsers.AnyAsync(user => user.Id == userId);
        if (!userExists)
        {
            return AdminBillingOperationResults.SubscriptionMissing("Пользователь не найден.");
        }

        var now = DateTime.UtcNow;
        var hasActiveSubscription = await this._opencrmContext.UserSubscriptions
            .AnyAsync(subscription => subscription.UserId == userId && subscription.EndDate >= now);
        if (hasActiveSubscription)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("У пользователя уже есть действующий тариф. Сначала завершите его.");
        }

        var endDate = now.AddDays(days);
        var subscription = new UserSubscription
        {
            UserId = userId,
            SubscriptionPlanId = plan.Id,
            SourceCode = "manual",
            StartDate = now,
            EndDate = endDate,
            AutoRenewal = false
        };
        this._opencrmContext.UserSubscriptions.Add(subscription);
        this._opencrmContext.UserSubscriptionHistory.Add(new UserSubscriptionHistory
        {
            UserId = userId,
            SubscriptionPlanId = plan.Id,
            StartDate = now,
            EndDate = endDate,
            Status = "created",
            AutoRenewal = false,
            SourceCode = "manual",
            CreatedAt = now,
            Comment = "Тариф предоставлен администратором без оплаты"
        });
        await this._opencrmContext.SaveChangesAsync();
        this._mutation.AddAudit("users", subscription.Id, userId, "granted", null, endDate, adminUserId, normalizedReason, now);
        await this._opencrmContext.SaveChangesAsync();

        return new AdminBillingSubscriptionOperationDto
        {
            Succeeded = true,
            Message = $"Тариф предоставлен до {endDate:dd.MM.yyyy} без оплаты.",
            EndDate = endDate
        };
    }

    /// <summary> Выдаёт пользователю демо-подписку. </summary>
    public Task<AdminBillingSubscriptionOperationDto> GrantUserDemoAsync(string userId, string? reason, string adminUserId)
    {
        return this.UpdateUserDemoAsync(userId, reason, adminUserId, grant: true);
    }

    /// <summary> Отзывает демо-подписку пользователя. </summary>
    public Task<AdminBillingSubscriptionOperationDto> RevokeUserDemoAsync(string userId, string? reason, string adminUserId)
    {
        return this.UpdateUserDemoAsync(userId, reason, adminUserId, grant: false);
    }

    /// <summary> Выдаёт или отзывает демо-подписку пользователя. </summary>
    private async Task<AdminBillingSubscriptionOperationDto> UpdateUserDemoAsync(
        string userId,
        string? reason,
        string adminUserId,
        bool grant)
    {
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 500)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Укажите причину длиной до 500 символов.");
        }

        return await this._mutation.ExecuteAsync(async () =>
        {
            var now = DateTime.UtcNow;
            var userExists = await this._opencrmContext.ApplicationUsers.AnyAsync(user => user.Id == userId);
            if (!userExists)
            {
                return AdminBillingOperationResults.SubscriptionMissing("Пользователь не найден.");
            }

            var activeSubscriptions = await this._opencrmContext.UserSubscriptions
                .Include(subscription => subscription.SubscriptionPlan)
                .Where(subscription => subscription.UserId == userId && subscription.EndDate >= now)
                .ToListAsync();

            if (grant)
            {
                if (activeSubscriptions.Count > 0)
                {
                    return AdminBillingOperationResults.SubscriptionInvalid("У пользователя уже есть действующий тариф. Демо не выдано.");
                }

                var demoPlan = await this._opencrmContext.SubscriptionPlans
                    .AsNoTracking()
                    .FirstOrDefaultAsync(plan => plan.IsActive
                        && plan.IsDemoPlan
                        && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal);
                if (demoPlan == null || demoPlan.DurationDays <= 0)
                {
                    return AdminBillingOperationResults.SubscriptionInvalid("Активный личный демо-тариф не настроен.");
                }

                var endDate = now.AddDays(demoPlan.DurationDays);
                var subscription = new UserSubscription
                {
                    UserId = userId,
                    SubscriptionPlanId = demoPlan.Id,
                    SourceCode = "demo",
                    StartDate = now,
                    EndDate = endDate,
                    AutoRenewal = false
                };
                this._opencrmContext.UserSubscriptions.Add(subscription);
                this._opencrmContext.UserSubscriptionHistory.Add(new UserSubscriptionHistory
                {
                    UserId = userId,
                    SubscriptionPlanId = demoPlan.Id,
                    StartDate = now,
                    EndDate = endDate,
                    Status = "created",
                    AutoRenewal = false,
                    SourceCode = "demo",
                    CreatedAt = now,
                    Comment = "Демо выдано администратором"
                });
                await this._opencrmContext.SaveChangesAsync();
                this._mutation.AddAudit("users", subscription.Id, userId, "demo_granted", null, endDate, adminUserId, normalizedReason, now);
                await this._opencrmContext.SaveChangesAsync();

                return new AdminBillingSubscriptionOperationDto
                {
                    Succeeded = true,
                    Message = $"Демо выдано до {endDate:dd.MM.yyyy}.",
                    EndDate = endDate
                };
            }

            var demoSubscription = activeSubscriptions
                .Where(subscription => subscription.SubscriptionPlan.IsDemoPlan)
                .OrderByDescending(subscription => subscription.EndDate)
                .FirstOrDefault();
            if (demoSubscription == null)
            {
                return AdminBillingOperationResults.SubscriptionInvalid("Активного демо у пользователя нет.");
            }

            var previousEndDate = demoSubscription.EndDate;
            demoSubscription.EndDate = now.AddTicks(-1);
            demoSubscription.AutoRenewal = false;
            this._mutation.AddAudit(
                "users", demoSubscription.Id, userId, "demo_revoked", previousEndDate,
                demoSubscription.EndDate, adminUserId, normalizedReason, now);
            await this._opencrmContext.SaveChangesAsync();

            return new AdminBillingSubscriptionOperationDto
            {
                Succeeded = true,
                Message = "Демо пользователя отозвано."
            };
        });
    }
}
