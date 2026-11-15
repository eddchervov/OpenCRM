using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant.Actions;
using OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

public static class AssistantRegistration
{
    public static void AddServerAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        var options = AssistantOptions.Load(configuration);
        services.AddSingleton(options);
        services.AddAuthorization(authorization => authorization.AddPolicy("AssistantWebSession", policy =>
            policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser()));
        services.AddScoped<AssistantProviderContext>();
        services.AddScoped<AssistantUploadLimitFilter>();
        services.AddScoped<AssistantAccessService>();
        services.AddScoped<AssistantDashboardService>();
        services.AddScoped<IAssistantExecutionGuard>(provider => provider.GetRequiredService<AssistantAccessService>());
        services.AddScoped<AssistantUsageService>();
        services.AddScoped<AssistantSettingsStore>();
        services.AddScoped<AssistantService>();
        services.AddScoped<AiPilotConversationService>();
        services.AddScoped<IAssistantActionHandler, CoreAssistantActionHandler>();
        services.AddScoped<IAssistantActionHandler, OperationsAssistantCatalogEmployeeShiftActionHandler>();
        services.AddScoped<IAssistantActionHandler, OperationsAssistantReportScheduleActionHandler>();
        services.AddScoped<IAssistantActionHandler, OperationsAssistantVehicleRecordActionHandler>();
        services.AddScoped<IAssistantActionHandler, OperationsAssistantWorkActionHandler>();
        services.AddScoped<IAssistantActionHandler, OperationsAssistantInventoryActionHandler>();
        services.AddScoped<IAssistantActionExecutor, AssistantActionExecutor>();
        services.AddHttpClient<RouterAiPilotClient>(client => client.Timeout = TimeSpan.FromSeconds(options.ProviderTimeoutSeconds))
            .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient("assistant-telegram", client => client.Timeout =
                TimeSpan.FromSeconds(configuration.GetValue<int>("AssistantTelegram:HttpTimeoutSeconds")))
            .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<AssistantTelegramApiClient>();
        services.AddScoped<IAssistantTelegramApiClient>(provider => provider.GetRequiredService<AssistantTelegramApiClient>());
        services.AddScoped<AssistantTelegramService>();
        services.AddHostedService<AssistantTelegramUpdateProcessor>();
        services.AddHostedService<AssistantTelegramPollingService>();
        services.AddHostedService<AssistantCleanupService>();
        services.AddRateLimiter(limiter =>
        {
            foreach (var policy in new[] { "assistant", "assistant-dashboard" })
            {
                limiter.AddPolicy(policy, context =>
                {
                    var identity = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>();
                    var userId = context.User.FindFirst(identity.Value.ClaimsIdentity.UserIdClaimType)?.Value ?? "anonymous";

                    return RateLimitPartition.GetConcurrencyLimiter(userId, _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = configuration.GetValue<int>("Assistant:ConcurrentRequestsPerUser"), QueueLimit = 0
                    });
                });
            }
        });
    }
}
