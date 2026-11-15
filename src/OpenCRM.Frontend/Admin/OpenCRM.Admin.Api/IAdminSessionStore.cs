using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

/// <summary> Защищённое хранилище данных для восстановления административной сессии. </summary>
public interface IAdminSessionStore
{
    /// <summary> Загружает сохранённую сессию, если она существует. </summary>
    SavedAdminSession? Load();

    /// <summary> Сохраняет актуальный токен обновления. </summary>
    void Save(SavedAdminSession session);

    /// <summary> Удаляет сохранённую сессию. </summary>
    void Delete();
}
