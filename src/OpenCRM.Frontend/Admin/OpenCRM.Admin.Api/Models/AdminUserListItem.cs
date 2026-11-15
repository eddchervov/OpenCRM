namespace OpenCRM.Admin.Api.Models;

/// <summary> Пользователь в списке для административного просмотра. </summary>
public sealed class AdminUserListItem
{
    /// <summary> Идентификатор пользователя. </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary> Телефон пользователя. </summary>
    public string? Phone { get; init; }

    /// <summary> Признак подтверждённого телефона. </summary>
    public bool PhoneConfirmed { get; init; }

    /// <summary> Email пользователя. </summary>
    public string? Email { get; init; }

    /// <summary> Признак подтверждённого email. </summary>
    public bool EmailConfirmed { get; init; }

    /// <summary> Признак наличия пароля. </summary>
    public bool HasPassword { get; init; }

    /// <summary> Признак доступности блокировки учётной записи. </summary>
    public bool LockoutEnabled { get; init; }

    /// <summary> Момент создания учётной записи. </summary>
    public DateTime DatetimeCreation { get; init; }

    /// <summary> Момент последнего входа. </summary>
    public DateTime? LastDateOfEntry { get; init; }

    /// <summary> Количество посещений. </summary>
    public int CountVisits { get; init; }

    /// <summary> Дата последнего входа. </summary>
    public DateTime? LastLoginDate { get; init; }

    /// <summary> Количество входов. </summary>
    public int CountLogins { get; init; }

    /// <summary> UTM-источник первого посещения. </summary>
    public string? FirstUtmSource { get; init; }

    /// <summary> UTM-тип трафика первого посещения. </summary>
    public string? FirstUtmMedium { get; init; }

    /// <summary> Количество транспортных средств пользователя. </summary>
    public int CountVehicles { get; init; }

    /// <summary> Количество обслуживаний пользователя. </summary>
    public int CountMaintenance { get; init; }

    /// <summary> Количество организаций пользователя. </summary>
    public int OrganizationsCount { get; init; }

    /// <summary> Признак разрешения тестовых подписок. </summary>
    public bool CanUseTestSubscriptions { get; init; }

    /// <summary> Название текущей подписки. </summary>
    public string? CurrentSubscriptionName { get; init; }

    /// <summary> Дата окончания текущей подписки. </summary>
    public DateTime? CurrentSubscriptionEndDate { get; init; }

    /// <summary> Признак демо-режима текущей подписки. </summary>
    public bool CurrentSubscriptionIsDemo { get; init; }

    /// <summary> Название эффективного тарифного плана. </summary>
    public string? EffectivePlanName { get; init; }

    /// <summary> Код типа эффективного тарифного плана. </summary>
    public string EffectivePlanType { get; init; } = "none";

    /// <summary> Количество активных предоставленных возможностей. </summary>
    public int ActiveFeatureGrantsCount { get; init; }

    /// <summary> Признак удаления пользователя. </summary>
    public bool IsDeleted { get; init; }
}
