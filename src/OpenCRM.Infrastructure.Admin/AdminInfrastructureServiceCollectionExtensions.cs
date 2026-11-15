using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Services.Interfaces.Admin;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Методы регистрации сервисов админ-модуля. </summary>
public static class AdminInfrastructureServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы админ-модуля в контейнере. </summary>
    public static IServiceCollection AddOpenCRMAdminModule(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permissionCode in AdminAuthorizationPolicies.All)
            {
                options.AddPolicy(
                    permissionCode,
                    policy => policy
                        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, "Bearer")
                        .RequireAuthenticatedUser()
                        .AddRequirements(new AdminPermissionRequirement(permissionCode)));
            }

            options.AddPolicy(
                AdminAuthorizationPolicies.ManagePermissions,
                policy => policy
                    .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, "Bearer")
                    .RequireAuthenticatedUser()
                    .AddRequirements(new AdminPermissionRequirement(AdminAuthorizationPolicies.ManagePermissions)));
        });

        services.AddScoped<IAuthorizationHandler, AdminPermissionAuthorizationHandler>();
        services.AddScoped<IAdminCurrentUserService, AdminCurrentUserService>();
        services.AddScoped<IAdminCatalogSuggestionService, AdminCatalogSuggestionService>();
        services.AddSingleton<AdminDashboardCache>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IAdminNavigationService, AdminNavigationService>();
        services.AddScoped<IAdminUsersService, AdminUsersService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAdminOrganizationService, AdminOrganizationService>();
        services.AddScoped<IAdminPermissionService, AdminPermissionService>();
        services.AddScoped<IAdminRoleService, AdminRoleService>();
        services.AddScoped<IAdminTestDataService, AdminTestDataService>();
        services.AddScoped<IAdminVehicleCatalogSeedService, AdminVehicleCatalogSeedService>();
        services.AddScoped<IAdminVehicleCatalogService, AdminVehicleCatalogService>();

        return services;
    }
}
