namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Согласованный снимок повреждений с версией заказа. </summary>
public sealed class MaintenanceIntakeDamagesDto
{
    public uint Version { get; set; }

    public string DiagramVersion { get; set; } = string.Empty;

    public IReadOnlyList<MaintenanceIntakeDamageDto> Damages { get; set; } = [];
}
