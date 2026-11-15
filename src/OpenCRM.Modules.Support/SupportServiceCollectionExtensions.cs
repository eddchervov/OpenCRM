using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Home;
using OpenCRM.Modules.Support.Admin;
using OpenCRM.Modules.Support.Personal;

namespace OpenCRM.Modules.Support;

/// <summary> Регистрация сервисов модуля поддержки. </summary>
public static class SupportServiceCollectionExtensions
{
    /// <summary> Добавляет сервисы модуля поддержки в контейнер. </summary>
    public static IServiceCollection AddOpenCRMSupport(this IServiceCollection services)
    {
        services.AddScoped<SupportAttachmentService>();
        services.AddScoped<ISupportAttachmentService>(provider => provider.GetRequiredService<SupportAttachmentService>());
        services.AddScoped<ISupportMessageService, SupportMessageService>();
        services.AddScoped<IAdminSupportMessageService, AdminSupportMessageService>();

        return services;
    }
}
