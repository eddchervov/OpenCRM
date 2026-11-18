using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Данные карточки техники. </summary>
public class GetVehicleDto
{
    /// <summary> Идентификатор записи. </summary>
    public required Guid Id { get; set; }

    /// <summary> Тип техники. </summary>
    public required VehicleKind Kind { get; set; }

    /// <summary> Единица учета пробега или наработки. </summary>
    public required VehicleMileageUnit MileageUnit { get; set; }

    /// <summary> Идентификатор бренда из справочника. </summary>
    public required int? BrandId { get; set; }

    /// <summary> Название бренда. </summary>
    public required string BrandName { get; set; }

    /// <summary> Ключ логотипа связанной марки из справочника. </summary>
    public string? BrandLogoKey { get; set; }

    /// <summary> Идентификатор модели из справочника. </summary>
    public required int? ModelId { get; set; }

    /// <summary> Название модели. </summary>
    public required string ModelName { get; set; }

    /// <summary> Подпись для отображения; не используется при сохранении марки и модели. </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary> Год выпуска. </summary>
    public required int? YearIssue { get; set; }

    /// <summary> Идентификатор фотографии. </summary>
    public required Guid? PhotoId { get; set; }

    /// <summary> Признак архива. </summary>
    public required bool IsArchived { get; set; }

    /// <summary> Дата архивации. </summary>
    public DateTime? ArchiveDate { get; set; }

    /// <summary> Оставшиеся дни в архиве. </summary>
    public int? DaysLeft { get; set; }

    /// <summary> Количество обслуживаний. </summary>
    public required int MaintenanceCount { get; set; }

    /// <summary> Дата последнего обслуживания, если оно было. </summary>
    public DateTime? LastMaintenanceDate { get; set; }

    /// <summary> Состояние текущего планового или выполняемого обслуживания. </summary>
    public MaintenanceStatus? CurrentMaintenanceStatus { get; set; }

    /// <summary> Идентификатор текущего обслуживания. </summary>
    public Guid? CurrentMaintenanceId { get; set; }

    /// <summary> Название первой работы текущего обслуживания. </summary>
    public string? CurrentMaintenanceName { get; set; }

    /// <summary> Количество завершённых работ текущего обслуживания. </summary>
    public int CompletedWorkCount { get; set; }

    /// <summary> Общее количество работ текущего обслуживания. </summary>
    public int TotalWorkCount { get; set; }

    /// <summary> Плановая дата текущего обслуживания. </summary>
    public DateTime? CurrentMaintenanceDate { get; set; }

    /// <summary> Пользовательское имя или номер. </summary>
    public required string? Name { get; set; }

    /// <summary> VIN или серийный номер техники. </summary>
    public string? Vin { get; set; }

    /// <summary> Регистрационный номер техники. </summary>
    public string? RegistrationPlate { get; set; }

    /// <summary> Страна регистрации автомобиля. </summary>
    public string? RegistrationCountryCode { get; set; }

    /// <summary> Номер кузова или рамы. </summary>
    public string? BodyNumber { get; set; }

    /// <summary> Номер двигателя. </summary>
    public string? EngineNumber { get; set; }

    /// <summary> Цвет техники. </summary>
    public string? Color { get; set; }

    /// <summary> Номер СТС или иного регистрационного документа. </summary>
    public string? RegistrationCertificate { get; set; }

    /// <summary> Дата ввода техники в эксплуатацию. </summary>
    public DateTime? CommissioningDate { get; set; }

    /// <summary> Описание техники. </summary>
    public required string? Description { get; set; }

    /// <summary> Текущее значение пробега или наработки. </summary>
    public int? CurrentMileage { get; set; }

    /// <summary> Дата последнего обновления счетчика. </summary>
    public DateTime? CurrentMileageUpdatedAt { get; set; }

    /// <summary> Бренд, введенный вручную. </summary>
    public string? ManualBrandName { get; set; }

    /// <summary> Модель, введенная вручную. </summary>
    public string? ManualModelName { get; set; }

    /// <summary> Признак ручного ввода. </summary>
    public bool IsManualEntry { get; set; }

    /// <summary> Дата создания записи. </summary>
    public DateTime DateTimeCreation { get; set; }

    /// <summary> Организация, к которой относится техника. Null означает личную технику. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Клиент организации, к которому относится техника. </summary>
    public Guid? OrganizationClientId { get; set; }

    /// <summary> Имя клиента организации. </summary>
    public string? OrganizationClientName { get; set; }
}
