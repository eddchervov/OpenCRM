namespace OpenCRM.Admin.Api.Models;

/// <summary> Разрешённые для изменения настройки помощника. </summary>
public sealed class AdminAssistantSettingsRequest
{
    public bool IsEnabled { get; init; }
    public bool WebEnabled { get; init; }
    public bool TelegramEnabled { get; init; }
    public bool SpeechRepliesEnabled { get; init; }
    public string TextModel { get; init; } = string.Empty;
    public string SpeechModel { get; init; } = string.Empty;
    public string VoiceModel { get; init; } = string.Empty;
    public string VoiceName { get; init; } = string.Empty;
    public decimal DailyBudgetRub { get; init; }
    public decimal MonthlyBudgetRub { get; init; }
    public decimal TextReservationRub { get; init; }
    public decimal AudioReservationRub { get; init; }
    public int MaximumOutputTokens { get; init; }
    public long Revision { get; init; }
}
