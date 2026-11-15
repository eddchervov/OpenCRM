namespace OpenCRM.WebApp.Infrastructure.Assistant;

public static class AssistantConfiguration
{
    public static bool IsRouterConfigured(IConfiguration configuration) =>
        HasSecret(configuration, "Assistant:RouterApiKey", "Assistant:RouterApiKeyFile") ||
        HasSecret(configuration, "AiPilot:ApiKey", "AiPilot:ApiKeyFile");

    public static bool IsTelegramConfigured(IConfiguration configuration) =>
        new Telegram.AssistantTelegramConfiguration(configuration).IsConfigured;

    private static bool HasSecret(IConfiguration configuration, string valueKey, string fileKey)
    {
        if (!string.IsNullOrWhiteSpace(configuration[valueKey])) return true;
        var path = configuration[fileKey];

        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 0;
    }
}
