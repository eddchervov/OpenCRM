namespace OpenCRM.Services.Interfaces.Export1C.Dtos;

/// <summary> Пакет данных выгрузки в 1С. </summary>
public sealed class Export1CPackageDto
{
    /// <summary> Версия схемы выгрузки. </summary>
    public int SchemaVersion { get; set; } = 3;

    /// <summary> Отображаемое название проекта, сформировавшего выгрузку. </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary> Время формирования выгрузки в UTC. </summary>
    public DateTime ExportedAtUtc { get; set; }

    /// <summary> Время снимка справочных данных в UTC. </summary>
    public DateTime ReferenceDataSnapshotAtUtc { get; set; }

    /// <summary> Дата начала периода выгрузки. </summary>
    public DateOnly DateFrom { get; set; }

    /// <summary> Дата окончания периода выгрузки. </summary>
    public DateOnly DateTo { get; set; }

    /// <summary> Идентификатор часового пояса выгрузки. </summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary> Сведения об организации. </summary>
    public OrganizationInfo Organization { get; set; } = new();

    /// <summary> Список незаполненных бухгалтерских полей. </summary>
    public List<string> MissingAccountingFields { get; set; } = [];

    /// <summary> Список проблем валидации данных выгрузки. </summary>
    public List<ValidationIssue> ValidationIssues { get; set; } = [];

    /// <summary> Клиенты в выгрузке. </summary>
    public List<ClientInfo> Clients { get; set; } = [];

    /// <summary> Техника в выгрузке. </summary>
    public List<VehicleInfo> Vehicles { get; set; } = [];

    /// <summary> Позиции справочника в выгрузке. </summary>
    public List<CatalogItemInfo> CatalogItems { get; set; } = [];

    /// <summary> Заказы в выгрузке. </summary>
    public List<OrderInfo> Orders { get; set; } = [];

    /// <summary> Платежи в выгрузке. </summary>
    public List<PaymentInfo> Payments { get; set; } = [];

    /// <summary> Остатки в выгрузке. </summary>
    public List<StockInfo> Stocks { get; set; } = [];

    /// <summary> Движения материалов в выгрузке. </summary>
    public List<MaterialMovementInfo> MaterialMovements { get; set; } = [];

    /// <summary> Проблема валидации данных выгрузки. </summary>
    public sealed class ValidationIssue
    {
        /// <summary> Тип сущности с проблемой. </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary> Идентификатор сущности с проблемой. </summary>
        public Guid EntityId { get; set; }

        /// <summary> Идентификатор заказа, если проблема относится к нему. </summary>
        public Guid? OrderId { get; set; }

        /// <summary> Описание проблемы. </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary> Сведения об организации в выгрузке 1С. </summary>
    public sealed class OrganizationInfo
    {
        /// <summary> Идентификатор организации. </summary>
        public Guid Id { get; set; }

        /// <summary> Название организации. </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary> Юридическое наименование, если указано. </summary>
        public string? LegalName { get; set; }

        /// <summary> ИНН организации, если указан. </summary>
        public string? Inn { get; set; }

        /// <summary> КПП организации, если указан. </summary>
        public string? Kpp { get; set; }

        /// <summary> ОГРН организации, если указан. </summary>
        public string? Ogrn { get; set; }

        /// <summary> Юридический адрес, если указан. </summary>
        public string? LegalAddress { get; set; }
    }

    /// <summary> Сведения о клиенте в выгрузке 1С. </summary>
    public sealed class ClientInfo
    {
        /// <summary> Идентификатор клиента. </summary>
        public Guid Id { get; set; }

        /// <summary> Имя клиента. </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary> Телефон клиента, если указан. </summary>
        public string? Phone { get; set; }

        /// <summary> Код типа клиента, если указан. </summary>
        public string? PartyTypeCode { get; set; }

        /// <summary> ИНН клиента, если указан. </summary>
        public string? Inn { get; set; }

        /// <summary> КПП клиента, если указан. </summary>
        public string? Kpp { get; set; }

        /// <summary> Юридический адрес клиента, если указан. </summary>
        public string? LegalAddress { get; set; }

        /// <summary> Код статуса клиента. </summary>
        public string StatusCode { get; set; } = string.Empty;
    }

    /// <summary> Сведения о технике в выгрузке 1С. </summary>
    public sealed class VehicleInfo
    {
        /// <summary> Идентификатор техники. </summary>
        public Guid Id { get; set; }

        /// <summary> Идентификатор клиента, если техника ему сопоставлена. </summary>
        public Guid? ClientId { get; set; }

        /// <summary> Название техники, если указано. </summary>
        public string? Name { get; set; }

        /// <summary> Марка техники, если указана. </summary>
        public string? Brand { get; set; }

        /// <summary> Модель техники, если указана. </summary>
        public string? Model { get; set; }

        /// <summary> VIN техники, если указан. </summary>
        public string? Vin { get; set; }

        /// <summary> Регистрационный номер, если указан. </summary>
        public string? RegistrationPlate { get; set; }

        /// <summary> Признак нахождения техники в архиве. </summary>
        public bool IsArchived { get; set; }
    }

    /// <summary> Сведения о позиции справочника в выгрузке 1С. </summary>
    public sealed class CatalogItemInfo
    {
        /// <summary> Идентификатор позиции справочника. </summary>
        public Guid Id { get; set; }

        /// <summary> Код сценария позиции. </summary>
        public string ScenarioCode { get; set; } = string.Empty;

        /// <summary> Код вида позиции. </summary>
        public string KindCode { get; set; } = string.Empty;

        /// <summary> Код позиции справочника в выгрузке 1С. </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary> Наименование позиции справочника в выгрузке 1С. </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary> Описание позиции справочника в выгрузке 1С. </summary>
        public string? Description { get; set; }

        /// <summary> Цена за единицу позиции справочника. </summary>
        public decimal? UnitPrice { get; set; }

        /// <summary> Идентификаторы услуг, связанных с позицией справочника. </summary>
        public Guid[] ServiceIds { get; set; } = [];

        /// <summary> Признак активности позиции справочника. </summary>
        public bool IsActive { get; set; }
    }

    /// <summary> Сведения о заказе в выгрузке 1С. </summary>
    public sealed class OrderInfo
    {
        /// <summary> Идентификатор заказа. </summary>
        public Guid Id { get; set; }

        /// <summary> Признак того, что заказ используется только как платёжный контекст. </summary>
        public bool IsPaymentContextOnly { get; set; }

        /// <summary> Идентификатор техники из заказа. </summary>
        public Guid VehicleId { get; set; }

        /// <summary> Идентификатор клиента заказа. </summary>
        public Guid? ClientId { get; set; }

        /// <summary> Наименование клиента заказа. </summary>
        public string? ClientName { get; set; }

        /// <summary> Код типа клиента. </summary>
        public string? ClientPartyTypeCode { get; set; }

        /// <summary> ИНН клиента. </summary>
        public string? ClientInn { get; set; }

        /// <summary> КПП клиента. </summary>
        public string? ClientKpp { get; set; }

        /// <summary> Юридический адрес клиента. </summary>
        public string? ClientLegalAddress { get; set; }

        /// <summary> Наименование техники из заказа. </summary>
        public string? VehicleName { get; set; }

        /// <summary> Регистрационный номерной знак техники. </summary>
        public string? RegistrationPlate { get; set; }

        /// <summary> Номер акта выполненных работ. </summary>
        public int? WorkActNumber { get; set; }

        /// <summary> Признак удаления заказа. </summary>
        public bool IsDeleted { get; set; }

        /// <summary> Статус заказа. </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary> Дата заказа. </summary>
        public DateTime Date { get; set; }

        /// <summary> Дата учёта заказа. </summary>
        public DateOnly AccountingDate { get; set; }

        /// <summary> Плановая дата выполнения заказа. </summary>
        public DateTime? PlannedDate { get; set; }

        /// <summary> Дата фактического выполнения заказа. </summary>
        public DateTime? CompletedDate { get; set; }

        /// <summary> Дата выдачи заказа клиенту. </summary>
        public DateTime? HandedOverAt { get; set; }

        /// <summary> Общая сумма заказа. </summary>
        public decimal GrossAmount { get; set; }

        /// <summary> Сумма скидки по заказу. </summary>
        public decimal DiscountAmount { get; set; }

        /// <summary> Сумма заказа с учётом скидки. </summary>
        public decimal NetAmount { get; set; }

        /// <summary> Оплаченная сумма по заказу. </summary>
        public decimal PaidAmount { get; set; }

        /// <summary> Сумма затрат на материалы по заказу. </summary>
        public decimal MaterialCostAmount { get; set; }

        /// <summary> Сумма затрат на работы по заказу. </summary>
        public decimal LaborCostAmount { get; set; }

        /// <summary> Строки заказа. </summary>
        public List<OrderLineInfo> Lines { get; set; } = [];
    }

    /// <summary> Строка заказа в выгрузке 1С. </summary>
    public sealed class OrderLineInfo
    {
        /// <summary> Идентификатор строки заказа. </summary>
        public Guid Id { get; set; }

        /// <summary> Идентификатор позиции справочника из строки заказа. </summary>
        public Guid? CatalogItemId { get; set; }

        /// <summary> Наименование строки заказа. </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary> Описание строки заказа. </summary>
        public string? Description { get; set; }

        /// <summary> Количество по строке заказа. </summary>
        public int Quantity { get; set; }

        /// <summary> Сумма строки без учёта скидки. </summary>
        public decimal? TotalPrice { get; set; }

        /// <summary> Сумма строки с учётом скидки. </summary>
        public decimal DiscountedTotalPrice { get; set; }

        /// <summary> Код ставки НДС строки заказа. </summary>
        public string? VatRateCode { get; set; }

        /// <summary> Сумма НДС строки заказа. </summary>
        public decimal? VatAmount { get; set; }

        /// <summary> Идентификатор позиции справочника материала. </summary>
        public Guid? MaterialCatalogItemId { get; set; }

        /// <summary> Наименование материала. </summary>
        public string? MaterialName { get; set; }

        /// <summary> Количество материала. </summary>
        public decimal? MaterialQuantity { get; set; }

        /// <summary> Единица измерения материала. </summary>
        public string? MaterialUnit { get; set; }

        /// <summary> Стоимость единицы материала. </summary>
        public decimal? MaterialUnitCost { get; set; }

        /// <summary> Общая стоимость материала по строке. </summary>
        public decimal? MaterialTotalCost { get; set; }
    }

    /// <summary> Сведения о платеже в выгрузке 1С. </summary>
    public sealed class PaymentInfo
    {
        /// <summary> Идентификатор платежа. </summary>
        public Guid Id { get; set; }

        /// <summary> Идентификатор заказа, к которому относится платёж. </summary>
        public Guid OrderId { get; set; }

        /// <summary> Дата учёта платежа. </summary>
        public DateOnly AccountingDate { get; set; }

        /// <summary> Сумма платежа. </summary>
        public decimal Amount { get; set; }

        /// <summary> Код способа оплаты. </summary>
        public string? MethodCode { get; set; }

        /// <summary> Идентификатор клиента-плательщика. </summary>
        public Guid? PayerClientId { get; set; }

        /// <summary> Наименование плательщика. </summary>
        public string? PayerName { get; set; }

        /// <summary> Код типа плательщика. </summary>
        public string? PayerPartyTypeCode { get; set; }

        /// <summary> ИНН плательщика. </summary>
        public string? PayerInn { get; set; }

        /// <summary> КПП плательщика. </summary>
        public string? PayerKpp { get; set; }

        /// <summary> Юридический адрес плательщика. </summary>
        public string? PayerLegalAddress { get; set; }

        /// <summary> Дата и время регистрации платежа в UTC. </summary>
        public DateTime RecordedAtUtc { get; set; }

        /// <summary> Автор создания платежа. </summary>
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary> Признак начального остатка. </summary>
        public bool IsOpeningBalance { get; set; }

        /// <summary> Дата и время отмены платежа в UTC. </summary>
        public DateTime? CancelledAtUtc { get; set; }

        /// <summary> Автор отмены платежа. </summary>
        public string? CancelledBy { get; set; }

        /// <summary> Причина отмены платежа. </summary>
        public string? CancellationReason { get; set; }

        /// <summary> Комментарий к платежу. </summary>
        public string? Comment { get; set; }
    }

    /// <summary> Сведения об остатках на складе в выгрузке 1С. </summary>
    public sealed class StockInfo
    {
        /// <summary> Идентификатор складской записи. </summary>
        public Guid Id { get; set; }

        /// <summary> Идентификатор позиции справочника. </summary>
        public Guid? CatalogItemId { get; set; }

        /// <summary> Наименование позиции на складе. </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary> Единица измерения позиции. </summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary> Количество в упаковке. </summary>
        public decimal? PackageQuantity { get; set; }

        /// <summary> Наличный остаток позиции. </summary>
        public decimal QuantityOnHand { get; set; }

        /// <summary> Зарезервированное количество позиции. </summary>
        public decimal ReservedQuantity { get; set; }

        /// <summary> Средняя стоимость единицы позиции. </summary>
        public decimal AverageUnitCost { get; set; }

        /// <summary> Стоимость запаса позиции. </summary>
        public decimal InventoryValue { get; set; }
    }

    /// <summary> Движение материала в выгрузке 1С. </summary>
    public sealed class MaterialMovementInfo
    {
        /// <summary> Идентификатор движения материала. </summary>
        public Guid Id { get; set; }

        /// <summary> Идентификатор складской записи. </summary>
        public Guid StockId { get; set; }

        /// <summary> Идентификатор связанного заказа. </summary>
        public Guid? OrderId { get; set; }

        /// <summary> Идентификатор связанной операции. </summary>
        public Guid? OperationId { get; set; }

        /// <summary> Код типа движения материала. </summary>
        public string TypeCode { get; set; } = string.Empty;

        /// <summary> Дата и время регистрации движения в UTC. </summary>
        public DateTime RecordedAtUtc { get; set; }

        /// <summary> Количество материала в движении. </summary>
        public decimal Quantity { get; set; }

        /// <summary> Введённое пользователем количество. </summary>
        public decimal? EnteredQuantity { get; set; }

        /// <summary> Введённая пользователем единица измерения. </summary>
        public string? EnteredUnit { get; set; }

        /// <summary> Коэффициент пересчёта единицы измерения. </summary>
        public decimal? ConversionFactor { get; set; }

        /// <summary> Единица измерения движения. </summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary> Стоимость единицы материала. </summary>
        public decimal UnitCost { get; set; }

        /// <summary> Общая стоимость движения. </summary>
        public decimal TotalCost { get; set; }

        /// <summary> Остаток количества после движения. </summary>
        public decimal QuantityBalanceAfter { get; set; }

        /// <summary> Остаток стоимости после движения. </summary>
        public decimal CostBalanceAfter { get; set; }

        /// <summary> Тип исходной сущности движения. </summary>
        public string? SourceEntityType { get; set; }

        /// <summary> Идентификатор исходной сущности движения. </summary>
        public Guid? SourceEntityId { get; set; }

        /// <summary> Комментарий к движению материала. </summary>
        public string? Comment { get; set; }
    }
}
