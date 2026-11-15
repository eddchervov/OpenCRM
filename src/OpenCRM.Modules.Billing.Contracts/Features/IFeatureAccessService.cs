using OpenCRM.Services.Interfaces.Features.Dtos;

namespace OpenCRM.Services.Interfaces.Features;

/// <summary> Контракт доступа пользователя к функциям тарифа. </summary>
public interface IFeatureAccessService
{
    /// <summary> Проверяет доступность функции для пользователя. </summary>
    Task<bool> HasFeatureAsync(string userId, string featureCode);

    /// <summary> Возвращает целочисленное значение функции для пользователя. </summary>
    Task<int> GetIntValueAsync(string userId, string featureCode, int defaultValue = 0);

    /// <summary> Возвращает снимок функций пользователя. </summary>
    Task<FeatureAccessSnapshotDto> GetSnapshotAsync(string userId);
}

