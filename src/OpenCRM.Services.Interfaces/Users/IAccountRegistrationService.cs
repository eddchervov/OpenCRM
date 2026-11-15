using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.Services.Interfaces.Users;

/// <summary> Контракт сервиса регистрации учётных записей. </summary>
public interface IAccountRegistrationService
{
    /// <summary> Создаёт учётную запись по запросу регистрации. </summary>
    Task<AccountCreationResult> CreateAsync(AccountRegistrationRequest request);
}
