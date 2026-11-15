using OpenCRM.Services.Interfaces.Subscriptions.Dtos.Admin;

namespace OpenCRM.Services.Interfaces.Subscriptions;

/// <summary> Контракт административного управления тарифными планами. </summary>
public interface ISubscriptionPlanAdminService
{
    /// <summary> Возвращает все тарифные планы. </summary>
    Task<List<SubscriptionPlanAdminDto>> GetAllSubscriptionPlansAsync();

    /// <summary> Возвращает тарифный план по идентификатору. </summary>
    Task<SubscriptionPlanAdminDto?> GetSubscriptionPlanByIdAsync(int id);

    /// <summary> Создаёт тарифный план. </summary>
    Task<SubscriptionPlanAdminDto> CreateSubscriptionPlanAsync(SubscriptionPlanCreateDto createDto);

    /// <summary> Обновляет тарифный план. </summary>
    Task<SubscriptionPlanAdminDto?> UpdateSubscriptionPlanAsync(int id, SubscriptionPlanUpdateDto updateDto);

    /// <summary> Переключает активность тарифного плана. </summary>
    Task<bool> ToggleSubscriptionPlanStatusAsync(int id);

    /// <summary> Активирует тарифный план. </summary>
    Task<bool> ActivateSubscriptionPlanAsync(int id);

    /// <summary> Деактивирует тарифный план. </summary>
    Task<bool> DeactivateSubscriptionPlanAsync(int id);

    /// <summary> Удаляет тарифный план. </summary>
    Task<bool> DeleteSubscriptionPlanAsync(int id);
}

