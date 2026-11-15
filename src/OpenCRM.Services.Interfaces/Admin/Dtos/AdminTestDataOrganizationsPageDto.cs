namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Страница организаций для генерации тестовых данных. </summary>
public sealed class AdminTestDataOrganizationsPageDto
{
    /// <summary> Организации на странице. </summary>
    public IReadOnlyList<AdminTestDataOrganizationDto> Items { get; init; } = [];

    /// <summary> Общее количество организаций после применения поиска. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}
