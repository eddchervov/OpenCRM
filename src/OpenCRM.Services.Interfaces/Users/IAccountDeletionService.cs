using OpenCRM.Domain.Identity;

namespace OpenCRM.Services.Interfaces.Users;

/// <summary> Контракт сервиса удаления учётной записи. </summary>
public interface IAccountDeletionService
{
    /// <summary> Удаляет учётную запись пользователя, возвращая ошибку при неудаче. </summary>
    Task<string?> DeleteAsync(ApplicationUser user);
}
