using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис административной корректировки подписок. </summary>
internal sealed class AdminSubscriptionAdjustmentService(
    OpenCRMContext opencrmContext,
    AdminSubscriptionMutation mutation)
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Зависимость мутации подписок. </summary>
    private readonly AdminSubscriptionMutation _mutation = mutation;

    /// <summary> Останавливает подписку. </summary>
    public Task<AdminBillingSubscriptionOperationDto> StopSubscriptionAsync(
        string subscriptionScope,
        int subscriptionId,
        string? reason,
        DateTime? expectedEndDate,
        string adminUserId)
    {
        return this._mutation.ExecuteAsync(() =>
            this.StopSubscriptionCoreAsync(subscriptionScope, subscriptionId, reason, expectedEndDate, adminUserId));
    }

    /// <summary> Основная логика остановки подписки. </summary>
    private async Task<AdminBillingSubscriptionOperationDto> StopSubscriptionCoreAsync(
        string subscriptionScope,
        int subscriptionId,
        string? reason,
        DateTime? expectedEndDate,
        string adminUserId)
    {
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 500)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Укажите причину длиной до 500 символов.");
        }

        var normalizedScope = subscriptionScope.Trim().ToLowerInvariant();
        if (!expectedEndDate.HasValue)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Обновите данные подписки перед изменением.");
        }

        var now = DateTime.UtcNow;
        var newEndDate = now.AddTicks(-1);

        if (normalizedScope is "users" or "user")
        {
            var subscription = await this._opencrmContext.UserSubscriptions
                .FirstOrDefaultAsync(item => item.Id == subscriptionId);
            if (subscription == null)
            {
                return AdminBillingOperationResults.SubscriptionMissing("Подписка пользователя не найдена.");
            }

            if (subscription.EndDate < now)
            {
                return AdminBillingOperationResults.SubscriptionInvalid("Подписка уже завершена.");
            }

            if (subscription.EndDate != expectedEndDate.Value)
            {
                return AdminBillingOperationResults.SubscriptionInvalid("Срок подписки изменился. Обновите страницу.");
            }

            var activeSubscriptions = await this._opencrmContext.UserSubscriptions
                .Where(item => item.UserId == subscription.UserId && item.EndDate >= now)
                .ToListAsync();

            foreach (var activeSubscription in activeSubscriptions)
            {
                this._mutation.AddAudit(
                    "users", activeSubscription.Id, activeSubscription.UserId, "stopped", activeSubscription.EndDate,
                    newEndDate, adminUserId, normalizedReason, now);
                activeSubscription.EndDate = newEndDate;
                activeSubscription.AutoRenewal = false;
            }
        }
        else if (normalizedScope is "organizations" or "organization")
        {
            var subscription = await this._opencrmContext.OrganizationSubscriptions
                .FirstOrDefaultAsync(item => item.Id == subscriptionId);
            if (subscription == null)
            {
                return AdminBillingOperationResults.SubscriptionMissing("Подписка организации не найдена.");
            }

            if (subscription.EndDate < now)
            {
                return AdminBillingOperationResults.SubscriptionInvalid("Подписка уже завершена.");
            }

            if (subscription.EndDate != expectedEndDate.Value)
            {
                return AdminBillingOperationResults.SubscriptionInvalid("Срок подписки изменился. Обновите страницу.");
            }

            var activeSubscriptions = await this._opencrmContext.OrganizationSubscriptions
                .Where(item => item.OrganizationId == subscription.OrganizationId && item.EndDate >= now)
                .ToListAsync();

            foreach (var activeSubscription in activeSubscriptions)
            {
                this._mutation.AddAudit(
                    "organizations", activeSubscription.Id, activeSubscription.OrganizationId.ToString(), "stopped", activeSubscription.EndDate,
                    newEndDate, adminUserId, normalizedReason, now);
                activeSubscription.EndDate = newEndDate;
                activeSubscription.AutoRenewal = false;
            }
        }
        else
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Неизвестный тип подписки.");
        }

        await this._opencrmContext.SaveChangesAsync();

        return new AdminBillingSubscriptionOperationDto
        {
            Succeeded = true,
            Message = "Все действующие подписки клиента остановлены."
        };
    }

    /// <summary> Продлевает подписку на заданное число дней. </summary>
    public Task<AdminBillingSubscriptionOperationDto> ExtendSubscriptionAsync(
        string subscriptionScope,
        int subscriptionId,
        int days,
        string? reason,
        DateTime? expectedEndDate,
        string adminUserId)
    {
        return this._mutation.ExecuteAsync(() =>
            this.ExtendSubscriptionCoreAsync(subscriptionScope, subscriptionId, days, reason, expectedEndDate, adminUserId));
    }

    /// <summary> Основная логика продления подписки. </summary>
    private async Task<AdminBillingSubscriptionOperationDto> ExtendSubscriptionCoreAsync(
        string subscriptionScope,
        int subscriptionId,
        int days,
        string? reason,
        DateTime? expectedEndDate,
        string adminUserId)
    {
        if (days <= 0 || days > 366)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Укажите срок продления от 1 до 366 дней.");
        }

        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 500)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Укажите причину длиной до 500 символов.");
        }

        var normalizedScope = subscriptionScope.Trim().ToLowerInvariant();
        if (!expectedEndDate.HasValue)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Обновите данные подписки перед изменением.");
        }

        var now = DateTime.UtcNow;
        DateTime endDate;
        DateTime previousEndDate;
        string clientId;

        if (normalizedScope is "users" or "user")
        {
            var subscription = await this._opencrmContext.UserSubscriptions
                .FirstOrDefaultAsync(item => item.Id == subscriptionId);
            if (subscription == null)
            {
                return AdminBillingOperationResults.SubscriptionMissing("Подписка пользователя не найдена.");
            }

            if (subscription.EndDate != expectedEndDate.Value)
            {
                return AdminBillingOperationResults.SubscriptionInvalid("Срок подписки изменился. Обновите страницу.");
            }

            previousEndDate = subscription.EndDate;
            clientId = subscription.UserId;
            subscription.EndDate = (subscription.EndDate > now ? subscription.EndDate : now).AddDays(days);
            endDate = subscription.EndDate;
        }
        else if (normalizedScope is "organizations" or "organization")
        {
            var subscription = await this._opencrmContext.OrganizationSubscriptions
                .FirstOrDefaultAsync(item => item.Id == subscriptionId);
            if (subscription == null)
            {
                return AdminBillingOperationResults.SubscriptionMissing("Подписка организации не найдена.");
            }

            if (subscription.EndDate != expectedEndDate.Value)
            {
                return AdminBillingOperationResults.SubscriptionInvalid("Срок подписки изменился. Обновите страницу.");
            }

            var hasOtherActiveSubscription = await this._opencrmContext.OrganizationSubscriptions
                .AnyAsync(item => item.OrganizationId == subscription.OrganizationId && item.Id != subscription.Id && item.EndDate >= now);
            if (hasOtherActiveSubscription)
            {
                return AdminBillingOperationResults.SubscriptionInvalid("У клиента несколько подписок. Завершите лишние перед продлением.");
            }

            previousEndDate = subscription.EndDate;
            clientId = subscription.OrganizationId.ToString();
            subscription.EndDate = (subscription.EndDate > now ? subscription.EndDate : now).AddDays(days);
            endDate = subscription.EndDate;
        }
        else
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Неизвестный тип подписки.");
        }

        this._mutation.AddAudit(normalizedScope, subscriptionId, clientId, "extended", previousEndDate, endDate, adminUserId, normalizedReason, now);
        await this._opencrmContext.SaveChangesAsync();

        return new AdminBillingSubscriptionOperationDto
        {
            Succeeded = true,
            Message = $"Подписка продлена до {endDate:dd.MM.yyyy}.",
            EndDate = endDate
        };
    }
}
