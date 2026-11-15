namespace OpenCRM.Domain;

/// <summary> Неизменяемая исходная строка загруженной таблицы. </summary>
public sealed class OrganizationImportSourceRow
{
    public Guid DraftId { get; set; }
    public string TableId { get; set; } = string.Empty;
    public int Number { get; set; }
    public string ValuesJson { get; set; } = string.Empty;
}
