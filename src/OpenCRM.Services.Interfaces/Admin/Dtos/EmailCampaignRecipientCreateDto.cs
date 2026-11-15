using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Данные получателя при создании рассылки. </summary>
public sealed class EmailCampaignRecipientCreateDto
{
    /// <summary> Идентификатор пользователя, если получатель известен. </summary>
    public string? UserId { get; init; }

    /// <summary> Email получателя. </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary> Отображаемое имя получателя. </summary>
    public string DisplayName { get; init; } = string.Empty;
}

