namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Безопасная запись платёжного журнала для диагностики администратором. </summary>
public sealed class AdminBillingPaymentEventDto
{
    /// <summary> Идентификатор записи журнала. </summary>
    public Guid Id { get; init; }

    /// <summary> Идентификатор сообщения платёжного сервиса. </summary>
    public Guid? MessageId { get; init; }

    /// <summary> Идентификатор покупки. </summary>
    public Guid? PurchaseId { get; init; }

    /// <summary> Идентификатор команды платёжного сервиса. </summary>
    public Guid? CommandId { get; init; }

    /// <summary> Идентификатор заказа провайдера. </summary>
    public Guid? ProviderOrderId { get; init; }

    /// <summary> Версия состояния заказа у провайдера. </summary>
    public long? ProviderVersion { get; init; }

    /// <summary> Этап обработки. </summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary> Безопасный код события или ошибки. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Время записи события в UTC. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Время обработки события в UTC. </summary>
    public DateTime? ProcessedAt { get; init; }

    /// <summary> Сумма заказа в минимальных денежных единицах. </summary>
    public long? AmountMinor { get; init; }

    /// <summary> Возвращённая сумма в минимальных денежных единицах. </summary>
    public long? RefundedAmountMinor { get; init; }

    /// <summary> Код валюты заказа. </summary>
    public string Currency { get; init; } = string.Empty;
}
