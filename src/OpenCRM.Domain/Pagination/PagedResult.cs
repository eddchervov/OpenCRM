namespace OpenCRM.Domain.Pagination;

/// <summary> Результат постраничной выдачи. </summary>
public class PagedResult<T>
{
    /// <summary> Элементы текущей страницы. </summary>
    public required IEnumerable<T> Items { get; set; }
    /// <summary>
    /// Общее кол-во с учетом фильтров
    /// </summary>
    public required int TotalCount { get; set; }

    /// <summary> Номер текущей страницы. </summary>
    public required int Page { get; set; }

    /// <summary> Размер страницы. </summary>
    public required int PageSize { get; set; }
    /// <summary>
    /// Общее кол-во без учета фильтров
    /// </summary>
    public required int AllCount { get; set; }

    /// <summary> Общее количество страниц. </summary>
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

