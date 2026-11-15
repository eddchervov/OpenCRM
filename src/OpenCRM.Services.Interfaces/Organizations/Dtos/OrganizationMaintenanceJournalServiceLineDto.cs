namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Строка услуги журнала обслуживания. </summary>
public sealed class OrganizationMaintenanceJournalServiceLineDto
{
    /// <summary> Наименование услуги. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Исполнитель услуги из сохранённой строки. </summary>
    public string? EmployeeName { get; init; }

    /// <summary> Сумма строки, если она указана. </summary>
    public decimal? Amount { get; init; }
}
