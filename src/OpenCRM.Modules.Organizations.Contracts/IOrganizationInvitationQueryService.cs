using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Contracts;

/// <summary> Контракт запросов приглашений в организацию. </summary>
public interface IOrganizationInvitationQueryService
{
    /// <summary> Возвращает ожидающие приглашения пользователя. </summary>
    Task<IReadOnlyList<PendingOrganizationInvitationDto>> GetPendingInvitationsAsync(string userId);

    /// <summary> Возвращает приглашения пользователя. </summary>
    Task<IReadOnlyList<PendingOrganizationInvitationDto>> GetInvitationsAsync(string userId);
}
