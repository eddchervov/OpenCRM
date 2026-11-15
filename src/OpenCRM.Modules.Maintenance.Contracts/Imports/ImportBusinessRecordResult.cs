namespace OpenCRM.Modules.Maintenance.Contracts.Imports;

/// <summary>Итог пакетного применения исторических записей обслуживания.</summary>
public sealed class ImportBusinessRecordResult
{
    /// <summary>Количество созданных заказов.</summary>
    public int CreatedOrders { get; set; }

    /// <summary>Количество созданных строк заказов.</summary>
    public int CreatedOrderLines { get; set; }

    /// <summary>Количество созданных оплат.</summary>
    public int CreatedPayments { get; set; }

    /// <summary>Количество созданных событий истории.</summary>
    public int CreatedHistoryEvents { get; set; }

    /// <summary>Идентификаторы первых созданных заказов.</summary>
    public List<Guid> OrderIds { get; set; } = [];

    /// <summary>Идентификаторы первых созданных оплат.</summary>
    public List<Guid> PaymentIds { get; set; } = [];

    /// <summary>Назначенные целевые идентификаторы строк текущего пакета.</summary>
    public Dictionary<string, Guid> RowTargets { get; set; } = [];
}
