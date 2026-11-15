namespace OpenCRM.Services.Interfaces.Imports;

/// <summary>Строка исходной таблицы с номером строки в файле.</summary>
public class ImportSourceRow
{
    /// <summary>Номер исходной строки.</summary>
    public int Number { get; set; }

    /// <summary>Значения ячеек в порядке исходных столбцов.</summary>
    public List<string> Values { get; set; } = [];
}
