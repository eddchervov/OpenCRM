namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed class AssistantOptions
{
    public bool Enabled { get; set; }
    public bool WebEnabled { get; set; }
    public bool TelegramEnabled { get; set; }
    public bool SpeechRepliesEnabled { get; set; }
    public string ProviderBaseUrl { get; set; } = string.Empty;
    public string[] AllowedProviderHosts { get; set; } = [];
    public string TextModel { get; set; } = string.Empty;
    public string SpeechModel { get; set; } = string.Empty;
    public string SpeechRecognitionPrompt { get; set; } = string.Empty;
    public string VoiceModel { get; set; } = string.Empty;
    public string VoiceName { get; set; } = string.Empty;
    public decimal DailyBudgetRub { get; set; }
    public decimal MonthlyBudgetRub { get; set; }
    public decimal TextReservationRub { get; set; }
    public decimal AudioReservationRub { get; set; }
    public int MaximumOutputTokens { get; set; }
    public int MaximumOutputTokensAllowed { get; set; }
    public int ProviderTimeoutSeconds { get; set; }
    public int ConversationTtlMinutes { get; set; }
    public int DraftTtlMinutes { get; set; }
    public int ProcessingLeaseSeconds { get; set; }
    public int MaximumUserTurns { get; set; }
    public int MaximumHistoryCharacters { get; set; }
    public int MaximumHistoryMessages { get; set; }
    public int MaximumMessageCharacters { get; set; }
    public int MaximumCommandCharacters { get; set; }
    public int MaximumResponseBytes { get; set; }
    public int MaximumAudioBytes { get; set; }
    public int MaximumUploadRequestBytes { get; set; }
    public int MaximumAudioDurationSeconds { get; set; }
    public int MaximumWorkLines { get; set; }
    public int MaximumClarificationChoices { get; set; }
    public int MaximumCatalogCandidates { get; set; }
    public int LookupPageSize { get; set; }
    public int LookupLimit { get; set; }
    public int MaintenanceHistoryLimit { get; set; }
    public int AvailabilityPollingSeconds { get; set; }
    public int StaleReservationMinutes { get; set; }
    public int HistoryRetentionDays { get; set; }
    public int ConversationHistoryRetentionDays { get; set; }
    public int MaximumUsagePeriodDays { get; set; }
    public int CleanupIntervalSeconds { get; set; }
    public string BudgetTimeZone { get; set; } = string.Empty;
    public string DefaultTimeZone { get; set; } = string.Empty;
    public string[] AllowedScenarioCodes { get; set; } = [];
    public decimal MaximumBudgetRub { get; set; }
    public decimal MaximumReservationRub { get; set; }

    public static AssistantOptions Load(IConfiguration configuration)
    {
        var options = configuration.GetSection("Assistant").Get<AssistantOptions>() ?? new AssistantOptions();

        // Выключенный помощник не требует настройки провайдера: механизмы остаются в поставке,
        // а обращения к внешнему провайдеру не выполняются.
        if (!options.Enabled)
        {
            return options;
        }
        if (!Uri.TryCreate(options.ProviderBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !options.AllowedProviderHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !options.ProviderBaseUrl.EndsWith('/') ||
            options.ProviderTimeoutSeconds <= 0 || options.MaximumOutputTokens <= 0 || options.MaximumMessageCharacters <= 0 ||
            options.MaximumHistoryCharacters < options.MaximumMessageCharacters || options.MaximumResponseBytes <= 0 ||
            options.MaximumAudioBytes <= 0 || options.MaximumUploadRequestBytes < options.MaximumAudioBytes ||
            options.ConversationTtlMinutes <= 0 || options.DraftTtlMinutes <= 0 || options.ProcessingLeaseSeconds <= options.ProviderTimeoutSeconds ||
            options.MaximumOutputTokensAllowed < options.MaximumOutputTokens || options.MaximumMessageCharacters is <= 100 or > 1000 ||
            options.MaximumHistoryMessages <= 0 || options.MaximumUserTurns <= 0 || options.MaximumWorkLines is <= 0 or > 20 ||
            options.MaximumClarificationChoices <= 0 || options.MaximumCatalogCandidates <= 0 || options.LookupPageSize <= 0 ||
            options.LookupLimit < options.LookupPageSize || options.MaintenanceHistoryLimit <= 0 || options.AvailabilityPollingSeconds <= 0 ||
            options.StaleReservationMinutes * 60 <= options.ProviderTimeoutSeconds || options.HistoryRetentionDays <= 0 ||
            options.ConversationHistoryRetentionDays <= 0 || options.MaximumUsagePeriodDays <= 0 || options.CleanupIntervalSeconds <= 0 ||
            options.MaximumAudioDurationSeconds <= 0 || options.MaximumBudgetRub <= 0 || options.MaximumReservationRub <= 0 ||
            configuration.GetValue<int>("Assistant:ConcurrentRequestsPerUser") <= 0 ||
            new[] { options.TextModel, options.SpeechModel, options.VoiceModel, options.VoiceName }.Any(string.IsNullOrWhiteSpace) ||
            options.AllowedScenarioCodes.Length == 0)
        {
            throw new InvalidOperationException("Проверьте адрес провайдера, таймауты и ограничения в конфигурации Assistant.");
        }

        TimeZoneInfo.FindSystemTimeZoneById(options.BudgetTimeZone);
        TimeZoneInfo.FindSystemTimeZoneById(options.DefaultTimeZone);

        return options;
    }
}
