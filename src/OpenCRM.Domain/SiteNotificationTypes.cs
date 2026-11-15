namespace OpenCRM.Domain;

/// <summary> Типы уведомлений на сайте. </summary>
public static class SiteNotificationTypes
{
    /// <summary> Тип: уведомление, созданное вручную. </summary>
    public const string Manual = "manual";

    /// <summary> Тип: приглашение в организацию. </summary>
    public const string OrganizationInvitation = "organization-invitation";

    /// <summary> Тип: приглашение принято. </summary>
    public const string InvitationAccepted = "invitation-accepted";

    /// <summary> Тип: подписка организации скоро истекает. </summary>
    public const string OrganizationSubscriptionExpiring = "organization-subscription-expiring";

    /// <summary> Тип: подписка организации истекла. </summary>
    public const string OrganizationSubscriptionExpired = "organization-subscription-expired";

    /// <summary> Тип: приветственное уведомление. </summary>
    public const string Welcome = "welcome";

    /// <summary> Список кодов автоматически создаваемых типов уведомлений. </summary>
    public static IReadOnlyCollection<string> Automatic { get; } =
    [
        OrganizationInvitation,
        InvitationAccepted,
        OrganizationSubscriptionExpiring,
        OrganizationSubscriptionExpired,
        Welcome
    ];
}
