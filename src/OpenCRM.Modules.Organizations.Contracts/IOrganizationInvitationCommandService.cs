using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Contracts;

/// <summary> Контракт команд работы с приглашениями в организацию. </summary>
public interface IOrganizationInvitationCommandService
{
    /// <summary> Принятие приглашения пользователем. </summary>
    Task AcceptInvitationAsync(string userId, Guid invitationId);

    /// <summary> Отклонение приглашения пользователем. </summary>
    Task DeclineInvitationAsync(string userId, Guid invitationId);

    /// <summary> Приглашение участника в организацию. </summary>
    Task InviteMemberAsync(string currentUserId, Guid organizationId, InviteOrganizationMemberDto dto);

    /// <summary> Создаёт сотрудника и приглашение в организацию. </summary>
    Task<CreateOrganizationEmployeeInvitationResultDto> CreateEmployeeAndInviteAsync(string currentUserId, Guid organizationId, CreateOrganizationEmployeeInvitationDto dto);

    /// <summary> Отзывает приглашение организации. </summary>
    Task RevokeInvitationAsync(string currentUserId, Guid organizationId, Guid invitationId);
}
