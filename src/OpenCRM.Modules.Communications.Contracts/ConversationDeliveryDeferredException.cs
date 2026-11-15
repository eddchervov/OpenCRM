namespace OpenCRM.Modules.Communications.Contracts;

/// <summary> Указывает, что более ранняя доставка временно блокирует отправку сообщения. </summary>
public sealed class ConversationDeliveryDeferredException : Exception
{
    public ConversationDeliveryDeferredException(string reasonCode, TimeSpan retryAfter)
        : base("Отправка отложена до завершения более ранней доставки.")
    {
        this.ReasonCode = reasonCode;
        this.RetryAfter = retryAfter < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : retryAfter;
    }

    public string ReasonCode { get; }
    public TimeSpan RetryAfter { get; }
}
