using OpenCRM.Services.Interfaces.Features.Dtos;

namespace OpenCRM.Services.Interfaces.Features;

/// <summary> Контракт административного управления функциональными возможностями. </summary>
public interface IFeatureAdminService
{
    /// <summary> Обеспечивает наличие стандартных функциональных возможностей. </summary>
    Task EnsureDefaultsAsync();

    /// <summary> Возвращает список описаний функциональных возможностей. </summary>
    Task<List<FeatureDefinitionDto>> GetDefinitionsAsync();

    /// <summary> Возвращает описание функциональной возможности по идентификатору. </summary>
    Task<FeatureDefinitionDto?> GetDefinitionByIdAsync(int id);

    /// <summary> Создаёт описание функциональной возможности. </summary>
    Task<FeatureDefinitionDto> CreateDefinitionAsync(FeatureDefinitionUpsertDto dto);

    /// <summary> Обновляет описание функциональной возможности. </summary>
    Task<FeatureDefinitionDto?> UpdateDefinitionAsync(int id, FeatureDefinitionUpsertDto dto);

    /// <summary> Переключает статус активности функциональной возможности. </summary>
    Task<bool> ToggleDefinitionStatusAsync(int id);

    /// <summary> Возвращает привязки функциональных возможностей к тарифному плану. </summary>
    Task<List<PlanFeatureAssignmentDto>> GetPlanFeaturesAsync(int planId);

    /// <summary> Сохраняет привязки функциональных возможностей к тарифному плану. </summary>
    Task SavePlanFeaturesAsync(int planId, SavePlanFeaturesDto dto);

    /// <summary> Возвращает права пользователя на функциональные возможности. </summary>
    Task<List<UserFeatureGrantDto>> GetUserFeatureGrantsAsync(string userId);

    /// <summary> Создаёт право пользователя на функциональную возможность. </summary>
    Task<UserFeatureGrantDto> CreateUserFeatureGrantAsync(string userId, string grantedByUserId, CreateUserFeatureGrantDto dto);

    /// <summary> Отзывает право пользователя на функциональную возможность. </summary>
    Task<bool> RevokeUserFeatureGrantAsync(int grantId);
}

