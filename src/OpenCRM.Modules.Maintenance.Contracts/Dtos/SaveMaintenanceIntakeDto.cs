namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary>Результат осмотра автомобиля при приёмке.</summary>
public sealed class SaveMaintenanceIntakeDto
{
    /// <summary> Пробег транспортного средства при приёмке. </summary>
    public int? Mileage { get; set; }

    /// <summary> Комплектность автомобиля при приёмке. </summary>
    public string? Completeness { get; set; }

    /// <summary> Видимые повреждения при приёмке. </summary>
    public string? VisibleDamage { get; set; }

    /// <summary> Заметки приёмки. </summary>
    public string? Notes { get; set; }
}
