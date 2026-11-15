using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    /// <summary> Загружает промокоды. </summary>
    public Task<IReadOnlyList<AdminPromoCode>> GetPromoCodesAsync(CancellationToken cancellationToken = default)
    {
        return this.GetAuthorizedReportAsync<IReadOnlyList<AdminPromoCode>>(
            "api/v1/admin/promo-codes",
            "Не удалось загрузить промокоды.",
            cancellationToken);
    }

    /// <summary> Загружает историю применений промокода. </summary>
    public Task<IReadOnlyList<AdminPromoCodeUsage>> GetPromoCodeUsagesAsync(
        int promoCodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(promoCodeId);

        return this.GetAuthorizedReportAsync<IReadOnlyList<AdminPromoCodeUsage>>(
            $"api/v1/admin/promo-codes/{promoCodeId}/usages",
            "Не удалось загрузить историю применений промокода.",
            cancellationToken);
    }

    /// <summary> Создаёт организационный промокод. </summary>
    public Task<AdminPromoCode> CreatePromoCodeAsync(
        AdminPromoCodeSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return this.SendAuthorizedRequestAsync<AdminPromoCode>(
            HttpMethod.Post,
            "api/v1/admin/promo-codes",
            request,
            "Не удалось создать промокод.",
            cancellationToken);
    }

    /// <summary> Сохраняет изменения промокода. </summary>
    public Task<AdminPromoCode> UpdatePromoCodeAsync(
        int promoCodeId,
        AdminPromoCodeSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(promoCodeId);
        ArgumentNullException.ThrowIfNull(request);

        return this.SendAuthorizedRequestAsync<AdminPromoCode>(
            HttpMethod.Put,
            $"api/v1/admin/promo-codes/{promoCodeId}",
            request,
            "Не удалось сохранить промокод.",
            cancellationToken);
    }

    /// <summary> Меняет активность промокода. </summary>
    public Task<AdminEmptyResponse> SetPromoCodeStatusAsync(
        int promoCodeId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(promoCodeId);

        return this.SendAuthorizedRequestAsync<AdminEmptyResponse>(
            HttpMethod.Patch,
            $"api/v1/admin/promo-codes/{promoCodeId}/status",
            new SetPromoCodeStatusRequest(isActive),
            "Не удалось изменить статус промокода.",
            cancellationToken);
    }

    /// <summary> Удаляет промокод без применений. </summary>
    public Task<AdminEmptyResponse> DeletePromoCodeAsync(
        int promoCodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(promoCodeId);

        return this.SendAuthorizedRequestAsync<AdminEmptyResponse>(
            HttpMethod.Delete,
            $"api/v1/admin/promo-codes/{promoCodeId}",
            null,
            "Не удалось удалить промокод.",
            cancellationToken);
    }

    private sealed record SetPromoCodeStatusRequest(bool IsActive);
}
