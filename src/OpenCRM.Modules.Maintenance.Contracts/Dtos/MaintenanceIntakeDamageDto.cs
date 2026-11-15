namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Повреждение на схеме осмотра и связанные фотографии. </summary>
public sealed class MaintenanceIntakeDamageDto
{
    public Guid Id { get; set; }

    /// <summary> Порядковый номер среди активных повреждений текущего осмотра. </summary>
    public int Number { get; set; }

    public string ViewCode { get; set; } = string.Empty;

    public string DamageType { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public string? Description { get; set; }

    public IReadOnlyList<Guid> PhotoIds { get; set; } = [];
}
