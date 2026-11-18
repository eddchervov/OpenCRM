namespace OpenCRM.Services.Interfaces.Word.Dtos;

/// <summary> Модель данных для формирования документа по обслуживанию из сценария. </summary>
public class WordScenarioMaintenanceDocumentModel
{
    /// <summary> Код стиля оформления документа. </summary>
    public string DocumentStyleCode { get; set; } = "auto";

    /// <summary> Код сценария, по которому формируется документ. </summary>
    public string ScenarioCode { get; set; } = string.Empty;

    /// <summary> Код вида документа. </summary>
    public string DocumentKindCode { get; set; } = string.Empty;

    /// <summary> Заголовок документа. </summary>
    public string DocumentTitle { get; set; } = string.Empty;

    /// <summary> Код валюты денежных сумм документа. </summary>
    public string CurrencyCode { get; set; } = "RUB";

    /// <summary> Номер документа. </summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary> Дата документа в текстовом виде. </summary>
    public string DocumentDate { get; set; } = string.Empty;

    /// <summary> Дата формирования документа в текстовом виде. </summary>
    public string GeneratedDate { get; set; } = string.Empty;

    /// <summary> Наименование исполнителя работ. </summary>
    public string ExecutorName { get; set; } = string.Empty;

    /// <summary> ИНН исполнителя. </summary>
    public string? ExecutorInn { get; set; }

    /// <summary> КПП исполнителя. </summary>
    public string? ExecutorKpp { get; set; }

    /// <summary> ОГРН исполнителя. </summary>
    public string? ExecutorOgrn { get; set; }

    /// <summary> Адрес исполнителя. </summary>
    public string? ExecutorAddress { get; set; }

    /// <summary> Телефон исполнителя. </summary>
    public string? ExecutorPhone { get; set; }

    /// <summary> Электронная почта исполнителя. </summary>
    public string? ExecutorEmail { get; set; }

    /// <summary> Должность подписанта со стороны исполнителя. </summary>
    public string ExecutorSignatoryPosition { get; set; } = string.Empty;

    /// <summary> Имя подписанта со стороны исполнителя. </summary>
    public string ExecutorSignatoryName { get; set; } = string.Empty;

    /// <summary> Наименование заказчика. </summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary> Название транспортного средства. </summary>
    public string VehicleTitle { get; set; } = string.Empty;

    /// <summary> Регистрационный знак транспортного средства. </summary>
    public string? VehicleRegistrationPlate { get; set; }

    /// <summary> VIN транспортного средства. </summary>
    public string? VehicleVin { get; set; }

    /// <summary> Год выпуска транспортного средства. </summary>
    public string? VehicleYear { get; set; }

    /// <summary> Дата обслуживания. </summary>
    public string MaintenanceDate { get; set; } = string.Empty;

    /// <summary>Плановый период записи, отдельно от даты выполнения документа.</summary>
    public string? PlannedPeriod { get; set; }

    /// <summary> Вид обслуживания. </summary>
    public string MaintenanceType { get; set; } = string.Empty;

    /// <summary> Место проведения обслуживания. </summary>
    public string? Location { get; set; }

    /// <summary> Исполнитель работ по обслуживанию. </summary>
    public string? Performer { get; set; }

    /// <summary> Пробег на момент обслуживания. </summary>
    public int? Mileage { get; set; }

    /// <summary> Подпись для показателя пробега. </summary>
    public string MileageLabel { get; set; } = string.Empty;

    /// <summary> Наименование единицы измерения пробега. </summary>
    public string MileageUnit { get; set; } = string.Empty;

    /// <summary> Комментарий к обслуживанию. </summary>
    public string? Comment { get; set; }

    /// <summary> Общая сумма по обслуживанию. </summary>
    public decimal GrossAmount { get; set; }

    /// <summary> Сумма скидки. </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary> Оплаченная сумма. </summary>
    public decimal PaidAmount { get; set; }

    /// <summary> Сумма задолженности. </summary>
    public decimal DebtAmount { get; set; }

    /// <summary> Стоимость материалов. </summary>
    public decimal MaterialCostAmount { get; set; }

    /// <summary> Стоимость работ. </summary>
    public decimal LaborCostAmount { get; set; }

    /// <summary> Сумма прибыли. </summary>
    public decimal ProfitAmount { get; set; }

    /// <summary> Статус оплаты в текстовом виде. </summary>
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary> Список работ или услуг по обслуживанию. </summary>
    public List<WordMaintenanceDetail> Works { get; set; } = [];

    /// <summary> Список фотографий для документа. </summary>
    public List<WordPhotoInfo> Photos { get; set; } = [];
}
