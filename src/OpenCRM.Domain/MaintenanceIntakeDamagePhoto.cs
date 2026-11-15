namespace OpenCRM.Domain;

/// <summary> Связь повреждения и фотографии того же заказа. </summary>
public sealed class MaintenanceIntakeDamagePhoto
{
    public Guid MaintenanceId { get; set; }

    public Guid DamageId { get; set; }

    public Guid PhotoId { get; set; }

    public MaintenanceIntakeDamage Damage { get; set; } = null!;

    public MaintenancePhoto Photo { get; set; } = null!;
}
