namespace OpenCRM.Domain;

/// <summary> Постоянная связь записи исходной CRM с результатом переноса. </summary>
public sealed class OrganizationImportIdentity
{
    public Guid OrganizationId { get; set; }

    public string SourceKey { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;

    public Guid? ClientId { get; set; }

    public Guid? VehicleId { get; set; }

    public Guid? OrderId { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Guid TargetId => ClientId ?? VehicleId ?? OrderId ?? Guid.Empty;

    public string Fingerprint { get; set; } = string.Empty;

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
