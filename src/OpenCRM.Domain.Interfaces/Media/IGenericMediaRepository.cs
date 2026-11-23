using OpenCRM.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Domain.Interfaces.Media;

/// <summary> Контракт универсального репозитория медиафайлов. </summary>
public interface IGenericMediaRepository
{
    /// <summary> Находит медиафайл заданного типа по идентификатору. </summary>
    Task<T?> GetByIdAsync<T>(string? userId, Guid mediaId, bool includeDeleted = false) where T : BaseMedia;

    /// <summary> Возвращает путь медиафайла по его идентификатору. </summary>
    Task<string?> GetMediaPathAsync(string? userId, Guid mediaId, bool includeDeleted = false);

    /// <summary> Проверяет доступ пользователя к медиафайлу. </summary>
    Task<bool> UserHasAccessToMediaAsync(string? userId, Guid mediaId);

    /// <summary> Возвращает идентификаторы медиафайлов пользователя с фильтром по типу. </summary>
    Task<IEnumerable<Guid>> GetMediaIdsByUserAsync(string? userId, MediaType? type = null, bool includeDeleted = false);

    // Методы для системных файлов
    /// <summary> Возвращает системные медиафайлы заданного типа. </summary>
    Task<IEnumerable<T>> GetSystemMediaAsync<T>(MediaType? type = null, bool includeDeleted = false) where T : BaseMedia;
}
