using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Доступ к данным текущего администратора. </summary>
public interface IAdminCurrentUserService
{
    /// <summary> Возвращает данные администратора по идентификатору пользователя. </summary>
    Task<AdminCurrentUserDto?> GetAsync(string userId);
}
