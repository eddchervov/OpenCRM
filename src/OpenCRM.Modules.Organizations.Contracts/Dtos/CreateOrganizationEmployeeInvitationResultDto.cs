namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> Результат создания сотрудника и приглашения с их идентификаторами. </summary>
public sealed record CreateOrganizationEmployeeInvitationResultDto(Guid EmployeeId, Guid InvitationId);
