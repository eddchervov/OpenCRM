namespace OpenCRM.Admin.Api.Models;

/// <summary> Пользователь для административной панели. </summary>
public sealed class AdminDashboardUser
{
    /// <summary> Идентификатор пользователя. </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary> Отображаемое имя пользователя. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Email пользователя. </summary>
    public string? Email { get; init; }

    /// <summary> Телефон пользователя. </summary>
    public string? Phone { get; init; }

    /// <summary> Момент регистрации пользователя. </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary> Момент последнего входа. </summary>
    public DateTimeOffset LastLoginDate { get; init; }
}
