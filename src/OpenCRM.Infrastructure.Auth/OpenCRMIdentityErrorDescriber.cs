using Microsoft.AspNetCore.Identity;

namespace OpenCRM.Infrastructure.Auth;

/// <summary> Локализованные описания ошибок Identity. </summary>
public class OpenCRMIdentityErrorDescriber : IdentityErrorDescriber
{
    /// <summary> Возвращает описание общей ошибки. </summary>
    public override IdentityError DefaultError() => Create(nameof(DefaultError), "Произошла неизвестная ошибка.");

    /// <summary> Возвращает описание ошибки конкуренции. </summary>
    public override IdentityError ConcurrencyFailure() => Create(nameof(ConcurrencyFailure), "Данные были изменены. Обновите страницу и попробуйте снова.");

    /// <summary> Возвращает описание ошибки несовпадения пароля. </summary>
    public override IdentityError PasswordMismatch() => Create(nameof(PasswordMismatch), "Неверный пароль.");

    /// <summary> Возвращает описание ошибки недействительного токена. </summary>
    public override IdentityError InvalidToken() => Create(nameof(InvalidToken), "Недействительный токен.");

    /// <summary> Возвращает описание ошибки уже связанного внешнего входа. </summary>
    public override IdentityError LoginAlreadyAssociated() => Create(nameof(LoginAlreadyAssociated), "Эта учетная запись уже привязана к другому пользователю.");

    /// <summary> Возвращает описание ошибки недопустимого имени пользователя. </summary>
    public override IdentityError InvalidUserName(string? userName) => Create(nameof(InvalidUserName), $"Логин \"{userName}\" содержит недопустимые символы.");

    /// <summary> Возвращает описание ошибки недопустимого email. </summary>
    public override IdentityError InvalidEmail(string? email) => Create(nameof(InvalidEmail), $"Email \"{email}\" имеет неверный формат.");

    /// <summary> Возвращает описание ошибки занятого имени пользователя. </summary>
    public override IdentityError DuplicateUserName(string userName) => Create(nameof(DuplicateUserName), $"Логин \"{userName}\" уже занят.");

    /// <summary> Возвращает описание ошибки занятого email. </summary>
    public override IdentityError DuplicateEmail(string email) => Create(nameof(DuplicateEmail), $"Email \"{email}\" уже используется.");

    /// <summary> Возвращает описание ошибки недопустимого имени роли. </summary>
    public override IdentityError InvalidRoleName(string? role) => Create(nameof(InvalidRoleName), $"Роль \"{role}\" содержит недопустимые символы.");

    /// <summary> Возвращает описание ошибки дублирующейся роли. </summary>
    public override IdentityError DuplicateRoleName(string role) => Create(nameof(DuplicateRoleName), $"Роль \"{role}\" уже существует.");

    /// <summary> Возвращает описание ошибки наличия пароля у пользователя. </summary>
    public override IdentityError UserAlreadyHasPassword() => Create(nameof(UserAlreadyHasPassword), "Для пользователя уже установлен пароль.");

    /// <summary> Возвращает описание ошибки выключенной блокировки пользователя. </summary>
    public override IdentityError UserLockoutNotEnabled() => Create(nameof(UserLockoutNotEnabled), "Блокировка для пользователя не включена.");

    /// <summary> Возвращает описание ошибки наличия пользователя в роли. </summary>
    public override IdentityError UserAlreadyInRole(string role) => Create(nameof(UserAlreadyInRole), $"Пользователь уже состоит в роли \"{role}\".");

    /// <summary> Возвращает описание ошибки отсутствия пользователя в роли. </summary>
    public override IdentityError UserNotInRole(string role) => Create(nameof(UserNotInRole), $"Пользователь не состоит в роли \"{role}\".");

    /// <summary> Возвращает описание ошибки короткого пароля. </summary>
    public override IdentityError PasswordTooShort(int length) => Create(nameof(PasswordTooShort), $"Пароль должен содержать не менее {length} символов.");

    /// <summary> Возвращает описание требования спецсимвола в пароле. </summary>
    public override IdentityError PasswordRequiresNonAlphanumeric() => Create(nameof(PasswordRequiresNonAlphanumeric), "Пароль должен содержать хотя бы один специальный символ.");

    /// <summary> Возвращает описание требования цифры в пароле. </summary>
    public override IdentityError PasswordRequiresDigit() => Create(nameof(PasswordRequiresDigit), "Пароль должен содержать хотя бы одну цифру.");

    /// <summary> Возвращает описание требования строчной буквы в пароле. </summary>
    public override IdentityError PasswordRequiresLower() => Create(nameof(PasswordRequiresLower), "Пароль должен содержать хотя бы одну строчную букву.");

    /// <summary> Возвращает описание требования заглавной буквы в пароле. </summary>
    public override IdentityError PasswordRequiresUpper() => Create(nameof(PasswordRequiresUpper), "Пароль должен содержать хотя бы одну заглавную букву.");

    /// <summary> Возвращает описание требования уникальных символов в пароле. </summary>
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Create(nameof(PasswordRequiresUniqueChars), $"Пароль должен содержать не менее {uniqueChars} уникальных символов.");

    /// <summary> Возвращает описание ошибки использования кода восстановления. </summary>
    public override IdentityError RecoveryCodeRedemptionFailed() => Create(nameof(RecoveryCodeRedemptionFailed), "Не удалось использовать код восстановления.");

    /// <summary> Создаёт объект ошибки с кодом и описанием. </summary>
    private static IdentityError Create(string code, string description) =>
        new()
        {
            Code = code,
            Description = description
        };
}
