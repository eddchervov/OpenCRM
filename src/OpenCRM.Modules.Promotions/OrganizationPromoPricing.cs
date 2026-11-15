using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Modules.Promotions;

/// <summary>
/// Расчёт скидки организации по применённым промокодам. Только чтение, без изменения данных.
/// </summary>
internal static class OrganizationPromoPricing
{
    /// <summary> Рассчитывает цену тарифа организации с учётом промокодов. </summary>
    public static async Task<PromotionPriceDto> CalculateAsync(OpenCRMContext context, Guid organizationId, int planId, decimal originalPrice)
    {
        if (originalPrice < 0)
        {
            throw new InvalidOperationException("Исходная цена не может быть отрицательной.");
        }

        var organization = await context.Organizations
            .AsNoTracking()
            .Where(item => item.Id == organizationId && item.IsActive && !item.IsDeleted && !item.IsArchived)
            .Select(item => new { item.PrimaryScenarioCode })
            .FirstOrDefaultAsync();

        if (organization == null)
        {
            return CreateRegularPrice(originalPrice, organizationId);
        }

        var now = DateTime.UtcNow;

        var applications = await context.PromoCodeApplications
            .AsNoTracking()
            .Include(application => application.PromoCode)
                .ThenInclude(promo => promo.Scenarios)
            .Include(application => application.PromoCode)
                .ThenInclude(promo => promo.Plans)
            .Include(application => application.PaymentCreationOperation)
            .Include(application => application.SubscriptionPayment)
            .Where(application => application.OrganizationId == organizationId
                && (application.Status == PromoCodeApplicationStatuses.Assigned
                    || application.Status == PromoCodeApplicationStatuses.Claimed))
            .ToListAsync();

        var plan = await context.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == planId);

        if (plan == null
            || !plan.IsActive
            || plan.IsDemoPlan
            || plan.IsDefaultPlan
            || SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode) != SubscriptionPlanCatalogCodes.PlanTypes.Business)
        {
            return CreateRegularPrice(originalPrice, organizationId);
        }

        PromotionPriceDto? bestQuote = null;

        foreach (var application in applications)
        {
            if (!PromotionPaymentCoordinator.CanReuseClaim(application))
            {
                continue;
            }

            var promo = application.PromoCode;
            var benefitType = application.BenefitTypeCode?.Trim().ToLowerInvariant() ?? string.Empty;

            // Только денежные выгоды участвуют в расчёте цены.
            if (benefitType != PromoCodeCatalogCodes.DiscountTypes.Percentage
                && benefitType != PromoCodeCatalogCodes.DiscountTypes.FixedAmount)
            {
                continue;
            }

            // Срок действия кода проверялся при Apply: расчёт учитывает только дедлайн заявки.
            if (application.DiscountValidUntil is DateTime deadline && deadline <= now)
            {
                continue;
            }

            if (!promo.AppliesToAllScenarios && !promo.Scenarios.Any(item => item.ScenarioCode == organization.PrimaryScenarioCode))
            {
                continue;
            }

            if (promo.AppliesToAllPaidPlans || !promo.Plans.Any(item => item.SubscriptionPlanId == planId))
            {
                continue;
            }

            var value = application.DiscountValue;

            if (!value.HasValue || value.Value <= 0
                || benefitType == PromoCodeCatalogCodes.DiscountTypes.Percentage && value.Value > 99)
            {
                continue;
            }

            var finalPrice = GetFinalPrice(originalPrice, benefitType, value.Value);

            if (finalPrice >= originalPrice)
            {
                continue;
            }

            var quote = new PromotionPriceDto
            {
                OriginalPrice = originalPrice,
                FinalPrice = finalPrice,
                DiscountAmount = originalPrice - finalPrice,
                PromoCodeId = promo.Id,
                PromoCode = promo.Code,
                OrganizationId = organizationId,
                PromoCodeApplicationId = application.Id
            };

            // Выгоды не складываются: берём минимальную итоговую цену, при равенстве — минимальный Id заявки.
            if (bestQuote == null
                || quote.FinalPrice < bestQuote.FinalPrice
                || (quote.FinalPrice == bestQuote.FinalPrice && application.Id < bestQuote.PromoCodeApplicationId!.Value))
            {
                bestQuote = quote;
            }
        }

        if (bestQuote != null)
        {
            return bestQuote;
        }

        return CreateRegularPrice(originalPrice, organizationId);
    }

    /// <summary>
    /// Итоговая цена по типу и значению выгоды. Минимальная цена 1 рубль, округление от нуля.
    /// </summary>
    internal static decimal GetFinalPrice(decimal originalPrice, string benefitTypeCode, decimal value)
    {
        decimal finalPrice;

        if (benefitTypeCode == PromoCodeCatalogCodes.DiscountTypes.Percentage)
        {
            finalPrice = originalPrice * (1 - value / 100m);
        }
        else
        {
            finalPrice = originalPrice - value;
        }

        finalPrice = Math.Min(Math.Max(1m, finalPrice), originalPrice);

        return Math.Round(finalPrice, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary> Формирует DTO регулярной цены без скидки. </summary>
    private static PromotionPriceDto CreateRegularPrice(decimal originalPrice, Guid organizationId) =>
        new()
        {
            OriginalPrice = originalPrice,
            FinalPrice = Math.Round(originalPrice, 2, MidpointRounding.AwayFromZero),
            DiscountAmount = 0m,
            PromoCodeId = null,
            PromoCode = null,
            OrganizationId = organizationId,
            PromoCodeApplicationId = null
        };
}
