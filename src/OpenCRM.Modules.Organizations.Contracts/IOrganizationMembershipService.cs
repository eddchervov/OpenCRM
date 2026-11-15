namespace OpenCRM.Modules.Organizations.Contracts;

/// <summary> Контракт управления участниками организации. </summary>
public interface IOrganizationMembershipService
{
    /// <summary> Передаёт владение организацией другому участнику. </summary>
    Task TransferOwnershipAsync(string currentUserId, Guid organizationId, Guid memberId);

    /// <summary> Выход пользователя из организации. </summary>
    Task LeaveOrganizationAsync(string currentUserId, Guid organizationId);

    /// <summary> Изменяет роль участника организации. </summary>
    Task UpdateMemberRoleAsync(string currentUserId, Guid organizationId, Guid memberId, string roleCode);

    /// <summary> Удаляет участника из организации. </summary>
    Task RemoveMemberAsync(string currentUserId, Guid organizationId, Guid memberId);
}
