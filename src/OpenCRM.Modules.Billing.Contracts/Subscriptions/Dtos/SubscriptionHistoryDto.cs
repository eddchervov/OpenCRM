namespace OpenCRM.Services.Interfaces.Subscriptions.Dtos;

/// <summary> Запись истории подписки. </summary>
public class SubscriptionHistoryDto
{
    /// <summary> Идентификатор записи истории. </summary>
    public int Id { get; set; }

    /// <summary> Тарифный план записи истории. </summary>
    public SubscriptionPlanDto SubscriptionPlan { get; set; } = null!;

    /// <summary> Дата начала периода. </summary>
    public DateTime StartDate { get; set; }

    /// <summary> Дата окончания периода. </summary>
    public DateTime EndDate { get; set; }

    /// <summary> Фактическая дата окончания подписки. </summary>
    public DateTime? ActualEndDate { get; set; }

    /// <summary> Дата и время создания записи. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Комментарий к записи истории. </summary>
    public string? Comment { get; set; }

    /// <summary> Статус записи истории. </summary>
    public string Status { get; set; } = string.Empty;

    // Цена из тарифного плана
    /// <summary> Сумма оплаты за период. </summary>
    public decimal Amount { get; set; }

    // Вычисляемые свойства
    /// <summary> Текстовое описание периода. </summary>
    public string PeriodText => $"{StartDate:dd.MM.yyyy} - {EndDate:dd.MM.yyyy}";

    /// <summary> Признак того, что подписка была активной. </summary>
    public bool WasActive => ActualEndDate == null || ActualEndDate >= EndDate;
}
