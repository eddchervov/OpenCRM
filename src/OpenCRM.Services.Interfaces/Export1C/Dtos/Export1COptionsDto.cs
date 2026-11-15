namespace OpenCRM.Services.Interfaces.Export1C.Dtos;

/// <summary> Параметры выгрузки данных в 1С. </summary>
public sealed class Export1COptionsDto
{
    /// <summary> Назначение файла: bp3 для подготовки к 1С или full для универсального обмена. </summary>
    public string DestinationCode { get; set; } = "full";

    /// <summary> Дата начала периода выгрузки. </summary>
    public DateOnly DateFrom { get; set; }

    /// <summary> Дата окончания периода выгрузки. </summary>
    public DateOnly DateTo { get; set; }

    /// <summary> Идентификатор часового пояса выгрузки. </summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary> Признак включения заказов в выгрузку. </summary>
    public bool IncludeOrders { get; set; } = true;

    /// <summary> Признак включения незавершённых заказов в выгрузку. </summary>
    public bool IncludeUnfinishedOrders { get; set; }

    /// <summary> Признак включения платежей в выгрузку. </summary>
    public bool IncludePayments { get; set; } = true;

    /// <summary> Признак включения клиентов в выгрузку. </summary>
    public bool IncludeClients { get; set; } = true;

    /// <summary> Признак включения техники в выгрузку. </summary>
    public bool IncludeVehicles { get; set; } = true;

    /// <summary> Признак включения справочника в выгрузку. </summary>
    public bool IncludeCatalog { get; set; } = true;

    /// <summary> Признак включения материалов в выгрузку. </summary>
    public bool IncludeMaterials { get; set; } = true;
}
