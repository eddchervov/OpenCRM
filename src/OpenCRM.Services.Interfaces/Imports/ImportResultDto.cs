namespace OpenCRM.Services.Interfaces.Imports;

/// <summary>Итоговые сведения о выполненном импорте.</summary>
public class ImportResultDto
{
    /// <summary>Количество созданных клиентов.</summary>
    public int CreatedClients { get; set; }

    /// <summary>Количество созданных автомобилей.</summary>
    public int CreatedVehicles { get; set; }

    /// <summary>Количество автомобилей, связанных с клиентами.</summary>
    public int LinkedClients { get; set; }

    /// <summary>Количество пропущенных строк.</summary>
    public int Skipped { get; set; }

    /// <summary>Количество созданных заказов обслуживания.</summary>
    public int CreatedOrders { get; set; }

    /// <summary>Количество созданных строк заказов.</summary>
    public int CreatedOrderLines { get; set; }

    /// <summary>Количество импортированных оплат.</summary>
    public int CreatedPayments { get; set; }

    /// <summary>Количество импортированных событий истории.</summary>
    public int CreatedHistoryEvents { get; set; }

    /// <summary>Идентификаторы созданных клиентов.</summary>
    public List<Guid> ClientIds { get; set; } = [];

    /// <summary>Идентификаторы созданных автомобилей.</summary>
    public List<Guid> VehicleIds { get; set; } = [];

    /// <summary>Идентификаторы первых созданных заказов.</summary>
    public List<Guid> OrderIds { get; set; } = [];

    /// <summary>Идентификаторы первых импортированных оплат.</summary>
    public List<Guid> PaymentIds { get; set; } = [];
}
