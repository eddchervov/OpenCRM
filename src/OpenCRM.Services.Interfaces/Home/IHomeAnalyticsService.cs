using OpenCRM.Services.Interfaces.Home.Dtos;

namespace OpenCRM.Services.Interfaces.Home;

/// <summary> Контракт сервиса аналитики главной страницы. </summary>
public interface IHomeAnalyticsService
{
    /// <summary> Возвращает аналитику обслуживания для пользователя. </summary>
    Task<HomeAnalyticsDto> GetAsync(string userId, int vehicleCount);
}
