namespace OpenCRM.Services.Interfaces.Pdf.Dtos;

/// <summary>Данные печатного акта приёмки автомобиля.</summary>
public sealed class MaintenanceIntakePdfModel
{
    /// <summary> Данные транспортного средства в документе. </summary>
    public required VehicleInfo Vehicle { get; init; }

    /// <summary> Реквизит записи обслуживания. </summary>
    public required string MaintenanceReference { get; init; }

    /// <summary> Дата записи обслуживания. </summary>
    public required string RecordedDate { get; init; }

    /// <summary> Дата формирования документа. </summary>
    public required string GeneratedDate { get; init; }

    /// <summary> Название организации, если есть. </summary>
    public string? OrganizationName { get; init; }

    /// <summary> Имя клиента, если есть. </summary>
    public string? ClientName { get; init; }

    /// <summary> Исполнитель работ, если есть. </summary>
    public string? Performer { get; init; }

    /// <summary> Пробег на момент приёмки, если известен. </summary>
    public int? Mileage { get; init; }

    /// <summary> Комплектность автомобиля при приёмке. </summary>
    public string? Completeness { get; init; }

    /// <summary> Видимые повреждения при приёмке. </summary>
    public string? VisibleDamage { get; init; }

    /// <summary> Нумерованные повреждения, отмеченные на схеме осмотра. </summary>
    public IReadOnlyList<MaintenanceIntakeDamagePdfInfo> Damages { get; init; } = [];

    /// <summary> Закреплённая за осмотром версия схемы повреждений. </summary>
    public string? DiagramVersion { get; init; }

    /// <summary> Заметки приёмки, если есть. </summary>
    public string? Notes { get; init; }

    /// <summary> Фотографии автомобиля до работ. </summary>
    public IReadOnlyList<PhotoInfo> BeforePhotos { get; init; } = [];
}
