namespace OpenCRM.Services.Interfaces.Imports;

/// <summary>Сопоставление исходных столбцов с полями сущности OpenCRM.</summary>
public class ImportTableMapping
{
    /// <summary>Идентификатор исходной таблицы.</summary>
    public string TableId { get; set; } = string.Empty;

    /// <summary>Тип импортируемой сущности.</summary>
    public string Kind { get; set; } = "client";

    /// <summary>Индексы исходных столбцов для каждого поля сущности.</summary>
    public Dictionary<string, int[]> Fields { get; set; } = [];

    /// <summary>Преобразования значений исходных столбцов перед проверкой записей.</summary>
    public Dictionary<string, ImportFieldTransformation> Transformations { get; set; } = [];
}
