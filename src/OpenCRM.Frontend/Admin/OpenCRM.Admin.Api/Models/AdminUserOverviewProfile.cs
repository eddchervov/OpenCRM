namespace OpenCRM.Admin.Api.Models;

/// <summary> Профиль пользователя в обзоре. </summary>
public sealed class AdminUserOverviewProfile
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

    /// <summary> Время создания пользователя. </summary>
    public DateTime DatetimeCreation { get; init; }

    /// <summary> Дата последнего входа. </summary>
    public DateTime LastDateOfEntry { get; init; }

    /// <summary> Дата последнего входа в систему. </summary>
    public DateTime LastLoginDate { get; init; }

    /// <summary> Число входов. </summary>
    public int CountLogins { get; init; }

    /// <summary> Число посещений. </summary>
    public int CountVisits { get; init; }

    /// <summary> Признак доступности блокировки. </summary>
    public bool LockoutEnabled { get; init; }

    /// <summary> Время окончания блокировки. </summary>
    public DateTimeOffset? LockoutEnd { get; init; }

    /// <summary> Число неудачных попыток входа. </summary>
    public int AccessFailedCount { get; init; }

    /// <summary> Признак наличия пароля. </summary>
    public bool HasPassword { get; init; }

    /// <summary> Признак принятого согласия на обработку персональных данных. </summary>
    public bool PersonalDataProcessingConsentAccepted { get; init; }

    /// <summary> Время принятия согласия на обработку персональных данных. </summary>
    public DateTime? PersonalDataProcessingConsentAcceptedAt { get; init; }

    /// <summary> Признак принятого согласия на рекламу. </summary>
    public bool AdvertisingConsentAccepted { get; init; }

    /// <summary> Время принятия согласия на рекламу. </summary>
    public DateTime? AdvertisingConsentAcceptedAt { get; init; }

    /// <summary> UTM-источник первого визита. </summary>
    public string? FirstUtmSource { get; init; }

    /// <summary> UTM-канал первого визита. </summary>
    public string? FirstUtmMedium { get; init; }

    /// <summary> UTM-кампания первого визита. </summary>
    public string? FirstUtmCampaign { get; init; }

    /// <summary> UTM-содержимое первого визита. </summary>
    public string? FirstUtmContent { get; init; }

    /// <summary> UTM-ключевое слово первого визита. </summary>
    public string? FirstUtmTerm { get; init; }

    /// <summary> Время первого визита. </summary>
    public DateTime? FirstVisitAt { get; init; }

    /// <summary> UTM-источник последнего визита. </summary>
    public string? LastUtmSource { get; init; }

    /// <summary> UTM-канал последнего визита. </summary>
    public string? LastUtmMedium { get; init; }

    /// <summary> UTM-кампания последнего визита. </summary>
    public string? LastUtmCampaign { get; init; }

    /// <summary> UTM-содержимое последнего визита. </summary>
    public string? LastUtmContent { get; init; }

    /// <summary> UTM-ключевое слово последнего визита. </summary>
    public string? LastUtmTerm { get; init; }

    /// <summary> Время последнего визита. </summary>
    public DateTime? LastVisitAt { get; init; }

    /// <summary> Признак разрешения использовать тестовые подписки. </summary>
    public bool CanUseTestSubscriptions { get; init; }

    /// <summary> Признак удалённого пользователя. </summary>
    public bool IsDeleted { get; init; }

    /// <summary> Время удаления пользователя. </summary>
    public DateTime? DeletedAt { get; init; }

    /// <summary> Роли пользователя. </summary>
    public IReadOnlyList<string?> Roles { get; init; } = [];
}
