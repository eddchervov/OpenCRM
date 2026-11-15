using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.Services.Interfaces.Users;

/// <summary> Предоставляет сведения о текущем клиенте без зависимости от HTTP в модуле аккаунтов. </summary>
public interface IUserAccessInfoAccessor
{
    /// <summary> Возвращает сведения о запросе либо null вне HTTP-запроса или при имперсонации. </summary>
    UserAccessInfoDto? GetCurrent();
}
