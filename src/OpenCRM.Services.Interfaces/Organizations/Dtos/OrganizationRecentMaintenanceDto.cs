using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Краткие данные о недавнем обслуживании организации. </summary>
public sealed class OrganizationRecentMaintenanceDto
{
    /// <summary> Идентификатор записи обслуживания. </summary>
    public Guid Id { get; init; }

    /// <summary> Версия записи для оптимистичной блокировки. </summary>
    public uint Version { get; init; }

    /// <summary> Идентификатор транспортного средства. </summary>
    public Guid VehicleId { get; init; }

    /// <summary> Отображаемое название транспортного средства. </summary>
    public string VehicleDisplayName { get; init; } = string.Empty;

    /// <summary> Дополнительная подпись транспортного средства. </summary>
    public string? VehicleSubtitle { get; init; }

    /// <summary> Государственный номер транспортного средства. </summary>
    public string? RegistrationPlate { get; init; }

    /// <summary> Ключ логотипа справочной марки. </summary>
    public string? BrandLogoKey { get; init; }

    /// <summary> Названия услуг в порядке строк заказа. </summary>
    public IReadOnlyList<string> ServiceNames { get; init; } = [];

    /// <summary> Имена назначенных исполнителей услуг. </summary>
    public IReadOnlyList<string> EmployeeNames { get; init; } = [];

    /// <summary> Дата готовности к выдаче. </summary>
    public DateTime? ReadyForHandoverAt { get; init; }

    /// <summary> Дата выдачи транспортного средства. </summary>
    public DateTime? HandedOverAt { get; init; }

    /// <summary> Название типа обслуживания. </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary> Статус обслуживания. </summary>
    public MaintenanceStatus Status { get; init; }

    /// <summary> Название статуса обслуживания. </summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary> Признак того, что дата является плановой. </summary>
    public bool IsPlannedDate { get; init; }

    /// <summary> Дата обслуживания. </summary>
    public DateTime Date { get; init; }

    /// <summary> Плановое время начала. </summary>
    public TimeSpan? PlannedStartTime { get; init; }

    /// <summary> Плановое время окончания. </summary>
    public TimeSpan? PlannedEndTime { get; init; }

    /// <summary>Плановая дата окончания записи.</summary>
    public DateTime? PlannedEndDate { get; init; }

    /// <summary> Место проведения обслуживания. </summary>
    public string? Location { get; init; }

    /// <summary> Пробег транспортного средства. </summary>
    public int? Mileage { get; init; }

    /// <summary> Единица измерения пробега. </summary>
    public VehicleMileageUnit MileageUnit { get; init; }

    /// <summary> Комментарий к обслуживанию. </summary>
    public string? Comment { get; init; }

    /// <summary> Валюта сохранённого обслуживания. </summary>
    public string CurrencyCode { get; init; } = "RUB";

    /// <summary> Стоимость обслуживания. </summary>
    public decimal? Price { get; init; }

    /// <summary> Адрес страницы с деталями обслуживания. </summary>
    public string DetailUrl { get; init; } = string.Empty;
}
