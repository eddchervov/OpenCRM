using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис правил покупки личных подписок. </summary>
internal sealed class PersonalSubscriptionPurchasePolicyService(
    OpenCRMContext context,
    IPersonalSubscriptionQueryService subscriptions) : IPersonalSubscriptionPurchasePolicyService
{
    /// <summary> Проверяет, может ли пользователь купить выбранный тариф. </summary>
    public async Task<SubscriptionResult> CanPurchasePlanAsync(string userId, int planId)
    {
        var plan = await context.SubscriptionPlans.FindAsync(planId);
        if (plan == null || !plan.IsActive)
        {
            return SubscriptionResult.CreateFailed("Тарифный план не найден");
        }

        if (SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode)
            != SubscriptionPlanCatalogCodes.PlanTypes.Personal)
        {
            return SubscriptionResult.CreateFailed("Бизнес-тарифы будут подключаться к рабочим пространствам, а не к личному аккаунту.");
        }

        if (plan.IsDefaultPlan)
        {
            return SubscriptionResult.CreateFailed("Базовый тариф нельзя приобрести вручную");
        }

        if (plan.IsDemoPlan)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф нельзя приобрести");
        }

        var currentSubscription = await subscriptions.GetUserSubscriptionAsync(userId);
        if (currentSubscription == null)
        {
            return SubscriptionResult.CreateSuccess();
        }

        if (currentSubscription.SubscriptionPlan.Id == planId)
        {
            return SubscriptionResult.CreateFailed("У вас уже активна подписка на этот тариф");
        }

        if (plan.Order < currentSubscription.SubscriptionPlan.Order)
        {
            return SubscriptionResult.CreateFailed("Нельзя перейти на тариф ниже текущего");
        }

        return SubscriptionResult.CreateSuccess();
    }
}
