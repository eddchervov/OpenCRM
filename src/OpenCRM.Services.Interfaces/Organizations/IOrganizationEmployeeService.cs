using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Контракт сервиса управления сотрудниками организации. </summary>
public interface IOrganizationEmployeeService
{
    /// <summary> Возвращает список сотрудников организации. </summary>
    Task<IReadOnlyList<OrganizationEmployeeDto>> GetAsync(string userId, Guid organizationId);

    /// <summary> Создаёт или обновляет сотрудника организации. </summary>
    Task<Guid> SaveAsync(string userId, Guid organizationId, Guid? employeeId, OrganizationEmployeeDto dto);

    /// <summary> Создаёт сотрудника на основе участника организации. </summary>
    Task<Guid> CreateForMemberAsync(
        string userId,
        Guid organizationId,
        Guid memberId,
        string name,
        string? phone,
        bool isActive = true);

    /// <summary> Связывает сотрудника с участником организации. </summary>
    Task LinkMemberAsync(string userId, Guid organizationId, Guid employeeId, Guid? memberId, uint version);

    /// <summary> Возвращает варианты участников для привязки к сотрудникам. </summary>
    Task<IReadOnlyList<OrganizationEmployeeMemberOptionDto>> GetMemberOptionsAsync(string userId, Guid organizationId);
}
