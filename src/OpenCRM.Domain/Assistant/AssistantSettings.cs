namespace OpenCRM.Domain.Assistant;

public sealed class AssistantSettings
{
    public int Id { get; set; } = 1;
    public bool IsEnabled { get; set; }
    public bool WebEnabled { get; set; }
    public bool TelegramEnabled { get; set; }
    public bool SpeechRepliesEnabled { get; set; }
    public string TextModel { get; set; } = string.Empty;
    public string SpeechModel { get; set; } = string.Empty;
    public string VoiceModel { get; set; } = string.Empty;
    public string VoiceName { get; set; } = string.Empty;
    public decimal DailyBudgetRub { get; set; }
    public decimal MonthlyBudgetRub { get; set; }
    public decimal TextReservationRub { get; set; }
    public decimal AudioReservationRub { get; set; }
    public int MaximumOutputTokens { get; set; }
    public long Revision { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
