namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> DTO с данными локальной записи о платеже. </summary>
public class LocalPaymentInfoDto
{
    /// <summary> Идентификатор пользователя платежа. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Идентификатор тарифного плана платежа. </summary>
    public int PlanId { get; set; }

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; set; }

    /// <summary> Статус платежа. </summary>
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary> Способ платежа. </summary>
    public string PaymentMethod { get; set; } = string.Empty;
}

