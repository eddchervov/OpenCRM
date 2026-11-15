namespace OpenCRM.Admin.Api.Models;

/// <summary> Страница организаций для административной панели. </summary>
public sealed class AdminOrganizationsPage
{
    public int AllCount { get; init; }
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int ActiveCount { get; init; }
    public int ArchivedCount { get; init; }
    public int DeletedCount { get; init; }
    public int AttentionCount { get; init; }
    public IReadOnlyList<AdminOrganization> Organizations { get; init; } = [];
}
