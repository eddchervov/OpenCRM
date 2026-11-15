namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Страница списка обслуживаний. </summary>
public class MaintenanceListResponse
{
    /// <summary> Элементы списка обслуживаний. </summary>
    public required IEnumerable<GetMaintenanceDto> Items { get; set; }

    /// <summary> Общее количество обслуживаний. </summary>
    public required int TotalCount { get; set; }

    /// <summary> Номер текущей страницы. </summary>
    public required int Page { get; set; }

    /// <summary> Размер страницы. </summary>
    public required int PageSize { get; set; }

    /// <summary> Признак наличия следующих страниц. </summary>
    public required bool HasMore { get; set; }
}
