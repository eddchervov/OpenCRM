namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO профиля пользователя в обзоре. </summary>
public sealed class AdminUserOverviewProfileDto
{
    /// <summary> Идентификатор пользователя. </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary> Email пользователя. </summary>
    public string? Email { get; init; }

    /// <summary> Признак подтверждённого email. </summary>
    public bool EmailConfirmed { get; init; }

    /// <summary> Телефон пользователя. </summary>
    public string? Phone { get; init; }

    /// <summary> Признак подтверждённого телефона. </summary>
    public bool PhoneNumberConfirmed { get; init; }

    /// <summary> Отфильтрованный телефон пользователя. </summary>
    public string? PhoneFiltered { get; init; }

    /// <summary> Дата создания пользователя. </summary>
    public DateTime DatetimeCreation { get; init; }

    /// <summary> Дата последнего входа. </summary>
    public DateTime LastDateOfEntry { get; init; }

    /// <summary> Дата последнего входа в систему. </summary>
    public DateTime LastLoginDate { get; init; }

    /// <summary> Количество входов. </summary>
    public int CountLogins { get; init; }

    /// <summary> Количество визитов. </summary>
    public int CountVisits { get; init; }

    /// <summary> Признак доступной блокировки пользователя. </summary>
    public bool LockoutEnabled { get; init; }

    /// <summary> Дата окончания блокировки. </summary>
    public DateTimeOffset? LockoutEnd { get; init; }

    /// <summary> Количество неудачных попыток доступа. </summary>
    public int AccessFailedCount { get; init; }

    /// <summary> Признак наличия пароля. </summary>
    public bool HasPassword { get; init; }

    /// <summary> Согласие на обработку персональных данных. </summary>
    public bool PersonalDataProcessingConsentAccepted { get; init; }

    /// <summary> Дата принятия согласия на обработку персональных данных. </summary>
    public DateTime? PersonalDataProcessingConsentAcceptedAt { get; init; }

    /// <summary> Согласие на рекламу. </summary>
    public bool AdvertisingConsentAccepted { get; init; }

    /// <summary> Дата принятия согласия на рекламу. </summary>
    public DateTime? AdvertisingConsentAcceptedAt { get; init; }

    /// <summary> Первый UTM-источник. </summary>
    public string? FirstUtmSource { get; init; }

    /// <summary> Первый UTM-канал. </summary>
    public string? FirstUtmMedium { get; init; }

    /// <summary> Первая UTM-кампания. </summary>
    public string? FirstUtmCampaign { get; init; }

    /// <summary> Первый UTM-контент. </summary>
    public string? FirstUtmContent { get; init; }

    /// <summary> Первый UTM-термин. </summary>
    public string? FirstUtmTerm { get; init; }

    /// <summary> Дата первого визита. </summary>
    public DateTime? FirstVisitAt { get; init; }

    /// <summary> Последний UTM-источник. </summary>
    public string? LastUtmSource { get; init; }

    /// <summary> Последний UTM-канал. </summary>
    public string? LastUtmMedium { get; init; }

    /// <summary> Последняя UTM-кампания. </summary>
    public string? LastUtmCampaign { get; init; }

    /// <summary> Последний UTM-контент. </summary>
    public string? LastUtmContent { get; init; }

    /// <summary> Последний UTM-термин. </summary>
    public string? LastUtmTerm { get; init; }

    /// <summary> Дата последнего визита. </summary>
    public DateTime? LastVisitAt { get; init; }

    /// <summary> Разрешение на использование тестовых подписок. </summary>
    public bool CanUseTestSubscriptions { get; init; }

    /// <summary> Признак удалённого пользователя. </summary>
    public bool IsDeleted { get; init; }

    /// <summary> Дата удаления пользователя. </summary>
    public DateTime? DeletedAt { get; init; }

    /// <summary> Список ролей пользователя. </summary>
    public IReadOnlyList<string?> Roles { get; init; } = [];
}
