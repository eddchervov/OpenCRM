using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.WebApp.Models.Personal;

/// <summary> Модель профиля аккаунта пользователя. </summary>
public class AccountProfileViewModel
{
    /// <summary> Список ожидающих приглашений в организации. </summary>
    public IReadOnlyList<PendingOrganizationInvitationDto> OrganizationInvitations { get; set; } = [];

    /// <summary> Сообщение о статусе операции. </summary>
    public string? StatusMessage { get; set; }
}
