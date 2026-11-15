namespace OpenCRM.WebApp.Models.Auth;

/// <summary> Запрос на вход от имени пользователя. </summary>
public sealed class ImpersonateUserRequest
{
    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; init; } = string.Empty;
}
