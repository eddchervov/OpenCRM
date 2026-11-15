namespace OpenCRM.Admin.Api.Models;

/// <summary> Ответ со списком пользователей для администрирования. </summary>
public sealed class AdminUsersResponse
{
    /// <summary> Список пользователей на странице. </summary>
    public IReadOnlyList<AdminUserListItem> Users { get; init; } = [];

    /// <summary> Общее число пользователей. </summary>
    public int AllCount { get; init; }

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}
