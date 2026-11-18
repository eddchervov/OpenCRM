namespace OpenCRM.Modules.Maintenance.Contracts.Imports;

/// <summary>Историческая запись обслуживания, подготовленная из внешней CRM.</summary>
public sealed class ImportBusinessRecord
{
    /// <summary>Идентификатор строки черновика.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Заранее назначенный сервером идентификатор целевой строки.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>Тип записи: заказ, строка заказа, оплата или событие истории.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Идентификатор заказа во внешней системе.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Идентификатор автомобиля во внешней системе.</summary>
    public string? VehicleExternalId { get; set; }

    /// <summary>Идентификатор заказа для связанной строки.</summary>
    public string? OrderExternalId { get; set; }

    /// <summary>Идентификатор исходного клиента организации для покупателя или плательщика.</summary>
    public string? ClientExternalId { get; set; }

    /// <summary>Имя покупателя или плательщика в исходной записи.</summary>
    public string? PartyName { get; set; }

    /// <summary>Исходная дата события в однозначном формате.</summary>
    public string? OccurredAt { get; set; }

    /// <summary>Исходный статус заказа.</summary>
    public string? OrderStatus { get; set; }

    /// <summary>Тип обслуживания.</summary>
    public string? OrderType { get; set; }

    /// <summary>Название строки услуги или работы.</summary>
    public string? DetailName { get; set; }

    /// <summary>Описание строки услуги или работы.</summary>
    public string? DetailDescription { get; set; }

    /// <summary>Количество единиц в строке.</summary>
    public string? Quantity { get; set; }

    /// <summary>Цена одной единицы в строке.</summary>
    public string? UnitPrice { get; set; }

    /// <summary>Сумма заказа или отдельной оплаты.</summary>
    public string? Amount { get; set; }

    /// <summary>Скидка заказа.</summary>
    public string? DiscountAmount { get; set; }

    /// <summary>Способ оплаты.</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>Дата отмены оплаты.</summary>
    public string? CancelledAt { get; set; }

    /// <summary>Имя исполнителя или автора события.</summary>
    public string? ActorName { get; set; }

    /// <summary>Тип события истории.</summary>
    public string? EventType { get; set; }

    /// <summary>Комментарий к исходному заказу.</summary>
    public string? OrderComment { get; set; }

    /// <summary>Комментарий к оплате или событию истории.</summary>
    public string? HistoryComment { get; set; }

    /// <summary>Признак исключения строки из импорта.</summary>
    public bool Excluded { get; set; }

    /// <summary>Результат серверной проверки строки.</summary>
    public string Status { get; set; } = "create";

    /// <summary>Сообщение о проверке строки.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Код системного сообщения проверки строки.</summary>
    public string? MessageCode { get; set; }

    /// <summary>Параметры системного сообщения проверки строки.</summary>
    public Dictionary<string, string>? MessageParameters { get; set; }

    /// <summary>Подробные причины проверки строки.</summary>
    public List<ImportRowMessageDetail>? MessageDetails { get; set; }
}
