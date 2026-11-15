namespace OpenCRM.Services.Interfaces.Imports;

/// <summary>Подготовленная к проверке строка импорта.</summary>
public class ImportCandidate
{
    /// <summary> Время применения строки, запрещающее дальнейшее редактирование. </summary>
    public DateTime? AppliedAt { get; set; }

    /// <summary> Созданная или связанная серверная запись. </summary>
    public Guid? TargetId { get; set; }

    /// <summary>Идентификатор строки черновика.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Идентификатор исходной таблицы.</summary>
    public string TableId { get; set; } = string.Empty;

    /// <summary>Тип импортируемой сущности.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Имя клиента или автомобиля.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Номер исходной строки.</summary>
    public int SourceRow { get; set; }

    /// <summary>Признак исключения строки из импорта.</summary>
    public bool Excluded { get; set; }

    /// <summary>Внешний идентификатор записи в исходной CRM.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Внешний идентификатор клиента исходной CRM.</summary>
    public string? ClientExternalId { get; set; }

    /// <summary> Исходное имя покупателя или плательщика на момент исторической операции. </summary>
    public string? PartyName { get; set; }

    /// <summary>Телефон клиента, связанного с автомобилем.</summary>
    public string? ClientPhone { get; set; }

    /// <summary>Телефон клиента.</summary>
    public string? Phone { get; set; }

    /// <summary>Комментарий к записи.</summary>
    public string? Comment { get; set; }

    /// <summary>Марка автомобиля.</summary>
    public string? Brand { get; set; }

    /// <summary>Модель автомобиля.</summary>
    public string? Model { get; set; }

    /// <summary>VIN автомобиля.</summary>
    public string? Vin { get; set; }

    /// <summary>Государственный регистрационный номер автомобиля.</summary>
    public string? Plate { get; set; }

    /// <summary>Год выпуска автомобиля.</summary>
    public string? Year { get; set; }

    /// <summary>Внешний идентификатор автомобиля для связанных исторических записей.</summary>
    public string? VehicleExternalId { get; set; }

    /// <summary>Внешний идентификатор заказа для его строк, оплат и событий.</summary>
    public string? OrderExternalId { get; set; }

    /// <summary>Дата исходного события в однозначном формате.</summary>
    public string? OccurredAt { get; set; }

    /// <summary>Статус исходного заказа.</summary>
    public string? OrderStatus { get; set; }

    /// <summary>Тип обслуживания.</summary>
    public string? OrderType { get; set; }

    /// <summary>Количество единиц в строке заказа.</summary>
    public string? Quantity { get; set; }

    /// <summary>Цена единицы строки заказа.</summary>
    public string? UnitPrice { get; set; }

    /// <summary>Сумма заказа или отдельной оплаты.</summary>
    public string? Amount { get; set; }

    /// <summary>Скидка заказа.</summary>
    public string? DiscountAmount { get; set; }

    /// <summary>Способ оплаты.</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>Дата отмены оплаты.</summary>
    public string? CancelledAt { get; set; }

    /// <summary>Имя исполнителя или автора исходного события.</summary>
    public string? ActorName { get; set; }

    /// <summary>Тип импортируемого события истории.</summary>
    public string? EventType { get; set; }

    /// <summary>Комментарий к исходному заказу.</summary>
    public string? OrderComment { get; set; }

    /// <summary>Описание строки заказа.</summary>
    public string? DetailDescription { get; set; }

    /// <summary>Комментарий к заказу, оплате или событию.</summary>
    public string? HistoryComment { get; set; }

    /// <summary>Идентификатор найденного клиента OpenCRM.</summary>
    public Guid? ExistingClientId { get; set; }

    /// <summary> Явное подтверждение создания отдельного клиента при совпадениях. </summary>
    public bool CreateSeparateClient { get; set; }

    /// <summary>Идентификатор клиента, разрешённого серверной проверкой связи.</summary>
    public Guid? ResolvedClientId { get; set; }

    /// <summary>Идентификатор строки клиента в исходном черновике.</summary>
    public string? ResolvedClientRowId { get; set; }

    /// <summary>Планируемое действие с записью.</summary>
    public string Status { get; set; } = "create";

    /// <summary>Сообщение о результате проверки строки.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Возможные совпадения с существующими клиентами.</summary>
    public List<ImportClientMatch> Matches { get; set; } = [];

    /// <summary>Имя выбранного связанного клиента.</summary>
    public string? LinkedClientName { get; set; }
}
