namespace OpenCRM.Domain;

/// <summary> Временный исходный файл до завершения фонового разбора. </summary>
public sealed class OrganizationImportFile
{
    public Guid DraftId { get; set; }
    public byte[] Content { get; set; } = [];
}
