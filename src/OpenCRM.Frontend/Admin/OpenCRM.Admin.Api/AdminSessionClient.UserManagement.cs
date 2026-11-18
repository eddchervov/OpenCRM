using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    /// <summary> Обновляет доступ пользователя к тестовым подпискам. </summary>
    public Task<AdminOperationResponse> UpdateTestSubscriptionsAsync(
        string userId,
        bool canUseTestSubscriptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Put,
            $"api/v1/admin/users/{Uri.EscapeDataString(userId)}/test-subscriptions",
            new UpdateTestSubscriptionsRequest(canUseTestSubscriptions),
            "Не удалось изменить доступ к тестовым подпискам.",
            cancellationToken);
    }

    /// <summary> Выдаёт пользователю демонстрационную подписку. </summary>
    public Task<AdminOperationResponse> GrantUserDemoAsync(
        string userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/billing/users/{Uri.EscapeDataString(userId)}/demo",
            new SubscriptionActionRequest(reason),
            "Не удалось выдать пользователю демо-доступ.",
            cancellationToken);
    }

    /// <summary> Отзывает демонстрационную подписку пользователя. </summary>
    public Task<AdminOperationResponse> RevokeUserDemoAsync(
        string userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Delete,
            $"api/v1/admin/billing/users/{Uri.EscapeDataString(userId)}/demo",
            new SubscriptionActionRequest(reason),
            "Не удалось отозвать демо-доступ пользователя.",
            cancellationToken);
    }

    /// <summary> Выдаёт организации демонстрационную подписку. </summary>
    public Task<AdminOperationResponse> GrantOrganizationDemoAsync(
        string userId,
        string organizationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/billing/users/{Uri.EscapeDataString(userId)}/organizations/{Uri.EscapeDataString(organizationId)}/demo",
            new SubscriptionActionRequest(reason),
            "Не удалось выдать организации демо-доступ.",
            cancellationToken);
    }

    /// <summary> Отзывает демонстрационную подписку организации. </summary>
    public Task<AdminOperationResponse> RevokeOrganizationDemoAsync(
        string userId,
        string organizationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Delete,
            $"api/v1/admin/billing/users/{Uri.EscapeDataString(userId)}/organizations/{Uri.EscapeDataString(organizationId)}/demo",
            new SubscriptionActionRequest(reason),
            "Не удалось отозвать демо-доступ организации.",
            cancellationToken);
    }

    /// <summary> Загружает тарифы, доступные для назначения. </summary>
    public Task<IReadOnlyList<AdminAvailablePlan>> GetAvailablePlansAsync(
        CancellationToken cancellationToken = default)
    {
        return this.GetAuthorizedReportAsync<IReadOnlyList<AdminAvailablePlan>>(
            "api/v1/admin/billing/available-plans",
            "Не удалось загрузить доступные тарифы.",
            cancellationToken);
    }

    /// <summary> Назначает пользователю или организации выбранный тариф. </summary>
    public Task<AdminOperationResponse> GrantPlanAsync(
        string scope,
        string targetId,
        int planId,
        int days,
        string reason,
        CancellationToken cancellationToken = default)
    {
        this.ValidateSubscriptionScope(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var path = scope == "users"
            ? $"api/v1/admin/billing/users/{Uri.EscapeDataString(targetId)}/plan"
            : $"api/v1/admin/billing/organizations/{Uri.EscapeDataString(targetId)}/plan";

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Post,
            path,
            new GrantSubscriptionRequest(planId, days, reason),
            "Не удалось назначить тариф.",
            cancellationToken);
    }

    /// <summary> Продлевает подписку пользователя или организации. </summary>
    public Task<AdminOperationResponse> ExtendSubscriptionAsync(
        string scope,
        int subscriptionId,
        int days,
        string reason,
        DateTime? expectedEndDate,
        CancellationToken cancellationToken = default)
    {
        this.ValidateSubscriptionScope(scope);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subscriptionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/billing/subscriptions/{scope}/{subscriptionId}/extend",
            new ExtendSubscriptionRequest(days, reason, expectedEndDate),
            "Не удалось продлить подписку.",
            cancellationToken);
    }

    /// <summary> Останавливает подписку пользователя или организации. </summary>
    public Task<AdminOperationResponse> StopSubscriptionAsync(
        string scope,
        int subscriptionId,
        string reason,
        DateTime? expectedEndDate,
        CancellationToken cancellationToken = default)
    {
        this.ValidateSubscriptionScope(scope);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/billing/subscriptions/{scope}/{subscriptionId}/stop",
            new SubscriptionActionRequest(reason, expectedEndDate),
            "Не удалось остановить подписку.",
            cancellationToken);
    }

    private void ValidateSubscriptionScope(string scope)
    {
        if (scope is not ("users" or "organizations"))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), this.LocalizeFallback("Область подписки должна быть users или organizations."));
        }
    }

    private sealed record UpdateTestSubscriptionsRequest(bool CanUseTestSubscriptions);

    private sealed record SubscriptionActionRequest(string Reason, DateTime? ExpectedEndDate = null);

    private sealed record GrantSubscriptionRequest(int PlanId, int Days, string Reason);

    private sealed record ExtendSubscriptionRequest(int Days, string Reason, DateTime? ExpectedEndDate);
}
