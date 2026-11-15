namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO страницы организаций для админ-панели. </summary>
public sealed class AdminOrganizationsPageDto
{
    /// <summary> Количество организаций после применения фильтров. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }

    /// <summary> Общее количество организаций. </summary>
    public int AllCount { get; init; }

    /// <summary> Количество активных организаций. </summary>
    public int ActiveCount { get; init; }

    /// <summary> Количество архивных организаций. </summary>
    public int ArchivedCount { get; init; }

    /// <summary> Количество удалённых организаций. </summary>
    public int DeletedCount { get; init; }

    /// <summary> Количество организаций, требующих внимания. </summary>
    public int AttentionCount { get; init; }

    /// <summary> Список организаций страницы. </summary>
    public IReadOnlyList<AdminOrganizationDto> Organizations { get; init; } = [];
}

