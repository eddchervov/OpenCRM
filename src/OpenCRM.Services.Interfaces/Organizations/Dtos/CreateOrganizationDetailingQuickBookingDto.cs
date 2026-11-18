using System.ComponentModel.DataAnnotations;

using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Данные для быстрого создания записи детейлинга в организации. </summary>
public class CreateOrganizationDetailingQuickBookingDto
{
    /// <summary> Идентификатор явно выбранного автомобиля. </summary>
    public Guid? VehicleId { get; set; }

    /// <summary> Регистрационный номерной знак техники. </summary>
    [Required]
    [MaxLength(24)]
    public string RegistrationPlate { get; set; } = string.Empty;

    /// <summary> Страна регистрации автомобиля по ISO 3166-1 alpha-2. </summary>
    [MaxLength(2)]
    public string? RegistrationCountryCode { get; set; }

    /// <summary> Явно разрешает иностранный или другой нестандартный формат номера. </summary>
    public bool AllowOtherRegistrationPlateFormat { get; set; }

    /// <summary> Текстовое описание техники без выбора из справочника. </summary>
    [MaxLength(120)]
    public string? VehicleText { get; set; }

    /// <summary> Вид техники. </summary>
    public VehicleKind Kind { get; set; } = VehicleKind.PassengerCar;

    /// <summary> Идентификатор модели техники. </summary>
    public int? ModelId { get; set; }

    /// <summary> Название марки, введённое вручную. </summary>
    [MaxLength(120)]
    public string? ManualBrandName { get; set; }

    /// <summary> Название модели, введённое вручную. </summary>
    [MaxLength(120)]
    public string? ManualModelName { get; set; }

    /// <summary> Идентификатор клиента организации. </summary>
    public Guid? OrganizationClientId { get; set; }

    /// <summary> Имя клиента. </summary>
    [MaxLength(120)]
    public string? ClientName { get; set; }

    /// <summary> Телефон клиента. </summary>
    [MaxLength(32)]
    public string? ClientPhone { get; set; }

    /// <summary>Идентификатор первой выбранной услуги справочника организации.</summary>
    public Guid? ServiceId { get; set; }

    /// <summary> Список услуг для записи. </summary>
    public List<OpenCRM.Modules.Maintenance.Contracts.Dtos.CreateMaintenanceDetailDto>? Services { get; set; }

    /// <summary> Плановая дата записи. </summary>
    public DateTime PlannedDate { get; set; }

    /// <summary> Определяет, что запись создаётся на будущее и должна остаться запланированной. По умолчанию true. </summary>
    public bool IsPlannedBooking { get; set; } = true;

    /// <summary> Плановое время начала. </summary>
    public TimeSpan? PlannedStartTime { get; set; }

    /// <summary> Плановое время окончания. </summary>
    public TimeSpan? PlannedEndTime { get; set; }

    /// <summary>Плановая дата окончания записи.</summary>
    public DateTime? PlannedEndDate { get; set; }

    /// <summary> Стоимость записи. </summary>
    [Range(0, 999999999)]
    public decimal? Price { get; set; }

    /// <summary> Место проведения. </summary>
    [MaxLength(200)]
    public string? Location { get; set; }

    /// <summary> Комментарий к записи. </summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }
}
