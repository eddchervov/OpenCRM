namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запись предпросмотра получателя рассылки. </summary>
public sealed class EmailRecipientPreviewItem
{
    /// <summary> Идентификатор пользователя, если получатель известен. </summary>
    public string? UserId { get; init; }

    /// <summary> Email получателя. </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary> Отображаемое имя получателя. </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary> Телефон получателя, если указан. </summary>
    public string? Phone { get; init; }

    /// <summary> Признак подтверждённого email получателя. </summary>
    public bool EmailConfirmed { get; init; }

    /// <summary> Признак согласия получателя на рекламную рассылку. </summary>
    public bool AdvertisingConsentAccepted { get; init; }
}
