using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Promotions.Contracts;

namespace OpenCRM.Modules.Billing;

/// <summary>
/// Реализация порта выдачи подарочного доступа организации без оплаты.
/// Требует активную транзакцию вызывающей стороны; сохранение выполняет вызывающая сторона.
/// </summary>
internal sealed class OrganizationPromoAccessGrantService(OpenCRMContext context) : IOrganizationPromoAccessGrantPort
{
    /// <summary> Код источника промо-доступа. </summary>
    private const string PromoSourceCode = "promo";

    /// <summary> Код типа выгоды бонусных дней. </summary>
    private const string BonusDaysBenefitTypeCode = "bonus_days";

    /// <summary> Выдаёт организации подарочный доступ по промо-заявке. </summary>
    public async Task<DateTime> GrantAsync(Guid organizationId, string actorUserId, int planId, int days, long applicationId)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Выдача подарочного доступа требует активной транзакции вызывающей стороны.");
        }

        if (days is < 1 or > 3650)
        {
            throw new InvalidOperationException($"Количество подарочных дней должно быть в диапазоне 1..3650: {days}.");
        }

        var plan = await context.Set<SubscriptionPlan>()
            .SingleOrDefaultAsync(p => p.Id == planId);
        if (plan is null || !plan.IsActive
            || SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode) != SubscriptionPlanCatalogCodes.PlanTypes.Business
            || plan.IsDefaultPlan || plan.IsDemoPlan)
        {
            throw new InvalidOperationException($"Тарифный план {planId} недоступен для подарочной выдачи.");
        }

        var application = await context.Set<PromoCodeApplication>()
            .SingleOrDefaultAsync(a => a.Id == applicationId);
        if (application is null || application.OrganizationId != organizationId
            || application.ApplyActorUserId != actorUserId
            || application.BenefitTypeCode != BonusDaysBenefitTypeCode
            || application.BonusDays != days
            || application.Status != PromoCodeApplicationStatuses.Assigned)
        {
            throw new InvalidOperationException($"Заявка промокода {applicationId} недоступна для выдачи подарочных дней.");
        }

        if (application.ConsumedAt is not null || application.GrantedSubscriptionId is not null)
        {
            throw new InvalidOperationException($"Заявка промокода {applicationId} уже использована.");
        }

        var now = DateTime.UtcNow;
        var current = await context.Set<OrganizationSubscription>()
            .Include(s => s.SubscriptionPlan)
            .Where(s => s.OrganizationId == organizationId && s.EndDate > now)
            .OrderByDescending(s => s.EndDate)
            .FirstOrDefaultAsync();

        if (current is not null)
        {
            await context.Entry(current).ReloadAsync();
        }

        DateTime resultEnd;
        OrganizationSubscription granted;
        if (current is not null && current.IsActive
            && current.SourceCode != PromoSourceCode
            && !current.SubscriptionPlan.IsDemoPlan
            && !current.SubscriptionPlan.IsDefaultPlan)
        {
            current.EndDate = current.EndDate.AddDays(days);
            resultEnd = current.EndDate;
            granted = current;
        }
        else if (current is not null && current.IsActive && current.SourceCode == PromoSourceCode)
        {
            if (current.PromoDaysStartDate is null)
            {
                throw new InvalidOperationException($"Промо-подписка {current.Id} организации {organizationId} не содержит начала подарочных дней.");
            }

            current.EndDate = current.EndDate.AddDays(days);
            resultEnd = current.EndDate;
            granted = current;
        }
        else
        {
            var remainingDemoEnd = current is not null && current.SubscriptionPlan.IsDemoPlan
                ? current.EndDate
                : now;
            if (current is not null && current.IsActive)
            {
                current.EndDate = now;
            }

            granted = new OrganizationSubscription
            {
                OrganizationId = organizationId,
                SubscriptionPlanId = planId,
                StartDate = now,
                EndDate = remainingDemoEnd.AddDays(days),
                AutoRenewal = false,
                SourceCode = PromoSourceCode,
                PromoDaysStartDate = remainingDemoEnd
            };
            context.Set<OrganizationSubscription>().Add(granted);
            resultEnd = granted.EndDate;
        }

        application.GrantedSubscription = granted;
        application.AccessUntil = resultEnd;

        return resultEnd;
    }
}
