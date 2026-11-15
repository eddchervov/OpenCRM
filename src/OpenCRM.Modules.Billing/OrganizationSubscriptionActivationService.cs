using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Notifications;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис активации подписок организаций. </summary>
internal sealed class OrganizationSubscriptionActivationService(
    OpenCRMContext context,
    IFeatureDefaultsInitializer featureDefaultsInitializer,
    IOrganizationPermissionService organizationPermissionService,
    IOrganizationSubscriptionPurchasePolicyService purchasePolicy,
    IOrganizationAuditService organizationAuditService,
    IPromotionPaymentCoordinator promotionPaymentCoordinator,
    ILogger<OrganizationSubscriptionActivationService> logger,
    BillingPaymentTransactionRunner transactionRunner) : IOrganizationSubscriptionActivationService
{
    /// <summary> Префикс типа операции покупки. </summary>
    private const string PurchaseOperationPrefix = "yookassa_workspace_purchase:";

    /// <summary> Префикс типа операции продления. </summary>
    private const string ExtensionOperationPrefix = "yookassa_workspace_extension:";

    /// <summary> Код источника промо-доступа. </summary>
    private const string PromoSourceCode = "promo";

    /// <summary> Активирует новую подписку организации после оплаты. </summary>
    public Task<SubscriptionResult> ActivateNewSubscriptionAsync(string userId, Guid organizationId, int planId, string paymentId) =>
        this.ExecuteAsync(
            userId,
            organizationId,
            planId,
            paymentId,
            OrganizationSubscriptionActivationService.BuildPurchaseOperationType(organizationId),
            this.ActivateNewSubscriptionCoreAsync);

    /// <summary> Активирует продление подписки организации после оплаты. </summary>
    public Task<SubscriptionResult> ActivateExtendedSubscriptionAsync(string userId, Guid organizationId, int planId, string paymentId) =>
        this.ExecuteAsync(
            userId,
            organizationId,
            planId,
            paymentId,
            OrganizationSubscriptionActivationService.BuildExtensionOperationType(organizationId),
            this.ActivateExtendedSubscriptionCoreAsync);

    /// <summary> Выполняет активацию подписки заданного типа. </summary>
    private Task<SubscriptionResult> ExecuteAsync(
        string userId,
        Guid organizationId,
        int planId,
        string paymentId,
        string operationType,
        Func<SubscriptionPayment, Organization, Task<SubscriptionResult>> activate) =>
        transactionRunner.ExecuteAsync(transaction =>
            this.ExecuteAttemptAsync(transaction, userId, organizationId, planId, paymentId, operationType, activate));

    /// <summary> Выполняет попытку активации в транзакции. </summary>
    private async Task<SubscriptionResult> ExecuteAttemptAsync(
        IDbContextTransaction transaction,
        string userId,
        Guid organizationId,
        int planId,
        string paymentId,
        string operationType,
        Func<SubscriptionPayment, Organization, Task<SubscriptionResult>> activate)
    {
        var payments = await context.SubscriptionPayments
            .FromSqlInterpolated($"SELECT * FROM \"SubscriptionPayments\" WHERE \"ExternalPaymentId\" = {paymentId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync();

        if (payments.Count != 1)
        {
            await transaction.CommitAsync();

            return SubscriptionResult.CreateFailed("Платеж не найден или не может быть однозначно определен.");
        }

        var payment = payments[0];
        if (payment.UserId != userId
            || payment.SubscriptionPlanId != planId
            || !string.Equals(payment.PaymentMethod, operationType, StringComparison.OrdinalIgnoreCase))
        {
            await transaction.CommitAsync();

            return SubscriptionResult.CreateFailed("Платеж не соответствует пользователю, организации, тарифу или операции.");
        }

        if (payment.PaymentStatus == "completed")
        {
            await transaction.CommitAsync();

            return SubscriptionResult.CreateSuccess();
        }

        if (payment.PaymentStatus is "canceled" or "failed" or "refunded")
        {
            await transaction.CommitAsync();

            return SubscriptionResult.CreateFailed("Платеж уже закрыт и не может быть обработан повторно.");
        }

        if (payment.PaidAt == null
            || payment.PaymentStatus is not ("pending" or "processing" or "waiting_for_capture" or "paid_waiting_confirmation"))
        {
            await transaction.CommitAsync();

            return SubscriptionResult.CreateFailed("Платеж не подтвержден провайдером.");
        }

        var organization = await this.LockOrganizationAsync(organizationId);
        var accessError = await this.GetBillingAccessErrorAsync(userId, organizationId, organization);
        if (accessError != null)
        {
            return await this.FailPaymentAsync(transaction, payment, accessError);
        }

        var result = await activate(payment, organization!);
        if (!result.Success)
        {
            return await this.FailPaymentAsync(transaction, payment, result.Error!);
        }

        // Погашение промо-скидки в той же транзакции, что и активация тарифа.
        if (payment.PromoCodeApplicationId != null)
        {
            await promotionPaymentCoordinator.ConsumeAsync(payment, organizationId);
        }

        await context.SaveChangesAsync();
        await context.SubscriptionPayments
            .Where(item => item.Id == payment.Id)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.PaymentStatus, "completed")
                .SetProperty(item => item.StatusUpdatedAt, DateTime.UtcNow));
        await transaction.CommitAsync();
        logger.LogInformation(
            "Оплачен тариф рабочего пространства. User: {UserId}, Organization: {OrganizationId}, Plan: {PlanId}, Payment: {PaymentId}",
            userId,
            organizationId,
            planId,
            paymentId);

        return result;
    }

    /// <summary> Основная логика активации новой подписки. </summary>
    private async Task<SubscriptionResult> ActivateNewSubscriptionCoreAsync(SubscriptionPayment payment, Organization organization)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();
        var plan = await context.SubscriptionPlans.FindAsync(payment.SubscriptionPlanId);
        if (plan == null || !plan.IsActive)
        {
            return SubscriptionResult.CreateFailed("Бизнес-тариф не найден или недоступен.");
        }

        var canPurchaseResult = await purchasePolicy.CanPurchasePlanAsync(payment.UserId, organization.Id, plan.Id);
        if (!canPurchaseResult.Success)
        {
            return canPurchaseResult;
        }

        var now = DateTime.UtcNow;

        // Подарочный остаток переносится на оплаченный период; чистый демо-доступ не переносится.
        var promoSubscription = await context.OrganizationSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.OrganizationId == organization.Id
                && subscription.SourceCode == PromoSourceCode
                && subscription.EndDate > now)
            .OrderByDescending(subscription => subscription.EndDate)
            .Select(subscription => new
            {
                subscription.EndDate,
                subscription.PromoDaysStartDate,
                subscription.StartDate
            })
            .FirstOrDefaultAsync();

        var giftRemaining = TimeSpan.Zero;
        if (promoSubscription != null)
        {
            var giftStart = promoSubscription.PromoDaysStartDate ?? promoSubscription.StartDate;
            var giftReference = now > giftStart ? now : giftStart;
            var remaining = promoSubscription.EndDate - giftReference;
            if (remaining > TimeSpan.Zero)
            {
                giftRemaining = remaining;
            }
        }

        this.DetachTrackedOrganizationSubscriptions(organization.Id);
        await context.OrganizationSubscriptions
            .Where(subscription => subscription.OrganizationId == organization.Id && subscription.EndDate >= now)
            .ExecuteUpdateAsync(update => update.SetProperty(subscription => subscription.EndDate, now));

        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = plan.Id,
            SourceCode = "paid",
            StartDate = now,
            EndDate = now.AddDays(plan.DurationDays).Add(giftRemaining),
            AutoRenewal = false,
            PromoDaysStartDate = null
        });

        var payerEmail = await context.ApplicationUsers
            .Where(user => user.Id == payment.UserId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync();

        TelegramAdminNotificationPublisher.AddPurchase(
            context, payment.UserId, payerEmail, plan.Name, payment.Amount, payment.ExternalPaymentId!, organization.Id);
        await this.AddBillingAuditAsync(
            organization.Id,
            payment.UserId,
            "billing.subscription.activated",
            "Тариф активирован",
            $"{plan.Name}, платеж {payment.ExternalPaymentId}",
            plan.Id);

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Основная логика активации продления подписки. </summary>
    private async Task<SubscriptionResult> ActivateExtendedSubscriptionCoreAsync(SubscriptionPayment payment, Organization organization)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();

        // Признак ранее подготовленного продления проверяем один раз до выборок текущего тарифа.
        var expectedOperationType = OrganizationSubscriptionActivationService.BuildExtensionOperationType(organization.Id);
        var isTrustedPreparedExtension = await context.PaymentCreationOperations
            .AsNoTracking()
            .AnyAsync(operation => operation.SubscriptionPaymentId == payment.Id
                && operation.UserId == payment.UserId
                && operation.SubscriptionPlanId == payment.SubscriptionPlanId
                && operation.OperationType == expectedOperationType);

        var currentSubscription = await context.OrganizationSubscriptions
            .AsNoTracking()
            .Include(subscription => subscription.SubscriptionPlan)
            .Where(subscription => subscription.OrganizationId == organization.Id && subscription.EndDate >= DateTime.UtcNow)
            .OrderByDescending(subscription => subscription.EndDate)
            .FirstOrDefaultAsync();

        if (currentSubscription == null)
        {
            // Подтвержденный подготовленный платеж активируется по правилам покупки, чтобы не терять оплату.
            if (isTrustedPreparedExtension)
            {
                return await this.ActivateNewSubscriptionCoreAsync(payment, organization);
            }

            return SubscriptionResult.CreateFailed("У рабочего пространства нет активного тарифа для продления.");
        }

        if (currentSubscription.SubscriptionPlan.IsDemoPlan)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф нельзя продлить.");
        }

        if (currentSubscription.SubscriptionPlan.IsDefaultPlan)
        {
            return SubscriptionResult.CreateFailed("Базовый тариф не продлевается.");
        }

        // Подарочный доступ не продлевается: при доверенном подготовленном платеже
        // сохраняем подарочный остаток через активацию оплаченного тарифа.
        if (currentSubscription.SourceCode == PromoSourceCode)
        {
            if (isTrustedPreparedExtension)
            {
                return await this.ActivateNewSubscriptionCoreAsync(payment, organization);
            }

            return SubscriptionResult.CreateFailed("Подарочный доступ не продлевается.");
        }

        if (currentSubscription.SubscriptionPlanId != payment.SubscriptionPlanId)
        {
            return SubscriptionResult.CreateFailed("Платеж не соответствует текущему тарифу рабочего пространства.");
        }

        var oldEndDate = currentSubscription.EndDate;
        if ((oldEndDate - DateTime.UtcNow).TotalDays > 7 && !isTrustedPreparedExtension)
        {
            var daysUntilExpiry = (int)Math.Ceiling((oldEndDate - DateTime.UtcNow).TotalDays);

            return SubscriptionResult.CreateFailed(
                $"Продление откроется за 7 дней до окончания. Осталось дней: {daysUntilExpiry}.");
        }

        var newEndDate = oldEndDate.AddDays(currentSubscription.SubscriptionPlan.DurationDays);
        this.DetachTrackedOrganizationSubscription(currentSubscription.Id);
        await context.OrganizationSubscriptions
            .Where(subscription => subscription.Id == currentSubscription.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(subscription => subscription.EndDate, newEndDate));

        var payerEmail = await context.ApplicationUsers
            .Where(user => user.Id == payment.UserId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync();

        TelegramAdminNotificationPublisher.AddPurchase(
            context,
            payment.UserId,
            payerEmail,
            currentSubscription.SubscriptionPlan.Name,
            payment.Amount,
            payment.ExternalPaymentId!,
            organization.Id);
        await this.AddBillingAuditAsync(
            organization.Id,
            payment.UserId,
            "billing.subscription.extended",
            "Тариф продлен",
            $"{currentSubscription.SubscriptionPlan.Name}, платеж {payment.ExternalPaymentId}",
            currentSubscription.SubscriptionPlanId);

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Блокирует запись организации для транзакции. </summary>
    private async Task<Organization?> LockOrganizationAsync(Guid organizationId) =>
        await context.Organizations
            .FromSqlInterpolated($"SELECT * FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync();

    /// <summary> Отсоединяет отслеживаемые подписки организации. </summary>
    private void DetachTrackedOrganizationSubscriptions(Guid organizationId)
    {
        var entries = context.ChangeTracker.Entries<OrganizationSubscription>()
            .Where(entry => entry.Entity.OrganizationId == organizationId)
            .ToArray();

        foreach (var entry in entries)
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary> Отсоединяет отслеживаемую подписку по идентификатору. </summary>
    private void DetachTrackedOrganizationSubscription(int subscriptionId)
    {
        var entry = context.ChangeTracker.Entries<OrganizationSubscription>()
            .FirstOrDefault(item => item.Entity.Id == subscriptionId);

        if (entry != null)
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary> Определяет ошибку доступа пользователя к биллингу организации. </summary>
    private async Task<string?> GetBillingAccessErrorAsync(string userId, Guid organizationId, Organization? organization)
    {
        if (organization == null || !organization.IsActive || organization.IsDeleted || organization.IsArchived)
        {
            return "Организация не найдена или доступ закрыт.";
        }

        var isMember = await context.OrganizationMembers
            .AnyAsync(member => member.OrganizationId == organizationId && member.UserId == userId);

        if (!isMember)
        {
            return "Организация не найдена или доступ закрыт.";
        }

        if (!await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.BillingManage))
        {
            return OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.BillingManage);
        }

        return null;
    }

    /// <summary> Добавляет запись аудита биллинга организации. </summary>
    private Task AddBillingAuditAsync(
        Guid organizationId,
        string userId,
        string eventType,
        string title,
        string description,
        int planId) =>
        organizationAuditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = eventType,
            Title = title,
            Description = description,
            TargetType = "subscription_plan",
            TargetId = planId.ToString()
        }, saveChanges: false);

    /// <summary> Формирует тип операции покупки подписки. </summary>
    private static string BuildPurchaseOperationType(Guid organizationId) => $"{PurchaseOperationPrefix}{organizationId:D}";

    /// <summary> Формирует тип операции продления подписки. </summary>
    private static string BuildExtensionOperationType(Guid organizationId) => $"{ExtensionOperationPrefix}{organizationId:D}";

    /// <summary> Помечает платёж как неуспешный. </summary>
    private async Task<SubscriptionResult> FailPaymentAsync(
        IDbContextTransaction transaction,
        SubscriptionPayment payment,
        string error)
    {
        await context.SubscriptionPayments
            .Where(item => item.Id == payment.Id)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.PaymentStatus, "failed")
                .SetProperty(item => item.StatusUpdatedAt, DateTime.UtcNow));
        await transaction.CommitAsync();

        return SubscriptionResult.CreateFailed(error);
    }
}
