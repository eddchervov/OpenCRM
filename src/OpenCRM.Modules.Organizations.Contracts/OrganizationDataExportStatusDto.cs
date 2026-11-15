namespace OpenCRM.Modules.Organizations.Contracts;

/// <summary> Состояние подготовки архива без внутренних путей файлового хранилища. </summary>
public sealed class OrganizationDataExportStatusDto
{
    public Guid? Id { get; init; }
    public string State { get; init; } = "idle";
    public DateTime? RequestedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public DateTime? RetryAtUtc { get; init; }
    public long? SizeBytes { get; init; }
    public string? ErrorMessage { get; init; }
}
