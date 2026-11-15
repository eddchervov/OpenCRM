namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Изменение одной отметки осмотра с защитой от потери параллельных правок. </summary>
public sealed class SaveMaintenanceIntakeDamageDto
{
    public uint Version { get; set; }

    public string DiagramVersion { get; set; } = string.Empty;

    public string ViewCode { get; set; } = string.Empty;

    public string DamageType { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public string? Description { get; set; }

    public IReadOnlyList<Guid> PhotoIds { get; set; } = [];
}
