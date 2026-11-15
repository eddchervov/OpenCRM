using OpenCRM.Domain.Identity;
using OpenCRM.Domain;
using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Данные для создания техники. </summary>
public class CreateVehicleDto
{
    /// <summary> Тип техники. </summary>
    public OpenCRM.Domain.Enums.VehicleKind Kind { get; set; } = OpenCRM.Domain.Enums.VehicleKind.PassengerCar;

    /// <summary> Единица учета пробега или наработки. </summary>
    public OpenCRM.Domain.Enums.VehicleMileageUnit MileageUnit { get; set; } = OpenCRM.Domain.Enums.VehicleMileageUnit.Kilometers;

    /// <summary> Идентификатор модели из справочника. </summary>
    public int? ModelId { get; set; }

    /// <summary> Год выпуска. </summary>
    [Range(1900, 9999, ErrorMessage = "Год выпуска должен быть не меньше 1900.")]
    public int? YearIssue { get; set; }

    /// <summary> Бренд, введенный вручную. </summary>
    public string? ManualBrandName { get; set; }

    /// <summary> Модель, введенная вручную. </summary>
    public string? ManualModelName { get; set; }

    /// <summary> Пользовательское имя или номер. </summary>
    public string? Name { get; set; }

    /// <summary> VIN или серийный номер техники. </summary>
    [MaxLength(32, ErrorMessage = "VIN не должен быть длиннее 32 символов.")]
    public string? Vin { get; set; }

    /// <summary> Регистрационный номер техники. </summary>
    [MaxLength(24, ErrorMessage = "Госномер не должен быть длиннее 24 символов.")]
    public string? RegistrationPlate { get; set; }

    /// <summary> Разрешает сохранить безопасный регистрационный номер вне настроенного шаблона. </summary>
    public bool AllowOtherRegistrationPlateFormat { get; set; }

    /// <summary> Номер кузова или рамы. </summary>
    [MaxLength(64, ErrorMessage = "Номер кузова не должен быть длиннее 64 символов.")]
    public string? BodyNumber { get; set; }

    /// <summary> Номер двигателя. </summary>
    [MaxLength(64, ErrorMessage = "Номер двигателя не должен быть длиннее 64 символов.")]
    public string? EngineNumber { get; set; }

    /// <summary> Цвет техники. </summary>
    [MaxLength(32, ErrorMessage = "Цвет не должен быть длиннее 32 символов.")]
    public string? Color { get; set; }

    /// <summary> Номер СТС или иного регистрационного документа. </summary>
    [MaxLength(64, ErrorMessage = "Номер СТС не должен быть длиннее 64 символов.")]
    public string? RegistrationCertificate { get; set; }

    /// <summary> Дата ввода техники в эксплуатацию. </summary>
    public DateTime? CommissioningDate { get; set; }

    /// <summary> Описание техники. </summary>
    public string? Description { get; set; }

    /// <summary> Организация, в которой создается техника. Null означает личный аккаунт. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Клиент организации, к которому относится техника. </summary>
    public Guid? OrganizationClientId { get; set; }
}
