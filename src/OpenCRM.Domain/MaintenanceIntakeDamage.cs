namespace OpenCRM.Domain;

/// <summary> Повреждение, зафиксированное при осмотре конкретного заказа. </summary>
public sealed class MaintenanceIntakeDamage
{
    public Guid Id { get; set; }

    public Guid MaintenanceId { get; set; }

    /// <summary> Неизменяемый порядок создания; видимый номер рассчитывается среди активных меток. </summary>
    public int Number { get; set; }

    public string ViewCode { get; set; } = string.Empty;

    public string DamageType { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public string? Description { get; set; }

    public bool IsDeleted { get; set; }

    public Maintenance Maintenance { get; set; } = null!;

    public ICollection<MaintenanceIntakeDamagePhoto> Photos { get; set; } = [];
}
