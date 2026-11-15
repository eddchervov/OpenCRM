using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Accounts.Users;
using OpenCRM.Services.Interfaces.Users;

namespace OpenCRM.Modules.Accounts;

/// <summary> Методы расширения регистрации модуля аккаунтов. </summary>
public static class AccountsServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы модуля аккаунтов в контейнере. </summary>
    public static IServiceCollection AddOpenCRMAccounts(this IServiceCollection services)
    {
        services.AddTransient<IUsersService, UsersService>();
        services.AddScoped<IAccountRegistrationService, AccountRegistrationService>();
        services.AddScoped<IAccountDeletionService, AccountDeletionService>();
        services.AddScoped<IEmailCodeLoginService, EmailCodeLoginService>();

        return services;
    }
}
