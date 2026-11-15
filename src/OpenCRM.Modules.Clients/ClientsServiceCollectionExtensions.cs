using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Clients.Commands;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Queries;
using OpenCRM.Modules.Clients.Inquiries;
using OpenCRM.Modules.Clients.RepeatVisits;

namespace OpenCRM.Modules.Clients;

/// <summary> Расширение регистрации сервисов модуля клиентов. </summary>
public static class ClientsServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы модуля клиентов в контейнере. </summary>
    public static IServiceCollection AddOpenCRMClients(this IServiceCollection services)
    {
        services.AddScoped<ClientsAccess>();
        services.AddScoped<ClientReadData>();
        services.AddScoped<ClientVehicleReader>();
        services.AddScoped<ClientVehiclePhotos>();
        services.AddScoped<ClientHistoryReader>();
        services.AddScoped<OrganizationClientPageReader>();
        services.AddScoped<IOrganizationClientQueryService, OrganizationClientQueryService>();
        services.AddScoped<IOrganizationClientCountQueryService, OrganizationClientCountQueryService>();
        services.AddScoped<IOrganizationClientDetailsQueryService, OrganizationClientDetailsQueryService>();
        services.AddScoped<IOrganizationClientMaintenancePageQueryService, ClientMaintenancePageQueryService>();
        services.AddScoped<IOrganizationClientCommandService, OrganizationClientCommandService>();
        services.AddScoped<IOrganizationClientVehicleLinkService, OrganizationClientVehicleLinkService>();
        services.AddScoped<IQuickBookingClientLookupService, QuickBookingClientLookupService>();
        services.AddScoped<OrganizationClientHistoryWriter>();
        services.AddScoped<OrganizationClientTagWriter>();
        services.AddScoped<OrganizationClientResponsibleResolver>();
        services.AddScoped<OrganizationClientTransaction>();
        services.AddScoped<InquiryAccess>();
        services.AddScoped<OrganizationInquiryService>();
        services.AddScoped<IOrganizationInquiryService>(provider => provider.GetRequiredService<OrganizationInquiryService>());
        services.AddScoped<IOrganizationInquiryBookingLinkService>(provider => provider.GetRequiredService<OrganizationInquiryService>());
        services.AddScoped<IOrganizationInquiryOriginReader, OrganizationInquiryOriginReader>();
        services.AddScoped<RepeatVisitAccess>();
        services.AddScoped<OrganizationRepeatVisitService>();
        services.AddScoped<IOrganizationRepeatVisitService>(provider => provider.GetRequiredService<OrganizationRepeatVisitService>());
        services.AddScoped<IOrganizationRepeatVisitBookingLinkService>(provider => provider.GetRequiredService<OrganizationRepeatVisitService>());

        return services;
    }
}
