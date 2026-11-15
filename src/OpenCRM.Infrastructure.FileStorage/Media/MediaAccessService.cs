using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Services.Interfaces.Media;

namespace OpenCRM.Infrastructure.FileStorage.Media;

/// <summary> Сервис доступа к медиафайлам. Актуальные права доступа проверяются каждым запросом, а не сохраняются в кеш. </summary>
public sealed class MediaAccessService : IMediaAccessService
{
    /// <summary> Зависимость репозитория медиафайлов. </summary>
    private readonly IGenericMediaRepository _genericMediaRepository;

    /// <summary> Создаёт сервис с репозиторием медиафайлов. </summary>
    public MediaAccessService(IGenericMediaRepository genericMediaRepository)
    {
        this._genericMediaRepository = genericMediaRepository;
    }

    /// <summary> Возвращает путь медиафайла, доступного пользователю. </summary>
    public Task<string?> GetMediaPathAsync(string userId, Guid mediaId)
    {
        return this._genericMediaRepository.GetMediaPathAsync(userId, mediaId);
    }

    /// <summary> Проверяет, имеет ли пользователь доступ к медиафайлу. </summary>
    public Task<bool> UserHasAccessToMediaAsync(string userId, Guid mediaId)
    {
        return this._genericMediaRepository.UserHasAccessToMediaAsync(userId, mediaId);
    }
}