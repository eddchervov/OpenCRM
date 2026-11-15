namespace OpenCRM.Services.Interfaces.Imports;

/// <summary> Сохраняемая структура таблиц и правила преобразования без исходных строк. </summary>
public sealed class ImportProfileDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? SourceKey { get; set; }

    public List<ImportSourceTable> Tables { get; set; } = [];

    public List<ImportTableMapping> Mappings { get; set; } = [];

    public DateTime UpdatedAt { get; set; }
}
