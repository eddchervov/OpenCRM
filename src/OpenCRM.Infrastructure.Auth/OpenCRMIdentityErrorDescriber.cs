using System.Globalization;
using System.Resources;
using Microsoft.AspNetCore.Identity;

namespace OpenCRM.Infrastructure.Auth;

/// <summary> Локализованные описания ошибок Identity. </summary>
public class OpenCRMIdentityErrorDescriber : IdentityErrorDescriber
{
    private static readonly ResourceManager s_resources = new(
        "OpenCRM.Infrastructure.Auth.Resources.IdentityErrors",
        typeof(OpenCRMIdentityErrorDescriber).Assembly);

    /// <summary> Возвращает описание общей ошибки. </summary>
    public override IdentityError DefaultError() => OpenCRMIdentityErrorDescriber.Create(nameof(DefaultError));

    /// <summary> Возвращает описание ошибки конкуренции. </summary>
    public override IdentityError ConcurrencyFailure() => OpenCRMIdentityErrorDescriber.Create(nameof(ConcurrencyFailure));

    /// <summary> Возвращает описание ошибки несовпадения пароля. </summary>
    public override IdentityError PasswordMismatch() => OpenCRMIdentityErrorDescriber.Create(nameof(PasswordMismatch));

    /// <summary> Возвращает описание ошибки недействительного токена. </summary>
    public override IdentityError InvalidToken() => OpenCRMIdentityErrorDescriber.Create(nameof(InvalidToken));

    /// <summary> Возвращает описание ошибки уже связанного внешнего входа. </summary>
    public override IdentityError LoginAlreadyAssociated() => OpenCRMIdentityErrorDescriber.Create(nameof(LoginAlreadyAssociated));

    /// <summary> Возвращает описание ошибки недопустимого имени пользователя. </summary>
    public override IdentityError InvalidUserName(string? userName) => OpenCRMIdentityErrorDescriber.Create(nameof(InvalidUserName), userName);

    /// <summary> Возвращает описание ошибки недопустимого email. </summary>
    public override IdentityError InvalidEmail(string? email) => OpenCRMIdentityErrorDescriber.Create(nameof(InvalidEmail), email);

    /// <summary> Возвращает описание ошибки занятого имени пользователя. </summary>
    public override IdentityError DuplicateUserName(string userName) => OpenCRMIdentityErrorDescriber.Create(nameof(DuplicateUserName), userName);

    /// <summary> Возвращает описание ошибки занятого email. </summary>
    public override IdentityError DuplicateEmail(string email) => OpenCRMIdentityErrorDescriber.Create(nameof(DuplicateEmail), email);

    /// <summary> Возвращает описание ошибки недопустимого имени роли. </summary>
    public override IdentityError InvalidRoleName(string? role) => OpenCRMIdentityErrorDescriber.Create(nameof(InvalidRoleName), role);

    /// <summary> Возвращает описание ошибки дублирующейся роли. </summary>
    public override IdentityError DuplicateRoleName(string role) => OpenCRMIdentityErrorDescriber.Create(nameof(DuplicateRoleName), role);

    /// <summary> Возвращает описание ошибки наличия пароля у пользователя. </summary>
    public override IdentityError UserAlreadyHasPassword() => OpenCRMIdentityErrorDescriber.Create(nameof(UserAlreadyHasPassword));

    /// <summary> Возвращает описание ошибки выключенной блокировки пользователя. </summary>
    public override IdentityError UserLockoutNotEnabled() => OpenCRMIdentityErrorDescriber.Create(nameof(UserLockoutNotEnabled));

    /// <summary> Возвращает описание ошибки наличия пользователя в роли. </summary>
    public override IdentityError UserAlreadyInRole(string role) => OpenCRMIdentityErrorDescriber.Create(nameof(UserAlreadyInRole), role);

    /// <summary> Возвращает описание ошибки отсутствия пользователя в роли. </summary>
    public override IdentityError UserNotInRole(string role) => OpenCRMIdentityErrorDescriber.Create(nameof(UserNotInRole), role);

    /// <summary> Возвращает описание ошибки короткого пароля. </summary>
    public override IdentityError PasswordTooShort(int length) => OpenCRMIdentityErrorDescriber.Create(nameof(PasswordTooShort), length);

    /// <summary> Возвращает описание требования спецсимвола в пароле. </summary>
    public override IdentityError PasswordRequiresNonAlphanumeric() => OpenCRMIdentityErrorDescriber.Create(nameof(PasswordRequiresNonAlphanumeric));

    /// <summary> Возвращает описание требования цифры в пароле. </summary>
    public override IdentityError PasswordRequiresDigit() => OpenCRMIdentityErrorDescriber.Create(nameof(PasswordRequiresDigit));

    /// <summary> Возвращает описание требования строчной буквы в пароле. </summary>
    public override IdentityError PasswordRequiresLower() => OpenCRMIdentityErrorDescriber.Create(nameof(PasswordRequiresLower));

    /// <summary> Возвращает описание требования заглавной буквы в пароле. </summary>
    public override IdentityError PasswordRequiresUpper() => OpenCRMIdentityErrorDescriber.Create(nameof(PasswordRequiresUpper));

    /// <summary> Возвращает описание требования уникальных символов в пароле. </summary>
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => OpenCRMIdentityErrorDescriber.Create(nameof(PasswordRequiresUniqueChars), uniqueChars);

    /// <summary> Возвращает описание ошибки использования кода восстановления. </summary>
    public override IdentityError RecoveryCodeRedemptionFailed() => OpenCRMIdentityErrorDescriber.Create(nameof(RecoveryCodeRedemptionFailed));

    /// <summary> Создаёт объект ошибки с кодом и описанием. </summary>
    private static IdentityError Create(string code, params object?[] arguments) =>
        new()
        {
            Code = code,
            Description = string.Format(CultureInfo.CurrentCulture,
                OpenCRMIdentityErrorDescriber.s_resources.GetString(code, CultureInfo.CurrentUICulture)!, arguments)
        };
}
