using OpenCRM.Domain.Identity;

namespace OpenCRM.Services.Interfaces.Users.Dtos;

/// <summary> Результат входа по коду из письма. </summary>
public sealed class EmailCodeLoginResult
{
    /// <summary> Пользователь, если вход выполнен или аккаунт найден. </summary>
    public ApplicationUser? User { get; init; }

    /// <summary> Признак создания нового аккаунта при входе. </summary>
    public bool IsAccountCreated { get; init; }

    /// <summary> Признак необходимости согласий при регистрации. </summary>
    public bool RequiresRegistrationConsent { get; init; }

    /// <summary> Признак отправки кода на почту. </summary>
    public bool CodeSent { get; init; }

    /// <summary> Признак повторной отправки нового кода. </summary>
    public bool IsNewCodeSent { get; init; }

    /// <summary> Статусное сообщение результата, если есть. </summary>
    public string? StatusMessage { get; init; }

    /// <summary> Поле формы с ошибкой валидации, если есть. </summary>
    public string ErrorField { get; init; } = string.Empty;

    /// <summary> Список ошибок входа или регистрации. </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}
