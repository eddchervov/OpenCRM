using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Organizations.Bookings;
using OpenCRM.Modules.Organizations.Archive;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Export;
using OpenCRM.Modules.Organizations.Invitations;
using OpenCRM.Modules.Organizations.Membership;
using OpenCRM.Modules.Organizations.Profile;
using OpenCRM.Modules.Organizations.Queries;
using OpenCRM.Modules.Organizations.Setup;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations;

/// <summary> Методы регистрации сервисов модуля организаций. </summary>
public static class OrganizationsServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы модуля организаций. </summary>
    public static IServiceCollection AddOpenCRMOrganizations(this IServiceCollection services)
    {
        services.AddScoped<OrganizationTransaction>();
        services.AddScoped<OrganizationAccess>();
        services.AddScoped<OrganizationAuditWriter>();
        services.AddScoped<IOrganizationAuditService, OrganizationAuditService>();
        services.AddScoped<IOrganizationPermissionService, OrganizationPermissionService>();
        services.AddScoped<IOrganizationEmployeeService, OrganizationEmployeeService>();
        services.AddScoped<IOrganizationShiftService, OrganizationShiftService>();
        services.AddScoped<IOrganizationTimeZoneService, OrganizationTimeZoneService>();
        services.AddScoped<IOrganizationCurrencyService, OrganizationCurrencyService>();
        services.AddScoped<IUserCurrencyService, UserCurrencyService>();
        services.AddScoped<IOrganizationDetailingQuickBookingService, OrganizationDetailingQuickBookingService>();
        services.AddScoped<IOrganizationAutoServiceQuickBookingService, OrganizationAutoServiceQuickBookingService>();
        services.AddScoped<IOrganizationInquiryBookingService, OrganizationInquiryBookingService>();
        services.AddScoped<IOrganizationRepeatVisitBookingService, OrganizationRepeatVisitBookingService>();
        services.AddScoped<IOrganizationTypeCatalog, OrganizationTypeCatalog>();
        services.AddScoped<OrganizationScenarioReader>();
        services.AddScoped<OrganizationWorkingSchedule>();
        services.AddScoped<OrganizationInvitationNotifications>();
        services.AddScoped<OrganizationInvitationEmail>();
        services.AddScoped<IOrganizationQueryService, OrganizationQueryService>();
        services.AddScoped<IOrganizationProfileCommandService, OrganizationProfileCommandService>();
        services.AddScoped<IOrganizationArchiveService, OrganizationArchiveService>();
        services.TryAddSingleton<OrganizationDataExportOptions>();
        services.AddScoped<IOrganizationDataExportService, OrganizationDataExportService>();
        services.AddScoped<IOrganizationMembershipService, OrganizationMembershipService>();
        services.AddScoped<IOrganizationInvitationQueryService, OrganizationInvitationQueryService>();
        services.AddScoped<IOrganizationInvitationCommandService, OrganizationInvitationCommandService>();
        services.AddScoped<IOrganizationSetupService, OrganizationSetupService>();

        return services;
    }
}
