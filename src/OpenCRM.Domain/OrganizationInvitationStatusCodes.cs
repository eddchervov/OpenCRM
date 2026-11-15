namespace OpenCRM.Domain;

/// <summary> Коды статусов приглашений в организацию. </summary>
public static class OrganizationInvitationStatusCodes
{
    /// <summary> Код статуса ожидающего ответа приглашения. </summary>
    public const string Pending = "pending";

    /// <summary> Код статуса принятого приглашения. </summary>
    public const string Accepted = "accepted";

    /// <summary> Код статуса отклонённого приглашения. </summary>
    public const string Declined = "declined";

    /// <summary> Код статуса отозванного приглашения. </summary>
    public const string Revoked = "revoked";
}
