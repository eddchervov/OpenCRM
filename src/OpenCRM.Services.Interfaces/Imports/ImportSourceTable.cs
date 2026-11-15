namespace OpenCRM.Services.Interfaces.Imports;

/// <summary>Исходная таблица, извлечённая из файла импорта.</summary>
public class ImportSourceTable
{
    /// <summary>Стабильный идентификатор таблицы внутри файла.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Название листа или таблицы.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Имена столбцов исходной таблицы.</summary>
    public List<string> Columns { get; set; } = [];

    /// <summary>Строки исходной таблицы.</summary>
    public List<ImportSourceRow> Rows { get; set; } = [];
}
