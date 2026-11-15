using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.Services.Interfaces.Users;

/// <summary> Контракт сервиса входа по коду из письма. </summary>
public interface IEmailCodeLoginService
{
    /// <summary> Отправляет код для входа на указанную почту. </summary>
    Task<EmailCodeLoginResult> SendAsync(EmailCodeLoginRequest request);

    /// <summary> Проверяет код входа и при успехе выполняет вход. </summary>
    Task<EmailCodeLoginResult> VerifyAsync(EmailCodeLoginRequest request, RegistrationAttributionData? attribution);
}
