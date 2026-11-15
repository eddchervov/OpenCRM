namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Вариант пользователя для выбора в рассылке. </summary>
public sealed class EmailUserOption
{
    /// <summary> Идентификатор пользователя. </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary> Email пользователя. </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary> Телефон пользователя, если указан. </summary>
    public string? Phone { get; init; }

    /// <summary> Отображаемое имя пользователя. </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary> Признак подтверждённого email пользователя. </summary>
    public bool EmailConfirmed { get; init; }

    /// <summary> Признак согласия пользователя на рекламную рассылку. </summary>
    public bool AdvertisingConsentAccepted { get; init; }
}
