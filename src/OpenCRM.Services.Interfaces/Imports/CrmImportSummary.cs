namespace OpenCRM.Services.Interfaces.Imports;

/// <summary> Сводка по строкам импорта CRM. </summary>
public sealed class CrmImportSummary
{
    /// <summary> Количество клиентов. </summary>
    public int Clients { get; set; }

    /// <summary> Количество единиц техники. </summary>
    public int Vehicles { get; set; }

    /// <summary> Количество строк на создание. </summary>
    public int Create { get; set; }

    /// <summary> Количество дубликатов. </summary>
    public int Duplicate { get; set; }

    /// <summary> Количество строк с ошибками. </summary>
    public int Error { get; set; }
}
