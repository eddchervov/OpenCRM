namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Исполнитель, зарегистрированный в организации. </summary>
public sealed class OrganizationMaintenanceJournalPerformerDto
{
    /// <summary> Идентификатор сотрудника. </summary>
    public Guid Id { get; init; }

    /// <summary> Имя сотрудника. </summary>
    public string Name { get; init; } = string.Empty;
}
