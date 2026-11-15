using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Promotions.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис оформления покупки и продления подписок организаций. </summary>
internal sealed class OrganizationSubscriptionCheckoutService(
    OpenCRMContext context,
    IPaymentCreationGateway paymentCreationGateway,
    IOrganizationSubscriptionPurchasePolicyService purchasePolicy,
    IFeatureDefaultsInitializer featureDefaultsInitializer,
    IOrganizationPermissionService organizationPermissionService,
    IPromoCodeService promoCodeService,
    ILogger<OrganizationSubscriptionCheckoutService> logger) : IOrganizationSubscriptionCheckoutService
{
    /// <summary> Код источника промо-доступа. </summary>
    private const string PromoSourceCode = "promo";

    /// <summary> Оформляет покупку подписки организации. </summary>
    public async Task<PaymentCreationResult> PurchaseAsync(
        string userId,
        Guid organizationId,
        int planId,
        PaymentBuyerDto? buyer,
        Guid operationId,
        string? returnUrl = null)
    {
        var operationType = $"yookassa_workspace_purchase:{organizationId:D}";
        var intent = new PaymentCreationIntent(operationId, userId, operationType, planId, buyer, returnUrl);

        try
        {
            var organization = await this.EnsureBillingAccessAsync(userId, organizationId);
            if (organization is null)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Организация не найдена или доступ закрыт.");
            }

            var existingResult = await paymentCreationGateway.ResumeAsync(intent);
            if (existingResult is not null)
            {
                return existingResult;
            }

            var policyResult = await purchasePolicy.CanPurchasePlanAsync(userId, organizationId, planId);
            if (!policyResult.Success)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, policyResult.Error);
            }

            var plan = await context.SubscriptionPlans
                .AsNoTracking()
                .FirstAsync(item => item.Id == planId);
            var promotion = await promoCodeService.CalculatePriceAsync(userId, organizationId, planId, plan.EffectivePrice);

            return await paymentCreationGateway.CreateAsync(new PaymentCreationDraft(
                intent,
                planId,
                promotion.FinalPrice,
                $"Тариф {plan.Name} для рабочего пространства {organization.Name} на {plan.DurationDays} дней",
                OrganizationSubscriptionCheckoutService.MapPromotion(promotion)));
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ошибка оплаты тарифа рабочего пространства {OrganizationId}.", organizationId);
            return new PaymentCreationResult(PaymentCreationStatus.InProgress, "Состояние платежа проверяется. Повторите запрос с тем же ключом.");
        }
    }

    /// <summary> Оформляет продление подписки организации. </summary>
    public async Task<PaymentCreationResult> ExtendAsync(
        string userId,
        Guid organizationId,
        PaymentBuyerDto? buyer,
        Guid operationId,
        string? returnUrl = null)
    {
        var operationType = $"yookassa_workspace_extension:{organizationId:D}";
        var intent = new PaymentCreationIntent(operationId, userId, operationType, null, buyer, returnUrl);

        try
        {
            var organization = await this.EnsureBillingAccessAsync(userId, organizationId);
            if (organization is null)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Организация не найдена или доступ закрыт.");
            }

            var existingResult = await paymentCreationGateway.ResumeAsync(intent);
            if (existingResult is not null)
            {
                return existingResult;
            }

            var currentSubscription = await context.OrganizationSubscriptions
                .AsNoTracking()
                .Include(subscription => subscription.SubscriptionPlan)
                .Where(subscription => subscription.OrganizationId == organizationId && subscription.EndDate >= DateTime.UtcNow)
                .OrderByDescending(subscription => subscription.EndDate)
                .FirstOrDefaultAsync();

            if (currentSubscription is null)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "У рабочего пространства нет активного тарифа для продления.");
            }

            if (currentSubscription.SourceCode == PromoSourceCode)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Подарочный доступ не продлевается оплатой. Выберите тариф для покупки.");
            }

            var plan = currentSubscription.SubscriptionPlan;
            if (plan.IsDemoPlan)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Демо-тариф нельзя продлить.");
            }

            if (plan.IsDefaultPlan)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Базовый тариф не продлевается.");
            }

            if ((currentSubscription.EndDate - DateTime.UtcNow).TotalDays > 7)
            {
                var daysUntilExpiry = (int)Math.Ceiling((currentSubscription.EndDate - DateTime.UtcNow).TotalDays);
                return new PaymentCreationResult(
                    PaymentCreationStatus.Invalid,
                    $"Продление откроется за 7 дней до окончания. Осталось дней: {daysUntilExpiry}.");
            }

            var promotion = await promoCodeService.CalculatePriceAsync(
                userId,
                organizationId,
                currentSubscription.SubscriptionPlanId,
                plan.EffectivePrice);

            return await paymentCreationGateway.CreateAsync(new PaymentCreationDraft(
                intent,
                currentSubscription.SubscriptionPlanId,
                promotion.FinalPrice,
                $"Продление тарифа {plan.Name} для рабочего пространства {organization.Name} на {plan.DurationDays} дней",
                OrganizationSubscriptionCheckoutService.MapPromotion(promotion)));
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ошибка продления тарифа рабочего пространства {OrganizationId}.", organizationId);
            return new PaymentCreationResult(PaymentCreationStatus.InProgress, "Состояние платежа проверяется. Повторите запрос с тем же ключом.");
        }
    }

    /// <summary> Проверяет доступ пользователя к биллингу организации. </summary>
    private async Task<Organization?> EnsureBillingAccessAsync(string userId, Guid organizationId)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();

        var organization = await context.Organizations
            .Include(item => item.Members)
            .FirstOrDefaultAsync(item => item.Id == organizationId
                && item.IsActive
                && !item.IsDeleted
                && !item.IsArchived
                && item.Members.Any(member => member.UserId == userId));

        if (organization is null)
        {
            return null;
        }

        if (!await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.BillingManage))
        {
            throw new UnauthorizedAccessException(OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.BillingManage));
        }

        return organization;
    }

    /// <summary> Преобразует промо-цену в DTO. </summary>
    private static PaymentPromotionDto? MapPromotion(PromotionPriceDto promotion)
    {
        if (!promotion.HasDiscount)
        {
            return null;
        }

        return new PaymentPromotionDto
        {
            PromoCodeId = promotion.PromoCodeId!.Value,
            PromoCode = promotion.PromoCode!,
            OriginalAmount = promotion.OriginalPrice,
            DiscountAmount = promotion.DiscountAmount,
            OrganizationId = promotion.OrganizationId,
            PromoCodeApplicationId = promotion.PromoCodeApplicationId
        };
    }
}
