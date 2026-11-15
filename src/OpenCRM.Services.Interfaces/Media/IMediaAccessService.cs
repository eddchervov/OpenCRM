namespace OpenCRM.Services.Interfaces.Media;

/// <summary> Проверка актуального доступа к медиафайлам и получение доступного пути. </summary>
public interface IMediaAccessService
{
    /// <summary> Проверяет доступ пользователя к медиафайлу. </summary>
    Task<bool> UserHasAccessToMediaAsync(string userId, Guid mediaId);

    /// <summary> Возвращает путь медиафайла, доступного пользователю. </summary>
    Task<string?> GetMediaPathAsync(string userId, Guid mediaId);
}
