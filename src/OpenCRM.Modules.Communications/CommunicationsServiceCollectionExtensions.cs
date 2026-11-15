using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Communications.Contracts;

namespace OpenCRM.Modules.Communications;

/// <summary> Регистрирует сервисы переписок организации. </summary>
public static class CommunicationsServiceCollectionExtensions
{
    public static IServiceCollection AddOpenCRMCommunications(this IServiceCollection services)
    {
        services.AddScoped<ConversationAccess>();
        services.AddScoped<ConversationRouteAvailability>();
        services.AddScoped<OrganizationConversationDeliveryService>();
        services.AddScoped<OrganizationConversationService>();
        services.AddScoped<OrganizationChannelService>();
        services.AddScoped<OrganizationMessageTemplateService>();
        services.AddScoped<IConversationAttachmentService, Files.ConversationAttachmentService>();
        services.AddHostedService<Files.ConversationAttachmentCleanupWorker>();
        services.AddScoped<IOrganizationConversationService>(provider => provider.GetRequiredService<OrganizationConversationService>());
        services.AddScoped<IOrganizationChannelService>(provider => provider.GetRequiredService<OrganizationChannelService>());
        services.AddScoped<IOrganizationMessageTemplateService>(provider => provider.GetRequiredService<OrganizationMessageTemplateService>());
        services.AddScoped<IConversationDeliveryOutbox>(provider => provider.GetRequiredService<OrganizationConversationDeliveryService>());
        services.AddScoped<IConversationDispatchOutbox>(provider => provider.GetRequiredService<OrganizationConversationDeliveryService>());
        services.AddScoped<IConversationBotIdentityRegistry>(provider => provider.GetRequiredService<OrganizationConversationService>());

        return services;
    }
}
