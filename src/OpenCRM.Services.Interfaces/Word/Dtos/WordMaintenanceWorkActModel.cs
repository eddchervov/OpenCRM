namespace OpenCRM.Services.Interfaces.Word.Dtos;

/// <summary> Модель Word-документа акта выполненных работ. </summary>
public class WordMaintenanceWorkActModel
{
    /// <summary> Код стиля оформления документа. </summary>
    public string DocumentStyleCode { get; set; } = "auto";

    /// <summary> Код сценария обслуживания. </summary>
    public string ScenarioCode { get; set; } = string.Empty;

    /// <summary> Код валюты денежных сумм документа. </summary>
    public string CurrencyCode { get; set; } = "RUB";

    /// <summary> Номер акта. </summary>
    public string ActNumber { get; set; } = string.Empty;

    /// <summary> Дата акта. </summary>
    public string ActDate { get; set; } = string.Empty;

    /// <summary> Дата формирования документа. </summary>
    public string GeneratedDate { get; set; } = string.Empty;

    /// <summary> Название исполнителя. </summary>
    public string ExecutorName { get; set; } = string.Empty;

    /// <summary> ИНН исполнителя, если есть. </summary>
    public string? ExecutorInn { get; set; }

    /// <summary> КПП исполнителя, если есть. </summary>
    public string? ExecutorKpp { get; set; }

    /// <summary> ОГРН исполнителя, если есть. </summary>
    public string? ExecutorOgrn { get; set; }

    /// <summary> Адрес исполнителя, если есть. </summary>
    public string? ExecutorAddress { get; set; }

    /// <summary> Телефон исполнителя, если есть. </summary>
    public string? ExecutorPhone { get; set; }

    /// <summary> Email исполнителя, если есть. </summary>
    public string? ExecutorEmail { get; set; }

    /// <summary> Должность подписанта исполнителя. </summary>
    public string ExecutorSignatoryPosition { get; set; } = string.Empty;

    /// <summary> ФИО подписанта исполнителя. </summary>
    public string ExecutorSignatoryName { get; set; } = string.Empty;

    /// <summary> Основание полномочий подписанта. </summary>
    public string ExecutorSignatoryBasis { get; set; } = string.Empty;

    /// <summary> Название заказчика. </summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary> Наименование транспортного средства. </summary>
    public string VehicleTitle { get; set; } = string.Empty;

    /// <summary> Регистрационный знак автомобиля, если есть. </summary>
    public string? VehicleRegistrationPlate { get; set; }

    /// <summary> VIN автомобиля, если есть. </summary>
    public string? VehicleVin { get; set; }

    /// <summary> Год выпуска автомобиля, если есть. </summary>
    public string? VehicleYear { get; set; }

    /// <summary> Дата обслуживания. </summary>
    public string MaintenanceDate { get; set; } = string.Empty;

    /// <summary>Плановый период записи, отдельно от даты выполнения документа.</summary>
    public string? PlannedPeriod { get; set; }

    /// <summary> Тип обслуживания. </summary>
    public string MaintenanceType { get; set; } = string.Empty;

    /// <summary> Место обслуживания, если есть. </summary>
    public string? Location { get; set; }

    /// <summary> Исполнитель, если есть. </summary>
    public string? Performer { get; set; }

    /// <summary> Пробег, если известен. </summary>
    public int? Mileage { get; set; }

    /// <summary> Подпись пробега для документа. </summary>
    public string MileageLabel { get; set; } = string.Empty;

    /// <summary> Единица измерения пробега. </summary>
    public string MileageUnit { get; set; } = string.Empty;

    /// <summary> Комментарий к обслуживанию, если есть. </summary>
    public string? Comment { get; set; }

    /// <summary> Скидка на обслуживание. </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary> Список работ акта. </summary>
    public List<WordMaintenanceDetail> Works { get; set; } = [];
}
