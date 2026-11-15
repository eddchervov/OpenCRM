namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO пользователя для дашборда. </summary>
public sealed class AdminDashboardUserDto
{
    /// <summary> Идентификатор пользователя. </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary> Отображаемое имя пользователя. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Email пользователя. </summary>
    public string? Email { get; init; }

    /// <summary> Телефон пользователя. </summary>
    public string? Phone { get; init; }

    /// <summary> Время создания учётной записи. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Дата последнего входа. </summary>
    public DateTime LastLoginDate { get; init; }
}

