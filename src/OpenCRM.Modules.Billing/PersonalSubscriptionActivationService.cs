using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Notifications;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис активации личных подписок после оплаты. </summary>
internal sealed class PersonalSubscriptionActivationService(
    OpenCRMContext context,
    ILogger<PersonalSubscriptionActivationService> logger,
    IPersonalSubscriptionPurchasePolicyService purchasePolicy,
    BillingPaymentTransactionRunner transactionRunner) : IPersonalSubscriptionActivationService
{
    /// <summary> Тип операции покупки подписки. </summary>
    private const string PurchaseOperationType = "yookassa_purchase";

    /// <summary> Тип операции продления подписки. </summary>
    private const string ExtensionOperationType = "yookassa_extension";

    /// <summary> Активирует новую личную подписку после оплаты. </summary>
    public Task<SubscriptionResult> ActivateNewSubscriptionAsync(string userId, int planId, string paymentId) =>
        this.ExecuteAsync(
            userId,
            planId,
            paymentId,
            PurchaseOperationType,
            this.ActivateNewSubscriptionCoreAsync);

    /// <summary> Активирует продление личной подписки после оплаты. </summary>
    public Task<SubscriptionResult> ActivateExtendedSubscriptionAsync(string userId, int planId, string paymentId) =>
        this.ExecuteAsync(
            userId,
            planId,
            paymentId,
            ExtensionOperationType,
            this.ActivateExtendedSubscriptionCoreAsync);

    /// <summary> Выполняет активацию подписки в общей обёртке с транзакцией. </summary>
    private Task<SubscriptionResult> ExecuteAsync(
        string userId,
        int planId,
        string paymentId,
        string operationType,
        Func<SubscriptionPayment, ApplicationUser, Task<SubscriptionResult>> activate) =>
        transactionRunner.ExecuteAsync(transaction =>
            this.ExecuteAttemptAsync(transaction, userId, planId, paymentId, operationType, activate));

    /// <summary> Выполняет одну попытку активации внутри транзакции. </summary>
    private async Task<SubscriptionResult> ExecuteAttemptAsync(
        IDbContextTransaction transaction,
        string userId,
        int planId,
        string paymentId,
        string operationType,
        Func<SubscriptionPayment, ApplicationUser, Task<SubscriptionResult>> activate)
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
        if (payment.UserId != userId || payment.PaymentMethod != operationType || payment.SubscriptionPlanId != planId)
        {
            await transaction.CommitAsync();

            return SubscriptionResult.CreateFailed("Платеж не соответствует пользователю, тарифу или операции.");
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

        if (payment.PaidAt == null || payment.PaymentStatus is not ("pending" or "processing" or "waiting_for_capture" or "paid_waiting_confirmation"))
        {
            await transaction.CommitAsync();

            return SubscriptionResult.CreateFailed("Платеж не подтвержден провайдером.");
        }

        var user = await context.ApplicationUsers
            .FromSqlInterpolated($"SELECT * FROM identity.\"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync();
        if (user == null)
        {
            return await this.FailPaymentAsync(transaction, payment, "Пользователь не найден.");
        }

        var result = await activate(payment, user);
        if (!result.Success)
        {
            return await this.FailPaymentAsync(transaction, payment, result.Error!);
        }

        await context.SaveChangesAsync();
        await context.SubscriptionPayments
            .Where(item => item.Id == payment.Id)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.PaymentStatus, "completed")
                .SetProperty(item => item.StatusUpdatedAt, DateTime.UtcNow));
        await transaction.CommitAsync();
        logger.LogInformation(
            "Личная подписка активирована после оплаты. User: {UserId}, Plan: {PlanId}, Payment: {PaymentId}, Operation: {OperationType}",
            userId,
            planId,
            paymentId,
            operationType);

        return result;
    }

    /// <summary> Активирует новую подписку пользователя по платежу. </summary>
    private async Task<SubscriptionResult> ActivateNewSubscriptionCoreAsync(SubscriptionPayment payment, ApplicationUser user)
    {
        var plan = await context.SubscriptionPlans.FindAsync(payment.SubscriptionPlanId);
        if (plan == null || !plan.IsActive)
        {
            return SubscriptionResult.CreateFailed("Тарифный план не найден или неактивен");
        }

        if (plan.IsDemoPlan)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф нельзя приобрести");
        }

        var canPurchaseResult = await purchasePolicy.CanPurchasePlanAsync(payment.UserId, plan.Id);
        if (!canPurchaseResult.Success)
        {
            return canPurchaseResult;
        }

        var currentSubscription = await context.UserSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(subscription => subscription.UserId == payment.UserId && subscription.EndDate >= DateTime.UtcNow);
        var now = DateTime.UtcNow;
        if (currentSubscription == null)
        {
            var expiredSubscription = await context.UserSubscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(subscription => subscription.UserId == payment.UserId);
            if (expiredSubscription != null)
            {
                await this.DeleteSubscriptionAsync(expiredSubscription.Id);
                this.AddHistoryRecord(expiredSubscription, "expired", "Подписка истекла автоматически", now);
            }
        }
        else
        {
            await this.DeleteSubscriptionAsync(currentSubscription.Id);
        }

        var newSubscription = new UserSubscription
        {
            UserId = payment.UserId,
            SubscriptionPlanId = plan.Id,
            SourceCode = "paid",
            StartDate = now,
            EndDate = now.AddDays(plan.DurationDays),
            AutoRenewal = false
        };
        context.UserSubscriptions.Add(newSubscription);
        this.AddHistoryRecord(
            newSubscription,
            currentSubscription == null ? "created" : "replaced",
            currentSubscription == null
                ? $"Оформлена новая подписка. Платеж: {payment.ExternalPaymentId}"
                : $"Заменен новым тарифом. Платеж: {payment.ExternalPaymentId}");
        TelegramAdminNotificationPublisher.AddPurchase(
            context, payment.UserId, user.Email, plan.Name, payment.Amount, payment.ExternalPaymentId!);

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Активирует продление подписки пользователя по платежу. </summary>
    private async Task<SubscriptionResult> ActivateExtendedSubscriptionCoreAsync(SubscriptionPayment payment, ApplicationUser user)
    {
        var currentSubscription = await context.UserSubscriptions
            .AsNoTracking()
            .Include(subscription => subscription.SubscriptionPlan)
            .FirstOrDefaultAsync(subscription => subscription.UserId == payment.UserId && subscription.EndDate >= DateTime.UtcNow);

        if (currentSubscription == null)
        {
            return SubscriptionResult.CreateFailed("У вас нет активной подписки для продления");
        }

        if (currentSubscription.SubscriptionPlan.IsDemoPlan)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф нельзя продлить");
        }

        if (currentSubscription.SubscriptionPlanId != payment.SubscriptionPlanId)
        {
            return SubscriptionResult.CreateFailed("Платеж не соответствует текущей подписке");
        }

        var oldEndDate = currentSubscription.EndDate;
        if ((oldEndDate - DateTime.UtcNow).TotalDays > 7)
        {
            var daysUntilExpiry = (int)Math.Ceiling((oldEndDate - DateTime.UtcNow).TotalDays);
            return SubscriptionResult.CreateFailed(
                $"Продление доступно только когда до окончания подписки осталось не более 7 дней. Сейчас осталось: {daysUntilExpiry} дней");
        }

        var newEndDate = oldEndDate.AddDays(currentSubscription.SubscriptionPlan.DurationDays);
        this.DetachTrackedSubscription(currentSubscription.Id);
        await context.UserSubscriptions
            .Where(subscription => subscription.Id == currentSubscription.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(subscription => subscription.EndDate, newEndDate));
        currentSubscription.EndDate = newEndDate;
        this.AddHistoryRecord(
            currentSubscription,
            "extended",
            $"Продление подписки. Платеж: {payment.ExternalPaymentId}. "
                + $"Старая дата окончания: {oldEndDate:dd.MM.yyyy}, "
                + $"новая дата: {newEndDate:dd.MM.yyyy}");
        TelegramAdminNotificationPublisher.AddPurchase(
            context,
            payment.UserId,
            user.Email,
            currentSubscription.SubscriptionPlan.Name,
            payment.Amount,
            payment.ExternalPaymentId!);

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Добавляет запись в историю подписки пользователя. </summary>
    private void AddHistoryRecord(UserSubscription subscription, string status, string comment, DateTime? actualEndDate = null)
    {
        context.UserSubscriptionHistory.Add(new UserSubscriptionHistory
        {
            UserId = subscription.UserId,
            SubscriptionPlanId = subscription.SubscriptionPlanId,
            StartDate = subscription.StartDate,
            EndDate = subscription.EndDate,
            ActualEndDate = actualEndDate,
            Status = status,
            AutoRenewal = subscription.AutoRenewal,
            SourceCode = subscription.SourceCode,
            CreatedAt = DateTime.UtcNow,
            Comment = comment
        });

        logger.LogInformation("Создана история подписки {SubscriptionId} со статусом {Status}", subscription.Id, status);
    }

    /// <summary> Удаляет подписку по её идентификатору. </summary>
    private async Task DeleteSubscriptionAsync(int subscriptionId)
    {
        this.DetachTrackedSubscription(subscriptionId);
        await context.UserSubscriptions
            .Where(subscription => subscription.Id == subscriptionId)
            .ExecuteDeleteAsync();
    }

    /// <summary> Отсоединяет отслеживаемую подписку от контекста. </summary>
    private void DetachTrackedSubscription(int subscriptionId)
    {
        var entry = context.ChangeTracker.Entries<UserSubscription>()
            .FirstOrDefault(item => item.Entity.Id == subscriptionId);
        if (entry != null)
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary> Помечает платёж как неуспешный и сохраняет изменения. </summary>
    private async Task<SubscriptionResult> FailPaymentAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
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
