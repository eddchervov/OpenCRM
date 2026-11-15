using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    /// <summary> Загружает все тарифы для управления. </summary>
    public Task<IReadOnlyList<AdminSubscriptionPlan>> GetSubscriptionPlansAsync(
        CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<IReadOnlyList<AdminSubscriptionPlan>>(
            "api/v1/admin/subscription-plans",
            "Не удалось загрузить тарифы.",
            cancellationToken);

    /// <summary> Создаёт тариф. </summary>
    public Task<AdminSubscriptionPlan> CreateSubscriptionPlanAsync(
        AdminSubscriptionPlanForm form,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);

        return this.SendAuthorizedRequestAsync<AdminSubscriptionPlan>(
            HttpMethod.Post,
            "api/v1/admin/subscription-plans",
            form,
            "Не удалось создать тариф.",
            cancellationToken);
    }

    /// <summary> Сохраняет поля тарифа. </summary>
    public Task<AdminSubscriptionPlan> UpdateSubscriptionPlanAsync(
        int planId,
        AdminSubscriptionPlanForm form,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);
        ArgumentNullException.ThrowIfNull(form);

        return this.SendAuthorizedRequestAsync<AdminSubscriptionPlan>(
            HttpMethod.Put,
            $"api/v1/admin/subscription-plans/{planId}",
            form,
            "Не удалось сохранить тариф.",
            cancellationToken);
    }

    /// <summary> Переключает активность тарифа. </summary>
    public Task<AdminOperationResponse> ToggleSubscriptionPlanAsync(
        int planId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Patch,
            $"api/v1/admin/subscription-plans/{planId}/toggle-status",
            null,
            "Не удалось изменить статус тарифа.",
            cancellationToken);
    }

    /// <summary> Удаляет тариф. </summary>
    public Task<AdminOperationResponse> DeleteSubscriptionPlanAsync(
        int planId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Delete,
            $"api/v1/admin/subscription-plans/{planId}",
            null,
            "Не удалось удалить тариф.",
            cancellationToken);
    }

    /// <summary> Загружает возможности тарифа. </summary>
    public Task<IReadOnlyList<AdminPlanFeature>> GetPlanFeaturesAsync(
        int planId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);

        return this.GetAuthorizedReportAsync<IReadOnlyList<AdminPlanFeature>>(
            $"api/v1/admin/subscription-plans/{planId}/features",
            "Не удалось загрузить возможности тарифа.",
            cancellationToken);
    }

    /// <summary> Сохраняет возможности тарифа для всех сроков оплаты. </summary>
    public Task<AdminOperationResponse> SavePlanFeaturesAsync(
        int planId,
        IReadOnlyList<AdminPlanFeature> features,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);
        ArgumentNullException.ThrowIfNull(features);

        var request = new SavePlanFeaturesRequest(features.Select(feature => new SavePlanFeatureRequest(
            feature.FeatureId,
            feature.IsAssigned,
            feature.BoolValue,
            feature.IntValue)).ToArray());

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Put,
            $"api/v1/admin/subscription-plans/{planId}/features",
            request,
            "Не удалось сохранить возможности тарифа.",
            cancellationToken);
    }

    private sealed record SavePlanFeaturesRequest(IReadOnlyList<SavePlanFeatureRequest> Features);

    private sealed record SavePlanFeatureRequest(int FeatureId, bool IsAssigned, bool? BoolValue, int? IntValue);
}
