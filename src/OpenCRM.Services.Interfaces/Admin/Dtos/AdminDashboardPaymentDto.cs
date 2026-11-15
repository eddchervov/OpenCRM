namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO платежа для дашборда. </summary>
public sealed class AdminDashboardPaymentDto
{
    /// <summary> Идентификатор платежа. </summary>
    public int Id { get; init; }

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; init; }

    /// <summary> Статус платежа. </summary>
    public string? PaymentStatus { get; init; }

    /// <summary> Дата платежа. </summary>
    public DateTime PaymentDate { get; init; }

    /// <summary> Тарифный план. </summary>
    public string Plan { get; init; } = string.Empty;

    /// <summary> Пользователь. </summary>
    public string User { get; init; } = string.Empty;
}

