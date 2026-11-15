using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Services.Interfaces.Auth;

namespace OpenCRM.Infrastructure.Auth;

/// <summary> Методы регистрации инфраструктуры аутентификации. </summary>
public static class AuthInfrastructureServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы инфраструктуры аутентификации в контейнере. </summary>
    public static IServiceCollection AddOpenCRMAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtTokenOptions>(configuration.GetSection("Authentication:Jwt"));
        services.AddScoped<IAuthTokenService, AuthTokenService>();
        return services;
    }
}
