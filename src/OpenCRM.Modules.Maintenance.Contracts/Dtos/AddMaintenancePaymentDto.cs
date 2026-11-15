namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для добавления платежа по обслуживанию. </summary>
public sealed class AddMaintenancePaymentDto
{
    /// <summary> Идентификатор клиента-плательщика, если указан. </summary>
    public Guid? PayerClientId { get; set; }

    /// <summary> Идентификатор платежа. </summary>
    public Guid Id { get; set; }

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; set; }

    /// <summary> Код способа оплаты. </summary>
    public string? MethodCode { get; set; }

    /// <summary> Ожидаемая оплаченная сумма. </summary>
    public decimal ExpectedPaidAmount { get; set; }

    /// <summary> Комментарий к платежу. </summary>
    public string? Comment { get; set; }
}
