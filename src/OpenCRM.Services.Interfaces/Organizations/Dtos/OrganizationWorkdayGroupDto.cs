namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary>Группа работ с общим количеством и ограниченной выборкой карточек.</summary>
public sealed class OrganizationWorkdayGroupDto
{
    /// <summary> Общее количество работ в группе. </summary>
    public int TotalCount { get; init; }

    /// <summary> Карточки работ в группе. </summary>
    public IReadOnlyList<OrganizationWorkdayItemDto> Items { get; init; } = [];
}
