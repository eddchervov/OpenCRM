namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary>Работы выбранного дня и текущая загрузка детейлинга.</summary>
public sealed class OrganizationWorkdayDto
{
    /// <summary> Признак наличия работ в выбранном дне. </summary>
    public bool HasAnyWork { get; init; }

    /// <summary> Группа запланированных работ дня. </summary>
    public OrganizationWorkdayGroupDto Planned { get; init; } = new();

    /// <summary> Группа работ в процессе выполнения. </summary>
    public OrganizationWorkdayGroupDto InProgress { get; init; } = new();

    /// <summary> Группа готовых к передаче работ. </summary>
    public OrganizationWorkdayGroupDto Ready { get; init; } = new();

    /// <summary> Группа переданных работ. </summary>
    public OrganizationWorkdayGroupDto HandedOver { get; init; } = new();
}
