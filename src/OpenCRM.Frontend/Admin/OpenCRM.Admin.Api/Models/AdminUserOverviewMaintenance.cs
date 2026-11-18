namespace OpenCRM.Admin.Api.Models;

/// <summary> Запись об обслуживании в обзоре пользователя. </summary>
public sealed class AdminUserOverviewMaintenance
{
    /// <summary> Идентификатор обслуживания. </summary>
    public Guid Id { get; init; }

    /// <summary> Дата обслуживания. </summary>
    public DateTime Date { get; init; }

    /// <summary> Время создания записи. </summary>
    public DateTime DateTimeCreation { get; init; }

    /// <summary> Код статуса обслуживания. </summary>
    public int Status { get; init; }

    /// <summary> Название статуса обслуживания. </summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary> Код типа обслуживания. </summary>
    public int Type { get; init; }

    /// <summary> Название типа обслуживания. </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary> Название транспортного средства. </summary>
    public string VehicleTitle { get; init; } = string.Empty;

    /// <summary> Идентификатор транспортного средства. </summary>
    public Guid VehicleId { get; init; }

    /// <summary> Место обслуживания. </summary>
    public string? Location { get; init; }

    /// <summary> Исполнитель обслуживания. </summary>
    public string? Performer { get; init; }

    /// <summary> Оплаченная сумма. </summary>
    public decimal PaidAmount { get; init; }

    /// <summary> Код валюты сумм записи обслуживания. </summary>
    public string CurrencyCode { get; init; } = "RUB";

    /// <summary> Стоимость материалов. </summary>
    public decimal MaterialCostAmount { get; init; }

    /// <summary> Стоимость работ. </summary>
    public decimal LaborCostAmount { get; init; }

    /// <summary> Номер акта работ. </summary>
    public int? WorkActNumber { get; init; }

    /// <summary> Комментарий к обслуживанию. </summary>
    public string? Comment { get; init; }
}
