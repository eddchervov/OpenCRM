namespace OpenCRM.Services.Interfaces.Pdf.Dtos;

/// <summary> Модель PDF документ по сценарию обслуживания. </summary>
public class ScenarioMaintenanceDocumentPdfModel
{
    /// <summary> Код стиля оформления документа. </summary>
    public string DocumentStyleCode { get; set; } = "auto";

    /// <summary> Код сценария обслуживания. </summary>
    public string ScenarioCode { get; set; } = string.Empty;

    /// <summary> Код типа документа. </summary>
    public string DocumentKindCode { get; set; } = string.Empty;

    /// <summary> Название документа. </summary>
    public string DocumentTitle { get; set; } = string.Empty;

    /// <summary> Код валюты денежных сумм документа. </summary>
    public string CurrencyCode { get; set; } = "RUB";

    /// <summary> Номер документа. </summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary> Дата документа. </summary>
    public string DocumentDate { get; set; } = string.Empty;

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

    /// <summary> Общая сумма документа. </summary>
    public decimal GrossAmount { get; set; }

    /// <summary> Сумма скидки. </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary> Сумма оплат. </summary>
    public decimal PaidAmount { get; set; }

    /// <summary> Сумма задолженности. </summary>
    public decimal DebtAmount { get; set; }

    /// <summary> Сумма затрат на материалы. </summary>
    public decimal MaterialCostAmount { get; set; }

    /// <summary> Сумма затрат на работы. </summary>
    public decimal LaborCostAmount { get; set; }

    /// <summary> Сумма прибыли. </summary>
    public decimal ProfitAmount { get; set; }

    /// <summary> Статус оплаты документа. </summary>
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary> Список работ документа. </summary>
    public List<MaintenanceDetail> Works { get; set; } = [];

    /// <summary> Фотографии документа. </summary>
    public List<PhotoInfo> Photos { get; set; } = [];
}
