using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>Собирает обзор организации с учётом режима профиля и доступа пользователя.</summary>
public interface IWorkspaceOverviewService
{
    /// <summary> Собирает обзор рабочего пространства организации. </summary>
    Task<WorkspaceOverviewResultDto> GetAsync(string userId, Guid organizationId);
}
