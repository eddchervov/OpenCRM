using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.Services.Interfaces.Users;

/// <summary> Контракт сервиса работы с пользователями. </summary>
public interface IUsersService
{
    /// <summary> Записывает дату последнего входа в приложение. </summary>
    Task RecordLastDateOfEntry(string userId);

    /// <summary> Записывает дату последнего входа пользователя. </summary>
    Task RecordLastLoginDate(string userId);

    /// <summary> Записывает успешную авторизацию и её способ. </summary>
    Task RecordLastLoginDate(string userId, string authenticationMethod);

    /// <summary> Обновляет сведения о посещении с действующей сессией. </summary>
    Task RecordLastAccessAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary> Возвращает личную статистику пользователя. </summary>
    Task<UserPersonalStatsDto> GetPersonalStatsAsync(string userId);

    /// <summary> Проверяет, удалялся ли ранее аккаунт с этой почтой. </summary>
    Task<bool> HasDeletedOriginalEmailAsync(string? email);
}
