namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Вариант выбора участника организации при создании сотрудника. </summary>
public sealed record OrganizationEmployeeMemberOptionDto(Guid Id, string? Email, string? UserName);
