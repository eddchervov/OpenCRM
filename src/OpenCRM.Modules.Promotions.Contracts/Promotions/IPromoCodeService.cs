using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Services.Interfaces.Promotions;

/// <summary> Контракт сервиса управления промокодами. </summary>
public interface IPromoCodeService
{
    /// <summary> Рассчитывает цену тарифа с учётом промокода. </summary>
    Task<PromotionPriceDto> CalculatePriceAsync(string userId, Guid organizationId, int planId, decimal originalPrice);

    /// <summary> Возвращает все промокоды. </summary>
    Task<IReadOnlyList<PromoCodeAdminDto>> GetAllAsync();

    /// <summary> Возвращает промокод по идентификатору. </summary>
    Task<PromoCodeAdminDto?> GetByIdAsync(int id);

    /// <summary> Создаёт промокод. </summary>
    Task<PromoCodeAdminDto> CreateAsync(PromoCodeSaveDto dto);

    /// <summary> Обновляет промокод по идентификатору. </summary>
    Task<PromoCodeAdminDto?> UpdateAsync(int id, PromoCodeSaveDto dto);

    /// <summary> Изменяет активность промокода. </summary>
    Task<bool> SetActiveAsync(int id, bool isActive);

    /// <summary> Удаляет промокод по идентификатору. </summary>
    Task<bool> DeleteAsync(int id);

    /// <summary> Возвращает использования промокода. </summary>
    Task<IReadOnlyList<PromoCodeUsageDto>> GetUsagesAsync(int id);
}
