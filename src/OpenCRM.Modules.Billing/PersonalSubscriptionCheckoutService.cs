using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис оформления покупки личных подписок. </summary>
internal sealed class PersonalSubscriptionCheckoutService(
    OpenCRMContext context,
    IPaymentCreationGateway paymentCreationGateway,
    IPersonalSubscriptionPurchasePolicyService purchasePolicy,
    IPersonalSubscriptionQueryService subscriptionQuery,
    ILogger<PersonalSubscriptionCheckoutService> logger) : IPersonalSubscriptionCheckoutService
{
    /// <summary> Создаёт платёж за покупку личной подписки. </summary>
    public async Task<PaymentCreationResult> PurchaseAsync(
        string userId,
        int planId,
        PaymentBuyerDto? buyer,
        Guid operationId,
        string? returnUrl = null)
    {
        var intent = new PaymentCreationIntent(operationId, userId, "yookassa_purchase", planId, buyer, returnUrl);

        try
        {
            var existingResult = await paymentCreationGateway.ResumeAsync(intent);
            if (existingResult is not null)
            {
                return existingResult;
            }

            var plan = await context.SubscriptionPlans.FindAsync(planId);
            if (plan is null || !plan.IsActive)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Тарифный план не найден или неактивен.");
            }

            if (plan.IsDemoPlan)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Демо-тариф нельзя приобрести.");
            }

            var policyResult = await purchasePolicy.CanPurchasePlanAsync(userId, planId);
            if (!policyResult.Success)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, policyResult.Error);
            }

            var userExists = await context.Users.AnyAsync(user => user.Id == userId);
            if (!userExists)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Пользователь не найден.");
            }

            return await paymentCreationGateway.CreateAsync(new PaymentCreationDraft(
                intent,
                planId,
                plan.EffectivePrice,
                $"Подписка {plan.Name} на {plan.DurationDays} дней"));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ошибка оплаты личной подписки пользователя {UserId}, тариф {PlanId}.", userId, planId);
            return new PaymentCreationResult(PaymentCreationStatus.InProgress, "Состояние платежа проверяется. Повторите запрос с тем же ключом.");
        }
    }

    /// <summary> Создаёт платёж за продление личной подписки. </summary>
    public async Task<PaymentCreationResult> ExtendAsync(
        string userId,
        PaymentBuyerDto? buyer,
        Guid operationId,
        string? returnUrl = null)
    {
        var intent = new PaymentCreationIntent(operationId, userId, "yookassa_extension", null, buyer, returnUrl);

        try
        {
            var existingResult = await paymentCreationGateway.ResumeAsync(intent);
            if (existingResult is not null)
            {
                return existingResult;
            }

            var currentSubscription = await context.UserSubscriptions
                .AsNoTracking()
                .Include(subscription => subscription.SubscriptionPlan)
                .Where(subscription => subscription.UserId == userId && subscription.EndDate >= DateTime.UtcNow)
                .OrderByDescending(subscription => subscription.EndDate)
                .FirstOrDefaultAsync();

            if (currentSubscription is null)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "У вас нет активной подписки для продления.");
            }

            if (currentSubscription.SubscriptionPlan.IsDemoPlan)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Демо-тариф нельзя продлить.");
            }

            if (!await subscriptionQuery.CanExtendSubscriptionAsync(userId))
            {
                var daysUntilExpiry = (currentSubscription.EndDate - DateTime.UtcNow).TotalDays;
                var message = $"Продление доступно только когда до окончания подписки осталось не более 7 дней. Сейчас осталось: {(int)Math.Ceiling(daysUntilExpiry)} дней";

                return new PaymentCreationResult(PaymentCreationStatus.Invalid, message);
            }

            var plan = currentSubscription.SubscriptionPlan;
            return await paymentCreationGateway.CreateAsync(new PaymentCreationDraft(
                intent,
                currentSubscription.SubscriptionPlanId,
                plan.EffectivePrice,
                $"Продление подписки {plan.Name} на {plan.DurationDays} дней"));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ошибка продления личной подписки пользователя {UserId}.", userId);
            return new PaymentCreationResult(PaymentCreationStatus.InProgress, "Состояние платежа проверяется. Повторите запрос с тем же ключом.");
        }
    }
}
