using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Критерии выбора получателей рассылки. </summary>
public class EmailCampaignRecipientsRequest
{
    /// <summary> Режим выбора целевой аудитории. </summary>
    public string TargetMode { get; init; } = EmailCampaignTargetModeCodes.User;

    /// <summary> Фильтр по email получателя. </summary>
    public string? Email { get; init; }

    /// <summary> Идентификатор пользователя для фильтра. </summary>
    public string? UserId { get; init; }

    /// <summary> Код типа организации для фильтра. </summary>
    public string? OrganizationTypeCode { get; init; }

    /// <summary> Признак отбора только пользователей с подтверждённым email. </summary>
    public bool OnlyConfirmedEmail { get; init; } = true;

    /// <summary> Признак отбора только согласившихся на рекламную рассылку. </summary>
    public bool OnlyAdvertisingConsent { get; init; }
}
