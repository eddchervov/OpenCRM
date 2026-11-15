using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Promotions.Contracts;

namespace OpenCRM.Modules.Promotions;

/// <summary>
/// Координатор однократной скидки организационного промокода.
/// Все методы выполняются в транзакции вызывающей стороны и не сохраняют изменения самостоятельно.
/// </summary>
internal sealed class PromotionPaymentCoordinator(OpenCRMContext context) : IPromotionPaymentCoordinator
{
    /// <summary> Код операции покупки рабочего пространства. </summary>
    private const string WorkspacePurchaseOperationType = "yookassa_workspace_purchase";

    /// <summary> Код операции продления рабочего пространства. </summary>
    private const string WorkspaceExtensionOperationType = "yookassa_workspace_extension";

    /// <summary> Захватывает заявку промокода для операции оплаты. </summary>
    public async Task ClaimAsync(PaymentCreationOperation operation)
    {
        // Без заявки промокода захват не требуется.
        if (operation.PromoCodeApplicationId is null)
        {
            return;
        }

        if (operation.OrganizationId is null)
        {
            throw new InvalidOperationException("Операция покупки рабочего пространства должна иметь идентификатор организации.");
        }

        EnsureTransaction();

        var organizationId = operation.OrganizationId.Value;
        var applicationId = operation.PromoCodeApplicationId.Value;

        // Допускается только покупка или расширение рабочего пространства конкретной организации.
        var expectedPurchase = $"{WorkspacePurchaseOperationType}:{organizationId:D}";
        var expectedExtension = $"{WorkspaceExtensionOperationType}:{organizationId:D}";
        if (operation.OperationType != expectedPurchase && operation.OperationType != expectedExtension)
        {
            throw new InvalidOperationException("Промокод применим только к покупке или расширению рабочего пространства организации.");
        }

        // Предварительное чтение без трекинга только для идентификации заявки.
        var identity = await context.PromoCodeApplications
            .AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Select(a => new { a.OrganizationId, a.PromoCodeId })
            .FirstOrDefaultAsync();

        if (identity is null)
        {
            throw new InvalidOperationException("Заявка промокода не найдена.");
        }

        // Порядок блокировок: организация, промокод, заявка — чтобы избежать взаимоблокировок.
        var organization = await context.Organizations
            .FromSqlInterpolated($"SELECT * FROM \"Organizations\" WHERE \"Id\" = {identity.OrganizationId} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync();

        if (organization is null)
        {
            throw new InvalidOperationException("Организация не найдена.");
        }

        var promoId = await context.PromoCodes
            .FromSqlInterpolated($"SELECT * FROM \"PromoCodes\" WHERE \"Id\" = {identity.PromoCodeId} FOR UPDATE")
            .Select(p => (int?)p.Id)
            .SingleOrDefaultAsync();

        if (promoId is null)
        {
            throw new InvalidOperationException("Промокод заявки не найден.");
        }

        var application = await LoadApplicationAsync(applicationId);

        if (application is null)
        {
            throw new InvalidOperationException("Заявка промокода не найдена.");
        }

        await ValidateForClaimAsync(operation, application, organization);

        if (application.Status == PromoCodeApplicationStatuses.Consumed)
        {
            throw new InvalidOperationException("Заявка промокода уже погашена и не может быть использована повторно.");
        }

        if (application.Status == PromoCodeApplicationStatuses.Claimed)
        {
            // Идемпотентный повтор той же операцией.
            if (application.PaymentCreationOperationId == operation.Id)
            {
                return;
            }

            // Предыдущий платёж мог измениться в другом запросе после загрузки контекста.
            if (application.SubscriptionPayment is not null)
            {
                await context.Entry(application.SubscriptionPayment).ReloadAsync();
            }
            else if (application.PaymentCreationOperation is not null)
            {
                await context.Entry(application.PaymentCreationOperation).ReloadAsync();
            }

            if (!CanReuseClaim(application))
            {
                throw new InvalidOperationException("Заявка промокода уже захвачена другой активной операцией оплаты.");
            }

            // Освобождаем предыдущую привязку, счётчик привязок организации не меняем.
            application.SubscriptionPaymentId = null;
            application.SubscriptionPayment = null;
        }

        application.Status = PromoCodeApplicationStatuses.Claimed;
        application.PaymentCreationOperationId = operation.Id;
        application.PaymentCreationOperation = operation;
    }

    /// <summary> Привязывает оплату подписки к заявке промокода. </summary>
    public async Task AttachPaymentAsync(PaymentCreationOperation operation, SubscriptionPayment payment)
    {
        if (operation.PromoCodeApplicationId is null)
        {
            return;
        }

        EnsureTransaction();

        var applicationId = operation.PromoCodeApplicationId.Value;

        var application = await LoadApplicationAsync(applicationId);

        if (application is null)
        {
            throw new InvalidOperationException("Заявка промокода не найдена.");
        }

        if (application.Status != PromoCodeApplicationStatuses.Claimed
            || application.PaymentCreationOperationId != operation.Id
            || application.OrganizationId != operation.OrganizationId)
        {
            throw new InvalidOperationException("Заявка промокода не захвачена данной операцией оплаты.");
        }

        payment.PromoCodeApplicationId = application.Id;
        // Навигация выставляется до сохранения: идентификатор платежа может быть ещё нулевым.
        application.SubscriptionPayment = payment;

        // Снимок условия из операции переносим в платёж, метаданные операции не меняем.
        payment.PromoCodeId = operation.PromoCodeId;
        payment.PromoCodeSnapshot = operation.PromoCodeSnapshot;
        payment.OriginalAmount = operation.OriginalAmount;
        payment.PromoDiscountAmount = operation.PromoDiscountAmount;

    }

    /// <summary> Погашает заявку промокода при оплате организации. </summary>
    public async Task ConsumeAsync(SubscriptionPayment payment, Guid organizationId)
    {
        if (payment.PromoCodeApplicationId is null)
        {
            return;
        }

        EnsureTransaction();

        var applicationId = payment.PromoCodeApplicationId.Value;

        var application = await LoadApplicationAsync(applicationId);

        if (application is null)
        {
            throw new InvalidOperationException("Заявка промокода не найдена.");
        }

        if (application.OrganizationId != organizationId)
        {
            throw new InvalidOperationException("Платёж относится к другой организации, чем заявка промокода.");
        }

        if (application.Status == PromoCodeApplicationStatuses.Consumed)
        {
            // Идемпотентное повторное погашение тем же платежом.
            if (application.SubscriptionPaymentId == payment.Id)
            {
                return;
            }

            throw new InvalidOperationException("Заявка промокода уже погашена другим платежом.");
        }

        if (application.Status != PromoCodeApplicationStatuses.Claimed
            || application.SubscriptionPaymentId != payment.Id
            || application.PaymentCreationOperation?.SubscriptionPaymentId != payment.Id
            || application.PromoCodeId != payment.PromoCodeId)
        {
            throw new InvalidOperationException("Платёж не связан с захваченной заявкой промокода.");
        }

        if (payment.PaidAt is null)
        {
            throw new InvalidOperationException("Погашение промокода возможно только по подтверждённой оплате.");
        }

        // Активность и срок промокода при погашении подтверждённого платежа не проверяем:
        // корректный захват по действующей на момент расчёта цены цене остаётся в силе.
        application.Status = PromoCodeApplicationStatuses.Consumed;
        application.ConsumedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Чистая функция для расчёта цены: можно ли переиспользовать заявку без побочных эффектов.
    /// </summary>
    internal static bool CanReuseClaim(PromoCodeApplication app)
    {
        if (app.Status == PromoCodeApplicationStatuses.Assigned)
        {
            return true;
        }

        if (app.Status != PromoCodeApplicationStatuses.Claimed)
        {
            return false;
        }

        var previousPayment = app.SubscriptionPayment;
        if (previousPayment is not null)
        {
            // Подтверждённый или возвращённый платёж удерживает заявку независимо от статуса.
            return previousPayment.PaidAt is null
                && previousPayment.RefundedAmount == 0m
                && (previousPayment.PaymentStatus == "canceled" || previousPayment.PaymentStatus == "failed");
        }

        var previousOperation = app.PaymentCreationOperation;
        if (previousOperation is not null)
        {
            return previousOperation.Status == PaymentCreationOperationStatuses.Rejected
                && string.IsNullOrEmpty(previousOperation.ExternalPaymentId)
                && previousOperation.SubscriptionPaymentId is null;
        }

        // Захваченная заявка без известной привязки не освобождается.
        return false;
    }

    /// <summary> Проверяет наличие открытой транзакции. </summary>
    private void EnsureTransaction()
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Методы координатора промокода должны вызываться в транзакции вызывающей стороны.");
        }
    }

    /// <summary>
    /// Загружает заявку с блокировкой FOR UPDATE. Ранее отслеживаемая той же заявкой
    /// сущность без несохранённых изменений отвязывается, чтобы перечитать актуальные данные.
    /// </summary>
    private async Task<PromoCodeApplication?> LoadApplicationAsync(long applicationId)
    {
        var trackedSame = context.ChangeTracker.Entries<PromoCodeApplication>()
            .Where(e => e.Entity.Id == applicationId)
            .ToList();

        foreach (var entry in trackedSame)
        {
            if (entry.State == EntityState.Unchanged)
            {
                entry.State = EntityState.Detached;
            }
            else
            {
                throw new InvalidOperationException("Применение промокода содержит несохранённые изменения.");
            }
        }

        return await context.PromoCodeApplications
            .FromSqlInterpolated($"SELECT * FROM \"PromoCodeApplications\" WHERE \"Id\" = {applicationId} FOR UPDATE")
            .Include(a => a.PromoCode)
                .ThenInclude(p => p.Scenarios)
            .Include(a => a.PromoCode)
                .ThenInclude(p => p.Plans)
            .Include(a => a.PaymentCreationOperation)
            .Include(a => a.SubscriptionPayment)
            .SingleOrDefaultAsync();
    }

    /// <summary> Проверяет возможность захвата заявки промокода. </summary>
    private async Task ValidateForClaimAsync(
        PaymentCreationOperation operation,
        PromoCodeApplication application,
        Organization organization)
    {
        if (application.OrganizationId != organization.Id || application.OrganizationId != operation.OrganizationId)
        {
            throw new InvalidOperationException("Заявка промокода принадлежит другой организации.");
        }

        // Организация должна быть активной, не удалённой и не архивной.
        if (!organization.IsActive || organization.IsDeleted || organization.IsArchived)
        {
            throw new InvalidOperationException("Организация недоступна для покупки рабочего пространства.");
        }

        await context.Entry(application.PromoCode).ReloadAsync();
        var promo = application.PromoCode;

        if (application.PromoCodeId != operation.PromoCodeId
            || !string.Equals(application.PromoCode.Code, operation.PromoCodeSnapshot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Промокод операции не совпадает со снимком заявки.");
        }

        // Только организационные промокоды участвуют в захвате заявки.
        if (!promo.IsOrganizationCode)
        {
            throw new InvalidOperationException("Промокод не является организационным.");
        }

        // Срок действия кода влияет только на Apply: при захвате проверяем дедлайн закреплённой скидки.
        if (application.DiscountValidUntil is DateTime deadline && deadline <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("Срок использования закреплённой скидки истёк.");
        }

        // Сценарий применимости проверяем по текущему сценарию организации.
        if (!promo.AppliesToAllScenarios
            && !await context.PromoCodeScenarios.AsNoTracking().AnyAsync(s => s.PromoCodeId == promo.Id
                && s.ScenarioCode == organization.PrimaryScenarioCode))
        {
            throw new InvalidOperationException("Промокод не применим к текущему сценарию организации.");
        }

        var plan = await context.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.Id == operation.SubscriptionPlanId)
            .FirstOrDefaultAsync();

        if (plan is null)
        {
            throw new InvalidOperationException("Тариф не найден.");
        }

        if (!plan.IsActive
            || SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode) != SubscriptionPlanCatalogCodes.PlanTypes.Business
            || plan.IsDemoPlan
            || plan.IsDefaultPlan)
        {
            throw new InvalidOperationException("Промокод применим только к действующему бизнес-тарифу.");
        }

        // Явный список тарифов: промокод должен быть привязан к выбранному тарифу.
        if (promo.AppliesToAllPaidPlans
            || !await context.PromoCodePlans.AsNoTracking().AnyAsync(p => p.PromoCodeId == promo.Id
                && p.SubscriptionPlanId == operation.SubscriptionPlanId))
        {
            throw new InvalidOperationException("Промокод не применим к выбранному тарифу.");
        }

        if (operation.OriginalAmount is null || operation.OriginalAmount.Value != plan.EffectivePrice)
        {
            throw new InvalidOperationException("Расчётная исходная цена не совпадает с действующей ценой тарифа.");
        }

        var originalPrice = operation.OriginalAmount.Value;
        var benefitType = application.BenefitTypeCode?.Trim().ToLowerInvariant() ?? string.Empty;

        // Только денежные выгоды могут участвовать в оплате со скидкой.
        if (benefitType != PromoCodeCatalogCodes.DiscountTypes.Percentage
            && benefitType != PromoCodeCatalogCodes.DiscountTypes.FixedAmount)
        {
            throw new InvalidOperationException("Заявка промокода содержит недопустимый тип выгоды.");
        }

        var discountValue = application.DiscountValue;
        if (discountValue is null
            || discountValue.Value <= 0m
            || (benefitType == PromoCodeCatalogCodes.DiscountTypes.Percentage && discountValue.Value > 99m))
        {
            throw new InvalidOperationException("Заявка промокода содержит некорректное значение скидки.");
        }

        var finalPrice = OrganizationPromoPricing.GetFinalPrice(originalPrice, benefitType, discountValue.Value);

        if (finalPrice <= 0m || finalPrice >= originalPrice)
        {
            throw new InvalidOperationException("Снимок выгоды промокода даёт некорректную итоговую цену.");
        }

        if (operation.Amount != finalPrice)
        {
            throw new InvalidOperationException("Итоговая сумма операции не совпадает с расчётом по промокоду.");
        }

        var discount = originalPrice - finalPrice;
        if (operation.PromoDiscountAmount != discount)
        {
            throw new InvalidOperationException("Размер скидки операции не совпадает с расчётом по промокоду.");
        }
    }
}
