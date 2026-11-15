namespace OpenCRM.Services.Interfaces.Imports;

/// <summary>Данные редактируемого черновика импорта.</summary>
public class ImportDraftData
{
    public ImportReconciliationDto? Reconciliation { get; set; }

    /// <summary>Исходные таблицы файла.</summary>
    public List<ImportSourceTable> Tables { get; set; } = [];

    /// <summary>Сопоставления столбцов с полями OpenCRM.</summary>
    public List<ImportTableMapping> Mappings { get; set; } = [];

    /// <summary>Подготовленные и редактируемые строки импорта.</summary>
    public List<ImportCandidate> Rows { get; set; } = [];
}
